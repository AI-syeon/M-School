using System;
using MagicCampus.Contracts;
using MagicCampus.UnityAdapters.Maps;

namespace MagicCampus.UnityAdapters.Collision
{
    /// <summary>
    /// ICollisionPort over the logical MapLayout grid (blocked tiles, closed portal doors, map bounds), for any known map, loaded or not.
    /// Sweep is a continuous swept-circle test against every blocked cell (rounded-rectangle time of impact), so a large delta cannot tunnel.
    /// It stops just before the first obstruction (1e-6 tiles of skin) and reports the blocker: tile:x,y / portal:id / bounds.
    /// Touching is allowed (a circle of diameter 1 passes a 1-tile doorway). A start that already overlaps an obstruction returns Blocked at the start.
    /// An unknown map is fully blocked (IsPassable false; Sweep blocked at the start with blocker map:id). Tile (x,y) covers [x,x+1) x [y,y+1).
    /// </summary>
    public sealed class GridCollisionPort : ICollisionPort
    {
        public const string BoundsBlocker = "bounds";
        private const double Eps = 1e-9, Skin = 1e-6, Reach = 1e6;

        private readonly MapLayout layout;

        public GridCollisionPort(MapLayout layout)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public bool IsPassable(Position at, double radius)
        {
            CheckRadius(radius);
            CheckPlaced(at, nameof(at));
            var g = layout.Find(at.MapId);
            return g != null && !Overlap(g, at.X, at.Y, radius, out _);
        }

        public SweepResult Sweep(Position from, Position to, double radius)
        {
            CheckRadius(radius);
            CheckPlaced(from, nameof(from));
            CheckPlaced(to, nameof(to));
            if (from.MapId != to.MapId) throw new ArgumentException("positions must be on the same map", nameof(to));
            var g = layout.Find(from.MapId);
            if (g == null) return new SweepResult(from, true, "map:" + from.MapId);
            string blocker;
            if (Overlap(g, from.X, from.Y, radius, out blocker)) return new SweepResult(from, true, blocker);

            double dx = to.X - from.X, dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (double.IsNaN(len) || double.IsInfinity(len)) throw new ArgumentOutOfRangeException(nameof(to), "sweep too long");
            if (len <= 0) return new SweepResult(to, false, string.Empty);

            var best = double.PositiveInfinity;
            string bestId = null;
            var w = (double)g.Width;
            var h = (double)g.Height;

            var wall = Wall(from.X, from.Y, dx, dy, len, radius, -Reach, -Reach, 0, h + Reach);
            if (wall < best) { best = wall; bestId = BoundsBlocker; }
            wall = Wall(from.X, from.Y, dx, dy, len, radius, w, -Reach, w + Reach, h + Reach);
            if (wall < best) { best = wall; bestId = BoundsBlocker; }
            wall = Wall(from.X, from.Y, dx, dy, len, radius, -Reach, -Reach, w + Reach, 0);
            if (wall < best) { best = wall; bestId = BoundsBlocker; }
            wall = Wall(from.X, from.Y, dx, dy, len, radius, -Reach, h, w + Reach, h + Reach);
            if (wall < best) { best = wall; bestId = BoundsBlocker; }

            var x0 = Math.Max(0, Cell(Math.Min(from.X, to.X) - radius));
            var x1 = Math.Min(g.Width - 1, Cell(Math.Max(from.X, to.X) + radius));
            var y0 = Math.Max(0, Cell(Math.Min(from.Y, to.Y) - radius));
            var y1 = Math.Min(g.Height - 1, Cell(Math.Max(from.Y, to.Y) + radius));
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    if (!layout.IsBlocked(from.MapId, x, y)) continue;
                    var t = Toi(from.X, from.Y, dx, dy, len, radius, x, y, x + 1.0, y + 1.0);
                    if (t < best) { best = t; bestId = layout.BlockerAt(from.MapId, x, y); }
                }

            if (double.IsPositiveInfinity(best)) return new SweepResult(to, false, string.Empty);
            var stop = Math.Max(0.0, best - Skin / len);
            return new SweepResult(new Position(from.MapId, from.X + dx * stop, from.Y + dy * stop), true, bestId);
        }

        // ------------------------------------------------------------ static overlap

