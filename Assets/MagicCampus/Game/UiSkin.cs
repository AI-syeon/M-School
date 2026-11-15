using System;
using System.Collections.Generic;
using System.IO;
using MagicCampus.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MagicCampus.Game
{
    public enum Frame { Panel, Button, ButtonSelected, ButtonDisabled, Slot, SlotSelected, QuickSlot, QuickSlotSelected, NameBox, Chip }

    public enum Glyph { Check, Cross, Box, Coin, Sun, Moon, Lock, Star, Arrow }

    /// <summary>
    /// The one look of every screen: palette, the Galmuri pixel fonts, 9-slice frames, pixel glyphs and item icons. Everything is
    /// sized in UI pixels of U screen pixels each, and text only at whole multiples of the fonts' pixel grid (Galmuri11 12 px,
    /// Galmuri9 10 px), so glyphs stay sharp. Frames and icons come from the art kit in StreamingAssets/UiArt when it is there,
    /// otherwise they are drawn here.
    /// </summary>
    public sealed class UiSkin
    {
        public static readonly Color Navy = Hex(0x1b2238, 0.93f), NavyDeep = Hex(0x111627, 0.96f), NavyLight = Hex(0x2b3554),
            Gold = Hex(0xc9a45c), GoldDark = Hex(0x6e5426), GoldBright = Hex(0xf2d48c), Text = Hex(0xf3e6c8), Muted = Hex(0xb6ab90),
            Faint = Hex(0x857f6d), Good = Hex(0x8fd27a), Bad = Hex(0xf2876b), Ink = Hex(0x0a0d17), Shade = new Color(0.03f, 0.04f, 0.08f, 0.55f);

        /// <summary>Rune colors 1-4, so a typed sequence reads at a glance.</summary>
        public static readonly Color[] RuneColors = { Hex(0xe8c25e), Hex(0x6cc7c0), Hex(0xb08be0), Hex(0xef8a6a) };

        public static readonly Dictionary<string, (string Name, Color Color)> Categories = new Dictionary<string, (string, Color)>
        {
            ["material"] = ("재료", Hex(0x8f9bb8)),
            ["seed"] = ("씨앗", Hex(0x7cbf5a)),
            ["food"] = ("음식", Hex(0xe0a050)),
            ["potion"] = ("물약", Hex(0x5f8de8)),
            ["tool"] = ("도구", Hex(0xb48ad6)),
            ["furniture"] = ("가구", Hex(0xb07a50)),
            ["key"] = ("필수품", Hex(0xe8c25e)),
        };

        public float TextScale = 1f;
        /// <summary>Item id to icon, set by the root so toasts can show the item they announce.</summary>
        public Func<string, Texture2D> ItemIcon;
        /// <summary>Screen pixels per UI pixel.</summary>
        public int U { get; private set; } = 2;
        public GUIStyle Body, Bold, Small, Title, Rich;

        Font regular, bold, small;
        int builtU = -1;
        float builtScale = -1f;
        readonly Dictionary<GUIStyle, GUIStyle> lines = new Dictionary<GUIStyle, GUIStyle>();
        readonly Dictionary<Frame, GUIStyle> frames = new Dictionary<Frame, GUIStyle>();
        readonly Dictionary<Glyph, Texture2D> glyphs = new Dictionary<Glyph, Texture2D>();
        readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        Dictionary<string, (Texture2D Tex, int[] Border, int[] Pad)> kitFrames;
        readonly Dictionary<Frame, int[]> pads = new Dictionary<Frame, int[]>();
        Dictionary<string, string> kitIcons;

        public static Color Hex(int rgb, float a = 1f) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, a);

        /// <summary>Rebuilds fonts and frames when the window or the text scale changed. Call at the top of OnGUI.</summary>
        public void Ensure()
        {
            if (regular == null)
            {
                regular = Resources.Load<Font>("Fonts/Galmuri11");
                bold = Resources.Load<Font>("Fonts/Galmuri11-Bold") ?? regular;
                small = Resources.Load<Font>("Fonts/Galmuri9") ?? regular;
                LoadKit();
            }
            var u = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(Screen.width / 480f, Screen.height / 320f) + 0.25f));
            if (u == builtU && Mathf.Approximately(TextScale, builtScale)) return;
            builtU = U = u;
            builtScale = TextScale;
            var k = Mathf.Max(1, Mathf.RoundToInt(u * TextScale));
            Body = Style(regular, 12 * k);
            Bold = Style(bold, 12 * k);
            Small = Style(small, 10 * k);
            Title = Style(bold, 12 * (k + 1));
            Rich = Style(regular, 12 * k);
            Rich.richText = true;
            Rich.wordWrap = false;
            wraps.Clear();
            lines.Clear();
            foreach (var t in frames.Values) UnityEngine.Object.Destroy(t.normal.background);
            frames.Clear();
            foreach (var t in glyphs.Values) UnityEngine.Object.Destroy(t);
            glyphs.Clear();
        }

        static GUIStyle Style(Font font, int size)
        {
            var s = new GUIStyle { font = font, fontSize = size, wordWrap = true, richText = false, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Overflow };
            s.normal.textColor = Text;
            return s;
        }

        /// <summary>The same style without word wrap, for one-line labels.</summary>
        public GUIStyle Line(GUIStyle s)
        {
            if (!s.wordWrap) return s;
            if (!lines.TryGetValue(s, out var l)) lines[s] = l = new GUIStyle(s) { wordWrap = false };
            return l;
        }

        public int Px(float units) => Mathf.RoundToInt(units * U);

        public static Rect Snap(Rect r) => new Rect(Mathf.Round(r.x), Mathf.Round(r.y), Mathf.Round(r.width), Mathf.Round(r.height));

        // ------------------------------------------------------------------ text

        public float LineH(GUIStyle s) => Mathf.Ceil(s.lineHeight);

        public Vector2 Measure(string text, GUIStyle s)
        {
            var v = s.CalcSize(new GUIContent(text));
            return new Vector2(Mathf.Ceil(v.x), Mathf.Ceil(v.y));
        }

        /// <summary>Height of text wrapped to width the way Label draws it.</summary>
        public float Height(string text, GUIStyle s, float width) => string.IsNullOrEmpty(text) ? 0f : Measure(Wrap(text, s, width), Line(s)).y;

        readonly Dictionary<(string, int, int, Font), string> wraps = new Dictionary<(string, int, int, Font), string>();

        /// <summary>
        /// Breaks text into lines at spaces so a Korean word never splits across lines (IMGUI breaks CJK anywhere). A word wider
        /// than the line is broken by character. Existing line breaks are kept.
        /// </summary>
        public string Wrap(string text, GUIStyle s, float width)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var key = (text, Mathf.FloorToInt(width), s.fontSize, s.font);
            if (wraps.TryGetValue(key, out var done)) return done;
            var line = Line(s);
            float W(string t) => line.CalcSize(new GUIContent(t)).x;
            var sb = new System.Text.StringBuilder();
            foreach (var para in text.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                var cur = "";
                foreach (var word in para.Split(' '))
                {
                    var next = cur.Length == 0 ? word : cur + " " + word;
                    if (W(next) <= width) { cur = next; continue; }
                    if (cur.Length > 0)
                    {
                        sb.Append(cur).Append('\n');
                        cur = "";
                    }
                    foreach (var ch in word)
                    {
                        if (cur.Length > 0 && W(cur + ch) > width)
                        {
                            sb.Append(cur).Append('\n');
                            cur = "";
                        }
                        cur += ch;
                    }
                }
                sb.Append(cur);
            }
            if (wraps.Count > 512) wraps.Clear();
            return wraps[key] = sb.ToString();
        }

        /// <summary>Text at a snapped rect; a shadow one UI pixel down-right keeps it readable over the world. Wrapping styles wrap at spaces.</summary>
        public void Label(Rect r, string text, GUIStyle s, Color c, bool shadow = false, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            if (string.IsNullOrEmpty(text)) return;
            r = Snap(r);
            if (s.wordWrap && !s.richText)
            {
                text = Wrap(text, s, r.width);
                s = Line(s);
            }
            if (anchor != TextAnchor.UpperLeft)
            {
                var size = Measure(text, s);
                var x = anchor == TextAnchor.UpperCenter || anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.LowerCenter ? r.x + Mathf.Floor((r.width - size.x) / 2f)
                    : anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight ? r.xMax - size.x : r.x;
                var y = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.MiddleRight ? r.y + Mathf.Floor((r.height - size.y) / 2f)
                    : anchor == TextAnchor.LowerLeft || anchor == TextAnchor.LowerCenter || anchor == TextAnchor.LowerRight ? r.yMax - size.y : r.y;
                r = new Rect(x, y, size.x + 1, size.y);
            }
            if (shadow)
            {
                s.normal.textColor = new Color(Ink.r, Ink.g, Ink.b, c.a * 0.9f);
                GUI.Label(new Rect(r.x + U, r.y + U, r.width, r.height), text, s);
            }
            s.normal.textColor = c;
            GUI.Label(r, text, s);
        }

        /// <summary>The inside of a card drawn with the slot frame, with breathing room beyond the frame's own padding.</summary>
        public Rect CardInner(Rect r)
        {
            var i = Inner(Frame.Slot, r);
            return new Rect(i.x + Px(5), i.y + Px(3), i.width - Px(10), i.height - Px(6));
        }

        public float CardPadH => Pad(Frame.Slot).horizontal + Px(10);
        public float CardPadV => Pad(Frame.Slot).vertical + Px(6);

        // ------------------------------------------------------------------ frames

        public void Fill(Rect r, Color c) => GUI.DrawTexture(Snap(r), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, 0, 0);

        public void Outline(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        public void Draw(Frame f, Rect r, Color? tint = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            if (tint.HasValue) GUI.color = old * tint.Value;
            Style(f).Draw(Snap(r), false, false, false, false);
            if (f == Frame.ButtonSelected || f == Frame.SlotSelected || f == Frame.QuickSlotSelected)
            {
                // The kit's selected sprites differ from normal only by a slightly brighter border; a warm inner glow makes the cursor obvious.
                var inset = Px(f == Frame.ButtonSelected ? 3 : 4);
                var inner = new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, r.height - 2 * inset);
                Fill(inner, new Color(GoldBright.r, GoldBright.g, GoldBright.b, 0.13f));
                Outline(inner, new Color(GoldBright.r, GoldBright.g, GoldBright.b, 0.75f), U);
            }
            GUI.color = old;
        }

        /// <summary>Where content sits inside the frame, in screen pixels.</summary>
        public RectOffset Pad(Frame f)
        {
            Style(f);
            var p = pads[f];
            return new RectOffset(p[0], p[2], p[1], p[3]);
        }

        public Rect Inner(Frame f, Rect r)
        {
            var p = Pad(f);
            return new Rect(r.x + p.left, r.y + p.top, r.width - p.horizontal, r.height - p.vertical);
        }

        GUIStyle Style(Frame f)
        {
            if (frames.TryGetValue(f, out var s)) return s;
            Texture2D tex;
            int[] b;
            if (kitFrames != null && kitFrames.TryGetValue(KitName(f), out var kit))
            {
                tex = Upscale(kit.Tex, U);
                b = new[] { kit.Border[0] * U, kit.Border[1] * U, kit.Border[2] * U, kit.Border[3] * U };
                pads[f] = new[] { kit.Pad[0] * U, kit.Pad[1] * U, kit.Pad[2] * U, kit.Pad[3] * U };
            }
            else
            {
                var border = f == Frame.Panel ? 4 : f == Frame.Chip || f == Frame.NameBox ? 2 : 3;
                tex = Upscale(Procedural(f, border * 2 + 4, border), U);
                b = new[] { border * U, border * U, border * U, border * U };
                var pad = (border + (f == Frame.Panel ? 4 : f == Frame.Chip ? 0 : 2)) * U;
                pads[f] = new[] { pad, pad, pad, pad };
            }
            s = new GUIStyle { border = new RectOffset(b[0], b[2], b[1], b[3]) };
            s.normal.background = tex;
            frames[f] = s;
            return s;
        }

        static string KitName(Frame f)
        {
            switch (f)
            {
                case Frame.Panel: return "ui-panel/normal";
                case Frame.Button: return "ui-button/normal";
                case Frame.ButtonSelected: return "ui-button/selected";
                case Frame.ButtonDisabled: return "ui-button/disabled";
                case Frame.Slot: return "ui-slot/normal";
                case Frame.SlotSelected: return "ui-slot/selected";
                case Frame.QuickSlot: return "ui-quickslot/normal";
                case Frame.QuickSlotSelected: return "ui-quickslot/selected";
                case Frame.NameBox: return "ui-namebox/normal";
                default: return "chip";
            }
        }

        /// <summary>A frame drawn ring by ring: ring 0 is the outer line, corners are cut one pixel.</summary>
        static Texture2D Procedural(Frame f, int size, int border)
        {
            Color fill, r0, r1, r2, r3;
            switch (f)
            {
                case Frame.Panel: r0 = Ink; r1 = Gold; r2 = GoldDark; r3 = Hex(0x26304d, 0.95f); fill = Navy; break;
                case Frame.Button: r0 = Ink; r1 = GoldDark; r2 = Hex(0x3a466c); fill = NavyLight; r3 = fill; break;
                case Frame.ButtonSelected: r0 = Ink; r1 = GoldBright; r2 = Hex(0x56638f); fill = Hex(0x3d4a74); r3 = fill; break;
                case Frame.ButtonDisabled: r0 = Ink; r1 = Hex(0x3b4052); r2 = Hex(0x232a40); fill = Hex(0x1d2337); r3 = fill; break;
                case Frame.Slot: r0 = Hex(0x0b0f1b); r1 = Hex(0x2a3352); r2 = Hex(0x1a2036); fill = Hex(0x161c30, 0.97f); r3 = fill; break;
                case Frame.SlotSelected: r0 = GoldBright; r1 = Gold; r2 = Hex(0x323b60); fill = Hex(0x262e4c, 0.97f); r3 = fill; break;
                case Frame.QuickSlot: case Frame.QuickSlotSelected: r0 = f == Frame.QuickSlot ? Ink : GoldBright; r1 = GoldDark; r2 = Hex(0x1a2036); fill = Hex(0x161c30, 0.93f); r3 = fill; break;
                case Frame.NameBox: r0 = Ink; r1 = Gold; fill = NavyDeep; r2 = r3 = fill; break;
                default: r0 = r1 = r2 = r3 = fill = Color.white; break;
            }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    int cx = Math.Min(x, size - 1 - x), cy = Math.Min(y, size - 1 - y);
                    var ring = Math.Min(Math.Min(cx, cy), cx + cy - 1);
                    var c = ring < 0 ? Color.clear : ring == 0 ? r0 : ring == 1 ? r1 : ring == 2 ? r2 : ring == 3 && border > 3 ? r3 : fill;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        static Texture2D Upscale(Texture2D src, int k)
        {
            var t = new Texture2D(src.width * k, src.height * k, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = src.GetPixels32();
            var dst = new Color32[t.width * t.height];
            for (var y = 0; y < t.height; y++)
                for (var x = 0; x < t.width; x++)
                    dst[y * t.width + x] = px[y / k * src.width + x / k];
            t.SetPixels32(dst);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ glyphs

        static readonly Dictionary<Glyph, string[]> GlyphArt = new Dictionary<Glyph, string[]>
        {
            [Glyph.Check] = new[] { "......##", ".....##.", "##..##..", ".####...", "..##...." },
            [Glyph.Cross] = new[] { "##...##", ".##.##.", "..###..", ".##.##.", "##...##" },
            [Glyph.Box] = new[] { "#######", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#######" },
            [Glyph.Coin] = new[] { "..ooo..", ".oyyyo.", "oywyyyo", "oywyyyo", "oyyyyyo", ".oyyyo.", "..ooo.." },
            [Glyph.Sun] = new[] { "....y....", ".y.....y.", "...yyy...", "..yyyyy..", "y.yyyyy.y", "..yyyyy..", "...yyy...", ".y.....y.", "....y...." },
            [Glyph.Moon] = new[] { "..wwww...", ".www.....", "www......", "www......", "www......", "www......", "www......", ".www.....", "..wwww..." },
            [Glyph.Lock] = new[] { ".###.", "#...#", "#...#", "#####", "##.##", "##.##", "#####" },
            [Glyph.Star] = new[] { "...#...", "...#...", "#######", ".#####.", "..###..", ".##.##.", "##...##" },
            [Glyph.Arrow] = new[] { "#....", "##...", "###..", "####.", "###..", "##...", "#...." },
        };

        Texture2D GlyphTex(Glyph g)
        {
            if (glyphs.TryGetValue(g, out var t)) return t;
            var art = GlyphArt[g];
            var w = 0;
            foreach (var row in art) w = Math.Max(w, row.Length);
            var src = new Texture2D(w, art.Length, TextureFormat.RGBA32, false);
            for (var y = 0; y < art.Length; y++)
                for (var x = 0; x < w; x++)
                {
                    var ch = x < art[y].Length ? art[y][x] : '.';
                    src.SetPixel(x, art.Length - 1 - y, ch == '#' || ch == 'w' ? Color.white : ch == 'y' ? Gold : ch == 'o' ? GoldDark : Color.clear);
                }
            src.Apply();
            glyphs[g] = t = Upscale(src, U);
            UnityEngine.Object.Destroy(src);
            return t;
        }

        public Vector2 GlyphSize(Glyph g) => new Vector2(GlyphTex(g).width, GlyphTex(g).height);

        /// <summary>Draws a glyph centred in r, tinted by c ('#' and 'w' pixels take the tint; gold pixels keep their color under white).</summary>
        public void DrawGlyph(Rect r, Glyph g, Color c, bool shadow = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            var t = GlyphTex(g);
            var at = new Rect(Mathf.Round(r.x + (r.width - t.width) / 2f), Mathf.Round(r.y + (r.height - t.height) / 2f), t.width, t.height);
            if (shadow) GUI.DrawTexture(new Rect(at.x + U, at.y + U, at.width, at.height), t, ScaleMode.StretchToFill, true, 0, new Color(Ink.r, Ink.g, Ink.b, c.a * 0.8f), 0, 0);
            GUI.DrawTexture(at, t, ScaleMode.StretchToFill, true, 0, c, 0, 0);
        }

        /// <summary>A small rounded chip with text, sized to the text. Returns its rect.</summary>
        public Rect Chip(Vector2 at, string text, GUIStyle s, Color fill, Color ink)
        {
            var size = Measure(text, s);
            var r = Snap(new Rect(at.x, at.y, size.x + Px(8), size.y + Px(2)));
            Draw(Frame.Chip, r, fill);
            Label(new Rect(r.x + Px(4), r.y + Px(1), size.x + 1, size.y), text, Line(s), ink);
            return r;
        }

        /// <summary>A key cap such as "E" or "Enter".</summary>
        public Rect Key(Vector2 at, string key, GUIStyle s)
        {
            var size = Measure(key, s);
            var r = Snap(new Rect(at.x, at.y, Mathf.Max(size.x + Px(6), size.y), size.y + Px(2)));
            Draw(Frame.Chip, new Rect(r.x, r.y + U, r.width, r.height), GoldDark);
            Draw(Frame.Chip, r, Text);
            Label(new Rect(r.x, r.y + Px(1), r.width, size.y), key, s, Ink, false, TextAnchor.UpperCenter);
            return r;
        }

        public Rect RuneChip(Vector2 at, int rune, float side)
        {
            var r = Snap(new Rect(at.x, at.y, side, side));
            var c = RuneColors[Mathf.Clamp(rune - 1, 0, 3)];
            Draw(Frame.Chip, new Rect(r.x, r.y + U, r.width, r.height), Ink);
            Draw(Frame.Chip, r, c);
            Fill(new Rect(r.x + Px(2), r.y + Px(1), r.width - Px(4), U), new Color(1f, 1f, 1f, 0.35f));
            Label(r, rune.ToString(), Line(Small), Ink, false, TextAnchor.MiddleCenter);
            return r;
        }

        /// <summary>A framed button. Returns true when it was clicked. Hover lights the frame.</summary>
        public bool Button(Rect r, string text, bool selected = false, bool enabled = true, GUIStyle s = null)
        {
            s = s ?? Body;
            var hover = enabled && r.Contains(Event.current.mousePosition);
            Draw(!enabled ? Frame.ButtonDisabled : selected || hover ? Frame.ButtonSelected : Frame.Button, r);
            Label(r, text, Line(s), enabled ? (selected ? GoldBright : Text) : Faint, false, TextAnchor.MiddleCenter);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ------------------------------------------------------------------ pop-in

        /// <summary>Scale-and-fade for a window opened `age` seconds ago (120 ms). Pair with EndPop.</summary>
        public Matrix4x4 BeginPop(float age, Rect r)
        {
            var old = GUI.matrix;
            var t = Mathf.Clamp01(age / 0.12f);
            if (t >= 1f) return old;
            var e = 1f - (1f - t) * (1f - t);
            var scale = Mathf.Lerp(0.94f, 1f, e);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), r.center);
            GUI.color = new Color(1f, 1f, 1f, e);
            return old;
        }

        public void EndPop(Matrix4x4 old)
        {
            GUI.matrix = old;
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ item icons

        /// <summary>The item's 32×32 icon: the art kit's when present, otherwise a category shape.</summary>
        public Texture2D Icon(Core.Game game, string itemId)
        {
            if (icons.TryGetValue(itemId, out var t)) return t;
            var key = itemId.Replace("item.", "item-").Replace('_', '-');
            if (kitIcons != null && kitIcons.TryGetValue(key, out var file) && File.Exists(file)) t = LoadPng(file);
            if (t == null)
            {
                var cat = game.Content.Item(itemId)?.Category ?? "material";
                t = CategoryIcon(cat, Categories.TryGetValue(cat, out var c) ? c.Color : Categories["material"].Color);
            }
            icons[itemId] = t;
            return t;
        }

        static readonly Dictionary<string, string[]> IconArt = new Dictionary<string, string[]>
        {
            ["material"] = new[] { "................", "......####......", ".....#wwoo#.....", "....#wwoooo#....", "...#wwooooOO#...", "..#wooooooOOO#..", "..#ooooooOOOO#..", "..#oooooOOOOO#..", "...#ooooOOOO#...", "....#oooOOO#....", ".....#ooOO#.....", "......#OO#......", ".......##.......", "................", "................", "................" },
            ["seed"] = new[] { "................", "........##......", ".......#oo#.....", "......#ooo#.....", ".....#oooo#.....", "....#wooooO#....", "...#wwoooooO#...", "...#woooooOO#...", "...#ooooooOO#...", "...#oooooOOO#...", "....#oooOOO#....", ".....#OOOO#.....", "......####......", "................", "................", "................" },
            ["food"] = new[] { "................", "................", "....########....", "...#wwwooooo#...", "..#wwoooooooO#..", "..#woooooooooO#.", ".#ooo#oo#oo#oO#.", ".#oooooooooooO#.", ".#ooooooooooOO#.", ".#oooooooooOOO#.", "..#OOOOOOOOOO#..", "...##########...", "................", "................", "................", "................" },
            ["potion"] = new[] { "................", "......####......", "......#ww#......", ".......##.......", "......#ww#......", "......#..#......", ".....#....#.....", "....#wooooo#....", "...#wooooooO#...", "...#ooooooOO#...", "...#oooooOOO#...", "....#ooOOOO#....", ".....######.....", "................", "................", "................" },
            ["tool"] = new[] { "................", ".....######.....", "....#wwwwww#....", "...#ww....ow#...", "...#w......o#...", "...#w......o#...", "...#ww....oo#...", "....#oooooo#....", ".....######.....", ".......##.......", ".......#O#......", ".......#O#......", ".......#O#......", "........#.......", "................", "................" },
            ["key"] = new[] { "................", "................", "..############..", ".#wwwwwwwwwwww#.", ".#woo##oooooOO#.", ".#wo#ww#ooooOO#.", ".#wo#ww#oo##OO#.", ".#woo##ooooooO#.", ".#wo####o####O#.", ".#oooooooooooO#.", ".#OOOOOOOOOOOO#.", "..############..", "................", "................", "................", "................" },
        };

        static Texture2D CategoryIcon(string cat, Color c)
        {
            var art = IconArt.TryGetValue(cat, out var a) ? a : IconArt["material"];
            var t = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var dark = Color.Lerp(c, Ink, 0.45f);
            var light = Color.Lerp(c, Color.white, 0.55f);
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var ch = art[15 - y / 2][x / 2];
                    t.SetPixel(x, y, ch == '#' ? Ink : ch == 'w' ? light : ch == 'o' ? c : ch == 'O' ? dark : Color.clear);
                }
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ art kit

        /// <summary>
        /// Reads the art kit the build copies to StreamingAssets/UiArt: UI/manifest.json (schema 1: elements, their state PNGs,
        /// slice and contentPadding in source pixels; a fixed-size element without a slice is cut 6 px in) and Items/*.png named
        /// by item id with hyphens (item-lens), optionally mapped by Items/manifest.json. Missing or broken files keep the drawn look.
        /// </summary>
        void LoadKit()
        {
            var root = Path.Combine(GamePaths.StreamingAssets, "UiArt");
            try
            {
                var ui = Path.Combine(root, "UI");
                var manifest = Path.Combine(ui, "manifest.json");
                if (File.Exists(manifest) && JObject.Parse(File.ReadAllText(manifest))["elements"] is JObject elements)
                {
                    kitFrames = new Dictionary<string, (Texture2D, int[], int[])>();
                    foreach (var el in elements.Properties())
                    {
                        if (!(el.Value is JObject e) || !(e["states"] is JObject states)) continue;
                        var pad = Sides(e["contentPadding"]) ?? new[] { 4, 4, 4, 4 };
                        var slice = Sides(e["slice"]) ?? new[] { 6, 6, 6, 6 };
                        foreach (var state in states.Properties())
                        {
                            var path = Path.Combine(ui, (string)state.Value ?? "");
                            var tex = File.Exists(path) ? LoadPng(path) : null;
                            if (tex != null) kitFrames[el.Name + "/" + state.Name] = (tex, slice, pad);
                        }
                    }
                }
                var items = Path.Combine(root, "Items");
                if (Directory.Exists(items))
                {
                    kitIcons = new Dictionary<string, string>();
                    foreach (var png in Directory.GetFiles(items, "*.png")) kitIcons[Path.GetFileNameWithoutExtension(png)] = png;
                    var im = Path.Combine(items, "manifest.json");
                    var map = File.Exists(im) ? JObject.Parse(File.ReadAllText(im)) : null;
                    var entries = map?["items"] as JObject ?? map?["elements"] as JObject ?? map?["icons"] as JObject ?? map;
                    if (entries != null)
                        foreach (var p in entries.Properties())
                        {
                            var v = p.Value;
                            var file = v is JObject o ? (string)(o["file"] ?? o["png"] ?? o["path"] ?? (o["states"] as JObject)?["normal"]) : v.Type == JTokenType.String ? (string)v : null;
                            if (file != null && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) kitIcons[p.Name] = Path.Combine(items, file);
                        }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("UI art kit ignored: " + e.Message);
                kitFrames = null;
                kitIcons = null;
            }
        }

        static int[] Sides(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Integer) return new[] { (int)t, (int)t, (int)t, (int)t };
            if (t is JArray a && a.Count == 4) return new[] { (int)a[0], (int)a[1], (int)a[2], (int)a[3] };
            if (t is JObject o) return new[] { (int?)o["left"] ?? 0, (int?)o["top"] ?? 0, (int?)o["right"] ?? 0, (int?)o["bottom"] ?? 0 };
            return null;
        }

        static Texture2D LoadPng(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            return t.LoadImage(File.ReadAllBytes(file)) ? t : null;
        }
    }
}
