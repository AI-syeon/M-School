using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>The one IMGUI style set for the HUD, panels and the dialogue box. Built inside OnGUI because it copies GUI.skin.</summary>
    public sealed class Styles
    {
        public float TextScale = 1f;
        public int HudSize;
        public float Scale;
        public GUIStyle Label, Bold, Button, Box, Error, Body, Speaker, Prompt, Option, Hint, Marker;
        Font font;
        int builtHud = -1, builtBody = -1;

        public void Ensure()
        {
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic" }, 16);
            HudSize = Mathf.Max(11, Mathf.RoundToInt(Screen.height / 40f * TextScale));
            Scale = Mathf.Max(0.3f, Screen.height / 1080f);
            var body = Mathf.Max(12, Mathf.RoundToInt(30f * Scale * TextScale));
            if (HudSize == builtHud && body == builtBody) return;
            builtHud = HudSize;
            builtBody = body;
            Label = new GUIStyle(GUI.skin.label) { font = font, fontSize = HudSize, wordWrap = true };
            Bold = new GUIStyle(Label) { fontStyle = FontStyle.Bold };
            Button = new GUIStyle(GUI.skin.button) { font = font, fontSize = HudSize, wordWrap = true };
            Box = new GUIStyle(GUI.skin.box);
            Error = new GUIStyle(Label);
            Error.normal.textColor = new Color(1f, 0.45f, 0.4f);
            Body = Plain(body, Color.white, TextAnchor.UpperLeft, true);
            Speaker = Plain(Mathf.RoundToInt(body * 0.9f), new Color(1f, 0.88f, 0.55f), TextAnchor.MiddleCenter, false);
            Prompt = Plain(body, DialogueBox.Dim, TextAnchor.UpperLeft, true);
            Option = Plain(body, Color.white, TextAnchor.UpperLeft, true);
            Hint = Plain(Mathf.Max(10, Mathf.RoundToInt(body * 0.6f)), DialogueBox.Dim, TextAnchor.MiddleRight, false);
            Marker = Plain(body, Color.white, TextAnchor.MiddleCenter, false);
        }

        GUIStyle Plain(int size, Color color, TextAnchor anchor, bool wrap)
        {
            var g = new GUIStyle { font = font, fontSize = size, wordWrap = wrap, richText = false, alignment = anchor, clipping = TextClipping.Overflow };
            g.normal.textColor = color;
            return g;
        }
    }

    /// <summary>
    /// Draws Game.Dialogue: a line with paging (Up/Down or the wheel scrolls text that does not fit) or a choice list with a focus.
    /// Uses only screen-space GUI calls so Layout and Repaint never disagree.
    /// </summary>
    public sealed class DialogueBox
    {
        public const int Advance = -2;
        public static readonly Color Dim = new Color(0.78f, 0.82f, 0.90f);
        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.10f, 0.94f);
        static readonly Color BorderColor = new Color(0.62f, 0.72f, 0.95f);
        static readonly Color FocusColor = new Color(0.20f, 0.36f, 0.66f);
        static readonly Color RowColor = new Color(0.12f, 0.14f, 0.20f);

        Dialogue shown;
        float optionScroll;
        int drawnFocus = -1;

        public int Focus { get; private set; }
        public int ScrollLines { get; private set; }

        /// <summary>-1 up, +1 down: scrolls a long line by two text lines or moves the choice focus.</summary>
        public void Navigate(Dialogue d, int delta)
        {
            Sync(d);
            if (d.Choices == null) ScrollLines = Mathf.Max(0, ScrollLines + 2 * delta);
            else Focus = (Focus + delta + d.Choices.Count) % d.Choices.Count;
        }

        /// <summary>Returns the clicked choice index, Advance when a line was clicked, otherwise -1.</summary>
        public int Draw(Dialogue d, string speaker, Styles st)
        {
            Sync(d);
            var e = Event.current;
            if (e.type != EventType.Repaint && e.type != EventType.ScrollWheel && !(e.type == EventType.MouseDown && e.button == 0)) return -1;
            return d.Choices == null ? DrawLine(d, speaker, st, e) : DrawChoices(d, st, e);
        }

        void Sync(Dialogue d)
        {
            if (ReferenceEquals(d, shown)) return;
            shown = d;
            Focus = ScrollLines = 0;
            optionScroll = 0f;
            drawnFocus = -1;
        }

        int DrawLine(Dialogue d, string speaker, Styles st, Event e)
        {
            float w = Screen.width, h = Screen.height, s = st.Scale;
            var margin = 40f * s;
            var pad = 26f * s;
            var footer = st.Hint.fontSize * 1.8f;
            var panelW = Mathf.Min(w - 2f * margin, 1600f * s);
            var panelH = Mathf.Min(Mathf.Max(200f * s, h * 0.34f), h - 2f * margin);
            var panel = new Rect((w - panelW) * 0.5f, h - margin - panelH, panelW, panelH);
            var area = new Rect(panel.x + pad, panel.y + pad, panel.width - 2f * pad, panel.height - 2f * pad - footer);
            if (e.type == EventType.ScrollWheel)
            {
                Navigate(d, e.delta.y > 0f ? 1 : -1);
                e.Use();
                return -1;
            }
            if (e.type == EventType.MouseDown)
            {
                if (!panel.Contains(e.mousePosition)) return -1;
                e.Use();
                return Advance;
            }

            var content = new GUIContent(d.Text);
            var contentH = st.Body.CalcHeight(content, area.width);
            var lineH = Mathf.Max(1f, st.Body.lineHeight);
            var maxScroll = Mathf.Max(0f, contentH - area.height);
            ScrollLines = Mathf.Clamp(ScrollLines, 0, Mathf.CeilToInt(maxScroll / lineH));
            var scrollPx = Mathf.Min(maxScroll, ScrollLines * lineH);

            Fill(panel, PanelColor);
            Frame(panel, BorderColor, Mathf.Max(1f, 2f * s));
            if (!string.IsNullOrEmpty(speaker))
            {
                var size = st.Speaker.CalcSize(new GUIContent(speaker));
                var tag = new Rect(panel.x + pad, panel.y - size.y - 10f * s, size.x + 40f * s, size.y + 14f * s);
                Fill(tag, PanelColor);
                Frame(tag, BorderColor, Mathf.Max(1f, 2f * s));
                GUI.Label(tag, speaker, st.Speaker);
            }
            GUI.BeginClip(area);
            GUI.Label(new Rect(0f, -scrollPx, area.width, contentH), content, st.Body);
            GUI.EndClip();
            if (scrollPx > 0f) GUI.Label(new Rect(area.xMax - 40f * s, area.y, 40f * s, 40f * s), "▲", st.Marker);
            if (scrollPx < maxScroll - 0.5f) GUI.Label(new Rect(area.xMax - 40f * s, area.yMax - 40f * s, 40f * s, 40f * s), "▼", st.Marker);
            GUI.Label(new Rect(area.x, area.yMax + 2f * s, area.width, footer), maxScroll > 0f ? "▲▼ 더 보기    확인" : "확인", st.Hint);
            return -1;
        }

        int DrawChoices(Dialogue d, Styles st, Event e)
        {
            float w = Screen.width, h = Screen.height, s = st.Scale;
            var margin = 40f * s;
            var pad = 28f * s;
            var gap = 10f * s;
            var rowPad = 12f * s;
            var markW = 52f * s;
            var panelW = Mathf.Min(w - 2f * margin, 1000f * s);
            var inner = panelW - 2f * pad;
            var textW = inner - markW - 16f * s;
            var n = d.Choices.Count;
            var heights = new float[n];
            var tops = new float[n];
            var total = 0f;
            for (var i = 0; i < n; i++)
            {
                heights[i] = st.Option.CalcHeight(new GUIContent(d.Choices[i]), textW) + 2f * rowPad;
                tops[i] = total;
                total += heights[i] + gap;
            }
            total = Mathf.Max(0f, total - gap);
            var promptH = string.IsNullOrEmpty(d.Text) ? 0f : st.Prompt.CalcHeight(new GUIContent(d.Text), inner) + 16f * s;
            var footer = st.Hint.fontSize * 1.8f;
            var view = Mathf.Min(total, Mathf.Max(90f * s, h * 0.72f - 2f * pad - promptH - footer));
            var panelH = 2f * pad + promptH + view + footer;
            var panel = new Rect((w - panelW) * 0.5f, Mathf.Max(margin, (h - panelH) * 0.5f), panelW, panelH);
            var list = new Rect(panel.x + pad, panel.y + pad + promptH, inner, view);
            var maxScroll = Mathf.Max(0f, total - view);

            if (drawnFocus != Focus)
            {
                if (tops[Focus] < optionScroll) optionScroll = tops[Focus];
                else if (tops[Focus] + heights[Focus] > optionScroll + view) optionScroll = tops[Focus] + heights[Focus] - view;
                drawnFocus = Focus;
            }
            if (e.type == EventType.ScrollWheel)
            {
                optionScroll = Mathf.Clamp(optionScroll + e.delta.y * 40f * s, 0f, maxScroll);
                e.Use();
                return -1;
            }
            optionScroll = Mathf.Clamp(optionScroll, 0f, maxScroll);
            if (e.type == EventType.MouseDown)
            {
                for (var i = 0; i < n && list.Contains(e.mousePosition); i++)
                {
                    if (!new Rect(list.x, list.y + tops[i] - optionScroll, inner, heights[i]).Contains(e.mousePosition)) continue;
                    e.Use();
                    return i;
                }
                return -1;
            }

            Fill(panel, PanelColor);
            Frame(panel, BorderColor, Mathf.Max(1f, 2f * s));
            if (promptH > 0f) GUI.Label(new Rect(panel.x + pad, panel.y + pad, inner, promptH), d.Text, st.Prompt);
            GUI.BeginClip(list);
            for (var i = 0; i < n; i++)
            {
                var row = new Rect(0f, tops[i] - optionScroll, inner, heights[i]);
                if (row.yMax < 0f || row.y > view) continue;
                Fill(row, i == Focus ? FocusColor : RowColor);
                if (i == Focus)
                {
                    Frame(row, BorderColor, Mathf.Max(1f, 2f * s));
                    GUI.Label(new Rect(row.x + 6f * s, row.y, markW, row.height), "▶", st.Marker);
                }
                GUI.Label(new Rect(row.x + markW, row.y + rowPad, textW, row.height - 2f * rowPad), d.Choices[i], st.Option);
            }
            GUI.EndClip();
            if (optionScroll > 0f) GUI.Label(new Rect(list.xMax - 40f * s, list.y, 40f * s, 40f * s), "▲", st.Marker);
            if (optionScroll < maxScroll - 0.5f) GUI.Label(new Rect(list.xMax - 40f * s, list.yMax - 40f * s, 40f * s, 40f * s), "▼", st.Marker);
            GUI.Label(new Rect(list.x, list.yMax + 4f * s, inner, footer), "▲▼ 이동    확인 결정    취소 닫기", st.Hint);
            return -1;
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Frame(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
    }
}
