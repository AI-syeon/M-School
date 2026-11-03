using System;
using System.Collections.Generic;

namespace MagicCampus.Contracts
{
    // Producer-side view of a handle. Only the first terminal call returns true; later calls return false and change nothing.
    public interface IHandleController<T> : IHandle<T>
    {
        void Start();
        bool Succeed(T value, long revision);
        bool Fail(string code, long revision, IReadOnlyList<Diagnostic> details);
        // Cancellation status is only reached through this explicit finalizer, never through Cancel(string) alone.
        bool Cancelled(string reason, long revision);
        // The handler must release children/resources before any terminal state is published.
        void SetCancellationHandler(Action<string> handler);
    }

    public interface IHandleFactory
    {
        IHandleController<T> Create<T>(string operationId);
    }

    public interface IRuntimeTick
    {
        void Tick(double simulationSeconds, double unpausedSeconds);
    }

    public interface ICompletionPump : ICompletionQueue
    {
        // Drains a snapshot of the queue taken at entry, in deterministic order.
        // Items posted while draining are deferred to the next AdvanceTick.
        void AdvanceTick();
    }

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

    // Implemented by World/Movement (stage 3). Only MoveService writes simulation positions.
    // Readers see the latest immutable simulation snapshot. Persistence is projected only at a safe boundary.
    // A failed SetPosition keeps the last valid position.
    public interface IActorWorld
    {
        ActorState Find(string id);
        IReadOnlyList<ActorState> All { get; }
        Result SetPosition(string actorId, Position position, string ownerToken);
        // Initialization/restore only, and only at a safe boundary.
        Result Restore(IReadOnlyList<ActorState> actors);
    }

    public sealed class MapGeometry
    {
        public MapGeometry(string mapId, int width, int height)
        {
            Ids.Require(mapId, nameof(mapId));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            MapId = mapId;
            Width = width;
            Height = height;
        }

        public string MapId { get; }
        public int Width { get; }
        public int Height { get; }

        // Valid coordinates are [0,Width) x [0,Height).
        public bool Contains(double x, double y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }
    }

    public interface IMapGeometry
    {
        // Returns null when the map is unknown.
        MapGeometry Find(string mapId);
    }

    // Injected into the store separately from slice participants; checked before any state replacement.
    public interface IStateInvariant
    {
        IReadOnlyList<Diagnostic> Validate(StateSnapshot candidate);
    }

    // Scene-entry recovery only: owns the entry snapshot plus volatile actor positions (IActorWorld)
    // and retains Accepted reservations. Not file IO and not a global room rollback.
    public interface ISceneRecovery
    {
        Result CaptureEntry(StateSnapshot acceptedSnapshot, string ownerToken);
        Outcome<StateSnapshot> RestoreEntry(string ownerToken);
        Result ClearEntry(string ownerToken);
    }

    public interface IDataCodec
    {
        // File hashes are computed by the caller on the exact original bytes, never on re-serialized DataNode output.
        Outcome<ContentBundle> Parse(string file, ReadOnlyMemory<byte> utf8);
        ReadOnlyMemory<byte> Serialize(DataNode root);
    }
}
