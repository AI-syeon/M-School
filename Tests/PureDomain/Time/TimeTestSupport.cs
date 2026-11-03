using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Runtime;
using MagicCampus.State;
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

        public static PlannedChange Grow(long day)
        {
            return new PlannedChange("fake-growth", "grow", Obj(P("day", Int(day))));
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

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan)
        {
            var flags = basis.Slice("flags");
            foreach (var change in plan.Changes)
            {
                if (change.ParticipantId != ParticipantId) continue;
                if (change.Operation != "mark")
                    return Outcome<StateReplacement>.Fail(ResultCodes.Unregistered, basis.Revision, new Diagnostic("FAKE_OPERATION", "unknown operation", null, "mark", change.Operation));
                flags = flags.With(change.Payload.Members["name"].Text, DataNode.True);
            }

            return Outcome<StateReplacement>.Ok(new StateReplacement(new[] { Nodes.P("/flags", flags) }), basis.Revision);
        }
    }

    internal sealed class FakeGrowthParticipant : IStateParticipant
    {
        public bool FailPrepare;
        public string ParticipantId { get { return "fake-growth"; } }
        public IReadOnlyList<string> OwnedSlices { get; } = new[] { "/crops" };

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan)
        {
            if (FailPrepare)
                return Outcome<StateReplacement>.Fail(ResultCodes.InvariantViolation, basis.Revision, new Diagnostic("GROWTH_REFUSED", "growth refused the day", null, "accepted", "refused"));
            var items = new List<DataNode>(basis.Slice("crops").Items);
            foreach (var change in plan.Changes)
            {
                if (change.ParticipantId != ParticipantId) continue;
                items.Add(change.Payload.Members["day"]);
            }

            return Outcome<StateReplacement>.Ok(new StateReplacement(new[] { Nodes.P("/crops", DataNode.FromList(items)) }), basis.Revision);
        }
    }

    internal enum ConsumerMode { Normal, ReturnNull, Throw, TargetClock, TargetGhost }

    internal sealed class FakeConsumer : IDayBoundaryConsumer
    {
        public ConsumerMode Mode;
        public int Calls;

        public FakeConsumer(string id) { ConsumerId = id; }

        public string ConsumerId { get; }

        public PlannedChange Plan(StateSnapshot basis, DayBoundary boundary)
        {
            Calls++;
            switch (Mode)
            {
                case ConsumerMode.ReturnNull: return null;
                case ConsumerMode.Throw: throw new InvalidOperationException("consumer exploded");
                case ConsumerMode.TargetClock: return Nodes.Advance(1);
                case ConsumerMode.TargetGhost: return new PlannedChange("ghost", "grow", Nodes.Obj(Nodes.P("day", Nodes.Int(boundary.Day))));
                default: return Nodes.Grow(boundary.Day);
            }
        }
    }

    /// <summary>Delegating store that runs one hook right before the next commit, to simulate a competing writer.</summary>
    internal sealed class InterferingStore : IStateStore
    {
        private readonly IStateStore inner;
        private Action hook;

        public InterferingStore(IStateStore inner, Action hook) { this.inner = inner; this.hook = hook; }

        public StateSnapshot Current { get { return inner.Current; } }

        public Outcome<StateSnapshot> Commit(IStatePlan plan)
        {
            if (hook != null)
            {
                var run = hook;
                hook = null;
                run();
            }

            return inner.Commit(plan);
        }

        public Outcome<StateSnapshot> ReplaceAll(StateSnapshot candidate, long expectedRevision, TransactionId transactionId)
        {
            return inner.ReplaceAll(candidate, expectedRevision, transactionId);
        }

        public void OnCommitted(Action<StateCommitted> listener) { inner.OnCommitted(listener); }
    }

    internal sealed class TimeWorld
    {
        public IStateStore Store;
        public CompletionPump Pump;
        public Calendar Calendar;
        public DayBoundaryConsumerRegistry Consumers;
        public ClockParticipant Participant;
        public ClockService Clock;
        public FakeConsumer Growth;
        public FakeGrowthParticipant GrowthParticipant;

        public static readonly string[] UntouchedFields = { "saveVersion", "player", "npcs", "inventory", "flags" };

        public static DataNode BuildRoot(long absoluteMinute, double fraction, IEnumerable<string> dayJobs, bool returnPending)
        {
            return Nodes.Obj(
                Nodes.P("saveVersion", Nodes.Int(2)),
                Nodes.P("player", Nodes.Obj(Nodes.P("mapId", Nodes.Str("dorm")), Nodes.P("x", DataNode.FromNumber(3.5)), Nodes.P("y", Nodes.Int(4)))),
                Nodes.P("npcs", Nodes.List(Nodes.Obj(Nodes.P("npcId", Nodes.Str("mina")), Nodes.P("mapId", Nodes.Str("hall")), Nodes.P("x", DataNode.FromNumber(1.25))))),
                Nodes.P("inventory", Nodes.Obj(Nodes.P("herb", Nodes.Int(2)))),
                Nodes.P("flags", Nodes.Obj()),
                Nodes.P("crops", Nodes.List()),
                Nodes.P("clock", Nodes.Obj(
                    Nodes.P("absoluteMinute", Nodes.Int(absoluteMinute)),
                    Nodes.P("fraction", DataNode.FromNumber(fraction)),
                    Nodes.P("note", Nodes.Str("preserved")))),
                Nodes.P("dayJobs", Nodes.Strings(dayJobs)),
                Nodes.P("returnPending", DataNode.FromBool(returnPending)));
        }

        public static TimeWorld Create(long absoluteMinute, double fraction, IEnumerable<string> dayJobs, bool returnPending = false, bool registerGrowth = true)
        {
            return Create(BuildRoot(absoluteMinute, fraction, dayJobs, returnPending), 1, registerGrowth);
        }

        public static TimeWorld Create(DataNode root, long revision, bool registerGrowth)
        {
            var w = new TimeWorld();
            w.Calendar = new Calendar();
            w.Consumers = new DayBoundaryConsumerRegistry();
            w.Growth = new FakeConsumer("growth");
            Check.True(w.Consumers.Register(w.Growth).Status == OperationStatus.Succeeded, "growth consumer registers");
            w.Participant = new ClockParticipant(w.Calendar, w.Consumers);
            w.GrowthParticipant = new FakeGrowthParticipant();

            var registry = new StateParticipantRegistry();
            RegisterOk(registry, w.Participant);
            RegisterOk(registry, new FakeEventParticipant());
            if (registerGrowth) RegisterOk(registry, w.GrowthParticipant);
            registry.Seal();

            w.Pump = new CompletionPump();
            w.Store = new StateStore(new StateSnapshot(revision, root), registry, new IStateInvariant[0], w.Pump, new PersistedCompletionReplayPolicy());
            w.Clock = new ClockService(w.Store, w.Calendar, w.Consumers);
            return w;
        }

        private static void RegisterOk(StateParticipantRegistry registry, IStateParticipant participant)
        {
            Check.True(registry.Register(participant).Status == OperationStatus.Succeeded, "participant " + participant.ParticipantId + " registers");
        }

        public long Minute { get { return Calendar.FromSnapshot(Store.Current).AbsoluteMinute; } }
        public double Fraction { get { return Calendar.FromSnapshot(Store.Current).Fraction; } }
        public bool Pending { get { return Store.Current.Slice("returnPending").Bool; } }
        public int CropCount { get { return Store.Current.Slice("crops").Items.Count; } }

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
