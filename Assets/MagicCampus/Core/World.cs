using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCampus.Core
{
    /// <summary>What a map cell is, for drawing: the world tilemap and the map screen read the same answer.</summary>
    public enum MapCell { Floor, Wall, Door, Bed }

    /// <summary>
    /// Tile rules (R7). A cell is (floor x, floor y). Stepping into a portal cell moves the actor to the portal's arrival;
    /// standing on an arrival cell does nothing because only a cell change triggers a portal.
    /// </summary>
    public static class World
    {
        public const double PlayerRadius = 0.2;

        public static bool Passable(MapDef map, int cx, int cy)
        {
            return cx >= 0 && cy >= 0 && cx < map.Width && cy < map.Height && !map.Blocked.Any(b => b[0] == cx && b[1] == cy) && !map.Furnished.Contains((cx, cy));
        }

        public static MapCell CellAt(MapDef map, Spot bed, int cx, int cy)
        {
            if (!Passable(map, cx, cy)) return MapCell.Wall;
            if (PortalAt(map, cx, cy) != null) return MapCell.Door;
            return bed != null && bed.MapId == map.Id && Floor(bed.X) == cx && Floor(bed.Y) == cy ? MapCell.Bed : MapCell.Floor;
        }

        public static PortalDef PortalAt(MapDef map, int cx, int cy)
        {
            return map.Portals.FirstOrDefault(p => (int)Math.Floor(p.X) == cx && (int)Math.Floor(p.Y) == cy);
        }

        public static void Place(Actor actor, Spot at, string facing = null)
        {
            actor.MapId = at.MapId; actor.X = at.X; actor.Y = at.Y;
            actor.Path.Clear();
            actor.Moving = false;
            actor.Action = Actor.Idle;
            actor.Goal = null;
            if (facing != null) actor.Facing = facing;
        }

        /// <summary>Shortest 4-way tile path. Stepping into a portal cell continues from its arrival, so paths cross maps. Null when unreachable.</summary>
        public static List<Spot> FindPath(Content content, Actor actor, Spot goal)
        {
            var start = Cell(actor.MapId, actor.X, actor.Y);
            var end = Cell(goal.MapId, goal.X, goal.Y);
            var cameFrom = new Dictionary<(string, int, int), ((string, int, int) From, Spot[] Steps)> { [start] = (start, new Spot[0]) };
            var queue = new Queue<(string, int, int)>(new[] { start });
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node.Equals(end))
                {
                    var path = new List<Spot>();
                    for (var at = end; !at.Equals(start); at = cameFrom[at].From) path.InsertRange(0, cameFrom[at].Steps);
                    if (path.Count > 0) path.RemoveAt(path.Count - 1);
                    path.Add(goal);
                    return path;
                }
                var map = content.Map(node.Item1);
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int x = node.Item2 + dx, y = node.Item3 + dy;
                    if (!Passable(map, x, y)) continue;
                    var portal = PortalAt(map, x, y);
                    var step = new Spot { MapId = map.Id, X = x + 0.5, Y = y + 0.5 };
                    var next = portal == null ? (map.Id, x, y) : Cell(portal.To.MapId, portal.To.X, portal.To.Y);
                    if (cameFrom.ContainsKey(next)) continue;
                    cameFrom[next] = (node, portal == null ? new[] { step } : new[] { step, portal.To });
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        static (string, int, int) Cell(string mapId, double x, double y) => (mapId, Floor(x), Floor(y));

        /// <summary>Sends the actor walking; whatever it was doing stops.</summary>
        public static void WalkTo(Content content, Actor actor, DestinationDef destination)
        {
            actor.Path = FindPath(content, actor, destination.Position)
                ?? throw new InvalidOperationException(actor.Id + " has no path to " + destination.Id);
            actor.Action = Actor.Idle;
            actor.Goal = null;
        }

        /// <summary>Walks the actor along its path at its speed for dt seconds.</summary>
        public static void Walk(Content content, Actor actor, double dt)
        {
            var budget = actor.Speed * dt;
            while (actor.Path.Count > 0 && budget > 0)
            {
                var target = actor.Path[0];
                if (target.MapId != actor.MapId) { actor.Path.RemoveAt(0); continue; }
                double dx = target.X - actor.X, dy = target.Y - actor.Y, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > 1e-9) actor.Facing = FacingOf(dx, dy);
                if (distance <= budget)
                {
                    budget -= distance;
                    actor.Path.RemoveAt(0);
                    MoveTo(content, actor, target.X, target.Y);
                }
                else
                {
                    MoveTo(content, actor, actor.X + dx / distance * budget, actor.Y + dy / distance * budget);
                    budget = 0;
                }
            }
            actor.Moving = actor.Path.Count > 0;
        }

        /// <summary>Direct movement from input (-1..1 per axis), resolved one axis at a time against blocked cells.</summary>
        public static void MovePlayer(Content content, Actor player, double inputX, double inputY, double dt)
        {
            var length = Math.Sqrt(inputX * inputX + inputY * inputY);
            player.Moving = length > 1e-6;
            if (!player.Moving) return;
            player.Facing = FacingOf(inputX, inputY);
            var scale = player.Speed * dt / Math.Max(length, 1);
            var map = content.Map(player.MapId);
            if (Free(map, player.X + inputX * scale, player.Y) && MoveTo(content, player, player.X + inputX * scale, player.Y)) return;
            if (Free(map, player.X, player.Y + inputY * scale)) MoveTo(content, player, player.X, player.Y + inputY * scale);
        }

        static bool Free(MapDef map, double x, double y)
        {
            const double r = PlayerRadius;
            return Passable(map, Floor(x - r), Floor(y - r)) && Passable(map, Floor(x + r), Floor(y - r))
                && Passable(map, Floor(x - r), Floor(y + r)) && Passable(map, Floor(x + r), Floor(y + r));
        }

        /// <summary>Sets the position; returns true when the move entered a portal cell and warped.</summary>
        static bool MoveTo(Content content, Actor actor, double x, double y)
        {
            int oldX = Floor(actor.X), oldY = Floor(actor.Y);
            actor.X = x; actor.Y = y;
            if (Floor(x) == oldX && Floor(y) == oldY) return false;
            var portal = PortalAt(content.Map(actor.MapId), Floor(x), Floor(y));
            if (portal == null) return false;
            actor.MapId = portal.To.MapId; actor.X = portal.To.X; actor.Y = portal.To.Y;
            return true;
        }

        public static string FacingOf(double dx, double dy)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx > 0 ? "east" : "west";
            return dy > 0 ? "north" : "south";
        }

        public static double Distance(Actor actor, Spot spot)
        {
            if (actor.MapId != spot.MapId) return double.PositiveInfinity;
            return Math.Sqrt((actor.X - spot.X) * (actor.X - spot.X) + (actor.Y - spot.Y) * (actor.Y - spot.Y));
        }

        static int Floor(double v) => (int)Math.Floor(v);
    }
}
