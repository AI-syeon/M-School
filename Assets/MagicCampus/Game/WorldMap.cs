using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MagicCampus.Game
{
    /// <summary>How one map is drawn, from FirstConnection/world-art.json (written by derive_world.py): a CampusGrid layout or Stage5 rows.</summary>
    public sealed class MapArt
    {
        public int Width, Height;
        /// <summary>Path under StreamingAssets of a CampusGrid layout.json (schema 1).</summary>
        public string Layout;
        public Stage5Rows Stage5;
        /// <summary>Top row first, each row the space separated "#rrggbb" mean colours of its cells as the map draws them (derive_world.py).</summary>
        public string[] Minimap = new string[0];
        /// <summary>The movable furniture of an arrangeable room by piece id: the layout parts that draw it and its minimap colours (derive_world.py).</summary>
        public Dictionary<string, PieceArt> Pieces = new Dictionary<string, PieceArt>();

        Color[][] colours;

        /// <summary>The mean colour of the cell at (x, y), y counted upward like the world; null when the map carries no minimap.</summary>
        public Color? CellColor(int x, int y)
        {
            if (colours == null)
                colours = Minimap.Select(row => row.Split(' ').Select(c => ColorUtility.TryParseHtmlString(c, out var rgb) ? rgb : UnityEngine.Color.black).ToArray()).ToArray();
            var r = colours.Length - 1 - y;
            return r >= 0 && r < colours.Length && x >= 0 && x < colours[r].Length ? colours[r][x] : (Color?)null;
        }

        /// <summary>
        /// The minimap of the empty room with the given furniture painted over it, cell by cell, layer by layer: rugs first, then
        /// bodies, then what stands on them, each from the back of the room to the front. Cells no piece covers keep CellColor.
        /// </summary>
        public Dictionary<(int X, int Y), Color> Furnished(IEnumerable<PiecePlacement> placed, ArrangeableDef room)
        {
            var result = new Dictionary<(int X, int Y), Color>();
            var stance = Furniture.Stand(room, placed);
            foreach (var p in placed.Where(p => Pieces.ContainsKey(p.Piece) && stance.ContainsKey(p.Piece)).OrderBy(p => (int)stance[p.Piece].Layer).ThenByDescending(p => p.Y))
                foreach (var t in Pieces[p.Piece].Tints)
                {
                    var at = (X: p.X + t.Dx, Y: p.Y + t.Dy);
                    var below = result.TryGetValue(at, out var seen) ? seen : CellColor(at.X, at.Y);
                    if (below.HasValue) result[at] = Color.Lerp(below.Value, t.Color, t.Alpha);
                }
            return result;
        }

        public sealed class Stage5Rows
        {
            public string Floor;
            /// <summary>Top row first, one character per cell.</summary>
            public string[] Rows;
            public Dictionary<string, string> Legend = new Dictionary<string, string>();
        }
    }

    /// <summary>One layout placement a movable piece is drawn from.</summary>
    public sealed class PiecePart
    {
        public string Prop;
        public int X, Y;
    }

    /// <summary>How a movable piece looks: its layout parts (the first is the anchor) and, per cell it covers, a colour and opacity for the minimap.</summary>
    public sealed class PieceArt
    {
        public sealed class Tint
        {
            public int Dx, Dy;
            public Color Color;
            public float Alpha;
        }

        public PiecePart[] Parts = new PiecePart[0];
        /// <summary>A rug: drawn on the floor under everything else.</summary>
        public bool Flat;
        /// <summary>"dx dy #rrggbb opacity" per covered cell.</summary>
        public string[] Cells = new string[0];

        Tint[] tints;

        public Tint[] Tints => tints ?? (tints = Cells.Select(c =>
        {
            var f = c.Split(' ');
            return new Tint
            {
                Dx = int.Parse(f[0]), Dy = int.Parse(f[1]), Color = ColorUtility.TryParseHtmlString(f[2], out var rgb) ? rgb : UnityEngine.Color.black,
                Alpha = float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture)
            };
        }).ToArray());
    }

    public sealed class LookState
    {
        /// <summary>The state applies when the target has any of these tags.</summary>
        public string[] When = new string[0];
        public string Sprite, Glow;
        public float[] GlowAt;
    }

    /// <summary>A magic target's sprite per tag state: the first state whose tag the target has wins, else the default.</summary>
    public sealed class TargetLook
    {
        public string Default;
        public List<LookState> States = new List<LookState>();

        public IEnumerable<string> AllSprites => new[] { Default }.Concat(States.Select(s => s.Sprite));

        public LookState StateFor(ISet<string> tags) => States.FirstOrDefault(s => s.When.Any(tags.Contains));
    }

    public sealed class ColorSpec
    {
        public string Color;
        public float Radius, Alpha;

        public UnityEngine.Color Rgb => ColorUtility.TryParseHtmlString(Color, out var c) ? c : UnityEngine.Color.white;
    }

    /// <summary>FirstConnection/world-art.json (written by derive_world.py): how maps are drawn, target looks, glows and spell effect colours.</summary>
    public sealed class WorldArt
    {
        public Dictionary<string, MapArt> Maps = new Dictionary<string, MapArt>();
        public Dictionary<string, TargetLook> Targets = new Dictionary<string, TargetLook>();
        public Dictionary<string, ColorSpec> TargetGlows = new Dictionary<string, ColorSpec>();
        public Dictionary<string, ColorSpec> SpellEffects = new Dictionary<string, ColorSpec>();

        public static WorldArt Load(string path) =>
            File.Exists(path) ? JsonConvert.DeserializeObject<WorldArt>(File.ReadAllText(path)) : new WorldArt();
    }

    /// <summary>What the arrange mode shows over the world: the piece in focus, standing at (X, Y) or, when held, as a ghost there.</summary>
    public sealed class ArrangeOverlay
    {
        public PieceDef Piece;
        public PieceArt Art;
        /// <summary>The anchor cell: where the piece stands, or where the held piece would land.</summary>
        public int X, Y;
        public bool Held, Valid;
        /// <summary>How many pixels (32 to a cell) the piece is drawn higher because it would stand on a host's surface.</summary>
        public int Lift;
    }

    /// <summary>
    /// The scene objects of the map on screen: one ground Tilemap, props as sprites that depth-sort with actors by foot line,
    /// contact shadows and the canopy daylight layer when the layout has them. Built on map change, destroyed on the next.
    /// </summary>
    public sealed class WorldMap
    {
        public const int TileSize = 32;
        const int GroundOrder = 0, CanopyOrder = 1, ContactShadowOrder = 2, FloorDecorOrder = 3;

        /// <summary>Sorting order for anything standing with its feet at world y: lower on screen draws in front.</summary>
        public static int DepthOrder(double footY) => 20000 - Mathf.RoundToInt((float)footY * 100f);

        public readonly GameObject Root;
        public CampusDaylight Daylight { get; private set; }
        public Vector4[] Glows { get; private set; } = new Vector4[0];
        /// <summary>The CampusGrid layout the map was built from; null for Stage5 maps.</summary>
        public CampusLayout Layout { get; private set; }
        readonly List<(SpriteRenderer Renderer, Sprite[] Frames, Material[] Materials, float Fps)> animated =
            new List<(SpriteRenderer, Sprite[], Material[], float)>();
        readonly Dictionary<string, List<SpriteRenderer>> pieceRenderers = new Dictionary<string, List<SpriteRenderer>>();
        readonly List<SpriteRenderer> marks = new List<SpriteRenderer>();
        readonly List<(SpriteRenderer Renderer, Vector2 Offset)> ghost = new List<(SpriteRenderer, Vector2)>();
        string ghostOf, hiddenPiece;
        static Sprite whiteSquare;

        WorldMap(string id)
        {
            Root = new GameObject("Map:" + id, typeof(Grid));
        }

        public void Animate(float time)
        {
            foreach (var (renderer, frames, materials, fps) in animated)
            {
                var index = (int)(time * fps) % frames.Length;
                if (renderer.sprite == frames[index]) continue;
                renderer.sprite = frames[index];
                renderer.sharedMaterial = materials[index];
            }
        }

        public void Destroy() => UnityEngine.Object.Destroy(Root);

        // ------------------------------------------------------------------ arrange mode

        const int MarkOrder = 29000, GhostOrder = 29500;
        static readonly Color GoodTint = new Color(0.42f, 0.9f, 0.45f), BadTint = new Color(0.95f, 0.38f, 0.33f), FocusTint = new Color(1f, 0.83f, 0.35f);

        /// <summary>The box around the outline drawn this frame, in cells; null when nothing is outlined.</summary>
        public RectInt? OutlineBox { get; private set; }

        /// <summary>
        /// Draws the arrange overlay for this frame (null clears it): the floor the piece takes is filled and outlined, in gold for
        /// the piece in focus; a held piece is hidden at its old spot and shown as a tinted ghost, its floor green (it can go there)
        /// or red (it cannot). The floor is the cells it blocks, or for a rug, which blocks nothing, every cell it draws. The rest of
        /// the drawing stands over cells people may walk through, so the outline leaves it out.
        /// </summary>
        public void UpdateOverlay(ArrangeOverlay o, float time)
        {
            if (o == null && marks.Count == 0 && ghost.Count == 0 && hiddenPiece == null) return;
            var hidden = o != null && o.Held ? o.Piece.Id : null;
            if (hidden != hiddenPiece)
            {
                SetPieceVisible(hiddenPiece, true);
                SetPieceVisible(hidden, false);
                hiddenPiece = hidden;
            }
            var rects = new List<(float X, float Y, float W, float H, Color Colour)>();
            OutlineBox = null;
            if (o != null)
            {
                var colour = o.Held ? (o.Valid ? GoodTint : BadTint) : FocusTint;
                var pulse = 0.65f + 0.35f * Mathf.Sin(time * 5f);
                var foot = new HashSet<(int X, int Y)>(o.Piece.Cells.Count > 0 ? Furniture.Footprint(o.Piece, o.X, o.Y) : Furniture.Covered(o.Piece, o.X, o.Y));
                var fill = new Color(colour.r, colour.g, colour.b, o.Held ? 0.38f : 0.18f * pulse + 0.1f);
                var edge = new Color(colour.r, colour.g, colour.b, o.Held ? 1f : pulse);
                const float t = 2f / TileSize;
                foreach (var (x, y) in foot)
                {
                    rects.Add((x, y, 1f, 1f, fill));
                    if (!foot.Contains((x, y - 1))) rects.Add((x, y, 1f, t, edge));
                    if (!foot.Contains((x, y + 1))) rects.Add((x, y + 1 - t, 1f, t, edge));
                    if (!foot.Contains((x - 1, y))) rects.Add((x, y, t, 1f, edge));
                    if (!foot.Contains((x + 1, y))) rects.Add((x + 1 - t, y, t, 1f, edge));
                }
                if (foot.Count > 0)
                {
                    int minX = foot.Min(c => c.X), minY = foot.Min(c => c.Y);
                    OutlineBox = new RectInt(minX, minY, foot.Max(c => c.X) - minX + 1, foot.Max(c => c.Y) - minY + 1);
                }
            }
            SetMarks(rects);
            SetGhost(o != null && o.Held ? o : null);
        }

        /// <summary>The lowest sorting order among the piece's sprites; null when the piece is not drawn.</summary>
        public int? PieceOrder(string piece) =>
            pieceRenderers.TryGetValue(piece, out var list) && list.Count > 0 ? list.Min(r => r.sortingOrder) : (int?)null;

        void SetPieceVisible(string piece, bool visible)
        {
            if (piece != null && pieceRenderers.TryGetValue(piece, out var list))
                foreach (var r in list) r.enabled = visible;
        }

        void SetMarks(List<(float X, float Y, float W, float H, Color Colour)> rects)
        {
            if (whiteSquare == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                whiteSquare = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            }
            while (marks.Count < rects.Count)
            {
                var mark = new GameObject("Mark").AddComponent<SpriteRenderer>();
                mark.transform.SetParent(Root.transform, false);
                mark.sprite = whiteSquare;
                mark.sortingOrder = MarkOrder;
                marks.Add(mark);
            }
            for (var i = 0; i < marks.Count; i++)
            {
                marks[i].enabled = i < rects.Count;
                if (i >= rects.Count) continue;
                var r = rects[i];
                marks[i].transform.localPosition = new Vector3(r.X + r.W / 2f, r.Y + r.H / 2f, 0f);
                marks[i].transform.localScale = new Vector3(r.W, r.H, 1f);
                marks[i].color = r.Colour;
            }
        }

        void SetGhost(ArrangeOverlay o)
        {
            var id = o?.Piece.Id;
            if (id != ghostOf)
            {
                foreach (var g in ghost) UnityEngine.Object.Destroy(g.Renderer.gameObject);
                ghost.Clear();
                ghostOf = id;
                if (o != null && Layout != null)
                {
                    var props = Layout.Props.ToDictionary(p => p.Id);
                    foreach (var part in o.Art.Parts)
                    {
                        if (!props.TryGetValue(part.Prop, out var prop)) continue;
                        foreach (var visual in Layout.Visuals(prop))
                        {
                            var renderer = new GameObject("Ghost:" + prop.Id).AddComponent<SpriteRenderer>();
                            renderer.transform.SetParent(Root.transform, false);
                            renderer.sprite = visual.Frames[0];
                            renderer.sortingOrder = GhostOrder;
                            ghost.Add((renderer, new Vector2(part.X - o.Art.Parts[0].X, part.Y - o.Art.Parts[0].Y) + visual.Centre));
                        }
                    }
                }
            }
            foreach (var (renderer, offset) in ghost)
            {
                renderer.transform.localPosition = new Vector3(o.X + offset.x, o.Y + offset.y + o.Lift / (float)TileSize, 0f);
                renderer.color = o.Valid ? new Color(0.75f, 1f, 0.75f, 0.8f) : new Color(1f, 0.6f, 0.6f, 0.78f);
            }
        }

        /// <summary>Stage5 floor, walls on blocked cells and doors on portals: the look of a map nobody has drawn yet.</summary>
        public static WorldMap Plain(MapDef map, Spot bed, IReadOnlyDictionary<string, Tile> stage5, Material material = null)
        {
            var world = new WorldMap(map.Id);
            var ground = world.Ground(material);
            var floor = stage5.TryGetValue("floor." + map.Id.Substring("map.".Length), out var own) ? own : stage5["floor.courtyard"];
            for (var y = 0; y < map.Height; y++)
                for (var x = 0; x < map.Width; x++)
                {
                    var cell = World.CellAt(map, bed, x, y);
                    ground.SetTile(new Vector3Int(x, y, 0), cell == MapCell.Wall ? stage5["wall.school"] : cell == MapCell.Door ? stage5["door"] : floor);
                }
            return world;
        }

        public static WorldMap FromRows(MapDef map, MapArt.Stage5Rows art, IReadOnlyDictionary<string, Tile> stage5, Material material = null)
        {
            var world = new WorldMap(map.Id);
            var ground = world.Ground(material);
            for (var r = 0; r < art.Rows.Length; r++)
                for (var x = 0; x < art.Rows[r].Length; x++)
                {
                    var tile = art.Legend.TryGetValue(art.Rows[r][x].ToString(), out var name) ? name : art.Floor;
                    ground.SetTile(new Vector3Int(x, art.Rows.Length - 1 - r, 0), stage5[tile]);
                }
            return world;
        }

        /// <summary>
        /// A CampusGrid layout as drawn. The placements art.Pieces lists are the player's movable furniture: they are drawn where
        /// the arrangement puts their piece (not drawn at all while the piece is stored); arrangement null leaves the layout as it is.
        /// A piece standing on a host (Furniture.Stand) is drawn raised by the host's Lift and just in front of the host.
        /// </summary>
        public static WorldMap FromLayout(MapDef map, CampusLayout layout, WorldArtAssets assets, MapArt art = null, IReadOnlyList<PiecePlacement> arrangement = null, ArrangeableDef room = null)
        {
            var world = new WorldMap(map.Id) { Layout = layout };
            var ground = world.Ground();
            foreach (var cell in layout.Ground)
                ground.SetTile(new Vector3Int(cell.X, cell.Y, 0), layout.GroundTile(cell.Tile));
            var groundMaterial = assets?.NormalMaterial(layout.GroundNormals, new Vector2(layout.Width, layout.Height));
            if (groundMaterial != null) ground.GetComponent<TilemapRenderer>().sharedMaterial = groundMaterial;

            var props = layout.Props.ToDictionary(p => p.Id);
            var movable = new Dictionary<(string Prop, int X, int Y), (string Piece, int Dx, int Dy)>();
            if (art != null && arrangement != null)
                foreach (var piece in art.Pieces)
                    foreach (var part in piece.Value.Parts)
                        movable[(part.Prop, part.X, part.Y)] = (piece.Key, part.X - piece.Value.Parts[0].X, part.Y - piece.Value.Parts[0].Y);
            var drawn = new List<(CampusLayout.Placement Placement, string Piece)>();
            foreach (var p in layout.Placements)
            {
                if (!movable.TryGetValue((p.Id, p.X, p.Y), out var m)) drawn.Add((p, null));
                else if (arrangement.FirstOrDefault(a => a.Piece == m.Piece) is PiecePlacement at)
                    drawn.Add((new CampusLayout.Placement { Id = p.Id, X = at.X + m.Dx, Y = at.Y + m.Dy, Order = p.Order, Decorative = p.Decorative }, m.Piece));
            }
            var placements = drawn.Select(d => d.Placement).ToArray();
            var lines = SortLines(props, placements);
            var stance = room != null && arrangement != null ? Furniture.Stand(room, arrangement) : new Dictionary<string, (Layer Layer, string Host)>();
            // A Top piece's line comes from the lowest line of its host's parts, so it draws in front of all of them and still sorts with actors by the host's foot.
            double HostLine(string host) => drawn.Select((d, i) => (d.Piece, Line: lines[i])).Where(t => t.Piece == host).Select(t => t.Line).DefaultIfEmpty(double.NaN).Min();
            var anchors = new List<(Transform At, string Prop)>();
            for (var i = 0; i < drawn.Count; i++)
            {
                var (placement, piece) = drawn[i];
                var footY = lines[i];
                var lift = 0f;
                if (piece != null && stance.TryGetValue(piece, out var s) && s.Layer == Layer.Top && !double.IsNaN(HostLine(s.Host)))
                {
                    footY = HostLine(s.Host) - 0.01;
                    lift = room.Pieces.First(d => d.Id == s.Host).Lift / (float)TileSize;
                }
                var root = new GameObject("Prop:" + placement.Id).transform;
                root.SetParent(world.Root.transform, false);
                root.localPosition = new Vector3(placement.X, placement.Y + lift, 0f);
                var order = layout.IsFloorDecor(placement, props[placement.Id]) ? FloorDecorOrder + placement.Order : DepthOrder(footY);
                foreach (var (centre, frames, fps) in layout.Visuals(props[placement.Id]))
                {
                    var renderer = new GameObject("Sprite").AddComponent<SpriteRenderer>();
                    renderer.transform.SetParent(root, false);
                    renderer.transform.localPosition = centre;
                    renderer.sprite = frames[0];
                    renderer.sortingOrder = order;
                    var fallback = renderer.sharedMaterial;
                    if (frames.Length > 1 && fps > 0)
                    {
                        var materials = frames.Select(frame => assets?.NormalMaterial(layout.NormalFor(frame)) ?? fallback).ToArray();
                        renderer.sharedMaterial = materials[0];
                        world.animated.Add((renderer, frames, materials, fps));
                    }
                    else
                    {
                        var material = assets?.NormalMaterial(layout.NormalFor(frames[0]));
                        if (material != null) renderer.sharedMaterial = material;
                    }
                    if (piece != null)
                    {
                        if (!world.pieceRenderers.TryGetValue(piece, out var list)) world.pieceRenderers[piece] = list = new List<SpriteRenderer>();
                        list.Add(renderer);
                    }
                }
                anchors.Add((root, placement.Id));
            }

            world.Glows = layout.Glows.Select(g => new Vector4(g.X, g.Y, g.Radius, g.Power)).ToArray();
            if (assets != null && assets.ContactShadowShader != null && layout.Shadows.Length > 0) world.AddContactShadows(layout.Shadows, assets.ContactShadowShader);
            if (assets != null && assets.DaylightTemplate != null && layout.Daylight != null) world.AddDaylight(layout, anchors, assets);
            return world;
        }

        Tilemap Ground(Material material = null)
        {
            var layer = new GameObject("Ground", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(Root.transform, false);
            var renderer = layer.GetComponent<TilemapRenderer>();
            renderer.sortingOrder = GroundOrder;
            if (material != null) renderer.sharedMaterial = material;
            return layer.GetComponent<Tilemap>();
        }

        /// <summary>
        /// Each placement's foot line (its bottom row), lowered just enough that a prop the layout orders in front of an
        /// overlapping prop still draws in front of it: the artist's order wins between props, the foot line against actors.
        /// </summary>
        static List<double> SortLines(Dictionary<string, CampusLayout.Prop> props, CampusLayout.Placement[] placements)
        {
            var lines = placements.Select(p => (double)p.Y).ToList();
            RectInt Bounds(CampusLayout.Placement p)
            {
                var bounds = props[p.Id].VisualBounds;
                bounds.position += new Vector2Int(p.X, p.Y);
                return bounds;
            }
            var byOrder = Enumerable.Range(0, placements.Length).OrderBy(i => placements[i].Order).ThenBy(i => i).ToList();
            for (var k = 0; k < byOrder.Count; k++)
            {
                var i = byOrder[k];
                for (var b = 0; b < k; b++)
                {
                    var j = byOrder[b];
                    if (placements[j].Order < placements[i].Order && Bounds(placements[i]).Overlaps(Bounds(placements[j])))
                        lines[i] = Math.Min(lines[i], lines[j] - 0.01);
                }
            }
            return lines;
        }

        void AddContactShadows(CampusLayout.Shadow[] shadows, Shader shader)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            foreach (var shadow in shadows)
            {
                var first = vertices.Count;
                var rotation = Quaternion.Euler(0, 0, shadow.Angle);
                foreach (var corner in corners)
                {
                    vertices.Add(new Vector3(shadow.X, shadow.Y) + rotation * new Vector3(corner.x * shadow.Width / 2, corner.y * shadow.Height / 2));
                    colors.Add(new Color(1, 1, 1, shadow.Opacity));
                    uv.Add(corner);
                }
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            var mesh = new Mesh { name = "Contact shadows" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            var layer = new GameObject("Contact Shadows", typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(Root.transform, false);
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(shader);
            renderer.sortingOrder = ContactShadowOrder;
        }

        /// <summary>The canopy shadow layer, set up the way CampusGridImport does for the preview scene.</summary>
        void AddDaylight(CampusLayout layout, List<(Transform At, string Prop)> anchors, WorldArtAssets assets)
        {
            var spec = layout.Daylight;
            var canopy = spec.Canopy;
            if (canopy == null) return;
            CampusDaylight.Caster Caster(Transform anchor, Vector2 offset) => new CampusDaylight.Caster
            {
                anchor = anchor, offset = offset, width = canopy.Width, depth = canopy.Depth, height = canopy.Height, opacity = canopy.Opacity
            };
            var casters = anchors.Where(a => a.Prop == canopy.Prop).Select(a => Caster(a.At, new Vector2(canopy.OffsetX, canopy.OffsetY)))
                .Concat((spec.Offscreen ?? new CampusLayout.Point[0]).Select(p => Caster(null, new Vector2(p.X, p.Y)))).ToArray();

            var daylight = UnityEngine.Object.Instantiate(assets.DaylightTemplate, Root.transform, false);
            var layer = daylight.gameObject;
            layer.name = "Daylight";
            var mesh = new Mesh { name = "Canopy projection" };
            mesh.vertices = new[] { Vector3.zero, new Vector3(layout.Width, 0), new Vector3(layout.Width, layout.Height), new Vector3(0, layout.Height) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(assets.CanopyShader) { mainTexture = assets.CanopyMask };
            renderer.sortingOrder = CanopyOrder;
            daylight.Configure(renderer, casters, spec.StartHour, 0f);
            layer.SetActive(true);
            Daylight = daylight;
        }
    }

    /// <summary>A CampusGrid layout with terrain tiles and whole-object sprite sheets.</summary>
    public sealed class CampusLayout
    {
        public sealed class TileSpec { public string Id; public string[] Frames; public float Fps; }
        public sealed class Cell { public string Tile; public int X, Y; }
        public sealed class FrameRect { public int X, Y, W, H; }
        public sealed class Prop
        {
            public string Id, Image, Normal;
            public FrameRect[] Rectangles;
            public float Fps;
            public int[] Bounds;
            public Cell[] Cells;
            public Cell[] Blocked = new Cell[0];

            public RectInt VisualBounds
            {
                get
                {
                    if (Bounds != null) return new RectInt(Bounds[0], Bounds[1], Bounds[2], Bounds[3]);
                    int x = Cells.Min(c => c.X), y = Cells.Min(c => c.Y);
                    return new RectInt(x, y, Cells.Max(c => c.X) - x + 1, Cells.Max(c => c.Y) - y + 1);
                }
            }
        }
        public sealed class Placement { public string Id; public int X, Y, Order; public bool Decorative; }
        public sealed class Glow { public float X, Y, Radius, Power; }
        public sealed class Shadow { public float X, Y, Width, Height, Angle, Opacity; }
        public sealed class Point { public float X, Y; }
        public sealed class Canopy { public string Prop; public float OffsetX, OffsetY, Width, Depth, Height, Opacity; }
        public sealed class DaylightSpec { public float StartHour; public Canopy Canopy; public Point[] Offscreen; }

        sealed class Atlas
        {
            public sealed class Frame { public string Name; public int X, Y, W, H; }
            public int TileSize;
            public string Png;
            public Frame[] Frames;
            public TileSpec[] Tiles;
        }

        public int Schema, TileSize, Width, Height;
        public string PackedAtlas, NormalDirectory, PackedNormalAtlas, GroundNormal;
        public Texture2D GroundNormals { get; private set; }
        public TileSpec[] Tiles = new TileSpec[0];
        public Cell[] Ground = new Cell[0];
        public Prop[] Props = new Prop[0];
        public Placement[] Placements = new Placement[0];
        public Glow[] Glows = new Glow[0];
        public Shadow[] Shadows = new Shadow[0];
        public DaylightSpec Daylight;

        readonly Dictionary<string, (Sprite[] Frames, float Fps)> frames = new Dictionary<string, (Sprite[], float)>();
        readonly Dictionary<string, Sprite[]> propFrames = new Dictionary<string, Sprite[]>();
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        readonly Dictionary<Texture2D, Texture2D> normals = new Dictionary<Texture2D, Texture2D>();

        public static CampusLayout Load(string path)
        {
            var layout = JsonConvert.DeserializeObject<CampusLayout>(File.ReadAllText(path));
            if (layout.Schema != 1 || layout.TileSize != WorldMap.TileSize) throw new InvalidDataException(path + " must use schema 1 and 32 px tiles");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(layout.GroundNormal))
                layout.GroundNormals = WorldArtAssets.LoadNormal(Path.Combine(dir, layout.GroundNormal),
                    layout.Width * WorldMap.TileSize, layout.Height * WorldMap.TileSize);
            var files = new Dictionary<string, Sprite>();
            Sprite CellSprite(string name)
            {
                if (files.TryGetValue(name, out var sprite)) return sprite;
                var textures = Path.Combine(dir, "Textures", name);
                var tex = LoadTexture(File.ReadAllBytes(File.Exists(textures) ? textures : Path.Combine(dir, name)), name);
                if (!string.IsNullOrEmpty(layout.NormalDirectory))
                    layout.AddNormal(tex, Path.Combine(dir, layout.NormalDirectory, name));
                return files[name] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), WorldMap.TileSize, 0, SpriteMeshType.FullRect);
            }
            foreach (var spec in layout.Tiles) layout.frames[spec.Id] = (spec.Frames.Select(CellSprite).ToArray(), spec.Fps);
            if (!string.IsNullOrEmpty(layout.PackedAtlas))
            {
                var atlas = JsonConvert.DeserializeObject<Atlas>(File.ReadAllText(Path.Combine(dir, layout.PackedAtlas)));
                var tex = LoadTexture(Convert.FromBase64String(atlas.Png), layout.PackedAtlas);
                if (!string.IsNullOrEmpty(layout.PackedNormalAtlas))
                    layout.AddNormal(tex, Path.Combine(dir, layout.PackedNormalAtlas));
                var sprites = atlas.Frames.ToDictionary(f => f.Name,
                    f => Sprite.Create(tex, new Rect(f.X, tex.height - f.Y - f.H, f.W, f.H), new Vector2(0.5f, 0.5f), WorldMap.TileSize, 0, SpriteMeshType.FullRect));
                foreach (var spec in atlas.Tiles) layout.frames[spec.Id] = (spec.Frames.Select(n => sprites[n]).ToArray(), spec.Fps);
            }
            foreach (var prop in layout.Props.Where(p => p.Image != null))
            {
                var tex = LoadTexture(File.ReadAllBytes(Path.Combine(dir, prop.Image)), prop.Image);
                if (prop.Normal != null) layout.AddNormal(tex, Path.Combine(dir, prop.Normal));
                layout.propFrames[prop.Id] = prop.Rectangles.Select(r => Sprite.Create(tex,
                    new Rect(r.X, tex.height - r.Y - r.H, r.W, r.H), new Vector2(0.5f, 0.5f),
                    WorldMap.TileSize, 0, SpriteMeshType.FullRect)).ToArray();
            }
            return layout;
        }

        public IEnumerable<(Vector2 Centre, Sprite[] Frames, float Fps)> Visuals(Prop prop)
        {
            if (propFrames.TryGetValue(prop.Id, out var sprites))
            {
                yield return (sprites[0].bounds.size / 2f, sprites, prop.Fps);
                yield break;
            }
            foreach (var cell in prop.Cells)
            {
                var (spritesForCell, fps) = Frames(cell.Tile);
                yield return (new Vector2(cell.X + 0.5f, cell.Y + 0.5f), spritesForCell, fps);
            }
        }

        void AddNormal(Texture2D color, string path)
        {
            var normal = WorldArtAssets.LoadNormal(path, color.width, color.height);
            if (normal != null) normals.Add(color, normal);
        }

        public Texture2D NormalFor(Sprite sprite) =>
            normals.TryGetValue(sprite.texture, out var normal) ? normal : null;

        /// <summary>
        /// A rug or runner: decorative, nothing blocked, and ordered below every collidable prop by the layout. It lies on the floor,
        /// so actors walk over it whatever its foot line is.
        /// </summary>
        public bool IsFloorDecor(Placement placement, Prop prop) =>
            placement.Decorative && prop.Blocked.Length == 0 && Placements.Where(p => !p.Decorative).All(p => placement.Order < p.Order);

        public (Sprite[] Frames, float Fps) Frames(string tileId) =>
            frames.TryGetValue(tileId, out var f) ? f : throw new InvalidDataException("layout tile " + tileId + " has no frames");

        public Tile GroundTile(string tileId)
        {
            if (tiles.TryGetValue(tileId, out var tile)) return tile;
            var (sprites, fps) = Frames(tileId);
            var animated = ScriptableObject.CreateInstance<CampusGridTile>();
            animated.SetFrames(sprites, fps);
            return tiles[tileId] = animated;
        }

        static Texture2D LoadTexture(byte[] png, string name)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name };
            if (!tex.LoadImage(png)) throw new InvalidDataException("cannot decode " + name);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
