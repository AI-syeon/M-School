using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Transactions.Effects
{
    /// <summary>
    /// Builds one StatePlan: effects, event.complete, time cost, world projection, reservations.complete.
    /// Collaborators are injected ports only. Conditions are re-checked by the runner on the newest revision.
    /// </summary>
    public sealed class EventCommitPlanner : IEventCommitPlanner
    {
        private static readonly HashSet<string> StagedDomains = new HashSet<string>(StringComparer.Ordinal)
        {
            "class", "encounter", "puzzle", "craft", "relationship", "project", "activity"
        };

        private readonly IEffectPlanner effects;
        private readonly IClockCostPlanner clock;
        private readonly IWorldStateProjector world;
        private readonly ICatalog catalog;

        public EventCommitPlanner(IEffectPlanner effects, IClockCostPlanner clock, IWorldStateProjector world, ICatalog catalog)
        {
            this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public Outcome<StatePlan> PlanCommit(CompiledEvent definition, EffectContext context)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var basis = context.Basis;
            var revision = basis.Revision;
            if (context.TransactionId.Value == null)
                return Refuse(revision, Diag.Make(ResultCodes.ContentInvalid, "transaction id is required", "TransactionId", "default"));

            var known = catalog.FindEvent(definition.Id);
            if (known == null)
                return Refuse(revision, Diag.Make(ResultCodes.RefNotFound, "event is not in the catalog", "known event", definition.Id));
            if (known.Repeat != definition.Repeat || known.CostMinutes != definition.CostMinutes || !SameKey(known.Completion, definition.Completion))
                return Refuse(revision, Diag.Make(ResultCodes.TransactionMismatch, "definition differs from the catalog event", "catalog definition", definition.Id));

            var practice = definition.Repeat == RepeatPolicy.Practice;
            CompletionKey? completion = null;
            if (!practice) completion = definition.Completion ?? new CompletionKey(definition.Id);
            if (context.Completion.HasValue && (!completion.HasValue || !completion.Value.Equals(context.Completion.Value)))
                return Refuse(revision, Diag.Make(ResultCodes.TransactionMismatch, "context completion differs from the event completion", completion.HasValue ? completion.Value.Value : "no completion", context.Completion.Value.Value));

            if (completion.HasValue)
            {
                List<string> done;
                List<string> ledger;
                Diagnostic error;
                if (!SaveTree.TryReadStrings(basis, SaveTree.Completed, out done, out error) || !SaveTree.TryReadStrings(basis, SaveTree.RewardLedger, out ledger, out error))
                    return Refuse(revision, error);
                if (done.Contains(completion.Value.Value) || ledger.Contains(completion.Value.Value))
                    return Refuse(revision, Diag.Make(ResultCodes.ConditionFalse, "completion key already consumed; no reward or time is produced again", "unconsumed completion key", completion.Value.Value));
            }

            long minutes;
            Diagnostic costProblem;
            if (!TryResolveMinutes(definition, context, out minutes, out costProblem)) return Refuse(revision, costProblem);

            var effectContext = new EffectContext(basis, context.TransactionId, completion, context.Staged);
            var planned = effects.Plan(definition.Effects, effectContext);
            if (planned == null) return Refuse(revision, Diag.Make(ResultCodes.ContentInvalid, "effect planner returned no outcome", "Outcome", "null"));
            if (!planned.Succeeded) return Propagate(revision, planned);
            var changes = new List<PlannedChange>(planned.Value.Changes);
            foreach (var change in changes)
            {
                if (change.ParticipantId != EffectOperations.ParticipantId) continue;
                if (change.Operation == EffectOperations.EventComplete)
                    return Refuse(revision, Diag.Make(ResultCodes.TransactionMismatch, "effects may not write the event ledger", "planner-owned event.complete", change.Operation));
                if (practice && change.Operation == EffectOperations.CurrencyAdd)
                    return Refuse(revision, Diag.Make(ResultCodes.TransactionMismatch, "practice runs earn no currency", "no reward", change.Operation));
            }

            if (!practice)
            {
                changes.Add(new PlannedChange(EffectOperations.ParticipantId, EffectOperations.EventComplete, DataNode.FromObject(new[]
                {
                    new KeyValuePair<string, DataNode>("eventId", DataNode.FromString(definition.Id)),
                    new KeyValuePair<string, DataNode>("completionKey", DataNode.FromString(completion.Value.Value)),
                    new KeyValuePair<string, DataNode>("repeatPolicy", DataNode.FromString(EffectOperations.Once))
                })));
            }

            if (minutes > 0)
            {
                var cost = clock.PlanCost(basis, minutes);
                if (cost == null) return Refuse(revision, Diag.Make(ResultCodes.ContentInvalid, "clock cost planner returned no outcome", "Outcome", "null"));
                if (!cost.Succeeded) return Propagate(revision, cost);
                var clockProblem = CheckClockChanges(cost.Value, minutes);
                if (clockProblem != null) return Refuse(revision, clockProblem);
                changes.AddRange(cost.Value.Changes);
            }

            var projection = world.PlanProjection(basis);
            if (projection == null) return Refuse(revision, Diag.Make(ResultCodes.ContentInvalid, "world projector returned no outcome", "Outcome", "null"));
            if (!projection.Succeeded) return Propagate(revision, projection);
            var worldProblem = CheckWorldChanges(projection.Value);
            if (worldProblem != null) return Refuse(revision, worldProblem);
            changes.AddRange(projection.Value.Changes);

            if (HasReservation(basis, definition.Id))
            {
                changes.Add(new PlannedChange(EffectOperations.ReservationsParticipant, EffectOperations.ReservationsComplete, DataNode.FromObject(new[]
                {
                    new KeyValuePair<string, DataNode>("eventId", DataNode.FromString(definition.Id))
                })));
            }

            var plan = new StatePlan(context.TransactionId, completion, revision, changes);
            return Outcome<StatePlan>.Ok(plan, revision);
        }

        private bool TryResolveMinutes(CompiledEvent definition, EffectContext context, out long minutes, out Diagnostic problem)
        {
            minutes = 0;
            problem = null;
            long? otherCost = null;
            long classMinutes = -1;
            foreach (var result in context.Staged)
            {
                if (!StagedDomains.Contains(result.Domain))
                {
                    problem = Diag.Make(ResultCodes.Unregistered, "no registered operation domain '" + result.Domain + "'", "registered domain", result.Domain);
                    return false;
                }
                long? staged = null;
                if (result.Staged.Kind == DataKind.Object)
                {
                    DataNode cost;
                    if (result.Staged.TryGet("costMinutes", out cost))
                    {
                        if (cost.Kind != DataKind.Integer || cost.Integer < 0)
                        {
                            problem = Diag.Make(ResultCodes.ContentInvalid, "staged costMinutes must be a nonnegative integer", "Integer >= 0", Diag.Value(cost));
                            return false;
                        }
                        staged = cost.Integer;
                    }
                }
                else if (result.Staged.Kind != DataKind.Null)
                {
                    problem = Diag.Make(ResultCodes.ContentInvalid, "staged result must be an object", "Object", result.Staged.Kind.ToString());
                    return false;
                }

                if (result.Domain == "class")
                {
                    if (classMinutes >= 0 || context.Staged.Count != 1)
                    {
                        problem = Diag.Make(ResultCodes.TransactionMismatch, "a class perform yields exactly one staged result", "1 result", Diag.Num(context.Staged.Count));
                        return false;
                    }
                    if (definition.CostMinutes != 0)
                    {
                        problem = Diag.Make(ResultCodes.TransactionMismatch, "event costMinutes must be 0 when the class duration is the time source", "0", Diag.Num(definition.CostMinutes));
                        return false;
                    }
                    var row = catalog.FindDefinition("class", result.DefinitionId);
                    if (row == null)
                    {
                        problem = Diag.Make(ResultCodes.RefNotFound, "class is not in the catalog", "known class", result.DefinitionId);
                        return false;
                    }
                    DataNode duration;
                    if (row.Kind != DataKind.Object || !row.TryGet("durationMinutes", out duration) || duration.Kind != DataKind.Integer || duration.Integer <= 0)
                    {
                        problem = Diag.Make(ResultCodes.ContentInvalid, "class.durationMinutes must be a positive integer", "Integer > 0", result.DefinitionId);
                        return false;
                    }
                    if (!staged.HasValue || staged.Value != duration.Integer)
                    {
                        problem = Diag.Make(ResultCodes.TransactionMismatch, "staged costMinutes must equal the catalog class duration", Diag.Num(duration.Integer), staged.HasValue ? Diag.Num(staged.Value) : "absent");
                        return false;
                    }
                    classMinutes = duration.Integer;
                }
                else if (staged.HasValue)
                {
                    if (otherCost.HasValue)
                    {
                        problem = Diag.Make(ResultCodes.TransactionMismatch, "several staged results carry a cost; costs are never summed", "one cost source", "several");
                        return false;
                    }
                    otherCost = staged;
                }
            }

            if (classMinutes >= 0)
            {
                minutes = classMinutes;
            }
            else if (otherCost.HasValue)
            {
                if (definition.CostMinutes != 0)
                {
                    problem = Diag.Make(ResultCodes.TransactionMismatch, "event and domain both carry a cost; exactly one source is allowed", "one cost source", "event + domain");
                    return false;
                }
                minutes = otherCost.Value;
            }
            else
            {
                minutes = definition.CostMinutes;
            }
            if (definition.Repeat == RepeatPolicy.Practice) minutes = 0;
            return true;
        }

        private static Diagnostic CheckClockChanges(ChangeSet set, long minutes)
        {
            var advances = 0;
            foreach (var change in set.Changes)
            {
                if (change.ParticipantId == EffectOperations.ParticipantId || change.ParticipantId == EffectOperations.WorldParticipant || change.ParticipantId == EffectOperations.ReservationsParticipant)
                    return Diag.Make(ResultCodes.TransactionMismatch, "clock cost planner may not write other planners' participants", "clock or day-boundary consumers", change.ParticipantId);
                if (change.ParticipantId != EffectOperations.ClockParticipant || change.Operation != EffectOperations.ClockAdvance) continue;
                advances++;
                DataNode amount;
                if (change.Payload.Kind != DataKind.Object || !change.Payload.TryGet("minutes", out amount))
                    return Diag.Make(ResultCodes.TransactionMismatch, "clock advance carries no minutes", Diag.Num(minutes), "absent");
                double value;
                if (amount.Kind == DataKind.Integer) value = amount.Integer;
                else if (amount.Kind == DataKind.Number) value = amount.Number;
                else return Diag.Make(ResultCodes.TransactionMismatch, "clock advance minutes must be numeric", Diag.Num(minutes), Diag.Value(amount));
                if (Math.Abs(value - minutes) > 1e-9)
                    return Diag.Make(ResultCodes.TransactionMismatch, "clock advance must carry exactly the single-source cost", Diag.Num(minutes), Diag.Value(amount));
            }
            if (advances != 1)
                return Diag.Make(ResultCodes.TransactionMismatch, "clock cost planner must return exactly one clock advance", "1", Diag.Num(advances));
            return null;
        }

        private static Diagnostic CheckWorldChanges(ChangeSet set)
        {
            if (set.Changes.Count > 1)
                return Diag.Make(ResultCodes.TransactionMismatch, "world projection is a single change", "at most 1", Diag.Num(set.Changes.Count));
            foreach (var change in set.Changes)
            {
                if (change.ParticipantId != EffectOperations.WorldParticipant || change.Operation != EffectOperations.WorldProject)
                    return Diag.Make(ResultCodes.TransactionMismatch, "world projector may only emit world/project", "world/project", change.ParticipantId + "/" + change.Operation);
            }
            return null;
        }

        private static bool HasReservation(StateSnapshot basis, string eventId)
        {
            var appointments = basis.Slice(SaveTree.Appointments);
            if (appointments == null || appointments.Kind != DataKind.List) return false;
            foreach (var item in appointments.Items)
            {
                DataNode id;
                if (item.Kind == DataKind.Object && item.TryGet("id", out id) && id.Kind == DataKind.String && id.Text == eventId) return true;
            }
            return false;
        }

        private static bool SameKey(CompletionKey? a, CompletionKey? b)
        {
            return a.HasValue == b.HasValue && (!a.HasValue || a.Value.Equals(b.Value));
        }

        private static Outcome<StatePlan> Refuse(long revision, Diagnostic diagnostic)
        {
            return Outcome<StatePlan>.Fail(diagnostic.Code, revision, diagnostic);
        }

        private static Outcome<StatePlan> Propagate<T>(long revision, Outcome<T> failed) where T : class
        {
            var details = new List<Diagnostic>(failed.Result.Details);
            if (details.Count == 0) details.Add(Diag.Make(failed.Result.Code, "collaborator failed without diagnostics", "diagnostics", "none"));
            return Outcome<StatePlan>.Fail(failed.Result.Code, revision, details.ToArray());
        }
    }
}
