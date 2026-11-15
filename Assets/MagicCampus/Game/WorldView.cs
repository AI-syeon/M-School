using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MagicCampus.Game
{
    /// <summary>
    /// Draws the player's map and everyone on it. The map look comes from FirstConnection/world-art.json (a CampusGrid layout,
    /// Stage5 rows, or Stage5 floor and walls when the map has no entry); actors and Stage5 tiles from Stage5Art's
    /// runtime-manifest.json. Actors, props and magic targets (TargetViews) depth-sort by foot line. The camera follows the player
    /// at an integer zoom, clamped to the map, over a dark backdrop, and leaves room under the dialogue box; the courtyard's
    /// daylight follows the game clock. A map change fades out (the old map freezes), switches and fades in; doors carry a cue.
    /// </summary>
    public sealed class WorldView
    {
        static readonly Color Backdrop = new Color32(26, 24, 28, 255);

        sealed class Clip
        {
            public float Fps;
            public Sprite[] Frames;
            public Material NormalMaterial;
        }

        sealed class ActorSprite
        {
            public SpriteRenderer Renderer;
            public string Clip;
            public float Time;
        }

        sealed class DoorCue
        {
            public SpriteRenderer Arrow, Strip;
            public Vector2 Base, Toward;
        }

        enum Fade { None, Out, In }

        public const float FadeSeconds = 0.25f;

        /// <summary>Art pixels per world tile. The screen shows Zoom screen pixels per art pixel.</summary>
        public readonly int PixelsPerUnit = WorldMap.TileSize;
        /// <summary>Screen pixels covered by the HUD at the top; the camera centres the player in the area below it.</summary>
        public int TopInset;
        /// <summary>Screen pixels the dialogue box and its choices cover at the bottom; the camera keeps the player above them.</summary>
        public float BottomInset;
        /// <summary>Screen pixels covered at the left (the arrange panel); the map is centred in the rest.</summary>
        public int LeftInset;
        /// <summary>World point the camera follows instead of the player, or null. The arrange mode follows the piece it moves.</summary>
        public Vector2? FocusOverride;
        /// <summary>What the arrange mode shows over the map this frame; null when it is closed.</summary>
        public ArrangeOverlay Arrange;
        /// <summary>Screen pixels per art pixel.</summary>
        public int Zoom { get; private set; } = 1;

        readonly string root;
        readonly Camera camera;
        readonly CampusCourtyardLook look;
        readonly WorldArtAssets assets;
        readonly WorldArt art;
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Tile> stage5Tiles = new Dictionary<string, Tile>();
        readonly Dictionary<string, Dictionary<string, Clip>> clips = new Dictionary<string, Dictionary<string, Clip>>();
        readonly Dictionary<string, ActorSprite> actors = new Dictionary<string, ActorSprite>();
        readonly Dictionary<string, float> heads = new Dictionary<string, float>();
        readonly Dictionary<string, Texture2D> faces = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, CampusLayout> layouts = new Dictionary<string, CampusLayout>();
        readonly List<DoorCue> doorCues = new List<DoorCue>();
        readonly int stage5Ppu;
        readonly Material stage5GroundMaterial;
        WorldMap shown;
        TargetViews targets;
        string shownId;
        float time, bottomInset, fadeAt;
        Fade fade;
        Sprite cueStrip;
        readonly Sprite[] cueArrows = new Sprite[4];

        /// <summary>The old map is frozen and going dark: the game waits so nothing happens behind the black.</summary>
        public bool Freezing => fade == Fade.Out;
        public bool Transitioning => fade != Fade.None;
        public int DoorCueCount => doorCues.Count;
        /// <summary>0 clear, 1 black.</summary>
        public float FadeAlpha => fade == Fade.Out ? Mathf.Clamp01(fadeAt / FadeSeconds) : fade == Fade.In ? Mathf.Clamp01(1f - fadeAt / FadeSeconds) : 0f;
        /// <summary>World y of the top of the target's sprite.</summary>
        public double TargetTop(MagicTargetDef target) => Mathf.Floor((float)target.Y) + (targets?.HeightOf(target.Id) ?? 1);
        public string TargetSprite(string targetId) => targets?.SpriteOf(targetId);
        public bool TargetEffectPlaying(string targetId) => targets != null && targets.EffectPlaying(targetId, time);
        public bool TargetGlowShown(string targetId) => targets != null && targets.GlowShown(targetId);

        public WorldView(string artRoot, Camera camera)
        {
            root = artRoot;
            assets = UnityEngine.Object.FindFirstObjectByType<WorldArtAssets>();
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(artRoot, "runtime-manifest.json")));
            stage5Ppu = (int?)manifest["pixelsPerUnit"] ?? 16;
            foreach (var t in (JObject)manifest["tiles"])
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = Sprites(LoadTexture(Path.Combine(root, (string)t.Value["image"])), t.Value["frames"], new Vector2(0.5f, 0.5f), stage5Ppu)[0];
                tile.colliderType = Tile.ColliderType.None;
                stage5Tiles[t.Key] = tile;
            }
            var stage5Atlases = stage5Tiles.Values.Select(tile => tile.sprite.texture).Distinct().ToArray();
            if (assets != null && assets.NormalShader != null && stage5Atlases.Length == 1)
            {
                var atlas = stage5Atlases[0];
                stage5GroundMaterial = assets.NormalMaterial(
                    WorldArtAssets.LoadNormal(Path.Combine(root, "tiles", "normals", "atlas.png"), atlas.width, atlas.height));
            }
            foreach (var a in (JObject)manifest["actors"])
            {
                var pivot = a.Value["pivot"] is JArray p ? new Vector2((float)p[0], (float)p[1]) : new Vector2(0.5f, 0.0625f);
                var tex = LoadTexture(Path.Combine(root, (string)a.Value["image"]));
                var ppu = (int?)a.Value["pixelsPerUnit"] ?? stage5Ppu;
                var material = a.Value["normal"] is JValue normal && assets?.NormalShader != null
                    ? assets.NormalMaterial(WorldArtAssets.LoadNormal(Path.Combine(root, (string)normal), tex.width, tex.height))
                    : null;
                Clip ReadClip(JToken spec)
                {
                    var clip = NewClip(tex, spec, pivot, ppu);
                    clip.NormalMaterial = material;
                    return clip;
                }
                var set = new Dictionary<string, Clip>();
                foreach (var c in (JObject)a.Value["animations"]) set[c.Key] = ReadClip(c.Value);
                if (a.Value["directionAnimations"] is JObject directions)
                    foreach (var d in directions)
                        foreach (var c in (JObject)d.Value) set[d.Key + "." + c.Key] = ReadClip(c.Value);
                clips[a.Key] = set;
            }
            var characters = Path.Combine(GamePaths.StreamingAssets, "UiArt", "Characters");
            if (Directory.Exists(characters))
                foreach (var manifestPath in Directory.GetFiles(characters, "manifest.json", SearchOption.AllDirectories)) LoadCharacter(manifestPath);
            art = WorldArt.Load(Path.Combine(GamePaths.StreamingAssets, "FirstConnection", "world-art.json"));

            this.camera = camera;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Backdrop;
            camera.allowMSAA = false;
            var letterbox = new GameObject("Letterbox").AddComponent<Camera>();
            letterbox.clearFlags = CameraClearFlags.SolidColor;
            letterbox.backgroundColor = Backdrop;
            letterbox.cullingMask = 0;
            letterbox.depth = camera.depth - 1f;
            if (assets != null && assets.GradeShader != null)
            {
                look = camera.gameObject.AddComponent<CampusCourtyardLook>();
                look.enabled = false;
            }
        }

        /// <summary>Called once per frame after Game.Update; dt advances the animations (0 while paused).</summary>
        public void Draw(Core.Game game, float dt)
        {
            time += dt;
            var real = Time.unscaledDeltaTime;
            var map = game.Content.Map(game.MapId);
            if (map.Id != shownId)
            {
                if (shownId == null || game.Hidden) Show(game, map);
                else if (fade != Fade.Out)
                {
                    fade = Fade.Out;
                    fadeAt = 0f;
                }
            }
            if (fade == Fade.Out)
            {
                fadeAt += real;
                shown.Animate(time);
                if (fadeAt < FadeSeconds) return;
                Show(game, map);
                fade = Fade.In;
                fadeAt = 0f;
            }
            else if (fade == Fade.In)
            {
                fadeAt += real;
                if (fadeAt >= FadeSeconds) fade = Fade.None;
            }
            shown.Animate(time);
            shown.UpdateOverlay(map.Id == shownId ? Arrange : null, time);
            if (shown.Daylight != null) shown.Daylight.SetGameMinute(game.State.Minute);
            assets?.SetNormalLight(shown.Daylight);
            camera.cullingMask = game.Hidden ? 0 : ~0;
            foreach (var actor in game.Actors) DrawActor(actor, map.Id, dt);
            var free = game.Event == null && game.Dialogue == null;
            targets.Update(game, time, free ? game.Magic.Aim(3) : null);
            DrawDoorCues();
            bottomInset = Mathf.Abs(bottomInset - BottomInset) < 0.5f ? BottomInset : bottomInset + (BottomInset - bottomInset) * (1f - Mathf.Exp(-14f * real));
            PlaceCamera(map, FocusOverride ?? new Vector2((float)game.Player.X, (float)game.Player.Y));
        }

        /// <summary>Draws the shown map again from the game's current furniture arrangement.</summary>
        public void Rebuild(Core.Game game)
        {
            if (shownId != null) Show(game, game.Content.Map(shownId));
        }

        /// <summary>Screen position in GUI coordinates (y down) of a world point, for labels drawn by OnGUI.</summary>
        public Vector2 GuiPoint(double x, double y)
        {
            var p = camera.WorldToScreenPoint(new Vector3((float)x, (float)y, 0f));
            return new Vector2(p.x, Screen.height - p.y);
        }

        /// <summary>GUI point just above an actor's head, for speech bubbles and name tags.</summary>
        public Vector2 HeadGuiPoint(Actor actor) => GuiPoint(actor.X, actor.Y + (heads.TryGetValue(actor.Id, out var head) ? head + 0.08 : 1.9));

        void Show(Core.Game game, MapDef map)
        {
            shown?.Destroy();
            targets?.Destroy();
            doorCues.Clear();
            if (!art.Maps.TryGetValue(map.Id, out var look)) shown = WorldMap.Plain(map, game.Content.Bed, stage5Tiles, stage5GroundMaterial);
            else if (look.Layout != null)
                shown = WorldMap.FromLayout(map, Layout(Path.Combine(GamePaths.StreamingAssets, look.Layout)), assets, look,
                    game.Furniture.IsArrangeable(map.Id) ? game.Furniture.Placed(map.Id) : null, game.Furniture.Room(map.Id));
            else shown = WorldMap.FromRows(map, look.Stage5, stage5Tiles, stage5GroundMaterial);
            shownId = map.Id;
            targets = new TargetViews(map.Id, game.Content.MagicTargets.Where(t => t.MapId == map.Id), shown.Layout, art, assets);
            BuildDoorCues(map);
        }

        /// <summary>A layout is read once; its sprites and tiles are shared by every WorldMap built from it (rebuilds after a furniture change stay cheap).</summary>
        CampusLayout Layout(string path) => layouts.TryGetValue(path, out var layout) ? layout : layouts[path] = CampusLayout.Load(path);

        // ------------------------------------------------------------------ door cues

        /// <summary>
        /// A warm light strip and a bobbing arrow on every portal cell, pointing out of the room: the cell itself is drawn by the
        /// art (a rug, a doorway), so the opening is told here and not by tile colour.
        /// </summary>
        void BuildDoorCues(MapDef map)
        {
            if (cueStrip == null) MakeCueSprites();
            foreach (var portal in map.Portals)
            {
                int cx = Mathf.FloorToInt((float)portal.X), cy = Mathf.FloorToInt((float)portal.Y);
                var inside = new[] { (0, 1), (0, -1), (1, 0), (-1, 0) }
                    .Where(d => World.Passable(map, cx + d.Item1, cy + d.Item2) && World.PortalAt(map, cx + d.Item1, cy + d.Item2) == null).ToList();
                var toward = inside.Count == 0 ? new Vector2(0f, -1f) : new Vector2(-inside[0].Item1, -inside[0].Item2);
                var order = WorldMap.DepthOrder(cy) + 5;
                SpriteRenderer Part(string name, Sprite sprite, int o)
                {
                    var r = new GameObject("Door:" + portal.Id + ":" + name).AddComponent<SpriteRenderer>();
                    r.transform.SetParent(shown.Root.transform, false);
                    r.sprite = sprite;
                    r.sortingOrder = o;
                    return r;
                }
                var arrowIndex = toward.y < 0 ? 0 : toward.y > 0 ? 1 : toward.x < 0 ? 2 : 3;
                var cue = new DoorCue { Base = new Vector2(cx + 0.5f, cy + 0.5f), Toward = toward, Strip = Part("strip", cueStrip, order), Arrow = Part("arrow", cueArrows[arrowIndex], order + 1) };
                cue.Strip.transform.localScale = toward.x != 0f ? new Vector3(0.5f, 1.2f, 1f) : new Vector3(1.2f, 0.5f, 1f);
                doorCues.Add(cue);
            }
        }

        void DrawDoorCues()
        {
            var pulse = 0.5f + 0.5f * Mathf.Sin(time * 4f);
            var bob = Mathf.Floor(time * 2.2f % 2f) * 2f / PixelsPerUnit;
            foreach (var cue in doorCues)
            {
                cue.Strip.transform.localPosition = new Vector3(cue.Base.x, cue.Base.y, 0f);
                cue.Strip.color = new Color(1f, 0.82f, 0.42f, 0.3f + 0.3f * pulse);
                var p = cue.Base + cue.Toward * (bob - 1f / PixelsPerUnit);
                cue.Arrow.transform.localPosition = new Vector3(Snap(p.x), Snap(p.y), 0f);
                cue.Arrow.color = new Color(1f, 1f, 1f, 0.75f + 0.25f * pulse);
            }
        }

        static readonly string[] ArrowShape =
        {
            "..XXXXX..",
            "..XXXXX..",
            "XXXXXXXXX",
            ".XXXXXXX.",
            "..XXXXX..",
            "...XXX...",
            "....X....",
        };

        void MakeCueSprites()
        {
            var halo = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var d = Mathf.Sqrt((x + 0.5f - 16f) * (x + 0.5f - 16f) + (y + 0.5f - 16f) * (y + 0.5f - 16f)) / 16f;
                    var a = Mathf.Clamp01(1f - d);
                    halo.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            halo.Apply();
            cueStrip = Sprite.Create(halo, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);

            // The down arrow on an 11 x 9 grid (row 0 on top) with a one pixel outline; up, left and right are turned copies.
            const int cols = 11, rows = 9;
            bool Fill(int c, int r) => c >= 1 && r >= 1 && c <= 9 && r <= 7 && ArrowShape[r - 1][c - 1] == 'X';
            Color Pixel(int c, int r)
            {
                if (c < 0 || r < 0 || c >= cols || r >= rows) return Color.clear;
                if (Fill(c, r)) return new Color(1f, 0.9f, 0.5f);
                var edge = Fill(c + 1, r) || Fill(c - 1, r) || Fill(c, r + 1) || Fill(c, r - 1);
                return edge ? new Color(0.2f, 0.11f, 0.03f) : Color.clear;
            }
            for (var dir = 0; dir < 4; dir++)
            {
                int w = dir < 2 ? cols : rows, h = dir < 2 ? rows : cols;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        // dir 0 points down, 1 up, 2 left, 3 right; texture y runs up, grid rows run down.
                        var pixel = dir == 0 ? Pixel(x, h - 1 - y) : dir == 1 ? Pixel(x, y) : dir == 2 ? Pixel(y, w - 1 - x) : Pixel(y, x);
                        tex.SetPixel(x, y, pixel);
                    }
                tex.Apply();
                cueArrows[dir] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
        }

        void DrawActor(Actor actor, string mapId, float dt)
        {
            if (!actors.TryGetValue(actor.Id, out var view))
            {
                view = new ActorSprite { Renderer = new GameObject("Actor:" + actor.Id).AddComponent<SpriteRenderer>() };
                actors[actor.Id] = view;
            }
            view.Renderer.enabled = actor.MapId == mapId;
            var set = clips[actor.Id];
            var key = ActorClips.KeyFor(actor, set.ContainsKey);
            if (key != view.Clip)
            {
                view.Clip = key;
                view.Time = 0f;
            }
            else view.Time += dt;
            var clip = set[key];
            view.Renderer.sprite = clip.Frames[(int)(view.Time * clip.Fps) % clip.Frames.Length];
            if (clip.NormalMaterial != null && view.Renderer.sharedMaterial != clip.NormalMaterial)
                view.Renderer.sharedMaterial = clip.NormalMaterial;
            view.Renderer.sortingOrder = WorldMap.DepthOrder(actor.Y);
            view.Renderer.transform.position = new Vector3(Snap(actor.X), Snap(actor.Y), 0f);
        }

        /// <summary>The box the arrange outline covers on the shown map, in cells; null when nothing is outlined.</summary>
        public RectInt? ArrangeOutline => shown?.OutlineBox;

        /// <summary>Whether every sprite of the piece draws over the actor: true for an actor standing behind its floor.</summary>
        public bool PieceCovers(string pieceId, string actorId) =>
            shown?.PieceOrder(pieceId) is int order && actors.TryGetValue(actorId, out var view) && order > view.Renderer.sortingOrder;

        /// <summary>The clip the actor plays now ("south.read", "west.walk"); null before its first draw.</summary>
        public string ClipOf(string actorId) => actors.TryGetValue(actorId, out var view) ? view.Clip : null;

        /// <summary>The frame index of that clip now, so a probe can see it advance.</summary>
        public int FrameOf(string actorId)
        {
            if (!actors.TryGetValue(actorId, out var view) || view.Clip == null) return -1;
            var clip = clips[actorId][view.Clip];
            return (int)(view.Time * clip.Fps) % clip.Frames.Length;
        }

        float Snap(double v) => Mathf.Round((float)v * PixelsPerUnit) / PixelsPerUnit;

        /// <summary>
        /// Full-window camera at Zoom screen pixels per art pixel (2 at 640 px high). It centres the player in the area below
        /// TopInset and above the bottom inset, and stops at the map edges; a map smaller than that area sits in its middle over the backdrop.
        /// </summary>
        void PlaceCamera(MapDef map, Vector2 focus)
        {
            int sw = Screen.width, sh = Screen.height;
            Zoom = Math.Max(1, Mathf.RoundToInt(sh / 320f));
            float unit = PixelsPerUnit * Zoom, halfW = sw / (2f * unit), halfH = sh / (2f * unit);
            var top = Mathf.Clamp(TopInset, 0, sh) / unit;
            var bottom = Mathf.Clamp(bottomInset, 0, sh) / unit;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.ResetAspect();
            camera.orthographicSize = halfH;
            var x = Follow(focus.x, map.Width, halfW, 0f, Mathf.Clamp(LeftInset, 0, sw) / unit);
            var y = Follow(focus.y, map.Height, halfH, top, bottom);
            x = Mathf.Round((x - halfW) * unit) / unit + halfW;
            y = Mathf.Round((y - halfH) * unit) / unit + halfH;
            camera.transform.SetPositionAndRotation(new Vector3(x, y, -10f), Quaternion.identity);
            if (look == null) return;
            look.enabled = shown.Daylight != null || shown.Glows.Length > 0;
            look.Configure(assets.GradeShader, new Vector4(x - halfW, y - halfH, 2f * halfW, 2f * halfH), shown.Glows, shown.Daylight);
        }

        /// <summary>The camera centre along one axis. Insets are the strips covered at the high and low end; the player stays centred between them.</summary>
        static float Follow(float at, int size, float half, float top, float bottom)
        {
            if (size + top + bottom <= 2f * half) return size / 2f + (top - bottom) / 2f;
            return Mathf.Clamp(at + (top - bottom) / 2f, half - bottom, size - half + top);
        }

        Clip NewClip(Texture2D tex, JToken clip, Vector2 pivot, int ppu)
        {
            return new Clip { Fps = (float?)clip["fps"] ?? 8f, Frames = Sprites(tex, clip["rectangles"], pivot, ppu) };
        }

        /// <summary>
        /// Loads one 32x64 character from UiArt/Characters/&lt;name&gt;/manifest.json: clips are keyed "&lt;facing&gt;.&lt;idle|walk&gt;",
        /// the pivot is in top-left art pixels (the foot point), and the sprite draws at the manifest's pixelsPerUnit.
        /// </summary>
        void LoadCharacter(string manifestPath)
        {
            var m = JObject.Parse(File.ReadAllText(manifestPath));
            var folder = Path.GetDirectoryName(manifestPath);
            var tex = LoadTexture(Path.Combine(folder, (string)m["game_input"]));
            var normal = assets != null && assets.NormalShader != null
                ? WorldArtAssets.LoadNormal(Path.Combine(folder, (string)m["normal"] ?? "normals/atlas.png"), tex.width, tex.height)
                : null;
            var material = assets?.NormalMaterial(normal);
            var ppu = (int)m["pixelsPerUnit"];
            var pivot = new Vector2((float)m["pivot"][0] / (int)m["size"][0], 1f - (float)m["pivot"][1] / (int)m["size"][1]);
            var id = (string)m["id"];
            var set = new Dictionary<string, Clip>();
            var top = 0f;
            foreach (var facing in (JObject)m["animations"])
                foreach (var c in (JObject)facing.Value)
                {
                    var clip = NewClip(tex, c.Value, pivot, ppu);
                    clip.NormalMaterial = material;
                    set[facing.Key + "." + c.Key] = clip;
                    foreach (var r in c.Value["rectangles"]) top = Mathf.Max(top, OpaqueTop(tex, r, pivot.y, ppu));
                }
            clips[id] = set;
            heads[id] = top;
            if (m["animations"]?["south"]?["idle"]?["rectangles"]?[0] is JToken idle)
            {
                var face = CharacterFace.Crop(tex, (int)idle["x"], (int)idle["y"], (int)idle["w"], (int)idle["h"]);
                if (face != null) faces[id] = face;
            }
        }

        /// <summary>The head of the character's south idle frame, for the map screen; null for an actor without a character manifest.</summary>
        public Texture2D Face(string actorId) => faces.TryGetValue(actorId, out var face) ? face : null;

        /// <summary>How the map is drawn per world-art.json, including its minimap colours; null for a map with no entry.</summary>
        public MapArt ArtOf(string mapId) => art.Maps.TryGetValue(mapId, out var look) ? look : null;

        /// <summary>World units from the foot point to the highest opaque pixel of one frame.</summary>
        static float OpaqueTop(Texture2D tex, JToken rect, float pivotY, int ppu)
        {
            int x = (int)rect["x"], y = (int)rect["y"], w = (int)rect["w"], h = (int)rect["h"];
            var pixels = tex.GetPixels(x, tex.height - y - h, w, h);
            for (var i = pixels.Length - 1; i >= 0; i--)
                if (pixels[i].a > 0f) return (i / w + 1 - pivotY * h) / ppu;
            return 0f;
        }

        static Sprite[] Sprites(Texture2D tex, JToken rects, Vector2 pivot, int ppu)
        {
            return rects.Select(r =>
            {
                int x = (int)r["x"], y = (int)r["y"], w = (int)r["w"], h = (int)r["h"];
                return Sprite.Create(tex, new Rect(x, tex.height - y - h, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
            }).ToArray();
        }

        Texture2D LoadTexture(string path)
        {
            if (textures.TryGetValue(path, out var tex)) return tex;
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(path) };
            tex.LoadImage(File.ReadAllBytes(path));
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            return textures[path] = tex;
        }
    }
}
