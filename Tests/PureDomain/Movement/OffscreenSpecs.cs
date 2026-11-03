using System;
using MagicCampus.Contracts;
using MagicCampus.World.Movement;

namespace MagicCampus.Tests.Movement
{
    /// <summary>Logical (offscreen) movement versus visible-map movement, using the real Stage2 factory/pump and the pure fakes.</summary>
    public static class OffscreenSpecs
    {
        public static void Run()
        {
            OffscreenSameMapStepsWhileAnotherMapStaysVisible();
            OffscreenCrossMapWalksToPortalWithoutLoading();
            VisibleSceneSourceLoadsBeforeFirstStep();
            VisibleAlreadyArrivedWaitsForSourceLoad();
            SourceLoadFailureAndCancelKeepDisplayedMapAndActor();
            TimeoutWhileSourceLoadPending();
        }

        // Visible: the player, or the scene-owned request used by these specs. Everything else is logical.
        private static MoveService Build(Rig rig)
        {
            return new MoveService(rig.Actors, rig.World, rig.Maps, rig.World, rig.Handles, rig.Pause, null, "player", null, null,
                request => request.ActorId == "player" || request.OwnerToken == "scene.intro");
        }

        private static void Frame(Rig rig, MoveService svc, double dt)
        {
            rig.Pump.AdvanceTick();
            svc.Tick(dt, dt);
        }

        private static void RunUntil(Rig rig, MoveService svc, IHandle handle, double dt, int max)
        {
            for (var n = 0; handle.Terminal == null && n < max; n++) Frame(rig, svc, dt);
        }

        private static void Clean(Rig rig, MoveService svc)
        {
            Frame(rig, svc, 0.0);
            Frame(rig, svc, 0.0);
            Expect.True(svc.ActiveMoves.Count == 0, "offscreen: no active moves remain");
            Expect.True(rig.Handles.LiveCount == 0, "offscreen: no live handles remain");
        }

