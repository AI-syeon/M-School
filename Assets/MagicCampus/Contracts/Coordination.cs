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

    // Implemented by World/Movement (stage 3). Only MoveService writes simulation positions.
    // Readers see the latest immutable simulation snapshot. Persistence is projected only at a safe boundary.
    // A failed SetPosition keeps the last valid position.

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

    public interface IDataCodec
    {
        // File hashes are computed by the caller on the exact original bytes, never on re-serialized DataNode output.
        Outcome<ContentBundle> Parse(string file, ReadOnlyMemory<byte> utf8);
        ReadOnlyMemory<byte> Serialize(DataNode root);
    }
}
