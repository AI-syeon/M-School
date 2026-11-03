using System;
using System.Collections.Generic;
using MagicCampus.Application.Schedules;
using MagicCampus.Contracts;

namespace MagicCampus.Tests.PureDomain.Schedules
{
    public static class SchedulesSpecs
    {
        public static void Run()
        {
            SelectsExactlyOneDestination();
            ReportsExplicitFailures();
            YieldsToForegroundAndResumesFromCurrentClock();
            BacksOffRepeatedFailuresWithoutBusyLoop();
            StopActorsWaitsForTerminalMove();
            ReservationSpecs.Run();
            StagingSpecs.Run();
        }

        private static ScheduleService BuildService(FakeResolver resolver)
        {
            var root = Kit.Obj(
                Kit.Kv("npcs", DataNode.FromList(new[] { Kit.Npc("npc.a"), Kit.Npc("npc.b"), Kit.Npc("npc.c") })),
                Kit.Kv("schedules", DataNode.FromList(new[] { Kit.Sched("schedule.a", "npc.a"), Kit.Sched("schedule.b", "npc.b") })));
            var compiled = new Dictionary<string, IReadOnlyList<CompiledCall>>(StringComparer.Ordinal)
            {
                { "schedule.a", Kit.Calls("schedule.a", ("420", "dest.x"), ("720", "dest.y"), ("1080", "home")) },
                { "schedule.b", Kit.Calls("schedule.b", ("420", "dest.x"), ("420", "dest.y")) }
            };
            return new ScheduleService(new FakeCatalog(new CompiledEvent[0], root), compiled, resolver);
        }

        private static string DestId(ScheduleService service, string npc, long day, int minute)
        {
            var outcome = service.CurrentDestination(npc, new GameTime(day * GameTime.MinutesPerDay + minute, 0));
            Spec.True(outcome.Succeeded, "destination for " + npc + " at " + minute);
            return outcome.Value.DestinationId;
        }

        private static void SelectsExactlyOneDestination()
        {
            var service = BuildService(new FakeResolver());
            Spec.Equal("dest.home_a", DestId(service, "npc.a", 3, 419), "before the first entry the previous day's last entry applies");
            Spec.Equal("dest.x", DestId(service, "npc.a", 3, 420), "first entry at its start minute");
            Spec.Equal("dest.x", DestId(service, "npc.a", 3, 719), "still first entry");
            Spec.Equal("dest.y", DestId(service, "npc.a", 3, 720), "second entry");
            Spec.Equal("dest.y", DestId(service, "npc.a", 3, 1079), "second entry until the next start");
            Spec.Equal("dest.home_a", DestId(service, "npc.a", 3, 1080), "home keyword resolves the npc home");
            Spec.Equal("dest.home_a", DestId(service, "npc.a", 5, 0), "midnight uses the evening entry, not the morning path");
            Spec.Equal("dest.home_a", DestId(service, "npc.a", 5, 1439), "late evening");
            Spec.Equal("dest.x", DestId(service, "npc.a", 9, 421), "later day starts again from the morning entry");
        }

        private static void ReportsExplicitFailures()
        {
            var service = BuildService(new FakeResolver());
            var now = new GameTime(1440 + 500, 0);
            Spec.Equal(ResultCodes.RefNotFound, service.CurrentDestination("npc.zzz", now).Result.Code, "unknown npc is explicit");
            Spec.Equal(ResultCodes.RefNotFound, service.CurrentDestination("npc.c", now).Result.Code, "npc without schedule row is explicit");
            var bad = service.CurrentDestination("npc.b", now);
            Spec.Equal(ResultCodes.ContentInvalid, bad.Result.Code, "duplicate start minute is invalid content");
            Spec.True(bad.Result.Details.Count > 0, "invalid schedule carries diagnostics");
            Spec.True(service.CurrentDestination("npc.a", now).Succeeded, "other npc stays usable");
        }

        private sealed class ControllerRig
        {
            internal readonly Rt Rt = new Rt();
            internal readonly FakeClock Clock = new FakeClock { Now = new GameTime(GameTime.MinutesPerDay + 420, 0) };
            internal readonly FakePause Pause = new FakePause();
            internal readonly List<string> Log = new List<string>();
            internal readonly HashSet<string> Leased = new HashSet<string>(StringComparer.Ordinal);
            internal readonly FakeWorld World;
            internal readonly FakeMove Move;
            internal readonly ScheduleController Controller;

