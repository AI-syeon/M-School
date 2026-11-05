using System;
using System.Linq;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.State;
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
            CaptureRefusesUnknownPlacesAndProjectKeepsAnEmptyWorld();
        }

        private static DataNode Npc(string id, string map, DataNode x, DataNode y)
        {
            return Dn.O(Dn.P("npcId", Dn.S(id)), Dn.P("mapId", Dn.S(map)), Dn.P("x", x), Dn.P("y", y));
        }

        private static GameState State(DataNode player, DataNode npcPositions)
        {
            var m = new Dictionary<string, DataNode>(StringComparer.Ordinal) { { "saveVersion", Dn.I(2) }, { "player", player } };
            if (npcPositions != null) m["npcPositions"] = npcPositions;
            TypedFields.FillMissing(m);
            var read = SaveDocument.Read(DataNode.FromObject(m));
            Expect.True(read.Succeeded, "test tree is a readable state");
            return read.Value;
        }

        private static DataNode Player(DataNode x, DataNode y)
        {
            return Dn.O(Dn.P("mapId", Dn.S("hall")), Dn.P("x", x), Dn.P("y", y), Dn.P("gold", Dn.I(40)), Dn.P("inventory", Dn.L(Dn.S("quill"))));
        }

        private static ActorWorld World()
        {
            return new ActorWorld(Rig.StandardWorld(), new[]
            {
                Rig.Actor("player", "hall", 2.5, 2.5), Rig.Actor("ben", "yard", 3.5, 3.5), Rig.Actor("cy", "hall", 6.5, 4.5)
            });
        }

        private static void ProjectsActiveActorsAndPreservesTheRest()
        {
            var dee = Npc("dee", "yard", Dn.I(3), Dn.I(3));
            var before = State(Player(Dn.N(1.5), Dn.I(2)), Dn.L(Npc("ben", "hall", Dn.I(1), Dn.I(1)), Npc("cy", "hall", Dn.I(6), Dn.I(4)), dee));
            var projected = new WorldProjection(World(), Rig.StandardWorld()).Project(before);
            Expect.True(projected.Succeeded, "projection succeeds");
            var root = SaveDocument.Write(projected.Value);
            var basis = SaveDocument.Write(before);

            var player = root.Members["player"];
            Expect.True(player.Members["gold"].Integer == 40 && player.Members["inventory"].Equals(basis.Members["player"].Members["inventory"]), "other player fields preserved");
            Expect.True(player.Members["mapId"].Text == "hall" && player.Members["x"].Number == 2.5 && player.Members["y"].Number == 2.5, "player position projected");
            var rows = root.Members["npcPositions"].Items;
            Expect.True(rows.Count == 3 && rows[0].Members["npcId"].Text == "ben" && rows[1].Members["npcId"].Text == "cy", "row order preserved");
            Expect.True(rows[0].Members["mapId"].Text == "yard" && rows[0].Members["x"].Number == 3.5, "active npc updated");
            Expect.True(rows[2].Equals(dee), "unspawned npc row preserved untouched");
            Expect.True(root.Members["clock"].Equals(basis.Members["clock"]), "unrelated fields untouched");
            Expect.True(before.World.Player.X.Value == 1.5, "the input state is never modified");

            var appended = new WorldProjection(World(), Rig.StandardWorld()).Project(State(Player(Dn.N(1.5), Dn.I(2)), null)).Value;
            Expect.True(string.Join(",", appended.World.Npcs.Select(n => n.NpcId)) == "ben,cy", "new rows appended in actor order");
        }

        private static void KeepsNumberKindsWhenUnchanged()
        {
            var world = new ActorWorld(Rig.StandardWorld(), new[] { Rig.Actor("player", "hall", 3, 2) });
            var before = State(Player(Dn.I(3), Dn.I(2)), null);
            var projected = new WorldProjection(world, Rig.StandardWorld()).Project(before);
            var player = SaveDocument.Write(projected.Value).Members["player"];
            Expect.True(projected.Succeeded && player.Members["x"].Kind == DataKind.Integer && player.Members["y"].Kind == DataKind.Integer, "unchanged integers stay integers");

            world.SetPosition("player", Rig.Pos("hall", 3.5, 2), "mover.1");
            var moved = SaveDocument.Write(new WorldProjection(world, Rig.StandardWorld()).Project(before).Value).Members["player"];
            Expect.True(moved.Members["x"].Kind == DataKind.Number && moved.Members["x"].Number == 3.5 && moved.Members["y"].Kind == DataKind.Integer, "only the moved coordinate becomes a number");
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
