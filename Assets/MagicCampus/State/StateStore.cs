using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;
using MagicCampus.Persistence;

namespace MagicCampus.State
{
    /// <summary>
    /// The only commit authority. A commit runs a pure transition on the current typed state, checks the candidate against the
    /// invariants, installs it as one replacement and notifies through the completion queue. Single-threaded by contract; a lock
    /// only guards visibility.
    /// </summary>
    public sealed class StateStore : IStateStore
    {
        private readonly object gate = new object();
        private readonly IStateInvariant[] invariants;
        private readonly ICompletionQueue queue;
        private readonly Dictionary<string, Outcome<StateSnapshot>> outcomes = new Dictionary<string, Outcome<StateSnapshot>>(StringComparer.Ordinal);
        private readonly List<Action<StateCommitted>> listeners = new List<Action<StateCommitted>>();
        private StateSnapshot current;
        private bool busy;

        /// <param name="initial">Starting state; it is not run through invariants (load goes through ReplaceAll).</param>
        public StateStore(StateSnapshot initial, IEnumerable<IStateInvariant> invariants, ICompletionQueue queue)
        {
            current = initial ?? throw new ArgumentNullException(nameof(initial));
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            var list = new List<IStateInvariant>();
            if (invariants != null)
                foreach (var inv in invariants)
                {
                    if (inv == null) throw new ArgumentException("null invariant", nameof(invariants));
                    list.Add(inv);
                }
            this.invariants = list.ToArray();
        }

        /// <summary>Last exception thrown while posting notifications; the commit itself is never undone by it.</summary>
        public Exception LastNotificationError { get; private set; }

        public StateSnapshot Current
        {
            get { lock (gate) return current; }
        }

        public void OnCommitted(Action<StateCommitted> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            lock (gate) listeners.Add(listener);
        }

        /// <summary>
        /// Runs a pure transition on the current typed state and stores SaveDocument.Write of its result after the invariants accept it.
        /// A transition that returns the state it was given changes nothing and keeps the revision.
        /// </summary>
        public Outcome<StateSnapshot> Commit(TransactionId transactionId, Func<GameState, Outcome<GameState>> transition)
        {
            if (transition == null) throw new ArgumentNullException(nameof(transition));
            return Run(transactionId, false, () => CommitTransition(transactionId, transition));
        }

        public Outcome<StateSnapshot> ReplaceAll(StateSnapshot candidate, long expectedRevision, TransactionId transactionId)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            return Run(transactionId, true, () => ReplaceAllCore(candidate, expectedRevision, transactionId));
        }

        private Outcome<StateSnapshot> Run(TransactionId id, bool replaceAll, Func<Outcome<StateSnapshot>> core)
        {
            lock (gate)
            {
                if (id.Value == null)
                    return Fail(ResultCodes.TransactionMismatch, Diag("TRANSACTION_ID_MISSING", "default transaction id", null, "", ""));
                if (busy)
                    return Fail(ResultCodes.InvariantViolation, Diag("REENTRANT_COMMIT", "the store cannot be re-entered while a transaction is being prepared", null, "", ""));
                if (outcomes.TryGetValue(id.Value, out var first)) return first;

                busy = true;
                Outcome<StateSnapshot> outcome;
                try { outcome = core(); }
                catch (Exception ex) { outcome = Fail(ResultCodes.InvariantViolation, Diag("STORE_EXCEPTION", Describe(ex), null, "", "")); }
                finally { busy = false; }

                if (replaceAll && outcome.Succeeded) outcomes.Clear();
                outcomes[id.Value] = outcome;
                return outcome;
            }
        }

        private Outcome<StateSnapshot> CommitTransition(TransactionId transactionId, Func<GameState, Outcome<GameState>> transition)
        {
            var basis = current;
            if (!basis.TryTypedState(out var state, out var unreadable))
                return Fail(ResultCodes.ContentInvalid, unreadable);

            var next = transition(state);
            if (next == null)
                return Fail(ResultCodes.InvariantViolation, Diag("TRANSITION_NULL_OUTCOME", "the transition returned no outcome", null, "", ""));
            if (!next.Succeeded) return Fail(next.Result.Code, new List<Diagnostic>(next.Result.Details).ToArray());
            if (ReferenceEquals(next.Value, state)) return Outcome<StateSnapshot>.Ok(basis, basis.Revision);

            return Install(new StateSnapshot(basis.Revision + 1, SaveDocument.Write(next.Value)), transactionId);
        }

        private Outcome<StateSnapshot> ReplaceAllCore(StateSnapshot candidate, long expectedRevision, TransactionId transactionId)
        {
            var basis = current;
            if (expectedRevision != basis.Revision)
                return Fail(ResultCodes.RevisionConflict, Diag("REVISION_CONFLICT", "replacement was prepared on a different revision", null, L(expectedRevision), L(basis.Revision)));

            return Install(new StateSnapshot(basis.Revision + 1, candidate.Root), transactionId);
        }

        private Outcome<StateSnapshot> Install(StateSnapshot candidate, TransactionId transactionId)
        {
            var basis = current;
            var problems = Validate(candidate);
            if (problems.Length > 0) return Outcome<StateSnapshot>.Fail(ResultCodes.InvariantViolation, basis.Revision, problems);

            current = candidate;
            Notify(new StateCommitted(transactionId, null, basis.Revision, candidate.Revision));
            return Outcome<StateSnapshot>.Ok(candidate, candidate.Revision);
        }

        private Diagnostic[] Validate(StateSnapshot candidate)
        {
            var problems = new List<Diagnostic>();
            foreach (var inv in invariants)
            {
                IReadOnlyList<Diagnostic> found;
                try { found = inv.Validate(candidate); }
                catch (Exception ex) { problems.Add(Diag("INVARIANT_EXCEPTION", Describe(ex), null, "", "")); continue; }
                if (found == null) continue;
                foreach (var d in found)
                    if (d != null) problems.Add(d);
            }
            return problems.ToArray();
        }

        private void Notify(StateCommitted committed)
        {
            if (listeners.Count == 0) return;
            var targets = listeners.ToArray();
            try { queue.Post(() => Deliver(targets, committed)); }
            catch (Exception ex) { LastNotificationError = ex; }
        }

        private static void Deliver(Action<StateCommitted>[] targets, StateCommitted committed)
        {
            List<Exception> errors = null;
            foreach (var target in targets)
            {
                try { target(committed); }
                catch (Exception ex)
                {
                    if (errors == null) errors = new List<Exception>();
                    errors.Add(ex);
                }
            }
            if (errors != null) throw new AggregateException(errors);
        }

        private Outcome<StateSnapshot> Fail(string code, params Diagnostic[] details)
            => Outcome<StateSnapshot>.Fail(code, current.Revision, details);

        private static Diagnostic Diag(string code, string message, string pointer, string expected, string actual)
            => new Diagnostic(code, message, pointer == null ? null : new SourceLocation("", pointer, "", "", 0), expected, actual);

        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
        private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;
    }
}
