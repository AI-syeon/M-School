using System;
using System.Collections.Generic;
using MagicCampus.Application.Reservations;
using MagicCampus.Contracts;

namespace MagicCampus.Tests.PureDomain.Schedules
{
    public static class ReservationSpecs
    {
        public static void Run()
        {
            OrdersCandidatesByPriorityThenAcceptance();
            RejectsInvalidRowsExplicitly();
            PlansAndAppliesAccept();
            CompleteRemovesOnlyTheMatchingReservation();
            RefusesUnknownOperations();
        }

        private static Outcome<StateReplacement> Prepare(ReservationRig rig, StateSnapshot snapshot, CompletionKey? completion, params PlannedChange[] changes)
        {
            return rig.Participant.Prepare(snapshot, new StatePlan(new TransactionId("tx.1"), completion, snapshot.Revision, changes));
        }

        private static List<string> Rows(Outcome<StateReplacement> outcome)
        {
            Spec.True(outcome.Succeeded, "prepare succeeded");
            var rows = new List<string>();
            foreach (var item in outcome.Value.Slices["/appointments"].Items)
            {
                item.TryGet("id", out var id);
                item.TryGet("status", out var status);
                item.TryGet("acceptedAt", out var at);
                rows.Add(id.Text + ":" + status.Text + ":" + at.Integer);
            }
            return rows;
        }

        private static void OrdersCandidatesByPriorityThenAcceptance()
        {
            var rig = new ReservationRig(
                Kit.Event("event.res", "resolution"), Kit.Event("event.rel", "relationship"), Kit.Event("event.eve", "event"),
                Kit.Event("event.gen"), Kit.Event("event.gen2"), Kit.Event("event.off"), Kit.Event("event.done", completion: "node.done"));
            rig.Ledger.Done.Add("node.done");
            var snapshot = Kit.Snapshot(4,
                Kit.Row("event.gen", "Accepted", 50), Kit.Row("event.rel", "Ready", 90), Kit.Row("event.res", "Accepted", 100),
                Kit.Row("event.gen2", "Accepted", 50), Kit.Row("event.off", "Offered", 1), Kit.Row("event.done", "Accepted", 1),
                Kit.Row("event.eve", "Accepted", 5), Kit.Row("event.ghost", "Accepted", 1));

            var inspection = rig.Service.Inspect(snapshot);
            var order = new List<string>();
            foreach (var c in inspection.Candidates) order.Add(c.EventId);
            Spec.Equal("event.res,event.rel,event.eve,event.gen,event.gen2", string.Join(",", order),
                "priority 400/300/200/100, then acceptedAt, then row index; offered and completed are excluded");
            Spec.Equal(1, inspection.Diagnostics.Count, "unknown event row is reported");
            Spec.Equal(ResultCodes.RefNotFound, inspection.Diagnostics[0].Code, "unknown event code");
            Spec.Equal(5, rig.Service.Candidates(snapshot).Count, "Candidates matches Inspect");
        }

        private static void RejectsInvalidRowsExplicitly()
        {
            var rig = new ReservationRig(Kit.Event("event.one"));
            var extra = Kit.Obj(Kit.Kv("id", Kit.Str("event.one")), Kit.Kv("status", Kit.Str("Accepted")),
                Kit.Kv("acceptedAt", DataNode.FromInteger(1)), Kit.Kv("expiresAt", DataNode.FromInteger(9)));
            var snapshot = Kit.Snapshot(1, extra, Kit.Row("event.one", "Waiting", 1));

            var inspection = rig.Service.Inspect(snapshot);
            Spec.Equal(0, inspection.Candidates.Count, "invalid rows are not candidates");
            Spec.True(inspection.Diagnostics.Count >= 1, "the unreadable appointments are reported");

            var prepared = Prepare(rig, snapshot, null, Kit.Accept("event.one", 5));
            Spec.Equal(ResultCodes.ContentInvalid, prepared.Result.Code, "participant refuses an invalid slice");
        }

