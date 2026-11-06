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
    /// Draws the player's map: a tilemap (wall on blocked cells, door on portal cells, the bench tile on the bed cell until bed art
    /// exists, the map's floor elsewhere), actor sprites
    /// animated from Actor.Facing and Actor.Moving, and an integer-scale pixel camera. Art comes from runtime-manifest.json
    /// (top-left rectangles); world y grows upward.
    /// </summary>
    public sealed class WorldView
    {
        const int MarginPixels = 32;

        sealed class Clip
        {
            public float Fps;
            public Sprite[] Frames;
        }

        sealed class ActorSprite
        {
            public SpriteRenderer Renderer;
            public string Clip;
            public float Time;
        }

        public readonly int PixelsPerUnit;
        public int TopInset;
        readonly string root;
        readonly Camera camera;
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        readonly Dictionary<string, Dictionary<string, Clip>> clips = new Dictionary<string, Dictionary<string, Clip>>();
        readonly Dictionary<string, ActorSprite> actors = new Dictionary<string, ActorSprite>();
        GameObject grid;
        string gridMap;

        public WorldView(string artRoot, Camera camera)
        {
            root = artRoot;
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(artRoot, "runtime-manifest.json")));
            PixelsPerUnit = (int?)manifest["pixelsPerUnit"] ?? 16;
            foreach (var t in (JObject)manifest["tiles"])
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = Sprites(t.Value, t.Value["frames"], new Vector2(0.5f, 0.5f))[0];
                tile.colliderType = Tile.ColliderType.None;
                tiles[t.Key] = tile;
            }
            foreach (var a in (JObject)manifest["actors"])
            {
                var pivot = a.Value["pivot"] is JArray p ? new Vector2((float)p[0], (float)p[1]) : new Vector2(0.5f, 0.0625f);
                var set = new Dictionary<string, Clip>();
                foreach (var c in (JObject)a.Value["animations"]) set[c.Key] = NewClip(a.Value, c.Value, pivot);
                if (a.Value["directionAnimations"] is JObject directions)
                    foreach (var d in directions)
                        foreach (var c in (JObject)d.Value) set[d.Key + "." + c.Key] = NewClip(a.Value, c.Value, pivot);
                clips[a.Key] = set;
            }

            this.camera = camera;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var letterbox = new GameObject("Letterbox").AddComponent<Camera>();
            letterbox.clearFlags = CameraClearFlags.SolidColor;
            letterbox.backgroundColor = Color.black;
            letterbox.cullingMask = 0;
            letterbox.depth = camera.depth - 1f;
        }

        /// <summary>Called once per frame after Game.Update; dt advances the animations (0 while paused).</summary>
        public void Draw(Core.Game game, float dt)
        {
            var map = game.Content.Map(game.MapId);
            if (map.Id != gridMap) BuildMap(map, game.Content.Bed);
            camera.cullingMask = game.Hidden ? 0 : ~0;
            foreach (var actor in game.Actors) DrawActor(actor, map.Id, dt);
            PlaceCamera(map);
        }

        void BuildMap(MapDef map, Spot bed)
        {
            if (grid != null) UnityEngine.Object.Destroy(grid);
            grid = new GameObject("Map:" + map.Id, typeof(Grid));
            var layer = new GameObject("Tiles", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(grid.transform, false);
            var tilemap = layer.GetComponent<Tilemap>();
            var floor = tiles.TryGetValue("floor." + map.Id.Substring("map.".Length), out var own) ? own : tiles["floor.courtyard"];
            for (var y = 0; y < map.Height; y++)
                for (var x = 0; x < map.Width; x++)
                {
                    var isBed = bed.MapId == map.Id && (int)Math.Floor(bed.X) == x && (int)Math.Floor(bed.Y) == y;
                    var tile = !World.Passable(map, x, y) ? tiles["wall.school"] : World.PortalAt(map, x, y) != null ? tiles["door"] : isBed ? tiles["bench"] : floor;
                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            gridMap = map.Id;
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
            var name = actor.Moving ? "walk" : "idle";
            var key = set.ContainsKey(actor.Facing + "." + name) ? actor.Facing + "." + name : name;
            if (key != view.Clip)
            {
                view.Clip = key;
                view.Time = 0f;
            }
            else view.Time += dt;
            var clip = set[key];
            view.Renderer.sprite = clip.Frames[(int)(view.Time * clip.Fps) % clip.Frames.Length];
            view.Renderer.sortingOrder = Mathf.Clamp(2000 - Mathf.RoundToInt((float)actor.Y * PixelsPerUnit), 1, 32000);
            view.Renderer.transform.position = new Vector3(Snap(actor.X), Snap(actor.Y), 0f);
        }

        float Snap(double v) => Mathf.Round((float)v * PixelsPerUnit) / PixelsPerUnit;

        /// <summary>Largest integer scale at which the map plus margin fits below the HUD; drops the inset before going below 1x.</summary>
        void PlaceCamera(MapDef map)
        {
            int sw = Screen.width, sh = Screen.height;
            int baseW = map.Width * PixelsPerUnit + MarginPixels, baseH = map.Height * PixelsPerUnit + MarginPixels;
            var availH = sh - TopInset;
            var scale = availH > 0 ? Math.Min(sw / baseW, availH / baseH) : 0;
            if (scale < 1)
            {
                availH = sh;
                scale = Math.Min(sw / baseW, sh / baseH);
            }
            int vw = Math.Min(baseW, sw), vh = Math.Min(baseH, availH);
            if (scale >= 1)
            {
                vw = baseW * scale;
                vh = baseH * scale;
            }
            else scale = 1;
            camera.pixelRect = new Rect((sw - vw) / 2, (availH - vh) / 2, vw, vh);
            camera.orthographicSize = vh / (2f * PixelsPerUnit * scale);
            camera.transform.SetPositionAndRotation(new Vector3(map.Width / 2f, map.Height / 2f, -10f), Quaternion.identity);
        }

        Clip NewClip(JToken actor, JToken clip, Vector2 pivot)
        {
            return new Clip { Fps = (float?)clip["fps"] ?? 8f, Frames = Sprites(actor, clip["rectangles"], pivot) };
        }

        Sprite[] Sprites(JToken entry, JToken rects, Vector2 pivot)
        {
            var tex = LoadTexture((string)entry["image"]);
            return rects.Select(r =>
            {
                int x = (int)r["x"], y = (int)r["y"], w = (int)r["w"], h = (int)r["h"];
                return Sprite.Create(tex, new Rect(x, tex.height - y - h, w, h), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }).ToArray();
        }

        Texture2D LoadTexture(string image)
        {
            if (textures.TryGetValue(image, out var tex)) return tex;
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = image };
            tex.LoadImage(File.ReadAllBytes(Path.Combine(root, image)));
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            return textures[image] = tex;
        }
    }
}
