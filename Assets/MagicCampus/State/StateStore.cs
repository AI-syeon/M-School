using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Transactions.Core;

namespace MagicCampus.State
{
    /// <summary>
    /// The only commit authority. Order: expected revision, completed-key replay check, every participant prepares against the same basis,
    /// pointer ownership and overlap checks, candidate construction, invariants, one replacement, notifications through the completion queue.
    /// Participant ownership comes from the registry (parsed at registration); the store contains no domain branches.
    /// Single-threaded by contract; a lock only guards visibility.
    /// </summary>
    public sealed class StateStore : IStateStore
    {
        private sealed class Entry
        {
            public readonly string ParticipantId;
            public readonly JsonPointer Path;
            public readonly DataNode Value;
            public Entry(string participantId, JsonPointer path, DataNode value) { ParticipantId = participantId; Path = path; Value = value; }
        }

        private static readonly string[] CompletionLedgers = { "completed", "rewardLedger" };

        private readonly object gate = new object();
        private readonly StateParticipantRegistry registry;
        private readonly IStateInvariant[] invariants;
        private readonly ICompletionQueue queue;
        private readonly Dictionary<string, Outcome<StateSnapshot>> outcomes = new Dictionary<string, Outcome<StateSnapshot>>(StringComparer.Ordinal);
        private readonly List<Action<StateCommitted>> listeners = new List<Action<StateCommitted>>();
        private StateSnapshot current;
        private bool busy;

        /// <param name="initial">Starting state; it is not run through invariants (load goes through ReplaceAll).</param>
        public StateStore(StateSnapshot initial, StateParticipantRegistry registry, IEnumerable<IStateInvariant> invariants, ICompletionQueue queue)
        {
            current = initial ?? throw new ArgumentNullException(nameof(initial));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
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

        public Outcome<StateSnapshot> Commit(StatePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return Run(plan.TransactionId, false, () => CommitCore(plan));
        }

        /// <summary>
        /// Runs a pure transition on the current typed state and stores SaveDocument.Write of its result under the same invariants as a plan.
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

        private Outcome<StateSnapshot> CommitCore(StatePlan plan)
        {
            var basis = current;
            if (plan.ExpectedRevision != basis.Revision)
                return Fail(ResultCodes.RevisionConflict, Diag("REVISION_CONFLICT", "plan was built on a different revision", null, L(plan.ExpectedRevision), L(basis.Revision)));

            if (plan.Completion.HasValue && IsCompleted(basis, plan.Completion.Value.Value))
                return Fail(ResultCodes.ConditionFalse, Diag("COMPLETION_ALREADY_RECORDED", "completion key is already persisted", null, "", plan.Completion.Value.Value));

            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in plan.Changes)
                if (seen.Add(change.ParticipantId)) ids.Add(change.ParticipantId);

            var resolved = new List<KeyValuePair<string, StateParticipantRegistry.Registration>>();
            foreach (var id in ids)
            {
                var registration = registry.Find(id);
                if (registration == null)
                    return Fail(ResultCodes.Unregistered, Diag("PARTICIPANT_UNREGISTERED", "no participant registered for this id", null, id, ""));
                resolved.Add(new KeyValuePair<string, StateParticipantRegistry.Registration>(id, registration));
            }

            var entries = new List<Entry>();
            foreach (var r in resolved)
            {
                var id = r.Key;
                Outcome<StateReplacement> prepared;
                try { prepared = r.Value.Participant.Prepare(basis, plan); }
                catch (Exception ex) { return Fail(ResultCodes.InvariantViolation, Diag("PARTICIPANT_EXCEPTION", id + ": " + Describe(ex), null, "", "")); }
                if (prepared == null)
                    return Fail(ResultCodes.InvariantViolation, Diag("PARTICIPANT_NULL_OUTCOME", id + " returned no outcome", null, "", ""));
                if (!prepared.Succeeded) return Reject(prepared.Result, id);

                foreach (var kv in prepared.Value.Slices)
                {
                    if (!JsonPointer.TryParse(kv.Key, out var path, out var pathError))
                        return Fail(ResultCodes.ParseError, Diag("POINTER_MALFORMED", pathError, kv.Key, "canonical JSON pointer", kv.Key));
                    if (!Owns(r.Value.Owned, path))
                        return Fail(ResultCodes.OutOfScope, Diag("SLICE_NOT_OWNED", id + " does not own this path", kv.Key, OwnedText(r.Value.Owned), kv.Key));
                    entries.Add(new Entry(id, path, kv.Value));
                }
            }

            for (var i = 0; i < entries.Count; i++)
                for (var j = i + 1; j < entries.Count; j++)
                    if (entries[i].Path.Overlaps(entries[j].Path))
                        return Fail(ResultCodes.SliceOverlap, Diag("SLICE_OVERLAP", entries[i].ParticipantId + " and " + entries[j].ParticipantId + " write overlapping paths", entries[i].Path.Text, entries[i].Path.Text, entries[j].Path.Text));

            if (entries.Count == 0)
                return Outcome<StateSnapshot>.Ok(basis, basis.Revision);

            entries.Sort((a, b) => string.CompareOrdinal(a.Path.Text, b.Path.Text));
            var root = basis.Root;
            foreach (var e in entries)
            {
                var next = StateTree.TryReplace(root, e.Path, e.Value, out var code, out var message);
                if (next == null) return Fail(code, Diag("SLICE_UNWRITABLE", message, e.Path.Text, "writable object path", e.Path.Text));
                root = next;
            }

            return Install(new StateSnapshot(basis.Revision + 1, root), plan.TransactionId, plan.Completion);
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

            return Install(new StateSnapshot(basis.Revision + 1, SaveDocument.Write(next.Value)), transactionId, null);
        }

