using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    public enum ClockAdvanceStatus { None, Advanced, Paused, ZeroDelta, Failed }

    /// <summary>
    /// Reads the current time from the state store and advances it only by committing a clock.advance plan.
    /// Event time costs are only planned by PlanCost; the caller commits them together with the event's other changes.
    /// IClockService.Advance has no failure channel, so every call records LastStatus/LastResult and LastFailure
    /// (LastFailure is null unless the most recent call failed).
    /// </summary>
    public sealed class ClockService : IClockService, IClockCostPlanner
    {
        private readonly IStateStore store;
        private readonly ICalendar calendar;
        private readonly DayBoundaryConsumerRegistry consumers;
        private long sequence;

        public ClockService(IStateStore store, ICalendar calendar, DayBoundaryConsumerRegistry consumers)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
            this.consumers = consumers ?? throw new ArgumentNullException(nameof(consumers));
        }

        public ClockAdvanceStatus LastStatus { get; private set; }
        public Result LastResult { get; private set; }
        public Result LastFailure { get; private set; }

        public GameTime Now => calendar.FromSnapshot(store.Current);

        public ClockAdvance Advance(double simulationSeconds, IPausePolicy pause)
        {
            if (pause == null) throw new ArgumentNullException(nameof(pause));
            var basis = store.Current;

            if (!ClockStateReader.TryRead(basis, out var state, out var readProblems))
                return Reject(default(GameTime), basis.Revision, ResultCodes.ContentInvalid, readProblems);

            var now = state.Time;
            if (pause.Calendar) return Idle(now, basis.Revision, ClockAdvanceStatus.Paused);

            // 1 simulation-second = 1 game-minute
            var minutes = simulationSeconds;
            if (!Calendar.TryCheckMinutes(minutes, out var minutesProblem))
                return Reject(now, basis.Revision, ResultCodes.ContentInvalid, new[] { minutesProblem });
            if (minutes == 0) return Idle(now, basis.Revision, ClockAdvanceStatus.ZeroDelta);

            if (!TryBuild(basis, state, minutes, out var advance, out var changes, out var problems))
                return Reject(now, basis.Revision, ResultCodes.ContentInvalid, problems);
            if (changes.Count == 0) return Idle(now, basis.Revision, ClockAdvanceStatus.ZeroDelta);

            sequence++;
            var transaction = new TransactionId("clock.advance.r" + basis.Revision.ToString(CultureInfo.InvariantCulture) + ".n" + sequence.ToString(CultureInfo.InvariantCulture));
            var outcome = store.Commit(new StatePlan(transaction, null, basis.Revision, changes));
            if (!outcome.Succeeded) return Rejected(now, outcome.Result);

            if (!ClockStateReader.TryRead(outcome.Value, out var after, out var afterProblems))
                return Reject(now, outcome.Result.Revision, ResultCodes.InvariantViolation, afterProblems);

            if (!after.Time.Equals(advance.To))
            {
                var mismatch = TimeDiagnostics.Make("CLOCK_COMMIT_MISMATCH", ClockParticipant.ClockPointer, "committed time differs from the planned time",
                    advance.To.AbsoluteMinute.ToString(CultureInfo.InvariantCulture), after.Time.AbsoluteMinute.ToString(CultureInfo.InvariantCulture));
                var failure = new Result(OperationStatus.Failed, ResultCodes.InvariantViolation, outcome.Result.Revision, new[] { mismatch });
                LastStatus = ClockAdvanceStatus.Failed;
                LastResult = failure;
                LastFailure = failure;
                return new ClockAdvance(now, after.Time, advance.Boundaries);
            }

            LastStatus = ClockAdvanceStatus.Advanced;
            LastResult = outcome.Result;
            LastFailure = null;
            return new ClockAdvance(now, after.Time, advance.Boundaries);
        }

        /// <summary>
        /// Plans the clock change plus every registered consumer's change for each newly crossed midnight.
        /// Nothing is applied; a plan that is never committed advances nothing.
        /// </summary>
        public Outcome<ChangeSet> PlanCost(StateSnapshot basis, double minutes)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (!ClockStateReader.TryRead(basis, out var state, out var readProblems))
                return Outcome<ChangeSet>.Fail(ResultCodes.ContentInvalid, basis.Revision, TimeDiagnostics.ToArray(readProblems));
            if (!Calendar.TryCheckMinutes(minutes, out var minutesProblem))
                return Outcome<ChangeSet>.Fail(ResultCodes.ContentInvalid, basis.Revision, minutesProblem);
            if (!TryBuild(basis, state, minutes, out _, out var changes, out var problems))
                return Outcome<ChangeSet>.Fail(ResultCodes.ContentInvalid, basis.Revision, problems);
            return Outcome<ChangeSet>.Ok(new ChangeSet(changes), basis.Revision);
        }

        private bool TryBuild(StateSnapshot basis, ClockState state, double minutes, out ClockAdvance advance, out List<PlannedChange> changes, out Diagnostic[] problems)
        {
            changes = new List<PlannedChange>();
            problems = new Diagnostic[0];
            advance = null;
            try
            {
                advance = calendar.Plan(state.Time, minutes);
            }
            catch (ArgumentException ex)
            {
                problems = new[] { TimeDiagnostics.Make("CLOCK_MINUTES_INVALID", string.Empty, ex.Message, "plannable minutes", minutes.ToString("R", CultureInfo.InvariantCulture)) };
                return false;
            }

            if (advance.To.Equals(advance.From)) return true;

            var done = new HashSet<string>(state.DayJobs, StringComparer.Ordinal);
            if (!BoundaryJobs.TryCollect(consumers, basis, done, advance.Boundaries, out var work, out var workProblems))
            {
                problems = workProblems.ToArray();
                return false;
            }

            changes.Add(ClockParticipant.CreateAdvanceChange(minutes));
            foreach (var item in work) changes.Add(item.Change);
            return true;
        }

        private ClockAdvance Reject(GameTime now, long revision, string code, IReadOnlyList<Diagnostic> details)
        {
            return Rejected(now, new Result(OperationStatus.Failed, code, revision, details));
        }

        private ClockAdvance Rejected(GameTime now, Result failure)
        {
            LastStatus = ClockAdvanceStatus.Failed;
            LastResult = failure;
            LastFailure = failure;
            return new ClockAdvance(now, now, null);
        }

        private ClockAdvance Idle(GameTime now, long revision, ClockAdvanceStatus status)
        {
            LastStatus = status;
            LastResult = new Result(OperationStatus.Succeeded, ResultCodes.Ok, revision, null);
            LastFailure = null;
            return new ClockAdvance(now, now, null);
        }
    }
}
