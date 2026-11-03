using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Transactions.Effects
{
    /// <summary>
    /// State participant 'event-effects'. Owns /flags, /completed, /rewardLedger and /wallet only. Prepare never writes the store:
    /// it aggregates every event-effects change of one plan on the same basis and returns one replacement or one refusal.
    /// </summary>
    public sealed class EventEffectsParticipant : IStateParticipant
    {
        private static readonly IReadOnlyList<string> Owned = Array.AsReadOnly(new[] { "/flags", "/completed", "/rewardLedger", "/wallet" });

        private readonly Func<ICatalog> catalogSource;

        public EventEffectsParticipant(ICatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            catalogSource = () => catalog;
        }

        public EventEffectsParticipant(ICatalogProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            catalogSource = () => provider.Current;
        }

        public string ParticipantId => EffectOperations.ParticipantId;
        public IReadOnlyList<string> OwnedSlices => Owned;

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, StatePlan plan)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Diagnostic problem = null;
            var catalog = catalogSource();
            if (plan.ExpectedRevision != basis.Revision)
                problem = Diag.Make(ResultCodes.RevisionConflict, "plan was prepared for another revision", "revision " + Diag.Num(plan.ExpectedRevision), "revision " + Diag.Num(basis.Revision));
            else if (catalog == null)
                problem = Diag.Make(ResultCodes.ContentInvalid, "no catalog available", "catalog", "null");
            if (problem == null)
            {
                var session = new Session(basis, plan, catalog);
                problem = session.Run();
                if (problem == null) return Outcome<StateReplacement>.Ok(session.Replacement(), basis.Revision);
            }
            return Outcome<StateReplacement>.Fail(problem.Code, basis.Revision, problem);
        }

        private sealed class Session
        {
            private readonly StateSnapshot basis;
            private readonly StatePlan plan;
            private readonly ICatalog catalog;
            private HashSet<string> reservedKeys;
            private string claimedKey;
            private bool claimedOnce;
            private bool eventCompleteApplied;
            private FlagTable flags;
            private bool flagsDirty;
            private List<string> completed;
            private HashSet<string> completedSet;
            private bool completedDirty;
            private List<string> ledger;
            private HashSet<string> ledgerSet;
            private bool ledgerDirty;
            private DataNode wallet;
            private long crowns;
            private bool walletLoaded;
            private bool walletDirty;

            public Session(StateSnapshot basis, StatePlan plan, ICatalog catalog)
            {
                this.basis = basis;
                this.plan = plan;
                this.catalog = catalog;
            }

            public Diagnostic Run()
            {
                var mine = new List<PlannedChange>();
                foreach (var change in plan.Changes)
                    if (change.ParticipantId == EffectOperations.ParticipantId) mine.Add(change);
                var eventCompletes = 0;
                foreach (var change in mine)
                {
                    if (change.Operation != EffectOperations.EventComplete) continue;
                    eventCompletes++;
                    Scan(change.Payload);
                }
                if (eventCompletes > 1)
                    return Diag.Make(ResultCodes.TransactionMismatch, "a plan carries at most one event.complete", "1", Diag.Num(eventCompletes));
                foreach (var change in mine)
                {
                    var problem = Apply(change);
                    if (problem != null) return problem;
                }
                return null;
            }

            public StateReplacement Replacement()
            {
                var slices = new Dictionary<string, DataNode>(StringComparer.Ordinal);
                if (flagsDirty) slices["/flags"] = flags.ToNode();
                if (completedDirty) slices["/completed"] = SaveTree.ToList(completed);
                if (ledgerDirty) slices["/rewardLedger"] = SaveTree.ToList(ledger);
                if (walletDirty) slices["/wallet"] = DataNode.FromInteger(crowns);
                return new StateReplacement(slices);
            }

            private void Scan(DataNode payload)
            {
                DataNode key;
                DataNode policy;
                if (payload != null && payload.Kind == DataKind.Object && payload.TryGet("completionKey", out key) && payload.TryGet("repeatPolicy", out policy)
                    && key.Kind == DataKind.String && policy.Kind == DataKind.String)
                {
                    claimedKey = key.Text;
                    claimedOnce = policy.Text == EffectOperations.Once;
                }
            }

            private Diagnostic Apply(PlannedChange change)
            {
                switch (change.Operation)
                {
                    case EffectOperations.Set: return ApplySet(change.Payload);
                    case EffectOperations.Complete: return ApplyComplete(change.Payload);
                    case EffectOperations.CurrencyAdd: return ApplyCurrency(change.Payload);
                    case EffectOperations.EventComplete: return ApplyEventComplete(change.Payload);
                    default:
                        return Diag.Make(ResultCodes.Unregistered, "event-effects does not implement this operation", "set|complete|currency.add|event.complete", change.Operation);
                }
            }

            private static Diagnostic Exact(DataNode payload, string operation, params string[] keys)
            {
                if (payload == null || payload.Kind != DataKind.Object || payload.Members.Count != keys.Length)
                    return Diag.Make(ResultCodes.ContentInvalid, operation + " payload must be an object with exactly: " + string.Join(", ", keys), string.Join(",", keys), payload == null ? "null" : Diag.Value(payload));
                foreach (var key in keys)
                    if (!payload.Members.ContainsKey(key))
                        return Diag.Make(ResultCodes.ContentInvalid, operation + " payload misses '" + key + "'", key, "absent");
                return null;
            }

            private static Diagnostic TextOf(DataNode payload, string key, out string text)
            {
                text = null;
                var node = payload.Members[key];
                if (node.Kind != DataKind.String || node.Text.Length == 0)
                    return Diag.Make(ResultCodes.ArgType, key + " must be a non-empty string", "string", Diag.Value(node));
                text = node.Text;
                return null;
            }

            private Diagnostic LoadFlags()
            {
                if (flags != null) return null;
                FlagTable table;
                Diagnostic error;
                if (!FlagTable.TryParse(basis.Slice(SaveTree.Flags), out table, out error)) return error;
                flags = table;
                return null;
            }

            private Diagnostic LoadCompleted()
            {
                if (completed != null) return null;
                List<string> values;
                Diagnostic error;
                if (!SaveTree.TryReadStrings(basis, SaveTree.Completed, out values, out error)) return error;
                completed = values;
                completedSet = new HashSet<string>(values, StringComparer.Ordinal);
                return null;
            }

            private Diagnostic LoadLedger()
            {
                if (ledger != null) return null;
                List<string> values;
                Diagnostic error;
                if (!SaveTree.TryReadStrings(basis, SaveTree.RewardLedger, out values, out error)) return error;
                ledger = values;
                ledgerSet = new HashSet<string>(values, StringComparer.Ordinal);
                return null;
            }

            private Diagnostic LoadWallet()
            {
                if (walletLoaded) return null;
                DataNode node;
                long amount;
                Diagnostic error;
                if (!SaveTree.TryReadWallet(basis, out node, out amount, out error)) return error;
                wallet = node;
                crowns = amount;
                walletLoaded = true;
                return null;
            }

            private Diagnostic ApplySet(DataNode payload)
            {
                var problem = Exact(payload, EffectOperations.Set, "variable", "value");
                if (problem != null) return problem;
                string name;
                problem = TextOf(payload, "variable", out name);
                if (problem != null) return problem;
                VariableDeclaration declaration;
                if (!VariableDeclaration.TryResolve(catalog, name, out declaration, out problem)) return problem;
                var value = payload.Members["value"];
                if (!declaration.TryAccept(value, out problem)) return problem;
                problem = LoadFlags();
                if (problem != null) return problem;
                flags.Set(declaration.Id, value);
                flagsDirty = true;
                return null;
            }

            private Diagnostic ApplyComplete(DataNode payload)
            {
                var problem = Exact(payload, EffectOperations.Complete, "id");
                if (problem != null) return problem;
                string id;
                problem = TextOf(payload, "id", out id);
                if (problem != null) return problem;
                try
                {
                    var key = new CompletionKey(id);
                    if (key.Value.Length == 0) throw new ArgumentException();
                }
                catch (ArgumentException)
                {
                    return Diag.Make(ResultCodes.ContentInvalid, "completion id must be non-empty without whitespace", "valid id", id);
                }
                if (reservedKeys == null) reservedKeys = EffectHandlerSupport.EventKeys(catalog);
                if (reservedKeys.Contains(id))
                    return Diag.Make(ResultCodes.ContentInvalid, "event completion keys are written only by event.complete together with the reward ledger", "non-event completion id", id);
                problem = LoadCompleted();
                if (problem != null) return problem;
                if (!completedSet.Add(id))
                    return Diag.Make(ResultCodes.ConditionFalse, "already completed", "not yet completed", id);
                completed.Add(id);
                completedDirty = true;
                return null;
            }

            private Diagnostic ApplyCurrency(DataNode payload)
            {
                var problem = Exact(payload, EffectOperations.CurrencyAdd, "amountCrowns");
                if (problem != null) return problem;
                var amount = payload.Members["amountCrowns"];
                if (amount.Kind != DataKind.Integer)
                    return Diag.Make(ResultCodes.ArgType, "amountCrowns must be an integer", "Integer", Diag.Value(amount));
                if (amount.Integer <= 0 || amount.Integer > SaveTree.WalletLimit)
                    return Diag.Make(ResultCodes.InvariantViolation, "amountCrowns must be positive and wallet-sized", "1.." + Diag.Num(SaveTree.WalletLimit), Diag.Num(amount.Integer));
                if (!plan.Completion.HasValue || claimedKey == null || !claimedOnce || claimedKey != plan.Completion.Value.Value)
                    return Diag.Make(ResultCodes.TransactionMismatch, "currency.add requires the once event.complete ledger entry for the plan completion key in the same plan", "event.complete once + completion key", plan.Completion.HasValue ? plan.Completion.Value.Value : "no completion");
                problem = LoadWallet();
                if (problem != null) return problem;
                if (amount.Integer > SaveTree.WalletLimit - crowns)
                    return Diag.Make(ResultCodes.InvariantViolation, "wallet would overflow", "crowns <= " + Diag.Num(SaveTree.WalletLimit), Diag.Num(crowns) + " + " + Diag.Num(amount.Integer));
                crowns += amount.Integer;
                walletDirty = true;
                return null;
            }

            private Diagnostic ApplyEventComplete(DataNode payload)
            {
                var problem = Exact(payload, EffectOperations.EventComplete, "eventId", "completionKey", "repeatPolicy");
                if (problem != null) return problem;
                string eventId;
                string key;
                string policyText;
                problem = TextOf(payload, "eventId", out eventId);
                if (problem != null) return problem;
                problem = TextOf(payload, "completionKey", out key);
                if (problem != null) return problem;
                problem = TextOf(payload, "repeatPolicy", out policyText);
                if (problem != null) return problem;
                var definition = catalog.FindEvent(eventId);
                if (definition == null)
                    return Diag.Make(ResultCodes.RefNotFound, "event is not in the catalog", "known event", eventId);
                var expectedKey = definition.Completion.HasValue ? definition.Completion.Value.Value : definition.Id;
                if (key != expectedKey)
                    return Diag.Make(ResultCodes.TransactionMismatch, "completion key is not the event's shared key", expectedKey, key);
                RepeatPolicy policy;
                if (policyText == EffectOperations.Once) policy = RepeatPolicy.Once;
                else if (policyText == EffectOperations.Practice) policy = RepeatPolicy.Practice;
                else return Diag.Make(ResultCodes.ContentInvalid, "unknown repeat policy", "once|practice", policyText);
                if (policy != definition.Repeat)
                    return Diag.Make(ResultCodes.TransactionMismatch, "repeat policy differs from the catalog", definition.Repeat.ToString(), policy.ToString());
                if (eventCompleteApplied)
                    return Diag.Make(ResultCodes.TransactionMismatch, "a plan carries at most one event.complete", "1", "2");
                eventCompleteApplied = true;
                if (policy == RepeatPolicy.Practice)
                {
                    if (plan.Completion.HasValue)
                        return Diag.Make(ResultCodes.TransactionMismatch, "practice runs carry no completion key", "no completion", plan.Completion.Value.Value);
                    return null;
                }
                if (!plan.Completion.HasValue || plan.Completion.Value.Value != key)
                    return Diag.Make(ResultCodes.TransactionMismatch, "plan completion must equal the event completion key", key, plan.Completion.HasValue ? plan.Completion.Value.Value : "no completion");
                problem = LoadCompleted();
                if (problem != null) return problem;
                problem = LoadLedger();
                if (problem != null) return problem;
                if (completedSet.Contains(key) || ledgerSet.Contains(key))
                    return Diag.Make(ResultCodes.ConditionFalse, "completion key already recorded; reward and time are not repeated", "unconsumed completion key", key);
                completedSet.Add(key);
                completed.Add(key);
                ledgerSet.Add(key);
                ledger.Add(key);
                completedDirty = true;
                ledgerDirty = true;
                return null;
            }
        }
    }
}
