using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCampus.Core
{
    /// <summary>
    /// One piece of furniture the player may move, as the bundle's "arrangeable" section writes it (derive_world.py). At is where
    /// the layout puts its anchor by default. Cells are the cells it blocks, relative to the anchor; a rug has none. Bounds is the box
    /// of every cell it draws, (x0, y0, width, height) relative to the anchor. A Stackable piece (a plant, a book) may stand on the
    /// Surface cells of a host instead of on the floor; Surface lies inside Cells, and a piece with a Surface is not Stackable. Lift
    /// is how many pixels (32 to a cell) a piece standing on this host is drawn higher.
    /// </summary>
    public sealed class PieceDef
    {
        public string Id, Name;
        public int[] At = new int[2];
        public List<int[]> Cells = new List<int[]>();
        public int[] Bounds = new int[4];
        public bool Stackable;
        public List<int[]> Surface = new List<int[]>();
        public int Lift;
    }

    /// <summary>
    /// What a placed piece is drawn and occupied as. A rug (no footprint) is Floor, under everything. A Stackable piece whose every
    /// footprint cell lies on the Surface of one placed host is Top. Everything else is Body. Two pieces of one layer never share a cell.
    /// </summary>
    public enum Layer { Floor, Body, Top }

    /// <summary>
    /// A room whose furniture the player may arrange. Area is (x0, y0, x1, y1), inclusive: the cells a piece's footprint may cover
    /// (a rug, which blocks nothing, must lie wholly inside it). Keep lists the cells that must stay free and walkable together:
    /// doors, the bed, destinations.
    /// </summary>
    public sealed class ArrangeableDef
    {
        public string MapId;
        public int[] Area = new int[4];
        public List<int[]> Keep = new List<int[]>();
        public List<PieceDef> Pieces = new List<PieceDef>();
    }

    /// <summary>Where one piece stands: the cell of its anchor.</summary>
    public sealed class PiecePlacement
    {
        public string Piece;
        public int X, Y;
    }

    /// <summary>The player's arrangement of one room as saved. Every piece of the room is in exactly one of the two lists.</summary>
    public sealed class RoomArrangement
    {
        public List<PiecePlacement> Placed = new List<PiecePlacement>();
        /// <summary>Pieces put away; they take no room and block nothing.</summary>
        public List<string> Stored = new List<string>();
    }

    /// <summary>Piece: two bodies overlap. Layer: two rugs overlap. SurfaceTaken: something already stands on that spot of the host. Loaded: the host has something standing on it.</summary>
    public enum PlaceProblem { None, Busy, UnknownPiece, OffMap, OutsideArea, Wall, Keep, Piece, Layer, SurfaceTaken, Loaded, Actor, Cutoff }

    /// <summary>
    /// Furniture the player arranges (Game owns one, like Inventory). The arrangement lives in GameState.Arrangements and is saved
    /// when the day ends; a room with no entry stands as its layout drew it. Collision has one rule: the cells the current
    /// arrangement blocks are written to MapDef.Furnished, which World.Passable reads for the player and for NPC paths alike.
    /// Rule is the one placement rule, pure so the tests and the game ask the same question.
    /// </summary>
    public sealed class Furniture
    {
        readonly Game game;

        public Furniture(Game game)
        {
            this.game = game;
            Normalize();
            foreach (var room in Content.Arrangeable) Apply(room.MapId);
        }

        Content Content => game.Content;
        GameState S => game.State;

        /// <summary>Counts every change, so a view can tell when to redraw.</summary>
        public int Revision { get; private set; }

        public bool IsArrangeable(string mapId) => Room(mapId) != null;
        public ArrangeableDef Room(string mapId) => Content.Arrangeable.FirstOrDefault(r => r.MapId == mapId);
        public PieceDef Piece(string mapId, string pieceId) => Room(mapId)?.Pieces.FirstOrDefault(p => p.Id == pieceId);

        /// <summary>Pieces standing in the room now, in the order the room lists them.</summary>
        public IReadOnlyList<PiecePlacement> Placed(string mapId) =>
            S.Arrangements.TryGetValue(mapId, out var entry) ? entry.Placed : Defaults(Room(mapId));

        /// <summary>Pieces put away, in the order the room lists them.</summary>
        public IReadOnlyList<string> Stored(string mapId) =>
            S.Arrangements.TryGetValue(mapId, out var entry) ? (IReadOnlyList<string>)entry.Stored : new string[0];

        public bool IsStored(string mapId, string pieceId) => Stored(mapId).Contains(pieceId);

        public PiecePlacement PlacementOf(string mapId, string pieceId) => Placed(mapId).FirstOrDefault(p => p.Piece == pieceId);

        public static List<PiecePlacement> Defaults(ArrangeableDef room) =>
            room == null ? new List<PiecePlacement>() : room.Pieces.Select(p => new PiecePlacement { Piece = p.Id, X = p.At[0], Y = p.At[1] }).ToList();

        /// <summary>The cells a piece blocks with its anchor at (x, y).</summary>
        public static IEnumerable<(int X, int Y)> Footprint(PieceDef piece, int x, int y) => piece.Cells.Select(c => (x + c[0], y + c[1]));

        /// <summary>Every cell a piece draws on, with its anchor at (x, y).</summary>
        public static IEnumerable<(int X, int Y)> Covered(PieceDef piece, int x, int y)
        {
            var b = piece.Bounds;
            for (var i = 0; i < b[2]; i++)
                for (var j = 0; j < b[3]; j++) yield return (x + b[0] + i, y + b[1] + j);
        }

        /// <summary>The cells the given arrangement blocks.</summary>
        public static HashSet<(int X, int Y)> Blocks(ArrangeableDef room, IEnumerable<PiecePlacement> placed)
        {
            var cells = new HashSet<(int X, int Y)>();
            foreach (var p in placed)
            {
                var piece = room.Pieces.FirstOrDefault(d => d.Id == p.Piece);
                if (piece != null) cells.UnionWith(Footprint(piece, p.X, p.Y));
            }
            return cells;
        }

        /// <summary>Writes the default arrangement of every arrangeable room into its map. Content.Parse calls it so a bundle read without a game is not bare.</summary>
        public static void ApplyDefaults(Content content)
        {
            foreach (var room in content.Arrangeable)
            {
                var map = content.Maps.FirstOrDefault(m => m.Id == room.MapId);
                if (map != null) map.Furnished = Blocks(room, Defaults(room));
            }
        }

        // ------------------------------------------------------------------ the placement rule

        /// <summary>
        /// The layer of every placed piece, and for a Top piece the id of the host it stands on. This is the one place layers are
        /// decided: the rule, the loader, the renderer and the minimap all ask it. Placement order does not matter; a stackable
        /// piece resting on a host is Top whatever order the list is in.
        /// </summary>
        public static Dictionary<string, (Layer Layer, string Host)> Stand(ArrangeableDef room, IEnumerable<PiecePlacement> placed)
        {
            var list = placed.Select(p => (At: p, Def: room.Pieces.FirstOrDefault(d => d.Id == p.Piece))).Where(t => t.Def != null).ToList();
            var surfaces = list.Where(t => !t.Def.Stackable && t.Def.Surface.Count > 0)
                .Select(t => (Id: t.At.Piece, Cells: new HashSet<(int X, int Y)>(t.Def.Surface.Select(c => (t.At.X + c[0], t.At.Y + c[1])))))
                .ToList();
            var result = new Dictionary<string, (Layer Layer, string Host)>();
            foreach (var (at, def) in list)
            {
                if (def.Cells.Count == 0)
                {
                    result[at.Piece] = (Layer.Floor, null);
                    continue;
                }
                var foot = Footprint(def, at.X, at.Y).ToList();
                var host = def.Stackable ? surfaces.FirstOrDefault(s => s.Id != at.Piece && foot.All(s.Cells.Contains)).Id : null;
                result[at.Piece] = host != null ? (Layer.Top, host) : (Layer.Body, null);
            }
            return result;
        }

        /// <summary>
        /// Whether piece may stand with its anchor at (x, y) among the others. Occupancy is per cell and layer: two rugs may not
        /// share a cell, nor two bodies, nor two things on one cell of a surface; a rug lies under a body and a body under a top
        /// piece. Every cell it draws is on the map and its footprint inside the area. A rug needs no more. A body's footprint must
        /// be clear of walls (the map's fixed collision), doors and kept cells and of actors, and the kept cells and the actors
        /// stay walkable together. A piece resting on a host's surface leaves all that to the host, which already blocks those cells.
        /// </summary>
        public static PlaceProblem Rule(Content content, ArrangeableDef room, IEnumerable<PiecePlacement> others, PieceDef piece, int x, int y, IEnumerable<(int X, int Y)> actors)
        {
            var map = content.Map(room.MapId);
            var b = piece.Bounds;
            if (x + b[0] < 0 || y + b[1] < 0 || x + b[0] + b[2] > map.Width || y + b[1] + b[3] > map.Height) return PlaceProblem.OffMap;
            var foot = Footprint(piece, x, y).ToList();
            var a = room.Area;
            if ((foot.Count > 0 ? foot : Covered(piece, x, y).ToList()).Any(c => c.X < a[0] || c.X > a[2] || c.Y < a[1] || c.Y > a[3])) return PlaceProblem.OutsideArea;
            var rest = others.Where(p => p.Piece != piece.Id).ToList();
            var stance = Stand(room, rest.Concat(new[] { new PiecePlacement { Piece = piece.Id, X = x, Y = y } }));
            var layer = stance[piece.Id].Layer;
            var ofLayer = rest.Where(p => stance.TryGetValue(p.Piece, out var s) && s.Layer == layer).ToList();
            if (layer == Layer.Floor)
            {
                var taken = new HashSet<(int X, int Y)>(ofLayer.SelectMany(p => Covered(room.Pieces.First(d => d.Id == p.Piece), p.X, p.Y)));
                return Covered(piece, x, y).Any(taken.Contains) ? PlaceProblem.Layer : PlaceProblem.None;
            }
            if (layer == Layer.Top)
            {
                var taken = new HashSet<(int X, int Y)>(ofLayer.SelectMany(p => Footprint(room.Pieces.First(d => d.Id == p.Piece), p.X, p.Y)));
                return foot.Any(taken.Contains) ? PlaceProblem.SurfaceTaken : PlaceProblem.None;
            }
            var fixedWalls = new HashSet<(int X, int Y)>(map.Blocked.Select(c => (c[0], c[1])));
            if (foot.Any(fixedWalls.Contains)) return PlaceProblem.Wall;
            var keep = room.Keep.Select(c => (X: c[0], Y: c[1])).ToList();
            if (foot.Any(c => keep.Contains(c) || World.PortalAt(map, c.X, c.Y) != null)) return PlaceProblem.Keep;
            var theirs = Blocks(room, ofLayer);
            if (foot.Any(theirs.Contains)) return PlaceProblem.Piece;
            var actorCells = actors.ToList();
            if (foot.Any(actorCells.Contains)) return PlaceProblem.Actor;
            fixedWalls.UnionWith(theirs);
            fixedWalls.UnionWith(foot);
            return Connected(map, fixedWalls, keep.Concat(actorCells)) ? PlaceProblem.None : PlaceProblem.Cutoff;
        }

        /// <summary>Every cell of must can be walked to from the first one. A door cell is reached but not walked through (stepping in warps).</summary>
        static bool Connected(MapDef map, HashSet<(int X, int Y)> blocked, IEnumerable<(int X, int Y)> must)
        {
            var cells = must.ToList();
            if (cells.Count == 0) return true;
            var reached = new HashSet<(int X, int Y)> { cells[0] };
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue(cells[0]);
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (X: cx + dx, Y: cy + dy);
                    if (n.X < 0 || n.Y < 0 || n.X >= map.Width || n.Y >= map.Height || blocked.Contains(n) || !reached.Add(n)) continue;
                    if (World.PortalAt(map, n.X, n.Y) == null) queue.Enqueue(n);
                }
            }
            return cells.All(reached.Contains);
        }

        IEnumerable<(int X, int Y)> ActorCells(string mapId)
        {
            var actors = new List<Actor> { S.Player };
            if (game.Npcs != null) actors.AddRange(game.Npcs);
            return actors.Where(a => a.MapId == mapId).Select(a => ((int)Math.Floor(a.X), (int)Math.Floor(a.Y))).ToList();
        }

        /// <summary>Where a piece stands now and, on a surface, which host holds it; null for a stored or unknown piece.</summary>
        public (Layer Layer, string Host)? Stance(string mapId, string pieceId) =>
            Room(mapId) is ArrangeableDef room && Stand(room, Placed(mapId)).TryGetValue(pieceId, out var s) ? s : ((Layer, string)?)null;

        /// <summary>How many pixels the piece would be drawn raised with its anchor at (x, y): the Lift of the host it would stand on, else 0.</summary>
        public int LiftAt(string mapId, string pieceId, int x, int y)
        {
            var room = Room(mapId);
            if (room == null) return 0;
            var stance = Stand(room, Placed(mapId).Where(p => p.Piece != pieceId).Concat(new[] { new PiecePlacement { Piece = pieceId, X = x, Y = y } }));
            return stance.TryGetValue(pieceId, out var s) && s.Layer == Layer.Top ? room.Pieces.First(d => d.Id == s.Host).Lift : 0;
        }

        /// <summary>Whether something stands on the piece. A loaded host cannot be lifted, moved or stored until its load is taken off.</summary>
        public bool IsLoaded(string mapId, string pieceId) =>
            Room(mapId) is ArrangeableDef room && Stand(room, Placed(mapId)).Any(s => s.Value.Host == pieceId);

        /// <summary>Asks the rule about the current arrangement: could the piece (placed or stored) stand with its anchor at (x, y) now?</summary>
        public PlaceProblem Check(string mapId, string pieceId, int x, int y)
        {
            var room = Room(mapId);
            var piece = room?.Pieces.FirstOrDefault(p => p.Id == pieceId);
            if (piece == null) return PlaceProblem.UnknownPiece;
            if (game.Event != null) return PlaceProblem.Busy;
            if (IsLoaded(mapId, pieceId)) return PlaceProblem.Loaded;
            return Rule(Content, room, Placed(mapId).Where(p => p.Piece != pieceId), piece, x, y, ActorCells(mapId));
        }

        // ------------------------------------------------------------------ changes

        /// <summary>Moves a standing piece, or takes a stored one out, to anchor (x, y). False and nothing changed when the rule refuses.</summary>
        public bool Place(string mapId, string pieceId, int x, int y)
        {
            if (Check(mapId, pieceId, x, y) != PlaceProblem.None) return false;
            var entry = Entry(mapId);
            entry.Stored.Remove(pieceId);
            var at = entry.Placed.FirstOrDefault(p => p.Piece == pieceId);
            if (at == null) entry.Placed.Add(at = new PiecePlacement { Piece = pieceId });
            at.X = x;
            at.Y = y;
            Changed(mapId);
            return true;
        }

        /// <summary>Puts a standing piece away. Removing a piece only opens cells, so only an event, an unknown piece or a piece with something standing on it refuses.</summary>
        public bool Store(string mapId, string pieceId)
        {
            if (game.Event != null || Room(mapId) == null || IsLoaded(mapId, pieceId)) return false;
            var at = PlacementOf(mapId, pieceId);
            if (at == null) return false;
            var entry = Entry(mapId);
            entry.Placed.RemoveAll(p => p.Piece == pieceId);
            entry.Stored.Add(pieceId);
            Changed(mapId);
            return true;
        }

        RoomArrangement Entry(string mapId)
        {
            if (!S.Arrangements.TryGetValue(mapId, out var entry))
                S.Arrangements[mapId] = entry = new RoomArrangement { Placed = Defaults(Room(mapId)) };
            return entry;
        }

        void Changed(string mapId)
        {
            var order = Room(mapId).Pieces.Select(p => p.Id).ToList();
            var entry = S.Arrangements[mapId];
            entry.Placed = entry.Placed.OrderBy(p => order.IndexOf(p.Piece)).ToList();
            entry.Stored = entry.Stored.OrderBy(order.IndexOf).ToList();
            Apply(mapId);
            Repath(mapId);
            Revision++;
        }

        void Apply(string mapId)
        {
            var room = Room(mapId);
            var map = Content.Maps.FirstOrDefault(m => m.Id == mapId);
            if (map != null) map.Furnished = Blocks(room, Placed(mapId));
        }

        /// <summary>An NPC already walking through a cell that just became furniture finds a new way to the same goal.</summary>
        void Repath(string mapId)
        {
            var map = Content.Map(mapId);
            foreach (var npc in game.Npcs)
            {
                if (npc.Path.Count == 0 || !npc.Path.Any(s => s.MapId == mapId && !World.Passable(map, (int)Math.Floor(s.X), (int)Math.Floor(s.Y)))) continue;
                npc.Path = World.FindPath(Content, npc, npc.Path[npc.Path.Count - 1]) ?? npc.Path;
            }
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// A save can lack the field, name a piece the room no longer has, repeat a piece or hold a spot the rule refuses (a
        /// hand edit, an older layout). Unknown and repeated pieces are dropped. A piece in an invalid spot goes back to its default
        /// spot, or into storage when that is taken too. Pieces the save does not mention stand where the layout puts them.
        /// Rooms the content no longer defines are kept as they are, like unknown spells.
        /// </summary>
        void Normalize()
        {
            S.Arrangements = S.Arrangements ?? new Dictionary<string, RoomArrangement>();
            foreach (var room in Content.Arrangeable)
            {
                if (!S.Arrangements.TryGetValue(room.MapId, out var entry)) continue;
                if (entry == null)
                {
                    S.Arrangements.Remove(room.MapId);
                    continue;
                }
                var known = room.Pieces.Select(p => p.Id).ToList();
                var asked = (entry.Placed ?? new List<PiecePlacement>()).Where(p => p != null && known.Contains(p.Piece)).GroupBy(p => p.Piece).Select(g => g.First()).ToList();
                var keptAway = (entry.Stored ?? new List<string>()).Where(known.Contains).Distinct().Where(id => asked.All(a => a.Piece != id)).ToList();
                var placed = new List<PiecePlacement>();
                var away = new List<string>(keptAway);
                var actors = ActorCells(room.MapId).ToList();
                bool Fits(PieceDef piece, int x, int y) => Rule(Content, room, placed, piece, x, y, actors) == PlaceProblem.None;
                void Settle(PieceDef piece, int x, int y)
                {
                    if (Fits(piece, x, y)) placed.Add(new PiecePlacement { Piece = piece.Id, X = x, Y = y });
                    else if (Fits(piece, piece.At[0], piece.At[1])) placed.Add(new PiecePlacement { Piece = piece.Id, X = piece.At[0], Y = piece.At[1] });
                    else away.Add(piece.Id);
                }
                bool Stackable(string id) => room.Pieces.First(p => p.Id == id).Stackable;
                foreach (var at in asked.OrderBy(a => Stackable(a.Piece) ? 1 : 0)) Settle(room.Pieces.First(p => p.Id == at.Piece), at.X, at.Y);
                foreach (var piece in room.Pieces.OrderBy(p => p.Stackable ? 1 : 0).Where(p => placed.All(a => a.Piece != p.Id) && !away.Contains(p.Id))) Settle(piece, piece.At[0], piece.At[1]);
                S.Arrangements[room.MapId] = new RoomArrangement
                {
                    Placed = placed.OrderBy(p => known.IndexOf(p.Piece)).ToList(),
                    Stored = away.OrderBy(known.IndexOf).ToList()
                };
            }
        }
    }
}
