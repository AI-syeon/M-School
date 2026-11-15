using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;

static partial class Program
{
    const string Dorm = "map.dorm";

    /// <summary>The arrangeable rooms a bundle declares: well formed, and every piece's default spot already satisfies the rule the player is held to.</summary>
    static List<string> FurnitureProblems(Content c)
    {
        var p = new List<string>();
        void Rule(bool ok, string what) { if (!ok) p.Add(what); }
        foreach (var room in c.Arrangeable)
        {
            var map = c.Maps.FirstOrDefault(m => m.Id == room.MapId);
            Rule(map != null, room.MapId + " is a map");
            if (map == null) continue;
            Rule(room.Area.Length == 4 && room.Area[0] >= 0 && room.Area[1] >= 0 && room.Area[2] < map.Width && room.Area[3] < map.Height && room.Area[0] <= room.Area[2] && room.Area[1] <= room.Area[3],
                room.MapId + " area lies inside the map");
            Rule(room.Pieces.Count > 0 && room.Pieces.Select(x => x.Id).Distinct().Count() == room.Pieces.Count, room.MapId + " has pieces with distinct ids");
            Rule(room.Keep.All(k => !map.Blocked.Any(b => b[0] == k[0] && b[1] == k[1])), room.MapId + " keeps only cells the fixed map leaves open");
            var defaults = Furniture.Defaults(room);
            foreach (var piece in room.Pieces)
            {
                Rule(!string.IsNullOrEmpty(piece.Name) && piece.At.Length == 2 && piece.Bounds.Length == 4 && piece.Bounds[2] > 0 && piece.Bounds[3] > 0, piece.Id + " has a name, an anchor and bounds");
                Rule(Furniture.Rule(c, room, defaults.Where(d => d.Piece != piece.Id), piece, piece.At[0], piece.At[1], new (int, int)[0]) == PlaceProblem.None,
                    room.MapId + " " + piece.Id + " default spot satisfies the placement rule");
            }
            foreach (var piece in room.Pieces)
            {
                Rule(piece.Surface.All(cell => piece.Cells.Any(f => f[0] == cell[0] && f[1] == cell[1])), piece.Id + " surface lies inside its footprint");
                Rule(!(piece.Stackable && piece.Surface.Count > 0), piece.Id + " is not both stackable and a host");
                Rule(!piece.Stackable || piece.Cells.Count > 0, piece.Id + " is stackable and has a footprint to stand with");
                Rule(piece.Lift >= 0 && (piece.Lift == 0 || piece.Surface.Count > 0), piece.Id + " lifts only as a host");
            }
            Rule(room.Pieces.SelectMany(x => Furniture.Footprint(x, x.At[0], x.At[1])).All(cell => !map.Blocked.Any(b => b[0] == cell.X && b[1] == cell.Y)),
                room.MapId + " movable footprints are not in the fixed collision");
        }
        return p;
    }

    static Game DormGame(string fixture = "start-offered.json")
    {
        var g = NewGame(fixture);
        World.Place(g.Player, new Spot { MapId = Dorm, X = 5.5, Y = 1.5 }, "north");
        return g;
    }

    static Dictionary<string, string> Standing(Game g) => g.Furniture.Placed(Dorm).ToDictionary(p => p.Piece, p => p.X + "," + p.Y);

    static bool Open(Game g, int x, int y) => World.Passable(g.Content.Map(Dorm), x, y);

