using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.World.Navigation
{
    /// <summary>
    /// Map-level route over open portals (Dijkstra on map graph, distance between portal entry and exit points).
    /// Only portal availability is judged here; walkability inside each map is planned by GridPathPlanner when the actor is on it.
    /// </summary>
    public sealed class PortalRouter
    {
        private readonly IMapPort maps;
        private readonly IMapGeometry geometry;

        public PortalRouter(IMapPort maps, IMapGeometry geometry)
        {
            this.maps = maps ?? throw new ArgumentNullException(nameof(maps));
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        }

        /// <summary>Ordered portals to cross from start to targetMapId; empty on the same map; null when no open route exists.</summary>
        public IReadOnlyList<Portal> Route(Position start, string targetMapId)
        {
            if (start.MapId == null) throw new ArgumentException("default position", nameof(start));
            if (string.IsNullOrEmpty(targetMapId)) throw new ArgumentException("target map required", nameof(targetMapId));
            if (string.Equals(start.MapId, targetMapId, StringComparison.Ordinal)) return new Portal[0];
            if (geometry.Find(targetMapId) == null) return null;

            var dist = new Dictionary<string, double>(StringComparer.Ordinal);
            var entry = new Dictionary<string, Position>(StringComparer.Ordinal);
            var via = new Dictionary<string, Portal>(StringComparer.Ordinal);
            var prev = new Dictionary<string, string>(StringComparer.Ordinal);
            var settled = new HashSet<string>(StringComparer.Ordinal);
            dist[start.MapId] = 0;
            entry[start.MapId] = start;

            while (true)
            {
                string u = null;
                var best = double.PositiveInfinity;
                foreach (var kv in dist)
                {
                    if (settled.Contains(kv.Key)) continue;
                    if (u == null || kv.Value < best || (kv.Value == best && string.CompareOrdinal(kv.Key, u) < 0))
                    {
                        u = kv.Key;
                        best = kv.Value;
                    }
                }
                if (u == null) return null;
                if (string.Equals(u, targetMapId, StringComparison.Ordinal)) break;
                settled.Add(u);

                var portals = new List<Portal>();
                var listed = maps.PortalsOn(u);
                if (listed != null)
                    foreach (var p in listed)
                        if (p != null) portals.Add(p);
                portals.Sort((a, b) => string.CompareOrdinal(a.PortalId, b.PortalId));

                foreach (var p in portals)
                {
                    if (!p.Open || !string.Equals(p.From.MapId, u, StringComparison.Ordinal)) continue;
                    var v = p.Arrival.MapId;
                    if (string.Equals(v, u, StringComparison.Ordinal) || settled.Contains(v) || geometry.Find(v) == null) continue;
                    var nd = best + NavMath.Distance(entry[u], p.From);
                    double old;
                    if (!dist.TryGetValue(v, out old) || nd < old - 1e-12)
                    {
                        dist[v] = nd;
                        entry[v] = p.Arrival;
                        via[v] = p;
                        prev[v] = u;
                    }
                }
            }

            var route = new List<Portal>();
            for (var m = targetMapId; !string.Equals(m, start.MapId, StringComparison.Ordinal); m = prev[m]) route.Add(via[m]);
            route.Reverse();
            return route;
        }
    }
}
