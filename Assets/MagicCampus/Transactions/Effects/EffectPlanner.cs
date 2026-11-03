using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Transactions.Effects
{
    /// <summary>Plans a whole effect list through registered handlers: all effects or none. Unknown functions refuse the list with UNREGISTERED.</summary>
    public sealed class EffectPlanner : IEffectPlanner
    {
        private readonly Dictionary<string, IEffectHandler> handlers = new Dictionary<string, IEffectHandler>(StringComparer.Ordinal);

        public EffectPlanner(IEnumerable<IEffectHandler> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            foreach (var handler in handlers)
            {
                if (handler == null) throw new ArgumentException("null handler", nameof(handlers));
                if (string.IsNullOrEmpty(handler.FunctionName)) throw new ArgumentException("handler without function name", nameof(handlers));
                if (this.handlers.ContainsKey(handler.FunctionName)) throw new ArgumentException("duplicate handler " + handler.FunctionName, nameof(handlers));
                this.handlers.Add(handler.FunctionName, handler);
            }
        }

        /// <summary>The minimal connection: set, complete and currency.add only.</summary>
        public static EffectPlanner CreateMinimal(ICatalog catalog)
        {
            return new EffectPlanner(new IEffectHandler[] { new SetEffectHandler(catalog), new CompleteEffectHandler(catalog), new CurrencyAddEffectHandler() });
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
                IEffectHandler handler;
                if (call.Function.Role != FunctionRole.Effect || !handlers.TryGetValue(name, out handler))
                {
                    unregistered = true;
                    diagnostics.Add(Diag.Make(ResultCodes.Unregistered, "no registered effect handler for '" + name + "'", "registered effect", name));
                    continue;
                }
                var planned = handler.Plan(call, context);
                if (planned == null)
                {
                    diagnostics.Add(Diag.Make(ResultCodes.ContentInvalid, "handler returned no outcome", "Outcome", name));
                    if (firstCode == null) firstCode = ResultCodes.ContentInvalid;
                    continue;
                }
                if (!planned.Succeeded)
                {
                    diagnostics.AddRange(planned.Result.Details);
                    if (firstCode == null) firstCode = planned.Result.Code;
                    continue;
                }
                foreach (var change in planned.Value.Changes)
                {
                    if (change.ParticipantId != handler.ParticipantId)
                    {
                        diagnostics.Add(Diag.Make(ResultCodes.TransactionMismatch, "handler wrote to a participant it does not own", handler.ParticipantId, change.ParticipantId));
                        if (firstCode == null) firstCode = ResultCodes.TransactionMismatch;
                        continue;
                    }
                    changes.Add(change);
                }
            }
            if (unregistered) return Outcome<ChangeSet>.Fail(ResultCodes.Unregistered, revision, diagnostics.ToArray());
            if (firstCode != null)
            {
                if (diagnostics.Count == 0) diagnostics.Add(Diag.Make(firstCode, "effect planning failed", "plannable effects", "failure"));
                return Outcome<ChangeSet>.Fail(firstCode, revision, diagnostics.ToArray());
            }
            return Outcome<ChangeSet>.Ok(new ChangeSet(changes), revision);
        }
    }
}
