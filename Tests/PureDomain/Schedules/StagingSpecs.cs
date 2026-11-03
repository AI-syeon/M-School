using System;
using System.Collections.Generic;
using MagicCampus.Application.Schedules;
using MagicCampus.Contracts;

namespace MagicCampus.Tests.PureDomain.Schedules
{
    public static class StagingSpecs
    {
        public static void Run()
        {
            CurrentStagingNeverWarps();
            ReservedStagingOrdersHideLoadRestoreShow();
            ReservedStagingRefusesBeforeHiding();
            ReservedStagingUnhidesOnLoadFailure();
            ReservedStagingUnhidesOnRestoreFailure();
            ReservedStagingCancelUnhides();
            ReservedStagingWatchdogTimesOut();
            ReservedStagingNeedsHideCallback();
        }

        private sealed class StageRig
        {
            internal readonly Rt Rt = new Rt();
            internal readonly List<string> Log = new List<string>();
            internal readonly FakeWorld World;
            internal readonly FakeMap Map;
            internal readonly FakeCollision Collision = new FakeCollision();
            internal readonly List<IReadOnlyList<string>> StopCalls = new List<IReadOnlyList<string>>();
            internal ReservedJoinPlan Plan;
            internal int PlannerCalls;
            internal readonly ReservedStagingService Service;

            internal StageRig(bool withHide = true)
            {
                World = new FakeWorld(Log, Kit.Actor("npc.a", "map.old", 1.5, 1.5), Kit.Actor("npc.z", "map.old", 2.5, 2.5), Kit.Actor("player", "map.old", 3.5, 3.5));
                Map = new FakeMap(Log, Rt.Handles);
                Plan = new ReservedJoinPlan("map.new", new[]
                {
                    new ReservedActorPlacement("npc.a", new Position("map.new", 2.5, 2.5)),
                    new ReservedActorPlacement("player", new Position("map.new", 1.5, 1.5))
                });
                Service = new ReservedStagingService(World, Map, Collision, Rt.Handles,
                    (definition, lease) =>
                    {
                        PlannerCalls++;
                        Log.Add("plan");
                        return Outcome<ReservedJoinPlan>.Ok(Plan, 0);
                    },
                    withHide ? (Action<bool>)(hidden => Log.Add(hidden ? "hide" : "show")) : null,
                    ids =>
                    {
                        StopCalls.Add(ids);
                        var c = Rt.Handles.Create<bool>("stop");
                        c.Start();
                        c.Succeed(true, 0);
                        return c;
                    });
            }

            internal string Joined { get { return string.Join(",", Log); } }
        }

        private static CompiledEvent Reserved()
        {
            return Kit.Event("event.r", staging: "reserved", resources: new[] { "camera.main", "npc.a", "input.player" });
        }

        private static Lease FullLease()
        {
            return new Lease("lease.1", "owner", new[] { "snapshot.world", "camera.main", "npc.a", "input.player" });
        }

        private static void CurrentStagingNeverWarps()
        {
            var rig = new StageRig();
            var lease = new Lease("lease.c", "owner", new[] { "npc.a", "input.player", "camera.main" });
            var handle = rig.Service.Stage(Kit.Event("event.c", staging: "current"), lease);
            Spec.True(!handle.Status.IsTerminal(), "waits for the ambient stop");
            rig.Rt.Step();
            Spec.Equal(OperationStatus.Succeeded, handle.Status, "current staging succeeds after ambient stop");
            Spec.Equal("npc.a", string.Join(",", rig.StopCalls[0]), "only leased actors are stopped, not player resources");
            Spec.Equal("", rig.Joined, "current staging hides, loads and moves nothing");
            Spec.Equal(0, rig.World.RestoreCalls + rig.World.SetPositionCalls, "no warp");
            Spec.Equal(0, rig.Map.Loads.Count, "no map load");
        }

        private static void ReservedStagingOrdersHideLoadRestoreShow()
        {
            var rig = new StageRig();
            var handle = rig.Service.Stage(Reserved(), FullLease());
            Spec.Equal("plan", rig.Joined, "nothing is hidden before the stop completes");
            rig.Rt.Step();
            Spec.Equal("plan,hide,load:map.new", rig.Joined, "hide happens after lease validation, then the map loads");
            Spec.Equal("map.old", rig.World.Find("npc.a").Position.MapId, "no actor write before the load finishes");

            rig.Map.Active = "map.new";
            rig.Map.Loads[0].Succeed(true, 0);
            rig.Rt.Step();
            Spec.Equal("plan,hide,load:map.new,restore,show", rig.Joined, "one atomic restore after load, then show");
            Spec.Equal(OperationStatus.Succeeded, handle.Status, "stage succeeded");
            Spec.Equal("map.new", rig.World.Find("npc.a").Position.MapId, "npc joined");
            Spec.Equal("map.new", rig.World.Find("player").Position.MapId, "player joined");
            Spec.Equal("map.old", rig.World.Find("npc.z").Position.MapId, "other actors untouched");
            Spec.Equal(1, rig.World.RestoreCalls, "exactly one restore");
        }

