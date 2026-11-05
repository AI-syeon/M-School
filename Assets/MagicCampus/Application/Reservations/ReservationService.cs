using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;

namespace MagicCampus.Application.Reservations
{
    /// <summary>Candidates plus the explicit diagnostics for rows that could not be used.</summary>
    public sealed class ReservationInspection
    {
        public IReadOnlyList<StartCandidate> Candidates { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public ReservationInspection(IEnumerable<StartCandidate> candidates, IEnumerable<Diagnostic> diagnostics)
        {
            Candidates = new List<StartCandidate>(candidates ?? throw new ArgumentNullException(nameof(candidates))).AsReadOnly();
            Diagnostics = new List<Diagnostic>(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).AsReadOnly();
        }
    }

    public sealed class ReservationService
    {
        private readonly ICatalogProvider catalog;
        private readonly ICalendar calendar;
        private readonly ICompletionLedger ledger;

        public ReservationService(ICatalogProvider catalog, ICalendar calendar, ICompletionLedger ledger)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        /// <summary>Ordered by priority descending, ReservedAt, Sequence, EventId (ordinal). Accepted reservations stay candidates after a failed start, a missing lease or a failed move; only a successful completion commit removes them.</summary>
        public IReadOnlyList<StartCandidate> Candidates(StateSnapshot snapshot)
        {
            return Inspect(snapshot).Candidates;
        }

        /// <summary>Same ordering as Candidates; additionally reports invalid rows and rows whose event is unknown.</summary>
        public ReservationInspection Inspect(StateSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var diagnostics = new List<Diagnostic>();
            var list = new List<StartCandidate>();
            if (!snapshot.TryTypedState(out var state, out var refusal))
            {
                diagnostics.Add(refusal);
                return new ReservationInspection(list, diagnostics);
            }
            var current = catalog.Current;

            var rows = state.Appointments.Rows;
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.Status == ReservationStatus.Offered) continue;
                var ev = current.FindEvent(row.Id);
                if (ev == null)
                {
                    diagnostics.Add(ReservationRules.Diag(ResultCodes.RefNotFound, "appointment refers to an event that is not in the catalog",
                        AppointmentCodec.Pointer + "/" + index + "/id", row.Id, "known event", row.Id));
                    continue;
                }
                if (ReservationRules.IsCompleted(ev, snapshot, ledger)) continue;
                list.Add(new StartCandidate(row.Id, row.Id, row.Status, ReservationRules.Priority(ev), row.AcceptedAt, index));
            }

            list.Sort(Compare);
            return new ReservationInspection(list, diagnostics);
        }

        public Outcome<PlannedChange> PlanAccept(string reservationId, StateSnapshot basis)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (!AppointmentCodec.IsValidId(reservationId))
                return Outcome<PlannedChange>.Fail(ResultCodes.ContentInvalid, basis.Revision,
                    ReservationRules.Diag(ResultCodes.ContentInvalid, "reservation id is not a valid event id", AppointmentCodec.Pointer, reservationId, "dotted lowercase event id", reservationId ?? "null"));

            if (!basis.TryTypedState(out var state, out var refusal))
                return Outcome<PlannedChange>.Fail(ResultCodes.ContentInvalid, basis.Revision, refusal);

            var ev = catalog.Current.FindEvent(reservationId);
            if (ev == null)
                return Outcome<PlannedChange>.Fail(ResultCodes.RefNotFound, basis.Revision,
                    ReservationRules.Diag(ResultCodes.RefNotFound, "event is not in the catalog", AppointmentCodec.Pointer, reservationId, "known event", reservationId));

            var existing = state.Appointments.Find(reservationId);
            long acceptedAt;
            if (existing != null && existing.Status != ReservationStatus.Offered)
            {
                acceptedAt = existing.AcceptedAt;
            }
            else
            {
                if (ReservationRules.IsCompleted(ev, basis, ledger))
                    return Outcome<PlannedChange>.Fail(ResultCodes.ConditionFalse, basis.Revision,
                        ReservationRules.Diag(ResultCodes.ConditionFalse, "the shared completion key is already completed", AppointmentCodec.Pointer, reservationId, "not completed", ev.OnceKey.Value));
                acceptedAt = calendar.FromSnapshot(basis).AbsoluteMinute;
            }

            var payload = DataNode.FromObject(new[]
            {
                new KeyValuePair<string, DataNode>("eventId", DataNode.FromString(reservationId)),
                new KeyValuePair<string, DataNode>("acceptedAt", DataNode.FromInteger(acceptedAt))
            });
            return Outcome<PlannedChange>.Ok(new PlannedChange(ReservationParticipant.Id, ReservationParticipant.AcceptOperation, payload), basis.Revision);
        }

        private static int Compare(StartCandidate a, StartCandidate b)
        {
            var c = b.Priority.CompareTo(a.Priority);
            if (c != 0) return c;
            c = a.ReservedAt.CompareTo(b.ReservedAt);
            if (c != 0) return c;
            c = a.Sequence.CompareTo(b.Sequence);
            if (c != 0) return c;
            return string.CompareOrdinal(a.EventId, b.EventId);
        }
    }
}
