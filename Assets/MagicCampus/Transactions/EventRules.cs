using System;
using System.Collections.Generic;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Contracts;
using MagicCampus.State;
using MagicCampus.Timekeeping;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.Transactions
{
    /// <summary>
    /// Everything one successful event run writes, as a pure function of the current state: flags, complete ids, the once claim in
    /// both ledgers, crowns, the time cost, the live actor positions and the matching reservation row.
    /// </summary>
    public sealed class EventRules
    {
        private const string Appointments = "/appointments";

        private readonly ICatalog catalog;
        private readonly ICalendar calendar;

        public EventRules(ICatalog catalog, ICalendar calendar)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        }

        /// <summary>The once rule: a once event whose key is in either ledger cannot run or pay again. Practice events are never spent.</summary>
        public static bool IsSpent(GameState state, CompiledEvent definition)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return definition.Repeat == RepeatPolicy.Once && state.Progress.IsConsumed(definition.OnceKey);
        }

        public Outcome<GameState> Complete(GameState state, CompiledEvent definition, WorldPlaces live)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (live == null) throw new ArgumentNullException(nameof(live));

            var def = catalog.FindEvent(definition.Id);
            if (def == null) return Refuse(Diag.Make(ResultCodes.RefNotFound, "event is not in the catalog", "known event", definition.Id));
            if (IsSpent(state, def))
                return Refuse(Diag.Make(ResultCodes.ConditionFalse, "completion key already consumed; no reward or time is produced again", "unconsumed completion key", def.OnceKey.Value));

            var parsed = EventOutcome.Parse(catalog, def);
            if (!parsed.Succeeded) return Failed(parsed.Result);
            var o = parsed.Value;

            var progress = state.Progress;
            foreach (var set in o.Sets) progress = progress.Set(set.Key, set.Value);
            foreach (var id in o.Completes)
            {
                var done = progress.Complete(id);
                if (!done.Succeeded) return Failed(done.Result);
                progress = done.Value;
            }
            if (o.Claim.HasValue)
            {
                var claimed = progress.Claim(o.Claim.Value);
                if (!claimed.Succeeded) return Failed(claimed.Result);
                progress = claimed.Value;
            }
            if (o.Crowns > 0)
            {
                var earned = progress.Earn(o.Crowns);
                if (!earned.Succeeded) return Failed(earned.Result);
                progress = earned.Value;
            }
            var next = ReferenceEquals(progress, state.Progress) ? state : state.With(progress);

            if (o.Minutes > 0)
            {
                if (!Calendar.TryCheckMinutes(o.Minutes, out var minutesProblem)) return Outcome<GameState>.Fail(ResultCodes.ContentInvalid, 0, minutesProblem);
                if (!ClockService.TryPlan(calendar, next.Clock.Time, o.Minutes, out var step, out var planProblem)) return Outcome<GameState>.Fail(ResultCodes.ContentInvalid, 0, planProblem);
                if (!step.To.Equals(step.From)) next = next.With(next.Clock.Advance(step));
            }

            if (live.Player.HasValue || live.Npcs.Count > 0) next = next.With(next.World.Project(live));

            var row = next.Appointments.Find(def.Id);
            if (row != null)
            {
                if (!o.Claim.HasValue)
                    return Refuse(new Diagnostic(ResultCodes.TransactionMismatch, "reservation complete is only valid in the success plan of the same event",
                        new SourceLocation("save", Appointments, def.Id, "", 0), "event-effects/event.complete", "absent"));
                next = next.With(next.Appointments.Remove(def.Id));
            }
            return Outcome<GameState>.Ok(next, 0);
        }

        private static Outcome<GameState> Refuse(Diagnostic d) => Outcome<GameState>.Fail(d.Code, 0, d);
        private static Outcome<GameState> Failed(Result r) => Outcome<GameState>.Fail(r.Code, 0, new List<Diagnostic>(r.Details).ToArray());
    }

    /// <summary>
    /// What one event's effect list writes, read from the catalog alone. Only a once event carries a claim, crowns and minutes, so a
    /// practice reward or a reward without its ledger entry cannot be represented. Checks that read the state live in Progress.
    /// </summary>
    public sealed class EventOutcome
    {
        public readonly CompletionKey? Claim;
        public readonly IReadOnlyList<KeyValuePair<string, FlagValue>> Sets;
        public readonly IReadOnlyList<string> Completes;
        public readonly long Crowns;
        public readonly long Minutes;

        private EventOutcome(CompletionKey? claim, IReadOnlyList<KeyValuePair<string, FlagValue>> sets, IReadOnlyList<string> completes, long crowns, long minutes)
        {
            Claim = claim; Sets = sets; Completes = completes; Crowns = crowns; Minutes = minutes;
        }

        public static EventOutcome Once(CompletionKey claim, IReadOnlyList<KeyValuePair<string, FlagValue>> sets, IReadOnlyList<string> completes, long crowns, long minutes)
            => new EventOutcome(claim, sets, completes, crowns, minutes);

        public static EventOutcome Practice(IReadOnlyList<KeyValuePair<string, FlagValue>> sets, IReadOnlyList<string> completes)
            => new EventOutcome(null, sets, completes, 0, 0);

        /// <summary>
        /// All effects or none. An effect without a rule here (item.grant, spell.learn) refuses the list with UNREGISTERED, whatever
        /// else is wrong; otherwise the first problem's code wins and every problem is reported.
        /// </summary>
        public static Outcome<EventOutcome> Parse(ICatalog catalog, CompiledEvent definition)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var practice = definition.Repeat == RepeatPolicy.Practice;
            if (practice && definition.Completion.HasValue)
                return Refuse(Diag.Make(ResultCodes.TransactionMismatch, "context completion differs from the event completion", "no completion", definition.Completion.Value.Value));

            var sets = new List<KeyValuePair<string, FlagValue>>();
            var completes = new List<string>();
            long crowns = 0;
            var problems = new List<Diagnostic>();
            var unregistered = false;
            string firstCode = null;
            foreach (var call in definition.Effects)
            {
                var name = call.Function.Name;
                if (call.Function.Role != FunctionRole.Effect || (name != "set" && name != "complete" && name != "currency.add"))
                {
                    unregistered = true;
                    problems.Add(Diag.Make(ResultCodes.Unregistered, "no registered effect handler for '" + name + "'", "registered effect", name));
                    continue;
                }
                var problem = call.Negated || call.Alternatives.Count > 0
                    ? Diag.Make(ResultCodes.ContentInvalid, "effects cannot be negated or carry alternatives", "plain effect", name)
                    : name == "set" ? ParseSet(catalog, call, sets)
                    : name == "complete" ? ParseComplete(catalog, call, completes)
                    : ParseCurrency(call, practice, ref crowns);
                if (problem == null) continue;
                problems.Add(problem);
                if (firstCode == null) firstCode = problem.Code;
            }
            if (unregistered) return Outcome<EventOutcome>.Fail(ResultCodes.Unregistered, 0, problems.ToArray());
            if (firstCode != null) return Outcome<EventOutcome>.Fail(firstCode, 0, problems.ToArray());

            return Outcome<EventOutcome>.Ok(practice
                ? Practice(sets.AsReadOnly(), completes.AsReadOnly())
                : Once(definition.OnceKey, sets.AsReadOnly(), completes.AsReadOnly(), crowns, definition.CostMinutes), 0);
        }

        /// <summary>set(variable, value): declared bool/int variables only; clock variables are refused.</summary>
        private static Diagnostic ParseSet(ICatalog catalog, CompiledCall call, List<KeyValuePair<string, FlagValue>> sets)
        {
            if (!CallArgs.TryGet(call, 0, out var name) || !CallArgs.TryGet(call, 1, out var value))
                return Diag.Make(ResultCodes.ArgCount, "set needs a variable and a value", "2 arguments", "fewer");
            if (name.Kind != DataKind.String)
                return Diag.Make(ResultCodes.ArgType, "variable name must be a string", "string", name.Kind.ToString());
            if (!VariableDeclaration.TryResolve(catalog, name.Text, out var declaration, out var error)) return error;
            if (!declaration.TryAccept(value, out error)) return error;
            sets.Add(new KeyValuePair<string, FlagValue>(declaration.Id, value.Kind == DataKind.Bool ? FlagValue.Of(value.Bool) : FlagValue.Of(value.Integer)));
            return null;
        }

        /// <summary>complete(id): a plain completion id. Event completion keys are written only by the once claim.</summary>
        private static Diagnostic ParseComplete(ICatalog catalog, CompiledCall call, List<string> completes)
        {
            if (!CallArgs.TryGet(call, 0, out var id))
                return Diag.Make(ResultCodes.ArgCount, "complete needs an id", "1 argument", "none");
            if (id.Kind != DataKind.String)
                return Diag.Make(ResultCodes.ArgType, "id must be a string", "string", id.Kind.ToString());
            try
            {
                if (new CompletionKey(id.Text).Value.Length == 0) throw new ArgumentException();
            }
            catch (ArgumentException)
            {
                return Diag.Make(ResultCodes.ContentInvalid, "completion id must be non-empty without whitespace", "valid id", id.Text);
            }
            foreach (var e in catalog.Events)
                if (e.OnceKey.Value == id.Text)
                    return Diag.Make(ResultCodes.ContentInvalid, "event completion keys are written only by event.complete together with the reward ledger", "non-event completion id", id.Text);
            completes.Add(id.Text);
            return null;
        }

        /// <summary>currency.add(amountCrowns): a positive wallet-sized integer, paid only by a once event.</summary>
        private static Diagnostic ParseCurrency(CompiledCall call, bool practice, ref long crowns)
        {
            if (!CallArgs.TryGet(call, 0, out var amount))
                return Diag.Make(ResultCodes.ArgCount, "currency.add needs an amount", "1 argument", "none");
            if (amount.Kind != DataKind.Integer)
                return Diag.Make(ResultCodes.ArgType, "amount must be an integer number of crowns", "Integer", amount.Kind.ToString());
            if (amount.Integer <= 0 || amount.Integer > State.Crowns.Limit)
                return Diag.Make(ResultCodes.InvariantViolation, "amount must be a positive wallet-sized integer", "1.. " + Diag.Num(State.Crowns.Limit), Diag.Num(amount.Integer));
            if (practice)
                return Diag.Make(ResultCodes.TransactionMismatch, "rewards need the shared completion key; practice and ad-hoc runs earn nothing", "completion key", "none");
            crowns += amount.Integer;
            return null;
        }

        private static Outcome<EventOutcome> Refuse(Diagnostic d) => Outcome<EventOutcome>.Fail(d.Code, 0, d);
    }
}
