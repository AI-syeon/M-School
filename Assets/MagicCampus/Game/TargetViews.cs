using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>
    /// The magic targets of the map on screen, drawn as props: each target's sprite follows its live tags through the look table
    /// in world-art.json (written by derive_world.py from world.json), foot-anchored and depth-sorted like the map's own props.
    /// The aimed target gets a one-pixel outline, a state with a glow keeps a soft halo, and a cast that changes a target plays a
    /// short flash, light bloom and pixel burst in the spell's colour. Nothing here decides what a sprite or colour is.
    /// </summary>
    public sealed class TargetViews
    {
        public const float EffectSeconds = 0.55f;
        const float FlashSeconds = 0.28f;
        const int Particles = 18;
        static readonly Color AimColor = new Color(1f, 0.93f, 0.66f);
        static readonly Vector2[] OutlineSteps = { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) };

        sealed class Cell
        {
            public Vector2 Centre;
            public Sprite Body, Silhouette;
            public Material NormalMaterial;
        }

        sealed class View
        {
            public MagicTargetDef Def;
            public TargetLook Look;
            public Transform Root;
            public Dictionary<string, Cell[]> Sets = new Dictionary<string, Cell[]>();
            public int Height;
            public SpriteRenderer[] Body, Flash, Outline;
            public Material DefaultBodyMaterial;
            public SpriteRenderer Glow, Burst;
            public SpriteRenderer[] Dust;
            public string Sprite, Signature, GlowName;
            public Vector2 GlowAt;
            public float EffectAt = -100f;
            public Color EffectColor;
            public float[] Angle, Speed, Size;
        }

        readonly GameObject root;
        readonly CampusLayout layout;
        readonly WorldArt art;
        readonly WorldArtAssets assets;
        readonly Dictionary<string, View> views = new Dictionary<string, View>();
        readonly int cellSize = WorldMap.TileSize;
        Sprite halo, pixel;
        int seenCasts = -1;

        public TargetViews(string mapId, IEnumerable<MagicTargetDef> targets, CampusLayout layout, WorldArt art, WorldArtAssets assets)
        {
            root = new GameObject("Targets:" + mapId);
            this.layout = layout;
            this.art = art;
            this.assets = assets;
            halo = Halo();
            pixel = Pixel();
            foreach (var target in targets)
            {
                if (layout == null) throw new InvalidDataException("map " + mapId + " has magic targets but no CampusGrid layout to take their sprites from");
                if (!art.Targets.TryGetValue(target.Id, out var look)) throw new InvalidDataException(target.Id + " has no look in world-art.json (run derive_world.py)");
                views[target.Id] = Build(target, look);
            }
        }

        public void Destroy() => UnityEngine.Object.Destroy(root);

        /// <summary>The sprite prop a target shows right now, for probes.</summary>
        public string SpriteOf(string targetId) => views.TryGetValue(targetId, out var v) ? v.Sprite : null;

        /// <summary>Whether a reaction effect is still playing on the target at the given view time.</summary>
        public bool EffectPlaying(string targetId, float time) => views.TryGetValue(targetId, out var v) && time - v.EffectAt < EffectSeconds;

        /// <summary>How many cells tall the target's sprite stands, so labels can sit above it.</summary>
        public int HeightOf(string targetId) => views.TryGetValue(targetId, out var v) ? v.Height : 1;

        public bool GlowShown(string targetId) => views.TryGetValue(targetId, out var v) && v.Glow.enabled;

        // ------------------------------------------------------------------ build

        View Build(MagicTargetDef target, TargetLook look)
        {
            var view = new View { Def = target, Look = look, Root = new GameObject("Target:" + target.Id).transform };
            view.Root.SetParent(root.transform, false);
            view.Root.localPosition = new Vector3(Mathf.Floor((float)target.X), Mathf.Floor((float)target.Y), 0f);
            foreach (var sprite in look.AllSprites)
            {
                var prop = layout.Props.FirstOrDefault(p => p.Id == sprite) ?? throw new InvalidDataException(target.Id + " look uses " + sprite + ", which the layout does not define");
                if (sprite == look.Default) view.Height = prop.VisualBounds.yMax;
                view.Sets[sprite] = layout.Visuals(prop).Select(visual =>
                {
                    var body = visual.Frames[0];
                    return new Cell { Centre = visual.Centre, Body = body, Silhouette = Silhouette(body),
                        NormalMaterial = assets?.NormalMaterial(layout.NormalFor(body)) };
                }).ToArray();
            }
            var cells = view.Sets[look.Default];
            SpriteRenderer Make(string name, Vector3 at)
            {
                var r = new GameObject(name).AddComponent<SpriteRenderer>();
                r.transform.SetParent(view.Root, false);
                r.transform.localPosition = at;
                r.enabled = false;
                return r;
            }
            view.Body = cells.Select(c => Make("body", c.Centre)).ToArray();
            view.DefaultBodyMaterial = view.Body.FirstOrDefault()?.sharedMaterial;
            view.Flash = cells.Select(c => Make("flash", c.Centre)).ToArray();
            view.Outline = cells.SelectMany(c => OutlineSteps.Select(step => Make("outline", c.Centre + step / cellSize))).ToArray();
            view.Glow = Make("glow", Vector3.zero);
            view.Glow.sprite = halo;
            view.Burst = Make("burst", Vector3.zero);
            view.Burst.sprite = halo;
            view.Dust = Enumerable.Range(0, Particles).Select(_ => Make("dust", Vector3.zero)).ToArray();
            foreach (var d in view.Dust) d.sprite = pixel;
            view.Angle = new float[Particles];
            view.Speed = new float[Particles];
            view.Size = new float[Particles];
            var order = WorldMap.DepthOrder(view.Root.localPosition.y);
            for (var i = 0; i < view.Body.Length; i++)
            {
                view.Body[i].sortingOrder = order;
                view.Flash[i].sortingOrder = order + 1;
            }
            foreach (var o in view.Outline) o.sortingOrder = order - 1;
            view.Glow.sortingOrder = order - 2;
            view.Burst.sortingOrder = order + 3;
            foreach (var d in view.Dust) d.sortingOrder = order + 4;
            return view;
        }

        /// <summary>The cell's shape in solid white, for the flash and the aim outline.</summary>
        Sprite Silhouette(Sprite source)
        {
            var rect = source.textureRect;
            int w = (int)rect.width, h = (int)rect.height;
            var pixels = source.texture.GetPixels((int)rect.x, (int)rect.y, w, h);
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 1f, 1f, pixels[i].a);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = source.name + "-silhouette" };
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), cellSize, 0, SpriteMeshType.FullRect);
        }

        /// <summary>A round soft light, one unit across; the renderer's scale sets its size.</summary>
        static Sprite Halo()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "target-halo" };
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x + 0.5f - size / 2f) * (x + 0.5f - size / 2f) + (y + 0.5f - size / 2f) * (y + 0.5f - size / 2f)) / (size / 2f);
                    var a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(a, 1.4f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        Sprite Pixel()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "target-pixel" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), cellSize, 0, SpriteMeshType.FullRect);
        }

        // ------------------------------------------------------------------ per frame

        /// <param name="aimed">The target the player's spell would hit, or null.</param>
        public void Update(Core.Game game, float time, MagicTargetDef aimed)
        {
            var casts = game.Magic.Casts;
            var cast = seenCasts >= 0 && casts > seenCasts && game.Magic.Last != null && game.Magic.Last.Ok ? game.Magic.Last : null;
            seenCasts = casts;
            foreach (var view in views.Values)
            {
                var tags = game.Magic.Tags(view.Def.Id);
                var signature = string.Join(",", tags.OrderBy(t => t, StringComparer.Ordinal));
                var state = view.Look.StateFor(tags);
                var sprite = state?.Sprite ?? view.Look.Default;
                if (view.Signature != null && signature != view.Signature && cast != null && cast.TargetId == view.Def.Id) StartEffect(view, cast.SpellId, time);
                view.Signature = signature;
                if (sprite != view.Sprite) Show(view, sprite);
                DrawGlow(view, state, time);
                DrawOutline(view, aimed == view.Def, time);
                DrawEffect(view, time);
            }
        }

        void Show(View view, string sprite)
        {
            view.Sprite = sprite;
            var cells = view.Sets[sprite];
            for (var i = 0; i < cells.Length; i++)
            {
                view.Body[i].sprite = cells[i].Body;
                view.Body[i].transform.localPosition = cells[i].Centre;
                view.Body[i].sharedMaterial = cells[i].NormalMaterial ?? view.DefaultBodyMaterial;
                view.Body[i].enabled = true;
                view.Flash[i].sprite = cells[i].Silhouette;
                view.Flash[i].transform.localPosition = cells[i].Centre;
                for (var k = 0; k < OutlineSteps.Length; k++)
                {
                    var outline = view.Outline[i * OutlineSteps.Length + k];
                    outline.sprite = cells[i].Silhouette;
                    outline.transform.localPosition = cells[i].Centre + OutlineSteps[k] / cellSize;
                }
            }
        }

        void DrawGlow(View view, LookState state, float time)
        {
            var glow = state != null && state.Glow != null ? art.TargetGlows[state.Glow] : null;
            view.Glow.enabled = glow != null;
            if (glow == null) return;
            var at = state.GlowAt != null && state.GlowAt.Length >= 2 ? new Vector2(state.GlowAt[0], state.GlowAt[1]) : new Vector2(0.5f, 0.5f);
            var color = glow.Rgb;
            var pulse = 0.86f + 0.14f * Mathf.Sin(time * 2.6f);
            view.Glow.color = new Color(color.r, color.g, color.b, glow.Alpha * pulse);
            view.Glow.transform.localPosition = new Vector3(at.x, at.y, 0f);
            view.Glow.transform.localScale = Vector3.one * glow.Radius * 2f;
        }

        void DrawOutline(View view, bool aimed, float time)
        {
            var on = aimed && view.Sprite != null;
            var color = new Color(AimColor.r, AimColor.g, AimColor.b, 0.62f + 0.22f * Mathf.Sin(time * 4.2f));
            foreach (var o in view.Outline)
            {
                o.enabled = on;
                o.color = color;
            }
        }

        void StartEffect(View view, string spellId, float time)
        {
            view.EffectAt = time;
            view.EffectColor = (art.SpellEffects.TryGetValue(spellId, out var e) ? e : art.SpellEffects["default"]).Rgb;
            var rng = new System.Random(unchecked((int)(time * 1000f)) ^ view.Def.Id.Length);
            for (var i = 0; i < Particles; i++)
            {
                view.Angle[i] = (float)(rng.NextDouble() * Math.PI * 2.0);
                view.Speed[i] = 0.9f + (float)rng.NextDouble() * 1.3f;
                view.Size[i] = rng.NextDouble() < 0.5 ? 3f : 2f;
            }
        }

        void DrawEffect(View view, float time)
        {
            var t = time - view.EffectAt;
            var playing = t >= 0f && t < EffectSeconds;
            var flash = playing ? Mathf.Clamp01(1f - t / FlashSeconds) : 0f;
            var tint = Color.Lerp(Color.white, view.EffectColor, 1f - flash);
            foreach (var f in view.Flash)
            {
                f.enabled = flash > 0f && view.Sprite != null;
                f.color = new Color(tint.r, tint.g, tint.b, 0.92f * flash * flash);
            }
            view.Burst.enabled = playing;
            foreach (var d in view.Dust) d.enabled = playing;
            if (!playing) return;
            var u = t / EffectSeconds;
            var centre = BodyCentre(view);
            var ease = 1f - (1f - u) * (1f - u);
            view.Burst.transform.localPosition = centre;
            view.Burst.transform.localScale = Vector3.one * Mathf.Lerp(1.0f, 3.4f, ease);
            view.Burst.color = new Color(view.EffectColor.r, view.EffectColor.g, view.EffectColor.b, 0.9f * (1f - u) * (1f - u));
            for (var i = 0; i < Particles; i++)
            {
                var dir = new Vector2(Mathf.Cos(view.Angle[i]), Mathf.Sin(view.Angle[i]));
                var p = (Vector2)centre + dir * (view.Speed[i] * t * (1f - 0.45f * u)) * 1.1f + new Vector2(0f, 0.35f * t);
                var d = view.Dust[i];
                d.transform.localPosition = new Vector3(Mathf.Round(p.x * cellSize) / cellSize, Mathf.Round(p.y * cellSize) / cellSize, 0f);
                d.transform.localScale = Vector3.one * view.Size[i];
                var color = i % 3 == 0 ? Color.white : view.EffectColor;
                d.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(1.6f * (1f - u)));
            }
        }

        static Vector3 BodyCentre(View view)
        {
            var cells = view.Sets[view.Sprite];
            var top = cells.Max(c => c.Centre.y + c.Body.bounds.extents.y);
            return new Vector3(0.5f, top * 0.5f + 0.05f, 0f);
        }
    }
}
