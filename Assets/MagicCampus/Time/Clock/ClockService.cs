using System;
using System.Globalization;
using MagicCampus.Contracts;
using MagicCampus.State;
using MagicCampus.Transactions;

namespace MagicCampus.Timekeeping
{
    public enum ClockAdvanceStatus { None, Advanced, Paused, ZeroDelta, Failed }

    /// <summary>
    /// Reads the current time from the state store and advances it only by committing ClockState.Advance as a store transition.
    /// Event time costs are applied by EventRules in the event success commit, through the same TryPlan.
    /// IClockService.Advance has no failure channel, so every call records LastStatus/LastResult and LastFailure
    /// (LastFailure is null unless the most recent call failed).
    /// </summary>
    public sealed class ClockService : IClockService
    {
        private readonly GameStore store;
        private readonly ICalendar calendar;
        private long sequence;

        public ClockService(GameStore store, ICalendar calendar)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        }

        public ClockAdvanceStatus LastStatus { get; private set; }
        public Result LastResult { get; private set; }
        public Result LastFailure { get; private set; }

        public GameTime Now => store.Current.State.Clock.Time;

        public ClockAdvance Advance(double simulationSeconds, IPausePolicy pause)
        {
            if (pause == null) throw new ArgumentNullException(nameof(pause));
            var basis = store.Current;
            var now = basis.State.Clock.Time;
            if (pause.Calendar) return Idle(now, basis.Revision, ClockAdvanceStatus.Paused);

            // 1 simulation-second = 1 game-minute
            var minutes = simulationSeconds;
            if (!Calendar.TryCheckMinutes(minutes, out var minutesProblem))
                return Reject(now, basis.Revision, ResultCodes.ContentInvalid, minutesProblem);
            if (minutes == 0) return Idle(now, basis.Revision, ClockAdvanceStatus.ZeroDelta);

            sequence++;
            var transaction = new TransactionId("clock.advance.r" + basis.Revision.ToString(CultureInfo.InvariantCulture) + ".n" + sequence.ToString(CultureInfo.InvariantCulture));
            ClockAdvance step = null;
            var outcome = store.Commit(transaction, s =>
            {
                if (!TryPlan(calendar, s.Clock.Time, minutes, out step, out var problem)) return Outcome<GameState>.Fail(ResultCodes.ContentInvalid, 0, problem);
                return Outcome<GameState>.Ok(step.To.Equals(step.From) ? s : s.With(s.Clock.Advance(step)), 0);
            });
            if (!outcome.Succeeded) return Rejected(now, outcome.Result);
            if (outcome.Value.Revision == basis.Revision) return Idle(now, basis.Revision, ClockAdvanceStatus.ZeroDelta);

            LastStatus = ClockAdvanceStatus.Advanced;
            LastResult = outcome.Result;
            LastFailure = null;
            return step;
        }

        internal static bool TryPlan(ICalendar calendar, GameTime time, double minutes, out ClockAdvance step, out Diagnostic problem)
        {
            problem = null;
            try
            {
                step = calendar.Plan(time, minutes);
                return true;
            }
            catch (ArgumentException ex)
            {
                step = null;
                problem = TimeDiagnostics.Make("CLOCK_MINUTES_INVALID", string.Empty, ex.Message, "plannable minutes", minutes.ToString("R", CultureInfo.InvariantCulture));
                return false;
            }
        }

        private ClockAdvance Reject(GameTime now, long revision, string code, Diagnostic detail)
        {
            return Rejected(now, new Result(OperationStatus.Failed, code, revision, new[] { detail }));
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
