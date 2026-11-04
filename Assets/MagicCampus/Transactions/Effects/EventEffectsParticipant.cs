using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Transactions.Effects
{
    /// <summary>
    /// State participant 'event-effects'. Owns /flags, /completed, /rewardLedger and /wallet only. Prepare never writes the store:
    /// it applies the typed EffectChanges of one plan on the same basis and returns one replacement or one refusal.
    /// Per-call validation already happened in EffectPlanner; this checks only what depends on the whole plan and the stored state.
    /// </summary>
    public sealed class EventEffectsParticipant : IStateParticipant
    {
        private static readonly IReadOnlyList<string> Owned = Array.AsReadOnly(new[] { "/flags", "/completed", "/rewardLedger", "/wallet" });

        private readonly ICatalog catalog;

        public EventEffectsParticipant(ICatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public string ParticipantId => EffectOperations.ParticipantId;
        public IReadOnlyList<string> OwnedSlices => Owned;

        public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Diagnostic problem = null;
            if (plan.ExpectedRevision != basis.Revision)
                problem = Diag.Make(ResultCodes.RevisionConflict, "plan was prepared for another revision", "revision " + Diag.Num(plan.ExpectedRevision), "revision " + Diag.Num(basis.Revision));
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
            private readonly IStatePlan plan;
            private readonly ICatalog catalog;
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

            public Session(StateSnapshot basis, IStatePlan plan, ICatalog catalog)
            {
                this.basis = basis;
                this.plan = plan;
                this.catalog = catalog;
            }

            public Diagnostic Run()
            {
                var mine = new List<EffectChange>();
                var eventCompletes = 0;
                foreach (var change in plan.Changes)
                {
                    if (change.ParticipantId != EffectOperations.ParticipantId) continue;
                    var typed = change as EffectChange;
                    if (typed == null)
                        return Diag.Make(ResultCodes.Unregistered, "event-effects applies only planned effect changes", "set|complete|currency.add|event.complete", change.Operation);
                    mine.Add(typed);
                    if (typed.Kind != EffectKind.EventComplete) continue;
                    eventCompletes++;
                    claimedKey = typed.CompletionKey;
                    claimedOnce = typed.Policy == RepeatPolicy.Once;
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

            private Diagnostic Apply(EffectChange change)
            {
                switch (change.Kind)
                {
                    case EffectKind.Set: return ApplySet(change);
                    case EffectKind.Complete: return ApplyComplete(change);
                    case EffectKind.CurrencyAdd: return ApplyCurrency(change);
                    default: return ApplyEventComplete(change);
                }
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

            private Diagnostic ApplySet(EffectChange change)
            {
                var problem = LoadFlags();
                if (problem != null) return problem;
                flags.Set(change.Id, change.Value);
                flagsDirty = true;
                return null;
            }

            private Diagnostic ApplyComplete(EffectChange change)
            {
                var problem = LoadCompleted();
                if (problem != null) return problem;
                if (!completedSet.Add(change.Id))
                    return Diag.Make(ResultCodes.ConditionFalse, "already completed", "not yet completed", change.Id);
                completed.Add(change.Id);
                completedDirty = true;
                return null;
            }

            private Diagnostic ApplyCurrency(EffectChange change)
            {
                if (!plan.Completion.HasValue || claimedKey == null || !claimedOnce || claimedKey != plan.Completion.Value.Value)
                    return Diag.Make(ResultCodes.TransactionMismatch, "currency.add requires the once event.complete ledger entry for the plan completion key in the same plan", "event.complete once + completion key", plan.Completion.HasValue ? plan.Completion.Value.Value : "no completion");
                var problem = LoadWallet();
                if (problem != null) return problem;
                if (change.Crowns > SaveTree.WalletLimit - crowns)
                    return Diag.Make(ResultCodes.InvariantViolation, "wallet would overflow", "crowns <= " + Diag.Num(SaveTree.WalletLimit), Diag.Num(crowns) + " + " + Diag.Num(change.Crowns));
                crowns += change.Crowns;
                walletDirty = true;
                return null;
            }

            private Diagnostic ApplyEventComplete(EffectChange change)
            {
                var key = change.CompletionKey;
                var definition = catalog.FindEvent(change.Id);
                if (definition == null)
                    return Diag.Make(ResultCodes.RefNotFound, "event is not in the catalog", "known event", change.Id);
                var expectedKey = definition.Completion.HasValue ? definition.Completion.Value.Value : definition.Id;
                if (key != expectedKey)
                    return Diag.Make(ResultCodes.TransactionMismatch, "completion key is not the event's shared key", expectedKey, key);
                if (change.Policy != definition.Repeat)
                    return Diag.Make(ResultCodes.TransactionMismatch, "repeat policy differs from the catalog", definition.Repeat.ToString(), change.Policy.ToString());
                if (eventCompleteApplied)
                    return Diag.Make(ResultCodes.TransactionMismatch, "a plan carries at most one event.complete", "1", "2");
                eventCompleteApplied = true;
                if (change.Policy == RepeatPolicy.Practice)
                {
                    if (plan.Completion.HasValue)
                        return Diag.Make(ResultCodes.TransactionMismatch, "practice runs carry no completion key", "no completion", plan.Completion.Value.Value);
                    return null;
                }
                if (!plan.Completion.HasValue || plan.Completion.Value.Value != key)
                    return Diag.Make(ResultCodes.TransactionMismatch, "plan completion must equal the event completion key", key, plan.Completion.HasValue ? plan.Completion.Value.Value : "no completion");
                var problem = LoadCompleted();
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
