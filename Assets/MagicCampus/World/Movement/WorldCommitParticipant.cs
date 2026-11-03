using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.World.Movement
{
    /// <summary>
    /// State participant owning /player/mapId, /player/x, /player/y and /npcPositions. It accepts only the world.project operation,
    /// verifies the planned coordinates against the live actor world and replaces those pointers; every other player field and
    /// every npcPositions row of an actor that is not active is preserved. Prepare never writes the store.
    /// </summary>
    public sealed class WorldCommitParticipant : IStateParticipant
    {
        private const double Same = 1e-9;

        private sealed class Spot
        {
            public string Id;
            public string MapId;
            public double X;
            public double Y;
        }

        private static readonly IReadOnlyList<string> Owned = Array.AsReadOnly(new[]
        {
            WorldProjectionProtocol.PlayerMapPointer,
            WorldProjectionProtocol.PlayerXPointer,
            WorldProjectionProtocol.PlayerYPointer,
            WorldProjectionProtocol.NpcPositionsPointer
        });

        private readonly IActorWorld world;
        private readonly IMapGeometry geometry;
        private readonly string playerActorId;

        public WorldCommitParticipant(IActorWorld world, IMapGeometry geometry, string playerActorId = "player")
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (string.IsNullOrEmpty(playerActorId)) throw new ArgumentException("player actor id is required", nameof(playerActorId));
            this.playerActorId = playerActorId;
        }

        public string ParticipantId => WorldProjectionProtocol.ParticipantId;

        public IReadOnlyList<string> OwnedSlices => Owned;

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            PlannedChange mine = null;
            foreach (var change in plan.Changes)
            {
                if (!string.Equals(change.ParticipantId, ParticipantId, StringComparison.Ordinal)) continue;
                if (!string.Equals(change.Operation, WorldProjectionProtocol.Operation, StringComparison.Ordinal))
                    return Refuse(ResultCodes.Unregistered, basis, "OPERATION_UNKNOWN", "world participant has no operation " + change.Operation);
                if (mine != null) return Refuse(ResultCodes.ContentInvalid, basis, "OPERATION_DUPLICATE", "only one world.project change is allowed per plan");
                mine = change;
            }
            if (mine == null) return Outcome<StateReplacement>.Ok(new StateReplacement(new Dictionary<string, DataNode>()), basis.Revision);

            var payload = mine.Payload;
            if (payload.Kind != DataKind.Object) return Refuse(ResultCodes.ContentInvalid, basis, "PAYLOAD_INVALID", "payload must be an object");
            foreach (var key in payload.Members.Keys)
                if (key != WorldProjectionProtocol.PlayerKey && key != WorldProjectionProtocol.NpcPositionsKey)
                    return Refuse(ResultCodes.ContentInvalid, basis, "PAYLOAD_FIELD_UNKNOWN", "unknown payload field " + key);

            Spot player = null;
            DataNode node;
            if (payload.TryGet(WorldProjectionProtocol.PlayerKey, out node))
            {
                player = ReadSpot(node, false);
                if (player == null) return Refuse(ResultCodes.ContentInvalid, basis, "PLAYER_PAYLOAD_INVALID", "player needs exactly mapId, x and y");
                player.Id = playerActorId;
            }

            List<Spot> npcs = null;
            if (payload.TryGet(WorldProjectionProtocol.NpcPositionsKey, out node))
            {
                if (node.Kind != DataKind.List) return Refuse(ResultCodes.ContentInvalid, basis, "NPC_PAYLOAD_INVALID", "npcPositions must be a list");
                npcs = new List<Spot>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in node.Items)
                {
                    var spot = ReadSpot(item, true);
                    if (spot == null) return Refuse(ResultCodes.ContentInvalid, basis, "NPC_PAYLOAD_INVALID", "npc entries need npcId, mapId, x and y");
                    if (!seen.Add(spot.Id) || string.Equals(spot.Id, playerActorId, StringComparison.Ordinal))
                        return Refuse(ResultCodes.ContentInvalid, basis, "NPC_DUPLICATE", "duplicate actor " + spot.Id);
                    npcs.Add(spot);
                }
            }

            var planned = new List<Spot>();
            if (player != null) planned.Add(player);
            if (npcs != null) planned.AddRange(npcs);
            foreach (var spot in planned)
            {
                var map = geometry.Find(spot.MapId);
                if (map == null || !map.Contains(spot.X, spot.Y))
                    return Refuse(ResultCodes.ContentInvalid, basis, "POSITION_INVALID", "planned position for " + spot.Id + " is outside a known map");
                var actor = world.Find(spot.Id);
                if (actor == null) return Refuse(ResultCodes.OutOfScope, basis, "ACTOR_NOT_ACTIVE", "actor " + spot.Id + " is not in the actor world");
                if (!string.Equals(actor.Position.MapId, spot.MapId, StringComparison.Ordinal)
                    || Math.Abs(actor.Position.X - spot.X) > Same || Math.Abs(actor.Position.Y - spot.Y) > Same)
                    return Refuse(ResultCodes.RevisionConflict, basis, "ACTOR_MOVED", "actor " + spot.Id + " is no longer at the planned position");
            }

            var slices = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            if (player != null)
            {
                var current = basis.Slice(WorldProjectionProtocol.PlayerKey);
                if (current == null || current.Kind != DataKind.Object) return Refuse(ResultCodes.ContentInvalid, basis, "PLAYER_SLICE_INVALID", "the save has no player object");
                DataNode ex;
                DataNode ey;
                current.TryGet(WorldProjectionProtocol.XKey, out ex);
                current.TryGet(WorldProjectionProtocol.YKey, out ey);
                slices[WorldProjectionProtocol.PlayerMapPointer] = DataNode.FromString(player.MapId);
                slices[WorldProjectionProtocol.PlayerXPointer] = NumberLike(player.X, ex);
                slices[WorldProjectionProtocol.PlayerYPointer] = NumberLike(player.Y, ey);
            }

            if (npcs != null)
            {
                var rows = new List<DataNode>();
                var index = new Dictionary<string, int>(StringComparer.Ordinal);
                var existing = basis.Slice(WorldProjectionProtocol.NpcPositionsKey);
                if (existing != null)
                {
                    if (existing.Kind != DataKind.List) return Refuse(ResultCodes.ContentInvalid, basis, "NPC_SLICE_INVALID", "npcPositions in the save must be a list");
                    foreach (var row in existing.Items)
                    {
                        DataNode id;
                        if (row.Kind != DataKind.Object || !row.TryGet(WorldProjectionProtocol.NpcIdKey, out id) || id.Kind != DataKind.String)
                            return Refuse(ResultCodes.ContentInvalid, basis, "NPC_ROW_INVALID", "npcPositions row without a string npcId");
                        if (index.ContainsKey(id.Text)) return Refuse(ResultCodes.ContentInvalid, basis, "NPC_ROW_DUPLICATE", "duplicate npcPositions row " + id.Text);
                        index.Add(id.Text, rows.Count);
                        rows.Add(row);
                    }
                }
                foreach (var spot in npcs)
                {
                    int at;
                    if (index.TryGetValue(spot.Id, out at))
                    {
                        var row = rows[at];
                        DataNode ex;
                        DataNode ey;
                        row.TryGet(WorldProjectionProtocol.XKey, out ex);
                        row.TryGet(WorldProjectionProtocol.YKey, out ey);
                        rows[at] = row.With(WorldProjectionProtocol.MapIdKey, DataNode.FromString(spot.MapId))
                            .With(WorldProjectionProtocol.XKey, NumberLike(spot.X, ex))
                            .With(WorldProjectionProtocol.YKey, NumberLike(spot.Y, ey));
                    }
                    else
                    {
                        index.Add(spot.Id, rows.Count);
                        rows.Add(DataNode.FromObject(new[]
                        {
                            new KeyValuePair<string, DataNode>(WorldProjectionProtocol.NpcIdKey, DataNode.FromString(spot.Id)),
                            new KeyValuePair<string, DataNode>(WorldProjectionProtocol.MapIdKey, DataNode.FromString(spot.MapId)),
                            new KeyValuePair<string, DataNode>(WorldProjectionProtocol.XKey, DataNode.FromNumber(spot.X)),
                            new KeyValuePair<string, DataNode>(WorldProjectionProtocol.YKey, DataNode.FromNumber(spot.Y))
                        }));
                    }
                }
                slices[WorldProjectionProtocol.NpcPositionsPointer] = DataNode.FromList(rows);
            }

            return Outcome<StateReplacement>.Ok(new StateReplacement(slices), basis.Revision);
        }

        private static Spot ReadSpot(DataNode node, bool withId)
        {
            if (node == null || node.Kind != DataKind.Object) return null;
            if (node.Members.Count != (withId ? 4 : 3)) return null;
            DataNode map;
            DataNode x;
            DataNode y;
            if (!node.TryGet(WorldProjectionProtocol.MapIdKey, out map) || map.Kind != DataKind.String) return null;
            if (!node.TryGet(WorldProjectionProtocol.XKey, out x) || !node.TryGet(WorldProjectionProtocol.YKey, out y)) return null;
            var spot = new Spot { MapId = map.Text };
            if (!TryValue(x, out spot.X) || !TryValue(y, out spot.Y)) return null;
            if (withId)
            {
                DataNode id;
                if (!node.TryGet(WorldProjectionProtocol.NpcIdKey, out id) || id.Kind != DataKind.String || id.Text.Length == 0) return null;
                spot.Id = id.Text;
            }
            return spot;
        }

        private static bool TryValue(DataNode n, out double value)
        {
            value = 0;
            if (n.Kind == DataKind.Integer) { value = n.Integer; return true; }
            if (n.Kind == DataKind.Number) { value = n.Number; return true; }
            return false;
        }

        // Keeps the original node (and its Integer/Number kind) when the value did not change.
        private static DataNode NumberLike(double value, DataNode existing)
        {
            if (existing != null)
            {
                if (existing.Kind == DataKind.Integer && existing.Integer == value) return existing;
                if (existing.Kind == DataKind.Number && existing.Number == value) return existing;
            }
            return DataNode.FromNumber(value);
        }

        private static Outcome<StateReplacement> Refuse(string code, StateSnapshot basis, string diagnosticCode, string message)
        {
            return Outcome<StateReplacement>.Fail(code, basis.Revision, new Diagnostic(diagnosticCode, message, null, string.Empty, string.Empty));
        }
    }
}