        private static void ReservedStagingRefusesBeforeHiding()
        {
            var noWorld = new StageRig();
            var missing = noWorld.Service.Stage(Reserved(), new Lease("lease.2", "owner", new[] { "camera.main", "npc.a", "input.player" }));
            Spec.Equal(ResultCodes.ResourceBusy, missing.Terminal.Code, "snapshot.world must be leased");
            Spec.Equal(0, noWorld.PlannerCalls, "planner is not consulted without a complete lease");
            Spec.Equal("", noWorld.Joined, "nothing hidden");

            var blocked = new StageRig();
            blocked.Collision.Passable = p => false;
            var refused = blocked.Service.Stage(Reserved(), FullLease());
            Spec.Equal(ResultCodes.TargetBlocked, refused.Terminal.Code, "impassable position refuses");
            Spec.Equal("plan", blocked.Joined, "no hide, no load");

            var unleased = new StageRig();
            unleased.Plan = new ReservedJoinPlan("map.new", new[] { new ReservedActorPlacement("npc.z", new Position("map.new", 1.5, 1.5)) });
            var notHeld = unleased.Service.Stage(Reserved(), FullLease());
            Spec.Equal(ResultCodes.ResourceBusy, notHeld.Terminal.Code, "every joined actor must be leased");
            Spec.Equal("plan", unleased.Joined, "no hide, no load");
        }

        private static void ReservedStagingUnhidesOnLoadFailure()
        {
            var rig = new StageRig();
            var handle = rig.Service.Stage(Reserved(), FullLease());
            rig.Rt.Step();
            rig.Map.Loads[0].Fail(ResultCodes.MapLoadFailed, 0, new Diagnostic[0]);
            rig.Rt.Step();
            Spec.Equal(ResultCodes.MapLoadFailed, handle.Terminal.Code, "load failure is reported");
            Spec.Equal("plan,hide,load:map.new,show", rig.Joined, "unhidden and no restore");
            Spec.Equal(0, rig.World.RestoreCalls, "no partial actor writes");
            Spec.Equal(0, rig.Service.ActiveCount, "stage released");
        }

        private static void ReservedStagingUnhidesOnRestoreFailure()
        {
            var rig = new StageRig();
            rig.World.FailRestore = true;
            var handle = rig.Service.Stage(Reserved(), FullLease());
            rig.Rt.Step();
            rig.Map.Active = "map.new";
            rig.Map.Loads[0].Succeed(true, 0);
            rig.Rt.Step();
            Spec.Equal(ResultCodes.InvariantViolation, handle.Terminal.Code, "restore failure is reported");
            Spec.Equal("plan,hide,load:map.new,restore,show", rig.Joined, "unhidden after the failed restore");
            Spec.Equal("map.old", rig.World.Find("npc.a").Position.MapId, "actors keep their old positions");
        }

        private static void ReservedStagingCancelUnhides()
        {
            var rig = new StageRig();
            var handle = rig.Service.Stage(Reserved(), FullLease());
            rig.Rt.Step();
            handle.Cancel("user");
            Spec.Equal(1, rig.Map.CancelReasons.Count, "cancel reaches the running map load");
            Spec.True(!handle.Status.IsTerminal(), "stage waits for the child before ending");

            rig.Map.Loads[0].Cancelled("user", 0);
            rig.Rt.Step();
            Spec.Equal(OperationStatus.Cancelled, handle.Status, "stage ends cancelled");
            Spec.Equal("plan,hide,load:map.new,show", rig.Joined, "unhidden, no restore");
            Spec.Equal(0, rig.World.RestoreCalls, "no actor writes after cancel");
        }

        private static void ReservedStagingWatchdogTimesOut()
        {
            var rig = new StageRig();
            var handle = rig.Service.Stage(Reserved(), FullLease());
            rig.Rt.Step();
            rig.Service.Tick(0, 61);
            Spec.Equal(1, rig.Map.CancelReasons.Count, "watchdog cancels the child");
            rig.Map.Loads[0].Cancelled("watchdog", 0);
            rig.Rt.Step();
            Spec.Equal(OperationStatus.Failed, handle.Status, "timeout is a failure");
            Spec.Equal(ResultCodes.Timeout, handle.Terminal.Code, "timeout code");
            Spec.Equal("plan,hide,load:map.new,show", rig.Joined, "unhidden after the timeout");
        }

        private static void ReservedStagingNeedsHideCallback()
        {
            var rig = new StageRig(false);
            var handle = rig.Service.Stage(Reserved(), FullLease());
            Spec.Equal(ResultCodes.Unregistered, handle.Terminal.Code, "reserved staging without a hide callback is unregistered");
            Spec.Equal(0, rig.PlannerCalls, "nothing planned");
        }
    }
}
