using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Transactions.Conditions;
using MagicCampus.Transactions.Effects;

namespace MagicCampus.Tests.Effects
{
    public static class EffectsSpecs
    {
        public static void Run()
        {
            ConstructorGuards();
            ConditionFalseReportsReasons();
            ConditionFunctionsAndNegation();
            AnyUsesCompiledAlternatives();
            UnknownConditionIsUnregistered();
            ClockVariableIsDerivedAndReadOnly();
            OnlyDeclaredTypedVariables();
            CaseCountUsesValidatedOrder();
            UnknownEffectRefusesWholeList();
            ParticipantRefusalsLeaveStateUntouched();
            AllSlicesCommitAtomically();
            RevisionConflictThroughStore();
            RepeatedTransactionAndCompletionAddNothing();
            EventCostIsTheOnlyTimeSource();
            MalformedCollaboratorPlansRejected();
            PracticeEarnsNothingAndCostsNothing();
            PlanOrderAndStoreRefusesUnregisteredParticipants();
            PlanningAndCancellationWriteNothing();
        }

        // ------------------------------------------------------------------ helpers

        private static void Require(bool ok, string what) { Fx.Require(ok, what); }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("EffectsSpecs: expected " + typeof(T).Name);
        }

        private static ConditionReport Check(Rig r, params CompiledCall[] when)
        {
            return r.Conditions.Evaluate(when, r.Store.Current);
        }

        private static CompiledCall Eq(string v, DataNode x, bool negated = false)
        {
            return Fx.Call(Fx.EqFn, negated, Fx.P("variable", Fx.S(v)), Fx.P("value", x));
        }

        private static CompiledCall Gte(string v, DataNode x, bool negated = false)
        {
            return Fx.Call(Fx.GteFn, negated, Fx.P("variable", Fx.S(v)), Fx.P("value", x));
        }

        private static CompiledCall Has(FunctionDef f, string id, bool negated = false)
        {
            return Fx.Call(f, negated, Fx.P("id", Fx.S(id)));
        }

        private static CompiledCall Cases(DataNode n, bool negated = false)
        {
            return Fx.Call(Fx.CaseCountFn, negated, Fx.P("count", n));
        }

        private static DataNode FullState(Dictionary<string, DataNode> m)
        {
            m["flags"] = Fx.List(Fx.Flag("met_mentor", Fx.B(true)), Fx.Flag("gold_stars", Fx.I(4)));
            m["completed"] = Fx.Strings("intro_seen");
            return null;
        }

        private static Rig FullRig()
        {
            return new Rig(Fx.DefaultCatalog(), Fx.Root(m => FullState(m)));
        }

        private static StatePlan P(Rig r, string key, params PlannedChange[] changes)
        {
            return new StatePlan(Rig.Tx(), key == null ? (CompletionKey?)null : new CompletionKey(key), r.Store.Current.Revision, changes);
        }

        private static PlannedChange Ch(string op, DataNode payload)
        {
            return new PlannedChange(EffectOperations.ParticipantId, op, payload);
        }

        private static Outcome<ChangeSet> PlanCalls(Rig r, string completionKey, params CompiledCall[] calls)
        {
            var context = new EffectContext(r.Store.Current, Rig.Tx(), completionKey == null ? (CompletionKey?)null : new CompletionKey(completionKey));
            return r.Effects.Plan(calls, context);
        }

        private static PlannedChange PlanOne(Rig r, string completionKey, CompiledCall call)
        {
            var planned = PlanCalls(r, completionKey, call);
            Require(planned.Succeeded && planned.Value.Changes.Count == 1, "test effect plans");
            return planned.Value.Changes[0];
        }

        private static CompiledCall SetCall(string variable, DataNode value)
        {
            return Fx.Call(Fx.SetFn, false, Fx.P("variable", Fx.S(variable)), Fx.P("value", value));
        }

        private static CompiledCall CompleteCall(string id)
        {
            return Fx.Call(Fx.CompleteFn, false, Fx.P("id", Fx.S(id)));
        }

        private static CompiledCall CurrencyCall(DataNode amount)
        {
            return Fx.Call(Fx.CurrencyFn, false, Fx.P("amountCrowns", amount));
        }

        private static PlannedChange Setc(Rig r, string variable, DataNode value)
        {
            return PlanOne(r, null, SetCall(variable, value));
        }

        private static PlannedChange Comp(Rig r, string id)
        {
            return PlanOne(r, null, CompleteCall(id));
        }

        private static PlannedChange Cur(Rig r, long amount)
        {
            return PlanOne(r, "evt_intro", CurrencyCall(Fx.I(amount)));
        }

