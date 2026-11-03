using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.World.Navigation
{
    public enum PathStatus { Found, StartBlocked, TargetBlocked, NoRoute }

    /// <summary>Immutable planner answer. Found with no waypoints means the start already satisfies the goal.</summary>
    public sealed class PathPlan
    {
        public PathStatus Status { get; }
        public IReadOnlyList<Position> Waypoints { get; }
        public string Detail { get; }
        public int Expanded { get; }

        private PathPlan(PathStatus status, Position[] waypoints, string detail, int expanded)
        {
            Status = status;
            Waypoints = Array.AsReadOnly(waypoints);
            Detail = detail;
            Expanded = expanded;
        }

        public static PathPlan Found(IEnumerable<Position> waypoints, int expanded)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            return new PathPlan(PathStatus.Found, new List<Position>(waypoints).ToArray(), string.Empty, expanded);
        }

        public static PathPlan Failure(PathStatus status, string detail, int expanded)
        {
            if (status == PathStatus.Found) throw new ArgumentException("failure status required", nameof(status));
            return new PathPlan(status, new Position[0], detail ?? string.Empty, expanded);
        }
    }
}