            internal ControllerRig()
            {
                World = new FakeWorld(Log, Kit.Actor("npc.a", "map.t", 0.5, 0.5));
                Move = new FakeMove(Rt.Handles);
                Controller = new ScheduleController(BuildService(new FakeResolver()), Clock, Pause, World, Move, Rt.Handles,
                    new[] { "npc.a" }, id => Leased.Contains(id));
            }
        }

        private static void YieldsToForegroundAndResumesFromCurrentClock()
        {
            var rig = new ControllerRig();

            rig.Pause.Effective = new PauseSet(false, true, false, false, false);
            rig.Controller.Tick(1);
            Spec.Equal(0, rig.Move.Requests.Count, "paused ambient channel starts nothing");
            rig.Pause.Effective = PauseSet.None;

            rig.Controller.Tick(1);
            Spec.Equal(1, rig.Move.Requests.Count, "ambient move starts toward the current destination");
            Spec.Equal("dest.x", rig.Move.Requests[0].Destination.DestinationId, "morning destination");
            Spec.Equal("ambient:npc.a", rig.Move.Requests[0].OwnerToken, "owner token");
            rig.Controller.Tick(1);
            Spec.Equal(1, rig.Move.Requests.Count, "no second move while one is running");

            rig.Leased.Add("npc.a");
            rig.Controller.Tick(1);
            Spec.Equal(1, rig.Move.CancelReasons.Count, "leased npc's ambient move is cancelled");
            rig.Controller.Tick(1);
            Spec.Equal(1, rig.Move.CancelReasons.Count, "cancel is requested once");
            Spec.True(rig.Controller.IsMoving("npc.a"), "controller waits for the terminal state");
            Spec.Equal(1, rig.Move.Requests.Count, "nothing new while cancelling");

            rig.Move.Handles[0].Cancelled("yielded", 0);
            for (var i = 0; i < 4; i++) rig.Controller.Tick(1);
            Spec.True(!rig.Controller.IsMoving("npc.a"), "terminal move is released");
            Spec.Equal(1, rig.Move.Requests.Count, "foreground lease is never preempted");

            rig.Clock.Now = new GameTime(GameTime.MinutesPerDay + 720, 0);
            rig.Leased.Remove("npc.a");
            rig.Controller.Tick(1);
            Spec.Equal(2, rig.Move.Requests.Count, "resume after release");
            Spec.Equal("dest.y", rig.Move.Requests[1].Destination.DestinationId, "resume uses the current clock, not the stale morning path");
            Spec.Equal(0, rig.World.SetPositionCalls, "no teleport");
            Spec.Equal(0, rig.World.RestoreCalls, "no actor restore");
        }

        private static void BacksOffRepeatedFailuresWithoutBusyLoop()
        {
            var rig = new ControllerRig();
            rig.Move.AutoFail = true;
            for (var i = 0; i < 100; i++) rig.Controller.Tick(0.1);
            var count = rig.Move.Requests.Count;
            Spec.True(count >= 2, "failed target is retried");
            Spec.True(count <= 5, "retries are bounded by backoff, saw " + count);
            Spec.Equal(ResultCodes.NoRoute, rig.Controller.LastResult("npc.a").Code, "last failure is kept");
        }

        private static void StopActorsWaitsForTerminalMove()
        {
            var rig = new ControllerRig();
            rig.Controller.Tick(1);
            Spec.Equal(1, rig.Move.Requests.Count, "move running");

            var stop = rig.Controller.StopActors(new[] { "npc.a", "space.map.t" });
            Spec.Equal(1, rig.Move.CancelReasons.Count, "stop cancels the ambient move");
            Spec.True(!stop.Status.IsTerminal(), "stop waits for the move to terminate");

            rig.Move.Handles[0].Cancelled("stopped", 0);
            rig.Rt.Step();
            Spec.Equal(OperationStatus.Succeeded, stop.Status, "stop completes after the move is terminal");

            var idle = rig.Controller.StopActors(new[] { "npc.q" });
            Spec.Equal(OperationStatus.Succeeded, idle.Status, "nothing to stop succeeds immediately");
        }
    }
}