        private bool Overlap(MapGeometry g, double px, double py, double r, out string blocker)
        {
            blocker = null;
            if (px - r < -Eps || px + r > g.Width + Eps || py - r < -Eps || py + r > g.Height + Eps)
            {
                blocker = BoundsBlocker;
                return true;
            }
            var x0 = Math.Max(0, Cell(px - r));
            var x1 = Math.Min(g.Width - 1, Cell(px + r));
            var y0 = Math.Max(0, Cell(py - r));
            var y1 = Math.Min(g.Height - 1, Cell(py + r));
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    if (!layout.IsBlocked(g.MapId, x, y) || !OverlapsBox(px, py, r, x, y)) continue;
                    blocker = layout.BlockerAt(g.MapId, x, y);
                    return true;
                }
            return false;
        }

        private static bool OverlapsBox(double px, double py, double r, double bx, double by)
        {
            var nx = Math.Max(bx, Math.Min(px, bx + 1.0));
            var ny = Math.Max(by, Math.Min(py, by + 1.0));
            var dx = px - nx;
            var dy = py - ny;
            var d2 = dx * dx + dy * dy;
            if (d2 == 0)
            {
                if (r > Eps) return true;
                return px > bx && px < bx + 1.0 && py > by && py < by + 1.0;
            }
            return Math.Sqrt(d2) < r - Eps;
        }

        // ------------------------------------------------------------ time of impact (open shapes: touching is not a hit)

        private static double Wall(double px, double py, double dx, double dy, double len, double r, double x0, double y0, double x1, double y1) =>
            Slab(px, py, dx, dy, len, x0 - r, y0 - r, x1 + r, y1 + r);

        private static double Toi(double px, double py, double dx, double dy, double len, double r, double x0, double y0, double x1, double y1)
        {
            var t = Slab(px, py, dx, dy, len, x0 - r, y0, x1 + r, y1);
            t = Math.Min(t, Slab(px, py, dx, dy, len, x0, y0 - r, x1, y1 + r));
            if (r > 0)
            {
                t = Math.Min(t, Disc(px, py, dx, dy, len, r, x0, y0));
                t = Math.Min(t, Disc(px, py, dx, dy, len, r, x1, y0));
                t = Math.Min(t, Disc(px, py, dx, dy, len, r, x0, y1));
                t = Math.Min(t, Disc(px, py, dx, dy, len, r, x1, y1));
            }
            return t;
        }

        private static double Slab(double px, double py, double dx, double dy, double len, double x0, double y0, double x1, double y1)
        {
            double enter = double.NegativeInfinity, exit = double.PositiveInfinity;
            if (!Axis(px, dx, x0, x1, ref enter, ref exit)) return double.PositiveInfinity;
            if (!Axis(py, dy, y0, y1, ref enter, ref exit)) return double.PositiveInfinity;
            return Window(enter, exit, len);
        }

        private static bool Axis(double p, double d, double lo, double hi, ref double enter, ref double exit)
        {
            if (d == 0) return p > lo && p < hi;
            var a = (lo - p) / d;
            var b = (hi - p) / d;
            if (a > b) { var swap = a; a = b; b = swap; }
            if (a > enter) enter = a;
            if (b < exit) exit = b;
            return true;
        }

        private static double Disc(double px, double py, double dx, double dy, double len, double r, double cx, double cy)
        {
            var ox = px - cx;
            var oy = py - cy;
            var a = dx * dx + dy * dy;
            var b = 2 * (dx * ox + dy * oy);
            var c = ox * ox + oy * oy - r * r;
            var disc = b * b - 4 * a * c;
            if (disc <= 0) return double.PositiveInfinity;
            var s = Math.Sqrt(disc);
            return Window((-b - s) / (2 * a), (-b + s) / (2 * a), len);
        }

        /// <summary>Entry time when the path spends more than Eps tiles inside the shape within [0,1], otherwise infinity.</summary>
        private static double Window(double enter, double exit, double len)
        {
            var e = Math.Max(enter, 0.0);
            var x = Math.Min(exit, 1.0);
            return (x - e) * len > Eps ? e : double.PositiveInfinity;
        }

        private static int Cell(double v) => (int)Math.Max(-1e9, Math.Min(1e9, Math.Floor(v)));

        private static void CheckRadius(double radius)
        {
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        }

        private static void CheckPlaced(Position p, string name)
        {
            if (p.MapId == null) throw new ArgumentException("default position", name);
        }
    }
}
