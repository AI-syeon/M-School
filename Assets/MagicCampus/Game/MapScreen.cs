using System;
using System.Collections.Generic;
using System.Linq;
using MagicCampus.Core;
using UnityEngine;

namespace MagicCampus.Game
{
    /// <summary>
    /// The full map (M): one visited area at a time, drawn cell by cell from MapDef with World.CellAt. A cell's colour is the mean
    /// colour of what the area draws there (MapArt.Minimap from world-art.json), darkened where nobody can walk; doors and the bed
    /// carry a small marker. The player and NPCs show as their faces (the head of the south idle frame), the player with a pulsing
    /// gold ring and a facing tick, NPCs with a name tag. Magic targets and where each door leads are marked too. Areas the player has not stood
    /// on are not shown, and a door into one reads "???". Only reads Core state and the view's art.
    /// </summary>
    public sealed class MapScreen
    {
        /// <summary>The largest cell side, in UI pixels: a small area stays small instead of filling the panel.</summary>
        const int MaxCellUi = 9;
        /// <summary>Walls and other blocked cells keep their mean colour at this brightness so the walkable space stands out.</summary>
        const float BlockedShade = 0.55f;
        static readonly Color NoArtColor = new Color(0.30f, 0.30f, 0.30f), DoorColor = new Color(0.98f, 0.80f, 0.30f), BedColor = new Color(0.72f, 0.60f, 0.95f),
            GridColor = new Color(0f, 0f, 0f, 0.25f), NpcColor = new Color(0.35f, 0.80f, 1f), TargetColor = new Color(0.55f, 0.95f, 0.80f),
            PlayerRim = new Color(1f, 0.85f, 0.35f), Ink = new Color(0.05f, 0.04f, 0.06f, 0.95f), FacePlate = new Color(0.10f, 0.09f, 0.14f, 1f);

        /// <summary>The area being looked at. Set to the player's area each time the map opens.</summary>
        public string Shown { get; private set; }

        public void Open(Core.Game game) => Shown = game.MapId;

        /// <summary>-1 / +1: the previous or next visited area.</summary>
        public void Cycle(Core.Game game, int step)
        {
            var maps = Visited(game);
            if (maps.Count == 0) return;
            var i = Math.Max(0, maps.IndexOf(Shown));
            Shown = maps[((i + step) % maps.Count + maps.Count) % maps.Count];
        }

        static List<string> Visited(Core.Game game) => game.State.VisitedMaps.Where(id => game.Content.Maps.Any(m => m.Id == id)).ToList();

