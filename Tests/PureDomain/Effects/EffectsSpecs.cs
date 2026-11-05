using System;
using System.Collections.Generic;
using System.Linq;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.State;
using MagicCampus.Tests.PureDomain.Support;
using MagicCampus.Timekeeping;
using MagicCampus.Transactions;
using MagicCampus.Transactions.Conditions;

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
            EventRefusalsLeaveStateUntouched();
            AllSlicesCommitAtomically();
            RepeatedTransactionAndCompletionAddNothing();
            EventCostIsTheOnlyTimeSource();
            PracticeEarnsNothingAndCostsNothing();
            OneCommitWritesEffectsTimeWorldAndReservation();
            TransitionsWriteNothingUntilCommitted();
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

        private static CompiledCall Met()
        {
            return SetCall("met_mentor", Fx.B(true));
        }

        private static int cases;

        /// <summary>Adds an event with these effects to the rig catalog and returns it.</summary>
        private static CompiledEvent Ev(Rig r, RepeatPolicy repeat, int cost, params CompiledCall[] effects)
        {
            var ev = Fx.Event("evt_case_" + (++cases), repeat, null, cost, effects);
            r.Catalog.EventList.Add(ev);
            return ev;
        }

        private static Outcome<EventOutcome> Parse(Rig r, params CompiledCall[] effects)
        {
            return EventOutcome.Parse(r.Catalog, Fx.Event("evt_parse", RepeatPolicy.Once, null, 0, effects));
        }

        private static void Refused(Rig r, CompiledEvent ev, string code, string what)
        {
            var before = r.Store.Current;
            var outcome = r.Run(ev);
            Require(!outcome.Succeeded && outcome.Result.Code == code && outcome.Value == null, what + " must be refused with " + code + ", got " + outcome.Result.Code);
            Unchanged(r, before, what);
        }

        private static void Unchanged(Rig r, StateSnapshot before, string what)
        {
            Require(r.Store.Current.Revision == before.Revision && r.Store.Current.Tree().Equals(before.Tree()), what + ": state changed");
        }

        /// <summary>Top-level save fields whose value differs between two snapshots.</summary>
        private static string Changed(StateSnapshot before, StateSnapshot after)
        {
            var a = before.Tree(); var b = after.Tree();
            return string.Join(",", a.Members.Keys.Where(k => !a.Members[k].Equals(b.Members[k])).OrderBy(k => k, StringComparer.Ordinal));
        }

        private static long Crowns(Rig r)
        {
            return r.Store.Current.Slice("wallet").Integer;
        }

        private static long AbsoluteMinute(Rig r)
        {
            return r.Store.Current.State.Clock.AbsoluteMinute;
        }

        // ------------------------------------------------------------------ conditions

        private static void ConstructorGuards()
        {
            var catalog = Fx.DefaultCatalog();
            Throws<ArgumentNullException>(() => new ConditionEvaluator(null));
            Throws<ArgumentNullException>(() => new EventRules(null, new Calendar()));
            Throws<ArgumentNullException>(() => new EventRules(catalog, null));
            Throws<ArgumentNullException>(() => EventOutcome.Parse(null, catalog.FindEvent("evt_intro")));
            Throws<ArgumentNullException>(() => EventOutcome.Parse(catalog, null));
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

            Require(!Parse(r, SetCall("clock.minute_of_day", Fx.I(5))).Succeeded, "set on clock refused by the rules");
            Require(AbsoluteMinute(r) == 2040, "absolute minute untouched");
        }

        private static void OnlyDeclaredTypedVariables()
        {
            var r = new Rig(Fx.DefaultCatalog());
            Func<string, DataNode, bool> parses = (name, value) => Parse(r, SetCall(name, value)).Succeeded;
            Require(!parses("ghost", Fx.B(true)), "undeclared variable refused");
            Require(!parses("note", Fx.S("hi")), "string variable refused");
            Require(!parses("met_mentor", Fx.I(1)), "int for bool refused");
            Require(!parses("gold_stars", Fx.B(true)), "bool for int refused");
            Require(!parses("gold_stars", Fx.I(99)), "above declared max refused");
            Require(!parses("gold_stars", Fx.I(-1)), "below declared min refused");
            Require(!parses("gold_stars", DataNode.FromNumber(1.5)), "number for int refused");
            Require(parses("met_mentor", Fx.B(true)) && parses("gold_stars", Fx.I(10)) && parses("gold_stars", Fx.I(0)), "declared values accepted");
            var parsed = Parse(r, SetCall("gold_stars", Fx.I(7)));
            Require(parsed.Value.Sets.Count == 1 && parsed.Value.Sets[0].Key == "gold_stars" && parsed.Value.Sets[0].Value.Equals(FlagValue.Of(7)), "set reads one typed flag value");
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
            Require(SaveDocument.Read(Fx.Root(m => m["caseResolutionOrder"] = Fx.Strings("a", "a"))).Result.Code == ResultCodes.ContentInvalid, "duplicate case ids cannot be held in a state");
            Require(SaveDocument.Read(Fx.Root(m => m["caseResolutionOrder"] = Fx.List(Fx.I(1)))).Result.Code == ResultCodes.ContentInvalid, "a non-string case id cannot be held in a state");
            Require(Check(r, Cases(Fx.I(-1))).Reasons[0].Code == ResultCodes.ArgType, "negative count invalid");
        }


        // ------------------------------------------------------------------ effects

        private static void UnknownEffectRefusesWholeList()
        {
            var r = new Rig(Fx.DefaultCatalog());
            Refused(r, r.Catalog.FindEvent("evt_bad"), ResultCodes.Unregistered, "unknown effect refuses the whole event");
            var grant = Fx.Call(Fx.ItemGrantFn, false, Fx.P("item", Fx.S("potion")), Fx.P("quantity", Fx.I(1)));
            var direct = Parse(r, Met(), grant);
            Require(!direct.Succeeded && direct.Result.Code == ResultCodes.Unregistered && direct.Value == null, "valid effects before an unknown one are discarded");
            Require(Parse(r, grant, Met()).Result.Code == ResultCodes.Unregistered, "order does not matter");
            Require(Parse(r, grant, CurrencyCall(Fx.I(0))).Result.Code == ResultCodes.Unregistered, "an unknown effect outranks other problems");
            Require(Parse(r, Met()).Succeeded, "valid effect alone parses");
        }

        private static void EventRefusalsLeaveStateUntouched()
        {
            var r = new Rig(Fx.DefaultCatalog());
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CurrencyCall(Fx.I(0))), ResultCodes.InvariantViolation, "zero currency");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CurrencyCall(Fx.I(-5))), ResultCodes.InvariantViolation, "negative currency");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CurrencyCall(DataNode.FromNumber(2.5))), ResultCodes.ArgType, "fractional currency");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CurrencyCall(Fx.I(int.MaxValue))), ResultCodes.InvariantViolation, "wallet schema maximum overflow");
            Refused(r, Ev(r, RepeatPolicy.Practice, 0, CurrencyCall(Fx.I(5))), ResultCodes.TransactionMismatch, "practice reward");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CompleteCall("evt_intro")), ResultCodes.ContentInvalid, "forged event completion key");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, SetCall("ghost_flag", Fx.B(true))), ResultCodes.RefNotFound, "undeclared flag");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, SetCall("gold_stars", Fx.B(true))), ResultCodes.ArgType, "flag type mismatch");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, SetCall("gold_stars", Fx.I(99))), ResultCodes.InvariantViolation, "flag range");
            Require(!r.Run(Ev(r, RepeatPolicy.Once, 0, SetCall("clock.minute_of_day", Fx.I(5)))).Succeeded, "clock flag refused");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, Fx.Call(Fx.SetFn, true, Fx.P("variable", Fx.S("met_mentor")), Fx.P("value", Fx.B(true)))), ResultCodes.ContentInvalid, "negated effect");
            var withAlternatives = new CompiledCall(Fx.SetFn, 1, "", false, new[] { Fx.P("variable", Fx.S("met_mentor")), Fx.P("value", Fx.B(true)) },
                new IReadOnlyList<CompiledCall>[] { new[] { Met() } }, new SourceLocation("test.json", "/events/0", "evt", "evt#1", 0));
            Refused(r, Ev(r, RepeatPolicy.Once, 0, withAlternatives), ResultCodes.ContentInvalid, "effect with alternatives");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CurrencyCall(Fx.I((long)int.MaxValue + 1))), ResultCodes.InvariantViolation, "amount above the wallet maximum");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, Fx.Call(Fx.SetFn, false, Fx.P("variable", Fx.S("met_mentor")))), ResultCodes.ArgCount, "set without a value");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, Fx.Call(Fx.CompleteFn, false, Fx.P("id", Fx.I(3)))), ResultCodes.ArgType, "complete with a number");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, Fx.Call(Fx.CurrencyFn, false)), ResultCodes.ArgCount, "currency.add without an amount");

            Refused(r, Ev(r, RepeatPolicy.Once, 0, CompleteCall("intro_seen2"), CompleteCall("intro_seen2")), ResultCodes.ConditionFalse, "duplicate completion inside one event");
            Require(r.Run(Ev(r, RepeatPolicy.Once, 0, CompleteCall("recorded_later"))).Succeeded, "completion recorded");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CompleteCall("recorded_later")), ResultCodes.ConditionFalse, "completion already recorded");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, CurrencyCall(Fx.I(int.MaxValue - 100)), CurrencyCall(Fx.I(int.MaxValue - 100))), ResultCodes.InvariantViolation, "summed rewards overflow the wallet");
            Refused(r, Ev(r, RepeatPolicy.Once, 0, Met(), CompleteCall("intro_seen"), Fx.Call(Fx.ItemGrantFn, false, Fx.P("item", Fx.S("potion")), Fx.P("quantity", Fx.I(1)))), ResultCodes.Unregistered, "valid effects followed by an unknown one");
            Refused(r, Fx.Event("ghost", RepeatPolicy.Once, null, 0), ResultCodes.RefNotFound, "event not in the catalog");

            var keyed = Fx.Event("evt_practice_keyed", RepeatPolicy.Practice, "evt_practice_keyed", 0, Met());
            r.Catalog.EventList.Add(keyed);
            Refused(r, keyed, ResultCodes.TransactionMismatch, "practice with a completion key");

            var ok = r.Run("evt_intro");
            Require(ok.Succeeded && Crowns(r) == 125, "the same rig accepts a valid event");
        }

        private static void AllSlicesCommitAtomically()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var before = r.Store.Current;
            var outcome = r.Run("evt_intro");
            Require(outcome.Succeeded, "commit succeeds");
            var now = r.Store.Current;
            Require(now.Revision == before.Revision + 1 && now.Revision == outcome.Result.Revision, "one new revision");
            Require(now.Slice("flags").Equals(Fx.List(Fx.Flag("met_mentor", Fx.B(true)))), "flag written");
            Require(now.Slice("completed").Equals(Fx.Strings("intro_seen", "evt_intro")), "completed written, completion defaults to the event id");
            Require(now.Slice("rewardLedger").Equals(Fx.Strings("evt_intro")), "ledger written");
            Require(Crowns(r) == 125, "wallet written");
            Require(now.Tree().Members.Count == 45 && before.Tree().Members.Count == 45, "all 45 root fields survive");
            Require(Changed(before, now) == "completed,flags,rewardLedger,wallet", "only the effect fields change, got " + Changed(before, now));
        }

        private static void RepeatedTransactionAndCompletionAddNothing()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var intro = r.Catalog.FindEvent("evt_intro");
            var first = r.Run(intro, null, "tx-intro");
            Require(first.Succeeded, "first commit");
            var afterFirst = r.Store.Current;
            var resubmitted = r.Run(intro, null, "tx-intro");
            Require(!resubmitted.Succeeded && resubmitted.Result.Code == ResultCodes.ConditionFalse, "a resubmitted transaction is refused");
            Unchanged(r, afterFirst, "resubmitted transaction");
            Require(Crowns(r) == 125, "reward paid once");

            Require(EventRules.IsSpent(r.Store.Current.State, intro), "the claimed once event is spent");
            var second = r.Run(intro);
            Require(!second.Succeeded && second.Result.Code == ResultCodes.ConditionFalse, "new transaction for a consumed completion is refused");
            Unchanged(r, afterFirst, "repeat");
            Require(r.Store.Current.Slice("rewardLedger").Items.Count == 1, "ledger has one entry");

            Require(r.Run("evt_shared_a").Succeeded && Crowns(r) == 135, "shared key first use pays");
            var afterA = r.Store.Current;
            var b = r.Run("evt_shared_b");
            Require(!b.Succeeded && b.Result.Code == ResultCodes.ConditionFalse, "shared key is consumed for every event");
            Unchanged(r, afterA, "shared key reuse");

            var ledgerOnly = new Rig(Fx.DefaultCatalog(), Fx.Root(m => m["rewardLedger"] = Fx.Strings("shared_key")));
            Require(EventRules.IsSpent(ledgerOnly.Store.Current.State, ledgerOnly.Catalog.FindEvent("evt_shared_a")), "a key only in rewardLedger is spent");
            var before = ledgerOnly.Store.Current;
            Require(ledgerOnly.Run("evt_shared_a").Result.Code == ResultCodes.ConditionFalse, "a key only in rewardLedger is not paid again");
            Unchanged(ledgerOnly, before, "rewardLedger-only key");

            var spentBad = new Rig(Fx.DefaultCatalog(), Fx.Root(m => m["completed"] = Fx.Strings("evt_bad")));
            Require(spentBad.Run("evt_bad").Result.Code == ResultCodes.ConditionFalse, "a spent once event is refused as spent before its effects are read");
        }

        // ------------------------------------------------------------------ time sources

        private static void EventCostIsTheOnlyTimeSource()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var start = AbsoluteMinute(r);
            var before = r.Store.Current;
            Require(r.Run("evt_cost30", null, "tx-cost").Succeeded, "event commit");
            Require(AbsoluteMinute(r) == start + 30, "time applied once");
            Require(Changed(before, r.Store.Current) == "clock,completed,rewardLedger", "the cost rides in the event commit, got " + Changed(before, r.Store.Current));
            Require(!r.Run("evt_cost30", null, "tx-cost").Succeeded && AbsoluteMinute(r) == start + 30, "a resubmitted transaction is refused and adds no time");
            var again = r.Run("evt_cost30");
            Require(!again.Succeeded && AbsoluteMinute(r) == start + 30, "completed event adds no further time");

            Require(r.Run("evt_craft").Succeeded && AbsoluteMinute(r) == start + 30, "no cost means no clock change");
            Refused(r, Ev(r, RepeatPolicy.Once, 6000000), ResultCodes.ContentInvalid, "a cost past the per-advance limit");

            var late = new Rig(Fx.DefaultCatalog(), Fx.Root(m => m["clock"] = Fx.Obj(Fx.P("absoluteMinute", Fx.I(2870)), Fx.P("fraction", DataNode.FromNumber(0.5)))));
            Require(late.Run("evt_cost30").Succeeded, "cost across midnight");
            var clock = late.Store.Current.State.Clock;
            Require(clock.AbsoluteMinute == 2900 && clock.DayJobs.Items.SequenceEqual(new[] { "boundary.day_d2" }) && clock.ReturnPending, "the crossed midnight is recorded with the cost");
        }

        private static void PracticeEarnsNothingAndCostsNothing()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var practice = r.Catalog.FindEvent("evt_practice");
            var parsed = EventOutcome.Parse(r.Catalog, practice).Value;
            Require(!parsed.Claim.HasValue && parsed.Crowns == 0 && parsed.Minutes == 0 && parsed.Sets.Count == 1, "practice parses to flags only: no claim, no crowns, no minutes");
            var before = r.Store.Current;
            Require(!EventRules.IsSpent(before.State, practice), "practice is never spent");
            Require(r.Run(practice).Succeeded, "practice commits");
            Require(r.Run(practice).Succeeded, "practice can repeat");
            Require(!EventRules.IsSpent(r.Store.Current.State, practice), "practice stays unspent after it ran");
            Require(Crowns(r) == 100 && r.Store.Current.Slice("rewardLedger").Equals(before.Slice("rewardLedger")) && r.Store.Current.Slice("completed").Equals(before.Slice("completed")), "practice leaves wallet and ledgers alone");
            Require(AbsoluteMinute(r) == 2040, "practice leaves the clock alone");
            Require(Changed(before, r.Store.Current) == "flags", "practice writes flags only, got " + Changed(before, r.Store.Current));

            var priced = r.Catalog.FindEvent("evt_practice_cost");
            Require(EventOutcome.Parse(r.Catalog, priced).Value.Minutes == 0, "practice drops the event time cost");
            Require(r.Run(priced).Succeeded && AbsoluteMinute(r) == 2040, "a priced practice run leaves the clock alone");

            Refused(r, r.Catalog.FindEvent("evt_practice_reward"), ResultCodes.TransactionMismatch, "practice reward");

            var reserved = new Rig(Fx.DefaultCatalog(), Fx.Root(m => m["appointments"] = Fx.List(Appointment("evt_practice", 5))));
            Refused(reserved, reserved.Catalog.FindEvent("evt_practice"), ResultCodes.TransactionMismatch, "practice run with a reservation row (decision D4)");
            Require(reserved.Store.Current.State.Appointments.Find("evt_practice") != null, "the reservation row stays");
        }

        private static DataNode Appointment(string id, long at)
        {
            return Fx.Obj(Fx.P("id", Fx.S(id)), Fx.P("status", Fx.S("Accepted")), Fx.P("acceptedAt", Fx.I(at)));
        }

        private static void OneCommitWritesEffectsTimeWorldAndReservation()
        {
            var root = Fx.Root(m => m["appointments"] = Fx.List(Appointment("evt_order", 5), Appointment("evt_other", 6)));
            var r = new Rig(Fx.DefaultCatalog(), root);
            var before = r.Store.Current;
            var live = new WorldPlaces(new Position("map.b", 3.0, 4.5), new[] { new KeyValuePair<string, Position>("npc.x", new Position("map.b", 1.5, 1.5)) });
            var outcome = r.Run("evt_order", live);
            Require(outcome.Succeeded && r.Store.Current.Revision == before.Revision + 1, "one commit, one revision");
            var s = r.Store.Current.State;
            Require(s.Progress.Flags.TryGet("met_mentor", out var met) && met.Bool, "flag");
            Require(s.Progress.Completed.Items.SequenceEqual(new[] { "order_seen", "evt_order" }) && s.Progress.RewardLedger.Items.SequenceEqual(new[] { "evt_order" }), "complete id, then the claim in both ledgers");
            Require(s.Progress.Wallet.Value == 105 && s.Clock.AbsoluteMinute == 2070, "reward and time cost");
            Require(s.World.Player.MapId == "map.b" && s.World.Player.X.Value == 3.0 && s.World.Npcs.Any(n => n.NpcId == "npc.x"), "live positions projected");
            Require(string.Join(",", s.Appointments.Rows.Select(a => a.Id)) == "evt_other", "only the matching reservation row is removed");
            Require(Changed(before, r.Store.Current) == "appointments,clock,completed,flags,npcPositions,player,rewardLedger,wallet", "every write in one commit, got " + Changed(before, r.Store.Current));

            var plain = new Rig(Fx.DefaultCatalog());
            var start = plain.Store.Current;
            Require(plain.Run("evt_order").Succeeded, "event without a reservation row");
            Require(Changed(start, plain.Store.Current) == "clock,completed,flags,rewardLedger,wallet", "no reservation and no live actor write neither field, got " + Changed(start, plain.Store.Current));

            var empty = new Rig(Fx.DefaultCatalog());
            var untouched = empty.Store.Current;
            var nothing = Fx.Event("evt_nothing", RepeatPolicy.Practice, null, 0);
            empty.Catalog.EventList.Add(nothing);
            var same = empty.Run(nothing);
            Require(same.Succeeded && ReferenceEquals(empty.Store.Current, untouched), "an event that writes nothing keeps the revision");
        }

        private static void TransitionsWriteNothingUntilCommitted()
        {
            var r = new Rig(Fx.DefaultCatalog());
            var before = r.Store.Current;
            var state = before.State;
            var evt = r.Catalog.FindEvent("evt_cost30");
            var a = r.Rules.Complete(state, evt, Rig.Nobody);
            var b = r.Rules.Complete(state, evt, Rig.Nobody);
            Require(a.Succeeded && b.Succeeded && SaveDocument.Write(a.Value).Equals(SaveDocument.Write(b.Value)), "the transition is a pure function of the state");
            Unchanged(r, before, "transition only");
            Require(AbsoluteMinute(r) == 2040 && Crowns(r) == 100 && state.Clock.AbsoluteMinute == 2040, "an uncommitted transition persists nothing and leaves its input alone");
        }
    }
}
