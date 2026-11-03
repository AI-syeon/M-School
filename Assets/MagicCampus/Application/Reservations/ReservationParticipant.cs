using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Application.Reservations
{
    /// <summary>
    /// Owns /appointments. Operations: accept {eventId, acceptedAt} and complete {eventId}.
    /// Prepare never writes the store; complete is honoured only inside a plan that also carries the matching event completion.
    /// </summary>
    public sealed class ReservationParticipant : IStateParticipant
    {
        public const string Id = "reservations";
        public const string AcceptOperation = "accept";
        public const string CompleteOperation = "complete";

        private const string EventCompletionParticipantId = "event-effects";
        private const string EventCompletionOperation = "event.complete";

        private static readonly IReadOnlyList<string> Owned = Array.AsReadOnly(new[] { AppointmentCodec.Pointer });

        private readonly ICatalogProvider catalog;
        private readonly ICompletionLedger ledger;

        public ReservationParticipant(ICatalogProvider catalog, ICompletionLedger ledger)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public string ParticipantId => Id;
        public IReadOnlyList<string> OwnedSlices => Owned;

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var diagnostics = new List<Diagnostic>();
            var rows = AppointmentCodec.Read(basis, diagnostics);
            if (diagnostics.Count > 0)
                return Outcome<StateReplacement>.Fail(ResultCodes.ContentInvalid, basis.Revision, diagnostics.ToArray());

            var current = catalog.Current;
            var touched = false;
            foreach (var change in plan.Changes)
            {
                if (!string.Equals(change.ParticipantId, Id, StringComparison.Ordinal)) continue;
                touched = true;
                var failure = Apply(change, plan, basis, current, rows);
                if (failure != null) return failure;
            }

            if (!touched)
                return Outcome<StateReplacement>.Ok(new StateReplacement(new KeyValuePair<string, DataNode>[0]), basis.Revision);

            var replacement = new StateReplacement(new[]
            {
                new KeyValuePair<string, DataNode>(AppointmentCodec.Pointer, AppointmentCodec.Write(rows))
            });
            return Outcome<StateReplacement>.Ok(replacement, basis.Revision);
        }

        private Outcome<StateReplacement> Apply(PlannedChange change, IStatePlan plan, StateSnapshot basis, ICatalog current, List<AppointmentRow> rows)
        {
            if (change.Operation != AcceptOperation && change.Operation != CompleteOperation)
                return Reject(ResultCodes.Unregistered, basis, "reservations does not implement this operation", change.Operation, "accept|complete", change.Operation);

            if (!ReservationRules.TryString(change.Payload, "eventId", out var eventId) || !AppointmentCodec.IsValidId(eventId))
                return Reject(ResultCodes.ContentInvalid, basis, "payload.eventId is missing or malformed", change.Operation, "dotted lowercase event id", eventId ?? "missing");

            var ev = current.FindEvent(eventId);
            if (ev == null)
                return Reject(ResultCodes.RefNotFound, basis, "event is not in the catalog", eventId, "known event", eventId);

            var index = rows.FindIndex(r => string.Equals(r.Id, eventId, StringComparison.Ordinal));
            return change.Operation == AcceptOperation
                ? Accept(change, basis, ev, rows, index)
                : Complete(plan, basis, ev, rows, index);
        }

        private Outcome<StateReplacement> Accept(PlannedChange change, StateSnapshot basis, CompiledEvent ev, List<AppointmentRow> rows, int index)
        {
            if (!ReservationRules.TryInteger(change.Payload, "acceptedAt", out var acceptedAt) || acceptedAt < 0)
                return Reject(ResultCodes.ContentInvalid, basis, "payload.acceptedAt must be a non-negative integer minute", ev.Id, "integer >= 0", "invalid");

            // Already accepted or ready: idempotent, the original acceptance time is kept.
            if (index >= 0 && rows[index].Status != ReservationStatus.Offered) return null;

            if (ReservationRules.IsCompleted(ev, basis, ledger))
                return Reject(ResultCodes.ConditionFalse, basis, "the shared completion key is already completed", ev.Id, "not completed", ReservationRules.EffectiveKey(ev).Value);

            if (index >= 0) rows[index] = rows[index].With(ReservationStatus.Accepted, acceptedAt);
            else rows.Add(new AppointmentRow(ev.Id, ReservationStatus.Accepted, acceptedAt, rows.Count));
            return null;
        }

        private Outcome<StateReplacement> Complete(IStatePlan plan, StateSnapshot basis, CompiledEvent ev, List<AppointmentRow> rows, int index)
        {
            CompletionKey? expected = ev.Repeat == RepeatPolicy.Once ? ReservationRules.EffectiveKey(ev) : (CompletionKey?)null;
            var matches = plan.Completion.HasValue == expected.HasValue
                && (!expected.HasValue || plan.Completion.Value.Equals(expected.Value));
            if (!matches)
                return Reject(ResultCodes.TransactionMismatch, basis, "plan completion key does not match the event", ev.Id,
                    expected.HasValue ? expected.Value.Value : "none", plan.Completion.HasValue ? plan.Completion.Value.Value : "none");

            if (!HasEventCompletion(plan, ev.Id))
                return Reject(ResultCodes.TransactionMismatch, basis, "reservation complete is only valid in the success plan of the same event", ev.Id,
                    EventCompletionParticipantId + "/" + EventCompletionOperation, "absent");

            if (index >= 0) rows.RemoveAt(index);
            return null;
        }

        private bool HasEventCompletion(IStatePlan plan, string eventId)
        {
            foreach (var c in plan.Changes)
            {
                if (c.ParticipantId != EventCompletionParticipantId || c.Operation != EventCompletionOperation) continue;
                if (ReservationRules.TryString(c.Payload, "eventId", out var id) && id == eventId) return true;
            }
            return false;
        }

        private static Outcome<StateReplacement> Reject(string code, StateSnapshot basis, string message, string entity, string expected, string actual)
        {
            return Outcome<StateReplacement>.Fail(code, basis.Revision,
                ReservationRules.Diag(code, message, AppointmentCodec.Pointer, entity, expected, actual));
        }
    }
}
