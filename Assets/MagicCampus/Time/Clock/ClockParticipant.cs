using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    /// <summary>
    /// State participant "clock". Owns /clock, /dayJobs and /returnPending. The only operation is advance {minutes}.
    /// Several advances in one plan are applied in order. Every crossed midnight is recorded once in dayJobs (boundary.day_dN).
    /// Prepare never writes the store.
    /// </summary>
    public sealed class ClockParticipant : IStateParticipant
    {
        public const string Id = "clock";
        public const string AdvanceOperation = "advance";
        public const string ClockPointer = "/clock";
        public const string DayJobsPointer = "/dayJobs";
        public const string ReturnPendingPointer = "/returnPending";

        private readonly ICalendar calendar;
        private readonly IReadOnlyList<string> owned = Array.AsReadOnly(new[] { ClockPointer, DayJobsPointer, ReturnPendingPointer });

        public ClockParticipant(ICalendar calendar)
        {
            this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        }

        public string ParticipantId => Id;

        public IReadOnlyList<string> OwnedSlices => owned;

        /// <summary>Builds the frozen clock.advance change {minutes}. Whole values are Integer nodes, others Number nodes.</summary>
        public static PlannedChange CreateAdvanceChange(double minutes)
        {
            DataNode value;
            if (minutes >= 0 && minutes <= 9007199254740992.0 && minutes == Math.Floor(minutes))
                value = DataNode.FromInteger((long)minutes);
            else
                value = DataNode.FromNumber(minutes);
            var payload = DataNode.FromObject(new[] { new KeyValuePair<string, DataNode>("minutes", value) });
            return new PlannedChange(Id, AdvanceOperation, payload);
        }

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            if (!ClockStateReader.TryRead(basis, out var state, out var baseProblems))
                return Refuse(ResultCodes.ContentInvalid, basis.Revision, baseProblems);

            var time = state.Time;
            var boundaries = new List<DayBoundary>();
            var advances = 0;

            foreach (var change in plan.Changes)
            {
                if (change.ParticipantId != Id) continue;

                if (change.Operation != AdvanceOperation)
                    return Refuse(ResultCodes.Unregistered, basis.Revision,
                        TimeDiagnostics.Make("CLOCK_OPERATION_UNREGISTERED", string.Empty, "clock has no operation " + change.Operation, AdvanceOperation, change.Operation));

                if (!TryReadMinutes(change.Payload, out var minutes, out var payloadProblem))
                    return Refuse(ResultCodes.ContentInvalid, basis.Revision, payloadProblem);

                ClockAdvance step;
                try
                {
                    step = calendar.Plan(time, minutes);
                }
                catch (ArgumentException ex)
                {
                    return Refuse(ResultCodes.ContentInvalid, basis.Revision,
                        TimeDiagnostics.Make("CLOCK_MINUTES_INVALID", string.Empty, ex.Message, "plannable minutes", minutes.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                }

                advances++;
                time = step.To;
                boundaries.AddRange(step.Boundaries);
            }

            if (advances == 0)
                return Outcome<StateReplacement>.Ok(new StateReplacement(new KeyValuePair<string, DataNode>[0]), basis.Revision);

            var done = new HashSet<string>(state.DayJobs, StringComparer.Ordinal);
            var jobs = new List<string>(state.DayJobs);
            foreach (var boundary in boundaries)
                if (done.Add(boundary.Key.Value)) jobs.Add(boundary.Key.Value);

            var jobNodes = new List<DataNode>();
            foreach (var job in jobs) jobNodes.Add(DataNode.FromString(job));

            var clockNode = state.Clock
                .With("absoluteMinute", DataNode.FromInteger(time.AbsoluteMinute))
                .With("fraction", DataNode.FromNumber(time.Fraction));
            var pending = state.ReturnPending || boundaries.Count > 0;

            var slices = new[]
            {
                new KeyValuePair<string, DataNode>(ClockPointer, clockNode),
                new KeyValuePair<string, DataNode>(DayJobsPointer, DataNode.FromList(jobNodes)),
                new KeyValuePair<string, DataNode>(ReturnPendingPointer, DataNode.FromBool(pending))
            };
            return Outcome<StateReplacement>.Ok(new StateReplacement(slices), basis.Revision);
        }

        private static bool TryReadMinutes(DataNode payload, out double minutes, out Diagnostic problem)
        {
            minutes = 0;
            problem = null;
            if (payload == null || payload.Kind != DataKind.Object || payload.Members.Count != 1 || !payload.TryGet("minutes", out var node))
            {
                problem = TimeDiagnostics.Make("CLOCK_PAYLOAD_INVALID", string.Empty, "clock.advance payload must be exactly {minutes}", "{minutes}", payload == null ? "null" : payload.Kind.ToString());
                return false;
            }

            if (node.Kind == DataKind.Integer) minutes = node.Integer;
            else if (node.Kind == DataKind.Number) minutes = node.Number;
            else
            {
                problem = TimeDiagnostics.Make("CLOCK_PAYLOAD_INVALID", string.Empty, "minutes must be a number", "integer or number", node.Kind.ToString());
                return false;
            }

            return Calendar.TryCheckMinutes(minutes, out problem);
        }

        private static Outcome<StateReplacement> Refuse(string code, long revision, IReadOnlyList<Diagnostic> details)
        {
            return Outcome<StateReplacement>.Fail(code, revision, TimeDiagnostics.ToArray(details));
        }

        private static Outcome<StateReplacement> Refuse(string code, long revision, Diagnostic detail)
        {
            return Outcome<StateReplacement>.Fail(code, revision, detail);
        }
    }
}
