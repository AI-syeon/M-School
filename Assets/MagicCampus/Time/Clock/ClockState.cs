using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    /// <summary>Validated view of the clock-owned slices of one snapshot.</summary>
    public sealed class ClockState
    {
        public ClockState(GameTime time, DataNode clock, IReadOnlyList<string> dayJobs, bool returnPending)
        {
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (dayJobs == null) throw new ArgumentNullException(nameof(dayJobs));
            Time = time;
            Clock = clock;
            DayJobs = new List<string>(dayJobs).AsReadOnly();
            ReturnPending = returnPending;
        }

        public GameTime Time { get; }
        /// <summary>The whole original /clock object, so unknown members survive a rewrite.</summary>
        public DataNode Clock { get; }
        public IReadOnlyList<string> DayJobs { get; }
        public bool ReturnPending { get; }
    }

    public static class ClockStateReader
    {
        /// <summary>Validates the full clock-owned basis: /clock object, /dayJobs list of strings, /returnPending bool.</summary>
        public static bool TryRead(StateSnapshot basis, out ClockState state, out IReadOnlyList<Diagnostic> problems)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            state = null;
            var found = new List<Diagnostic>();
            var time = default(GameTime);

            var clock = basis.Slice("clock");
            if (clock == null || clock.Kind != DataKind.Object)
                found.Add(TimeDiagnostics.Make("CLOCK_INVALID", "/clock", "/clock must be an object", "object", "missing or wrong kind"));
            else if (!Calendar.TryReadClock(clock, out time, out var clockProblem))
                found.Add(clockProblem);

            var jobs = new List<string>();
            var jobsNode = basis.Slice("dayJobs");
            if (jobsNode == null || jobsNode.Kind != DataKind.List)
            {
                found.Add(TimeDiagnostics.Make("CLOCK_INVALID", "/dayJobs", "/dayJobs must be a list of strings", "list", "missing or wrong kind"));
            }
            else
            {
                for (var i = 0; i < jobsNode.Items.Count; i++)
                {
                    var item = jobsNode.Items[i];
                    if (item.Kind != DataKind.String)
                        found.Add(TimeDiagnostics.Make("CLOCK_INVALID", "/dayJobs/" + i, "dayJobs entries must be strings", "string", item.Kind.ToString()));
                    else
                        jobs.Add(item.Text);
                }
            }

            var pending = basis.Slice("returnPending");
            if (pending == null || pending.Kind != DataKind.Bool)
                found.Add(TimeDiagnostics.Make("CLOCK_INVALID", "/returnPending", "/returnPending must be a bool", "bool", "missing or wrong kind"));

            problems = found;
            if (found.Count > 0) return false;
            state = new ClockState(time, clock, jobs, pending.Bool);
            return true;
        }
    }
}