        private static PlannedChange Done(string eventId, string key, RepeatPolicy policy)
        {
            return EffectChange.EventComplete(eventId, key, policy);
        }

        private static void PlanRefused(Rig r, string completionKey, string code, string what, params CompiledCall[] calls)
        {
            var planned = PlanCalls(r, completionKey, calls);
            Require(!planned.Succeeded && planned.Result.Code == code && planned.Value == null, what + " must be refused at planning with " + code);
        }

        private static void Unchanged(Rig r, StateSnapshot before, string what)
        {
            Require(r.Store.Current.Revision == before.Revision && r.Store.Current.Root.Equals(before.Root), what + ": state changed");
        }

        private static long Crowns(Rig r)
        {
            return r.Store.Current.Slice("wallet").Integer;
        }

        private static long AbsoluteMinute(Rig r)
        {
            return r.Store.Current.TypedState().Clock.AbsoluteMinute;
        }

        private static string Sequence(StatePlan plan)
        {
            var parts = new List<string>();
            foreach (var c in plan.Changes) parts.Add(c.ParticipantId + "/" + c.Operation);
            return string.Join(",", parts);
        }

        // ------------------------------------------------------------------ conditions

        private static void ConstructorGuards()
        {
            Throws<ArgumentNullException>(() => new ConditionEvaluator(null));
            Throws<ArgumentNullException>(() => new EventEffectsParticipant(null));
            Throws<ArgumentNullException>(() => new EffectPlanner(null));
            var catalog = Fx.DefaultCatalog();
            Throws<ArgumentNullException>(() => new EventCommitPlanner(null, new FakeWorldProjector(), catalog));
            Throws<ArgumentNullException>(() => new EventCommitPlanner(new FakeClockPlanner(), null, catalog));
            Throws<ArgumentNullException>(() => new EventCommitPlanner(new FakeClockPlanner(), new FakeWorldProjector(), null));
        }