        private static void PlansAndAppliesAccept()
        {
            var rig = new ReservationRig(Kit.Event("event.one"), Kit.Event("event.done", completion: "node.done"));
            rig.Calendar.Minute = 777;
            var empty = Kit.Snapshot(1);

            var planned = rig.Service.PlanAccept("event.one", empty);
            Spec.True(planned.Succeeded, "known invitation can be accepted");
            planned.Value.Payload.TryGet("acceptedAt", out var at);
            Spec.Equal(777L, at.Integer, "acceptedAt is the absolute minute of the basis");
            Spec.Equal("event.one:Accepted:777", string.Join(",", Rows(rig.Participant.Prepare(empty,
                new StatePlan(new TransactionId("tx.2"), null, 1, new[] { planned.Value })))), "row created as Accepted");

            var offered = Kit.Snapshot(2, Kit.Row("event.one", "Offered", 3));
            Spec.Equal("event.one:Accepted:777", string.Join(",", Rows(Prepare(rig, offered, null, rig.Service.PlanAccept("event.one", offered).Value))), "offered becomes accepted");

            var accepted = Kit.Snapshot(3, Kit.Row("event.one", "Accepted", 100));
            var again = rig.Service.PlanAccept("event.one", accepted);
            Spec.True(again.Succeeded, "accept is idempotent");
            Spec.Equal("event.one:Accepted:100", string.Join(",", Rows(Prepare(rig, accepted, null, again.Value))), "original acceptance time is kept");

            Spec.Equal(ResultCodes.RefNotFound, rig.Service.PlanAccept("event.nope", empty).Result.Code, "unknown event");

            rig.Ledger.Done.Add("node.done");
            Spec.Equal(ResultCodes.ConditionFalse, rig.Service.PlanAccept("event.done", empty).Result.Code, "completed shared key blocks planning");
            Spec.Equal(ResultCodes.ConditionFalse, Prepare(rig, empty, null, Kit.Accept("event.done", 5)).Result.Code, "completed shared key blocks the participant too");
        }

        private static void CompleteRemovesOnlyTheMatchingReservation()
        {
            var rig = new ReservationRig(Kit.Event("event.one", completion: "node.a"), Kit.Event("event.two"));
            var snapshot = Kit.Snapshot(5, Kit.Row("event.one", "Accepted", 5), Kit.Row("event.two", "Accepted", 6));

            var ok = Prepare(rig, snapshot, new CompletionKey("node.a"), Kit.EventDone("event.one", "node.a"), Kit.Complete("event.one"));
            Spec.Equal("event.two:Accepted:6", string.Join(",", Rows(ok)), "only the matching reservation is removed");

            var plain = Prepare(rig, snapshot, new CompletionKey("event.two"), Kit.EventDone("event.two", "event.two"), Kit.Complete("event.two"));
            Spec.Equal("event.one:Accepted:5", string.Join(",", Rows(plain)), "event without an explicit completion key uses its event id");

            Spec.Equal(ResultCodes.TransactionMismatch, Prepare(rig, snapshot, null, Kit.EventDone("event.one", "node.a"), Kit.Complete("event.one")).Result.Code,
                "plan completion must match the event completion");
            Spec.Equal(ResultCodes.TransactionMismatch, Prepare(rig, snapshot, new CompletionKey("node.a"), Kit.Complete("event.one")).Result.Code,
                "complete without the success entry of the same event is refused");
            Spec.Equal(ResultCodes.TransactionMismatch, Prepare(rig, snapshot, new CompletionKey("node.a"), Kit.EventDone("event.two", "node.a"), Kit.Complete("event.one")).Result.Code,
                "success entry of another event does not authorize the removal");
        }

        private static void RefusesUnknownOperations()
        {
            var rig = new ReservationRig(Kit.Event("event.one"));
            var bogus = new PlannedChange(ReservationParticipant.Id, "expire", DataNode.Null);
            Spec.Equal(ResultCodes.Unregistered, Prepare(rig, Kit.Snapshot(1), null, bogus).Result.Code, "no expiry or other operation exists");
        }
    }
}
