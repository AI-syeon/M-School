using System;
using MagicCampus.Contracts;

namespace MagicCampus.Persistence
{
    public enum SafeBoundary { WorldSafe, SceneEntry, SceneComplete, RoomSafe }

    /// <summary>File bytes envelope. Checksum is lowercase hex SHA-256 of Payload (UTF-8 JSON of the full save v2 object).</summary>
    public sealed class SaveEnvelope
    {
        public int SaveVersion { get; }
        public string CatalogHash { get; }
        public long Generation { get; }
        public string Checksum { get; }
        private readonly byte[] payload;
        public ReadOnlyMemory<byte> Payload => new ReadOnlyMemory<byte>((byte[])payload.Clone());

        public SaveEnvelope(int saveVersion, string catalogHash, long generation, string checksum, ReadOnlyMemory<byte> payload)
        {
            SaveVersion = (int)PortGuard.Min(saveVersion, 1, nameof(saveVersion));
            CatalogHash = Ids.Require(catalogHash, nameof(catalogHash));
            Generation = PortGuard.Min(generation, 0, nameof(generation));
            Checksum = Ids.Require(checksum, nameof(checksum));
            this.payload = payload.ToArray();
        }
    }

    public interface ISaveService
    {
        /// <summary>Saves ISafeBoundaryPolicy.Latest. A disk failure does not undo the in-memory state.</summary>
        IHandle<FileGeneration> SaveLatestSafe();
        /// <summary>Loads the newest restorable generation and replaces the whole state.</summary>
        IHandle<FileGeneration> LoadLatest();
    }
}
