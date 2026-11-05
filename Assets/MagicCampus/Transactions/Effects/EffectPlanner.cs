using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Transactions.Effects
{
    /// <summary>
    /// Turns a compiled effect list (set, complete, currency.add) into typed EffectChanges against one basis: all effects or none.
    /// This is the only place that validates effect arguments. An effect function without a plan here refuses the list with UNREGISTERED.
    /// The participant later checks only what depends on the whole plan (duplicates, running wallet total, ledger pairing).
    /// </summary>
    public sealed class EffectPlanner
    {
        private readonly ICatalog catalog;
        private readonly HashSet<string> eventKeys = new HashSet<string>(StringComparer.Ordinal);

        public EffectPlanner(ICatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            foreach (var e in catalog.Events) eventKeys.Add(e.OnceKey.Value);
        }

        public Outcome<ChangeSet> Plan(IReadOnlyList<CompiledCall> effects, EffectContext context)
        {
            if (effects == null) throw new ArgumentNullException(nameof(effects));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var revision = context.Basis.Revision;
            var diagnostics = new List<Diagnostic>();
            var changes = new List<PlannedChange>();
            var unregistered = false;
            string firstCode = null;
            foreach (var call in effects)
            {
                if (call == null) throw new ArgumentException("null effect call", nameof(effects));
                var name = call.Function.Name;
                if (call.Function.Role != FunctionRole.Effect || (name != EffectOperations.Set && name != EffectOperations.Complete && name != EffectOperations.CurrencyAdd))
                {
                    unregistered = true;
                    diagnostics.Add(Diag.Make(ResultCodes.Unregistered, "no registered effect handler for '" + name + "'", "registered effect", name));
                    continue;
                }
                EffectChange change;
                var problem = PlanCall(call, name, context, out change);
                if (problem != null)
                {
                    diagnostics.Add(problem);
                    if (firstCode == null) firstCode = problem.Code;
                    continue;
                }
                changes.Add(change);
            }
            if (unregistered) return Outcome<ChangeSet>.Fail(ResultCodes.Unregistered, revision, diagnostics.ToArray());
            if (firstCode != null) return Outcome<ChangeSet>.Fail(firstCode, revision, diagnostics.ToArray());
            return Outcome<ChangeSet>.Ok(new ChangeSet(changes), revision);
        }

        private Diagnostic PlanCall(CompiledCall call, string name, EffectContext context, out EffectChange change)
        {
            change = null;
            if (call.Negated || call.Alternatives.Count > 0)
                return Diag.Make(ResultCodes.ContentInvalid, "effects cannot be negated or carry alternatives", "plain effect", name);
            switch (name)
            {
                case EffectOperations.Set: return PlanSet(call, out change);
                case EffectOperations.Complete: return PlanComplete(call, context, out change);
                default: return PlanCurrency(call, context, out change);
            }
        }

        /// <summary>set(variable, value): declared bool/int variables only; clock variables are refused.</summary>
        private Diagnostic PlanSet(CompiledCall call, out EffectChange change)
        {
            change = null;
            DataNode name;
            DataNode value;
            if (!CallArgs.TryGet(call, 0, out name) || !CallArgs.TryGet(call, 1, out value))
                return Diag.Make(ResultCodes.ArgCount, "set needs a variable and a value", "2 arguments", "fewer");
            if (name.Kind != DataKind.String)
                return Diag.Make(ResultCodes.ArgType, "variable name must be a string", "string", name.Kind.ToString());
            VariableDeclaration declaration;
            Diagnostic error;
            if (!VariableDeclaration.TryResolve(catalog, name.Text, out declaration, out error)) return error;
            if (!declaration.TryAccept(value, out error)) return error;
            change = EffectChange.Set(declaration.Id, value);
            return null;
        }

        /// <summary>complete(id): records a unique completion id. Event completion keys are reserved for event.complete.</summary>
        private Diagnostic PlanComplete(CompiledCall call, EffectContext context, out EffectChange change)
        {
            change = null;
            DataNode id;
            if (!CallArgs.TryGet(call, 0, out id))
                return Diag.Make(ResultCodes.ArgCount, "complete needs an id", "1 argument", "none");
            if (id.Kind != DataKind.String)
                return Diag.Make(ResultCodes.ArgType, "id must be a string", "string", id.Kind.ToString());
            try
            {
                var key = new CompletionKey(id.Text);
                if (key.Value.Length == 0) throw new ArgumentException();
            }
            catch (ArgumentException)
            {
                return Diag.Make(ResultCodes.ContentInvalid, "completion id must be non-empty without whitespace", "valid id", id.Text);
            }
            if (eventKeys.Contains(id.Text))
                return Diag.Make(ResultCodes.ContentInvalid, "event completion keys are written only by event.complete together with the reward ledger", "non-event completion id", id.Text);
            List<string> done;
            Diagnostic error;
            if (!SaveTree.TryReadStrings(context.Basis, SaveTree.Completed, out done, out error)) return error;
            if (done.Contains(id.Text))
                return Diag.Make(ResultCodes.ConditionFalse, "already completed", "not yet completed", id.Text);
            change = EffectChange.Complete(id.Text);
            return null;
        }

        /// <summary>currency.add(amountCrowns): positive integer reward bound to the shared completion ledger of the same plan.</summary>
        private static Diagnostic PlanCurrency(CompiledCall call, EffectContext context, out EffectChange change)
        {
            change = null;
            DataNode amount;
            if (!CallArgs.TryGet(call, 0, out amount))
                return Diag.Make(ResultCodes.ArgCount, "currency.add needs an amount", "1 argument", "none");
            if (amount.Kind != DataKind.Integer)
                return Diag.Make(ResultCodes.ArgType, "amount must be an integer number of crowns", "Integer", amount.Kind.ToString());
            if (amount.Integer <= 0 || amount.Integer > SaveTree.WalletLimit)
                return Diag.Make(ResultCodes.InvariantViolation, "amount must be a positive wallet-sized integer", "1.. " + Diag.Num(SaveTree.WalletLimit), Diag.Num(amount.Integer));
            if (!context.Completion.HasValue)
                return Diag.Make(ResultCodes.TransactionMismatch, "rewards need the shared completion key; practice and ad-hoc runs earn nothing", "completion key", "none");
            DataNode wallet;
            long crowns;
            Diagnostic error;
            if (!SaveTree.TryReadWallet(context.Basis, out wallet, out crowns, out error)) return error;
            if (amount.Integer > SaveTree.WalletLimit - crowns)
                return Diag.Make(ResultCodes.InvariantViolation, "wallet would overflow", "crowns <= " + Diag.Num(SaveTree.WalletLimit), Diag.Num(crowns) + " + " + Diag.Num(amount.Integer));
            change = EffectChange.CurrencyAdd(amount.Integer);
            return null;
        }
    }
}
