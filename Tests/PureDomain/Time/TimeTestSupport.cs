using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Tests.PureDomain.Support;
using MagicCampus.Timekeeping;

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
    }

    internal sealed class TimeWorld
    {
        public GameStore Store;
        public CompletionPump Pump;
        public Calendar Calendar;
        public ClockService Clock;

        public static readonly string[] UntouchedFields = { "saveVersion", "player", "npcs", "inventory", "flags" };

        public static DataNode BuildRoot(long absoluteMinute, double fraction, IEnumerable<string> dayJobs, bool returnPending)
        {
            var clock = new List<KeyValuePair<string, DataNode>> { Nodes.P("absoluteMinute", Nodes.Int(absoluteMinute)), Nodes.P("fraction", DataNode.FromNumber(fraction)) };
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

        public static TimeWorld Create(DataNode root, long revision)
        {
            var w = new TimeWorld();
            w.Calendar = new Calendar();
            w.Pump = new CompletionPump();
            w.Store = new GameStore(SnapshotTrees.Snapshot(revision, root), w.Pump);
            w.Clock = new ClockService(w.Store, w.Calendar);
            return w;
        }

        public long Minute { get { return Store.Current.State.Clock.AbsoluteMinute; } }
        public double Fraction { get { return Store.Current.State.Clock.Fraction.Value; } }
        public bool Pending { get { return Store.Current.State.Clock.ReturnPending; } }

        public List<string> Jobs
        {
            get
            {
                var jobs = new List<string>();
                jobs.AddRange(Store.Current.State.Clock.DayJobs.Items);
                return jobs;
            }
        }

        public void AssertUnchanged(StateSnapshot before, string what)
        {
            Check.Equal(before.Revision, Store.Current.Revision, what + ": revision");
            Check.True(ReferenceEquals(before.State, Store.Current.State), what + ": state");
        }
    }
}
