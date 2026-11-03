using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Runtime;

namespace MagicCampus.Tests.Runtime
{
    public static class RuntimeSpecs
    {
        public static void Run()
        {
            HandleTerminatesOnce();
            CancelWithoutHandlerFinalizesDirectly();
            CancelWithHandlerWaitsForExplicitFinalizer();
            CallbacksAreNextTickIncludingLate();
            PumpOrderingAndIsolation();
            LeasesOverlapBusyNoPartialNoPreempt();
            PauseUnionAndScenePolicy();
            TickClockStallsDelaysWhilePaused();
            DelaysCompleteInDeterministicOrder();
        }

        // ------------------------------------------------------------ helpers

        private sealed class Env
        {
            public readonly CompletionPump Pump = new CompletionPump();
            public readonly HandleFactory Factory;
            public Env() { Factory = new HandleFactory(Pump); }
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Runtime spec failed: " + message);
        }

        private static void Eq<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Runtime spec failed: " + message + " (expected " + expected + ", actual " + actual + ")");
        }

        private static void Throws<TEx>(Action action, string message) where TEx : Exception
        {
            try { action(); }
            catch (TEx) { return; }
            throw new InvalidOperationException("Runtime spec failed: expected exception " + typeof(TEx).Name + " for " + message);
        }

        private static string J(IEnumerable<string> items)
        {
            return string.Join(",", items);
        }

        private static LeaseRequest Req(string owner, params string[] resources)
        {
            return new LeaseRequest(owner, resources);
        }

        // ------------------------------------------------------------ handles

        private static void HandleTerminatesOnce()
        {
            var e = new Env();
            var h = e.Factory.Create<int>("op.1");
            Eq(OperationStatus.Pending, h.Status, "starts pending");
            True(h.Terminal == null, "no terminal yet");
            h.Start();
            Eq(OperationStatus.Running, h.Status, "running after Start");
            var calls = 0;
            Result seen = null;
            h.OnTerminal(r => { calls++; seen = r; });
            True(h.Succeed(7, 3), "first terminal wins");
            True(!h.Succeed(8, 4), "second success ignored");
            True(!h.Fail("LATE", 5, null), "late failure ignored");
            True(!h.Cancelled("late", 6), "late cancelled ignored");
            h.Cancel("late");
            h.Cancel("late");
            Eq(OperationStatus.Succeeded, h.Status, "status stays succeeded");
            Eq(7, h.Value, "value kept");
            Eq(3L, h.Terminal.Revision, "revision kept");
            Eq(0, calls, "callback never inline");
            e.Pump.AdvanceTick();
            Eq(1, calls, "callback delivered next tick");
            True(ReferenceEquals(seen, h.Terminal), "callback receives the terminal result");
            e.Pump.AdvanceTick();
            Eq(1, calls, "callback exactly once");
            Eq(0, e.Factory.LiveCount, "no live handles");
            Eq(1, e.Factory.CreatedCount, "created count");
        }

