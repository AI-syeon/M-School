using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Runtime;
using MagicCampus.Transactions;

namespace MagicCampus.State
{
    /// <summary>One revision of the game state. Revision counts changing commits and replacements since the store was created.</summary>
    public sealed class StateSnapshot
    {
        public long Revision { get; }
        public GameState State { get; }

        public StateSnapshot(long revision, GameState state)
        {
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            Revision = revision;
            State = state ?? throw new ArgumentNullException(nameof(state));
        }
    }

    public sealed class StateCommitted
    {
        public TransactionId TransactionId { get; }
        public long PreviousRevision { get; }
        public StateSnapshot Snapshot { get; }
        public long Revision => Snapshot.Revision;

        public StateCommitted(TransactionId transactionId, long previousRevision, StateSnapshot snapshot)
        {
            TransactionId = transactionId;
            PreviousRevision = previousRevision;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }
    }

    /// <summary>
    /// The only commit authority. A commit runs a pure transition on the current state and installs its result with one reference
    /// swap; a replacement installs a loaded or restored state. Neither re-validates: GameState cannot hold the states the save
    /// schema refuses, and SaveDocument.Admit and SaveCodec.Seal check the rest at the load and save boundaries.
    /// Listeners run through the completion queue after the swap, in commit order. Single-threaded by contract; a lock only guards visibility.
    /// </summary>
    public sealed class GameStore
    {
        private readonly object gate = new object();
        private readonly ICompletionQueue queue;
        private readonly List<Action<StateCommitted>> listeners = new List<Action<StateCommitted>>();
        private StateSnapshot current;
        private bool busy;

        public GameStore(StateSnapshot initial, ICompletionQueue queue)
        {
            current = initial ?? throw new ArgumentNullException(nameof(initial));
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
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
        /// Runs a pure transition on the current state. A refusal or a throw changes nothing; a transition that returns the state it
        /// was given keeps the revision. Resubmitting a transaction id runs the transition again on the newest state.
        /// </summary>
        public Outcome<StateSnapshot> Commit(TransactionId transactionId, Func<GameState, Outcome<GameState>> transition)
        {
            if (transition == null) throw new ArgumentNullException(nameof(transition));
            return Run(transactionId, () =>
            {
                var basis = current;
                var next = transition(basis.State);
                if (next == null)
                    return Fail(ResultCodes.InvariantViolation, Diag("TRANSITION_NULL_OUTCOME", "the transition returned no outcome"));
                if (!next.Succeeded) return Fail(next.Result.Code, new List<Diagnostic>(next.Result.Details).ToArray());
                if (ReferenceEquals(next.Value, basis.State)) return Outcome<StateSnapshot>.Ok(basis, basis.Revision);
                return Install(next.Value, transactionId);
            });
        }

        /// <summary>Full replacement for load and scene-abort restore. The new revision is Current.Revision + 1.</summary>
        public Outcome<StateSnapshot> Replace(GameState state, TransactionId transactionId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return Run(transactionId, () => Install(state, transactionId));
        }

        private Outcome<StateSnapshot> Run(TransactionId id, Func<Outcome<StateSnapshot>> core)
        {
            lock (gate)
            {
                if (id.Value == null)
                    return Fail(ResultCodes.TransactionMismatch, Diag("TRANSACTION_ID_MISSING", "default transaction id"));
                if (busy)
                    return Fail(ResultCodes.InvariantViolation, Diag("REENTRANT_COMMIT", "the store cannot be re-entered while a transaction is being prepared"));

                busy = true;
                try { return core(); }
                catch (Exception ex) { return Fail(ResultCodes.InvariantViolation, Diag("STORE_EXCEPTION", ex.GetType().Name + ": " + ex.Message)); }
                finally { busy = false; }
            }
        }

        private Outcome<StateSnapshot> Install(GameState state, TransactionId transactionId)
        {
            var previous = current.Revision;
            current = new StateSnapshot(previous + 1, state);
            Notify(new StateCommitted(transactionId, previous, current));
            return Outcome<StateSnapshot>.Ok(current, current.Revision);
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

        private static Diagnostic Diag(string code, string message) => new Diagnostic(code, message, null, "", "");
    }
}
