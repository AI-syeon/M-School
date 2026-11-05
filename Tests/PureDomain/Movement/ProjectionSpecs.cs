using System;
using System.Linq;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Tests.PureDomain.Support;
using MagicCampus.World.Movement;

namespace MagicCampus.Tests.Movement
{
    internal static class ProjectionSpecs
    {
        public static void Run()
        {
            ProjectsActiveActorsAndPreservesTheRest();
            KeepsNumberKindsWhenUnchanged();
            RefusesStaleUnknownAndMalformedPlans();
            HandlesMissingAndBrokenSaveSlices();
            CaptureRefusesUnknownPlacesAndProjectKeepsAnEmptyWorld();
        }

        private static DataNode Npc(string id, string map, DataNode x, DataNode y, params KeyValuePair<string, DataNode>[] extra)
        {
            var m = new List<KeyValuePair<string, DataNode>>
            {
                Dn.P("npcId", Dn.S(id)), Dn.P("mapId", Dn.S(map)), Dn.P("x", x), Dn.P("y", y)
            };
            m.AddRange(extra);
            return DataNode.FromObject(m);
        }

        private static StateSnapshot Basis(DataNode player, DataNode npcPositions)
        {
            var m = new List<KeyValuePair<string, DataNode>> { Dn.P("saveVersion", Dn.I(2)), Dn.P("clock", Dn.O(Dn.P("minute", Dn.I(420)))) };
            if (player != null) m.Add(Dn.P("player", player));
            if (npcPositions != null) m.Add(Dn.P("npcPositions", npcPositions));
            return new StateSnapshot(7, DataNode.FromObject(m));
        }

        private static DataNode Player(DataNode x, DataNode y)
        {
            return Dn.O(Dn.P("mapId", Dn.S("hall")), Dn.P("x", x), Dn.P("y", y), Dn.P("gold", Dn.I(40)), Dn.P("inventory", Dn.L(Dn.S("quill"))));
        }

        private static DataNode Apply(DataNode root, StateReplacement replacement)
        {
            foreach (var kv in replacement.Slices)
            {
                var parts = kv.Key.Substring(1).Split('/');
                root = parts.Length == 1
                    ? root.With(parts[0], kv.Value)
                    : root.With(parts[0], root.Members[parts[0]].With(parts[1], kv.Value));
            }
            return root;
        }

        private static ActorWorld World()
        {
            return new ActorWorld(Rig.StandardWorld(), new[]
            {
                Rig.Actor("player", "hall", 2.5, 2.5), Rig.Actor("ben", "yard", 3.5, 3.5), Rig.Actor("cy", "hall", 6.5, 4.5)
            });
        }

        private static StatePlan PlanOf(IEnumerable<PlannedChange> changes, StateSnapshot basis)
        {
            return new StatePlan(new TransactionId("tx.proj.1"), null, basis.Revision, changes);
        }

        private static void ProjectsActiveActorsAndPreservesTheRest()
        {
            var world = World();
            var geometry = Rig.StandardWorld();
            var projector = new WorldStateProjector(world);
            var participant = new WorldCommitParticipant(world, geometry);
            Expect.True(participant.ParticipantId == "world", "participant id");
            Expect.True(participant.OwnedSlices.Contains("/npcPositions") && participant.OwnedSlices.Contains("/player/x"), "owned pointers");

            var dee = Npc("dee", "yard", Dn.I(3), Dn.I(3));
            var basis = Basis(Player(Dn.N(1.5), Dn.I(2)), Dn.L(
                Npc("ben", "hall", Dn.I(1), Dn.I(1), Dn.P("mood", Dn.S("calm"))), Npc("cy", "hall", Dn.I(6), Dn.I(4)), dee));

            var planned = projector.PlanProjection(basis);
            Expect.True(planned.Succeeded && planned.Value.Changes.Count == 1, "one projection change");
            Expect.True(planned.Value.Changes[0].ParticipantId == "world" && planned.Value.Changes[0].Operation == "project", "frozen participant and operation");

            var prepared = participant.Prepare(basis, PlanOf(planned.Value.Changes, basis));
            Expect.True(prepared.Succeeded && prepared.Value.Slices.Count == 4, "four owned pointers replaced");
            var root = Apply(basis.Root, prepared.Value);

            var player = root.Members["player"];
            Expect.True(player.Members["gold"].Integer == 40 && player.Members["inventory"].Equals(basis.Slice("player").Members["inventory"]), "other player fields preserved");
            Expect.True(player.Members["mapId"].Text == "hall" && player.Members["x"].Number == 2.5 && player.Members["y"].Number == 2.5, "player position projected");
            var rows = root.Members["npcPositions"].Items;
            Expect.True(rows.Count == 3 && rows[0].Members["npcId"].Text == "ben" && rows[1].Members["npcId"].Text == "cy", "row order preserved");
            Expect.True(rows[0].Members["mapId"].Text == "yard" && rows[0].Members["x"].Number == 3.5 && rows[0].Members["mood"].Text == "calm", "active npc updated, extra field kept");
            Expect.True(ReferenceEquals(rows[2], dee), "unspawned npc row preserved untouched");
            Expect.True(root.Members["clock"].Equals(basis.Slice("clock")), "unrelated slices untouched");
            Expect.True(basis.Slice("player").Members["x"].Number == 1.5, "basis itself is never modified");
        }

