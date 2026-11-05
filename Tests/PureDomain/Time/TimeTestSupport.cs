using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Tests.PureDomain.Support;
using MagicCampus.Timekeeping;
using MagicCampus.Transactions.Core;

namespace MagicCampus.Tests.Time
{
    internal static class Check
    {
        public static void True(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("Time spec failed: " + what);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Time spec failed: " + what + " (expected " + expected + ", actual " + actual + ")");
        }

        public static void Throws<TException>(Action action, string what) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException("Time spec failed: " + what + " (expected " + typeof(TException).Name + ")");
        }
    }

    internal static class Nodes
    {
        public static KeyValuePair<string, DataNode> P(string key, DataNode value) { return new KeyValuePair<string, DataNode>(key, value); }
        public static DataNode Obj(params KeyValuePair<string, DataNode>[] members) { return DataNode.FromObject(members); }
        public static DataNode Str(string text) { return DataNode.FromString(text); }
        public static DataNode Int(long value) { return DataNode.FromInteger(value); }
        public static DataNode List(params DataNode[] items) { return DataNode.FromList(items); }

        public static DataNode Strings(IEnumerable<string> values)
        {
            var items = new List<DataNode>();
            foreach (var v in values) items.Add(DataNode.FromString(v));
            return DataNode.FromList(items);
        }

        /// <summary>Hand-built frozen clock.advance {minutes}, independent of the production helper.</summary>
        public static PlannedChange Advance(double minutes)
        {
            var value = minutes == Math.Floor(minutes) ? Int((long)minutes) : DataNode.FromNumber(minutes);
            return new PlannedChange("clock", "advance", Obj(P("minutes", value)));
        }

        public static PlannedChange Mark(string name)
        {
            return new PlannedChange("fake-event", "mark", Obj(P("name", Str(name))));
        }
    }

    internal sealed class FakeEventParticipant : IStateParticipant
    {
        public string ParticipantId { get { return "fake-event"; } }
        public IReadOnlyList<string> OwnedSlices { get; } = new[] { "/flags" };

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, StatePlan plan)
        {
            var rows = new List<DataNode>(basis.Slice("flags").Items);
            foreach (var change in plan.Changes)
            {
                if (change.ParticipantId != ParticipantId) continue;
                if (change.Operation != "mark")
                    return Outcome<StateReplacement>.Fail(ResultCodes.Unregistered, basis.Revision, new Diagnostic("FAKE_OPERATION", "unknown operation", null, "mark", change.Operation));
                rows.Add(Nodes.Obj(Nodes.P("id", change.Payload.Members["name"]), Nodes.P("value", DataNode.True)));
            }

            return Outcome<StateReplacement>.Ok(new StateReplacement(new[] { Nodes.P("/flags", DataNode.FromList(rows)) }), basis.Revision);
        }
    }

    internal sealed class TimeWorld
    {
        public StateStore Store;
        public CompletionPump Pump;
        public Calendar Calendar;
        public ClockParticipant Participant;
        public ClockService Clock;

        public static readonly string[] UntouchedFields = { "saveVersion", "player", "npcs", "inventory", "flags" };

        public static DataNode BuildRoot(long absoluteMinute, double fraction, IEnumerable<string> dayJobs, bool returnPending, bool clockNote = false)
        {
            var clock = new List<KeyValuePair<string, DataNode>> { Nodes.P("absoluteMinute", Nodes.Int(absoluteMinute)), Nodes.P("fraction", DataNode.FromNumber(fraction)) };
            if (clockNote) clock.Add(Nodes.P("note", Nodes.Str("preserved")));
            var m = new Dictionary<string, DataNode>(StringComparer.Ordinal)
            {
                { "saveVersion", Nodes.Int(2) },
                { "player", Nodes.Obj(Nodes.P("mapId", Nodes.Str("dorm")), Nodes.P("x", DataNode.FromNumber(3.5)), Nodes.P("y", Nodes.Int(4))) },
                { "npcs", Nodes.List(Nodes.Obj(Nodes.P("npcId", Nodes.Str("mina")), Nodes.P("mapId", Nodes.Str("hall")), Nodes.P("x", DataNode.FromNumber(1.25)))) },
                { "inventory", Nodes.Obj(Nodes.P("herb", Nodes.Int(2))) },
                { "flags", Nodes.List() },
                { "clock", DataNode.FromObject(clock) },
                { "dayJobs", Nodes.Strings(dayJobs) },
                { "returnPending", DataNode.FromBool(returnPending) }
            };
            TypedFields.FillMissing(m);
            return DataNode.FromObject(m);
        }

        public static TimeWorld Create(long absoluteMinute, double fraction, IEnumerable<string> dayJobs, bool returnPending = false)
        {
            return Create(BuildRoot(absoluteMinute, fraction, dayJobs, returnPending), 1);
        }

        public static TimeWorld Create(DataNode root, long revision, IStateInvariant[] invariants = null)
        {
            var w = new TimeWorld();
            w.Calendar = new Calendar();
            w.Participant = new ClockParticipant(w.Calendar);

            var registry = new StateParticipantRegistry();
            RegisterOk(registry, w.Participant);
            RegisterOk(registry, new FakeEventParticipant());
            registry.Seal();

            w.Pump = new CompletionPump();
            w.Store = new StateStore(new StateSnapshot(revision, root), registry, invariants ?? new IStateInvariant[0], w.Pump);
            w.Clock = new ClockService(w.Store, w.Calendar);
            return w;
        }

        private static void RegisterOk(StateParticipantRegistry registry, IStateParticipant participant)
        {
            Check.True(registry.Register(participant).Status == OperationStatus.Succeeded, "participant " + participant.ParticipantId + " registers");
        }

        public long Minute { get { return Calendar.FromSnapshot(Store.Current).AbsoluteMinute; } }
        public double Fraction { get { return Calendar.FromSnapshot(Store.Current).Fraction; } }
        public bool TypedFlag(string id) { return Store.Current.TypedState().Progress.Flags.TryGet(id, out var v) && v.IsBool && v.Bool; }
        public bool Pending { get { return Store.Current.Slice("returnPending").Bool; } }

        public List<string> Jobs
        {
            get
            {
                var jobs = new List<string>();
                foreach (var item in Store.Current.Slice("dayJobs").Items) jobs.Add(item.Text);
                return jobs;
            }
        }

        public Outcome<StateSnapshot> Commit(string transaction, params PlannedChange[] changes)
        {
            return Store.Commit(new StatePlan(new TransactionId(transaction), null, Store.Current.Revision, changes));
        }

        public void AssertUnchanged(StateSnapshot before, string what)
        {
            Check.Equal(before.Revision, Store.Current.Revision, what + ": revision");
            Check.True(before.Root.Equals(Store.Current.Root), what + ": root");
        }
    }
}
