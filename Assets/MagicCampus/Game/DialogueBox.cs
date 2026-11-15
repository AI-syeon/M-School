using System.Collections.Generic;
using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>
    /// The framed dialogue box at the bottom: a speaker plate, a typewriter line (Enter, E or a click reveals it at once, then
    /// advances), a blinking advance mark, paging for lines that do not fit (Up/Down or the wheel), and a choice list with a
    /// cursor above the box. It draws Core's Dialogue and the root's invitation, which uses the same shape.
    /// </summary>
    public sealed class DialogueBox
    {
        public const int Advance = -2;
        const float MinCharsPerSecond = 45f, MaxRevealSeconds = 1.5f;

        Dialogue shown;
        float shownAt, optionScroll;
        int drawnFocus = -1;
        Vector2 lastMouse;

        public int Focus { get; private set; }
        public int ScrollLines { get; private set; }

        /// <summary>True when the whole line is on screen (choices are never typed out).</summary>
        public bool Revealed => shown == null || shown.Choices != null || Shown(shown) >= shown.Text.Length;

        /// <summary>Whether d is fully on screen; starts tracking d if it is new.</summary>
        public bool RevealedFor(Dialogue d)
        {
            Sync(d);
            return Revealed;
        }

        public void RevealAll() => shownAt = -1000f;

        int Shown(Dialogue d)
        {
            var rate = Mathf.Max(MinCharsPerSecond, (d.Text?.Length ?? 0) / MaxRevealSeconds);
            return Mathf.FloorToInt((Time.unscaledTime - shownAt) * rate);
        }

        /// <summary>-1 up, +1 down: scrolls a long line by two text lines or moves the choice focus.</summary>
        public void Navigate(Dialogue d, int delta)
        {
            Sync(d);
            if (d.Choices == null) ScrollLines = Mathf.Max(0, ScrollLines + 2 * delta);
            else Focus = (Focus + delta + d.Choices.Count) % d.Choices.Count;
        }

        public void Sync(Dialogue d)
        {
            if (ReferenceEquals(d, shown)) return;
            shown = d;
            shownAt = Time.unscaledTime;
            Focus = ScrollLines = 0;
            optionScroll = 0f;
            drawnFocus = -1;
        }

        /// <summary>
        /// Screen pixels the box covers from the bottom edge: its margin, line panel, name plate and choice list. The camera keeps
        /// the player above this strip, so the box never has to move.
        /// </summary>
        public float Reserve(Dialogue d, string speaker, UiSkin skin)
        {
            var h = (float)Screen.height;
            var padY = skin.Px(11);
            var lineH = skin.LineH(skin.Body);
            var lines = Mathf.Clamp(Mathf.FloorToInt((h * 0.3f - 2f * padY) / lineH), 2, 3);
            var hasLine = !string.IsNullOrEmpty(d.Text) || !string.IsNullOrEmpty(speaker);
            var covered = skin.Px(8) + (hasLine ? 2f * padY + lines * lineH : 0f);
            if (hasLine && !string.IsNullOrEmpty(speaker)) covered += skin.LineH(skin.Bold) + skin.Px(6);
            if (d.Choices != null)
            {
                var rowH = skin.LineH(skin.Body) + skin.Px(6);
                var rows = Mathf.Min(d.Choices.Count * rowH, Mathf.Floor(h * 0.4f / rowH) * rowH);
                covered += skin.Px(4) + rows + 2f * skin.Px(10);
            }
            return covered;
        }

        /// <summary>Returns the clicked choice index, Advance when a revealed line was clicked, otherwise -1.</summary>
        public int Draw(Dialogue d, string speaker, UiSkin skin)
        {
            Sync(d);
            var e = Event.current;
            if (e.type != EventType.Repaint && e.type != EventType.ScrollWheel && !(e.type == EventType.MouseDown && e.button == 0)) return -1;
            float w = Screen.width, h = Screen.height;
            var margin = skin.Px(8);
            var panelW = Mathf.Min(w - 2f * margin, skin.Px(420));
            var padX = skin.Pad(Frame.Panel).left;
            var padY = skin.Px(11);
            var lineH = skin.LineH(skin.Body);
            var lines = Mathf.Clamp(Mathf.FloorToInt((h * 0.3f - 2f * padY) / lineH), 2, 3);
            var hasLine = !string.IsNullOrEmpty(d.Text) || !string.IsNullOrEmpty(speaker);
            var panelH = hasLine ? 2f * padY + lines * lineH : 0f;
            var plateH = skin.LineH(skin.Bold) + skin.Px(6);
            var hasPlate = !string.IsNullOrEmpty(speaker);
            var y = h - margin - panelH;
            var panel = UiSkin.Snap(new Rect((w - panelW) / 2f, y, panelW, panelH));
            var area = new Rect(panel.x + padX, panel.y + padY, panel.width - 2f * padX, lines * lineH);

            var result = -1;
            if (hasLine) result = DrawLine(d, speaker, skin, e, panel, area, plateH);
            if (d.Choices != null)
            {
                var edge = !hasLine ? h - margin : panel.y - (hasPlate ? plateH : 0) - skin.Px(4);
                var picked = DrawChoices(d, skin, e, panel, edge, margin);
                if (picked != -1) result = picked;
            }
            return result;
        }

        int DrawLine(Dialogue d, string speaker, UiSkin skin, Event e, Rect panel, Rect area, float plateH)
        {
            var text = skin.Wrap(d.Text ?? string.Empty, skin.Body, area.width);
            var contentH = skin.Height(text, skin.Body, area.width);
            var lineH = skin.LineH(skin.Body);
            var maxScroll = Mathf.Max(0f, contentH - area.height);
            ScrollLines = Mathf.Clamp(ScrollLines, 0, Mathf.CeilToInt(maxScroll / lineH));
            var scrollPx = Mathf.Min(maxScroll, ScrollLines * lineH);
            if (e.type == EventType.ScrollWheel && d.Choices == null && panel.Contains(e.mousePosition))
            {
                Navigate(d, e.delta.y > 0f ? 1 : -1);
                e.Use();
                return -1;
            }
            if (e.type == EventType.MouseDown)
            {
                if (d.Choices != null || !panel.Contains(e.mousePosition)) return -1;
                e.Use();
                if (Revealed) return Advance;
                RevealAll();
                return -1;
            }

            skin.Draw(Frame.Panel, panel);
            if (!string.IsNullOrEmpty(speaker))
            {
                var size = skin.Measure(speaker, skin.Line(skin.Bold));
                var plate = UiSkin.Snap(new Rect(panel.x + skin.Px(10), panel.y - plateH + skin.Px(3), size.x + skin.Px(14), plateH));
                skin.Draw(Frame.NameBox, plate);
                skin.Label(plate, speaker, skin.Line(skin.Bold), UiSkin.GoldBright, false, TextAnchor.MiddleCenter);
            }
            var count = d.Choices != null ? text.Length : Mathf.Clamp(Shown(d), 0, text.Length);
            var visible = count >= text.Length || text.IndexOf('<') >= 0 ? (count >= text.Length ? text : text.Substring(0, count))
                : text.Substring(0, count) + "<color=#00000000>" + text.Substring(count) + "</color>";
            GUI.BeginClip(UiSkin.Snap(area));
            skin.Label(new Rect(0f, -scrollPx, area.width, contentH + lineH), visible, skin.Rich, UiSkin.Text);
            GUI.EndClip();
            var mark = new Rect(area.xMax - skin.Px(6), 0, skin.Px(10), skin.Px(8));
            if (scrollPx > 0f) skin.Label(new Rect(mark.x, area.y, mark.width, lineH), "▲", skin.Line(skin.Small), UiSkin.Gold, false, TextAnchor.UpperCenter);
            if (count >= text.Length)
            {
                var more = scrollPx < maxScroll - 0.5f;
                var bob = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.4f ? 0f : skin.U;
                if (more || d.Choices == null)
                    skin.Label(new Rect(mark.x, area.yMax - lineH + skin.Px(4) + bob, mark.width, lineH), "▼", skin.Line(skin.Small), more ? UiSkin.Gold : UiSkin.GoldBright, false, TextAnchor.LowerCenter);
            }
            return -1;
        }

        int DrawChoices(Dialogue d, UiSkin skin, Event e, Rect panel, float edge, float margin)
        {
            var n = d.Choices.Count;
            var style = skin.Line(skin.Body);
            var rowH = skin.LineH(skin.Body) + skin.Px(6);
            var markW = skin.Px(14);
            var pad = skin.Px(10);
            var textW = 0f;
            foreach (var c in d.Choices) textW = Mathf.Max(textW, skin.Measure(c, style).x);
            var listW = Mathf.Min(panel.width > 0 ? panel.width : Screen.width - 2f * margin, textW + markW + 2f * pad + skin.Px(4));
            var available = Mathf.Max(rowH, edge - margin - 2f * pad);
            var view = Mathf.Min(n * rowH, Mathf.Floor(available / rowH) * rowH);
            var right = panel.width > 0 ? panel.xMax : (Screen.width + listW) / 2f;
            var box = UiSkin.Snap(new Rect(right - listW, edge - view - 2f * pad, listW, view + 2f * pad));
            var list = new Rect(box.x + pad, box.y + pad, box.width - 2f * pad, view);
            var maxScroll = Mathf.Max(0f, n * rowH - view);
            if (drawnFocus != Focus)
            {
                if (Focus * rowH < optionScroll) optionScroll = Focus * rowH;
                else if ((Focus + 1) * rowH > optionScroll + view) optionScroll = (Focus + 1) * rowH - view;
                drawnFocus = Focus;
            }
            if (e.type == EventType.ScrollWheel && box.Contains(e.mousePosition))
            {
                optionScroll = Mathf.Clamp(optionScroll + Mathf.Sign(e.delta.y) * rowH, 0f, maxScroll);
                e.Use();
                return -1;
            }
            optionScroll = Mathf.Clamp(optionScroll, 0f, maxScroll);
            var hovered = -1;
            for (var i = 0; i < n; i++)
                if (list.Contains(e.mousePosition) && new Rect(list.x, list.y + i * rowH - optionScroll, list.width, rowH).Contains(e.mousePosition)) hovered = i;
            if (e.type == EventType.MouseDown)
            {
                if (hovered < 0) return -1;
                e.Use();
                return hovered;
            }
            if (hovered >= 0 && e.mousePosition != lastMouse) Focus = drawnFocus = hovered;
            lastMouse = e.mousePosition;

            skin.Draw(Frame.Panel, box);
            GUI.BeginClip(UiSkin.Snap(list));
            for (var i = 0; i < n; i++)
            {
                var row = new Rect(0f, i * rowH - optionScroll, list.width, rowH);
                if (row.yMax < 0f || row.y > view) continue;
                if (i == Focus)
                {
                    skin.Fill(new Rect(row.x, row.y + skin.U, row.width, row.height - 2 * skin.U), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.16f));
                    var nudge = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.4f ? 0 : skin.U;
                    skin.DrawGlyph(new Rect(row.x + nudge, row.y, markW, row.height), Glyph.Arrow, UiSkin.GoldBright);
                }
                skin.Label(new Rect(row.x + markW + skin.Px(2), row.y, row.width - markW, row.height), d.Choices[i], style, i == Focus ? UiSkin.GoldBright : UiSkin.Text, false, TextAnchor.MiddleLeft);
            }
            GUI.EndClip();
            if (optionScroll > 0f) skin.Label(new Rect(box.xMax - skin.Px(12), box.y, skin.Px(8), pad), "▲", skin.Line(skin.Small), UiSkin.Gold, false, TextAnchor.MiddleCenter);
            if (optionScroll < maxScroll - 0.5f) skin.Label(new Rect(box.xMax - skin.Px(12), box.yMax - pad, skin.Px(8), pad), "▼", skin.Line(skin.Small), UiSkin.Gold, false, TextAnchor.MiddleCenter);
            return -1;
        }
    }
}