        private static void CancelWithoutHandlerFinalizesDirectly()
        {
            var e = new Env();
            var h = e.Factory.Create<int>("op.2");
            h.Start();
            var calls = 0;
            h.OnTerminal(r => calls++);
            h.Cancel("user");
            h.Cancel("again");
            Eq(OperationStatus.Cancelled, h.Status, "cancelled directly");
            Eq(ResultCodes.Cancelled, h.Terminal.Code, "cancelled code");
            Eq("user", h.Terminal.Details[0].Message, "first reason kept");
            True(!h.Succeed(1, 1), "no re-termination");
            e.Pump.AdvanceTick();
            e.Pump.AdvanceTick();
            Eq(1, calls, "one delivery");
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void CancelWithHandlerWaitsForExplicitFinalizer()
        {
            var e = new Env();
            var h = e.Factory.Create<int>("op.3");
            var asked = 0;
            string askedReason = null;
            h.SetCancellationHandler(r => { asked++; askedReason = r; });
            h.Start();
            h.Cancel("stop");
            h.Cancel("stop again");
            Eq(1, asked, "handler asked once");
            Eq("stop", askedReason, "first reason forwarded");
            Eq(OperationStatus.Running, h.Status, "no premature terminal");
            True(h.Terminal == null, "terminal still null");
            True(((Handle<int>)h).CancelRequested, "cancel recorded");
            True(h.Cancelled("cleaned", 9), "explicit finalizer");
            Eq(OperationStatus.Cancelled, h.Status, "cancelled");
            True(!h.Succeed(1, 1), "late completion ignored");
            h.Cancel("x");
            Eq(1, asked, "handler not asked again");
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void CallbacksAreNextTickIncludingLate()
        {
            var e = new Env();
            var h = e.Factory.Create<int>("op.4");
            var order = new List<string>();
            h.OnTerminal(r => order.Add("early"));
            h.Succeed(1, 0);
            h.OnTerminal(r =>
            {
                order.Add("late");
                h.OnTerminal(rr => order.Add("nested"));
            });
            Eq(0, order.Count, "nothing inline");
            e.Pump.AdvanceTick();
            Eq("early,late", J(order), "early then late registration");
            e.Pump.AdvanceTick();
            Eq("early,late,nested", J(order), "nested registration is next tick");
            e.Pump.AdvanceTick();
            Eq(3, order.Count, "each exactly once");
        }

        private static void PumpOrderingAndIsolation()
        {
            var p = new CompletionPump();
            var log = new List<string>();
            p.Post(() => log.Add("a"));
            p.Post(() => log.Add("a"));
            p.Post(() => log.Add("b"));
            Eq(0, log.Count, "post never runs inline");
            Eq(3, p.PendingCount, "every post is queued");
            p.AdvanceTick();
            Eq("a,a,b", J(log), "FIFO with duplicates kept");

            log.Clear();
            p.Post(() =>
            {
                log.Add("x");
                p.Post(() => log.Add("y"));
                p.AdvanceTick();
            });
            p.AdvanceTick();
            Eq("x", J(log), "posts during drain deferred; re-entrant drain ignored");
            p.AdvanceTick();
            Eq("x,y", J(log), "deferred item runs next tick");

            log.Clear();
            p.Post(() => { throw new InvalidOperationException("boom"); });
            p.Post(() => log.Add("after"));
            p.AdvanceTick();
            Eq(1, p.FaultCount, "fault counted");
            Eq("after", J(log), "later items still run");
            Eq(0, p.PendingCount, "drained");
        }

        // ------------------------------------------------------------ leases

        private static void LeasesOverlapBusyNoPartialNoPreempt()
        {
            var svc = new LeaseService();
            var a = svc.TryAcquire(Req("npc.a", "b", "a"));
            True(a.Succeeded, "first lease granted");
            Eq("a,b", J(a.Value.Resources), "resources canonical sorted");
            var b = svc.TryAcquire(Req("npc.b", "c", "b"));
            True(!b.Succeeded, "overlapping lease refused");
            Eq(ResultCodes.ResourceBusy, b.Result.Code, "busy code");
            Eq(1, b.Result.Details.Count, "one diagnostic per busy resource");
            True(svc.HolderOf("c") == null, "no partial acquisition");
            Eq(2, svc.HeldResourceCount, "only the first lease holds resources");

            var hi = svc.TryAcquire(Req("npc.hi", "a"));
            True(!hi.Succeeded, "a second request never preempts a holder");
            Eq(a.Value.LeaseId, svc.HolderOf("a"), "active holder not preempted");

            svc.Release(a.Value.LeaseId);
            Eq(0, svc.HeldResourceCount, "release frees every resource");
            var again = svc.TryAcquire(Req("npc.hi", "a"));
            True(again.Succeeded, "freed resource can be acquired");
            Eq(again.Value.LeaseId, svc.HolderOf("a"), "new holder owns a");
            Eq(1, svc.HeldResourceCount, "only the new lease holds resources");
            svc.Release(a.Value.LeaseId);
            Eq(1, svc.HeldResourceCount, "repeated release idempotent");
            svc.Release("unknown-lease");
            svc.Release(again.Value.LeaseId);
            Eq(0, svc.ActiveLeaseCount, "no active leases");
            Eq(0, svc.HeldResourceCount, "no leaked resources");
        }

        // ------------------------------------------------------------ pause

        private static void PauseUnionAndScenePolicy()
        {
            var ps = new PauseService();
            True(!ps.Effective.Calendar && !ps.Effective.Scene, "nothing paused initially");
            var scene = ps.AcquireScene("scene.1");
            True(ps.Effective.Calendar && ps.Effective.Ambient && ps.Effective.Player && ps.Effective.World, "scene pauses four channels");
            True(!ps.Effective.Scene, "scene channel keeps running so scene-owned movement continues");
            var menu = ps.AcquireMenu("menu");
            True(ps.Effective.Scene, "menu pauses everything");
            Eq(2, ps.LiveTokenCount, "two tokens");
            ps.Release(menu);
            ps.Release(menu);
            True(!ps.Effective.Scene, "menu released");
            Eq(1, ps.LiveTokenCount, "repeated release idempotent");
            var cal = ps.Acquire("calendar-only", new PauseSet(true, false, false, false, false));
            ps.Release(scene);
            True(ps.Effective.Calendar && !ps.Effective.Ambient && !ps.Effective.Player && !ps.Effective.World && !ps.Effective.Scene, "union of remaining token");
            var world = ps.Acquire("world-only", new PauseSet(false, false, false, true, false));
            True(ps.Effective.Calendar && ps.Effective.World && !ps.Effective.Player, "channel union");
            ps.Release(cal);
            ps.Release(world);
            Eq(0, ps.LiveTokenCount, "all tokens released");
            True(!ps.Effective.Calendar && !ps.Effective.World, "nothing paused at finish");
            Throws<ArgumentNullException>(() => ps.Release(null), "null token");
        }

        // ------------------------------------------------------------ tick clock

        private static void TickClockStallsDelaysWhilePaused()
        {
            var e = new Env();
            var clock = new TickClock(e.Factory);
            var delay = clock.Delay(2.0);
            Eq(OperationStatus.Running, delay.Status, "delay running");
            clock.Advance(0);
            clock.Advance(0);
            Eq(2L, clock.TickCount, "paused ticks are counted");
            Eq(OperationStatus.Running, delay.Status, "delay does not advance while paused");
            clock.Advance(1.0);
            Eq(OperationStatus.Running, delay.Status, "delay not due yet");
            clock.Advance(1.0);
            Eq(OperationStatus.Succeeded, delay.Status, "delay completes from the advanced seconds");
            Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1), "negative delta");
            Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.NaN), "NaN delta");
            Eq(4L, clock.TickCount, "rejected ticks do not advance");
            e.Pump.AdvanceTick();
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void DelaysCompleteInDeterministicOrder()
        {
            var e = new Env();
            var clock = new TickClock(e.Factory);
            var log = new List<string>();
            var d1 = clock.Delay(1.0);
            var d2 = clock.Delay(1.0);
            var d3 = clock.Delay(0.5);
            d1.OnTerminal(r => log.Add("a"));
            d2.OnTerminal(r => log.Add("b"));
            d3.OnTerminal(r => log.Add("c"));
            clock.Advance(1.0);
            Eq(0, log.Count, "callbacks are next tick");
            e.Pump.AdvanceTick();
            Eq("c,a,b", J(log), "due time then creation order");

            var d4 = clock.Delay(5.0);
            d4.Cancel("done");
            Eq(OperationStatus.Cancelled, d4.Status, "cancelled delay is terminal");
            clock.Advance(10.0);
            Eq(OperationStatus.Cancelled, d4.Status, "cancelled delay is not completed later");
            e.Pump.AdvanceTick();
            Eq(0, e.Factory.LiveCount, "no live handles");
        }
    }
}
