using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Timekeeping;

namespace MagicCampus.Tests.Time
{
    internal static class ClockServiceSpecs
    {
        public static void Run()
        {
            BasicAdvance();
            FractionAccumulation();
            PausePolicies();
            MidnightRecordsBoundary();
            ZeroAndInvalidDelta();
            InvariantsWatchTheTick();
            ReloadLikeDayJobs();
            EventCostSuccessOnceAndCancelledNone();
            InvalidPlanCost();
        }

        private static void BasicAdvance()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var advance = w.Clock.Advance(60, PauseSet.None);
            Check.Equal(ClockAdvanceStatus.Advanced, w.Clock.LastStatus, "advanced status");
            Check.True(w.Clock.LastFailure == null, "no failure");
            Check.Equal(480L, w.Minute, "one simulation second is one game minute");
            Check.Equal(420L, advance.From.AbsoluteMinute, "advance from");
            Check.Equal(480L, advance.To.AbsoluteMinute, "advance to");
            Check.Equal(480L, w.Clock.Now.AbsoluteMinute, "Now reads the store");
            Check.True(!w.Pending, "no midnight, no return pending");
            Check.Equal(0, w.Jobs.Count, "no jobs without a boundary");
        }

        private static void FractionAccumulation()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            for (var i = 0; i < 60; i++) w.Clock.Advance(1.0 / 60.0, PauseSet.None);
            Check.Equal(421L, w.Minute, "sixty frames of 1/60 make one game minute");
            Check.True(w.Fraction < 1e-9, "no leftover fraction");
            Check.Equal(ClockAdvanceStatus.Advanced, w.Clock.LastStatus, "still advancing");
        }

        private static void PausePolicies()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var before = w.Store.Current;
            var menu = w.Clock.Advance(30, PauseSet.All);
            Check.Equal(ClockAdvanceStatus.Paused, w.Clock.LastStatus, "menu pause status");
            Check.True(menu.From.Equals(menu.To), "paused advance is identity");
            w.AssertUnchanged(before, "menu pause");

            w.Clock.Advance(30, new PauseSet(true, false, false, false, false));
            Check.Equal(ClockAdvanceStatus.Paused, w.Clock.LastStatus, "calendar-only pause");
            w.AssertUnchanged(before, "calendar-only pause");

            w.Clock.Advance(10, new PauseSet(false, true, true, true, true));
            Check.Equal(ClockAdvanceStatus.Advanced, w.Clock.LastStatus, "other channels paused do not stop the calendar");
            Check.Equal(430L, w.Minute, "advanced while other channels are paused");
        }

        private static void MidnightRecordsBoundary()
        {
            var w = TimeWorld.Create(1430, 0, new[] { "legacy" });
            var before = w.Store.Current;
            var advance = w.Clock.Advance(20, PauseSet.None);
            Check.Equal(ClockAdvanceStatus.Advanced, w.Clock.LastStatus, "midnight advance status");
            Check.Equal(1, advance.Boundaries.Count, "reported boundary");
            Check.Equal(1450L, w.Minute, "minute after midnight");
            Check.True(w.Pending, "return pending set");
            Check.Equal("legacy|boundary.day_d1", string.Join("|", w.Jobs), "dayJobs keep old entries and add the boundary key");
            Check.Equal(2, w.Store.Current.Slice("clock").Members.Count, "clock keeps exactly its two members");
            foreach (var field in TimeWorld.UntouchedFields)
                Check.True(before.Slice(field).Equals(w.Store.Current.Slice(field)), "field " + field + " untouched");
            Check.Equal(DataKind.Integer, w.Store.Current.Slice("clock").Members["absoluteMinute"].Kind, "minute stays integer");
        }

        private static void ZeroAndInvalidDelta()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var before = w.Store.Current;
            w.Clock.Advance(0, PauseSet.None);
            Check.Equal(ClockAdvanceStatus.ZeroDelta, w.Clock.LastStatus, "zero delta status");
            w.AssertUnchanged(before, "zero delta");

            var bad = new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, CalendarLimits.MaxMinutesPerAdvance + 1 };
            foreach (var delta in bad)
            {
                w.Clock.Advance(delta, PauseSet.None);
                Check.Equal(ClockAdvanceStatus.Failed, w.Clock.LastStatus, "failed status for " + delta);
                Check.True(w.Clock.LastFailure != null, "failure exposed for " + delta);
                Check.Equal(ResultCodes.ContentInvalid, w.Clock.LastFailure.Code, "failure code for " + delta);
                w.AssertUnchanged(before, "invalid delta " + delta);
            }

            w.Clock.Advance(1, PauseSet.None);
            Check.True(w.Clock.LastFailure == null, "failure cleared by a later success");
            Check.Equal(421L, w.Minute, "valid advance after failures");
        }

        private static void InvariantsWatchTheTick()
        {
            var w = TimeWorld.Create(TimeWorld.BuildRoot(1430, 0, new string[0], false), 1, new IStateInvariant[] { new NoReturnPending() });
            var before = w.Store.Current;
            var refused = w.Clock.Advance(20, PauseSet.None);
            Check.Equal(ClockAdvanceStatus.Failed, w.Clock.LastStatus, "midnight tick refused by the commit invariant");
            Check.Equal(ResultCodes.InvariantViolation, w.Clock.LastFailure.Code, "invariant refusal code");
            Check.True(refused.From.Equals(refused.To), "refused tick reports no progress");
            w.AssertUnchanged(before, "refused tick");

            w.Clock.Advance(5, PauseSet.None);
            Check.Equal(ClockAdvanceStatus.Advanced, w.Clock.LastStatus, "tick the invariant accepts");
            Check.Equal(1435L, w.Minute, "accepted tick applied");
        }

        private sealed class NoReturnPending : IStateInvariant
        {
            public IReadOnlyList<Diagnostic> Validate(StateSnapshot candidate)
            {
                return candidate.Slice("returnPending").Bool ? new[] { new Diagnostic("TEST_RETURN_PENDING", "returnPending refused by the test invariant", null, "false", "true") } : new Diagnostic[0];
            }
        }

        private static void ReloadLikeDayJobs()
        {
            var done = TimeWorld.Create(1430, 0, new[] { "legacy", "boundary.day_d1" });
            done.Clock.Advance(20, PauseSet.None);
            Check.Equal(ClockAdvanceStatus.Advanced, done.Clock.LastStatus, "advance with recorded jobs");
            Check.Equal("legacy|boundary.day_d1", string.Join("|", done.Jobs), "no duplicate keys");
            Check.True(done.Pending, "return pending still derived");

            // Rebuild a world from the committed state (reload-like): same day adds nothing, next midnight adds day 2 only.
            var reloaded = TimeWorld.Create(done.Store.Current.Root, 7);
            reloaded.Clock.Advance(100, PauseSet.None);
            Check.Equal("legacy|boundary.day_d1", string.Join("|", reloaded.Jobs), "jobs unchanged within the day");
            reloaded.Clock.Advance(1440, PauseSet.None);
            Check.Equal("legacy|boundary.day_d1|boundary.day_d2", string.Join("|", reloaded.Jobs), "day 2 appended once");
        }

        private static void EventCostSuccessOnceAndCancelledNone()
        {
            var w = TimeWorld.Create(1400, 0, new string[0]);
            var start = w.Store.Current;
            var cost = w.Clock.PlanCost(start, 90);
            Check.True(cost.Succeeded, "cost planned");
            Check.Equal(1, cost.Value.Changes.Count, "one clock change");
            Check.Equal("clock", cost.Value.Changes[0].ParticipantId, "clock change");

            // Cancelled event: the plan is dropped, nothing advanced.
            w.AssertUnchanged(start, "cancelled event cost");
            Check.Equal(1400L, w.Minute, "cancelled cost leaves time");
            Check.True(!w.Pending, "cancelled cost leaves returnPending");

            // Successful event: one StatePlan carries the event change and the clock change.
            var changes = new List<PlannedChange> { Nodes.Mark("class_done") };
            changes.AddRange(cost.Value.Changes);
            var plan = new StatePlan(new TransactionId("evt.class.1"), null, start.Revision, changes);
            var first = w.Store.Commit(plan);
            Check.True(first.Succeeded, "event commit succeeds");
            Check.Equal(1490L, w.Minute, "cost applied once");
            Check.True(w.TypedFlag("class_done"), "event change in the same commit");
            Check.Equal("boundary.day_d1", string.Join("|", w.Jobs), "boundary jobs recorded");
            Check.True(w.Pending, "return pending set by the cost");
            var committedRevision = w.Store.Current.Revision;
            Check.Equal(first.Value.Revision, committedRevision, "commit revision");

            // Repeating the same transaction never applies the cost again.
            w.Store.Commit(plan);
            Check.Equal(1490L, w.Minute, "repeated transaction does not double the cost");
            Check.Equal(committedRevision, w.Store.Current.Revision, "repeated transaction keeps the revision");

            // A stale plan under a new transaction id is refused.
            var stale = w.Store.Commit(new StatePlan(new TransactionId("evt.class.2"), null, start.Revision, changes));
            Check.True(!stale.Succeeded, "stale plan refused");
            Check.Equal(ResultCodes.RevisionConflict, stale.Result.Code, "stale plan code");
            Check.Equal(1490L, w.Minute, "stale plan does not change time");
        }

        private static void InvalidPlanCost()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var start = w.Store.Current;
            foreach (var minutes in new[] { -1.0, double.NaN, double.PositiveInfinity, 1e300 })
            {
                var cost = w.Clock.PlanCost(start, minutes);
                Check.True(!cost.Succeeded, "cost refused for " + minutes);
                Check.Equal(ResultCodes.ContentInvalid, cost.Result.Code, "cost code for " + minutes);
                Check.True(cost.Result.Details.Count > 0, "cost diagnostics for " + minutes);
                Check.True(cost.Value == null, "no partial change set for " + minutes);
            }

            var zero = w.Clock.PlanCost(start, 0);
            Check.True(zero.Succeeded && zero.Value.Changes.Count == 0, "zero cost plans nothing");

            var malformed = new StateSnapshot(1, Nodes.Obj(Nodes.P("dayJobs", Nodes.List())));
            var broken = w.Clock.PlanCost(malformed, 5);
            Check.True(!broken.Succeeded, "malformed basis refused");
            Check.Equal(ResultCodes.ContentInvalid, broken.Result.Code, "malformed basis code");
            w.AssertUnchanged(start, "invalid costs");
        }
    }
}
