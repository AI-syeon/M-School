using System;
using System.Collections.Generic;
using System.Linq;
using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>What GameRoot draws each OnGUI: HUD, world cues, the tabbed window, dialogue, menu, sleep prompt and day report.</summary>
    public sealed partial class GameRoot
    {
        float journalScroll, journalHeight, bookScroll, bookHeight;
        int scrolledTo = -1;

        void OnGUI()
        {
            skin.Ensure();
            if (View != null)
            {
                View.TopInset = 0;
                View.BottomInset = 0;
                View.LeftInset = 0;
            }
            if (Game == null)
            {
                if (Error != null) DrawError();
                return;
            }
            var free = panel == Panel.None && !MenuOpen && report == null && Game.Dialogue == null && Game.Event == null;
            Hud.WatchBag(Game, panel == Panel.Book);
            if (!Game.Hidden && View != null && panel != Panel.Arrange) View.BottomInset = Hud.BottomReserve(Game, skin);
            if (!Game.Hidden && View != null)
            {
                if (free)
                {
                    Hud.DrawPrompts(Game, View, skin);
                    Hud.DrawAimed(Game, View, skin);
                }
            }
            var overlay = panel == Panel.Book || MenuOpen || report != null;
            var talking = Game.Dialogue != null || panel == Panel.Invite;
            if (!overlay && !talking && panel != Panel.Arrange)
            {
                Hud.DrawDayCard(Game, skin);
                if (Game.Event == null) Hud.DrawTracker(Game, skin);
            }
            if (free)
            {
                var hotbarTop = Hud.DrawHotbar(Game, skin, out var clickedSlot);
                if (Game.Furniture.IsArrangeable(Game.MapId)) Hud.DrawRoomHint(skin, "F", "가구 배치");
                Hud.DrawMagicBar(Game, skin, barHint, barHintAt, hotbarTop);
                if (Game.State.Day == 0 && !Game.Magic.Open && !Game.State.QuickCastUnlocked) Hud.DrawKeyHint(skin, hotbarTop);
                if (clickedSlot >= 0)
                {
                    OpenBook(Tab.Bag);
                    BagPanel.Select(clickedSlot);
                }
            }
            if (panel == Panel.Book) DrawBook();
            else if (panel == Panel.Sleep) DrawSleep();
            else if (panel == Panel.Arrange)
            {
                ArrangePanel.Draw(Game, skin, View);
                if (View != null) View.LeftInset = ArrangePanel.PanelWidth;
            }
            else if (panel == Panel.Invite && invite != null)
            {
                if (!Game.Hidden && View != null) View.BottomInset = Box.Reserve(invite, Name(invite.Speaker), skin);
                var clicked = Box.Draw(invite, Name(invite.Speaker), skin);
                if (clicked >= 0) Pick(invite, clicked);
            }
            var d = Game.Dialogue;
            if (d != null && panel != Panel.Invite)
            {
                var speaker = d.Speaker == null ? null : Name(d.Speaker);
                if (!Game.Hidden && View != null) View.BottomInset = Box.Reserve(d, speaker, skin);
                var clicked = Box.Draw(d, speaker, skin);
                if (clicked == DialogueBox.Advance) Game.Advance();
                else if (clicked >= 0) Game.Choose(clicked);
            }
            if (!overlay && !talking) Hud.DrawToasts(skin);
            if (!Game.Hidden && View != null && !overlay) Hud.DrawFloaters(Game, View, skin);
            if (MenuOpen) DrawMenu();
            DrawDayReport();
            if (View != null && View.FadeAlpha > 0f) skin.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.02f, 0.04f, View.FadeAlpha));
            if (Error != null) DrawError();
        }

        void Dim(float alpha = 0.55f) => skin.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.07f, alpha));

        // ------------------------------------------------------------------ the tabbed window

        Rect Centered(float units, float unitsH)
        {
            var m = skin.Px(8);
            var w = Mathf.Min(Screen.width - 2 * m, skin.Px(units));
            var h = Mathf.Min(Screen.height - 2 * m, skin.Px(unitsH));
            return UiSkin.Snap(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h));
        }

        void DrawBook()
        {
            Dim(0.45f);
            var r = Centered(452, 304);
            var pop = skin.BeginPop(Time.unscaledTime - panelAt, r);
            skin.Draw(Frame.Panel, r);
            var inner = skin.Inner(Frame.Panel, r);
            var body = skin.Line(skin.Body);
            var small = skin.Line(skin.Small);
            var tabH = skin.LineH(skin.Body) + skin.Px(10);
            var x = inner.x;
            foreach (var t in Tabs)
            {
                var key = t.Key.ToString().ToUpperInvariant();
                var w = skin.Measure(t.Name, body).x + skin.Measure(key, small).x + skin.Px(26);
                var tr = new Rect(x, inner.y, w, tabH);
                var on = t.Tab == tab;
                var hover = tr.Contains(Event.current.mousePosition);
                skin.Draw(on ? Frame.ButtonSelected : Frame.Button, tr);
                skin.Label(new Rect(tr.x + skin.Px(9), tr.y, w, tr.height), t.Name, body, on ? UiSkin.GoldBright : hover ? UiSkin.Text : UiSkin.Muted, false, TextAnchor.MiddleLeft);
                skin.Label(new Rect(tr.x, tr.y, tr.width - skin.Px(9), tr.height), key, small, on ? UiSkin.Gold : UiSkin.Faint, false, TextAnchor.MiddleRight);
                if (GUI.Button(tr, GUIContent.none, GUIStyle.none) && !on) SwitchTab(t.Tab);
                x += w + skin.Px(3);
            }
            var close = new Rect(inner.xMax - tabH, inner.y, tabH, tabH);
            if (skin.Button(close, "×")) ClosePanel();
            skin.Label(new Rect(inner.x, inner.y, close.x - inner.x - skin.Px(6), tabH), "Esc 닫기", small, UiSkin.Faint, false, TextAnchor.MiddleRight);
            var lineY = inner.y + tabH + skin.Px(4);
            skin.Fill(new Rect(inner.x, lineY, inner.width, skin.U), UiSkin.GoldDark);
            var content = new Rect(inner.x, lineY + skin.Px(6), inner.width, inner.yMax - lineY - skin.Px(6));
            switch (tab)
            {
                case Tab.Journal: DrawJournal(content); break;
                case Tab.Spellbook: DrawSpellbook(content); break;
                case Tab.Bag: BagPanel.Draw(content, Game, skin); break;
                case Tab.Map: MapPanel.Draw(content, Game, skin, View); break;
            }
            skin.EndPop(pop);
        }

        /// <summary>A vertical scroll area: the body draws at (0, y) inside it and returns its height. The wheel scrolls it.</summary>
        float Scroll(Rect area, ref float offset, float contentH, Func<float, float> body)
        {
            var e = Event.current;
            var max = Mathf.Max(0f, contentH - area.height);
            if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition))
            {
                offset += Mathf.Sign(e.delta.y) * skin.LineH(skin.Body) * 2f;
                e.Use();
            }
            offset = Mathf.Clamp(offset, 0f, max);
            GUI.BeginClip(UiSkin.Snap(area));
            var h = body(-Mathf.Round(offset));
            GUI.EndClip();
            if (max > 0f)
            {
                var track = new Rect(area.xMax - skin.Px(2), area.y, skin.Px(2), area.height);
                skin.Fill(track, new Color(0f, 0f, 0f, 0.35f));
                var thumbH = Mathf.Max(skin.Px(10), area.height * area.height / contentH);
                skin.Fill(new Rect(track.x, area.y + (area.height - thumbH) * offset / max, track.width, thumbH), UiSkin.GoldDark);
            }
            return h;
        }

        void Heading(Rect r, string text, string count = null)
        {
            skin.Label(r, text, skin.Line(skin.Bold), UiSkin.Gold);
            if (count != null) skin.Label(new Rect(r.x + skin.Measure(text, skin.Line(skin.Bold)).x + skin.Px(5), r.y + skin.Px(1), r.width, r.height), count, skin.Line(skin.Small), UiSkin.Faint);
        }

        // ------------------------------------------------------------------ journal: requests and appointments

        void DrawJournal(Rect c)
        {
            var gap = skin.Px(10);
            var leftW = Mathf.Floor((c.width - gap) * 0.6f);
            var left = new Rect(c.x, c.y, leftW, c.height);
            var right = new Rect(c.x + leftW + gap, c.y, c.width - leftW - gap, c.height);
            var headH = skin.LineH(skin.Bold) + skin.Px(4);
            var active = Game.Quests.Active;
            var done = Game.Quests.Completed;
            Heading(new Rect(left.x, left.y, left.width, headH), "진행 중인 의뢰", active.Count + "건");
            var listArea = new Rect(left.x, left.y + headH, left.width, left.height - headH);
            journalHeight = Scroll(listArea, ref journalScroll, journalHeight, y0 =>
            {
                var y = y0;
                var w = listArea.width - skin.Px(6);
                if (active.Count == 0)
                {
                    skin.Label(new Rect(0, y, w, skin.LineH(skin.Small)), "지금 맡은 의뢰가 없어요.", skin.Small, UiSkin.Muted);
                    y += skin.LineH(skin.Small) + skin.Px(6);
                }
                foreach (var q in active) y += QuestCard(new Rect(0, y, w, 0), q) + skin.Px(5);
                if (done.Count > 0)
                {
                    y += skin.Px(4);
                    skin.Label(new Rect(0, y, w, skin.LineH(skin.Small)), "완료한 의뢰 " + done.Count + "건", skin.Line(skin.Small), UiSkin.Faint);
                    y += skin.LineH(skin.Small);
                    foreach (var q in done)
                    {
                        skin.DrawGlyph(new Rect(0, y, skin.Px(10), skin.LineH(skin.Small)), Glyph.Check, UiSkin.Good);
                        skin.Label(new Rect(skin.Px(13), y, w - skin.Px(13), skin.LineH(skin.Small)), q.Title, skin.Line(skin.Small), UiSkin.Faint);
                        y += skin.LineH(skin.Small);
                    }
                }
                return y - y0;
            });
            DrawAppointments(right, headH);
        }

        /// <summary>One request card: title and giver, description, steps with check marks, reward. Returns its height.</summary>
        float QuestCard(Rect r, QuestProgress q)
        {
            var w = r.width - skin.CardPadH;
            var small = skin.Small;
            var smallH = skin.LineH(skin.Small);
            var boldH = skin.LineH(skin.Bold);
            var giver = q.Def.Giver == null ? null : Name(q.Def.Giver) + "의 부탁";
            var giverW = giver == null ? 0f : skin.Measure(giver, skin.Line(small)).x + skin.Px(4);
            var descH = string.IsNullOrEmpty(q.Def.Description) ? 0f : skin.Height(q.Def.Description, small, w) + skin.Px(3);
            var check = skin.Px(13);
            var stepH = q.Def.Steps.Select(s => skin.Height(s.Label, small, w - check)).ToList();
            var reward = RewardText(q.Def.Reward);
            var h = skin.CardPadV + boldH + skin.Px(2) + descH + stepH.Sum() + (reward.Length > 0 ? smallH + skin.Px(3) : 0);
            var box = new Rect(r.x, r.y, r.width, h);
            skin.Draw(Frame.Slot, box);
            var inner = skin.CardInner(box);
            var x = inner.x;
            var y = inner.y;
            skin.Label(new Rect(x, y, w - giverW, boldH), q.Def.Title, skin.Line(skin.Bold), UiSkin.Text);
            if (giver != null) skin.Label(new Rect(x, y + skin.Px(1), w, boldH), giver, skin.Line(small), UiSkin.Faint, false, TextAnchor.UpperRight);
            y += boldH + skin.Px(2);
            if (descH > 0f)
            {
                skin.Label(new Rect(x, y, w, descH), q.Def.Description, small, UiSkin.Muted);
                y += descH;
            }
            for (var i = 0; i < q.Def.Steps.Count; i++)
            {
                var ok = q.StepDone[i];
                skin.DrawGlyph(new Rect(x, y, skin.Px(10), smallH), ok ? Glyph.Check : Glyph.Box, ok ? UiSkin.Good : UiSkin.Faint);
                skin.Label(new Rect(x + check, y, w - check, stepH[i]), q.Def.Steps[i].Label, small, ok ? UiSkin.Faint : UiSkin.Text);
                y += stepH[i];
            }
            if (reward.Length > 0)
            {
                y += skin.Px(3);
                var coin = skin.GlyphSize(Glyph.Coin);
                skin.DrawGlyph(new Rect(x, y, coin.x, smallH), Glyph.Coin, Color.white);
                skin.Label(new Rect(x + coin.x + skin.Px(4), y, w, smallH), reward, skin.Line(small), UiSkin.GoldBright);
                var ix = x + coin.x + skin.Px(8) + skin.Measure(reward, skin.Line(small)).x;
                foreach (var item in q.Def.Reward?.Items ?? new List<QuestItemReward>())
                {
                    GUI.DrawTexture(UiSkin.Snap(new Rect(ix, y + (smallH - 32) / 2f, 32, 32)), skin.Icon(Game, item.ItemId));
                    ix += 34;
                }
            }
            return h;
        }

        string RewardText(QuestRewardDef reward)
        {
            if (reward == null) return "";
            var parts = new List<string>();
            if (reward.Crowns > 0) parts.Add(reward.Crowns + " 크라운");
            foreach (var item in reward.Items ?? new List<QuestItemReward>()) parts.Add(InventoryScreen.ItemName(Game, item.ItemId) + " ×" + item.Count);
            return parts.Count == 0 ? "" : "보상 " + string.Join(" · ", parts);
        }

        void DrawAppointments(Rect r, float headH)
        {
            var rows = Rows();
            selected = Mathf.Clamp(selected, 0, Math.Max(0, rows.Count - 1));
            Heading(new Rect(r.x, r.y, r.width, headH), "약속", rows.Count + "건");
            var y = r.y + headH;
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            if (rows.Count == 0) skin.Label(new Rect(r.x, y, r.width, smallH), "예정된 약속이 없어요.", skin.Small, UiSkin.Muted);
            var rowH = skin.LineH(skin.Body) + skin.Px(10);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = new Rect(r.x, y, r.width, rowH);
                var status = Game.State.Appointments[rows[i]];
                skin.Draw(i == selected ? Frame.ButtonSelected : Frame.Button, row);
                var chip = status == Appointment.Offered ? "제안" : "약속함";
                var chipW = skin.Measure(chip, small).x + skin.Px(8);
                skin.Label(new Rect(row.x + skin.Px(9), row.y, row.width - chipW - skin.Px(14), row.height), Label(rows[i]), skin.Line(skin.Body), i == selected ? UiSkin.GoldBright : UiSkin.Text, false, TextAnchor.MiddleLeft);
                skin.Chip(new Vector2(row.xMax - chipW - skin.Px(7), row.y + (row.height - smallH - skin.Px(2)) / 2f), chip, small,
                    status == Appointment.Offered ? UiSkin.Hex(0x3b4466) : UiSkin.Hex(0x3d6b3a), status == Appointment.Offered ? UiSkin.Muted : UiSkin.Text);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) selected = i;
                y += rowH + skin.Px(3);
            }
            if (rows.Count > 0)
            {
                y += skin.Px(4);
                var pick = rows[selected];
                var offered = Game.State.Appointments[pick] == Appointment.Offered;
                var bh = skin.LineH(skin.Body) + skin.Px(10);
                skin.Label(new Rect(r.x, y, r.width, smallH), "고른 약속 · " + Label(pick), skin.Line(skin.Small), UiSkin.Faint);
                y += smallH + skin.Px(3);
                if (skin.Button(new Rect(r.x, y, r.width, bh), offered ? "지금 같이 하기 · Enter" : "지금 시작 · Enter")) AcceptAndStart(pick);
                y += bh + skin.Px(3);
                if (offered && skin.Button(new Rect(r.x, y, r.width, bh), "약속만 잡기"))
                {
                    Game.Accept(pick);
                    SetStatus("약속을 잡았습니다 · " + Label(pick), ToastKind.Good);
                }
            }
            HintRow(new Rect(r.x, r.yMax - smallH, r.width, smallH), ("↑↓", "고르기"), ("Enter", "지금 시작"));
        }

        void HintRow(Rect r, params (string Key, string What)[] parts)
        {
            var small = skin.Line(skin.Small);
            var x = r.x;
            foreach (var (key, what) in parts)
            {
                var k = skin.Key(new Vector2(x, r.y - skin.Px(1)), key, skin.Small);
                x = k.xMax + skin.Px(3);
                var w = skin.Measure(what, small).x;
                skin.Label(new Rect(x, r.y, w + 2, r.height), what, small, UiSkin.Muted);
                x += w + skin.Px(9);
            }
        }

        // ------------------------------------------------------------------ spellbook: cards and quick slots

        void DrawSpellbook(Rect c)
        {
            var spells = LearnedSpells();
            selected = Mathf.Clamp(selected, 0, Math.Max(0, spells.Count - 1));
            var state = Game.State;
            var editable = state.QuickCastUnlocked && Game.Event == null;
            var gap = skin.Px(10);
            var sideW = skin.Px(112);
            var cards = new Rect(c.x, c.y, c.width - sideW - gap, c.height);
            var side = new Rect(cards.xMax + gap, c.y, sideW, c.height);
            var headH = skin.LineH(skin.Bold) + skin.Px(4);
            Heading(new Rect(cards.x, cards.y, cards.width, headH), "배운 주문", spells.Count + "개");
            var hintH = skin.LineH(skin.Small) + skin.Px(6);
            var area = new Rect(cards.x, cards.y + headH, cards.width, cards.height - headH - hintH);
            var colGap = skin.Px(5);
            var colW = Mathf.Floor((area.width - skin.Px(6) - colGap) / 2f);
            var smallH = skin.LineH(skin.Small);
            var boldH = skin.LineH(skin.Bold);
            var chip = skin.LineH(skin.Small) + skin.Px(2);
            var pad = skin.Pad(Frame.Slot);
            float CardH(SpellDef s) => skin.CardPadV + boldH + skin.Px(3) + chip + skin.Px(3) + Math.Max(1, s.Requirements.Count) * smallH;
            var rowTops = new List<float>();
            var top = 0f;
            for (var i = 0; i < spells.Count; i += 2)
            {
                rowTops.Add(top);
                top += Mathf.Max(CardH(spells[i]), i + 1 < spells.Count ? CardH(spells[i + 1]) : 0f) + colGap;
            }
            if (spells.Count > 0 && scrolledTo != selected)
            {
                scrolledTo = selected;
                var row = selected / 2;
                var rowH = CardH(spells[selected]);
                if (rowTops[row] < bookScroll) bookScroll = rowTops[row];
                else if (rowTops[row] + rowH > bookScroll + area.height) bookScroll = rowTops[row] + rowH - area.height;
            }
            bookHeight = Scroll(area, ref bookScroll, top, y0 =>
            {
                for (var i = 0; i < spells.Count; i++)
                {
                    var s = spells[i];
                    var rowH = Mathf.Max(CardH(spells[i - i % 2]), i - i % 2 + 1 < spells.Count ? CardH(spells[i - i % 2 + 1]) : 0f);
                    var card = new Rect(i % 2 * (colW + colGap), y0 + rowTops[i / 2], colW, rowH);
                    SpellCard(card, s, i == selected, pad, chip);
                    if (GUI.Button(card, GUIContent.none, GUIStyle.none)) selected = i;
                }
                return top;
            });

            Heading(new Rect(side.x, side.y, side.width, headH), "빠른 슬롯");
            var y = side.y + headH;
            var slotH = skin.LineH(skin.Body) + skin.Px(10);
            var pick = spells.Count == 0 ? null : spells[selected];
            for (var i = 0; i < Magic.QuickSlotCount; i++)
            {
                var r = new Rect(side.x, y, side.width, slotH);
                var id = state.QuickSlots[i];
                var holdsPick = pick != null && id == pick.Id;
                var enabled = editable && pick != null && (pick.AllowQuickCast || id != null && holdsPick);
                skin.Draw(!state.QuickCastUnlocked ? Frame.ButtonDisabled : holdsPick ? Frame.ButtonSelected : Frame.Button, r);
                skin.Key(new Vector2(r.x + skin.Px(6), r.y + (r.height - smallH - skin.Px(2)) / 2f), (i + 1).ToString(), skin.Small);
                var name = !state.QuickCastUnlocked ? "잠김" : id == null ? "비어 있음" : Game.Content.Spell(id)?.Name ?? "?";
                skin.Label(new Rect(r.x + skin.Px(22), r.y, r.width - skin.Px(26), r.height), name, skin.Line(skin.Body), id == null ? UiSkin.Faint : UiSkin.Text, false, TextAnchor.MiddleLeft);
                if (!state.QuickCastUnlocked) skin.DrawGlyph(new Rect(r.xMax - skin.Px(14), r.y, skin.Px(8), r.height), Glyph.Lock, UiSkin.Faint);
                if (enabled && GUI.Button(r, GUIContent.none, GUIStyle.none)) ToggleSlot(i, pick.Id);
                y += slotH + skin.Px(3);
            }
            y += skin.Px(3);
            var note = !state.QuickCastUnlocked
                ? "「" + (Game.Content.Spells.FirstOrDefault(s => s.UnlocksQuickCast)?.Name ?? "빠른 시전") + "」을 자기 자신에게 성공시키면 슬롯이 열려요."
                : Game.Event != null ? "이벤트 중에는 슬롯을 바꿀 수 없어요."
                : pick != null && !pick.AllowQuickCast ? "이 주문은 영창으로만 쓸 수 있어요."
                : "주문을 고르고 1-4를 누르면 그 슬롯에 넣어요. 같은 슬롯을 다시 누르면 비워요.";
            skin.Label(new Rect(side.x, y, side.width, side.yMax - y), note, skin.Small, UiSkin.Muted);
            HintRow(new Rect(cards.x, cards.yMax - smallH, cards.width, smallH), ("←↑↓→", "고르기"), ("1-4", "슬롯에 넣기"));
        }

        void SpellCard(Rect card, SpellDef s, bool on, RectOffset pad, float chip)
        {
            var hover = card.Contains(Event.current.mousePosition);
            skin.Draw(on ? Frame.SlotSelected : Frame.Slot, card);
            if (hover && !on) skin.Fill(skin.Inner(Frame.Slot, card), new Color(1f, 1f, 1f, 0.04f));
            var inner = skin.CardInner(card);
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var boldH = skin.LineH(skin.Bold);
            var slots = Enumerable.Range(0, Magic.QuickSlotCount).Where(k => Game.State.QuickSlots[k] == s.Id).Select(k => (k + 1).ToString()).ToList();
            skin.Label(new Rect(inner.x, inner.y, inner.width, boldH), s.Name, skin.Line(skin.Bold), on ? UiSkin.GoldBright : UiSkin.Text);
            if (slots.Count > 0)
            {
                var text = "슬롯 " + string.Join(",", slots);
                var w = skin.Measure(text, small).x + skin.Px(8);
                skin.Chip(new Vector2(inner.xMax - w, inner.y + skin.Px(1)), text, small, UiSkin.GoldDark, UiSkin.GoldBright);
            }
            var y = inner.y + boldH + skin.Px(3);
            var x = inner.x;
            foreach (var rune in s.Runes)
            {
                skin.RuneChip(new Vector2(x, y), rune, chip);
                x += chip + skin.Px(2);
            }
            var tag = s.UnlocksQuickCast ? "빠른 시전 해금" : s.AllowQuickCast ? "" : "영창 전용";
            if (tag.Length > 0) skin.Label(new Rect(x + skin.Px(4), y, inner.xMax - x, chip), tag, small, UiSkin.Faint, false, TextAnchor.MiddleLeft);
            y += chip + skin.Px(3);
            var needs = Game.Magic.Requirements(s);
            if (needs.Count == 0) skin.Label(new Rect(inner.x, y, inner.width, smallH), "조건 없음", small, UiSkin.Faint);
            foreach (var n in needs)
            {
                skin.DrawGlyph(new Rect(inner.x, y, skin.Px(10), smallH), n.Met ? Glyph.Check : Glyph.Cross, n.Met ? UiSkin.Good : UiSkin.Bad);
                skin.Label(new Rect(inner.x + skin.Px(13), y, inner.width - skin.Px(13), smallH), n.Text, small, n.Met ? UiSkin.Muted : UiSkin.Bad);
                y += smallH;
            }
        }

        // ------------------------------------------------------------------ sleep, menu, day report, error

        void DrawSleep()
        {
            Dim(0.35f);
            var title = "잠자리에 들까요?";
            var line = "하루를 마치고 저장해요. 다음 날 07:00에 여기서 일어나요.";
            var r = Centered(220, 0);
            var pad = skin.Pad(Frame.Panel);
            var w = r.width - pad.horizontal;
            var bh = skin.LineH(skin.Body) + skin.Px(10);
            var h = skin.LineH(skin.Bold) + skin.Px(4) + skin.Height(line, skin.Small, w) + skin.Px(8) + bh + pad.vertical;
            r = UiSkin.Snap(new Rect(r.x, (Screen.height - h) / 2f, r.width, h));
            var pop = skin.BeginPop(Time.unscaledTime - panelAt, r);
            skin.Draw(Frame.Panel, r);
            var inner = skin.Inner(Frame.Panel, r);
            skin.Label(new Rect(inner.x, inner.y, w, skin.LineH(skin.Bold)), title, skin.Line(skin.Bold), UiSkin.GoldBright);
            var y = inner.y + skin.LineH(skin.Bold) + skin.Px(4);
            skin.Label(new Rect(inner.x, y, w, h), line, skin.Small, UiSkin.Muted);
            var bw = (w - skin.Px(4)) / 2f;
            var by = inner.yMax - bh;
            if (skin.Button(new Rect(inner.x, by, bw, bh), "잔다 · Enter", sleepFocus == 0)) Sleep();
            if (skin.Button(new Rect(inner.x + bw + skin.Px(4), by, bw, bh), "아니요 · Esc", sleepFocus == 1)) ClosePanel();
            skin.EndPop(pop);
        }

        static readonly (string Keys, string What)[] Controls =
        {
            ("WASD · 방향키", "걷기"),
            ("E", "말 걸기 · 잠자기 · 대사 넘기기"),
            ("Q", "영창 열기 / 닫기"),
            ("1-4", "영창 중 룬 입력 · 평소엔 빠른 시전"),
            ("Enter", "시전 · 대사 넘기기 · 고르기"),
            ("J  K  I  M", "의뢰·약속 · 주문 · 가방 · 지도"),
            ("F", "가구 배치 (옮길 수 있는 방에서)"),
            ("Tab", "수첩에서 다음 탭"),
            ("X", "취소 · 창 닫기"),
            ("Esc", "메뉴 · 닫기"),
            ("F9", "마지막 저장 불러오기"),
        };

        void DrawMenu()
        {
            Dim(0.55f);
            var controls = Page == MenuPage.Controls;
            var pad = skin.Pad(Frame.Panel);
            var rowH = skin.LineH(skin.Body) + skin.Px(10);
            var smallH = skin.LineH(skin.Small);
            var titleH = skin.LineH(skin.Bold) + skin.Px(2) + smallH + skin.Px(8);
            var h = pad.vertical + titleH + (controls ? Controls.Length * (smallH + skin.Px(6)) : MenuItems.Length * (rowH + skin.Px(3)));
            var r = Centered(controls ? 300 : 190, 0);
            r = UiSkin.Snap(new Rect(r.x, Mathf.Max(skin.Px(8), (Screen.height - h) / 2f), r.width, Mathf.Min(h, Screen.height - skin.Px(16))));
            var pop = skin.BeginPop(Time.unscaledTime - panelAt, r);
            skin.Draw(Frame.Panel, r);
            var inner = skin.Inner(Frame.Panel, r);
            skin.Label(new Rect(inner.x, inner.y, inner.width, skin.LineH(skin.Bold)), controls ? "조작법" : "메뉴", skin.Line(skin.Bold), UiSkin.GoldBright);
            skin.Label(new Rect(inner.x, inner.y + skin.LineH(skin.Bold) + skin.Px(2), inner.width, smallH), controls ? "Esc 또는 Enter로 돌아가요" : "게임이 멈춰 있어요", skin.Line(skin.Small), UiSkin.Faint);
            var y = inner.y + titleH;
            if (controls)
            {
                var keyW = Controls.Max(c => c.Keys.Split(new[] { "  ", " · " }, StringSplitOptions.RemoveEmptyEntries).Sum(k => skin.Measure(k, skin.Line(skin.Small)).x + skin.Px(9))) + skin.Px(8);
                foreach (var (keys, what) in Controls)
                {
                    var x = inner.x;
                    foreach (var key in keys.Split(new[] { "  ", " · " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var k = skin.Key(new Vector2(x, y), key, skin.Small);
                        x = k.xMax + skin.Px(3);
                    }
                    skin.Label(new Rect(inner.x + keyW, y + skin.Px(1), inner.width - keyW, smallH), what, skin.Line(skin.Small), UiSkin.Text);
                    y += smallH + skin.Px(6);
                }
            }
            else
            {
                for (var i = 0; i < MenuItems.Length; i++)
                {
                    var label = i == 2 ? "글자 크기 · " + (TextScale > 1f ? "크게" : "보통") : i == 3 ? "마지막 저장 불러오기 · F9" : MenuItems[i];
                    var row = new Rect(inner.x, y, inner.width, rowH);
                    if (skin.Button(row, label, i == menuFocus)) MenuAction(i);
                    if (i == menuFocus) skin.DrawGlyph(new Rect(row.x + skin.Px(6), row.y, skin.Px(6), row.height), Glyph.Arrow, UiSkin.GoldBright);
                    y += rowH + skin.Px(3);
                }
            }
            skin.EndPop(pop);
        }

        void DrawDayReport()
        {
            var now = Time.unscaledTime;
            if (report == null)
            {
                var fade = 1f - (now - reportClosedAt) / 0.6f;
                if (fade > 0f) skin.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, fade));
                return;
            }
            skin.Fill(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            var age = now - reportAt;
            var a = Mathf.Clamp01((age - 0.15f) / 0.35f);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            var pad = skin.Pad(Frame.Panel);
            var r = Centered(250, 0);
            var w = r.width - pad.horizontal;
            var titleH = skin.LineH(skin.Title);
            var bodyH = skin.LineH(skin.Body);
            var smallH = skin.LineH(skin.Small);
            var quests = report.Quests;
            var h = pad.vertical + titleH + skin.Px(4) + bodyH + skin.Px(8) + skin.U + skin.Px(8) + bodyH + quests.Count * smallH + skin.Px(4) + bodyH + skin.Px(8) + skin.U + skin.Px(8) + smallH + skin.Px(10) + smallH;
            r = UiSkin.Snap(new Rect(r.x, (Screen.height - h) / 2f + Mathf.Round((1f - a) * skin.Px(6)), r.width, h));
            skin.Draw(Frame.Panel, r);
            var inner = skin.Inner(Frame.Panel, r);
            var y = inner.y;
            skin.Label(new Rect(inner.x, y, w, titleH), (report.Day + 1) + "일차를 마쳤습니다", skin.Line(skin.Title), UiSkin.GoldBright, false, TextAnchor.UpperCenter);
            y += titleH + skin.Px(4);
            var how = report.How == DayEnd.PassedOut ? "새벽 2시, 그만 쓰러져 잠들었어요." : "침대에서 푹 잤어요.";
            skin.Label(new Rect(inner.x, y, w, bodyH), how, skin.Line(skin.Body), report.How == DayEnd.PassedOut ? UiSkin.Bad : UiSkin.Muted, false, TextAnchor.UpperCenter);
            y += bodyH + skin.Px(8);
            skin.Fill(new Rect(inner.x, y, w, skin.U), UiSkin.GoldDark);
            y += skin.U + skin.Px(8);
            var star = skin.GlyphSize(Glyph.Star);
            skin.DrawGlyph(new Rect(inner.x, y, star.x, bodyH), Glyph.Star, UiSkin.GoldBright);
            skin.Label(new Rect(inner.x + star.x + skin.Px(5), y, w, bodyH), "완료한 의뢰 " + quests.Count + "건", skin.Line(skin.Body), UiSkin.Text);
            y += bodyH;
            foreach (var q in quests)
            {
                skin.DrawGlyph(new Rect(inner.x + skin.Px(14), y, skin.Px(10), smallH), Glyph.Check, UiSkin.Good);
                skin.Label(new Rect(inner.x + skin.Px(28), y, w - skin.Px(28), smallH), q.Title, skin.Line(skin.Small), UiSkin.Muted);
                y += smallH;
            }
            y += skin.Px(4);
            var coin = skin.GlyphSize(Glyph.Coin);
            skin.DrawGlyph(new Rect(inner.x, y, coin.x, bodyH), Glyph.Coin, Color.white);
            var crowns = report.CrownsEarned;
            skin.Label(new Rect(inner.x + coin.x + skin.Px(5), y, w, bodyH), crowns == 0 ? "크라운 변화 없음" : (crowns > 0 ? "+" : "") + crowns + " 크라운", skin.Line(skin.Body), crowns >= 0 ? UiSkin.Good : UiSkin.Bad);
            y += bodyH + skin.Px(8);
            skin.Fill(new Rect(inner.x, y, w, skin.U), UiSkin.GoldDark);
            y += skin.U + skin.Px(8);
            skin.Label(new Rect(inner.x, y, w, smallH), "하루를 저장했어요 · " + (report.Day + 2) + "일차 07:00", skin.Line(skin.Small), UiSkin.Faint, false, TextAnchor.UpperCenter);
            y += smallH + skin.Px(10);
            var pulse = 0.55f + 0.45f * Mathf.Cos(now * 4f);
            if (age > 0.4f) skin.Label(new Rect(inner.x, y, w, smallH), "아무 키나 눌러 계속", skin.Line(skin.Small), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, pulse), false, TextAnchor.UpperCenter);
            GUI.color = old;
        }

        void DrawError()
        {
            var r = UiSkin.Snap(new Rect(skin.Px(8), Screen.height * 0.65f, Screen.width - skin.Px(16), Screen.height * 0.35f - skin.Px(8)));
            skin.Draw(Frame.Panel, r);
            var inner = skin.Inner(Frame.Panel, r);
            skin.Label(new Rect(inner.x, inner.y, inner.width, skin.LineH(skin.Bold)), "오류", skin.Line(skin.Bold), UiSkin.Bad);
            skin.Label(new Rect(inner.x, inner.y + skin.LineH(skin.Bold), inner.width, inner.height), Error, skin.Small, UiSkin.Bad);
        }
    }
}
