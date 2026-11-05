using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.World.Movement
{
    public sealed class ActorState
    {
        public ActorState(string actorId, Position position, double radius, double speed)
        {
            Ids.Require(actorId, nameof(actorId));
            PortGuard.Placed(position, nameof(position));
            PortGuard.NonNegative(radius, nameof(radius));
            PortGuard.Positive(speed, nameof(speed));
            if (double.IsNaN(radius) || double.IsInfinity(radius))
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (double.IsNaN(speed) || double.IsInfinity(speed))
                throw new ArgumentOutOfRangeException(nameof(speed));
            ActorId = actorId;
            Position = position;
            Radius = radius;
            Speed = speed;
        }

        public string ActorId { get; }
        public Position Position { get; }
        public double Radius { get; }
        public double Speed { get; }

        public ActorState WithPosition(Position position)
        {
            return new ActorState(ActorId, position, Radius, Speed);
        }
    }

    public interface IActorWorld
    {
        ActorState Find(string id);
        IReadOnlyList<ActorState> All { get; }
        Result SetPosition(string actorId, Position position, string ownerToken);
        // Initialization/restore only, and only at a safe boundary.
        Result Restore(IReadOnlyList<ActorState> actors);
    }
}