        private Outcome<StateSnapshot> ReplaceAllCore(StateSnapshot candidate, long expectedRevision, TransactionId transactionId)
        {
            var basis = current;
            if (expectedRevision != basis.Revision)
                return Fail(ResultCodes.RevisionConflict, Diag("REVISION_CONFLICT", "replacement was prepared on a different revision", null, L(expectedRevision), L(basis.Revision)));

            return Install(new StateSnapshot(basis.Revision + 1, candidate.Root), transactionId, null);
        }

        private Outcome<StateSnapshot> Install(StateSnapshot candidate, TransactionId transactionId, CompletionKey? completion)
        {
            var basis = current;
            var problems = Validate(candidate);
            if (problems.Length > 0) return Outcome<StateSnapshot>.Fail(ResultCodes.InvariantViolation, basis.Revision, problems);

            current = candidate;
            Notify(new StateCommitted(transactionId, completion, basis.Revision, candidate.Revision));
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

        private Outcome<StateSnapshot> Reject(Result result, string participantId)
        {
            var code = result.Code == ResultCodes.Ok ? ResultCodes.InvariantViolation : result.Code;
            var details = new List<Diagnostic>(result.Details);
            if (details.Count == 0)
                details.Add(Diag("PARTICIPANT_REJECTED", participantId + " rejected the plan", null, "", result.Code));
            return Outcome<StateSnapshot>.Fail(code, current.Revision, details.ToArray());
        }

        private Outcome<StateSnapshot> Fail(string code, params Diagnostic[] details)
            => Outcome<StateSnapshot>.Fail(code, current.Revision, details);

        private static Diagnostic Diag(string code, string message, string pointer, string expected, string actual)
            => new Diagnostic(code, message, pointer == null ? null : new SourceLocation("", pointer, "", "", 0), expected, actual);

        /// <summary>True when the key is listed in a persisted top-level ledger: a list holding it as a string, or an object having it as a member name.</summary>
        private static bool IsCompleted(StateSnapshot snapshot, string key)
        {
            foreach (var field in CompletionLedgers)
            {
                var node = snapshot.Slice(field);
                if (node == null) continue;
                if (node.Kind == DataKind.List)
                {
                    foreach (var item in node.Items)
                        if (item.Kind == DataKind.String && string.Equals(item.Text, key, StringComparison.Ordinal)) return true;
                }
                else if (node.Kind == DataKind.Object && node.Members.ContainsKey(key)) return true;
            }
            return false;
        }

        private static bool Owns(IReadOnlyList<JsonPointer> owned, JsonPointer path)
        {
            foreach (var o in owned)
                if (o.IsPrefixOf(path)) return true;
            return false;
        }

        private static string OwnedText(IReadOnlyList<JsonPointer> owned)
        {
            var parts = new string[owned.Count];
            for (var i = 0; i < parts.Length; i++) parts[i] = owned[i].Text;
            return string.Join(",", parts);
        }

        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
        private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;
    }
}