        private static void OffscreenSameMapStepsWhileAnotherMapStaysVisible()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5), Rig.Actor("npc.a", "yard", 3.5, 1.5, 0.3, 2.0) });
            var svc = Build(rig);
            var playerAt = rig.Actors.Find("player").Position;
            var h = svc.Move(Rig.Req("npc.a", "yard", 5.5, 1.5));
            for (var i = 0; i < 5; i++) Frame(rig, svc, 0.1);
            Expect.True(h.Terminal == null, "offscreen same map: still walking");
            Expect.Near(rig.Actors.Find("npc.a").Position.X, 4.5, 0.01, "offscreen same map: real steps at actor speed");
            Expect.True(rig.Maps.ActiveMapId == "hall" && rig.Maps.LoadRequests.Count == 0, "offscreen same map: displayed map untouched, no load");
            RunUntil(rig, svc, h, 0.1, 60);
            Expect.Succeeded(h, "offscreen same map");
            Expect.True(h.Value.Arrived && h.Value.Actual.MapId == "yard", "offscreen same map: arrived on its own map");
            Expect.True(rig.Maps.ActiveMapId == "hall" && rig.Maps.LoadRequests.Count == 0, "offscreen same map: still no load at the end");
            Expect.True(rig.Actors.Find("player").Position.Equals(playerAt), "offscreen same map: player untouched");
            Clean(rig, svc);
        }

        private static void OffscreenCrossMapWalksToPortalWithoutLoading()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 3.5), Rig.Actor("npc.a", "hall", 1.5, 2.5, 0.3, 4.0) });
            var svc = Build(rig);
            var h = svc.Move(Rig.Req("npc.a", "yard", 5.5, 2.5));
            for (var i = 0; i < 200 && h.Terminal == null; i++)
            {
                Frame(rig, svc, 0.1);
                Expect.True(rig.Maps.ActiveMapId == "hall", "offscreen cross map: ActiveMapId unchanged every frame");
                Expect.True(rig.Maps.LoadRequests.Count == 0, "offscreen cross map: zero Load calls every frame");
            }
            Expect.Succeeded(h, "offscreen cross map");
            Expect.True(h.Value.Arrived && h.Value.Actual.MapId == "yard", "offscreen cross map: arrived on the destination map");
            Expect.Near(h.Value.Actual.X, 5.5, 0.11, "offscreen cross map: arrival x");
            var crossed = false;
            foreach (var e in svc.Trace) if (e.Kind == "portal-crossed") crossed = true;
            Expect.True(crossed, "offscreen cross map: portal crossing recorded");
            Expect.True(rig.Actors.Find("player").Position.MapId == "hall", "offscreen cross map: player stays on the displayed map");
            Clean(rig, svc);
        }

        private static void VisibleSceneSourceLoadsBeforeFirstStep()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "yard", 3.5, 2.5, 0.3, 2.0) });
            var svc = Build(rig);
            rig.Maps.AutoComplete = false;
            var start = rig.Actors.Find("player").Position;
            var h = svc.Move(Rig.Req("player", "yard", 5.5, 2.5, 0.1, "scene.intro"));
            Expect.True(rig.Maps.LoadRequests.Count == 1 && rig.Maps.LoadRequests[0] == "yard", "visible source: source map load requested");
            for (var i = 0; i < 5; i++)
            {
                Frame(rig, svc, 0.1);
                Expect.True(rig.Actors.Find("player").Position.Equals(start), "visible source: no step before the load succeeds");
            }
            Expect.True(h.Terminal == null && rig.Maps.Pending.Count == 1, "visible source: waiting for the load");
            Expect.True(rig.Maps.CompletePending(0, true), "visible source: load completes");
            Frame(rig, svc, 0.1);
            Expect.True(rig.Actors.Find("player").Position.X > 3.5, "visible source: first step after the load");
            RunUntil(rig, svc, h, 0.1, 60);
            Expect.Succeeded(h, "visible source");
            Expect.True(rig.Maps.LoadRequests.Count == 1 && rig.Maps.ActiveMapId == "yard", "visible source: no further load needed");
            Clean(rig, svc);
        }

        private static void VisibleAlreadyArrivedWaitsForSourceLoad()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "yard", 3.5, 2.5) });
            var svc = Build(rig);
            rig.Maps.AutoComplete = false;
            var start = rig.Actors.Find("player").Position;
            var h = svc.Move(Rig.Req("player", "yard", 3.5, 2.5, 0.1, "scene.intro"));
            for (var i = 0; i < 3; i++) Frame(rig, svc, 0.1);
            Expect.True(h.Terminal == null, "already arrived: no no-op success before the source load");
            Expect.True(rig.Maps.CompletePending(0, true), "already arrived: load completes");
            RunUntil(rig, svc, h, 0.1, 10);
            Expect.Succeeded(h, "already arrived after load");
            Expect.True(h.Value.Actual.Equals(start) && rig.Maps.ActiveMapId == "yard", "already arrived: actor unmoved, map displayed");
            Clean(rig, svc);
        }

        private static void SourceLoadFailureAndCancelKeepDisplayedMapAndActor()
        {
            var fail = new Rig("hall", new[] { Rig.Actor("player", "yard", 3.5, 2.5) });
            var failSvc = Build(fail);
            fail.Maps.FailLoads = true;
            var start = fail.Actors.Find("player").Position;
            var f = failSvc.Move(Rig.Req("player", "yard", 5.5, 2.5, 0.1, "scene.intro"));
            RunUntil(fail, failSvc, f, 0.1, 20);
            Expect.Failed(f, ResultCodes.MapLoadFailed, "source load failure");
            Expect.True(fail.Maps.ActiveMapId == "hall" && fail.Actors.Find("player").Position.Equals(start), "source load failure keeps displayed map and actor");
            Clean(fail, failSvc);

            var cancel = new Rig("hall", new[] { Rig.Actor("player", "yard", 3.5, 2.5) });
            var cancelSvc = Build(cancel);
            cancel.Maps.AutoComplete = false;
            var c = cancelSvc.Move(Rig.Req("player", "yard", 5.5, 2.5, 0.1, "scene.intro"));
            Frame(cancel, cancelSvc, 0.1);
            c.Cancel("user");
            c.Cancel("again");
            Frame(cancel, cancelSvc, 0.0);
            Expect.True(c.Terminal != null && c.Terminal.Status == OperationStatus.Cancelled, "source load cancel: parent cancelled");
            Expect.True(cancel.Maps.Pending[0].Handle.Terminal.Status == OperationStatus.Cancelled, "source load cancel: child load cancelled");
            Expect.True(!cancel.Maps.CompletePending(0, true), "source load cancel: late completion ignored");
            Expect.True(cancel.Maps.ActiveMapId == "hall" && cancel.Actors.Find("player").Position.Equals(cancel.Actors.Find("player").Position) && cancel.Actors.Find("player").Position.MapId == "yard" && cancel.Actors.Find("player").Position.X == 3.5,
                "source load cancel keeps displayed map and actor");
            Clean(cancel, cancelSvc);
        }

        private static void TimeoutWhileSourceLoadPending()
        {
            var rig = new Rig("hall", new[] { Rig.Actor("player", "yard", 3.5, 2.5) });
            var svc = Build(rig);
            rig.Maps.AutoComplete = false;
            var start = rig.Actors.Find("player").Position;
            var h = svc.Move(Rig.Req("player", "yard", 5.5, 2.5, 0.1, "scene.intro", 1.0, true));
            RunUntil(rig, svc, h, 0.1, 40);
            Expect.Failed(h, ResultCodes.Timeout, "timeout while the source load is pending");
            Expect.True(rig.Maps.Pending[0].Handle.Terminal.Status == OperationStatus.Cancelled, "timeout releases the source load child");
            Expect.True(rig.Maps.ActiveMapId == "hall" && rig.Actors.Find("player").Position.Equals(start), "timeout keeps displayed map and actor");
            Clean(rig, svc);
        }
    }
}
