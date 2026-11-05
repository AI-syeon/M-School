using System.Collections.Generic;
using MagicCampus.Content.Catalog;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.State;
using MagicCampus.Tests.Effects;
using MagicCampus.Tests.PureDomain.Support;
using MagicCampus.Transactions;
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
            ReloadLikeDayJobs();
            EventCostSuccessOnceAndCancelledNone();
            InvalidEventCost();
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

        private static void ReloadLikeDayJobs()
        {
            var done = TimeWorld.Create(1430, 0, new[] { "legacy", "boundary.day_d1" });
            done.Clock.Advance(20, PauseSet.None);
            Check.Equal(ClockAdvanceStatus.Advanced, done.Clock.LastStatus, "advance with recorded jobs");
            Check.Equal("legacy|boundary.day_d1", string.Join("|", done.Jobs), "no duplicate keys");
            Check.True(done.Pending, "return pending still derived");

            // Rebuild a world from the committed state (reload-like): same day adds nothing, next midnight adds day 2 only.
            var reloaded = TimeWorld.Create(done.Store.Current.Tree(), 7);
            reloaded.Clock.Advance(100, PauseSet.None);
            Check.Equal("legacy|boundary.day_d1", string.Join("|", reloaded.Jobs), "jobs unchanged within the day");
            reloaded.Clock.Advance(1440, PauseSet.None);
            Check.Equal("legacy|boundary.day_d1|boundary.day_d2", string.Join("|", reloaded.Jobs), "day 2 appended once");
        }

        private static readonly WorldPlaces Nobody = new WorldPlaces(null, new KeyValuePair<string, Position>[0]);

        private static Outcome<StateSnapshot> Complete(TimeWorld w, EventRules rules, CompiledEvent ev, string transaction)
        {
            return w.Store.Commit(new TransactionId(transaction), s => rules.Complete(s, ev, Nobody));
        }

        private static void EventCostSuccessOnceAndCancelledNone()
        {
            var w = TimeWorld.Create(1400, 0, new string[0]);
            var catalog = new FakeCatalog();
            var ev = Fx.Event("evt.class", RepeatPolicy.Once, null, 90);
            catalog.EventList.Add(ev);
            var rules = new EventRules(catalog, w.Calendar);
            var start = w.Store.Current;

            // Cancelled event: no commit, nothing advanced.
            w.AssertUnchanged(start, "cancelled event cost");
            Check.Equal(1400L, w.Minute, "cancelled cost leaves time");
            Check.True(!w.Pending, "cancelled cost leaves returnPending");

            // Successful event: one commit carries the completion and the clock change.
            var first = Complete(w, rules, ev, "evt.class.1");
            Check.True(first.Succeeded, "event commit succeeds");
            Check.Equal(1490L, w.Minute, "cost applied once");
            Check.True(w.Store.Current.State.Progress.IsConsumed(ev.OnceKey), "event completion in the same commit");
            Check.Equal("boundary.day_d1", string.Join("|", w.Jobs), "boundary jobs recorded");
            Check.True(w.Pending, "return pending set by the cost");
            var committedRevision = w.Store.Current.Revision;
            Check.Equal(start.Revision + 1, committedRevision, "one commit revision");

            // Repeating the same transaction never applies the cost again.
            Complete(w, rules, ev, "evt.class.1");
            Check.Equal(1490L, w.Minute, "repeated transaction does not double the cost");
            Check.Equal(committedRevision, w.Store.Current.Revision, "repeated transaction keeps the revision");

            // The same once event under a new transaction id is refused.
            var again = Complete(w, rules, ev, "evt.class.2");
            Check.True(!again.Succeeded, "spent event refused");
            Check.Equal(ResultCodes.ConditionFalse, again.Result.Code, "spent event code");
            Check.Equal(1490L, w.Minute, "spent event does not change time");
        }

        private static void InvalidEventCost()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var catalog = new FakeCatalog();
            var huge = Fx.Event("evt.huge", RepeatPolicy.Once, null, (int)CalendarLimits.MaxMinutesPerAdvance + 1);
            var free = Fx.Event("evt.free", RepeatPolicy.Once, null, 0);
            catalog.EventList.Add(huge);
            catalog.EventList.Add(free);
            var rules = new EventRules(catalog, w.Calendar);
            var start = w.Store.Current;

            var refused = Complete(w, rules, huge, "evt.huge.1");
            Check.True(!refused.Succeeded, "cost past the per-advance limit refused");
            Check.Equal(ResultCodes.ContentInvalid, refused.Result.Code, "cost code");
            Check.Equal("CLOCK_MINUTES_INVALID", refused.Result.Details[0].Code, "cost diagnostic");
            w.AssertUnchanged(start, "invalid cost");

            Check.True(Complete(w, rules, free, "evt.free.1").Succeeded, "zero cost commits");
            Check.Equal(420L, w.Minute, "zero cost moves no time");
            Check.True(w.Store.Current.Slice("clock").Equals(start.Slice("clock")), "zero cost leaves the clock bytes");
        }
    }
}
