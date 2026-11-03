using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Application.Schedules
{
    public sealed class ReservedActorPlacement
    {
        public string ActorId { get; }
        public Position Position { get; }

        public ReservedActorPlacement(string actorId, Position position)
        {
            ActorId = Ids.Require(actorId, nameof(actorId));
            if (position.MapId == null) throw new ArgumentException("default position", nameof(position));
            Position = position;
        }
    }

    /// <summary>Explicit join policy result for reserved staging: the map to show and where each joined actor stands on it.</summary>
    public sealed class ReservedJoinPlan
    {
        public string TargetMapId { get; }
        public IReadOnlyList<ReservedActorPlacement> Placements { get; }   // ordinal by actor id, unique

        public ReservedJoinPlan(string targetMapId, IEnumerable<ReservedActorPlacement> placements)
        {
            TargetMapId = Ids.Require(targetMapId, nameof(targetMapId));
            if (placements == null) throw new ArgumentNullException(nameof(placements));
            var sorted = new SortedDictionary<string, ReservedActorPlacement>(StringComparer.Ordinal);
            foreach (var p in placements)
            {
                if (p == null) throw new ArgumentException("null placement", nameof(placements));
                if (p.Position.MapId != targetMapId) throw new ArgumentException("placement is not on the target map", nameof(placements));
                if (sorted.ContainsKey(p.ActorId)) throw new ArgumentException("duplicate actor", nameof(placements));
                sorted.Add(p.ActorId, p);
            }
            Placements = new List<ReservedActorPlacement>(sorted.Values).AsReadOnly();
        }
    }
}