        private static void KeepsNumberKindsWhenUnchanged()
        {
            var world = new ActorWorld(Rig.StandardWorld(), new[] { Rig.Actor("player", "hall", 3, 2) });
            var basis = Basis(Player(Dn.I(3), Dn.I(2)), null);
            var planned = new WorldStateProjector(world).PlanProjection(basis);
            var prepared = new WorldCommitParticipant(world, Rig.StandardWorld()).Prepare(basis, PlanOf(planned.Value.Changes, basis));
            Expect.True(prepared.Succeeded && prepared.Value.Slices.Count == 3, "player only projection");
            Expect.True(prepared.Value.Slices["/player/x"].Kind == DataKind.Integer && prepared.Value.Slices["/player/y"].Kind == DataKind.Integer, "unchanged integers stay integers");

            var empty = new ActorWorld(Rig.StandardWorld(), new ActorState[0]);
            var none = new WorldStateProjector(empty).PlanProjection(basis);
            Expect.True(none.Succeeded && none.Value.Changes.Count == 0, "no actors, no change");
        }

        private static void RefusesStaleUnknownAndMalformedPlans()
        {
            var world = World();
            var participant = new WorldCommitParticipant(world, Rig.StandardWorld());
            var basis = Basis(Player(Dn.N(1.5), Dn.I(2)), Dn.L(Npc("ben", "hall", Dn.I(1), Dn.I(1)), Npc("cy", "hall", Dn.I(6), Dn.I(4))));
            var planned = new WorldStateProjector(world).PlanProjection(basis);

            world.SetPosition("player", Rig.Pos("hall", 3.5, 2.5), "mover.1");
            var stale = participant.Prepare(basis, PlanOf(planned.Value.Changes, basis));
            Expect.True(!stale.Succeeded && stale.Result.Code == ResultCodes.RevisionConflict && stale.Value == null, "stale projection refused");

            var unknown = participant.Prepare(basis, PlanOf(new[] { new PlannedChange("world", "explode", DataNode.Null) }, basis));
            Expect.True(!unknown.Succeeded && unknown.Result.Code == ResultCodes.Unregistered, "unknown operation never pretends success");

            var badShape = Dn.O(Dn.P("player", Dn.O(Dn.P("mapId", Dn.S("hall")), Dn.P("x", Dn.S("1")), Dn.P("y", Dn.I(1)))));
            Expect.True(participant.Prepare(basis, PlanOf(new[] { new PlannedChange("world", "project", badShape) }, basis)).Result.Code == ResultCodes.ContentInvalid, "malformed player payload");

            var ghost = Dn.O(Dn.P("npcPositions", Dn.L(Npc("ghost", "hall", Dn.N(1.5), Dn.N(1.5)))));
            Expect.True(participant.Prepare(basis, PlanOf(new[] { new PlannedChange("world", "project", ghost) }, basis)).Result.Code == ResultCodes.OutOfScope, "inactive actor refused");

            var extra = Dn.O(Dn.P("surprise", Dn.I(1)));
            Expect.True(participant.Prepare(basis, PlanOf(new[] { new PlannedChange("world", "project", extra) }, basis)).Result.Code == ResultCodes.ContentInvalid, "unknown payload field");

            var other = participant.Prepare(basis, PlanOf(new[] { new PlannedChange("clock", "advance", DataNode.Null) }, basis));
            Expect.True(other.Succeeded && other.Value.Slices.Count == 0, "foreign changes are not ours");
            Expect.Throws<ArgumentNullException>(() => participant.Prepare(null, PlanOf(new PlannedChange[0], basis)), "basis required");
        }

