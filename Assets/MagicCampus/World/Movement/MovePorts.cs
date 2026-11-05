using System;
using MagicCampus.Contracts;

namespace MagicCampus.World.Movement
{
    public sealed class MoveRequest
    {
        public string ActorId { get; }
        public Destination Destination { get; }
        public string OwnerToken { get; }
        public double TimeoutSeconds { get; }   // unpaused seconds
        public bool UsePortals { get; }

        public MoveRequest(string actorId, Destination destination, string ownerToken, double timeoutSeconds, bool usePortals)
        {
            ActorId = Ids.Require(actorId, nameof(actorId));
            Destination = PortGuard.Required(destination, nameof(destination));
            OwnerToken = Ids.Require(ownerToken, nameof(ownerToken));
            TimeoutSeconds = PortGuard.Positive(timeoutSeconds, nameof(timeoutSeconds));
            UsePortals = usePortals;
        }
    }

    /// <summary>Arrival is judged from the actual final position, never from the request.</summary>
    public sealed class MoveArrival
    {
        public Position Actual { get; }
        public Destination Target { get; }
        public double Distance { get; }   // PositiveInfinity when on a different map
        public bool Arrived { get; }

        public MoveArrival(Position actual, Destination target)
        {
            Actual = PortGuard.Placed(actual, nameof(actual));
            Target = PortGuard.Required(target, nameof(target));
            var t = target.Target;
            Distance = actual.MapId == t.MapId ? Math.Sqrt((actual.X - t.X) * (actual.X - t.X) + (actual.Y - t.Y) * (actual.Y - t.Y)) : double.PositiveInfinity;
            Arrived = Distance <= target.Tolerance;
        }
    }

    public interface IMoveService
    {
        /// <summary>Succeeds only with Value.Arrived true. Failures: NO_ROUTE, TARGET_BLOCKED, MAP_LOAD_FAILED, TIMEOUT, CANCELLED. Never teleports.</summary>
        IHandle<MoveArrival> Move(MoveRequest request);
    }

    public interface IDestinationResolver
    {
        /// <summary>Resolves a dest id (including home) for an actor; unknown ids fail with REF_NOT_FOUND.</summary>
        Outcome<Destination> Resolve(string actorId, string destinationId);
    }
}
