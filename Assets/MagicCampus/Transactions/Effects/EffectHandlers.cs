using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Transactions.Effects
{
    internal static class EffectHandlerSupport
    {
        internal static Outcome<ChangeSet> Fail(EffectContext context, Diagnostic diagnostic)
        {
            return Outcome<ChangeSet>.Fail(diagnostic.Code, context.Basis.Revision, diagnostic);
        }

        /// <summary>Returns a failure when the call is not a plain effect of the expected function, otherwise null.</summary>
        internal static Outcome<ChangeSet> Precheck(CompiledCall call, EffectContext context, string functionName)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (call.Function.Role != FunctionRole.Effect || call.Function.Name != functionName)
                return Fail(context, Diag.Make(ResultCodes.Unregistered, "handler '" + functionName + "' cannot plan this call", functionName, call.Function.Name));
            if (call.Negated || call.Alternatives.Count > 0)
                return Fail(context, Diag.Make(ResultCodes.ContentInvalid, "effects cannot be negated or carry alternatives", "plain effect", functionName));
            return null;
        }

        internal static KeyValuePair<string, DataNode> Pair(string key, DataNode value)
        {
            return new KeyValuePair<string, DataNode>(key, value);
        }

        internal static Outcome<ChangeSet> Single(EffectContext context, string operation, params KeyValuePair<string, DataNode>[] payload)
        {
            var change = new PlannedChange(EffectOperations.ParticipantId, operation, DataNode.FromObject(payload));
            return Outcome<ChangeSet>.Ok(new ChangeSet(new[] { change }), context.Basis.Revision);
        }

        /// <summary>Completion keys owned by catalog events; only event.complete may write them, together with the reward ledger.</summary>
        internal static HashSet<string> EventKeys(ICatalog catalog)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (catalog == null) return keys;
            foreach (var e in catalog.Events) keys.Add(e.Completion.HasValue ? e.Completion.Value.Value : e.Id);
            return keys;
        }
    }

    /// <summary>set(variable, value): declared bool/int variables only; clock variables are refused.</summary>
    public sealed class SetEffectHandler : IEffectHandler
    {
        private readonly Func<ICatalog> catalog;

        public SetEffectHandler(ICatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            this.catalog = () => catalog;
        }

        public SetEffectHandler(ICatalogProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            catalog = () => provider.Current;
        }

        public string FunctionName => EffectOperations.Set;
        public string ParticipantId => EffectOperations.ParticipantId;

        public Outcome<ChangeSet> Plan(CompiledCall call, EffectContext context)
        {
            var bad = EffectHandlerSupport.Precheck(call, context, FunctionName);
            if (bad != null) return bad;
            DataNode name;
            DataNode value;
            if (!CallArgs.TryGet(call, 0, out name) || !CallArgs.TryGet(call, 1, out value))
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ArgCount, "set needs a variable and a value", "2 arguments", "fewer"));
            if (name.Kind != DataKind.String)
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ArgType, "variable name must be a string", "string", name.Kind.ToString()));
            VariableDeclaration declaration;
            Diagnostic error;
            if (!VariableDeclaration.TryResolve(catalog(), name.Text, out declaration, out error)) return EffectHandlerSupport.Fail(context, error);
            if (!declaration.TryAccept(value, out error)) return EffectHandlerSupport.Fail(context, error);
            return EffectHandlerSupport.Single(context, EffectOperations.Set,
                EffectHandlerSupport.Pair("variable", DataNode.FromString(declaration.Id)), EffectHandlerSupport.Pair("value", value));
        }
    }

    /// <summary>complete(id): records a unique completion id. Event completion keys are reserved for event.complete.</summary>
    public sealed class CompleteEffectHandler : IEffectHandler
    {
        private readonly Func<ICatalog> catalog;

        public CompleteEffectHandler(ICatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            this.catalog = () => catalog;
        }

        public CompleteEffectHandler(ICatalogProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            catalog = () => provider.Current;
        }

        public string FunctionName => EffectOperations.Complete;
        public string ParticipantId => EffectOperations.ParticipantId;

        public Outcome<ChangeSet> Plan(CompiledCall call, EffectContext context)
        {
            var bad = EffectHandlerSupport.Precheck(call, context, FunctionName);
            if (bad != null) return bad;
            DataNode id;
            if (!CallArgs.TryGet(call, 0, out id))
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ArgCount, "complete needs an id", "1 argument", "none"));
            if (id.Kind != DataKind.String)
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ArgType, "id must be a string", "string", id.Kind.ToString()));
            try
            {
                var key = new CompletionKey(id.Text);
                if (key.Value.Length == 0) throw new ArgumentException();
            }
            catch (ArgumentException)
            {
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ContentInvalid, "completion id must be non-empty without whitespace", "valid id", id.Text));
            }
            if (EffectHandlerSupport.EventKeys(catalog()).Contains(id.Text))
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ContentInvalid, "event completion keys are written only by event.complete together with the reward ledger", "non-event completion id", id.Text));
            List<string> done;
            Diagnostic error;
            if (!SaveTree.TryReadStrings(context.Basis, SaveTree.Completed, out done, out error)) return EffectHandlerSupport.Fail(context, error);
            if (done.Contains(id.Text))
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ConditionFalse, "already completed", "not yet completed", id.Text));
            return EffectHandlerSupport.Single(context, EffectOperations.Complete, EffectHandlerSupport.Pair("id", id));
        }
    }

    /// <summary>currency.add(amountCrowns): positive integer reward bound to the shared completion ledger of the same plan.</summary>
    public sealed class CurrencyAddEffectHandler : IEffectHandler
    {
        public string FunctionName => EffectOperations.CurrencyAdd;
        public string ParticipantId => EffectOperations.ParticipantId;

        public Outcome<ChangeSet> Plan(CompiledCall call, EffectContext context)
        {
            var bad = EffectHandlerSupport.Precheck(call, context, FunctionName);
            if (bad != null) return bad;
            DataNode amount;
            if (!CallArgs.TryGet(call, 0, out amount))
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ArgCount, "currency.add needs an amount", "1 argument", "none"));
            if (amount.Kind != DataKind.Integer)
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.ArgType, "amount must be an integer number of crowns", "Integer", amount.Kind.ToString()));
            if (amount.Integer <= 0 || amount.Integer > SaveTree.WalletLimit)
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.InvariantViolation, "amount must be a positive wallet-sized integer", "1.. " + Diag.Num(SaveTree.WalletLimit), Diag.Num(amount.Integer)));
            if (!context.Completion.HasValue)
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.TransactionMismatch, "rewards need the shared completion key; practice and ad-hoc runs earn nothing", "completion key", "none"));
            DataNode wallet;
            long crowns;
            Diagnostic error;
            if (!SaveTree.TryReadWallet(context.Basis, out wallet, out crowns, out error)) return EffectHandlerSupport.Fail(context, error);
            if (amount.Integer > SaveTree.WalletLimit - crowns)
                return EffectHandlerSupport.Fail(context, Diag.Make(ResultCodes.InvariantViolation, "wallet would overflow", "crowns <= " + Diag.Num(SaveTree.WalletLimit), Diag.Num(crowns) + " + " + Diag.Num(amount.Integer)));
            return EffectHandlerSupport.Single(context, EffectOperations.CurrencyAdd, EffectHandlerSupport.Pair("amountCrowns", amount));
        }
    }
}