        private static void HandlesMissingAndBrokenSaveSlices()
        {
            var world = World();
            var participant = new WorldCommitParticipant(world, Rig.StandardWorld());
            var projector = new WorldStateProjector(world);

            var noNpcs = Basis(Player(Dn.N(1.5), Dn.I(2)), null);
            var prepared = participant.Prepare(noNpcs, PlanOf(projector.PlanProjection(noNpcs).Value.Changes, noNpcs));
            Expect.True(prepared.Succeeded, "missing npcPositions is created");
            var rows = prepared.Value.Slices["/npcPositions"].Items;
            Expect.True(rows.Count == 2 && rows[0].Members["npcId"].Text == "ben" && rows[1].Members["npcId"].Text == "cy", "new rows appended in actor order");

            var noPlayer = Basis(null, null);
            Expect.True(projector.PlanProjection(noPlayer).Result.Code == ResultCodes.ContentInvalid, "projection needs a player object");

            var duplicate = Basis(Player(Dn.N(1.5), Dn.I(2)), Dn.L(Npc("ben", "hall", Dn.I(1), Dn.I(1)), Npc("ben", "hall", Dn.I(2), Dn.I(2))));
            var refused = participant.Prepare(duplicate, PlanOf(projector.PlanProjection(duplicate).Value.Changes, duplicate));
            Expect.True(!refused.Succeeded && refused.Result.Code == ResultCodes.ContentInvalid, "duplicate rows are refused, not merged");

            var brokenRow = Basis(Player(Dn.N(1.5), Dn.I(2)), Dn.L(Dn.S("junk")));
            Expect.True(participant.Prepare(brokenRow, PlanOf(projector.PlanProjection(brokenRow).Value.Changes, brokenRow)).Result.Code == ResultCodes.ContentInvalid, "non-object rows are refused");
        }

        private static void CaptureRefusesUnknownPlacesAndProjectKeepsAnEmptyWorld()
        {
            var world = World();
            var captured = new WorldProjection(world, Rig.StandardWorld()).Capture();
            Expect.True(captured.Succeeded && captured.Value.Player.Value.Equals(Rig.Pos("hall", 2.5, 2.5)), "player captured");
            Expect.True(string.Join(",", captured.Value.Npcs.Select(n => n.Key + "@" + n.Value.MapId)) == "ben@yard,cy@hall", "npcs captured in actor order");

            var hallOnly = new TestWorld();
            hallOnly.Add(new TestGrid("hall", "##########", "#........#", "#........#", "#........#", "#........#", "##########"));
            var unknownMap = new WorldProjection(world, hallOnly).Capture();
            Expect.True(!unknownMap.Succeeded && unknownMap.Result.Code == ResultCodes.ContentInvalid && unknownMap.Result.Details[0].Code == "POSITION_INVALID", "npc on an unknown map is refused");

            var narrow = new TestWorld();
            narrow.Add(new TestGrid("hall", "....", "...."));
            narrow.Add(new TestGrid("yard", "....", "....", "....", "...."));
            var outside = new WorldProjection(world, narrow).Capture();
            Expect.True(!outside.Succeeded && outside.Result.Details[0].Message.Contains("player"), "out of bounds player is refused before any npc");

            var root = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            TypedFields.FillMissing(root);
            var state = SaveDocument.Read(DataNode.FromObject(root)).Value;
            var none = new WorldProjection(new ActorWorld(Rig.StandardWorld(), new ActorState[0]), Rig.StandardWorld()).Project(state);
            Expect.True(none.Succeeded && ReferenceEquals(none.Value, state), "no active actor leaves the state untouched");
            var moved = new WorldProjection(world, Rig.StandardWorld()).Project(state);
            Expect.True(moved.Succeeded && moved.Value.World.Player.MapId == "hall" && moved.Value.World.Npcs.Count == 2, "player and npcs projected into the state");
            var refused = new WorldProjection(world, hallOnly).Project(state);
            Expect.True(!refused.Succeeded && refused.Result.Code == ResultCodes.ContentInvalid && refused.Value == null, "a refused capture refuses the transition");
        }
    }
}
