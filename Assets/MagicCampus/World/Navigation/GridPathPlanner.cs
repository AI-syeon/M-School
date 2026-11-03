using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.World.Navigation
{
    /// <summary>
    /// Footprint-aware 8-way A* over the tile centres of one map. Every step is checked with the collision port
    /// (cell passability plus a swept move), so a returned path never crosses a wall for the given radius.
    /// The search is bounded by the cell count and by NavigationOptions.MaxExpansions.
    /// </summary>
    public sealed class GridPathPlanner
    {
        private const double ExactReach = 2.0;
        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly double Diagonal = Math.Sqrt(2.0);

        private readonly ICollisionPort collision;
        private readonly IMapGeometry geometry;
        private readonly NavigationOptions options;

        public GridPathPlanner(ICollisionPort collision, IMapGeometry geometry, NavigationOptions options = null)
        {
            this.collision = collision ?? throw new ArgumentNullException(nameof(collision));
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            this.options = options ?? NavigationOptions.Default;
        }

        /// <summary>Plans on the map shared by start and goal. The goal counts as reached within tolerance tiles.</summary>
        public PathPlan Plan(Position start, Position goal, double tolerance, double radius)
        {
            if (start.MapId == null) throw new ArgumentException("default position", nameof(start));
            if (goal.MapId == null) throw new ArgumentException("default position", nameof(goal));
            if (!string.Equals(start.MapId, goal.MapId, StringComparison.Ordinal)) throw new ArgumentException("positions must share a map", nameof(goal));
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));

            var map = geometry.Find(start.MapId);
            if (map == null) return PathPlan.Failure(PathStatus.NoRoute, "MAP_UNKNOWN", 0);
            if (!map.Contains(start.X, start.Y) || !collision.IsPassable(start, radius))
                return PathPlan.Failure(PathStatus.StartBlocked, "START_BLOCKED", 0);

            var search = new Search(collision, map, start.MapId, radius);
            var goalPassable = map.Contains(goal.X, goal.Y) && collision.IsPassable(goal, radius);
            if (!goalPassable && !HasStandingCell(search, goal, tolerance))
                return PathPlan.Failure(PathStatus.TargetBlocked, "TARGET_BLOCKED", 0);
            if (NavMath.Distance(start, goal) <= tolerance) return PathPlan.Found(new Position[0], 0);

            var width = map.Width;
            var height = map.Height;
            var cellCount = (long)width * height;
            if (cellCount > options.MaxCells) return PathPlan.Failure(PathStatus.NoRoute, "MAP_TOO_LARGE", 0);
            var n = (int)cellCount;
            var g = new double[n];
            var parent = new int[n];
            var closed = new bool[n];
            for (var i = 0; i < n; i++) { g[i] = double.PositiveInfinity; parent[i] = -1; }

            var startIdx = ClampCell(start.Y, height - 1) * width + ClampCell(start.X, width - 1);
            var heap = new Heap();
            g[startIdx] = 0;
            heap.Push(Heuristic(search.Center(startIdx), goal, tolerance), 0, startIdx);

            var bestTotal = double.PositiveInfinity;
            var bestIdx = -1;
            var bestExact = false;
            var expansions = 0;

            while (heap.Count > 0)
            {
                if (heap.PeekPriority() >= bestTotal - NavMath.Epsilon) break;
                var idx = heap.Pop();
                if (closed[idx]) continue;
                closed[idx] = true;
                if (++expansions > options.MaxExpansions)
                {
                    if (bestIdx < 0) return PathPlan.Failure(PathStatus.NoRoute, "EXPANSION_LIMIT", expansions);
                    break;
                }

                var cx = idx % width;
                var cy = idx / width;
                var center = search.Center(idx);
                var isStart = idx == startIdx;
                var origin = isStart ? start : center;

                if (NavMath.Distance(center, goal) <= tolerance && search.CellPassable(cx, cy) && (!isStart || search.CanStep(start, center)))
                {
                    var total = g[idx] + (isStart ? NavMath.Distance(start, center) : 0.0);
                    if (total < bestTotal) { bestTotal = total; bestIdx = idx; bestExact = false; }
                }
                if (goalPassable && NavMath.Distance(origin, goal) <= ExactReach && search.CanStep(origin, goal))
                {
                    var total = g[idx] + NavMath.Distance(origin, goal);
                    if (total < bestTotal) { bestTotal = total; bestIdx = idx; bestExact = true; }
                }

                for (var k = 0; k < 8; k++)
                {
                    var nx = cx + Dx[k];
                    var ny = cy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var nidx = ny * width + nx;
                    if (closed[nidx]) continue;
                    if (!search.CellPassable(nx, ny)) continue;
                    var diagonal = Dx[k] != 0 && Dy[k] != 0;
                    if (diagonal && (!search.CellPassable(cx + Dx[k], cy) || !search.CellPassable(cx, cy + Dy[k]))) continue;
                    var npos = search.Center(nidx);
                    if (!search.CanStep(origin, npos)) continue;
                    var step = isStart ? NavMath.Distance(start, npos) : (diagonal ? Diagonal : 1.0);
                    var ng = g[idx] + step;
                    if (ng < g[nidx])
                    {
                        g[nidx] = ng;
                        parent[nidx] = idx;
                        var h = Heuristic(npos, goal, tolerance);
                        heap.Push(ng + h, h, nidx);
                    }
                }
            }

            if (bestIdx < 0) return PathPlan.Failure(PathStatus.NoRoute, "NO_ROUTE", expansions);

            var cells = new List<int>();
            for (var c = bestIdx; c >= 0; c = parent[c]) cells.Add(c);
            cells.Reverse();
            var waypoints = new List<Position>();
            for (var i = 1; i < cells.Count; i++) waypoints.Add(search.Center(cells[i]));
            if (bestExact) waypoints.Add(goal);
            else if (cells.Count == 1) waypoints.Add(search.Center(bestIdx));
            return PathPlan.Found(waypoints, expansions);
        }

        private static double Heuristic(Position at, Position goal, double tolerance)
        {
            return Math.Max(0.0, NavMath.Distance(at, goal) - tolerance);
        }

        private static int ClampCell(double v, int max)
        {
            var f = Math.Floor(v);
            if (f < 0) return 0;
            if (f > max) return max;
            return (int)f;
        }

        private static bool HasStandingCell(Search search, Position goal, double tolerance)
        {
            var x0 = ClampCell(goal.X - tolerance, search.Map.Width - 1);
            var x1 = ClampCell(goal.X + tolerance, search.Map.Width - 1);
            var y0 = ClampCell(goal.Y - tolerance, search.Map.Height - 1);
            var y1 = ClampCell(goal.Y + tolerance, search.Map.Height - 1);
            for (var cy = y0; cy <= y1; cy++)
                for (var cx = x0; cx <= x1; cx++)
                {
                    var center = new Position(search.MapId, cx + 0.5, cy + 0.5);
                    if (NavMath.Distance(center, goal) <= tolerance && search.CellPassable(cx, cy)) return true;
                }
            return false;
        }

        private sealed class Search
        {
            private readonly ICollisionPort collision;
            private readonly double radius;
            private readonly sbyte[] cellState;   // 0 unknown, 1 passable, 2 blocked

            public Search(ICollisionPort collision, MapGeometry map, string mapId, double radius)
            {
                this.collision = collision;
                this.radius = radius;
                Map = map;
                MapId = mapId;
                var count = (long)map.Width * map.Height;
                cellState = new sbyte[count > int.MaxValue ? 0 : (int)count];
            }

            public MapGeometry Map { get; }
            public string MapId { get; }

            public Position Center(int idx)
            {
                return new Position(MapId, idx % Map.Width + 0.5, idx / Map.Width + 0.5);
            }

            public bool CellPassable(int cx, int cy)
            {
                if (cx < 0 || cy < 0 || cx >= Map.Width || cy >= Map.Height) return false;
                var idx = cy * Map.Width + cx;
                var s = cellState[idx];
                if (s == 0)
                {
                    s = collision.IsPassable(new Position(MapId, cx + 0.5, cy + 0.5), radius) ? (sbyte)1 : (sbyte)2;
                    cellState[idx] = s;
                }
                return s == 1;
            }

            public bool CanStep(Position from, Position to)
            {
                if (!Map.Contains(to.X, to.Y)) return false;
                if (from.Equals(to)) return true;
                var sweep = collision.Sweep(from, to, radius);
                return !sweep.Blocked
                    && string.Equals(sweep.End.MapId, to.MapId, StringComparison.Ordinal)
                    && Math.Abs(sweep.End.X - to.X) <= 1e-6
                    && Math.Abs(sweep.End.Y - to.Y) <= 1e-6;
            }
        }

        private sealed class Heap
        {
            private double[] f = new double[64];
            private double[] h = new double[64];
            private int[] id = new int[64];
            private int count;

            public int Count => count;

            public double PeekPriority() { return f[0]; }

            public void Push(double priority, double heuristic, int idx)
            {
                if (count == f.Length)
                {
                    Array.Resize(ref f, count * 2);
                    Array.Resize(ref h, count * 2);
                    Array.Resize(ref id, count * 2);
                }
                var i = count++;
                f[i] = priority; h[i] = heuristic; id[i] = idx;
                while (i > 0)
                {
                    var p = (i - 1) / 2;
                    if (!Less(i, p)) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                var top = id[0];
                count--;
                if (count > 0)
                {
                    f[0] = f[count]; h[0] = h[count]; id[0] = id[count];
                    var i = 0;
                    while (true)
                    {
                        var l = i * 2 + 1;
                        var r = l + 1;
                        var m = i;
                        if (l < count && Less(l, m)) m = l;
                        if (r < count && Less(r, m)) m = r;
                        if (m == i) break;
                        Swap(i, m);
                        i = m;
                    }
                }
                return top;
            }

            private bool Less(int a, int b)
            {
                if (f[a] != f[b]) return f[a] < f[b];
                if (h[a] != h[b]) return h[a] < h[b];
                return id[a] < id[b];
            }

            private void Swap(int a, int b)
            {
                var tf = f[a]; f[a] = f[b]; f[b] = tf;
                var th = h[a]; h[a] = h[b]; h[b] = th;
                var ti = id[a]; id[a] = id[b]; id[b] = ti;
            }
        }
    }
}