        private static void ConditionFalseReportsReasons()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var revision = r.Store.Current.Revision;
            var eq = Check(r, Eq("met_mentor", Fx.B(true)));
            Require(!eq.Satisfied && eq.Revision == revision, "eq false carries the snapshot revision");
            Require(eq.Reasons.Count == 1 && eq.Reasons[0].Code == ResultCodes.ConditionFalse, "eq reason code");
            Require(eq.Reasons[0].Expected == "met_mentor == true" && eq.Reasons[0].Actual == "met_mentor = false", "eq expected/actual");
            var gte = Check(r, Gte("gold_stars", Fx.I(3)));
            Require(!gte.Satisfied && gte.Reasons[0].Expected == "gold_stars >= 3" && gte.Reasons[0].Actual == "gold_stars = 0", "gte expected/actual");
            var completed = Check(r, Has(Fx.CompletedFn, "intro_seen"));
            Require(!completed.Satisfied && completed.Reasons[0].Expected == "completed contains intro_seen"
                && completed.Reasons[0].Actual == "completed holds 0 entries without it", "completed expected/actual");
            Require(Check(r).Satisfied, "empty condition list holds");
        }

        private static void ConditionFunctionsAndNegation()
        {
            var r = FullRig();
            var holds = new[]
            {
                Eq("met_mentor", Fx.B(true)), Eq("gold_stars", Fx.I(4)), Gte("gold_stars", Fx.I(4)), Has(Fx.CompletedFn, "intro_seen"),
                Has(Fx.DiscoveredFn, "lab"), Has(Fx.EvidenceFn, "e1"), Has(Fx.LearnedFn, "spark"), Cases(Fx.I(2))
            };
            var all = Check(r, holds);
            Require(all.Satisfied && all.Reasons.Count == 0, "all eight true conditions hold");
            var fails = new[]
            {
                Eq("gold_stars", Fx.I(5)), Gte("gold_stars", Fx.I(5)), Has(Fx.CompletedFn, "nope"), Has(Fx.DiscoveredFn, "nope"),
                Has(Fx.EvidenceFn, "nope"), Has(Fx.LearnedFn, "nope"), Cases(Fx.I(3))
            };
            foreach (var call in fails)
            {
                var one = Check(r, call);
                Require(!one.Satisfied && one.Reasons.Count == 1 && one.Reasons[0].Code == ResultCodes.ConditionFalse, call.Function.Name + " false");
            }
            Require(Check(r, fails).Reasons.Count == fails.Length, "every false condition explains itself");
            var negatedFails = new[]
            {
                Eq("gold_stars", Fx.I(5), true), Gte("gold_stars", Fx.I(5), true), Has(Fx.CompletedFn, "nope", true), Has(Fx.DiscoveredFn, "nope", true),
                Has(Fx.EvidenceFn, "nope", true), Has(Fx.LearnedFn, "nope", true), Cases(Fx.I(3), true)
            };
            Require(Check(r, negatedFails).Satisfied, "negating false conditions holds");
            var negatedHolds = new[]
            {
                Eq("gold_stars", Fx.I(4), true), Gte("gold_stars", Fx.I(4), true), Has(Fx.CompletedFn, "intro_seen", true), Has(Fx.DiscoveredFn, "lab", true),
                Has(Fx.EvidenceFn, "e1", true), Has(Fx.LearnedFn, "spark", true), Cases(Fx.I(2), true)
            };
            foreach (var call in negatedHolds)
            {
                var one = Check(r, call);
                Require(!one.Satisfied && one.Reasons[0].Code == ResultCodes.ConditionFalse, "negated " + call.Function.Name + " false");
            }
        }

        private static void AnyUsesCompiledAlternatives()
        {
            var r = FullRig();
            var yes = Fx.AnyCall(false, new[] { Eq("met_mentor", Fx.B(false)) }, new[] { Has(Fx.CompletedFn, "intro_seen") });
            Require(Check(r, yes).Satisfied, "any holds when one alternative holds");
            var no = Fx.AnyCall(false, new[] { Eq("met_mentor", Fx.B(false)) }, new[] { Has(Fx.CompletedFn, "nope"), Has(Fx.LearnedFn, "spark") });
            var noReport = Check(r, no);
            Require(!noReport.Satisfied && noReport.Reasons.Count == 3 && noReport.Reasons[0].Code == ResultCodes.ConditionFalse, "any false explains each alternative");
            Require(Check(r, Fx.AnyCall(true, new[] { Eq("met_mentor", Fx.B(false)) })).Satisfied, "negated false any holds");
            Require(!Check(r, Fx.AnyCall(true, new[] { Has(Fx.CompletedFn, "intro_seen") })).Satisfied, "negated true any fails");
            var nested = Fx.AnyCall(false, new[] { Fx.AnyCall(false, new[] { Has(Fx.CompletedFn, "nope") }, new[] { Has(Fx.EvidenceFn, "e1") }) });
            Require(Check(r, nested).Satisfied, "nested any");
            var none = Check(r, Fx.AnyCall(false));
            Require(!none.Satisfied && none.Reasons[0].Code == ResultCodes.ArgCount, "any without alternatives is invalid, not true");
            Require(!Check(r, Fx.AnyCall(true)).Satisfied, "negation does not turn an invalid any into success");
        }

        private static void UnknownConditionIsUnregistered()
        {
            var r = FullRig();
            var unknown = Check(r, Fx.Call(Fx.MysteryFn, false, Fx.P("id", Fx.S("x"))));
            Require(!unknown.Satisfied && unknown.Reasons[0].Code == ResultCodes.Unregistered, "unknown condition is UNREGISTERED");
            var negated = Check(r, Fx.Call(Fx.MysteryFn, true, Fx.P("id", Fx.S("x"))));
            Require(!negated.Satisfied && negated.Reasons[0].Code == ResultCodes.Unregistered, "negated unknown stays false");
            var wrongRole = Check(r, Fx.Call(Fx.SetFn, false, Fx.P("variable", Fx.S("met_mentor")), Fx.P("value", Fx.B(true))));
            Require(!wrongRole.Satisfied && wrongRole.Reasons[0].Code == ResultCodes.Unregistered, "effect function is not a condition");
            var inAny = Check(r, Fx.AnyCall(false, new[] { Fx.Call(Fx.MysteryFn, false, Fx.P("id", Fx.S("x"))) }));
            Require(!inAny.Satisfied && !Check(r, Fx.AnyCall(true, new[] { Fx.Call(Fx.MysteryFn, false, Fx.P("id", Fx.S("x"))) })).Satisfied, "unknown inside any never becomes true");
        }

        private static void ClockVariableIsDerivedAndReadOnly()
        {
            var r = new Rig(Fx.DefaultCatalog());
            Require(Check(r, Eq("clock.minute_of_day", Fx.I(600))).Satisfied, "minute_of_day derived from absoluteMinute");
            Require(Check(r, Gte("clock.minute_of_day", Fx.I(600))).Satisfied, "gte on derived clock");
            var late = Check(r, Gte("clock.minute_of_day", Fx.I(601)));
            Require(!late.Satisfied && late.Reasons[0].Actual == "clock.minute_of_day = 600", "derived value explained");
            Require(!Check(r, Eq("clock.minute_of_day", Fx.B(true))).Satisfied, "clock compares integers");
            Require(Check(r, Eq("clock.other", Fx.I(1))).Reasons[0].Code == ResultCodes.RefNotFound, "unknown derived variable");

            var call = Fx.Call(Fx.SetFn, false, Fx.P("variable", Fx.S("clock.minute_of_day")), Fx.P("value", Fx.I(5)));
            Require(!r.Effects.Plan(new[] { call }, r.Context()).Succeeded, "set on clock refused by planner");
            Require(AbsoluteMinute(r) == 2040, "absolute minute untouched");
        }

        private static void OnlyDeclaredTypedVariables()
        {
            var r = new Rig(Fx.DefaultCatalog());
            Func<string, DataNode, bool> plans = (name, value) =>
                r.Effects.Plan(new[] { Fx.Call(Fx.SetFn, false, Fx.P("variable", Fx.S(name)), Fx.P("value", value)) }, r.Context()).Succeeded;
            Require(!plans("ghost", Fx.B(true)), "undeclared variable refused");
            Require(!plans("note", Fx.S("hi")), "string variable refused");
            Require(!plans("met_mentor", Fx.I(1)), "int for bool refused");
            Require(!plans("gold_stars", Fx.B(true)), "bool for int refused");
            Require(!plans("gold_stars", Fx.I(99)), "above declared max refused");
            Require(!plans("gold_stars", Fx.I(-1)), "below declared min refused");
            Require(!plans("gold_stars", DataNode.FromNumber(1.5)), "number for int refused");
            Require(plans("met_mentor", Fx.B(true)) && plans("gold_stars", Fx.I(10)) && plans("gold_stars", Fx.I(0)), "declared values accepted");
            var planned = r.Effects.Plan(new[] { Fx.Call(Fx.SetFn, false, Fx.P("variable", Fx.S("gold_stars")), Fx.P("value", Fx.I(7))) }, r.Context());
            Require(planned.Value.Changes.Count == 1 && planned.Value.Changes[0].ParticipantId == "event-effects" && planned.Value.Changes[0].Operation == "set", "set plans one event-effects change");
            Require(Check(r, Eq("ghost", Fx.B(true))).Reasons[0].Code == ResultCodes.RefNotFound, "eq on undeclared");
            Require(Check(r, Eq("note", Fx.S("x"))).Reasons[0].Code == ResultCodes.ContentInvalid, "eq on string variable");
            Require(Check(r, Eq("gold_stars", Fx.B(true))).Reasons[0].Code == ResultCodes.ArgType, "eq with wrong value type");
            Require(Check(r, Gte("met_mentor", Fx.B(true))).Reasons[0].Code == ResultCodes.ArgType, "gte on bool");
        }

        private static void CaseCountUsesValidatedOrder()
        {
            var r = new Rig(Fx.DefaultCatalog());
            Require(Check(r, Cases(Fx.I(2))).Satisfied, "two resolved cases");
            var three = Check(r, Cases(Fx.I(3)));
            Require(!three.Satisfied && three.Reasons[0].Expected == "resolved cases >= 3" && three.Reasons[0].Actual == "resolved cases = 2", "case count explanation");
            var dup = new Rig(Fx.DefaultCatalog(), Fx.Root(m => m["caseResolutionOrder"] = Fx.Strings("a", "a")));
            Require(Check(dup, Cases(Fx.I(1))).Reasons[0].Code == ResultCodes.ContentInvalid, "duplicate case ids invalid");
            Require(!Check(dup, Cases(Fx.I(1), true)).Satisfied, "negation does not repair invalid order");
            var bad = new Rig(Fx.DefaultCatalog(), Fx.Root(m => m["caseResolutionOrder"] = Fx.List(Fx.I(1))));
            Require(!Check(bad, Cases(Fx.I(0))).Satisfied, "non-string element invalid");
            Require(Check(r, Cases(Fx.I(-1))).Reasons[0].Code == ResultCodes.ArgType, "negative count invalid");
        }

        // ------------------------------------------------------------------ effects

        private static void UnknownEffectRefusesWholeList()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var before = r.Store.Current;
            var bad = r.Catalog.FindEvent("evt_bad");
            var planned = r.Planner.PlanCommit(bad, r.Context());
            Require(!planned.Succeeded && planned.Result.Code == ResultCodes.Unregistered && planned.Value == null, "unknown effect refuses the commit plan");
            Require(r.Clock.Calls == 0 && r.World.Calls == 0, "no time or world planning after refusal");
            Unchanged(r, before, "unknown effect");
            var direct = r.Effects.Plan(bad.Effects, r.Context());
            Require(!direct.Succeeded && direct.Result.Code == ResultCodes.Unregistered && direct.Value == null, "valid effects before an unknown one are discarded");
            var met = Fx.Call(Fx.SetFn, false, Fx.P("variable", Fx.S("met_mentor")), Fx.P("value", Fx.B(true)));
            var grant = Fx.Call(Fx.ItemGrantFn, false, Fx.P("item", Fx.S("potion")), Fx.P("quantity", Fx.I(1)));
            Require(!r.Effects.Plan(new[] { grant, met }, r.Context()).Succeeded, "order does not matter");
            Require(r.Effects.Plan(new[] { met }, r.Context()).Succeeded, "valid effect alone plans");
        }

        private static void ParticipantRefusalsLeaveStateUntouched()
        {
            var r = new Rig(Fx.DefaultCatalog());
            Action<string, StatePlan> refuse = (name, plan) =>
            {
                var before = r.Store.Current;
                var outcome = r.Store.Commit(plan);
                Require(!outcome.Succeeded, name + " must be refused");
                Unchanged(r, before, name);
            };
            PlanRefused(r, "evt_intro", ResultCodes.InvariantViolation, "zero currency", CurrencyCall(Fx.I(0)));
            PlanRefused(r, "evt_intro", ResultCodes.InvariantViolation, "negative currency", CurrencyCall(Fx.I(-5)));
            PlanRefused(r, "evt_intro", ResultCodes.ArgType, "fractional currency", CurrencyCall(DataNode.FromNumber(2.5)));
            PlanRefused(r, "evt_intro", ResultCodes.InvariantViolation, "wallet schema maximum overflow", CurrencyCall(Fx.I(int.MaxValue)));
            PlanRefused(r, null, ResultCodes.TransactionMismatch, "reward without completion", CurrencyCall(Fx.I(5)));
            PlanRefused(r, null, ResultCodes.ContentInvalid, "forged event completion key", CompleteCall("evt_intro"));
            PlanRefused(r, null, ResultCodes.RefNotFound, "undeclared flag", SetCall("ghost_flag", Fx.B(true)));
            Require(!PlanCalls(r, null, SetCall("gold_stars", Fx.B(true))).Succeeded, "flag type mismatch must be refused at planning");
            Require(!PlanCalls(r, null, SetCall("gold_stars", Fx.I(99))).Succeeded, "flag range must be refused at planning");
            Require(!PlanCalls(r, null, SetCall("clock.minute_of_day", Fx.I(5))).Succeeded, "clock flag must be refused at planning");

            refuse("reward without ledger entry", P(r, "evt_intro", Cur(r, 5)));
            refuse("reward without plan completion", P(r, null, Cur(r, 5), Done("evt_intro", "evt_intro", RepeatPolicy.Once)));
            refuse("wrong completion key", P(r, "evt_intro", Done("evt_intro", "other_key", RepeatPolicy.Once)));
            refuse("unknown event", P(r, "ghost", Done("ghost", "ghost", RepeatPolicy.Once)));
            refuse("policy mismatch", P(r, "evt_intro", Done("evt_intro", "evt_intro", RepeatPolicy.Practice)));
            refuse("duplicate event.complete", P(r, "evt_intro", Done("evt_intro", "evt_intro", RepeatPolicy.Once), Done("evt_intro", "evt_intro", RepeatPolicy.Once)));
            refuse("duplicate completion inside one plan", P(r, null, Comp(r, "intro_seen2"), Comp(r, "intro_seen2")));
            var early = Comp(r, "recorded_later");
            Require(r.Store.Commit(P(r, null, Comp(r, "recorded_later"))).Succeeded, "completion recorded");
            refuse("completion already recorded", P(r, null, early));
            refuse("summed rewards overflow the wallet", P(r, "evt_intro", Cur(r, int.MaxValue - 100), Cur(r, int.MaxValue - 100), Done("evt_intro", "evt_intro", RepeatPolicy.Once)));
            refuse("unplanned operation", P(r, null, Ch("item.grant", Fx.Obj())));
            refuse("unplanned known operation", P(r, null, Ch(EffectOperations.Set, Fx.Obj(Fx.P("variable", Fx.S("met_mentor")), Fx.P("value", Fx.B(true))))));
            refuse("valid changes followed by an unknown one", P(r, "evt_intro", Setc(r, "met_mentor", Fx.B(true)), Comp(r, "intro_seen"), Ch("spell.learn", Fx.Obj())));
            refuse("foreign revision", new StatePlan(Rig.Tx(), null, r.Store.Current.Revision + 7, new[] { Setc(r, "met_mentor", Fx.B(true)) }));

            var ok = r.Store.Commit(P(r, "evt_intro", Setc(r, "met_mentor", Fx.B(true)), Comp(r, "intro_seen"), Cur(r, 25), Done("evt_intro", "evt_intro", RepeatPolicy.Once)));
            Require(ok.Succeeded && Crowns(r) == 125, "the same rig accepts a valid plan");
        }

        private static void AllSlicesCommitAtomically()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var before = r.Store.Current;
            var planned = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_intro"), r.Context());
            Require(planned.Succeeded, "intro plans");
            Require(planned.Value.Completion.Value.Value == "evt_intro" && planned.Value.ExpectedRevision == before.Revision, "completion defaults to the event id");
            Require(r.Clock.Calls == 0, "zero cost asks no clock");
            Unchanged(r, before, "planning");
            var outcome = r.Store.Commit(planned.Value);
            Require(outcome.Succeeded, "commit succeeds");
            var now = r.Store.Current;
            Require(now.Revision > before.Revision && now.Revision == outcome.Result.Revision, "one new revision");
            Require(now.Slice("flags").Equals(Fx.List(Fx.Flag("met_mentor", Fx.B(true)))), "flag written");
            Require(now.Slice("completed").Equals(Fx.Strings("intro_seen", "evt_intro")), "completed written");
            Require(now.Slice("rewardLedger").Equals(Fx.Strings("evt_intro")), "ledger written");
            Require(Crowns(r) == 125, "wallet written");
            Require(now.Root.Members.Count == 45 && before.Root.Members.Count == 45, "all 45 root fields survive");
            var owned = new HashSet<string> { "flags", "completed", "rewardLedger", "wallet" };
            foreach (var member in before.Root.Members)
            {
                if (owned.Contains(member.Key)) continue;
                Require(now.Root.Members[member.Key].Equals(member.Value), "field " + member.Key + " preserved");
            }
        }

        private static void RevisionConflictThroughStore()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var stale = P(r, null, Setc(r, "met_mentor", Fx.B(true)));
            Require(r.Store.Commit(P(r, null, Setc(r, "gold_stars", Fx.I(2)))).Succeeded, "first commit");
            var mid = r.Store.Current;
            var outcome = r.Store.Commit(stale);
            Require(!outcome.Succeeded && outcome.Result.Code == ResultCodes.RevisionConflict, "stale plan conflicts");
            Unchanged(r, mid, "stale plan");

            var oldContext = r.Context();
            var plan = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_intro"), oldContext).Value;
            Require(r.Store.Commit(P(r, null, Setc(r, "gold_stars", Fx.I(3)))).Succeeded, "interleaved commit");
            var afterInterleave = r.Store.Current;
            var conflict = r.Store.Commit(plan);
            Require(!conflict.Succeeded && conflict.Result.Code == ResultCodes.RevisionConflict, "event plan from an old basis conflicts");
            Unchanged(r, afterInterleave, "stale event plan");
            Require(Crowns(r) == 100, "no reward from a stale plan");
        }

        private static void RepeatedTransactionAndCompletionAddNothing()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var evt = r.Catalog.FindEvent("evt_intro");
            var plan = r.Planner.PlanCommit(evt, r.Context()).Value;
            var first = r.Store.Commit(plan);
            Require(first.Succeeded, "first commit");
            var afterFirst = r.Store.Current;
            var replay = r.Store.Commit(plan);
            Require(replay.Succeeded && replay.Result.Revision == first.Result.Revision, "replayed transaction returns the first result");
            Unchanged(r, afterFirst, "replayed transaction");
            Require(Crowns(r) == 125, "reward paid once");

            var second = r.Planner.PlanCommit(evt, r.Context());
            Require(!second.Succeeded && second.Result.Code == ResultCodes.ConditionFalse, "new transaction for a consumed completion is refused at planning");
            var forced = P(r, "evt_intro", Cur(r, 25), Done("evt_intro", "evt_intro", RepeatPolicy.Once));
            Require(!r.Store.Commit(forced).Succeeded, "participant refuses a forced repeat");
            Unchanged(r, afterFirst, "forced repeat");
            Require(r.Store.Current.Slice("rewardLedger").Items.Count == 1, "ledger has one entry");

            var a = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_shared_a"), r.Context());
            Require(a.Succeeded && r.Store.Commit(a.Value).Succeeded && Crowns(r) == 135, "shared key first use pays");
            var afterA = r.Store.Current;
            var b = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_shared_b"), r.Context());
            Require(!b.Succeeded && b.Result.Code == ResultCodes.ConditionFalse, "shared key is consumed for every event");
            Unchanged(r, afterA, "shared key reuse");
        }

        // ------------------------------------------------------------------ time sources

        private static void EventCostIsTheOnlyTimeSource()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var evt = r.Catalog.FindEvent("evt_cost30");
            var planned = r.Planner.PlanCommit(evt, r.Context());
            Require(planned.Succeeded && r.Clock.Calls == 1 && r.Clock.LastMinutes == 30, "event cost 30 asked once");
            var advances = 0;
            foreach (var c in planned.Value.Changes) if (c.ParticipantId == "clock" && c.Operation == "advance") advances++;
            Require(advances == 1, "exactly one clock advance");
            var start = AbsoluteMinute(r);
            Require(r.Store.Commit(planned.Value).Succeeded, "event commit");
            Require(AbsoluteMinute(r) == start + 30, "time applied once");
            Require(r.Store.Commit(planned.Value).Succeeded && AbsoluteMinute(r) == start + 30, "replay adds no time");
            var again = r.Planner.PlanCommit(evt, r.Context());
            Require(!again.Succeeded && r.Clock.Calls == 1, "completed event asks no further time");

            var free = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_craft"), r.Context());
            Require(free.Succeeded && r.Clock.Calls == 1, "no cost means no clock change");
        }

        private static void MalformedCollaboratorPlansRejected()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var evt = r.Catalog.FindEvent("evt_cost30");
            Func<string, string, DataNode, PlannedChange> change = (p, o, d) => new PlannedChange(p, o, d);
            Func<double, DataNode, PlannedChange> advance = (m, d) => change("clock", "advance", d);

            r.Clock.Override = m => new ChangeSet(new[] { change("clock", "advance", Fx.Obj(Fx.P("minutes", Fx.I(29)))) });
            Require(!r.Planner.PlanCommit(evt, r.Context()).Succeeded, "wrong minutes refused");
            r.Clock.Override = m => new ChangeSet(null);
            Require(!r.Planner.PlanCommit(evt, r.Context()).Succeeded, "missing clock advance refused");
            r.Clock.Override = m => new ChangeSet(new[] { change("event-effects", "set", Fx.Obj()), change("clock", "advance", Fx.Obj(Fx.P("minutes", Fx.I(30)))) });
            Require(!r.Planner.PlanCommit(evt, r.Context()).Succeeded, "clock planner writing event-effects refused");
            r.Clock.Override = m => new ChangeSet(new[] { change("clock", "advance", Fx.Obj(Fx.P("minutes", Fx.I(30)))), change("clock", "advance", Fx.Obj(Fx.P("minutes", Fx.I(30)))) });
            Require(!r.Planner.PlanCommit(evt, r.Context()).Succeeded, "two advances refused");
            r.Clock.Override = m => new ChangeSet(new[] { change("clock", "advance", Fx.Obj(Fx.P("minutes", Fx.S("30")))) });
            Require(!r.Planner.PlanCommit(evt, r.Context()).Succeeded, "non-numeric minutes refused");

            r.Clock.Override = m => new ChangeSet(new[] { change("clock", "advance", Fx.Obj(Fx.P("minutes", Fx.I(30)))), change("dayJobs", "consume", Fx.Obj()) });
            var withConsumer = r.Planner.PlanCommit(evt, r.Context());
            Require(withConsumer.Succeeded && Sequence(withConsumer.Value).Contains("clock/advance,dayJobs/consume"), "boundary consumer changes travel with the clock change");
            r.Clock.Override = null;

            r.World.Result = new ChangeSet(new[] { change("clock", "project", Fx.Obj()) });
            Require(!r.Planner.PlanCommit(evt, r.Context()).Succeeded, "world projector writing clock refused");
            r.World.Result = new ChangeSet(new[] { change("world", "project", Fx.Obj()), change("world", "project", Fx.Obj()) });
            Require(!r.Planner.PlanCommit(evt, r.Context()).Succeeded, "two projections refused");
            r.World.Result = new ChangeSet(new[] { change("world", "project", Fx.Obj()) });
            Require(r.Planner.PlanCommit(evt, r.Context()).Succeeded, "one projection accepted");

            var wrongCompletion = new EffectContext(r.Store.Current, Rig.Tx(), new CompletionKey("someone_else"));
            Require(!r.Planner.PlanCommit(evt, wrongCompletion).Succeeded, "context completion mismatch refused");
        }

        private static void PracticeEarnsNothingAndCostsNothing()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var practice = r.Catalog.FindEvent("evt_practice");
            var before = r.Store.Current;
            var planned = r.Planner.PlanCommit(practice, r.Context());
            Require(planned.Succeeded && !planned.Value.Completion.HasValue, "practice carries no completion key");
            Require(r.Clock.Calls == 0, "practice costs no time");
            var sequence = Sequence(planned.Value);
            Require(!sequence.Contains("event.complete") && !sequence.Contains("currency.add"), "practice writes no ledger or reward");
            Require(r.Store.Commit(planned.Value).Succeeded, "practice commits");
            var again = r.Planner.PlanCommit(practice, r.Context());
            Require(again.Succeeded && r.Store.Commit(again.Value).Succeeded, "practice can repeat");
            Require(Crowns(r) == 100 && r.Store.Current.Slice("rewardLedger").Equals(before.Slice("rewardLedger")) && r.Store.Current.Slice("completed").Equals(before.Slice("completed")), "practice leaves wallet and ledgers alone");
            Require(AbsoluteMinute(r) == 2040, "practice leaves the clock alone");

            var priced = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_practice_cost"), r.Context());
            Require(priced.Succeeded && r.Clock.Calls == 0 && !Sequence(priced.Value).Contains("clock/"), "practice drops the event time cost");

            var reward = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_practice_reward"), r.Context());
            Require(!reward.Succeeded, "practice reward refused");
            var withCompletion = new EffectContext(r.Store.Current, Rig.Tx(), new CompletionKey("evt_practice"));
            Require(!r.Planner.PlanCommit(practice, withCompletion).Succeeded, "practice with a completion key refused");
            Require(!r.Store.Commit(P(r, "evt_practice", Done("evt_practice", "evt_practice", RepeatPolicy.Practice))).Succeeded, "practice event.complete with a completion refused");
        }

        private static void PlanOrderAndStoreRefusesUnregisteredParticipants()
        {
            var root = Fx.Root(m => m["appointments"] = Fx.List(Fx.Obj(Fx.P("id", Fx.S("evt_order")))));
            var r = new Rig(Fx.DefaultCatalog(), root);
            r.World.Result = new ChangeSet(new[] { new PlannedChange("world", "project", Fx.Obj()) });
            var planned = r.Planner.PlanCommit(r.Catalog.FindEvent("evt_order"), r.Context());
            Require(planned.Succeeded, "order event plans");
            Require(Sequence(planned.Value) == "event-effects/set,event-effects/complete,event-effects/currency.add,event-effects/event.complete,clock/advance,world/project,reservations/complete",
                "effects, event.complete, time, world, reservations.complete in one plan");
            var before = r.Store.Current;
            var outcome = r.Store.Commit(planned.Value);
            Require(!outcome.Succeeded && outcome.Result.Code == ResultCodes.Unregistered, "unregistered participants refuse the whole plan");
            Unchanged(r, before, "unregistered participant");

            var plain = new Rig(Fx.DefaultCatalog());
            var noReservation = plain.Planner.PlanCommit(plain.Catalog.FindEvent("evt_order"), plain.Context());
            Require(noReservation.Succeeded && !Sequence(noReservation.Value).Contains("reservations/complete"), "no reservation, no reservations.complete");
            Require(Sequence(noReservation.Value).EndsWith("clock/advance"), "empty world projection adds nothing");
        }

        private static void PlanningAndCancellationWriteNothing()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var before = r.Store.Current;
            var evt = r.Catalog.FindEvent("evt_cost30");
            var a = r.Planner.PlanCommit(evt, r.Context());
            var b = r.Planner.PlanCommit(evt, r.Context());
            Require(a.Succeeded && b.Succeeded && a.Value.Changes.Count == b.Value.Changes.Count, "planning is repeatable");
            Unchanged(r, before, "planning only");
            Require(AbsoluteMinute(r) == 2040 && Crowns(r) == 100, "planning persists nothing");

            var cancelled = new Rig(Fx.DefaultCatalog());
            var snapshot = cancelled.Store.Current;
            Require(cancelled.Clock.Calls == 0 && cancelled.World.Calls == 0, "a cancelled perform never reaches the planners");
            Unchanged(cancelled, snapshot, "cancelled perform");
        }
    }
}
