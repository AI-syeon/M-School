using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.World.Movement
{
    /// <summary>
    /// Reads the live actor positions for a commit. The actor world stays the live authority. Callers capture inside the commit's
    /// transition, so the positions cannot move between capture and commit.
    /// </summary>
    public sealed class WorldProjection
    {
        private readonly IActorWorld world;
        private readonly IMapGeometry geometry;
        private readonly string playerActorId;

        public WorldProjection(IActorWorld world, IMapGeometry geometry, string playerActorId = "player")
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (string.IsNullOrEmpty(playerActorId)) throw new ArgumentException("player actor id is required", nameof(playerActorId));
            this.playerActorId = playerActorId;
        }

        /// <summary>Every active actor's position. POSITION_INVALID when one lies outside a known map; the player is checked first.</summary>
        public Outcome<WorldPlaces> Capture()
        {
            Position? player = null;
            var npcs = new List<KeyValuePair<string, Position>>();
            foreach (var actor in world.All)
            {
                if (string.Equals(actor.ActorId, playerActorId, StringComparison.Ordinal)) player = actor.Position;
                else npcs.Add(new KeyValuePair<string, Position>(actor.ActorId, actor.Position));
            }

            if (player.HasValue && !Inside(player.Value)) return Outside(playerActorId);
            foreach (var npc in npcs)
                if (!Inside(npc.Value)) return Outside(npc.Key);
            return Outcome<WorldPlaces>.Ok(new WorldPlaces(player, npcs), 0);
        }

        /// <summary>The safe-position transition: captures now and merges into the state. With no active actor the state comes back unchanged.</summary>
        public Outcome<GameState> Project(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var live = Capture();
            if (!live.Succeeded) return Outcome<GameState>.Fail(live.Result.Code, 0, new List<Diagnostic>(live.Result.Details).ToArray());
            if (!live.Value.Player.HasValue && live.Value.Npcs.Count == 0) return Outcome<GameState>.Ok(state, 0);
            return Outcome<GameState>.Ok(state.With(state.World.Project(live.Value)), 0);
        }

        private bool Inside(Position p)
        {
            var map = geometry.Find(p.MapId);
            return map != null && map.Contains(p.X, p.Y);
        }

        private static Outcome<WorldPlaces> Outside(string actorId)
        {
            return Outcome<WorldPlaces>.Fail(ResultCodes.ContentInvalid, 0,
                new Diagnostic("POSITION_INVALID", "planned position for " + actorId + " is outside a known map", null, string.Empty, string.Empty));
        }
    }
}
