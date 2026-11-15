using System.Linq;
using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>
    /// The bag tab: a 6×4 slot grid with item icons, the selected item's card, the notebook's key items and the dorm mailbox.
    /// Arrows move the cursor, Enter picks a stack up and puts it down on another slot (swap or merge), Delete throws one away.
    /// Every change goes through Core.Inventory.
    /// </summary>
    public sealed class InventoryScreen
    {
        public const int Columns = 6;

        float sideScroll, sideHeight;

        /// <summary>The slot under the cursor.</summary>
        public int Selected { get; private set; }
        /// <summary>The slot picked up with Enter and waiting to be put down, or -1.</summary>
        public int Holding { get; private set; } = -1;
        /// <summary>The last thing the screen did, shown at its bottom.</summary>
        public string Message { get; private set; } = string.Empty;

        public void Open()
        {
            Holding = -1;
            Message = string.Empty;
        }

        public void MoveCursor(int dx, int dy)
        {
            var col = Selected % Columns + dx;
            var row = Selected / Columns + dy;
            var rows = Inventory.BagSize / Columns;
            col = (col % Columns + Columns) % Columns;
            row = (row % rows + rows) % rows;
            Selected = row * Columns + col;
        }

        public void Select(int slot) => Selected = Mathf.Clamp(slot, 0, Inventory.BagSize - 1);

        /// <summary>Enter: picks up the stack under the cursor, or puts the held stack down there.</summary>
        public void PickOrPlace(Core.Game game)
        {
            var bag = game.State.Bag;
            if (Holding < 0)
            {
                if (bag[Selected] == null) { Message = "빈 칸입니다."; return; }
                Holding = Selected;
                Message = ItemName(game, bag[Selected].ItemId) + "을(를) 들었습니다. 놓을 칸에서 Enter를 누르세요.";
                return;
            }
            var from = Holding;
            Holding = -1;
            if (from == Selected) { Message = "제자리에 두었습니다."; return; }
            Message = game.Inventory.Move(from, Selected) ? "옮겼습니다." : "이 칸에는 더 쌓을 수 없습니다.";
        }

        /// <summary>Delete: throws away one from the slot under the cursor.</summary>
        public void DiscardOne(Core.Game game)
        {
            var lot = game.State.Bag[Selected];
            if (lot == null) { Message = "버릴 물건이 없습니다."; return; }
            var name = ItemName(game, lot.ItemId);
            if (Holding == Selected) Holding = -1;
            Message = game.Inventory.Discard(Selected, 1) == 1 ? name + " 1개를 버렸습니다." : name + "은(는) 버릴 수 없습니다.";
        }

        public void TakeMail(Core.Game game, int row)
        {
            if (row < 0 || row >= game.State.Mailbox.Count) return;
            var name = ItemName(game, game.State.Mailbox[row].ItemId);
            var moved = game.Inventory.TakeFromMailbox(row);
            Message = moved > 0 ? name + " " + moved + "개를 가방에 넣었습니다."
                : !game.NearBed ? "우편 보관함은 기숙사 잠자리 옆에서만 열 수 있습니다." : "가방에 자리가 없습니다.";
        }

        public static string ItemName(Core.Game game, string id) => game.Content.Item(id)?.Name ?? id;

        static string Quality(Lot lot) => lot.Quality == Inventory.Fine ? "고급" : "보통";

        /// <summary>Draws the tab's content into r.</summary>
        public void Draw(Rect r, Core.Game game, UiSkin skin)
        {
            var state = game.State;
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var bodyH = skin.LineH(skin.Body);
            var footerH = bodyH + skin.Px(3) + smallH;
            var rows = Inventory.BagSize / Columns;
            var gap = skin.Px(3);
            var headH = skin.LineH(skin.Bold) + skin.Px(4);
            var gridAreaW = Mathf.Floor(r.width * 0.6f);
            var slot = Mathf.Floor(Mathf.Min(skin.Px(40), (gridAreaW - (Columns - 1) * gap) / Columns, (r.height - headH - footerH - skin.Px(6) - (rows - 1) * gap) / rows));
            var gridW = Columns * slot + (Columns - 1) * gap;

            var used = state.Bag.Count(l => l != null);
            skin.Label(new Rect(r.x, r.y, gridW, headH), "가방", skin.Line(skin.Bold), UiSkin.Gold);
            skin.Label(new Rect(r.x + skin.Px(30), r.y + skin.Px(1), gridW, headH), used + " / " + Inventory.BagSize + "칸", small, UiSkin.Faint);
            var coin = skin.GlyphSize(Glyph.Coin);
            var wallet = state.Wallet + " 크라운";
            var walletW = skin.Measure(wallet, small).x;
            skin.Label(new Rect(r.x + gridW - walletW, r.y + skin.Px(1), walletW + 2, smallH), wallet, small, UiSkin.Text);
            skin.DrawGlyph(new Rect(r.x + gridW - walletW - coin.x - skin.Px(4), r.y + skin.Px(1), coin.x, smallH), Glyph.Coin, Color.white);

            var top = r.y + headH;
            for (var i = 0; i < Inventory.BagSize; i++)
            {
                var rect = UiSkin.Snap(new Rect(r.x + i % Columns * (slot + gap), top + i / Columns * (slot + gap), slot, slot));
                PaintSlot(rect, game, skin, state.Bag[i], i == Selected, i == Holding);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    if (Selected == i || Holding >= 0) { Selected = i; PickOrPlace(game); }
                    else Selected = i;
                }
            }

            var sideX = r.x + gridW + skin.Px(10);
            var side = new Rect(sideX, r.y, r.xMax - sideX, r.height - footerH - skin.Px(4));
            DrawSide(side, game, skin);

            var footer = new Rect(r.x, r.yMax - footerH, r.width, footerH);
            skin.Fill(new Rect(footer.x, footer.y - skin.Px(3), footer.width, skin.U), new Color(UiSkin.GoldDark.r, UiSkin.GoldDark.g, UiSkin.GoldDark.b, 0.6f));
            skin.Label(new Rect(footer.x, footer.y, footer.width, bodyH), Message.Length > 0 ? Message : "칸을 고르면 오른쪽에 설명이 나와요.", skin.Line(skin.Body), Message.Length > 0 ? UiSkin.GoldBright : UiSkin.Muted);
            var x = footer.x;
            var y = footer.y + bodyH + skin.Px(3);
            foreach (var (key, what) in new[] { ("←↑↓→", "칸 이동"), ("Enter", "들기·놓기"), ("Delete", "1개 버리기") })
            {
                var k = skin.Key(new Vector2(x, y - skin.Px(1)), key, skin.Small);
                x = k.xMax + skin.Px(3);
                skin.Label(new Rect(x, y, skin.Measure(what, small).x + 2, smallH), what, small, UiSkin.Muted);
                x += skin.Measure(what, small).x + skin.Px(9);
            }
        }

        /// <summary>One bag slot as the grid and the hotbar draw it: frame, icon, fine-quality star and count. on lights the frame, holding pulses it.</summary>
        public static void PaintSlot(Rect rect, Core.Game game, UiSkin skin, Lot lot, bool on, bool holding)
        {
            skin.Draw(on ? Frame.SlotSelected : Frame.Slot, rect);
            if (!on && rect.Contains(Event.current.mousePosition)) skin.Fill(skin.Inner(Frame.Slot, rect), new Color(1f, 1f, 1f, 0.06f));
            if (holding)
            {
                var pulse = 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f);
                skin.Fill(skin.Inner(Frame.Slot, rect), new Color(0.45f, 0.9f, 1f, pulse));
            }
            if (lot == null) return;
            var def = game.Content.Item(lot.ItemId);
            var icon = Mathf.Max(32f, Mathf.Floor((rect.width - skin.Px(7)) / 32f) * 32f);
            var lift = holding ? -skin.Px(2) : 0;
            GUI.DrawTexture(UiSkin.Snap(new Rect(rect.x + (rect.width - icon) / 2f, rect.y + (rect.height - icon) / 2f + lift, icon, icon)), skin.Icon(game, lot.ItemId));
            var small = skin.Line(skin.Small);
            if (lot.Quality == Inventory.Fine) skin.DrawGlyph(new Rect(rect.xMax - skin.Px(12), rect.y + skin.Px(3), skin.Px(9), skin.Px(9)), Glyph.Star, UiSkin.GoldBright, true);
            if (lot.Count > 1 || (def?.StackMax ?? Inventory.DefaultStackMax) > 1)
            {
                var text = lot.Count.ToString();
                var size = skin.Measure(text, small);
                skin.Label(new Rect(rect.xMax - size.x - skin.Px(6), rect.yMax - size.y - skin.Px(4), size.x + 2, size.y), text, small, UiSkin.Text, true);
            }
        }

        void DrawSide(Rect r, Core.Game game, UiSkin skin)
        {
            var e = Event.current;
            var max = Mathf.Max(0f, sideHeight - r.height);
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition))
            {
                sideScroll = Mathf.Clamp(sideScroll + Mathf.Sign(e.delta.y) * skin.LineH(skin.Body) * 2f, 0f, max);
                e.Use();
            }
            sideScroll = Mathf.Clamp(sideScroll, 0f, max);
            GUI.BeginClip(UiSkin.Snap(r));
            sideHeight = DrawSideBody(new Rect(0, -Mathf.Round(sideScroll), r.width - skin.Px(4), 0), game, skin);
            GUI.EndClip();
            if (max > 0f)
            {
                if (sideScroll < max - 0.5f) skin.Label(new Rect(r.xMax - skin.Px(14), r.yMax - skin.LineH(skin.Small), skin.Px(10), skin.LineH(skin.Small)), "▼", skin.Line(skin.Small), UiSkin.GoldBright, true, TextAnchor.LowerCenter);
                if (sideScroll > 0.5f) skin.Label(new Rect(r.xMax - skin.Px(14), r.y, skin.Px(10), skin.LineH(skin.Small)), "▲", skin.Line(skin.Small), UiSkin.GoldBright, true, TextAnchor.UpperCenter);
                skin.Fill(new Rect(r.xMax - skin.Px(2), r.y, skin.Px(2), r.height), new Color(0f, 0f, 0f, 0.35f));
                var thumb = Mathf.Max(skin.Px(10), r.height * r.height / sideHeight);
                skin.Fill(new Rect(r.xMax - skin.Px(2), r.y + (r.height - thumb) * sideScroll / max, skin.Px(2), thumb), UiSkin.GoldDark);
            }
        }

        float DrawSideBody(Rect r, Core.Game game, UiSkin skin)
        {
            var state = game.State;
            var small = skin.Small;
            var smallLine = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var boldH = skin.LineH(skin.Bold);
            var y = r.y;
            var lot = state.Bag[Selected];
            var w = r.width - skin.CardPadH;
            if (lot == null)
            {
                var h = skin.CardPadV + boldH + smallH;
                var box = new Rect(r.x, y, r.width, h);
                skin.Draw(Frame.Slot, box);
                var cin = skin.CardInner(box);
                skin.Label(new Rect(cin.x, cin.y, w, boldH), "빈 칸", skin.Line(skin.Bold), UiSkin.Muted);
                skin.Label(new Rect(cin.x, cin.y + boldH, w, smallH), (Selected + 1) + "번 칸", smallLine, UiSkin.Faint);
                y += h;
            }
            else
            {
                var def = game.Content.Item(lot.ItemId);
                var cat = UiSkin.Categories.TryGetValue(def?.Category ?? "", out var c) ? c : ("기타", UiSkin.Muted);
                var icon = skin.Px(32);
                var textX = icon + skin.Px(6);
                var desc = def?.Description ?? "";
                var descH = desc.Length == 0 ? 0f : skin.Height(desc, small, w) + skin.Px(4);
                var bh = skin.LineH(skin.Body) + skin.Px(10);
                var h = skin.CardPadV + Mathf.Max(icon, boldH + smallH + skin.Px(4)) + skin.Px(4) + descH + bh;
                var box = new Rect(r.x, y, r.width, h);
                skin.Draw(Frame.Slot, box);
                var cin = skin.CardInner(box);
                var x = cin.x;
                var ty = cin.y;
                GUI.DrawTexture(UiSkin.Snap(new Rect(x, ty, icon, icon)), skin.Icon(game, lot.ItemId));
                skin.Label(new Rect(x + textX, ty, w - textX, boldH), (def?.Name ?? lot.ItemId) + "  ×" + lot.Count, skin.Line(skin.Bold), UiSkin.Text);
                var chip = skin.Chip(new Vector2(x + textX, ty + boldH + skin.Px(2)), cat.Item1, smallLine, Color.Lerp(cat.Item2, UiSkin.Ink, 0.35f), UiSkin.Text);
                var extra = (def != null && def.HasQuality ? "품질 " + Quality(lot) + " · " : "") + "한 칸에 " + (def?.StackMax ?? Inventory.DefaultStackMax) + "개";
                skin.Label(new Rect(chip.xMax + skin.Px(4), chip.y + skin.Px(1), w, smallH), extra, smallLine, UiSkin.Faint);
                ty += Mathf.Max(icon, boldH + smallH + skin.Px(4)) + skin.Px(4);
                if (descH > 0f)
                {
                    skin.Label(new Rect(x, ty, w, descH), desc, small, UiSkin.Muted);
                    ty += descH;
                }
                var bw = (w - skin.Px(4)) / 2f;
                if (skin.Button(new Rect(x, ty, bw, bh), Holding == Selected ? "내려놓기" : Holding >= 0 ? "여기에 놓기" : "옮기기")) PickOrPlace(game);
                if (skin.Button(new Rect(x + bw + skin.Px(4), ty, bw, bh), "1개 버리기")) DiscardOne(game);
                y += h;
            }

            y += skin.Px(5);
            skin.Label(new Rect(r.x, y, r.width, boldH), "필수품", skin.Line(skin.Bold), UiSkin.Gold);
            skin.Label(new Rect(r.x + skin.Measure("필수품", skin.Line(skin.Bold)).x + skin.Px(6), y + skin.Px(1), r.width, smallH), "수첩에 보관", smallLine, UiSkin.Faint);
            y += boldH + skin.Px(1);
            var rowIcon = skin.Px(16);
            if (state.KeyItems.Count == 0)
            {
                skin.Label(new Rect(r.x, y, r.width, smallH), "아직 없어요.", smallLine, UiSkin.Faint);
                y += smallH;
            }
            var kx = r.x;
            foreach (var id in state.KeyItems)
            {
                var name = game.Content.Item(id)?.Name ?? id;
                var need = rowIcon + skin.Px(4) + skin.Measure(name, smallLine).x + skin.Px(8);
                if (kx > r.x && kx + need > r.xMax)
                {
                    kx = r.x;
                    y += rowIcon + skin.Px(1);
                }
                GUI.DrawTexture(UiSkin.Snap(new Rect(kx, y, rowIcon, rowIcon)), skin.Icon(game, id));
                skin.Label(new Rect(kx + rowIcon + skin.Px(4), y, need, rowIcon), name, smallLine, UiSkin.Text, false, TextAnchor.MiddleLeft);
                kx += need;
            }
            if (state.KeyItems.Count > 0) y += rowIcon + skin.Px(1);

            y += skin.Px(5);
            skin.Label(new Rect(r.x, y, r.width, boldH), "우편함", skin.Line(skin.Bold), UiSkin.Gold);
            var note = state.Mailbox.Count == 0 ? "비어 있어요" : game.NearBed ? "꺼낼 수 있어요" : "잠자리 옆에서 꺼내요";
            skin.Label(new Rect(r.x + skin.Measure("우편함", skin.Line(skin.Bold)).x + skin.Px(6), y + skin.Px(1), r.width, smallH), note, smallLine, UiSkin.Faint);
            y += boldH + skin.Px(1);
            var takeW = skin.Px(40);
            var takeH = smallH + skin.Px(6);
            for (var i = 0; i < state.Mailbox.Count; i++)
            {
                var mail = state.Mailbox[i];
                GUI.DrawTexture(UiSkin.Snap(new Rect(r.x, y + (takeH - rowIcon) / 2f, rowIcon, rowIcon)), skin.Icon(game, mail.ItemId));
                skin.Label(new Rect(r.x + rowIcon + skin.Px(5), y, r.width - rowIcon - takeW - skin.Px(10), takeH), ItemName(game, mail.ItemId) + (mail.Quality == Inventory.Fine ? " (고급)" : "") + " ×" + mail.Count, smallLine, UiSkin.Text, false, TextAnchor.MiddleLeft);
                if (skin.Button(new Rect(r.xMax - takeW, y, takeW, takeH), "꺼내기", false, game.NearBed, skin.Small)) TakeMail(game, i);
                y += takeH + skin.Px(2);
            }
            return y - r.y;
        }
    }
}
