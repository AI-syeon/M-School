using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicCampus.Presentation.Dialogue
{
    /// <summary>
    /// High-resolution IMGUI drawing. All sizes derive from Screen.height / ReferenceHeight, never from the world pixel
    /// scale. Only screen-space GUI calls are used (no GUILayout) so Layout/Repaint control counts cannot diverge.
    /// The raster look comes from the built-in white texture only; no art asset is created or required.
    /// </summary>
    internal sealed class DialogueGuiRenderer : IDisposable
    {
        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.10f, 0.94f);
        static readonly Color BorderColor = new Color(0.62f, 0.72f, 0.95f, 1f);
        static readonly Color FocusColor = new Color(0.20f, 0.36f, 0.66f, 1f);
        static readonly Color RowColor = new Color(0.12f, 0.14f, 0.20f, 1f);
        static readonly Color TextColor = Color.white;
        static readonly Color DimColor = new Color(0.78f, 0.82f, 0.90f, 1f);
        static readonly Color NameColor = new Color(1f, 0.88f, 0.55f, 1f);

        Font font;
        bool fontTried;
        string fontKey = string.Empty;
        int builtSize = -1;
        GUIStyle body, speaker, prompt, option, hint, marker;

        public void Draw(DialogueWindow w, DialogueGuiSettings st, Action<int> pointerOption, Action pointerConfirm, Action<int> pointerScroll)
        {
            var e = Event.current;
            if (e == null) return;
            var repaint = e.type == EventType.Repaint;
            var click = e.type == EventType.MouseDown && e.button == 0;
            var wheel = e.type == EventType.ScrollWheel;
            if (!repaint && !click && !wheel) return;
            var s = Mathf.Max(0.3f, Screen.height / Mathf.Max(1f, st.ReferenceHeight));
            EnsureStyles(st, s);
            if (w.Kind == DialogueWindowKind.Say) DrawSay(w, st, s, e, pointerConfirm, pointerScroll);
            else if (w.Kind == DialogueWindowKind.Choose) DrawChoose(w, st, s, e, pointerOption);
        }

        void DrawSay(DialogueWindow w, DialogueGuiSettings st, float s, Event e, Action pointerConfirm, Action<int> pointerScroll)
        {
            float W = Screen.width, H = Screen.height;
            var margin = 40f * s;
            var pad = 26f * s;
            var footer = hint.fontSize * 1.8f;
            var panelW = Mathf.Min(W - 2f * margin, 1600f * s);
            var panelH = Mathf.Min(Mathf.Max(200f * s, H * st.PanelHeightRatio), H - 2f * margin);
            var panel = new Rect((W - panelW) * 0.5f, H - margin - panelH, panelW, panelH);
            var area = new Rect(panel.x + pad, panel.y + pad, panel.width - 2f * pad, panel.height - 2f * pad - footer);

            if (e.type == EventType.ScrollWheel)
            {
                pointerScroll(e.delta.y > 0f ? 1 : -1);
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDown)
            {
                if (panel.Contains(e.mousePosition))
                {
                    e.Use();
                    pointerConfirm();
                }
                return;
            }

            var content = new GUIContent(w.Text);
            var contentH = body.CalcHeight(content, area.width);
            var lineH = Mathf.Max(1f, body.lineHeight);
            var maxScroll = Mathf.Max(0f, contentH - area.height);
            var maxLines = Mathf.CeilToInt(maxScroll / lineH);
            w.ScrollLines = Mathf.Clamp(w.ScrollLines, 0, maxLines);
            var scrollPx = Mathf.Min(maxScroll, w.ScrollLines * lineH);

            Fill(panel, PanelColor);
            Frame(panel, BorderColor, Mathf.Max(1f, 2f * s));

            if (!string.IsNullOrEmpty(w.Speaker))
            {
                var size = speaker.CalcSize(new GUIContent(w.Speaker));
                var tag = new Rect(panel.x + pad, panel.y - size.y - 10f * s, size.x + 40f * s, size.y + 14f * s);
                Fill(tag, PanelColor);
                Frame(tag, BorderColor, Mathf.Max(1f, 2f * s));
                GUI.Label(tag, w.Speaker, speaker);
            }

            GUI.BeginClip(area);
            GUI.Label(new Rect(0f, -scrollPx, area.width, contentH), content, body);
            GUI.EndClip();

            if (scrollPx > 0f) GUI.Label(new Rect(area.xMax - 40f * s, area.y, 40f * s, 40f * s), "▲", marker);
            if (scrollPx < maxScroll - 0.5f) GUI.Label(new Rect(area.xMax - 40f * s, area.yMax - 40f * s, 40f * s, 40f * s), "▼", marker);

            var line = maxScroll > 0f ? st.ScrollHint + "    " + st.ConfirmHint : st.ConfirmHint;
            GUI.Label(new Rect(area.x, area.yMax + 2f * s, area.width, footer), line, hint);
        }

        void DrawChoose(DialogueWindow w, DialogueGuiSettings st, float s, Event e, Action<int> pointerOption)
        {
            float W = Screen.width, H = Screen.height;
            var margin = 40f * s;
            var pad = 28f * s;
            var gap = 10f * s;
            var rowPad = 12f * s;
            var markW = 52f * s;
            var panelW = Mathf.Min(W - 2f * margin, 1000f * s);
            var inner = panelW - 2f * pad;
            var textW = inner - markW - 16f * s;
            var n = w.Options.Length;
            var heights = new float[n];
            var tops = new float[n];
            var total = 0f;
            for (var i = 0; i < n; i++)
            {
                heights[i] = option.CalcHeight(new GUIContent(w.Options[i].Text), textW) + 2f * rowPad;
                tops[i] = total;
                total += heights[i] + gap;
            }
            total = Mathf.Max(0f, total - gap);
            var promptH = string.IsNullOrEmpty(w.Prompt) ? 0f : prompt.CalcHeight(new GUIContent(w.Prompt), inner) + 16f * s;
            var footer = hint.fontSize * 1.8f;
            var maxView = Mathf.Max(90f * s, H * 0.72f - 2f * pad - promptH - footer);
            var view = Mathf.Min(total, maxView);
            var panelH = 2f * pad + promptH + view + footer;
            var panel = new Rect((W - panelW) * 0.5f, Mathf.Max(margin, (H - panelH) * 0.5f), panelW, panelH);
            var list = new Rect(panel.x + pad, panel.y + pad + promptH, inner, view);
            var maxScroll = Mathf.Max(0f, total - view);

            if (w.DrawnFocus != w.Focus && w.Focus >= 0 && w.Focus < n)
            {
                var top = tops[w.Focus];
                var bottom = top + heights[w.Focus];
                if (top < w.OptionScrollPx) w.OptionScrollPx = top;
                else if (bottom > w.OptionScrollPx + view) w.OptionScrollPx = bottom - view;
                w.DrawnFocus = w.Focus;
            }
            if (e.type == EventType.ScrollWheel)
            {
                w.OptionScrollPx = Mathf.Clamp(w.OptionScrollPx + e.delta.y * 40f * s, 0f, maxScroll);
                e.Use();
                return;
            }
            w.OptionScrollPx = Mathf.Clamp(w.OptionScrollPx, 0f, maxScroll);

            if (e.type == EventType.MouseDown)
            {
                if (!list.Contains(e.mousePosition)) return;
                for (var i = 0; i < n; i++)
                {
                    var row = new Rect(list.x, list.y + tops[i] - w.OptionScrollPx, inner, heights[i]);
                    if (row.Contains(e.mousePosition))
                    {
                        e.Use();
                        pointerOption(i);
                        return;
                    }
                }
                return;
            }

            Fill(panel, PanelColor);
            Frame(panel, BorderColor, Mathf.Max(1f, 2f * s));
            if (promptH > 0f) GUI.Label(new Rect(panel.x + pad, panel.y + pad, inner, promptH), w.Prompt, prompt);

            GUI.BeginClip(list);
            for (var i = 0; i < n; i++)
            {
                var row = new Rect(0f, tops[i] - w.OptionScrollPx, inner, heights[i]);
                if (row.yMax < 0f || row.y > view) continue;
                var focused = i == w.Focus;
                Fill(row, focused ? FocusColor : RowColor);
                if (focused) Frame(row, BorderColor, Mathf.Max(1f, 2f * s));
                if (focused) GUI.Label(new Rect(row.x + 6f * s, row.y, markW, row.height), "▶", marker);
                GUI.Label(new Rect(row.x + markW, row.y + rowPad, textW, row.height - 2f * rowPad), w.Options[i].Text, option);
            }
            GUI.EndClip();

            if (w.OptionScrollPx > 0f) GUI.Label(new Rect(list.xMax - 40f * s, list.y, 40f * s, 40f * s), "▲", marker);
            if (w.OptionScrollPx < maxScroll - 0.5f) GUI.Label(new Rect(list.xMax - 40f * s, list.yMax - 40f * s, 40f * s, 40f * s), "▼", marker);
            GUI.Label(new Rect(list.x, list.yMax + 4f * s, inner, footer), st.ChoiceHint, hint);
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

        void EnsureStyles(DialogueGuiSettings st, float s)
        {
            var size = Mathf.Max(12, Mathf.RoundToInt(st.BaseFontSize * s * Mathf.Clamp(st.TextScale, 0.5f, 3f)));
            var key = string.Join("|", st.FontNames ?? new string[0]);
            if (!fontTried || key != fontKey)
            {
                ReleaseFont();
                font = CreateFont(st.FontNames, size);
                fontTried = true;
                fontKey = key;
                builtSize = -1;
            }
            if (builtSize == size && body != null) return;
            builtSize = size;
            body = Make(size, TextColor, TextAnchor.UpperLeft, true);
            speaker = Make(Mathf.RoundToInt(size * 0.9f), NameColor, TextAnchor.MiddleCenter, false);
            prompt = Make(size, DimColor, TextAnchor.UpperLeft, true);
            option = Make(size, TextColor, TextAnchor.UpperLeft, true);
            hint = Make(Mathf.Max(10, Mathf.RoundToInt(size * 0.6f)), DimColor, TextAnchor.MiddleRight, false);
            marker = Make(size, TextColor, TextAnchor.MiddleCenter, false);
        }

        GUIStyle Make(int size, Color color, TextAnchor anchor, bool wrap)
        {
            var g = new GUIStyle();
            g.font = font;
            g.fontSize = size;
            g.wordWrap = wrap;
            g.richText = false;
            g.alignment = anchor;
            g.clipping = TextClipping.Overflow;
            g.normal.textColor = color;
            return g;
        }

        static Font CreateFont(string[] preferred, int size)
        {
            if (preferred == null || preferred.Length == 0) return null;
            string[] installed;
            try { installed = Font.GetOSInstalledFontNames(); }
            catch (Exception) { installed = new string[0]; }
            var picked = new List<string>();
            foreach (var p in preferred)
            {
                foreach (var i in installed)
                {
                    if (string.Equals(p, i, StringComparison.OrdinalIgnoreCase))
                    {
                        picked.Add(i);
                        break;
                    }
                }
            }
            if (picked.Count == 0) picked.AddRange(preferred);
            try { return Font.CreateDynamicFontFromOSFont(picked.ToArray(), size); }
            catch (Exception) { return null; }
        }

        void ReleaseFont()
        {
            if (font == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(font);
            else UnityEngine.Object.DestroyImmediate(font);
            font = null;
        }

        public void Dispose()
        {
            ReleaseFont();
            body = speaker = prompt = option = hint = marker = null;
            fontTried = false;
            builtSize = -1;
        }
    }
}
