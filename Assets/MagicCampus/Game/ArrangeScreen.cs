using System;
using System.Collections.Generic;
using System.Linq;
using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>
    /// Arrange mode (F in a room that allows it): a list of the room's pieces on the left and, in the world, the piece in focus.
    /// Browse picks a piece (up/down), Enter lifts it, and while it is held the arrows move a ghost one cell at a time, green where
    /// it may stand and red where the rule refuses (Core's Furniture.Check, with the reason in words), Enter sets it down, C puts
    /// it away. A stored piece is taken out the same way. The rules live in Core; this only holds the cursor and the ghost and
    /// draws them. Time stands still while the mode is open.
    /// </summary>
    public sealed class ArrangeScreen
    {
        public enum Phase { Browse, Move }

        public Phase Mode { get; private set; }
        /// <summary>Index into the room's piece list.</summary>
        public int Cursor { get; private set; }
        public string MapId { get; private set; }
        public int GhostX { get; private set; }
        public int GhostY { get; private set; }
        public PlaceProblem GhostProblem { get; private set; }
        /// <summary>The last thing a command did, for the root to show as a toast; cleared by TakeNote.</summary>
        public string Note { get; private set; }
        public ToastKind NoteKind { get; private set; }

        /// <summary>The left strip the panel covers, in screen pixels (the root feeds it to the camera).</summary>
        public int PanelWidth { get; private set; }

        public void Open(Core.Game game)
        {
            MapId = game.MapId;
            Mode = Phase.Browse;
            Cursor = 0;
            Note = null;
        }

        public (string Text, ToastKind Kind)? TakeNote()
        {
            if (Note == null) return null;
            var note = (Note, NoteKind);
            Note = null;
            return note;
        }

        ArrangeableDef Room(Core.Game game) => game.Furniture.Room(MapId);

        public PieceDef Selected(Core.Game game)
        {
            var room = Room(game);
            return room == null || room.Pieces.Count == 0 ? null : room.Pieces[Math.Max(0, Math.Min(Cursor, room.Pieces.Count - 1))];
        }

        public void Select(Core.Game game, int index)
        {
            if (Mode != Phase.Browse || Room(game) == null) return;
            Cursor = Math.Max(0, Math.Min(index, Room(game).Pieces.Count - 1));
        }

        public void MoveCursor(Core.Game game, int step)
        {
            var count = Room(game)?.Pieces.Count ?? 0;
            if (Mode == Phase.Browse && count > 0) Cursor = ((Cursor + step) % count + count) % count;
        }

        /// <summary>Enter in Browse: lifts the piece in focus. A standing piece starts at its own spot, a stored one at the free spot nearest the player's front.</summary>
        public bool Pick(Core.Game game)
        {
            var piece = Selected(game);
            if (Mode != Phase.Browse || piece == null) return false;
            var at = game.Furniture.PlacementOf(MapId, piece.Id);
            if (at != null && game.Furniture.IsLoaded(MapId, piece.Id))
            {
                Say("옮길 수 없어요 · " + Reason(PlaceProblem.Loaded), ToastKind.Bad);
                return false;
            }
            if (at != null)
            {
                GhostX = at.X;
                GhostY = at.Y;
            }
            else (GhostX, GhostY) = StartSpot(game, piece);
            Mode = Phase.Move;
            Refresh(game);
            return true;
        }

        /// <summary>The cell for a piece taken out of storage: the nearest anchor to the cell in front of the player that the rule allows, else the middle of the area.</summary>
        (int X, int Y) StartSpot(Core.Game game, PieceDef piece)
        {
            var a = Room(game).Area;
            var (fx, fy) = Front(game.Player);
            var best = ((a[0] + a[2]) / 2, (a[1] + a[3]) / 2);
            var bestDistance = int.MaxValue;
            for (var x = a[0]; x <= a[2]; x++)
                for (var y = a[1]; y <= a[3]; y++)
                {
                    var d = Math.Abs(x - fx) + Math.Abs(y - fy);
                    if (d < bestDistance && game.Furniture.Check(MapId, piece.Id, x, y) == PlaceProblem.None)
                    {
                        best = (x, y);
                        bestDistance = d;
                    }
                }
            return best;
        }

        static (int X, int Y) Front(Actor p)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            return p.Facing == "north" ? (x, y + 1) : p.Facing == "south" ? (x, y - 1) : p.Facing == "east" ? (x + 1, y) : (x - 1, y);
        }

        /// <summary>One cell of ghost movement. The ghost stays on the map; whether it may stand there is Core's answer.</summary>
        public void MoveGhost(Core.Game game, int dx, int dy)
        {
            var piece = Selected(game);
            if (Mode != Phase.Move || piece == null) return;
            var map = game.Content.Map(MapId);
            var b = piece.Bounds;
            GhostX = Math.Max(-b[0], Math.Min(map.Width - b[0] - b[2], GhostX + dx));
            GhostY = Math.Max(-b[1], Math.Min(map.Height - b[1] - b[3], GhostY + dy));
            Refresh(game);
        }

        void Refresh(Core.Game game) => GhostProblem = game.Furniture.Check(MapId, Selected(game).Id, GhostX, GhostY);

        /// <summary>Enter while holding: sets the piece down if the rule allows, else says why not.</summary>
        public bool Confirm(Core.Game game)
        {
            var piece = Selected(game);
            if (Mode != Phase.Move || piece == null) return false;
            Refresh(game);
            if (GhostProblem != PlaceProblem.None)
            {
                Say("놓을 수 없어요 · " + Reason(GhostProblem), ToastKind.Bad);
                return false;
            }
            var wasStored = game.Furniture.IsStored(MapId, piece.Id);
            if (!game.Furniture.Place(MapId, piece.Id, GhostX, GhostY))
            {
                Say("놓을 수 없어요 · " + Reason(game.Furniture.Check(MapId, piece.Id, GhostX, GhostY)), ToastKind.Bad);
                return false;
            }
            Mode = Phase.Browse;
            Say((wasStored ? "꺼내어 놓았어요 · " : "놓았어요 · ") + piece.Name, ToastKind.Good);
            return true;
        }

        /// <summary>C: puts the piece in focus (or the one being held) away.</summary>
        public bool Store(Core.Game game)
        {
            var piece = Selected(game);
            if (piece == null) return false;
            if (game.Furniture.IsStored(MapId, piece.Id))
            {
                Say("이미 보관 중이에요 · " + piece.Name, ToastKind.Info);
                return false;
            }
            if (!game.Furniture.Store(MapId, piece.Id))
            {
                if (game.Furniture.IsLoaded(MapId, piece.Id)) Say("보관할 수 없어요 · " + Reason(PlaceProblem.Loaded), ToastKind.Bad);
                return false;
            }
            Mode = Phase.Browse;
            Say("보관했어요 · " + piece.Name, ToastKind.Good);
            return true;
        }

        /// <summary>Esc while holding: puts the piece back in focus without moving it.</summary>
        public void Back() => Mode = Phase.Browse;

        void Say(string text, ToastKind kind)
        {
            Note = text;
            NoteKind = kind;
        }

        public static string Reason(PlaceProblem p)
        {
            switch (p)
            {
                case PlaceProblem.None: return "놓을 수 있어요";
                case PlaceProblem.Busy: return "지금은 옮길 수 없어요";
                case PlaceProblem.OffMap: return "방 밖으로 나가요";
                case PlaceProblem.OutsideArea: return "가구를 놓는 자리가 아니에요";
                case PlaceProblem.Wall: return "벽이나 붙박이와 겹쳐요";
                case PlaceProblem.Keep: return "문 앞이나 잠자리는 비워 둬요";
                case PlaceProblem.Piece: return "다른 가구와 겹쳐요";
                case PlaceProblem.Layer: return "같은 층의 가구와 겹쳐요";
                case PlaceProblem.SurfaceTaken: return "그 자리엔 이미 뭔가 올라가 있어요";
                case PlaceProblem.Loaded: return "위에 올려 둔 물건부터 치워 주세요";
                case PlaceProblem.Actor: return "누가 서 있는 자리예요";
                case PlaceProblem.Cutoff: return "길이 막혀요. 문에서 잠자리까지 걸어갈 수 있어야 해요";
                default: return "놓을 수 없어요";
            }
        }

        // ------------------------------------------------------------------ what the world shows

        /// <summary>The overlay for the world view: the held piece as a ghost, or the standing piece in focus; null for a piece in storage.</summary>
        public ArrangeOverlay Overlay(Core.Game game, MapArt art)
        {
            var piece = Selected(game);
            if (piece == null || art == null || !art.Pieces.TryGetValue(piece.Id, out var look)) return null;
            if (Mode == Phase.Move) return new ArrangeOverlay { Piece = piece, Art = look, X = GhostX, Y = GhostY, Held = true, Valid = GhostProblem == PlaceProblem.None, Lift = game.Furniture.LiftAt(MapId, piece.Id, GhostX, GhostY) };
            var at = game.Furniture.PlacementOf(MapId, piece.Id);
            return at == null ? null : new ArrangeOverlay { Piece = piece, Art = look, X = at.X, Y = at.Y };
        }

        /// <summary>The world point the camera should follow: the ghost, the piece in focus, or null for the player.</summary>
        public Vector2? Focus(Core.Game game)
        {
            var piece = Selected(game);
            if (piece == null) return null;
            var b = piece.Bounds;
            if (Mode == Phase.Move) return new Vector2(GhostX + b[0] + b[2] / 2f, GhostY + b[1] + b[3] / 2f);
            var at = game.Furniture.PlacementOf(MapId, piece.Id);
            return at == null ? (Vector2?)null : new Vector2(at.X + b[0] + b[2] / 2f, at.Y + b[1] + b[3] / 2f);
        }

        // ------------------------------------------------------------------ drawing

        public void Draw(Core.Game game, UiSkin skin, WorldView view)
        {
            var room = Room(game);
            if (room == null) return;
            var m = skin.Px(6);
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var panel = UiSkin.Snap(new Rect(m, m, skin.Px(86), Screen.height - 2 * m));
            PanelWidth = Mathf.RoundToInt(panel.xMax + skin.Px(2));
            skin.Draw(Frame.Panel, panel);
            var inner = skin.Inner(Frame.Panel, panel);
            skin.Label(new Rect(inner.x, inner.y, inner.width, skin.LineH(skin.Bold)), "가구 배치", skin.Line(skin.Bold), UiSkin.GoldBright);
            var y = inner.y + skin.LineH(skin.Bold) + skin.Px(1);
            skin.Label(new Rect(inner.x, y, inner.width, smallH), game.Content.MapName(MapId), small, UiSkin.Faint);
            y += smallH + skin.Px(4);
            skin.Fill(new Rect(inner.x, y, inner.width, skin.U), UiSkin.GoldDark);
            y += skin.U + skin.Px(4);

            var rowH = smallH + skin.Px(4);
            for (var i = 0; i < room.Pieces.Count; i++)
            {
                var piece = room.Pieces[i];
                var stored = game.Furniture.IsStored(MapId, piece.Id);
                var on = i == Cursor;
                var held = on && Mode == Phase.Move;
                var row = new Rect(inner.x, y, inner.width, rowH);
                var hover = row.Contains(Event.current.mousePosition);
                skin.Fill(row, held ? new Color(UiSkin.Good.r, UiSkin.Good.g, UiSkin.Good.b, 0.16f) : on ? new Color(UiSkin.GoldBright.r, UiSkin.GoldBright.g, UiSkin.GoldBright.b, 0.14f)
                    : new Color(0f, 0f, 0f, hover ? 0.3f : 0.2f));
                if (on) skin.Outline(row, held ? UiSkin.Good : UiSkin.Gold, skin.U);
                if (on) skin.DrawGlyph(new Rect(row.x + skin.Px(3), row.y, skin.Px(6), row.height), Glyph.Arrow, held ? UiSkin.Good : UiSkin.GoldBright);
                skin.Label(new Rect(row.x + skin.Px(11), row.y, row.width - skin.Px(14), row.height), piece.Name, small, stored ? UiSkin.Faint : on ? UiSkin.GoldBright : UiSkin.Text, false, TextAnchor.MiddleLeft);
                if (stored) DrawCrate(skin, new Rect(row.xMax - skin.Px(13), row.y + (row.height - skin.Px(8)) / 2f, skin.Px(10), skin.Px(8)));
                if (Mode == Phase.Browse && GUI.Button(row, GUIContent.none, GUIStyle.none)) Cursor = i;
                y += rowH + skin.Px(1);
            }
            var counts = "배치 " + game.Furniture.Placed(MapId).Count + " · 보관 " + game.Furniture.Stored(MapId).Count;
            skin.Label(new Rect(inner.x, inner.yMax - smallH, inner.width, smallH), counts, small, UiSkin.Faint);

            DrawHints(game, skin, panel);
            DrawWorldLabel(game, skin, view);
        }

        /// <summary>A small crate: the mark of a piece that is put away.</summary>
        static void DrawCrate(UiSkin skin, Rect r)
        {
            skin.Fill(r, UiSkin.Hex(0x3b4466));
            skin.Outline(r, UiSkin.Muted, skin.U);
            skin.Fill(new Rect(r.x, r.y + skin.U * 2, r.width, skin.U), UiSkin.Muted);
            skin.Fill(new Rect(r.center.x - skin.U / 2f, r.y + skin.U * 2, skin.U, r.height - skin.U * 2), UiSkin.Muted);
        }

        void DrawHints(Core.Game game, UiSkin skin, Rect panel)
        {
            var piece = Selected(game);
            var stored = piece != null && game.Furniture.IsStored(MapId, piece.Id);
            var parts = Mode == Phase.Browse
                ? new[] { ("↑↓", "고르기"), ("Enter", stored ? "꺼내기" : "집기"), ("C", "보관"), ("F", "끝내기") }
                : new[] { ("←↑↓→", "옮기기"), ("Enter", "놓기"), ("C", "보관"), ("Esc", "취소") };
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var pad = skin.Pad(Frame.NameBox);
            var total = parts.Sum(p => skin.Measure(p.Item1, small).x + skin.Px(6) + skin.Px(3) + skin.Measure(p.Item2, small).x + skin.Px(9)) - skin.Px(9);
            var area = new Rect(panel.xMax, 0, Screen.width - panel.xMax, Screen.height);
            var box = UiSkin.Snap(new Rect(area.x + (area.width - total - pad.horizontal) / 2f, Screen.height - skin.Px(6) - smallH - pad.vertical - skin.Px(2), total + pad.horizontal, smallH + skin.Px(2) + pad.vertical));
            skin.Draw(Frame.NameBox, box);
            var x = box.x + pad.left;
            var y = box.y + pad.top;
            foreach (var (key, what) in parts)
            {
                var k = skin.Key(new Vector2(x, y), key, skin.Small);
                x = k.xMax + skin.Px(3);
                skin.Label(new Rect(x, y + skin.Px(1), skin.Measure(what, small).x + 2, smallH), what, small, UiSkin.Muted);
                x += skin.Measure(what, small).x + skin.Px(9);
            }
        }

        /// <summary>A bubble over the piece in focus: its name, or while held whether it may stand there and why not.</summary>
        void DrawWorldLabel(Core.Game game, UiSkin skin, WorldView view)
        {
            var piece = Selected(game);
            if (piece == null || view == null) return;
            var b = piece.Bounds;
            int ax, ay;
            if (Mode == Phase.Move)
            {
                ax = GhostX;
                ay = GhostY;
            }
            else
            {
                var at = game.Furniture.PlacementOf(MapId, piece.Id);
                if (at == null) return;
                ax = at.X;
                ay = at.Y;
            }
            var p = view.GuiPoint(ax + b[0] + b[2] / 2f, ay + b[1] + b[3] + 0.05f);
            if (Mode == Phase.Move)
            {
                var ok = GhostProblem == PlaceProblem.None;
                Hud.Bubble(skin, p, null, piece.Name + " · " + Reason(GhostProblem), ok ? UiSkin.Good : UiSkin.Bad, true);
            }
            else Hud.Bubble(skin, p, null, piece.Name, UiSkin.GoldBright, true);
        }
    }
}
