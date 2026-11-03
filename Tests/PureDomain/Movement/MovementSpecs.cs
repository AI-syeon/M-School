using System;
using MagicCampus.Contracts;
using MagicCampus.World.Movement;
using MagicCampus.World.Navigation;

namespace MagicCampus.Tests.Movement
{
    public static class MovementSpecs
    {
        public static void Run()
        {
            SameMapArrivesOnlyAfterRealSteps();
            DiagonalMovementIsSpeedNormalized();
            FastActorNeverTunnelsThroughWalls();
            TwoMapRouteLoadsBeforeRelocation();
            RefusalsAreExplicitAndNeverMoveTheActor();
            DoorWidthDependsOnActorRadius();
            ClosingPortalAndNavRevisionDuringMovement();
            MovingBlockerReplansAndStuckIsBounded();
            LoadFailureCancelTimeoutAndRollbackKeepOldMap();
            WatchdogTimesOutWhilePaused();
            SceneMovementUsesSceneChannel();
            DuplicateCancelProducesOneTerminal();
            ExternalRestoreCancelsTheMove();
            ConstructorAndTickGuards();
            ActorWorldSpecs.Run();
            ResolverSpecs.Run();
            ProjectionSpecs.Run();
            OffscreenSpecs.Run();
        }

        private static void SameMapArrivesOnlyAfterRealSteps()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 2.0) });
            var h = rig.Moves.Move(Rig.Req("player", "hall", 5.5, 2.5));
            Expect.True(h.Status == OperationStatus.Running, "handle is running");
            var previous = rig.Actors.Find("player").Position;
            for (var i = 0; i < 10; i++)
            {
                rig.Frame(0.1);
                var now = rig.Actors.Find("player").Position;
                Expect.True(NavMath.Distance(previous, now) <= 0.2 + 1e-6, "step bounded by speed");
                previous = now;
            }
            Expect.True(h.Terminal == null, "no arrival before the steps are walked");
            Expect.Near(previous.X, 3.5, 0.01, "two seconds at speed 2 cover two tiles");
            var frames = 10 + rig.RunUntil(h, 0.1, 60);
            Expect.True(frames >= 19 && frames <= 22, "arrival frame count " + frames);
            Expect.Succeeded(h, "same map move");
            Expect.True(h.Value.Arrived && h.Value.Distance <= 0.1, "arrival judged from actual distance");
            Expect.True(h.Value.Actual.Equals(rig.Actors.Find("player").Position), "reported position is the actor position");
            rig.AssertClean();
        }

        private static void DiagonalMovementIsSpeedNormalized()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 1.5, 0.3, 3.0) });
            var h = rig.Moves.Move(Rig.Req("player", "hall", 4.5, 4.5));
            var start = rig.Actors.Find("player").Position;
            var previous = start;
            for (var i = 0; i < 10; i++)
            {
                rig.Frame(0.05);
                var now = rig.Actors.Find("player").Position;
                Expect.True(NavMath.Distance(previous, now) <= 0.15 + 1e-6, "diagonal step is not faster than straight");
                previous = now;
            }
            Expect.Near(NavMath.Distance(start, previous), 1.5, 0.02, "diagonal covers speed times time");
            var frames = 10 + rig.RunUntil(h, 0.05, 80);
            Expect.True(frames >= 28 && frames <= 30, "diagonal duration frames " + frames);
            Expect.Succeeded(h, "diagonal move");
            rig.AssertClean();
        }

        private static void FastActorNeverTunnelsThroughWalls()
        {
            var rig = new Rig("wall", new[] { Rig.Actor("player", "wall", 1.5, 2.5, 0.3, 40.0) });
            var h = rig.Moves.Move(Rig.Req("player", "wall", 8.5, 2.5));
            for (var i = 0; i < 50 && h.Terminal == null; i++)
            {
                rig.Frame(0.1);
                Expect.True(rig.World.IsPassable(rig.Actors.Find("player").Position, 0.3), "actor never inside a wall");
            }
            Expect.Succeeded(h, "move around the wall");
            Expect.Near(h.Value.Actual.X, 8.5, 0.11, "ended behind the wall");
            rig.AssertClean();
        }

        private static void TwoMapRouteLoadsBeforeRelocation()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });
            var atLoad = default(Position);
            rig.Maps.OnLoadRequested = m => atLoad = rig.Actors.Find("player").Position;
            var h = rig.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5));
            for (var i = 0; i < 200 && h.Terminal == null; i++)
            {
                rig.Frame(0.1);
                var p = rig.Actors.Find("player").Position;
                if (p.MapId == "yard") Expect.True(rig.Maps.LoadRequests.Count == 1 && rig.Maps.ActiveMapId == "yard", "relocated only after the load");
                if (h.Terminal != null) Expect.True(p.MapId == "yard", "no arrival on the wrong map");
            }
            Expect.Succeeded(h, "two map route");
            Expect.True(h.Value.Actual.MapId == "yard" && h.Value.Arrived, "arrived on the destination map");
            Expect.Near(h.Value.Actual.X, 5.5, 0.11, "arrival x");
            Expect.True(rig.Maps.LoadRequests.Count == 1 && rig.Maps.LoadRequests[0] == "yard", "exactly one load");
            Expect.True(atLoad.MapId == "hall" && Math.Abs(atLoad.X - 8.5) < 0.15, "load requested while standing on the portal");
            rig.AssertClean();
        }

        private static void RefusalsAreExplicitAndNeverMoveTheActor()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5), Rig.Actor("stuck", "hall", 0.5, 0.5) });
            var home = Rig.Pos("hall", 1.5, 2.5);
            rig.Maps.SetOpen("hall-east", false);
            Expect.Failed(rig.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5)), ResultCodes.NoRoute, "closed portal");
            Expect.Failed(rig.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5, 0.1, "owner.test", 60, false)), ResultCodes.NoRoute, "portals disabled");
            Expect.Failed(rig.Moves.Move(Rig.Req("player", "hall", 0.5, 0.5)), ResultCodes.TargetBlocked, "target inside a wall");
            var startBlocked = rig.Moves.Move(Rig.Req("stuck", "hall", 5.5, 2.5));
            Expect.Failed(startBlocked, ResultCodes.TargetBlocked, "start blocked");
            Expect.True(startBlocked.Terminal.Details.Count > 0 && startBlocked.Terminal.Details[0].Code == "START_BLOCKED", "start blocked diagnostic");
            Expect.Failed(rig.Moves.Move(Rig.Req("ghost", "hall", 5.5, 2.5)), ResultCodes.RefNotFound, "unknown actor");
            Expect.True(rig.Maps.LoadRequests.Count == 0 && rig.Actors.Find("player").Position.Equals(home), "refusals leave everything untouched");

            var busy = rig.Moves.Move(Rig.Req("player", "hall", 5.5, 2.5));
            Expect.Failed(rig.Moves.Move(Rig.Req("player", "hall", 6.5, 2.5)), ResultCodes.ResourceBusy, "second move for the same actor");
            busy.Cancel("test");
            rig.AssertClean();

            var sealedRig = new Rig("sealed", new[] { Rig.Actor("player", "sealed", 1.5, 2.5) });
            Expect.Failed(sealedRig.Moves.Move(Rig.Req("player", "sealed", 6.5, 2.5)), ResultCodes.NoRoute, "walled off target");
            sealedRig.AssertClean();

            var near = new Rig("hall", new[] { Rig.Actor("player", "hall", 4.5, 2.5) });
            var h = near.Moves.Move(Rig.Req("player", "hall", 0.5, 2.5, 1.2));
            near.RunUntil(h, 0.1, 60);
            Expect.Succeeded(h, "tolerance reaches a blocked target from a standable spot");
            Expect.True(h.Value.Distance <= 1.2 && h.Value.Arrived, "stopped within tolerance");
            near.AssertClean();
        }

        private static void DoorWidthDependsOnActorRadius()
        {
            var slim = new Rig("door", new[] { Rig.Actor("player", "door", 3.5, 2.5, 0.3, 3.0) });
            var h = slim.Moves.Move(Rig.Req("player", "door", 7.5, 2.5));
            slim.RunUntil(h, 0.1, 100);
            Expect.Succeeded(h, "narrow actor passes a one tile door");
            slim.AssertClean();

            var wide = new Rig("door", new[] { Rig.Actor("player", "door", 3.5, 2.5, 0.6, 3.0) });
            Expect.Failed(wide.Moves.Move(Rig.Req("player", "door", 7.5, 2.5)), ResultCodes.NoRoute, "wide actor cannot use the door");
            wide.AssertClean();
        }

        private static void ClosingPortalAndNavRevisionDuringMovement()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });
            var h = rig.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5));
            for (var i = 0; i < 10; i++) rig.Frame(0.1);
            rig.Maps.SetOpen("hall-east", false);
            rig.RunUntil(h, 0.1, 5);
            Expect.Failed(h, ResultCodes.NoRoute, "portal closed while walking");
            var p = rig.Actors.Find("player").Position;
            Expect.True(p.MapId == "hall" && p.X < 6.5, "actor stopped where it was, no warp");
            Expect.True(rig.Maps.LoadRequests.Count == 0, "no load after the portal closed");
            rig.AssertClean();

            var bump = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });
            var g = bump.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5));
            for (var i = 0; i < 5; i++) bump.Frame(0.1);
            bump.Maps.BumpNav();
            bump.RunUntil(g, 0.1, 200);
            Expect.Succeeded(g, "navigation revision change replans and still arrives");
            var replanned = false;
            foreach (var e in bump.Moves.Trace) if (e.Kind == "replan") replanned = true;
            Expect.True(replanned, "replan recorded in trace");
            bump.AssertClean();
        }

        private static void MovingBlockerReplansAndStuckIsBounded()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 3.0) });
            var h = rig.Moves.Move(Rig.Req("player", "hall", 8.5, 2.5));
            for (var i = 0; i < 5; i++) rig.Frame(0.1);
            rig.World.Block("hall", 4, 1);
            rig.World.Block("hall", 4, 2);
            rig.World.Block("hall", 4, 3);
            rig.Maps.BumpNav();
            for (var i = 0; i < 100 && h.Terminal == null; i++)
            {
                rig.Frame(0.1);
                Expect.True(rig.World.IsPassable(rig.Actors.Find("player").Position, 0.3), "never enters a blocker");
            }
            Expect.Succeeded(h, "detour around the new blockers");

            var stuck = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 3.0) }, null, new MoveOptions(stuckSeconds: 1.0));
            var s = stuck.Moves.Move(Rig.Req("player", "hall", 8.5, 2.5));
            // Add a sweep-only barrier after planning, so movement meets an unexpected obstruction.
            for (var y = 1; y <= 4; y++) stuck.World.Ghost("hall", 4, y);
            var frames = stuck.RunUntil(s, 0.1, 100);
            Expect.True(frames < 60, "stuck move ends in bounded time");
            Expect.Failed(s, ResultCodes.NoRoute, "stuck");
            var marked = false;
            foreach (var d in s.Terminal.Details) if (d.Code == "STUCK") marked = true;
            Expect.True(marked, "stuck diagnostic");
            Expect.True(stuck.Actors.Find("player").Position.X < 3.8, "actor never passes the ghost blocker");
            var replans = 0;
            foreach (var e in stuck.Moves.Trace) if (e.Kind == "replan") replans++;
            Expect.True(replans <= 32, "replans bounded");
            stuck.AssertClean();
            rig.AssertClean();
        }

        private static void LoadFailureCancelTimeoutAndRollbackKeepOldMap()
        {
            var fail = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });
            fail.Maps.FailLoads = true;
            var f = fail.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5));
            fail.RunUntil(f, 0.1, 100);
            Expect.Failed(f, ResultCodes.MapLoadFailed, "failed load");
            Expect.True(fail.Maps.ActiveMapId == "hall" && fail.Actors.Find("player").Position.MapId == "hall", "failed load keeps the old map and position");
            fail.AssertClean();

            var cancel = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });
            cancel.Maps.AutoComplete = false;
            var c = cancel.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5));
            for (var i = 0; i < 100 && cancel.Maps.Pending.Count == 0; i++) cancel.Frame(0.1);
            Expect.True(cancel.Maps.Pending.Count == 1 && c.Terminal == null, "waiting for the map load");
            var seen = 0;
            c.OnTerminal(r => seen++);
            c.Cancel("user");
            c.Cancel("again");
            cancel.Frame(0.0);
            Expect.True(c.Terminal.Status == OperationStatus.Cancelled && seen == 1, "duplicate cancel yields one terminal");
            Expect.True(cancel.Maps.Pending[0].Handle.Terminal.Status == OperationStatus.Cancelled, "load child cancelled");
            Expect.True(!cancel.Maps.CompletePending(0, true), "late load completion is ignored");
            Expect.True(cancel.Maps.ActiveMapId == "hall" && cancel.Actors.Find("player").Position.MapId == "hall", "cancelled load keeps the old map");
            cancel.AssertClean();

            var slow = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });
            slow.Maps.AutoComplete = false;
            var t = slow.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5, 0.1, "owner.test", 3.0, true));
            slow.RunUntil(t, 0.1, 100);
            Expect.Failed(t, ResultCodes.Timeout, "timeout while loading");
            Expect.True(slow.Maps.Pending[0].Handle.Terminal.Status == OperationStatus.Cancelled, "timeout releases the load");
            Expect.True(slow.Maps.ActiveMapId == "hall", "timeout keeps the old map");
            slow.AssertClean();

            var back = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });
            back.World.Block("yard", 2, 2);
            var b = back.Moves.Move(Rig.Req("player", "yard", 5.5, 2.5));
            back.RunUntil(b, 0.1, 100);
            Expect.Failed(b, ResultCodes.TargetBlocked, "blocked portal arrival");
            Expect.True(back.Maps.LoadRequests.Count == 2 && back.Maps.LoadRequests[1] == "hall", "previous map reloaded");
            Expect.True(back.Maps.ActiveMapId == "hall" && back.Actors.Find("player").Position.MapId == "hall", "actor and map stay consistent");
            back.AssertClean();
        }

        private static void WatchdogTimesOutWhilePaused()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5) });
            var h = rig.Moves.Move(Rig.Req("player", "hall", 8.5, 2.5, 0.1, "owner.test", 1.0, true));
            var menu = rig.Pause.Acquire("menu", PauseSet.All);
            var start = rig.Actors.Find("player").Position;
            var frames = rig.RunUntil(h, 0.1, 40);
            Expect.Failed(h, ResultCodes.Timeout, "watchdog");
            Expect.True(frames >= 10 && frames <= 11, "watchdog uses the unpaused clock, frames " + frames);
            Expect.True(rig.Actors.Find("player").Position.Equals(start), "paused actor did not move");
            rig.Pause.Release(menu);
            rig.AssertClean();
        }

        private static void SceneMovementUsesSceneChannel()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 1.5), Rig.Actor("npc.a", "hall", 1.5, 3.5) },
                owner => owner.StartsWith("scene.", StringComparison.Ordinal));
            var scene = rig.Moves.Move(Rig.Req("npc.a", "hall", 4.5, 3.5, 0.1, "scene.intro"));
            var free = rig.Moves.Move(Rig.Req("player", "hall", 4.5, 1.5, 0.1, "free.walk"));
            var cutscene = rig.Pause.Acquire("cutscene", new PauseSet(true, true, true, true, false));
            for (var i = 0; i < 10; i++) rig.Frame(0.1);
            Expect.Near(rig.Actors.Find("npc.a").Position.X, 3.5, 0.01, "scene move runs with calendar and world paused");
            Expect.Near(rig.Actors.Find("player").Position.X, 1.5, 1e-9, "free move obeys the pause");
            var menu = rig.Pause.Acquire("menu", PauseSet.All);
            var frozen = rig.Actors.Find("npc.a").Position;
            for (var i = 0; i < 5; i++) rig.Frame(0.1);
            Expect.True(rig.Actors.Find("npc.a").Position.Equals(frozen), "menu pause stops scene movement too");
            rig.Pause.Release(menu);
            rig.Pause.Release(cutscene);
            for (var i = 0; i < 100 && (scene.Terminal == null || free.Terminal == null); i++) rig.Frame(0.1);
            Expect.Succeeded(scene, "scene move");
            Expect.Succeeded(free, "free move");
            rig.AssertClean();

            var two = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 1.5), Rig.Actor("npc.b", "hall", 1.5, 3.5) });
            var p = two.Moves.Move(Rig.Req("player", "hall", 4.5, 1.5, 0.1, "free.a"));
            var n = two.Moves.Move(Rig.Req("npc.b", "hall", 4.5, 3.5, 0.1, "free.b"));
            var ambient = two.Pause.Acquire("ambient", new PauseSet(false, true, false, false, false));
            for (var i = 0; i < 10; i++) two.Frame(0.1);
            Expect.Near(two.Actors.Find("player").Position.X, 3.5, 0.01, "player ignores the ambient pause");
            Expect.Near(two.Actors.Find("npc.b").Position.X, 1.5, 1e-9, "npc obeys the ambient pause");
            two.Pause.Release(ambient);
            for (var i = 0; i < 100 && (p.Terminal == null || n.Terminal == null); i++) two.Frame(0.1);
            Expect.Succeeded(n, "npc resumes");
            two.AssertClean();
        }

        private static void DuplicateCancelProducesOneTerminal()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5) });
            var h = rig.Moves.Move(Rig.Req("player", "hall", 8.5, 2.5));
            var seen = 0;
            h.OnTerminal(r => seen++);
            for (var i = 0; i < 3; i++) rig.Frame(0.1);
            var at = rig.Actors.Find("player").Position;
            h.Cancel("one");
            h.Cancel("two");
            rig.Frame(0.1);
            rig.Frame(0.1);
            Expect.True(h.Terminal.Status == OperationStatus.Cancelled && seen == 1, "one cancelled terminal");
            Expect.True(rig.Actors.Find("player").Position.Equals(at), "cancel leaves the last valid position");
            rig.AssertClean();
        }

        private static void ExternalRestoreCancelsTheMove()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5) });
            var h = rig.Moves.Move(Rig.Req("player", "hall", 8.5, 2.5));
            for (var i = 0; i < 3; i++) rig.Frame(0.1);
            var restored = rig.Actors.Restore(new[] { Rig.Actor("player", "hall", 6.5, 2.5) });
            Expect.True(restored.Status == OperationStatus.Succeeded, "restore accepted");
            rig.Frame(0.1);
            Expect.True(h.Terminal != null && h.Terminal.Status == OperationStatus.Cancelled, "restore ends the stale move");
            Expect.Near(rig.Actors.Find("player").Position.X, 6.5, 1e-9, "restored position kept");
            rig.AssertClean();
        }

        private static void ConstructorAndTickGuards()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5) });
            Expect.Throws<ArgumentNullException>(() => new MoveService(null, rig.World, rig.Maps, rig.World, rig.Handles, rig.Pause), "actors required");
            Expect.Throws<ArgumentNullException>(() => rig.Moves.Move(null), "request required");
            Expect.Throws<ArgumentOutOfRangeException>(() => rig.Moves.Tick(-1, 0), "negative simulation delta");
            Expect.Throws<ArgumentOutOfRangeException>(() => rig.Moves.Tick(0, double.NaN), "NaN watchdog delta");
            rig.AssertClean();
        }
    }
}