        /// <summary>Draws the tab's content into r.</summary>
        public void Draw(Rect r, Core.Game game, UiSkin skin, WorldView view)
        {
            if (Shown == null || !game.Content.Maps.Any(m => m.Id == Shown)) Shown = game.MapId;
            var content = game.Content;
            var map = content.Map(Shown);
            var small = skin.Line(skin.Small);
            var smallH = skin.LineH(skin.Small);
            var boldH = skin.LineH(skin.Bold);
            var y = r.y;
            var title = content.MapName(map.Id);
            skin.Label(new Rect(r.x, y, r.width, boldH), title, skin.Line(skin.Bold), UiSkin.GoldBright);
            var tx = r.x + skin.Measure(title, skin.Line(skin.Bold)).x + skin.Px(6);
            if (map.Id == game.MapId) skin.Chip(new Vector2(tx, y + skin.Px(1)), "지금 여기", small, UiSkin.Hex(0x7a2f2a), UiSkin.Text);
            var where = "내 위치 · " + content.MapName(game.MapId) + " (" + Math.Floor(game.Player.X) + ", " + Math.Floor(game.Player.Y) + ") · " + Facing(game.Player.Facing) + "쪽을 봄";
            skin.Label(new Rect(r.x, y + skin.Px(1), r.width, smallH), where, small, UiSkin.Faint, false, TextAnchor.UpperRight);
            y += boldH + skin.Px(4);

            var maps = Visited(game);
            var x = r.x;
            var tabH = smallH + skin.Px(8);
            foreach (var id in maps)
            {
                var label = content.MapName(id) + (id == game.MapId ? " ●" : "");
                var w = skin.Measure(label, small).x + skin.Px(14);
                if (x > r.x && x + w > r.xMax)
                {
                    x = r.x;
                    y += tabH + skin.Px(3);
                }
                if (skin.Button(new Rect(x, y, w, tabH), label, id == Shown, true, skin.Small)) Shown = id;
                x += w + skin.Px(3);
            }
            y += tabH + skin.Px(5);

            var legendH = Mathf.Max(smallH + skin.Px(4), skin.Px(12));
            var hintH = smallH + skin.Px(2);
            var area = new Rect(r.x, y, r.width, r.yMax - y - legendH - hintH - skin.Px(4));
            DrawGrid(area, game, map, skin, view);
            DrawLegend(new Rect(r.x, area.yMax + skin.Px(2), r.width, legendH), game, skin, view);
            var hy = r.yMax - hintH;
            var hx = r.x;
            foreach (var (key, what) in new[] { ("←→", "다른 구역"), ("M", "닫기") })
            {
                var k = skin.Key(new Vector2(hx, hy - skin.Px(1)), key, skin.Small);
                hx = k.xMax + skin.Px(3);
                skin.Label(new Rect(hx, hy, skin.Measure(what, small).x + 2, smallH), what, small, UiSkin.Muted);
                hx += skin.Measure(what, small).x + skin.Px(9);
            }
            skin.Label(new Rect(r.x, hy, r.width, smallH), "칸 색은 그 칸 그림의 평균 색 · 가 본 구역만 나와요", small, UiSkin.Faint, false, TextAnchor.UpperRight);
        }

