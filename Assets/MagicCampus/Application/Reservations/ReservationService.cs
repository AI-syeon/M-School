using System;
using System.Collections.Generic;
using MagicCampus.Content.Catalog;
using MagicCampus.Contracts;
using MagicCampus.State;
using MagicCampus.Transactions;

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

        public ReservationService(ICatalogProvider catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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
            var state = snapshot.State;
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
                        ReservationRules.Pointer + "/" + index + "/id", row.Id, "known event", row.Id));
                    continue;
                }
                if (EventRules.IsSpent(state, ev)) continue;
                list.Add(new StartCandidate(row.Id, row.Id, row.Status, ReservationRules.Priority(ev), row.AcceptedAt, index));
            }

            list.Sort(Compare);
            return new ReservationInspection(list, diagnostics);
        }

        /// <summary>A new or Offered invitation becomes Accepted at the current minute unless its once key is consumed; an Accepted or Ready one keeps its time.</summary>
        public Outcome<GameState> Accept(GameState state, string reservationId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!ReservationRules.IsValidId(reservationId))
                return Outcome<GameState>.Fail(ResultCodes.ContentInvalid, 0,
                    ReservationRules.Diag(ResultCodes.ContentInvalid, "reservation id is not a valid event id", ReservationRules.Pointer, reservationId, "dotted lowercase event id", reservationId ?? "null"));

            var ev = catalog.Current.FindEvent(reservationId);
            if (ev == null)
                return Outcome<GameState>.Fail(ResultCodes.RefNotFound, 0,
                    ReservationRules.Diag(ResultCodes.RefNotFound, "event is not in the catalog", ReservationRules.Pointer, reservationId, "known event", reservationId));

            var existing = state.Appointments.Find(reservationId);
            if ((existing == null || existing.Status == ReservationStatus.Offered) && EventRules.IsSpent(state, ev))
                return Outcome<GameState>.Fail(ResultCodes.ConditionFalse, 0,
                    ReservationRules.Diag(ResultCodes.ConditionFalse, "the shared completion key is already completed", ReservationRules.Pointer, reservationId, "not completed", ev.OnceKey.Value));

            return Outcome<GameState>.Ok(state.With(state.Appointments.Accept(reservationId, state.Clock.AbsoluteMinute)), 0);
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
