using System.Collections.Generic;
using System.Linq;
using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    public enum ToastKind { Info, Good, Bad, QuestStep, QuestDone, Item }

    /// <summary>
    /// The always-on game screen over the world: the day card (top left), the request tracker (top right), toasts between them,
    /// the bag's top row as a hotbar with the quick-slot bar or the rune input above it (bottom centre), and world cues: the "E" prompt bubble, the aimed target's name and
    /// tags, and floating spell results. It only reads Core state; the root feeds it toasts and floats.
    /// </summary>
    public sealed class Hud
    {
        public sealed class Toast
        {
            /// <summary>Reward is the "10 크라운" part of a finished request, drawn as a chip so the number never wraps alone.</summary>
            public string Text, ItemId, Reward;
            public ToastKind Kind;
            public float Born;
            public float Life => Kind == ToastKind.QuestDone ? 4.2f : 3.2f;
        }

        sealed class Floater
        {
            public string Text, MapId;
            public double X, Y;
            public Color Color;
            public float Born;
        }

        readonly List<Toast> toasts = new List<Toast>();
        readonly List<Floater> floaters = new List<Floater>();
        float leftEdge, rightEdge;
        int flashSlot = -1;
        float flashAt;
        string[] seenKey;
        int[] seenCount;
        float[] landedAt;

        public IReadOnlyList<Toast> Toasts => toasts;

        public void Push(string text, ToastKind kind, string itemId = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (kind == ToastKind.QuestStep && text.Contains(" · ")) text = text.Substring(text.IndexOf(" · ", System.StringComparison.Ordinal) + 3);
            string reward = null;
            var cut = text.LastIndexOf(" · ", System.StringComparison.Ordinal);
            if (kind == ToastKind.QuestDone && cut > 0 && text.EndsWith("크라운", System.StringComparison.Ordinal))
            {
                reward = text.Substring(cut + 3);
                text = text.Substring(0, cut);
            }
            toasts.Add(new Toast { Text = text, Reward = reward, Kind = kind, ItemId = itemId, Born = Time.unscaledTime });
            while (toasts.Count > 3) toasts.RemoveAt(0);
        }

        public void Float(string text, string mapId, double x, double y, Color color)
        {
            floaters.Add(new Floater { Text = text, MapId = mapId, X = x, Y = y, Color = color, Born = Time.unscaledTime });
        }

        public void FlashSlot(int slot)
        {
            flashSlot = slot;
            flashAt = Time.unscaledTime;
        }

        /// <summary>While a window covers the screen the toasts and the hotbar's landing flashes wait instead of expiring unseen.</summary>
        public void HoldToasts(float dt)
        {
            foreach (var t in toasts) t.Born += dt;
            if (landedAt != null)
                for (var i = 0; i < landedAt.Length; i++) landedAt[i] += dt;
        }

        public void Clear()
        {
            toasts.Clear();
            floaters.Clear();
            seenKey = null;
        }

        static float Now => Time.unscaledTime;

        // ------------------------------------------------------------------ top left: day, clock, wallet

        public void DrawDayCard(Core.Game game, UiSkin skin)
        {
            var s = game.State;
            var m = skin.Px(6);
            var day = (s.Day + 1) + "일차";
            var clock = (s.Minute / 60 % 24).ToString("00") + ":" + (s.Minute % 60).ToString("00");
            var wallet = s.Wallet + " 크라운";
            var bold = skin.Line(skin.Bold);
            var body = skin.Line(skin.Body);
            var small = skin.Line(skin.Small);
            var dayW = skin.Measure(day, bold).x;
            var clockW = skin.Measure(clock, body).x;
            var coin = skin.GlyphSize(Glyph.Coin);
            var walletW = coin.x + skin.Px(4) + skin.Measure(wallet, small).x;
            var innerW = Mathf.Max(dayW + skin.Px(8) + clockW, walletW, skin.Px(70));
            var lineH = skin.LineH(skin.Body);
            var barH = skin.Px(4);
            var smallH = skin.LineH(skin.Small);
            var pad = skin.Pad(Frame.NameBox);
            var innerH = lineH + skin.Px(3) + barH + skin.Px(4) + smallH;
            var card = UiSkin.Snap(new Rect(m, m, innerW + pad.horizontal, innerH + pad.vertical));
            skin.Draw(Frame.NameBox, card);
            var x = card.x + pad.left;
            var y = card.y + pad.top;
            skin.Label(new Rect(x, y, dayW + 2, lineH), day, bold, UiSkin.GoldBright);
            var late = s.Minute >= Clock.MinutesPerDay;
            skin.Label(new Rect(x + innerW - clockW, y, clockW + 2, lineH), clock, body, late ? UiSkin.Bad : UiSkin.Text);
            y += lineH + skin.Px(3);
            var t = Mathf.Clamp01((s.Minute - Clock.DayStart) / (float)(Clock.PassOutAt - Clock.DayStart));
            var night = s.Minute >= 18 * 60;
            var glyph = skin.GlyphSize(Glyph.Sun);
            skin.DrawGlyph(new Rect(x, y + barH / 2f - glyph.y / 2f, glyph.x, glyph.y), night ? Glyph.Moon : Glyph.Sun, night ? UiSkin.Hex(0xb9c6ff) : Color.white);
            var bar = new Rect(x + glyph.x + skin.Px(3), y, innerW - glyph.x - skin.Px(3), barH);
            skin.Fill(bar, UiSkin.GoldDark);
            skin.Fill(new Rect(bar.x + skin.U, bar.y + skin.U, bar.width - 2 * skin.U, bar.height - 2 * skin.U), UiSkin.Hex(0x0e1220));
            skin.Fill(new Rect(bar.x + skin.U, bar.y + skin.U, Mathf.Max(skin.U, (bar.width - 2 * skin.U) * t), bar.height - 2 * skin.U), night ? UiSkin.Hex(0x8f9cf0) : UiSkin.GoldBright);
            y += barH + skin.Px(4);
            skin.DrawGlyph(new Rect(x, y, coin.x, smallH), Glyph.Coin, Color.white);
            skin.Label(new Rect(x + coin.x + skin.Px(4), y, innerW, smallH), wallet, small, UiSkin.Text);
            leftEdge = card.xMax;
        }

        // ------------------------------------------------------------------ top right: requests

        public void DrawTracker(Core.Game game, UiSkin skin)
        {
            var active = game.Quests.Active;
            var m = skin.Px(6);
            var pad = skin.Pad(Frame.NameBox);
            var w = Mathf.Min(skin.Px(150), Screen.width * 0.36f);
            var innerW = w - pad.horizontal;
            var smallH = skin.LineH(skin.Small);
            var check = skin.GlyphSize(Glyph.Check);
            var indent = check.x + skin.Px(4);
            var header = active.Count == 0 ? "진행 중인 의뢰 없음" : "의뢰 " + active.Count;
            var rows = new List<(string Text, float H, int Kind)>();
            foreach (var q in active.Take(3))
            {
                rows.Add((q.Def.Title, skin.Height(q.Def.Title, skin.Small, innerW), 0));
                for (var i = 0; i < q.Def.Steps.Count; i++)
                    rows.Add((q.Def.Steps[i].Label, skin.Height(q.Def.Steps[i].Label, skin.Small, innerW - indent), q.StepDone[i] ? 2 : 1));
            }
            var innerH = smallH + rows.Sum(r => r.H) + rows.Count(r => r.Kind == 0) * skin.Px(3);
            if (active.Count > 3) innerH += smallH;
            var box = UiSkin.Snap(new Rect(Screen.width - m - w, m, w, innerH + pad.vertical));
            skin.Draw(Frame.NameBox, box);
            var x = box.x + pad.left;
            var y = box.y + pad.top;
            skin.Label(new Rect(x, y, innerW, smallH), header, skin.Line(skin.Small), active.Count == 0 ? UiSkin.Muted : UiSkin.Gold);
            skin.Label(new Rect(x, y, innerW, smallH), "J", skin.Line(skin.Small), UiSkin.Faint, false, TextAnchor.UpperRight);
            y += smallH;
            foreach (var r in rows)
            {
                if (r.Kind == 0)
                {
                    y += skin.Px(3);
                    skin.Label(new Rect(x, y, innerW, r.H), r.Text, skin.Small, UiSkin.Text);
                }
                else
                {
                    var done = r.Kind == 2;
                    skin.DrawGlyph(new Rect(x, y, check.x, smallH), done ? Glyph.Check : Glyph.Box, done ? UiSkin.Good : UiSkin.Faint);
                    skin.Label(new Rect(x + indent, y, innerW - indent, r.H), r.Text, skin.Small, done ? UiSkin.Faint : UiSkin.Muted);
                }
                y += r.H;
            }
            if (active.Count > 3) skin.Label(new Rect(x, y, innerW, smallH), "외 " + (active.Count - 3) + "건 · J", skin.Line(skin.Small), UiSkin.Faint);
            rightEdge = box.x;
        }

        // ------------------------------------------------------------------ toasts

        public void DrawToasts(UiSkin skin)
        {
            toasts.RemoveAll(t => Now - t.Born > t.Life);
            if (toasts.Count == 0) return;
            var m = skin.Px(6);
            var left = leftEdge > 0 ? leftEdge + m : m;
            var right = rightEdge > 0 ? rightEdge - m : Screen.width - m;
            var maxW = Mathf.Max(skin.Px(120), right - left);
            var centre = (left + right) / 2f;
            var y = (float)m;
            var pad = skin.Pad(Frame.NameBox);
            foreach (var t in toasts)
            {
                var age = Now - t.Born;
                var style = skin.Small;
                var lead = t.Kind == ToastKind.Info ? 0f : skin.Px(t.ItemId != null ? 18 : 10);
                var inner = maxW - pad.horizontal - lead;
                var textW = Mathf.Min(skin.Measure(t.Text, skin.Line(style)).x + 2, inner);
                var textH = skin.Height(t.Text, style, textW);
                var chipW = t.Reward == null ? 0f : skin.Measure(t.Reward, skin.Line(style)).x + skin.Px(8);
                var chipH = t.Reward == null ? 0f : skin.LineH(style) + skin.Px(2);
                var gap = skin.Px(5);
                var sameLine = t.Reward != null && textW + gap + chipW <= inner && textH <= skin.LineH(style) + 1f;
                var bodyW = t.Reward == null ? textW : sameLine ? textW + gap + chipW : Mathf.Max(textW, chipW);
                var bodyH = t.Reward == null ? textH : sameLine ? Mathf.Max(textH, chipH) : textH + skin.Px(2) + chipH;
                var h = Mathf.Max(bodyH, lead > 0 ? skin.Px(16) : 0) + pad.vertical;
                var w = bodyW + lead + pad.horizontal;
                var slide = Mathf.Clamp01(age / 0.18f);
                var fade = Mathf.Clamp01((t.Life - age) / 0.4f);
                var r = UiSkin.Snap(new Rect(centre - w / 2f, y - (1f - slide * (2f - slide)) * skin.Px(8), w, h));
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, fade * Mathf.Min(1f, slide * 2f));
                skin.Draw(Frame.NameBox, r);
                if (t.Kind == ToastKind.QuestDone)
                {
                    var flash = Mathf.Clamp01(1f - age / 0.6f);
                    if (flash > 0f) skin.Fill(skin.Inner(Frame.NameBox, r), new Color(UiSkin.GoldBright.r, UiSkin.GoldBright.g, UiSkin.GoldBright.b, 0.55f * flash));
                    skin.Outline(r, new Color(UiSkin.GoldBright.r, UiSkin.GoldBright.g, UiSkin.GoldBright.b, 0.5f + 0.5f * flash), skin.U);
                }
                var box = skin.Inner(Frame.NameBox, r);
                if (lead > 0f)
                {
                    var icon = new Rect(box.x, box.y + (box.height - skin.Px(16)) / 2f, lead - skin.Px(2), skin.Px(16));
                    if (t.ItemId != null && skin.ItemIcon != null) GUI.DrawTexture(UiSkin.Snap(new Rect(icon.x, icon.y, skin.Px(16), skin.Px(16))), skin.ItemIcon(t.ItemId));
                    else skin.DrawGlyph(icon, t.Kind == ToastKind.Bad ? Glyph.Cross : t.Kind == ToastKind.QuestDone ? Glyph.Star : Glyph.Check,
                        t.Kind == ToastKind.Bad ? UiSkin.Bad : t.Kind == ToastKind.QuestDone ? UiSkin.GoldBright : UiSkin.Good);
                }
                var color = t.Kind == ToastKind.QuestDone ? UiSkin.GoldBright : t.Kind == ToastKind.Bad ? UiSkin.Bad : UiSkin.Text;
                var bodyTop = box.y + (box.height - bodyH) / 2f;
                skin.Label(new Rect(box.x + lead, sameLine ? bodyTop + (bodyH - textH) / 2f : bodyTop, textW + 2, textH), t.Text, style, color);
                if (t.Reward != null)
                {
                    var at = sameLine ? new Vector2(box.x + lead + textW + gap, bodyTop + (bodyH - chipH) / 2f) : new Vector2(box.x + lead, bodyTop + textH + skin.Px(2));
                    skin.Chip(at, t.Reward, skin.Small, UiSkin.Hex(0x5a4318, 0.95f), UiSkin.GoldBright);
                }
                GUI.color = old;
                y += h + skin.Px(3);
            }
        }

        // ------------------------------------------------------------------ bottom centre: hotbar, then quick slots or rune input above it

        /// <summary>The bag's top row at the very bottom centre, as wide as six bag slots.</summary>
        public static Rect HotbarRect(UiSkin skin)
        {
            var cols = InventoryScreen.Columns;
            var gap = skin.Px(3);
            var margin = skin.Px(6);
            var size = Mathf.Min(skin.Px(40), Mathf.Floor((Screen.width - 2 * margin - (cols - 1) * gap) / (float)cols));
            var w = cols * size + (cols - 1) * gap;
            return UiSkin.Snap(new Rect((Screen.width - w) / 2f, Screen.height - margin - size, w, size));
        }

        /// <summary>
        /// Screen pixels the bottom bars cover, for the camera: the hotbar plus the quick-slot row once unlocked. The rune input
        /// takes the quick slots' place, so opening it never moves the camera.
        /// </summary>
        public float BottomReserve(Core.Game game, UiSkin skin)
        {
            var top = HotbarRect(skin).y;
            if (game.State.QuickCastUnlocked) top -= skin.Px(3) + skin.Px(40);
            return Screen.height - top + skin.Px(4);
        }

        /// <summary>
        /// Remembers the bag's top row and lights a hotbar slot when an item lands in it. quiet skips the light while the bag is
        /// open, where the player rearranges the slots, and for the first look at a fresh game.
        /// </summary>
        public void WatchBag(Core.Game game, bool quiet)
        {
            var bag = game.State.Bag;
            var n = Mathf.Min(InventoryScreen.Columns, bag.Length);
            if (seenKey == null || seenKey.Length != n)
            {
                seenKey = new string[n];
                seenCount = new int[n];
                landedAt = new float[n];
                for (var i = 0; i < n; i++) landedAt[i] = -10f;
                quiet = true;
            }
            for (var i = 0; i < n; i++)
            {
                var lot = bag[i];
                var key = lot == null ? null : lot.ItemId + "|" + lot.Quality;
                var count = lot == null ? 0 : lot.Count;
                if (!quiet && lot != null && (key != seenKey[i] || count > seenCount[i])) landedAt[i] = Now;
                seenKey[i] = key;
                seenCount[i] = count;
            }
        }

        /// <summary>Draws the hotbar. clicked is the slot the player clicked, or -1. Returns the hotbar's top y.</summary>
        public float DrawHotbar(Core.Game game, UiSkin skin, out int clicked)
        {
            clicked = -1;
            var bar = HotbarRect(skin);
            var cols = InventoryScreen.Columns;
            var gap = skin.Px(3);
            var size = (bar.width - (cols - 1) * gap) / cols;
            for (var i = 0; i < cols; i++)
            {
                var r = UiSkin.Snap(new Rect(bar.x + i * (size + gap), bar.y, size, size));
                var age = landedAt != null && i < landedAt.Length ? Now - landedAt[i] : 99f;
                var lit = age < 0.7f;
                InventoryScreen.PaintSlot(r, game, skin, game.State.Bag[i], lit, false);
                if (lit) skin.Fill(skin.Inner(Frame.Slot, r), new Color(UiSkin.GoldBright.r, UiSkin.GoldBright.g, UiSkin.GoldBright.b, 0.3f * (1f - Mathf.Clamp01(age / 0.7f))));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) clicked = i;
            }
            return bar.y;
        }

        /// <summary>
        /// The rune input while the incantation is open, else the quick slots once unlocked. hint is shown for 2.5 s. bottom is the
        /// top of the hotbar; the bar sits just above it. Returns the bar's top y, or bottom when nothing is drawn.
        /// </summary>
        public float DrawMagicBar(Core.Game game, UiSkin skin, string hint, float hintAt, float bottom)
        {
            var magic = game.Magic;
            var m = skin.Px(6);
            bottom -= skin.Px(3);
            if (magic.Open)
            {
                var side = skin.Px(14);
                var gap = skin.Px(3);
                var small = skin.Line(skin.Small);
                var smallH = skin.LineH(skin.Small);
                var exact = magic.Exact;
                var name = exact == null ? "" : game.State.KnownSpells.Contains(exact.Id) ? exact.Name : "모르는 주문";
                var nameW = skin.Measure(name.Length == 0 ? "등불등불" : name, skin.Line(skin.Bold)).x;
                var label = "영창";
                var labelW = skin.Measure(label, small).x;
                var runesW = Magic.MaxRunes * side + (Magic.MaxRunes - 1) * gap;
                var showHint = hint.Length > 0 && Now - hintAt < 2.5f;
                var hintW = showHint ? skin.Measure(hint, small).x : skin.Px(110);
                var pad = skin.Pad(Frame.NameBox);
                var innerW = Mathf.Min(Screen.width - 2 * m - pad.horizontal, Mathf.Max(labelW + skin.Px(6) + runesW + skin.Px(8) + skin.Px(10) + nameW, hintW));
                var innerH = side + skin.Px(4) + smallH;
                var box = UiSkin.Snap(new Rect((Screen.width - innerW - pad.horizontal) / 2f, bottom - innerH - pad.vertical, innerW + pad.horizontal, innerH + pad.vertical));
                skin.Draw(Frame.NameBox, box);
                var x = box.x + pad.left;
                var y = box.y + pad.top;
                skin.Label(new Rect(x, y, labelW + 2, side), label, small, UiSkin.Gold, false, TextAnchor.MiddleLeft);
                x += labelW + skin.Px(6);
                for (var i = 0; i < Magic.MaxRunes; i++)
                {
                    var at = new Vector2(x + i * (side + gap), y);
                    if (i < magic.Runes.Count) skin.RuneChip(at, magic.Runes[i], side);
                    else skin.Draw(Frame.Chip, new Rect(at.x, at.y, side, side), new Color(1f, 1f, 1f, 0.08f));
                }
                x += runesW + skin.Px(8);
                if (name.Length > 0)
                {
                    skin.DrawGlyph(new Rect(x, y, skin.Px(6), side), Glyph.Arrow, UiSkin.Good);
                    skin.Label(new Rect(x + skin.Px(10), y, nameW + 2, side), name, skin.Line(skin.Bold), exact != null && game.State.KnownSpells.Contains(exact.Id) ? UiSkin.Good : UiSkin.Muted, false, TextAnchor.MiddleLeft);
                }
                y += side + skin.Px(4);
                if (showHint) skin.Label(new Rect(box.x, y, box.width, smallH), hint, small, UiSkin.Bad, false, TextAnchor.UpperCenter);
                else
                {
                    var parts = new[] { ("1-4", "룬"), ("Enter", "시전"), ("Q", "취소") };
                    var total = parts.Sum(p => skin.Measure(p.Item1, small).x + skin.Px(6) + skin.Px(3) + skin.Measure(p.Item2, small).x + skin.Px(8)) - skin.Px(8);
                    var hx = box.x + (box.width - total) / 2f;
                    foreach (var (key, what) in parts)
                    {
                        var k = skin.Key(new Vector2(hx, y - skin.Px(1)), key, skin.Small);
                        hx = k.xMax + skin.Px(3);
                        skin.Label(new Rect(hx, y, skin.Measure(what, small).x + 2, smallH), what, small, UiSkin.Muted);
                        hx += skin.Measure(what, small).x + skin.Px(8);
                    }
                }
                return box.y;
            }
            if (!game.State.QuickCastUnlocked) return bottom + skin.Px(3);
            var size = skin.Px(40);
            var spacing = skin.Px(4);
            var count = Magic.QuickSlotCount;
            var x0 = (Screen.width - count * size - (count - 1) * spacing) / 2f;
            var top = bottom - size;
            for (var i = 0; i < count; i++)
            {
                var r = UiSkin.Snap(new Rect(x0 + i * (size + spacing), top, size, size));
                var lit = i == flashSlot && Now - flashAt < 0.35f;
                skin.Draw(lit ? Frame.QuickSlotSelected : Frame.QuickSlot, r);
                skin.Label(new Rect(r.x + skin.Px(4), r.y + skin.Px(4), skin.Px(9), skin.Px(9)), (i + 1).ToString(), skin.Line(skin.Small), UiSkin.GoldBright, false, TextAnchor.MiddleCenter);
                var id = game.State.QuickSlots[i];
                var name = id == null ? "비움" : game.Content.Spell(id)?.Name ?? "?";
                var style = skin.Small;
                var area = new Rect(r.x + skin.Px(2), r.y + skin.Px(14), r.width - skin.Px(4), r.height - skin.Px(17));
                skin.Label(area, name, style, id == null ? UiSkin.Faint : UiSkin.Text, false, TextAnchor.MiddleCenter);
            }
            return top;
        }

        /// <summary>The day-one key strip, centred on the row just above the hotbar (bottom is the hotbar's top).</summary>
        public void DrawKeyHint(UiSkin skin, float bottom)
        {
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var parts = new[] { ("WASD", "이동"), ("E", "말 걸기"), ("Q", "영창"), ("J", "의뢰"), ("Esc", "메뉴") };
            var total = parts.Sum(p => Mathf.Max(skin.Measure(p.Item1, small).x + skin.Px(6), smallH) + skin.Px(3) + skin.Measure(p.Item2, small).x + skin.Px(8)) - skin.Px(8);
            var x = Mathf.Max(skin.Px(6), Mathf.Round((Screen.width - total) / 2f));
            var y = bottom - skin.Px(3) - smallH - skin.Px(2);
            foreach (var (key, what) in parts)
            {
                var k = skin.Key(new Vector2(x, y), key, skin.Small);
                x = k.xMax + skin.Px(3);
                skin.Label(new Rect(x, y + skin.Px(1), skin.Measure(what, small).x + 2, smallH), what, small, UiSkin.Text, true);
                x += skin.Measure(what, small).x + skin.Px(8);
            }
        }

        /// <summary>
        /// A key cap and what it does for a key that only works in this place: beside the hotbar at the bottom right, or on the row
        /// above the hotbar at the right edge when the screen is too narrow to leave room beside it.
        /// </summary>
        public void DrawRoomHint(UiSkin skin, string key, string what)
        {
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var width = skin.Measure(key, small).x + skin.Px(6) + skin.Px(3) + skin.Measure(what, small).x;
            var bar = HotbarRect(skin);
            var x = Screen.width - skin.Px(6) - width;
            var y = Screen.height - skin.Px(6) - smallH - skin.Px(2);
            if (x < bar.xMax + skin.Px(6)) y = bar.y - skin.Px(3) - smallH - skin.Px(2);
            var k = skin.Key(new Vector2(x, y), key, skin.Small);
            skin.Label(new Rect(k.xMax + skin.Px(3), y + skin.Px(1), skin.Measure(what, small).x + 2, smallH), what, small, UiSkin.Text, true);
        }

        // ------------------------------------------------------------------ world cues

        /// <summary>The aimed target's name and tags above it; nothing on the other targets.</summary>
        public void DrawAimed(Core.Game game, WorldView view, UiSkin skin)
        {
            var t = game.Magic.Aim(3);
            if (t == null || t.MapId != game.MapId) return;
            if (floaters.Any(f => f.MapId == t.MapId && f.X == t.X && f.Y == t.Y && Now - f.Born < 1.8f)) return;
            var p = view.GuiPoint(t.X, view.TargetTop(t) + 0.08);
            var tags = game.Magic.Tags(t.Id).OrderBy(x => x).Select(game.Content.TagName).ToList();
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var chipH = smallH + skin.Px(2);
            var chipsW = tags.Sum(x => skin.Measure(x, small).x + skin.Px(8)) + Mathf.Max(0, tags.Count - 1) * skin.Px(2);
            var y = p.y - (tags.Count > 0 ? chipH + skin.Px(2) : 0);
            var x = p.x - chipsW / 2f;
            foreach (var tag in tags)
            {
                var r = skin.Chip(new Vector2(x, y), tag, small, UiSkin.Hex(0x2b3554, 0.95f), UiSkin.Text);
                x = r.xMax + skin.Px(2);
            }
            Bubble(skin, new Vector2(p.x, y - skin.Px(2)), null, t.Name, UiSkin.GoldBright, tags.Count == 0);
        }

        /// <summary>The "E" prompt above whatever E would act on, and a door hint next to a portal.</summary>
        public void DrawPrompts(Core.Game game, WorldView view, UiSkin skin)
        {
            if (game.NearBed)
            {
                Bubble(skin, view.HeadGuiPoint(game.Player), "E", "잠자기", UiSkin.Text, true);
            }
            else
            {
                var invites = game.Interact();
                if (invites.Count > 0)
                {
                    var npc = game.Actor(invites[0].NpcId);
                    Bubble(skin, view.HeadGuiPoint(npc), "E", "대화", UiSkin.Text, true);
                }
            }
            var map = game.Content.Map(game.MapId);
            foreach (var portal in map.Portals)
            {
                if (World.Distance(game.Player, new Spot { MapId = map.Id, X = portal.X, Y = portal.Y }) > 1.6) continue;
                var to = game.State.VisitedMaps.Contains(portal.To.MapId) ? game.Content.MapName(portal.To.MapId) : "???";
                Bubble(skin, view.GuiPoint(portal.X, portal.Y + 0.9), null, "문 · " + to, UiSkin.Muted, true);
            }
        }

        public void DrawFloaters(Core.Game game, WorldView view, UiSkin skin)
        {
            floaters.RemoveAll(f => Now - f.Born > 1.8f);
            foreach (var f in floaters)
            {
                if (f.MapId != game.MapId) continue;
                var age = Now - f.Born;
                var p = view.GuiPoint(f.X, f.Y + 0.3) - new Vector2(0f, Mathf.Round(age / 1.8f * skin.Px(10)));
                var a = Mathf.Clamp01((1.8f - age) / 0.5f) * Mathf.Clamp01(age / 0.12f);
                var style = skin.Line(skin.Small);
                var size = skin.Measure(f.Text, style);
                skin.Label(new Rect(p.x - size.x / 2f, p.y - size.y, size.x + 2, size.y), f.Text, style, new Color(f.Color.r, f.Color.g, f.Color.b, a), true);
            }
        }

        /// <summary>A name-box bubble whose bottom centre sits at `at`, with an optional key cap and a small tail.</summary>
        public static void Bubble(UiSkin skin, Vector2 at, string key, string text, Color color, bool tail)
        {
            var small = skin.Line(skin.Small);
            var pad = skin.Pad(Frame.NameBox);
            var textSize = skin.Measure(text, small);
            var keyW = key == null ? 0f : Mathf.Max(skin.Measure(key, small).x + skin.Px(6), textSize.y) + skin.Px(4);
            var w = keyW + textSize.x + pad.horizontal;
            var h = textSize.y + pad.vertical;
            var r = UiSkin.Snap(new Rect(at.x - w / 2f, at.y - h - (tail ? skin.Px(3) : 0), w, h));
            if (tail)
            {
                skin.Fill(new Rect(Mathf.Round(at.x) - skin.Px(2), r.yMax - skin.U, skin.Px(4), skin.Px(2)), UiSkin.Gold);
                skin.Fill(new Rect(Mathf.Round(at.x) - skin.Px(1), r.yMax + skin.U, skin.Px(2), skin.Px(2)), UiSkin.Gold);
            }
            skin.Draw(Frame.NameBox, r);
            var inner = skin.Inner(Frame.NameBox, r);
            if (key != null) skin.Key(new Vector2(inner.x, inner.y + (inner.height - textSize.y - skin.Px(2)) / 2f), key, skin.Small);
            skin.Label(new Rect(inner.x + keyW, inner.y, textSize.x + 2, inner.height), text, small, color, false, TextAnchor.MiddleLeft);
        }
    }
}