        void DrawGrid(Rect area, Core.Game game, MapDef map, UiSkin skin, WorldView view)
        {
            var content = game.Content;
            var cell = Mathf.Floor(Mathf.Min(area.width / map.Width, area.height / map.Height, skin.Px(MaxCellUi)));
            if (cell < 4) return;
            var gw = cell * map.Width;
            var gh = cell * map.Height;
            var origin = new Vector2(area.x + (area.width - gw) / 2f, area.y + (area.height - gh) / 2f);
            // World y grows upward; the screen grows downward.
            Rect Cell(int cx, int cy) => new Rect(origin.x + cx * cell, origin.y + (map.Height - 1 - cy) * cell, cell, cell);
            Vector2 Point(double wx, double wy) => new Vector2(origin.x + (float)wx * cell, origin.y + (map.Height - (float)wy) * cell);

            var art = view?.ArtOf(map.Id);
            var furniture = art != null && game.Furniture.IsArrangeable(map.Id) ? art.Furnished(game.Furniture.Placed(map.Id), game.Furniture.Room(map.Id)) : null;
            var edge = Mathf.Max(1f, skin.U);
            Fill(new Rect(origin.x - edge, origin.y - edge, gw + 2 * edge, gh + 2 * edge), Color.black);
            for (var cy = 0; cy < map.Height; cy++)
                for (var cx = 0; cx < map.Width; cx++)
                {
                    var kind = World.CellAt(map, content.Bed, cx, cy);
                    var c = Cell(cx, cy);
                    var colour = (furniture != null && furniture.TryGetValue((cx, cy), out var painted) ? painted : art?.CellColor(cx, cy)) ?? NoArtColor;
                    Fill(c, kind == MapCell.Wall ? Shade(colour) : colour);
                    if (kind != MapCell.Wall) Outline(c, GridColor, 1);
                    if (kind == MapCell.Door) Marker(c, DoorColor, 0.46f, edge);
                    else if (kind == MapCell.Bed) Marker(c, BedColor, 0.46f, edge);
                }

            var small = skin.Line(skin.Small);
            foreach (var portal in map.Portals)
            {
                var to = portal.To.MapId;
                var name = game.State.VisitedMaps.Contains(to) ? content.MapName(to) : "???";
                // Labels sit outside the grid on the door's side so they never cover a marker.
                int px = (int)Math.Floor(portal.X), py = (int)Math.Floor(portal.Y);
                var text = "→ " + name;
                var size = small.CalcSize(new GUIContent(text));
                var c = Cell(px, py);
                var plate = new Color(0.25f, 0.18f, 0.02f, 0.9f);
                if (px == 0) Plate(new Rect(origin.x - size.x - 12, c.center.y - size.y / 2, size.x + 6, size.y), text, small, plate);
                else if (px == map.Width - 1) Plate(new Rect(origin.x + gw + 6, c.center.y - size.y / 2, size.x + 6, size.y), text, small, plate);
                else if (py == 0) Plate(new Rect(c.center.x - size.x / 2 - 3, origin.y + gh + 4, size.x + 6, size.y), text, small, plate);
                else Plate(new Rect(c.center.x - size.x / 2 - 3, origin.y - size.y - 4, size.x + 6, size.y), text, small, plate);
            }

            var mark = Mathf.Max(4f, cell * 0.4f);
            foreach (var t in content.MagicTargets.Where(t => t.MapId == map.Id))
            {
                var p = Point(t.X, t.Y);
                var box = new Rect(p.x - mark / 2, p.y - mark / 2, mark, mark);
                Fill(new Rect(box.x - 1, box.y - 1, box.width + 2, box.height + 2), Ink);
                Fill(box, TargetColor);
            }
            foreach (var npc in game.Npcs.Where(n => n.MapId == map.Id))
            {
                var p = Point(npc.X, npc.Y);
                var plate = DrawFace(p, view?.Face(npc.Id), cell, NpcColor);
                var def = content.Npcs.FirstOrDefault(n => n.Id == npc.Id);
                Tag(new Vector2(p.x, plate.yMax + 1), def?.Name ?? npc.Id, small, new Color(0f, 0.12f, 0.22f, 0.9f));
            }
            if (game.Player.MapId == map.Id) DrawPlayer(Point(game.Player.X, game.Player.Y), view?.Face(game.Player.Id), cell, game.Player.Facing);
        }

        static Color Shade(Color c) => new Color(c.r * BlockedShade, c.g * BlockedShade, c.b * BlockedShade, 1f);

        /// <summary>A square of the given share of the cell, ringed in ink, centred in the cell.</summary>
        static void Marker(Rect c, Color colour, float share, float edge)
        {
            var s = Mathf.Max(3f, Mathf.Floor(c.width * share));
            var box = new Rect(Mathf.Round(c.center.x - s / 2), Mathf.Round(c.center.y - s / 2), s, s);
            Fill(new Rect(box.x - edge, box.y - edge, s + 2 * edge, s + 2 * edge), Ink);
            Fill(box, colour);
        }

