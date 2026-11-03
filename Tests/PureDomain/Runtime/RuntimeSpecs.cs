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
            PumpOrderingDedupAndIsolation();
            LeasesOverlapBusyNoPartialNoPreempt();
            LeaseQueueOrderAndCancellation();
            LeaseQueueDoesNotAllowOvertaking();
            PauseUnionAndScenePolicy();
            TickClockSeparatesSimulationFromUnpaused();
            DelaysCompleteInDeterministicOrder();
            TickDriverOrder();
            ScopeCancelOrdering();
            ScopeFailedChildCancelsSiblings();
            ScopeSuccessAndUpgrade();
        }

        // ------------------------------------------------------------ helpers

        private sealed class Env
        {
            public readonly CompletionPump Pump = new CompletionPump();
            public readonly HandleFactory Factory;
            public Env() { Factory = new HandleFactory(Pump); }
        }

        private sealed class Step : IRuntimeTick
        {
            private readonly string name;
            private readonly List<string> log;
            private readonly Action extra;
            public Step(string name, List<string> log, Action extra) { this.name = name; this.log = log; this.extra = extra; }
            public void Tick(double simulationSeconds, double unpausedSeconds)
            {
                log.Add(name);
                if (extra != null) extra();
            }
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

        private static void Before(List<string> log, string first, string second, string message)
        {
            var i = log.IndexOf(first);
            var j = log.IndexOf(second);
            True(i >= 0 && j > i, message + " [" + J(log) + "]");
        }

        private static LeaseRequest Req(string owner, int priority, long reservedAt, long sequence, params string[] resources)
        {
            return new LeaseRequest(owner, resources, priority, reservedAt, sequence);
        }

        private static IHandleController<int> AsyncChild(Env e, string id, List<string> log)
        {
            var c = e.Factory.Create<int>(id);
            c.Start();
            c.SetCancellationHandler(reason => log.Add(id + ".cancelAsked"));
            return c;
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

        private static void PumpOrderingDedupAndIsolation()
        {
            var p = new CompletionPump();
            var log = new List<string>();
            p.Post(() => log.Add("a"));
            p.Post(new CompletionKey("k"), () => log.Add("k1"));
            p.Post(new CompletionKey("k"), () => log.Add("k2"));
            p.Post(() => log.Add("a"));
            p.Post(() => log.Add("b"));
            Eq(0, log.Count, "post never runs inline");
            Eq(4, p.PendingCount, "keyed duplicate dropped");
            Eq(1, p.DeduplicatedCount, "dedup counted");
            p.AdvanceTick();
            Eq("a,k1,a,b", J(log), "FIFO with unkeyed duplicates kept");

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
            var e = new Env();
            var svc = new LeaseService(e.Factory);
            var a = svc.TryAcquire(Req("npc.a", 10, 0, 0, "b", "a"));
            True(a.Succeeded, "first lease granted");
            Eq("a,b", J(a.Value.Resources), "resources canonical sorted");
            var b = svc.TryAcquire(Req("npc.b", 10, 0, 1, "c", "b"));
            True(!b.Succeeded, "overlapping lease refused");
            Eq(ResultCodes.ResourceBusy, b.Result.Code, "busy code");
            Eq(1, b.Result.Details.Count, "one diagnostic per busy resource");
            True(svc.HolderOf("c") == null, "no partial acquisition");
            Eq(2, svc.HeldResourceCount, "only the first lease holds resources");

            var hi = svc.Enqueue(Req("npc.hi", CandidatePriority.Resolution, 0, 2, "a"));
            Eq(OperationStatus.Running, hi.Status, "waiter stays pending");
            Eq(a.Value.LeaseId, svc.HolderOf("a"), "active holder not preempted");
            Eq(1, svc.QueuedCount, "waiter queued");

            svc.Release(a.Value.LeaseId);
            Eq(OperationStatus.Succeeded, hi.Status, "waiter granted on release");
            Eq(hi.Value.LeaseId, svc.HolderOf("a"), "waiter holds a");
            Eq(1, svc.HeldResourceCount, "b released");
            svc.Release(a.Value.LeaseId);
            Eq(1, svc.HeldResourceCount, "repeated release idempotent");
            svc.Release("unknown-lease");
            svc.Release(hi.Value.LeaseId);
            Eq(0, svc.ActiveLeaseCount, "no active leases");
            Eq(0, svc.HeldResourceCount, "no leaked resources");
            Eq(0, svc.QueuedCount, "no queued waiters");
            e.Pump.AdvanceTick();
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void LeaseQueueOrderAndCancellation()
        {
            var e = new Env();
            var svc = new LeaseService(e.Factory);
            var holder = svc.TryAcquire(Req("holder", 0, 0, 0, "x")).Value;
            var low = svc.Enqueue(Req("low", 100, 5, 0, "x"));
            var late = svc.Enqueue(Req("hi-late", 300, 9, 1, "x"));
            var early = svc.Enqueue(Req("hi-early", 300, 2, 2, "x"));
            var gone = svc.Enqueue(Req("gone", 200, 0, 3, "x"));
            Eq("hi-early,hi-late,gone,low", J(svc.QueuedOwners()), "priority desc, reservedAt, sequence");
            gone.Cancel("changed mind");
            Eq(OperationStatus.Cancelled, gone.Status, "queued request cancelled");
            Eq("hi-early,hi-late,low", J(svc.QueuedOwners()), "cancel removes request");

            svc.Release(holder.LeaseId);
            Eq(OperationStatus.Succeeded, early.Status, "earliest high priority first");
            Eq(OperationStatus.Running, late.Status, "next waits");
            Eq(OperationStatus.Running, low.Status, "last waits");
            svc.Release(early.Value.LeaseId);
            Eq(OperationStatus.Succeeded, late.Status, "second");
            Eq(OperationStatus.Running, low.Status, "last still waits");
            svc.Release(late.Value.LeaseId);
            Eq(OperationStatus.Succeeded, low.Status, "third");
            svc.Release(low.Value.LeaseId);
            svc.Release(low.Value.LeaseId);
            Eq(0, svc.ActiveLeaseCount, "leases released");
            Eq(0, svc.HeldResourceCount, "resources released");
            Eq(0, svc.QueuedCount, "queue empty");
            e.Pump.AdvanceTick();
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void LeaseQueueDoesNotAllowOvertaking()
        {
            var e = new Env();
            var svc = new LeaseService(e.Factory);
            var holder = svc.TryAcquire(Req("holder", 0, 0, 0, "x")).Value;
            var big = svc.Enqueue(Req("big", 300, 0, 1, "x", "y"));
            var small = svc.Enqueue(Req("small", 100, 0, 2, "y"));
            Eq(OperationStatus.Running, small.Status, "lower waiter cannot overtake on overlapping resource");
            True(svc.HolderOf("y") == null, "y stays free but reserved by queue order");
            svc.Release(holder.LeaseId);
            Eq(OperationStatus.Succeeded, big.Status, "high waiter granted whole set");
            Eq(OperationStatus.Running, small.Status, "small still waits");
            svc.Release(big.Value.LeaseId);
            Eq(OperationStatus.Succeeded, small.Status, "small granted afterwards");
            svc.Release(small.Value.LeaseId);
            Eq(0, svc.HeldResourceCount, "no leaks");

            var svc2 = new LeaseService(e.Factory);
            var holder2 = svc2.TryAcquire(Req("holder", 0, 0, 0, "x")).Value;
            var big2 = svc2.Enqueue(Req("big", 300, 0, 1, "x", "y"));
            var small2 = svc2.Enqueue(Req("small", 100, 0, 2, "y"));
            big2.Cancel("gone");
            Eq(OperationStatus.Succeeded, small2.Status, "cancelling the blocker unblocks the next waiter");
            svc2.Release(small2.Value.LeaseId);
            svc2.Release(holder2.LeaseId);
            Eq(0, svc2.HeldResourceCount, "no leaks");
            Eq(0, svc2.QueuedCount, "queue empty");
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

        private static void TickClockSeparatesSimulationFromUnpaused()
        {
            var e = new Env();
            var clock = new TickClock(e.Factory);
            IRuntimeTick rt = clock;
            ITickClock c = clock;
            var delay = c.Delay(2.0, "scene.1");
            var watchdog = new Watchdog(c, 1.0);
            Eq(OperationStatus.Running, delay.Status, "delay running");
            rt.Tick(0, 0.5);
            Eq(1L, c.Tick, "tick counted");
            Eq(0.0, c.SimulationSeconds, "simulation stops while paused");
            Eq(0.5, c.UnpausedSeconds, "unpaused clock keeps running");
            True(!watchdog.Expired, "not yet expired");
            rt.Tick(0, 0.6);
            True(watchdog.Expired, "watchdog expires while the simulation is paused");
            Eq(OperationStatus.Running, delay.Status, "simulated delay unaffected by unpaused time");
            rt.Tick(1.0, 1.0);
            Eq(OperationStatus.Running, delay.Status, "delay not due yet");
            rt.Tick(1.0, 1.0);
            Eq(OperationStatus.Succeeded, delay.Status, "delay completes from simulation delta");
            Eq(0, clock.PendingDelayCount, "no pending delays");
            Throws<ArgumentOutOfRangeException>(() => rt.Tick(-1, 0), "negative delta");
            Throws<ArgumentOutOfRangeException>(() => rt.Tick(0, double.NaN), "NaN delta");
            Eq(4L, clock.TickCount, "rejected ticks do not advance");
            e.Pump.AdvanceTick();
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void DelaysCompleteInDeterministicOrder()
        {
            var e = new Env();
            var clock = new TickClock(e.Factory);
            IRuntimeTick rt = clock;
            var log = new List<string>();
            var d1 = clock.Delay(1.0, "a");
            var d2 = clock.Delay(1.0, "b");
            var d3 = clock.Delay(0.5, "c");
            d1.OnTerminal(r => log.Add("a"));
            d2.OnTerminal(r => log.Add("b"));
            d3.OnTerminal(r => log.Add("c"));
            rt.Tick(1.0, 1.0);
            Eq(0, log.Count, "callbacks are next tick");
            e.Pump.AdvanceTick();
            Eq("c,a,b", J(log), "due time then creation order");

            var d4 = clock.Delay(5.0, "o");
            var d5 = clock.Delay(6.0, "o");
            var d6 = clock.Delay(7.0, "p");
            Eq(2, clock.CancelOwner("o"), "owner delays cancelled");
            Eq(OperationStatus.Cancelled, d4.Status, "d4 cancelled");
            Eq(OperationStatus.Cancelled, d5.Status, "d5 cancelled");
            Eq(1, clock.PendingDelayCount, "other owner untouched");
            d6.Cancel("done");
            Eq(0, clock.PendingDelayCount, "cancel removes pending delay");
            e.Pump.AdvanceTick();
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void TickDriverOrder()
        {
            var pump = new CompletionPump();
            var log = new List<string>();
            var a = new Step("a", log, () => pump.Post(() => log.Add("cb")));
            var b = new Step("b", log, null);
            var driver = new TickDriver(new IRuntimeTick[] { a, b }, pump);
            driver.Tick(1, 1);
            Eq("a,b", J(log), "steps in supplied order, posted callback not yet run");
            driver.Tick(1, 1);
            Eq("a,b,cb,a,b", J(log), "pump drains first, completions are next tick");

            var reenter = true;
            TickDriver d = null;
            var r = new Step("r", new List<string>(), () => { if (reenter) d.Tick(0, 0); });
            d = new TickDriver(new IRuntimeTick[] { r }, pump);
            Throws<InvalidOperationException>(() => d.Tick(1, 1), "re-entrant tick");
            reenter = false;
            d.Tick(1, 1);
        }

        // ------------------------------------------------------------ cancellation scope

        private static void ScopeCancelOrdering()
        {
            var e = new Env();
            var log = new List<string>();
            var parent = e.Factory.Create<string>("parent");
            parent.OnTerminal(r => log.Add("parent:" + r.Status));
            var scope = new CancellationScope<string>(parent);
            var c1 = scope.AddChild(AsyncChild(e, "c1", log));
            var c2 = scope.AddChild(AsyncChild(e, "c2", log));
            Action<string> cleanup = name => log.Add(name + ":children=" + (c1.Status.IsTerminal() && c2.Status.IsTerminal()) + ":parentOpen=" + (parent.Terminal == null));
            scope.AddCleanup(() => cleanup("A"));
            scope.AddCleanup(() => cleanup("B"));
            Eq(OperationStatus.Running, parent.Status, "scope starts the parent");
            Eq(2, scope.OutstandingChildren, "two children tracked");

            parent.Cancel("stop");
            parent.Cancel("stop");
            True(parent.Terminal == null, "no premature parent terminal");
            Eq(ScopePhase.Draining, scope.Phase, "draining");
            Eq(1, log.FindAll(s => s == "c1.cancelAsked").Count, "child 1 asked once");
            Eq(1, log.FindAll(s => s == "c2.cancelAsked").Count, "child 2 asked once");

            c1.Cancelled("c1 cleaned", 0);
            e.Pump.AdvanceTick();
            True(parent.Terminal == null, "parent waits for the last child");
            Eq(1, scope.OutstandingChildren, "one outstanding");
            c2.Cancelled("c2 cleaned", 0);
            e.Pump.AdvanceTick();
            Eq(OperationStatus.Cancelled, parent.Status, "parent cancelled after children");
            Eq(ScopePhase.Closed, scope.Phase, "closed");
            Before(log, "B:children=True:parentOpen=True", "A:children=True:parentOpen=True", "cleanup runs LIFO after children, before parent terminal");
            e.Pump.AdvanceTick();
            True(log.Contains("parent:Cancelled"), "parent callback delivered next tick");
            Eq(0, scope.PendingCleanupCount, "cleanups consumed");
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void ScopeFailedChildCancelsSiblings()
        {
            var e = new Env();
            var log = new List<string>();
            var parent = e.Factory.Create<int>("parent");
            var scope = new CancellationScope<int>(parent);
            var c1 = scope.AddChild(AsyncChild(e, "c1", log));
            var c2 = scope.AddChild(AsyncChild(e, "c2", log));
            var c3 = scope.AddChild(AsyncChild(e, "c3", log));
            scope.AddCleanup(() => log.Add("cleanup"));
            c1.Fail("BOOM", 4, null);
            e.Pump.AdvanceTick();
            True(log.Contains("c2.cancelAsked") && log.Contains("c3.cancelAsked"), "siblings cancelled");
            True(!log.Contains("c1.cancelAsked"), "failed child not cancelled");
            True(parent.Terminal == null, "parent waits for siblings to finish cleanup");
            c2.Cancelled("c2 cleaned", 0);
            c3.Cancelled("c3 cleaned", 0);
            e.Pump.AdvanceTick();
            Eq(OperationStatus.Failed, parent.Status, "parent failed");
            Eq("BOOM", parent.Terminal.Code, "child failure code propagated");
            Eq(4L, parent.Terminal.Revision, "child revision propagated");
            True(log.Contains("cleanup"), "cleanup ran");
            Eq(0, e.Factory.LiveCount, "no live handles");
        }

        private static void ScopeSuccessAndUpgrade()
        {
            var e = new Env();
            var log = new List<string>();

            var parent = e.Factory.Create<int>("p1");
            var scope = new CancellationScope<int>(parent);
            var c1 = scope.AddChild(AsyncChild(e, "c1", log));
            var c2 = scope.AddChild(AsyncChild(e, "c2", log));
            scope.Complete(42, 9);
            scope.Complete(43, 10);
            True(parent.Terminal == null, "success waits for children");
            Eq(ScopePhase.Draining, scope.Phase, "draining");
            c1.Succeed(1, 0);
            c2.Succeed(2, 0);
            e.Pump.AdvanceTick();
            Eq(OperationStatus.Succeeded, parent.Status, "parent succeeded");
            Eq(42, parent.Value, "first completion wins");
            Eq(9L, parent.Terminal.Revision, "revision kept");

            var parent2 = e.Factory.Create<int>("p2");
            var scope2 = new CancellationScope<int>(parent2);
            scope2.Complete(5, 1);
            Eq(OperationStatus.Succeeded, parent2.Status, "no children finalizes immediately");

            var parent3 = e.Factory.Create<int>("p3");
            var scope3 = new CancellationScope<int>(parent3);
            var child = scope3.AddChild(AsyncChild(e, "c3", log));
            scope3.Complete(1, 1);
            child.Fail("BAD", 2, null);
            e.Pump.AdvanceTick();
            Eq(OperationStatus.Failed, parent3.Status, "pending success upgraded by child failure");
            Eq("BAD", parent3.Terminal.Code, "failure code");

            var parent4 = e.Factory.Create<int>("p4");
            var scope4 = new CancellationScope<int>(parent4);
            scope4.AddCleanup(() => { throw new InvalidOperationException("cleanup broke"); });
            scope4.Complete(1, 1);
            Eq(OperationStatus.Failed, parent4.Status, "throwing cleanup turns success into failure");
            Eq(ResultCodes.InvariantViolation, parent4.Terminal.Code, "cleanup failure code");
            Eq(1, scope4.CleanupFaultCount, "fault counted");

            e.Pump.AdvanceTick();
            Eq(0, e.Factory.LiveCount, "no live handles");
        }
    }
}
