using System;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Tests.PureDomain.Support;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Timekeeping;
using MagicCampus.Transactions;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Tests.Effects
{
    /// <summary>Local fixtures: fake catalog, a save tree and a rig with the real store, event rules and condition evaluator.</summary>
    internal static class Fx
    {
        private static readonly SourceLocation Src = new SourceLocation("test.json", "/events/0", "evt", "evt#1", 0);

        public static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("EffectsSpecs: " + message);
        }

        public static DataNode S(string v) { return DataNode.FromString(v); }
        public static DataNode I(long v) { return DataNode.FromInteger(v); }
        public static DataNode B(bool v) { return DataNode.FromBool(v); }
        public static KeyValuePair<string, DataNode> P(string key, DataNode value) { return new KeyValuePair<string, DataNode>(key, value); }
        public static DataNode Obj(params KeyValuePair<string, DataNode>[] members) { return DataNode.FromObject(members); }
        public static DataNode List(params DataNode[] items) { return DataNode.FromList(items); }

        public static DataNode Strings(params string[] values)
        {
            var items = new List<DataNode>();
            foreach (var v in values) items.Add(S(v));
            return DataNode.FromList(items);
        }

        public static DataNode Flag(string id, DataNode value) { return Obj(P("id", S(id)), P("value", value)); }

        private static ParamDef Prm(string name, ParamType type, bool variadic = false)
        {
            return new ParamDef(name, type, true, variadic, "", null, null, null);
        }

        private static FunctionDef Fn(string name, FunctionRole role, params ParamDef[] parameters)
        {
            return new FunctionDef(name, role, "domain service", parameters);
        }

        public static readonly FunctionDef TriggerFn = Fn("interact", FunctionRole.Trigger);
        public static readonly FunctionDef EqFn = Fn("eq", FunctionRole.Condition, Prm("variable", ParamType.Word), Prm("value", ParamType.Any));
        public static readonly FunctionDef GteFn = Fn("gte", FunctionRole.Condition, Prm("variable", ParamType.Word), Prm("value", ParamType.Any));
        public static readonly FunctionDef CompletedFn = Fn("completed", FunctionRole.Condition, Prm("id", ParamType.Ref));
        public static readonly FunctionDef DiscoveredFn = Fn("discovered", FunctionRole.Condition, Prm("id", ParamType.Ref));
        public static readonly FunctionDef EvidenceFn = Fn("evidence", FunctionRole.Condition, Prm("id", ParamType.Ref));
        public static readonly FunctionDef LearnedFn = Fn("learned", FunctionRole.Condition, Prm("id", ParamType.Ref));
        public static readonly FunctionDef CaseCountFn = Fn("caseCountGte", FunctionRole.Condition, Prm("count", ParamType.Int));
        public static readonly FunctionDef AnyFn = Fn("any", FunctionRole.Condition, Prm("alternatives", ParamType.Text, true));
        public static readonly FunctionDef MysteryFn = Fn("mystery", FunctionRole.Condition, Prm("id", ParamType.Word));
        public static readonly FunctionDef SetFn = Fn("set", FunctionRole.Effect, Prm("variable", ParamType.Word), Prm("value", ParamType.Any));
        public static readonly FunctionDef CompleteFn = Fn("complete", FunctionRole.Effect, Prm("id", ParamType.Ref));
        public static readonly FunctionDef CurrencyFn = Fn("currency.add", FunctionRole.Effect, Prm("amountCrowns", ParamType.Int));
        public static readonly FunctionDef ItemGrantFn = Fn("item.grant", FunctionRole.Effect, Prm("item", ParamType.Ref), Prm("quantity", ParamType.Int));

        public static CompiledCall Call(FunctionDef function, bool negated, params KeyValuePair<string, DataNode>[] args)
        {
            return new CompiledCall(function, 1, "", negated, args, null, Src);
        }

        public static CompiledCall AnyCall(bool negated, params CompiledCall[][] alternatives)
        {
            var compiled = new List<IReadOnlyList<CompiledCall>>();
            var raw = new List<DataNode>();
            foreach (var alternative in alternatives)
            {
                compiled.Add(alternative);
                raw.Add(S("alternative" + compiled.Count));
            }
            return new CompiledCall(AnyFn, 1, "", negated, new[] { P("alternatives", DataNode.FromList(raw)) }, compiled, Src);
        }

        public static CompiledEvent Event(string id, RepeatPolicy repeat, string key, int cost, params CompiledCall[] effects)
        {
            return new CompiledEvent(id, ExecutionMode.Scene, repeat, null, Call(TriggerFn, false), null, cost, null, null, effects,
                key == null ? (CompletionKey?)null : new CompletionKey(key), Obj());
        }

        public static DataNode Root(Action<Dictionary<string, DataNode>> tweak = null)
        {
            var m = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            m["flags"] = List();
            m["completed"] = List();
            m["rewardLedger"] = List();
            m["wallet"] = I(100);
            m["discoveredCaseIds"] = Strings("lab");
            m["evidence"] = Strings("e1");
            m["learnedSpellIds"] = Strings("spark");
            m["caseResolutionOrder"] = Strings("case_a", "case_b");
            m["clock"] = Obj(P("absoluteMinute", I(1440 + 600)), P("fraction", DataNode.FromNumber(0.5)));
            m["appointments"] = List();
            TypedFields.FillMissing(m);
            for (var i = 0; i < 31; i++) m["field_" + i.ToString("00")] = i % 2 == 0 ? I(i) : S("v" + i);
            if (tweak != null) tweak(m);
            return DataNode.FromObject(m);
        }

        public static FakeCatalog DefaultCatalog()
        {
            var c = new FakeCatalog();
            c.Var("met_mentor", "bool");
            c.Var("gold_stars", "int", 0, 10);
            c.Var("note", "string");
            var met = Call(SetFn, false, P("variable", S("met_mentor")), P("value", B(true)));
            Func<string, CompiledCall> done = id => Call(CompleteFn, false, P("id", S(id)));
            Func<long, CompiledCall> pay = n => Call(CurrencyFn, false, P("amountCrowns", I(n)));
            c.EventList.Add(Event("evt_intro", RepeatPolicy.Once, null, 0, met, done("intro_seen"), pay(25)));
            c.EventList.Add(Event("evt_practice", RepeatPolicy.Practice, null, 0, met));
            c.EventList.Add(Event("evt_practice_cost", RepeatPolicy.Practice, null, 30, met));
            c.EventList.Add(Event("evt_practice_reward", RepeatPolicy.Practice, null, 0, pay(5)));
            c.EventList.Add(Event("evt_cost30", RepeatPolicy.Once, null, 30));
            c.EventList.Add(Event("evt_craft", RepeatPolicy.Once, null, 0));
            c.EventList.Add(Event("evt_order", RepeatPolicy.Once, null, 30, met, done("order_seen"), pay(5)));
            c.EventList.Add(Event("evt_bad", RepeatPolicy.Once, null, 0, met,
                Call(ItemGrantFn, false, P("item", S("potion")), P("quantity", I(1)))));
            c.EventList.Add(Event("evt_shared_a", RepeatPolicy.Once, "shared_key", 0, pay(10)));
            c.EventList.Add(Event("evt_shared_b", RepeatPolicy.Once, "shared_key", 0, pay(10)));
            return c;
        }
    }

    internal sealed class FakeCatalog : ICatalog
    {
        public readonly List<CompiledEvent> EventList = new List<CompiledEvent>();
        public readonly Dictionary<string, DataNode> Defs = new Dictionary<string, DataNode>(StringComparer.Ordinal);

        public string ContentVersion { get { return "test"; } }
        public string CatalogHash { get { return "hash"; } }
        public IReadOnlyList<CompiledEvent> Events { get { return EventList; } }

        public CompiledEvent FindEvent(string id)
        {
            foreach (var e in EventList) if (e.Id == id) return e;
            return null;
        }

        public DataNode FindDefinition(string kind, string id)
        {
            DataNode node;
            return Defs.TryGetValue(kind + "/" + id, out node) ? node : null;
        }

        public void Var(string id, string type, long? min = null, long? max = null)
        {
            var members = new List<KeyValuePair<string, DataNode>> { Fx.P("type", Fx.S(type)) };
            if (min.HasValue) members.Add(Fx.P("min", Fx.I(min.Value)));
            if (max.HasValue) members.Add(Fx.P("max", Fx.I(max.Value)));
            Defs["variable/" + id] = DataNode.FromObject(members);
        }
    }

    internal sealed class Rig
    {
        private static int counter;

        public static readonly WorldPlaces Nobody = new WorldPlaces(null, new KeyValuePair<string, Position>[0]);

        public readonly FakeCatalog Catalog;
        public readonly GameStore Store;
        public readonly EventRules Rules;
        public readonly ConditionEvaluator Conditions;

        public Rig(FakeCatalog catalog, DataNode root = null)
        {
            Catalog = catalog;
            Store = new GameStore(SnapshotTrees.Snapshot(0, root ?? Fx.Root()), new CompletionPump());
            Rules = new EventRules(catalog, new Calendar());
            Conditions = new ConditionEvaluator(catalog);
        }

        public static TransactionId Tx()
        {
            counter++;
            return new TransactionId("tx-" + counter);
        }

        /// <summary>Commits the success of the event under a fresh or the given transaction, with the given live positions.</summary>
        public Outcome<StateSnapshot> Run(CompiledEvent definition, WorldPlaces live = null, string transaction = null)
        {
            return Store.Commit(transaction == null ? Tx() : new TransactionId(transaction), s => Rules.Complete(s, definition, live ?? Nobody));
        }

        public Outcome<StateSnapshot> Run(string eventId, WorldPlaces live = null, string transaction = null)
        {
            return Run(Catalog.FindEvent(eventId), live, transaction);
        }
    }
}