    static void FurnitureTests()
    {
        foreach (var dir in new[] { "base", "variants/a10-mira", "variants/dest-moved" })
        {
            var c = Content.Load(Path.Combine(Fc, dir));
            Check("furniture " + dir + ": only the dorm is arrangeable, with twelve pieces",
                c.Arrangeable.Select(r => r.MapId).SequenceEqual(new[] { Dorm }) && c.Arrangeable[0].Pieces.Count == 12);
        }

        // Defaults: the layout's own arrangement, blocking through MapDef.Furnished and not through the fixed collision.
        var g = DormGame();
        var map = g.Content.Map(Dorm);
        Check("furniture: the dorm is arrangeable, the courtyard is not", g.Furniture.IsArrangeable(Dorm) && !g.Furniture.IsArrangeable("map.courtyard"));
        Check("furniture: with no saved entry every piece stands at its layout spot and none is stored",
            g.State.Arrangements.Count == 0 && g.Furniture.Placed(Dorm).Count == 12 && g.Furniture.Stored(Dorm).Count == 0
            && g.Furniture.Placed(Dorm).All(p => { var d = g.Furniture.Piece(Dorm, p.Piece); return p.X == d.At[0] && p.Y == d.At[1]; }));
        Check("furniture: the bookshelf's cells block, and the fixed collision does not list them",
            !Open(g, 3, 5) && !Open(g, 4, 5) && !map.Blocked.Any(b => b[0] == 3 && b[1] == 5) && Open(g, 8, 2) && Open(g, 9, 2));
        Check("furniture: the bed's own cell and the way in stay open", Open(g, 2, 5) && Open(g, 5, 1) && g.Furniture.Placed(Dorm).Count == 12);
        Check("furniture: a rug blocks nothing", Open(g, 2, 2) && Open(g, 3, 2) && Open(g, 4, 2));

        // The rule.
        PlaceProblem Ask(string piece, int x, int y) => g.Furniture.Check(Dorm, piece, x, y);
        Check("rule: a free spot in the area is allowed", Ask("bookshelf", 8, 2) == PlaceProblem.None);
        Check("rule: a footprint outside the area is refused", Ask("bookshelf", 0, 3) == PlaceProblem.OutsideArea && Ask("stool", 11, 2) == PlaceProblem.OutsideArea);
        Check("rule: drawing off the map is refused", Ask("bookshelf", 4, 7) == PlaceProblem.OffMap && Ask("mirror", -2, 2) == PlaceProblem.OffMap);
        Check("rule: the fixed window seat (a wall) is refused", Ask("mirror", 6, 6) == PlaceProblem.Wall);
        Check("rule: the bed's cell is kept free", Ask("stool", 2, 5) == PlaceProblem.Keep);
        Check("rule: another piece is refused", Ask("stool", 3, 5) == PlaceProblem.Piece && Ask("chest", 4, 5) == PlaceProblem.Piece);
        Check("rule: the player's own cell is refused", Ask("stool", 5, 1) == PlaceProblem.Actor);
        Check("rule: a piece may stay where it stands", Ask("desk", 6, 3) == PlaceProblem.None);
        Check("rule: a rug lies anywhere inside the area, under furniture too, but not outside it",
            Ask("rug", 4, 2) == PlaceProblem.None && Ask("rug", 6, 3) == PlaceProblem.None && Ask("rug", 9, 1) == PlaceProblem.OutsideArea && Ask("rug", 0, 1) == PlaceProblem.OutsideArea);
        Check("rule: an unknown piece is refused", Ask("sofa", 4, 2) == PlaceProblem.UnknownPiece && !g.Furniture.Place(Dorm, "sofa", 4, 2));
        Check("rule: a refused placement changes nothing", !g.Furniture.Place(Dorm, "stool", 2, 5) && g.State.Arrangements.Count == 0 && g.Furniture.Revision == 0 && !Open(g, 3, 5));

        // Walking to the bed has to stay possible. In the layout as drawn the bookshelf, the window seat and the bed close all
        // but one side of the bed's cell, so a piece on the cell below it would shut the bed in.
        Check("rule: a piece that would shut the bed in is refused as Cutoff and changes nothing",
            Ask("stool", 2, 4) == PlaceProblem.Cutoff && !g.Furniture.Place(Dorm, "stool", 2, 4) && Standing(g)["stool"] == "8,1" && Open(g, 2, 4) && g.State.Arrangements.Count == 0);
        World.Place(g.Player, new Spot { MapId = Dorm, X = 7.5, Y = 2.5 });
        Check("rule: the only cell in front of the door cannot be filled, it would cut the door off", Ask("stool", 5, 1) == PlaceProblem.Cutoff);
        World.Place(g.Player, new Spot { MapId = Dorm, X = 5.5, Y = 1.5 });

        // Moving a piece moves its collision: the old cells open, the new ones close, for the player and for walkers.
        var player = new Actor { Id = "test", MapId = Dorm, X = 4.5, Y = 4.5 };
        void Push(Actor a, double dx, double dy) { for (var i = 0; i < 60; i++) World.MovePlayer(g.Content, a, dx, dy, 1 / 30.0); }
        Push(player, 0, 1);
        Check("collision: the player stops under the bookshelf at its old spot", player.Y < 5.0 && player.Y > 4.7);
        var before = Standing(g);
        var revision = g.Furniture.Revision;
        Check("furniture: moving the bookshelf succeeds and writes an entry", g.Furniture.Place(Dorm, "bookshelf", 8, 2) && g.State.Arrangements.ContainsKey(Dorm) && g.Furniture.Revision == revision + 1);
        Check("furniture: only the bookshelf moved", Standing(g).Where(kv => kv.Key != "bookshelf").All(kv => before[kv.Key] == kv.Value) && Standing(g)["bookshelf"] == "8,2");
        Check("furniture: the old cells are open and the new ones block", Open(g, 3, 5) && Open(g, 4, 5) && !Open(g, 8, 2) && !Open(g, 9, 2));
        Check("furniture: the fixed collision list is untouched", map.Blocked.All(b => !(b[0] == 8 && b[1] == 2)) && map.Blocked.All(b => !(b[0] == 3 && b[1] == 5)));
        player = new Actor { Id = "test", MapId = Dorm, X = 4.5, Y = 4.5 };
        Push(player, 0, 1);
        Check("collision: the player now walks through the old spot", player.Y > 5.3);
        player = new Actor { Id = "test", MapId = Dorm, X = 9.5, Y = 3.5 };
        Push(player, 0, -1);
        Check("collision: the player stops at the new spot", player.Y >= 3.19 && player.Y < 3.6);
        Check("furniture: with the bookshelf gone the bed has another way in, so the cell below it is free to fill", Ask("stool", 2, 4) == PlaceProblem.None);
        Check("furniture: the player cell is still refused", Ask("stool", 5, 1) == PlaceProblem.Actor);

        // Storing and taking out.
        Check("store: a standing piece goes away and opens its cell", g.Furniture.Store(Dorm, "plant-a") && g.Furniture.IsStored(Dorm, "plant-a") && Open(g, 1, 1) && g.Furniture.Placed(Dorm).Count == 11);
        Check("store: a stored piece cannot be stored again or moved as if standing", !g.Furniture.Store(Dorm, "plant-a") && g.Furniture.PlacementOf(Dorm, "plant-a") == null);
        Check("store: taking it out puts it on a free spot and blocks that spot", g.Furniture.Place(Dorm, "plant-a", 4, 1) && !Open(g, 4, 1) && !g.Furniture.IsStored(Dorm, "plant-a") && g.Furniture.Placed(Dorm).Count == 12);
        Check("store: a stored piece is refused a taken spot", g.Furniture.Store(Dorm, "plant-a") && !g.Furniture.Place(Dorm, "plant-a", 6, 3) && g.Furniture.IsStored(Dorm, "plant-a"));
        g.Furniture.Store(Dorm, "rug");

        // Walkers use the same collision and re-plan when furniture lands on their way.
        var npc = g.Actor("npc.serin");
        World.Place(npc, new Spot { MapId = Dorm, X = 5.5, Y = 1.5 });
        var goal = new Spot { MapId = Dorm, X = 8.5, Y = 5.5 };
        var path = World.FindPath(g.Content, npc, goal);
        Check("path: a walker finds a way across the room around the furniture", path != null && path.All(s => Open(g, (int)Math.Floor(s.X), (int)Math.Floor(s.Y))));
        npc.Path = path;
        var first = path[0];
        var cell = ((int)Math.Floor(first.X), (int)Math.Floor(first.Y));
        Check("path: furniture set on the walker's way re-plans it around the new piece",
            g.Furniture.Place(Dorm, "books", cell.Item1, cell.Item2) && npc.Path.Count > 0 && npc.Path.All(s => (int)Math.Floor(s.X) != cell.Item1 || (int)Math.Floor(s.Y) != cell.Item2)
            && npc.Path[npc.Path.Count - 1] == goal);

        // The day saves the arrangement and a reload gets it back.
        World.Place(npc, new Spot { MapId = "map.courtyard", X = 8.5, Y = 5.5 });
        var arranged = SaveFile.ToJson(g.State);
        var standing = Standing(g);
        var stored = g.Furniture.Stored(Dorm).ToList();
        var blocked = new HashSet<(int X, int Y)>(map.Furnished);
        Check("save: the arrangement is part of the save", arranged.Contains("\"arrangements\"") && arranged.Contains("\"bookshelf\"") && arranged.Contains("\"stored\""));
        Check("save: the day ends and writes the file", g.EndDay(DayEnd.Slept) && File.Exists(g.SavePath));
        var reloaded = Game.Open(Base, Path.Combine(Base, "start-offered.json"), g.SavePath);
        Check("save: a reloaded game stands the same pieces on the same spots and keeps the same pieces away",
            Standing(reloaded).OrderBy(kv => kv.Key).SequenceEqual(standing.OrderBy(kv => kv.Key)) && reloaded.Furniture.Stored(Dorm).SequenceEqual(stored) && stored.Count == 2);
        Check("save: a reloaded game blocks the same cells", reloaded.Content.Map(Dorm).Furnished.SetEquals(blocked) && !World.Passable(reloaded.Content.Map(Dorm), 8, 2) && World.Passable(reloaded.Content.Map(Dorm), 3, 5));
        Check("save: the reloaded arrangement is the saved one, byte for byte", Newtonsoft.Json.JsonConvert.SerializeObject(reloaded.State.Arrangements) == Newtonsoft.Json.JsonConvert.SerializeObject(g.State.Arrangements)
            && reloaded.State.Arrangements[Dorm].Placed.Count + reloaded.State.Arrangements[Dorm].Stored.Count == 12);

        // Bad saves.
        var clean = NewGame("start-offered.json");
        Check("load: a save with no arrangement field loads the layout as drawn", clean.State.Arrangements.Count == 0 && clean.Furniture.Placed(Dorm).Count == 12);
        var state = SaveFile.FromJson(SaveFile.ToJson(clean.State));
        state.Arrangements[Dorm] = new RoomArrangement
        {
            Placed = new List<PiecePlacement>
            {
                new PiecePlacement { Piece = "desk", X = 7, Y = 3 },
                new PiecePlacement { Piece = "ghost", X = 4, Y = 4 },
                new PiecePlacement { Piece = "desk", X = 1, Y = 1 },
                new PiecePlacement { Piece = "mirror", X = 3, Y = 5 },
                null,
                new PiecePlacement { Piece = "stool", X = 11, Y = 11 },
                new PiecePlacement { Piece = "bookshelf", X = 0, Y = 0 },
            },
            Stored = new List<string> { "wardrobe", "nope", "mirror", "wardrobe" }
        };
        var odd = new Game(Content.Load(Base), state, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        var oddStanding = Standing(odd);
        var oddStored = odd.Furniture.Stored(Dorm);
        Check("load: unknown pieces, repeats and null rows are dropped; every piece is in exactly one list",
            oddStanding.Count + oddStored.Count == 12 && oddStanding.Keys.Concat(oddStored).Distinct().Count() == 12 && !oddStanding.ContainsKey("ghost"));
        Check("load: of two rows for one piece the first stands, and an invalid spot goes back to the default spot", oddStanding["desk"] == "7,3" && oddStanding["stool"] == "8,1");
        Check("load: a piece asked for both ways stands, the stored list loses it", oddStanding["mirror"] == "3,5" && !oddStored.Contains("mirror") && oddStored.Contains("wardrobe"));
        Check("load: a piece whose default spot is taken is stored", oddStored.Contains("bookshelf") && !oddStanding.ContainsKey("bookshelf"));
        Check("load: the cleaned arrangement blocks exactly what stands", odd.Content.Map(Dorm).Furnished.SetEquals(Furniture.Blocks(odd.Furniture.Room(Dorm), odd.Furniture.Placed(Dorm))));
        var bad = SaveFile.FromJson(SaveFile.ToJson(clean.State));
        bad.Arrangements["map.nowhere"] = new RoomArrangement { Stored = new List<string> { "x" } };
        bad.Arrangements[Dorm] = null;
        var kept = new Game(Content.Load(Base), bad, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        Check("load: an unknown room is kept untouched and a null entry means the layout", kept.State.Arrangements.ContainsKey("map.nowhere") && !kept.State.Arrangements.ContainsKey(Dorm) && kept.Furniture.Placed(Dorm).Count == 12);

        StackingTests();

        // Events hold the room still.
        var busy = NewGame("start-accepted.json");
        busy.StartEvent("event.snack");
        Check("rule: nothing moves while an event runs", busy.Furniture.Check(Dorm, "stool", 4, 2) == PlaceProblem.Busy && !busy.Furniture.Place(Dorm, "stool", 4, 2) && !busy.Furniture.Store(Dorm, "stool"));
    }

    /// <summary>Layers: a rug under everything, bodies that cannot overlap, small pieces that stand on a desk or a chest lid.</summary>
    static void StackingTests()
    {
        var c = Content.Load(Base);
        var data = c.Arrangeable[0];
        Check("layers: the desk and the chest are hosts, the plants, flowers and books are stackable, the rest are neither",
            data.Pieces.Where(x => x.Surface.Count > 0).Select(x => x.Id).OrderBy(x => x).SequenceEqual(new[] { "chest", "desk" })
            && data.Pieces.Where(x => x.Stackable).Select(x => x.Id).OrderBy(x => x).SequenceEqual(new[] { "books", "flowers", "plant-a", "plant-b" }));

        // Two rugs share no cell. The dorm has one rug, so the room is built here.
        PieceDef Rug(string id) => new PieceDef { Id = id, Name = id, At = new[] { 2, 1 }, Bounds = new[] { 0, 0, 2, 2 } };
        var rugs = new ArrangeableDef { MapId = Dorm, Area = new[] { 1, 1, 10, 7 }, Pieces = { Rug("a"), Rug("b") } };
        var onA = new[] { new PiecePlacement { Piece = "a", X = 3, Y = 3 } };
        PlaceProblem Rugs(int x, int y) => Furniture.Rule(c, rugs, onA, rugs.Pieces[1], x, y, new (int, int)[0]);
        Check("layers: a rug on another rug is refused, beside it or a cell away it is allowed",
            Rugs(3, 3) == PlaceProblem.Layer && Rugs(4, 4) == PlaceProblem.Layer && Rugs(5, 3) == PlaceProblem.None && Rugs(3, 5) == PlaceProblem.None);

        var g = DormGame();
        PlaceProblem Ask(string piece, int x, int y) => g.Furniture.Check(Dorm, piece, x, y);
        (Layer, string)? Stance(string piece) => g.Furniture.Stance(Dorm, piece);
        Check("layers: a rug lies under the desk and is Floor, a desk is Body", Ask("rug", 6, 3) == PlaceProblem.None && Stance("rug") == (Layer.Floor, null) && Stance("desk") == (Layer.Body, null));

        // The desk's tabletop is its top row: (6,4), (7,4), (8,4).
        Check("stack: a plant on the tabletop is allowed, though the desk's cells are taken", Ask("plant-a", 6, 4) == PlaceProblem.None && Ask("plant-a", 8, 4) == PlaceProblem.None);
        Check("stack: the front row of the desk is not tabletop, so a plant there overlaps the desk as a body",
            Ask("plant-a", 6, 3) == PlaceProblem.Piece && Ask("plant-a", 7, 3) == PlaceProblem.Piece);
        Check("stack: a piece that is not stackable cannot use the tabletop", Ask("stool", 6, 4) == PlaceProblem.Piece && Ask("books", 9, 4) == PlaceProblem.None);
        var revision = g.Furniture.Revision;
        Check("stack: placing the plant on the desk makes it Top with the desk as host", g.Furniture.Place(Dorm, "plant-a", 7, 4) && Stance("plant-a") == (Layer.Top, "desk") && g.Furniture.Revision == revision + 1);
        Check("stack: the tabletop plant adds no collision of its own, the desk's cells stay blocked and the rest stays open", !Open(g, 7, 4) && !Open(g, 7, 3) && Open(g, 5, 4) && Open(g, 1, 1));
        Check("stack: a second plant on the same tabletop cell is refused, on another cell it is allowed",
            Ask("plant-b", 7, 4) == PlaceProblem.SurfaceTaken && !g.Furniture.Place(Dorm, "plant-b", 7, 4) && Ask("plant-b", 6, 4) == PlaceProblem.None);
        Check("stack: a plant that stands on the floor is a body, a second one on the same floor cell is refused as Piece",
            Stance("plant-b") == (Layer.Body, null) && Ask("flowers", 9, 1) == PlaceProblem.Piece);

        // The host carries its load.
        Check("load: a desk with a plant on it is Loaded", g.Furniture.IsLoaded(Dorm, "desk") && !g.Furniture.IsLoaded(Dorm, "chest") && Ask("desk", 6, 3) == PlaceProblem.Loaded && Ask("desk", 6, 2) == PlaceProblem.Loaded);
        Check("load: the loaded desk cannot be moved or stored", !g.Furniture.Place(Dorm, "desk", 6, 2) && !g.Furniture.Store(Dorm, "desk") && Stance("desk") == (Layer.Body, null) && Standing(g)["desk"] == "6,3");
        Check("load: a plant on a desk can be lifted off, stored, and then the desk can move", g.Furniture.Store(Dorm, "plant-a") && !g.Furniture.IsLoaded(Dorm, "desk") && Ask("desk", 6, 2) == PlaceProblem.None);

        // A chest lid is one cell wide and sits at [1,0]; the candle stands on the other.
        Check("stack: a plant on the chest lid is allowed, on the candle side it overlaps the chest",
            Ask("plant-a", 3, 3) == PlaceProblem.None && Ask("plant-a", 2, 3) == PlaceProblem.Piece);
        Check("stack: the chest lid takes one thing", g.Furniture.Place(Dorm, "plant-a", 3, 3) && Stance("plant-a") == (Layer.Top, "chest") && Ask("flowers", 3, 3) == PlaceProblem.SurfaceTaken && !g.Furniture.Store(Dorm, "chest"));
        Check("stack: only a stackable piece takes a surface", Ask("stool", 3, 3) == PlaceProblem.Piece && Ask("chest", 6, 4) == PlaceProblem.Loaded);

        // Saved, reloaded: the plant stands on its host again, whatever order the save lists them in.
        Check("save: the plant on the chest is Top again after the day ends and the game is reloaded",
            g.EndDay(DayEnd.Slept) && Game.Open(Base, Path.Combine(Base, "start-offered.json"), g.SavePath) is Game back
            && back.Furniture.Stance(Dorm, "plant-a") == (Layer.Top, "chest") && back.Furniture.IsLoaded(Dorm, "chest"));
        var clean = NewGame("start-offered.json");
        var state = SaveFile.FromJson(SaveFile.ToJson(clean.State));
        state.Arrangements[Dorm] = new RoomArrangement
        {
            Placed = new List<PiecePlacement>
            {
                new PiecePlacement { Piece = "plant-b", X = 8, Y = 4 },
                new PiecePlacement { Piece = "plant-a", X = 6, Y = 4 },
                new PiecePlacement { Piece = "flowers", X = 6, Y = 4 },
                new PiecePlacement { Piece = "desk", X = 6, Y = 3 },
            }
        };
        var loaded = new Game(Content.Load(Base), state, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        Check("load: a plant saved on a desk stands on the desk, even when the save lists it before the desk",
            loaded.Furniture.Stance(Dorm, "plant-a") == (Layer.Top, "desk") && loaded.Furniture.Stance(Dorm, "plant-b") == (Layer.Top, "desk") && Standing(loaded)["desk"] == "6,3");
        Check("load: of two saved on one tabletop cell the first stays, the other goes back to its own default spot",
            Standing(loaded)["flowers"] == "10,1" && loaded.Furniture.Stance(Dorm, "flowers") == (Layer.Body, null));
        Check("load: the desk that holds plants is loaded", loaded.Furniture.IsLoaded(Dorm, "desk") && !loaded.Furniture.Store(Dorm, "desk"));
    }
}
