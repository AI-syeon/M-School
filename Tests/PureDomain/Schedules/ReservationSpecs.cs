using System;
using System.Collections.Generic;
using MagicCampus.Application.Reservations;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.State;
using MagicCampus.Tests.PureDomain.Support;

namespace MagicCampus.Tests.PureDomain.Schedules
{
    public static class ReservationSpecs
    {
        public static void Run()
        {
            OrdersCandidatesByPriorityThenAcceptance();
            RejectsInvalidRowsExplicitly();
            AcceptsInTheTypedState();
            CompleteRemovesOnlyTheMatchingReservation();
        }

        private static void OrdersCandidatesByPriorityThenAcceptance()
        {
            var rig = new ReservationRig(
                Kit.Event("event.res", "resolution"), Kit.Event("event.rel", "relationship"), Kit.Event("event.eve", "event"),
                Kit.Event("event.gen"), Kit.Event("event.gen2"), Kit.Event("event.off"), Kit.Event("event.done", completion: "node.done"));
            var snapshot = Spent(Kit.Snapshot(4,
                Kit.Row("event.gen", "Accepted", 50), Kit.Row("event.rel", "Ready", 90), Kit.Row("event.res", "Accepted", 100),
                Kit.Row("event.gen2", "Accepted", 50), Kit.Row("event.off", "Offered", 1), Kit.Row("event.done", "Accepted", 1),
                Kit.Row("event.eve", "Accepted", 5), Kit.Row("event.ghost", "Accepted", 1)), "rewardLedger", "node.done");

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
            var extra = Kit.Obj(Kit.Kv("id", Kit.Str("event.one")), Kit.Kv("status", Kit.Str("Accepted")),
                Kit.Kv("acceptedAt", DataNode.FromInteger(1)), Kit.Kv("expiresAt", DataNode.FromInteger(9)));
            Spec.Equal(ResultCodes.ContentInvalid, SaveDocument.Read(Kit.Tree(1, extra)).Result.Code, "a row with an unknown member cannot be held in a state");
            Spec.Equal(ResultCodes.ContentInvalid, SaveDocument.Read(Kit.Tree(1, Kit.Row("event.one", "Waiting", 1))).Result.Code, "a row with an unknown status cannot be held in a state");
        }

        private static StateSnapshot Spent(StateSnapshot snapshot, string ledger, string key)
        {
            return SnapshotTrees.Snapshot(snapshot.Revision, snapshot.Tree().With(ledger, DataNode.FromList(new[] { Kit.Str(key) })));
        }

        private static string Rows(GameState state)
        {
            var rows = new List<string>();
            foreach (var a in state.Appointments.Rows) rows.Add(a.Id + ":" + a.Status + ":" + a.AcceptedAt);
            return string.Join(",", rows);
        }

        private static void AcceptsInTheTypedState()
        {
            var rig = new ReservationRig(Kit.Event("event.one"), Kit.Event("event.done", completion: "node.done"),
                Kit.Event("event.drill", completion: "node.done", repeat: RepeatPolicy.Practice));
            var empty = Kit.Snapshot(1).State;

            var created = rig.Service.Accept(empty, "event.one");
            Spec.Equal("event.one:Accepted:420", Rows(created.Value), "new row is Accepted at the current absolute minute");

            var offered = Kit.Snapshot(2, Kit.Row("event.one", "Offered", 3)).State;
            Spec.Equal("event.one:Accepted:420", Rows(rig.Service.Accept(offered, "event.one").Value), "offered becomes accepted in place");

            var accepted = Kit.Snapshot(3, Kit.Row("event.one", "Ready", 100), Kit.Row("event.done", "Accepted", 7)).State;
            Spec.Equal("event.one:Ready:100,event.done:Accepted:7", Rows(rig.Service.Accept(accepted, "event.one").Value), "accept is idempotent and keeps the original time");

            Spec.Equal(ResultCodes.RefNotFound, rig.Service.Accept(empty, "event.nope").Result.Code, "unknown event");
            Spec.Equal(ResultCodes.ContentInvalid, rig.Service.Accept(empty, "Bad Id").Result.Code, "malformed id");

            foreach (var ledger in new[] { "completed", "rewardLedger" })
            {
                var spent = Spent(Kit.Snapshot(4), ledger, "node.done").State;
                Spec.Equal(ResultCodes.ConditionFalse, rig.Service.Accept(spent, "event.done").Result.Code, "a key consumed in " + ledger + " blocks the accept");
                Spec.Equal("event.drill:Accepted:420", Rows(rig.Service.Accept(spent, "event.drill").Value), "practice events never spend their key (" + ledger + ")");
            }
        }

        private static readonly WorldPlaces Nobody = new WorldPlaces(null, new KeyValuePair<string, Position>[0]);

        private static void CompleteRemovesOnlyTheMatchingReservation()
        {
            var rig = new ReservationRig(Kit.Event("event.one", completion: "node.a"), Kit.Event("event.two"), Kit.Event("event.drill", repeat: RepeatPolicy.Practice));
            var state = Kit.Snapshot(5, Kit.Row("event.one", "Accepted", 5), Kit.Row("event.two", "Accepted", 6), Kit.Row("event.drill", "Accepted", 7)).State;
            var catalog = rig.Catalog.Current;

            var ok = rig.Rules.Complete(state, catalog.FindEvent("event.one"), Nobody);
            Spec.Equal("event.two:Accepted:6,event.drill:Accepted:7", Rows(ok.Value), "only the matching reservation is removed");
            Spec.True(ok.Value.Progress.IsConsumed(new CompletionKey("node.a")), "removed in the success commit that claims the event's shared key");

            var plain = rig.Rules.Complete(state, catalog.FindEvent("event.two"), Nobody);
            Spec.Equal("event.one:Accepted:5,event.drill:Accepted:7", Rows(plain.Value), "event without an explicit completion key uses its event id");

            var practice = rig.Rules.Complete(state, catalog.FindEvent("event.drill"), Nobody);
            Spec.Equal(ResultCodes.TransactionMismatch, practice.Result.Code, "a practice run with a reservation row is refused (decision D4)");
            Spec.Equal("event.one:Accepted:5,event.two:Accepted:6,event.drill:Accepted:7", Rows(state), "a refused completion leaves every row");

            var unreserved = Kit.Snapshot(6).State;
            Spec.True(ReferenceEquals(rig.Rules.Complete(unreserved, catalog.FindEvent("event.drill"), Nobody).Value, unreserved), "a practice run without a row and without effects changes nothing");
        }
    }
}
