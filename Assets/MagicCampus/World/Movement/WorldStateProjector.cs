using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.World.Movement
{
    /// <summary>Plans the projection of active actor positions into the save tree; it reads the actor world and never writes anything.</summary>
    public sealed class WorldStateProjector : IWorldStateProjector
    {
        private readonly IActorWorld world;
        private readonly string playerActorId;

        public WorldStateProjector(IActorWorld world, string playerActorId = "player")
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrEmpty(playerActorId)) throw new ArgumentException("player actor id is required", nameof(playerActorId));
            this.playerActorId = playerActorId;
        }

        public Outcome<ChangeSet> PlanProjection(StateSnapshot basis)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            var members = new List<KeyValuePair<string, DataNode>>();
            var npcs = new List<DataNode>();
            var havePlayer = false;
            foreach (var actor in world.All)
            {
                if (string.Equals(actor.ActorId, playerActorId, StringComparison.Ordinal))
                {
                    members.Add(new KeyValuePair<string, DataNode>(WorldProjectionProtocol.PlayerKey, Spot(null, actor.Position)));
                    havePlayer = true;
                }
                else
                {
                    npcs.Add(Spot(actor.ActorId, actor.Position));
                }
            }

            if (!havePlayer && npcs.Count == 0) return Outcome<ChangeSet>.Ok(new ChangeSet(new PlannedChange[0]), basis.Revision);

            if (havePlayer)
            {
                var player = basis.Slice(WorldProjectionProtocol.PlayerKey);
                if (player == null || player.Kind != DataKind.Object)
                    return Outcome<ChangeSet>.Fail(ResultCodes.ContentInvalid, basis.Revision,
                        new Diagnostic("PLAYER_SLICE_INVALID", "the save has no player object to project into", null, "object", player == null ? "missing" : player.Kind.ToString()));
            }
            if (npcs.Count > 0) members.Add(new KeyValuePair<string, DataNode>(WorldProjectionProtocol.NpcPositionsKey, DataNode.FromList(npcs)));

            var change = new PlannedChange(WorldProjectionProtocol.ParticipantId, WorldProjectionProtocol.Operation, DataNode.FromObject(members));
            return Outcome<ChangeSet>.Ok(new ChangeSet(new[] { change }), basis.Revision);
        }

        private static DataNode Spot(string npcId, Position p)
        {
            var m = new List<KeyValuePair<string, DataNode>>
            {
                new KeyValuePair<string, DataNode>(WorldProjectionProtocol.MapIdKey, DataNode.FromString(p.MapId)),
                new KeyValuePair<string, DataNode>(WorldProjectionProtocol.XKey, DataNode.FromNumber(p.X)),
                new KeyValuePair<string, DataNode>(WorldProjectionProtocol.YKey, DataNode.FromNumber(p.Y))
            };
            if (npcId != null) m.Add(new KeyValuePair<string, DataNode>(WorldProjectionProtocol.NpcIdKey, DataNode.FromString(npcId)));
            return DataNode.FromObject(m);
        }
    }
}