        /// <summary>
        /// A face on a plate centred at p, drawn at the whole-number scale of the head crop that makes it about a cell and a third
        /// tall. Returns the plate's outer rectangle. Without a face the plate holds a flat square in the rim colour.
        /// </summary>
        static Rect DrawFace(Vector2 p, Texture2D face, float cell, Color rim)
        {
            var scale = face == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(cell * 1.3f / face.height));
            var w = face == null ? Mathf.Max(8f, cell * 0.6f) : face.width * scale;
            var h = face == null ? w : face.height * scale;
            var rect = new Rect(Mathf.Round(p.x - w / 2), Mathf.Round(p.y - h / 2), w, h);
            Fill(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), Ink);
            Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), rim);
            Fill(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), FacePlate);
            if (face != null) GUI.DrawTexture(rect, face, ScaleMode.StretchToFill, true);
            else Fill(rect, rim);
            return new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6);
        }

        static void DrawPlayer(Vector2 p, Texture2D face, float cell, string facing)
        {
            var plate = DrawFace(p, face, cell, PlayerRim);
            // A ring that pulses so the player finds themself at a glance.
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            var ring = Mathf.Max(plate.width, plate.height) + 6f + pulse * 10f;
            Outline(new Rect(p.x - ring / 2, p.y - ring / 2, ring, ring), new Color(PlayerRim.r, PlayerRim.g, PlayerRim.b, 0.4f + 0.55f * pulse), 2);
            var tick = Mathf.Max(3f, plate.height * 0.2f);
            var d = facing == "north" ? new Vector2(0, -1) : facing == "south" ? new Vector2(0, 1) : facing == "east" ? new Vector2(1, 0) : new Vector2(-1, 0);
            var reach = Mathf.Abs(d.x) * plate.width / 2 + Mathf.Abs(d.y) * plate.height / 2;
            var c = p + d * (reach + tick / 2 + 3);
            Fill(new Rect(c.x - tick / 2 - 1, c.y - tick / 2 - 1, tick + 2, tick + 2), Ink);
            Fill(new Rect(c.x - tick / 2, c.y - tick / 2, tick, tick), PlayerRim);
        }

        void DrawLegend(Rect r, Core.Game game, UiSkin skin, WorldView view)
        {
            var small = skin.Line(skin.Small);
            var x = r.x;
            var sw = Mathf.Min(r.height - 2, small.lineHeight * 0.9f);
            void Label(string label)
            {
                var size = small.CalcSize(new GUIContent(label));
                small.normal.textColor = UiSkin.Text;
                GUI.Label(UiSkin.Snap(new Rect(x, r.y + (r.height - size.y) / 2, size.x + 2, size.y)), label, small);
                x += size.x + 14;
            }
            void FaceItem(Texture2D face, Color rim, string label)
            {
                var h = face == null ? sw : r.height - 4;
                var w = face == null ? sw : h * face.width / face.height;
                var box = new Rect(x + 1, r.y + (r.height - h) / 2, w, h);
                Fill(new Rect(box.x - 1, box.y - 1, box.width + 2, box.height + 2), rim);
                Fill(box, FacePlate);
                if (face != null) GUI.DrawTexture(box, face, ScaleMode.StretchToFill, true);
                else Fill(box, rim);
                x += w + 6;
                Label(label);
            }
            void Swatch(Color colour, string label, bool marker)
            {
                var box = new Rect(x, r.y + (r.height - sw) / 2, sw, sw);
                Fill(box, marker ? NoArtColor : colour);
                if (marker) Marker(box, colour, 0.5f, 1f);
                Outline(box, Color.white, 1);
                x += sw + 4;
                Label(label);
            }
            var friend = game.Npcs.Select(n => view?.Face(n.Id)).FirstOrDefault(f => f != null);
            FaceItem(view?.Face(game.Player.Id), PlayerRim, "나");
            FaceItem(friend, NpcColor, "친구");
            Swatch(DoorColor, "문", true);
            Swatch(BedColor, "잠자리", true);
            Swatch(TargetColor, "마법 대상", false);
            Swatch(Shade(NoArtColor), "막힌 곳(어둡게)", false);
        }

        static string Facing(string f) => f == "north" ? "북" : f == "south" ? "남" : f == "east" ? "동" : "서";

        /// <summary>A label centered on top-center point at, on a dark plate.</summary>
        static void Tag(Vector2 at, string text, GUIStyle style, Color plate)
        {
            var size = style.CalcSize(new GUIContent(text));
            Plate(new Rect(at.x - size.x / 2 - 3, at.y, size.x + 6, size.y), text, style, plate);
        }

        static void Plate(Rect box, string text, GUIStyle style, Color plate)
        {
            Fill(box, plate);
            style.normal.textColor = Color.white;
            GUI.Label(UiSkin.Snap(new Rect(box.x + 3, box.y, box.width - 6, box.height)), text, style);
        }

        public static void Fill(Rect r, Color c) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, 0, 0);

        public static void Outline(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
    }
}
