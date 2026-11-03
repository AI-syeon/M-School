using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Transactions.Conditions
{
    /// <summary>
    /// Pure lookup evaluator over a single StateSnapshot. Only the registered algorithms below run (eq, gte, completed,
    /// discovered, evidence, learned, caseCountGte, any); negation comes from CompiledCall.Negated. No parsing, eval or reflection.
    /// </summary>
    public sealed class ConditionEvaluator : IConditionEvaluator
    {
        private const int MaxDepth = 8;

        private delegate Eval Algorithm(CompiledCall call, Context context, int depth);

        private readonly Func<ICatalog> catalogSource;
        private readonly Dictionary<string, Algorithm> algorithms;

        public ConditionEvaluator(ICatalog catalog) : this(Constant(catalog)) { }

        public ConditionEvaluator(ICatalogProvider provider) : this(Current(provider)) { }

        private ConditionEvaluator(Func<ICatalog> source)
        {
            catalogSource = source;
            algorithms = new Dictionary<string, Algorithm>(StringComparer.Ordinal)
            {
                { "eq", (c, x, d) => Compare(c, x, false) },
                { "gte", (c, x, d) => Compare(c, x, true) },
                { "completed", (c, x, d) => Membership(c, x, SaveTree.Completed) },
                { "discovered", (c, x, d) => Membership(c, x, SaveTree.Discovered) },
                { "evidence", (c, x, d) => Membership(c, x, SaveTree.Evidence) },
                { "learned", (c, x, d) => Membership(c, x, SaveTree.Learned) },
                { "caseCountGte", (c, x, d) => CaseCount(c, x) },
                { "any", Any }
            };
        }

        private static Func<ICatalog> Constant(ICatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return () => catalog;
        }

        private static Func<ICatalog> Current(ICatalogProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            return () => provider.Current;
        }

        public ConditionReport Evaluate(IReadOnlyList<CompiledCall> when, StateSnapshot snapshot)
        {
            if (when == null) throw new ArgumentNullException(nameof(when));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var context = new Context(snapshot, catalogSource());
            var result = EvaluateAll(when, context, 0);
            return new ConditionReport(result.Satisfied, snapshot.Revision, result.Reasons);
        }

        private Eval EvaluateAll(IReadOnlyList<CompiledCall> calls, Context context, int depth)
        {
            var all = new Eval { Satisfied = true };
            foreach (var call in calls)
            {
                if (call == null) throw new ArgumentException("null condition call", nameof(calls));
                var one = EvaluateCall(call, context, depth);
                if (one.Satisfied) continue;
                all.Satisfied = false;
                all.Error |= one.Error;
                all.Reasons.AddRange(one.Reasons);
            }
            return all;
        }

        private Eval EvaluateCall(CompiledCall call, Context context, int depth)
        {
            var name = call.Function.Name;
            Eval raw;
            Algorithm algorithm;
            if (depth > MaxDepth)
            {
                raw = Fault(context, Diag.Make(ResultCodes.ContentInvalid, "condition nesting is too deep", "depth <= " + MaxDepth, Diag.Num(depth)));
            }
            else if (call.Function.Role != FunctionRole.Condition)
            {
                raw = Fault(context, Diag.Make(ResultCodes.Unregistered, "'" + name + "' is not a condition function", "registered condition function", name));
            }
            else if (!algorithms.TryGetValue(name, out algorithm))
            {
                raw = Fault(context, Diag.Make(ResultCodes.Unregistered, "no registered condition '" + name + "'", "registered condition function", name));
            }
            else
            {
                raw = algorithm(call, context, depth);
            }

            if (!call.Negated || raw.Error) return raw;
            if (raw.Satisfied)
                return No(context, Diag.Make(ResultCodes.ConditionFalse, "negated condition holds", "!" + name + " true", name + " is true"));
            return new Eval { Satisfied = true };
        }

        private static Eval Fault(Context context, Diagnostic reason)
        {
            var e = new Eval { Error = true };
            e.Reasons.Add(context.Tag(reason));
            return e;
        }

        private static Eval No(Context context, Diagnostic reason)
        {
            var e = new Eval();
            e.Reasons.Add(context.Tag(reason));
            return e;
        }

        private static Eval Compare(CompiledCall call, Context context, bool ordered)
        {
            DataNode nameNode;
            DataNode expected;
            if (!CallArgs.TryGet(call, 0, out nameNode) || !CallArgs.TryGet(call, 1, out expected))
                return Fault(context, Diag.Make(ResultCodes.ArgCount, call.Function.Name + " needs a variable and a value", "2 arguments", "fewer"));
            if (nameNode.Kind != DataKind.String)
                return Fault(context, Diag.Make(ResultCodes.ArgType, "variable name must be a string", "string", nameNode.Kind.ToString()));
            var name = nameNode.Text;
            DataNode actual;
            Diagnostic error;
            if (name == VariableDeclaration.MinuteOfDay)
            {
                long absolute;
                if (!SaveTree.TryReadAbsoluteMinute(context.Snapshot, out absolute, out error)) return Fault(context, error);
                if (expected.Kind != DataKind.Integer)
                    return Fault(context, Diag.Make(ResultCodes.ArgType, "clock.minute_of_day compares with an integer", "Integer", expected.Kind.ToString()));
                actual = DataNode.FromInteger(absolute % GameTime.MinutesPerDay);
            }
            else
            {
                if (VariableDeclaration.IsDerived(name))
                    return Fault(context, Diag.Make(ResultCodes.RefNotFound, "unknown derived clock variable", VariableDeclaration.MinuteOfDay, name));
                VariableDeclaration declaration;
                if (!VariableDeclaration.TryResolve(context.Catalog, name, out declaration, out error)) return Fault(context, error);
                if (!declaration.TryAcceptKind(expected, out error)) return Fault(context, error);
                if (ordered && declaration.Type != VariableType.Int)
                    return Fault(context, Diag.Make(ResultCodes.ArgType, "gte compares int variables only", "int variable", name));
                FlagTable flags;
                if (!context.TryFlags(out flags, out error)) return Fault(context, error);
                actual = declaration.Current(flags);
            }
            var holds = ordered ? actual.Integer >= expected.Integer : actual.Equals(expected);
            if (holds) return new Eval { Satisfied = true };
            return No(context, Diag.Make(ResultCodes.ConditionFalse, name + " does not satisfy the condition",
                name + (ordered ? " >= " : " == ") + Diag.Value(expected), name + " = " + Diag.Value(actual)));
        }

        private static Eval Membership(CompiledCall call, Context context, string field)
        {
            DataNode idNode;
            if (!CallArgs.TryGet(call, 0, out idNode))
                return Fault(context, Diag.Make(ResultCodes.ArgCount, call.Function.Name + " needs an id", "1 argument", "none"));
            if (idNode.Kind != DataKind.String)
                return Fault(context, Diag.Make(ResultCodes.ArgType, "id must be a string", "string", idNode.Kind.ToString()));
            List<string> values;
            Diagnostic error;
            if (!SaveTree.TryReadStrings(context.Snapshot, field, out values, out error)) return Fault(context, error);
            if (values.Contains(idNode.Text)) return new Eval { Satisfied = true };
            return No(context, Diag.Make(ResultCodes.ConditionFalse, idNode.Text + " is not in " + SaveTree.Pointer(field),
                field + " contains " + idNode.Text, field + " holds " + Diag.Num(values.Count) + " entries without it"));
        }

        private static Eval CaseCount(CompiledCall call, Context context)
        {
            DataNode countNode;
            if (!CallArgs.TryGet(call, 0, out countNode))
                return Fault(context, Diag.Make(ResultCodes.ArgCount, "caseCountGte needs a count", "1 argument", "none"));
            if (countNode.Kind != DataKind.Integer || countNode.Integer < 0)
                return Fault(context, Diag.Make(ResultCodes.ArgType, "count must be a non-negative integer", "Integer >= 0", Diag.Value(countNode)));
            List<string> order;
            Diagnostic error;
            if (!SaveTree.TryReadStrings(context.Snapshot, SaveTree.CaseResolutionOrder, out order, out error)) return Fault(context, error);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in order)
            {
                if (id.Length == 0 || !seen.Add(id))
                    return Fault(context, Diag.Make(ResultCodes.ContentInvalid, "caseResolutionOrder must hold distinct non-empty case ids", "distinct ids", id.Length == 0 ? "empty id" : id));
            }
            if (seen.Count >= countNode.Integer) return new Eval { Satisfied = true };
            return No(context, Diag.Make(ResultCodes.ConditionFalse, "not enough resolved cases",
                "resolved cases >= " + Diag.Num(countNode.Integer), "resolved cases = " + Diag.Num(seen.Count)));
        }

        private Eval Any(CompiledCall call, Context context, int depth)
        {
            if (call.Alternatives.Count == 0)
                return Fault(context, Diag.Make(ResultCodes.ArgCount, "any needs at least one compiled alternative", "alternatives", "none"));
            var summary = new List<Diagnostic>();
            var faulted = false;
            for (var i = 0; i < call.Alternatives.Count; i++)
            {
                var alternative = call.Alternatives[i];
                if (alternative.Count == 0)
                {
                    faulted = true;
                    summary.Add(context.Tag(Diag.Make(ResultCodes.ContentInvalid, "alternative " + Diag.Num(i + 1) + " is empty", "conditions", "none")));
                    continue;
                }
                var r = EvaluateAll(alternative, context, depth + 1);
                if (r.Satisfied) return new Eval { Satisfied = true };
                faulted |= r.Error;
                summary.AddRange(r.Reasons);
            }
            var e = new Eval { Error = faulted };
            e.Reasons.Add(context.Tag(Diag.Make(ResultCodes.ConditionFalse, "no alternative of any holds",
                "one of " + Diag.Num(call.Alternatives.Count) + " alternatives true", "none true")));
            e.Reasons.AddRange(summary);
            return e;
        }

        private sealed class Eval
        {
            public bool Satisfied;
            public bool Error;
            public readonly List<Diagnostic> Reasons = new List<Diagnostic>();
        }

        private sealed class Context
        {
            public readonly StateSnapshot Snapshot;
            public readonly ICatalog Catalog;
            private FlagTable flags;
            private Diagnostic flagsError;
            private bool loaded;

            public Context(StateSnapshot snapshot, ICatalog catalog)
            {
                Snapshot = snapshot;
                Catalog = catalog;
            }

            public bool TryFlags(out FlagTable table, out Diagnostic error)
            {
                if (!loaded)
                {
                    loaded = true;
                    FlagTable.TryParse(Snapshot.Slice(SaveTree.Flags), out flags, out flagsError);
                }
                table = flags;
                error = flagsError;
                return flags != null;
            }

            public Diagnostic Tag(Diagnostic d)
            {
                return new Diagnostic(d.Code, "r" + Diag.Num(Snapshot.Revision) + ": " + d.Message, d.Location, d.Expected, d.Actual);
            }
        }
    }
}
