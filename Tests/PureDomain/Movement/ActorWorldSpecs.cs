using System;
using MagicCampus.Contracts;
using MagicCampus.World.Movement;

namespace MagicCampus.Tests.Movement
{
    internal static class ActorWorldSpecs
    {
        public static void Run()
        {
            ConstructionValidates();
            WritesNeedTokenAndKeepLastValidPosition();
            SnapshotsAreImmutableAndRestoreIsAtomic();
        }

        private static void ConstructionValidates()
        {
            var geometry = Rig.StandardWorld();
            Expect.Throws<ArgumentException>(() => new ActorWorld(geometry, new[] { Rig.Actor("a", "hall", 1.5, 1.5), Rig.Actor("a", "hall", 2.5, 1.5) }), "duplicate ids");
            Expect.Throws<ArgumentException>(() => new ActorWorld(geometry, new[] { Rig.Actor("a", "nowhere", 1.5, 1.5) }), "unknown map");
            Expect.Throws<ArgumentException>(() => new ActorWorld(geometry, new[] { Rig.Actor("a", "hall", 50, 1) }), "outside the map");
            Expect.Throws<ArgumentNullException>(() => new ActorWorld(null, new ActorState[0]), "geometry required");
            var world = new ActorWorld(geometry, new[] { Rig.Actor("b", "hall", 2.5, 1.5), Rig.Actor("a", "yard", 1.5, 1.5) });
            Expect.True(world.All.Count == 2 && world.All[0].ActorId == "a" && world.All[1].ActorId == "b", "ordinal ordered snapshot");
            Expect.True(world.Find("b") != null && world.Find("zzz") == null && world.Find(null) == null, "find");
        }

        private static void WritesNeedTokenAndKeepLastValidPosition()
        {
            var geometry = Rig.StandardWorld();
            var world = new ActorWorld(geometry, new[] { Rig.Actor("a", "hall", 1.5, 1.5) }, token => token.StartsWith("mover.", StringComparison.Ordinal));
            var home = Rig.Pos("hall", 1.5, 1.5);
            Expect.True(world.SetPosition("a", Rig.Pos("hall", 2.5, 1.5), "").Status == OperationStatus.Failed, "empty token");
            Expect.True(world.SetPosition("a", Rig.Pos("hall", 2.5, 1.5), "bad token").Status == OperationStatus.Failed, "token with whitespace");
            Expect.True(world.SetPosition("a", Rig.Pos("hall", 2.5, 1.5), "other.1").Code == ResultCodes.OutOfScope, "writer policy");
            Expect.True(world.SetPosition("ghost", Rig.Pos("hall", 2.5, 1.5), "mover.1").Code == ResultCodes.RefNotFound, "unknown actor");
            Expect.True(world.SetPosition("a", Rig.Pos("hall", 99, 1.5), "mover.1").Status == OperationStatus.Failed, "outside the map");
            Expect.True(world.SetPosition("a", Rig.Pos("nowhere", 1.5, 1.5), "mover.1").Status == OperationStatus.Failed, "unknown map");
            Expect.True(world.SetPosition("a", default(Position), "mover.1").Status == OperationStatus.Failed, "unplaced position");
            Expect.True(world.Find("a").Position.Equals(home) && world.Version == 0, "rejected writes keep the last valid position");
            var ok = world.SetPosition("a", Rig.Pos("yard", 3.5, 3.5), "mover.1");
            Expect.True(ok.Status == OperationStatus.Succeeded && world.Find("a").Position.MapId == "yard" && world.Version == 1, "accepted write");
        }

        private static void SnapshotsAreImmutableAndRestoreIsAtomic()
        {
            var world = new ActorWorld(Rig.StandardWorld(), new[] { Rig.Actor("a", "hall", 1.5, 1.5) });
            var before = world.All;
            var old = world.Find("a");
            world.SetPosition("a", Rig.Pos("hall", 2.5, 1.5), "mover.1");
            Expect.Near(before[0].Position.X, 1.5, 1e-12, "old snapshot unchanged");
            Expect.Near(old.Position.X, 1.5, 1e-12, "old state object unchanged");
            Expect.Near(world.All[0].Position.X, 2.5, 1e-12, "new snapshot updated");

            var restored = world.Restore(new[] { Rig.Actor("b", "yard", 2.5, 2.5) });
            Expect.True(restored.Status == OperationStatus.Succeeded && world.Find("a") == null && world.Find("b") != null, "restore replaces everything");
            var dup = world.Restore(new[] { Rig.Actor("c", "hall", 1.5, 1.5), Rig.Actor("c", "hall", 2.5, 1.5) });
            Expect.True(dup.Status == OperationStatus.Failed && dup.Code == ResultCodes.ContentInvalid, "duplicate restore refused");
            var oob = world.Restore(new[] { Rig.Actor("c", "hall", 99, 1) });
            Expect.True(oob.Status == OperationStatus.Failed, "out of map restore refused");
            Expect.True(world.Restore(null).Status == OperationStatus.Failed, "null restore refused");
            Expect.True(world.All.Count == 1 && world.Find("b") != null, "failed restores keep the previous state");
        }
    }
}
