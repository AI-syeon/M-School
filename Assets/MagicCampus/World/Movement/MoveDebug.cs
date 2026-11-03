using System;
using MagicCampus.Contracts;

namespace MagicCampus.World.Movement
{
    /// <summary>Read-only view of one running move, for tests and diagnostics. Never used for decisions.</summary>
    public sealed class MoveDebugInfo
    {
        public MoveDebugInfo(string operationId, string actorId, string ownerToken, Position position, string phase,
            int waypointsRemaining, int replans, double elapsedUnpausedSeconds, double headingX, double headingY)
        {
            OperationId = operationId;
            ActorId = actorId;
            OwnerToken = ownerToken;
            Position = position;
            Phase = phase;
            WaypointsRemaining = waypointsRemaining;
            Replans = replans;
            ElapsedUnpausedSeconds = elapsedUnpausedSeconds;
            HeadingX = headingX;
            HeadingY = headingY;
        }

        public string OperationId { get; }
        public string ActorId { get; }
        public string OwnerToken { get; }
        public Position Position { get; }
        public string Phase { get; }
        public int WaypointsRemaining { get; }
        public int Replans { get; }
        public double ElapsedUnpausedSeconds { get; }
        public double HeadingX { get; }   // unit vector of the last step; (0,0) before the first step
        public double HeadingY { get; }
    }

    public sealed class MoveTraceEntry
    {
        public MoveTraceEntry(long sequence, string operationId, string actorId, string kind, string detail)
        {
            Sequence = sequence;
            OperationId = operationId ?? string.Empty;
            ActorId = actorId ?? string.Empty;
            Kind = kind ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public long Sequence { get; }
        public string OperationId { get; }
        public string ActorId { get; }
        public string Kind { get; }
        public string Detail { get; }
    }
}
