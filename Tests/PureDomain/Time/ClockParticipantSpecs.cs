using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Timekeeping;

namespace MagicCampus.Tests.Time
{
    internal static class ClockParticipantSpecs
    {
        public static void Run()
        {
            FrozenAdvanceAggregates();
            MultiDayOrder();
            UnknownOperation();
            BadPayloads();
            MalformedBasis();
            ReplacementShape();
        }

        private static StatePlan PlanOf(TimeWorld w, params PlannedChange[] changes)
        {
            return new StatePlan(new TransactionId("spec." + w.Store.Current.Revision), null, w.Store.Current.Revision, changes);
        }

        private static void FrozenAdvanceAggregates()
        {
            var w = TimeWorld.Create(1400, 0, new[] { "legacy" });
            var result = w.Commit("agg.1", Nodes.Advance(30), Nodes.Advance(45.5));
            Check.True(result.Succeeded, "aggregated advances commit");
            Check.Equal(1475L, w.Minute, "30 + 45.5 minutes");
            Check.Equal(0.5, w.Fraction, "aggregated fraction");
            Check.Equal("legacy|boundary.day_d1", string.Join("|", w.Jobs), "single boundary recorded once");
            Check.True(w.Pending, "return pending after aggregated midnight");
        }

        private static void MultiDayOrder()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var ok = w.Commit("multi.1", Nodes.Advance(1440), Nodes.Advance(1440));
            Check.True(ok.Succeeded, "two day advance commits");
            Check.Equal("boundary.day_d1|boundary.day_d2", string.Join("|", w.Jobs), "date order of recorded jobs");
            Check.Equal(420L + 2880, w.Minute, "two days advanced");
        }

        private static void UnknownOperation()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var before = w.Store.Current;
            var outcome = w.Participant.Prepare(before, PlanOf(w, new PlannedChange("clock", "teleport", Nodes.Obj())));
            Check.True(!outcome.Succeeded, "unknown operation refused");
            Check.Equal(ResultCodes.Unregistered, outcome.Result.Code, "unknown operation code");
            var commit = w.Commit("op.1", new PlannedChange("clock", "teleport", Nodes.Obj()));
            Check.True(!commit.Succeeded, "store refuses unknown clock operation");
            w.AssertUnchanged(before, "unknown operation");
        }

        private static void BadPayloads()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var before = w.Store.Current;
            var payloads = new[]
            {
                Nodes.Obj(),
                DataNode.Null,
                Nodes.Obj(Nodes.P("minutes", Nodes.Str("5"))),
                Nodes.Obj(Nodes.P("minutes", Nodes.Int(-1))),
                Nodes.Obj(Nodes.P("minutes", DataNode.FromNumber(1e12))),
                Nodes.Obj(Nodes.P("minutes", DataNode.FromNumber(5)), Nodes.P("extra", Nodes.Int(1)))
            };
            for (var i = 0; i < payloads.Length; i++)
            {
                var change = new PlannedChange("clock", "advance", payloads[i]);
                var outcome = w.Participant.Prepare(before, PlanOf(w, change));
                Check.True(!outcome.Succeeded, "bad payload " + i + " refused");
                Check.Equal(ResultCodes.ContentInvalid, outcome.Result.Code, "bad payload code " + i);
                var commit = w.Store.Commit(new StatePlan(new TransactionId("bad." + i), null, before.Revision, new[] { change }));
                Check.True(!commit.Succeeded, "store refuses bad payload " + i);
            }

            w.AssertUnchanged(before, "bad payloads");
        }

        private static void MalformedBasis()
        {
            var w = TimeWorld.Create(420, 0, new string[0]);
            var plan = PlanOf(w, Nodes.Advance(5));
            var clock = Nodes.Obj(Nodes.P("absoluteMinute", Nodes.Int(420)), Nodes.P("fraction", DataNode.FromNumber(0)));
            var roots = new[]
            {
                Nodes.Obj(Nodes.P("dayJobs", Nodes.List()), Nodes.P("returnPending", DataNode.False)),
                Nodes.Obj(Nodes.P("clock", clock), Nodes.P("returnPending", DataNode.False)),
                Nodes.Obj(Nodes.P("clock", clock), Nodes.P("dayJobs", Nodes.List(Nodes.Int(1))), Nodes.P("returnPending", DataNode.False)),
                Nodes.Obj(Nodes.P("clock", clock), Nodes.P("dayJobs", Nodes.List())),
                Nodes.Obj(Nodes.P("clock", clock), Nodes.P("dayJobs", Nodes.List()), Nodes.P("returnPending", Nodes.Int(0)))
            };
            for (var i = 0; i < roots.Length; i++)
            {
                var outcome = w.Participant.Prepare(new StateSnapshot(1, roots[i]), plan);
                Check.True(!outcome.Succeeded, "malformed basis " + i + " refused");
                Check.Equal(ResultCodes.ContentInvalid, outcome.Result.Code, "malformed basis code " + i);
                Check.True(outcome.Result.Details.Count > 0, "malformed basis diagnostics " + i);
            }
        }

        private static void ReplacementShape()
        {
            var w = TimeWorld.Create(1430, 0.25, new[] { "legacy" });
            var before = w.Store.Current;
            var outcome = w.Participant.Prepare(before, PlanOf(w, Nodes.Advance(40)));
            Check.True(outcome.Succeeded, "prepare succeeds");
            var slices = outcome.Value.Slices;
            Check.Equal(3, slices.Count, "exactly the owned slices");
            Check.True(slices.ContainsKey("/clock") && slices.ContainsKey("/dayJobs") && slices.ContainsKey("/returnPending"), "owned pointers");
            Check.Equal(1470L, slices["/clock"].Members["absoluteMinute"].Integer, "replacement minute");
            Check.Equal(DataKind.Number, slices["/clock"].Members["fraction"].Kind, "replacement fraction kind");
            Check.Equal(0.25, slices["/clock"].Members["fraction"].Number, "replacement fraction");
            Check.Equal("preserved", slices["/clock"].Members["note"].Text, "unknown clock member preserved");
            Check.Equal(DataKind.List, slices["/dayJobs"].Kind, "dayJobs list");
            Check.Equal(2, slices["/dayJobs"].Items.Count, "dayJobs entries");
            Check.True(slices["/returnPending"].Bool, "returnPending derived");
            w.AssertUnchanged(before, "Prepare never writes");

            var empty = w.Participant.Prepare(before, PlanOf(w, Nodes.Mark("x")));
            Check.True(empty.Succeeded && empty.Value.Slices.Count == 0, "plans without clock changes replace nothing");

            var owned = new List<string>(w.Participant.OwnedSlices);
            Check.Equal("clock", w.Participant.ParticipantId, "participant id");
            Check.Equal("/clock|/dayJobs|/returnPending", string.Join("|", owned), "owned slices");
        }
    }
}
