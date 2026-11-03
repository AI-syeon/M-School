using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Core;

namespace MagicCampus.State
{
    /// <summary>
    /// The only commit authority. Order: expected revision, completion replay policy, every participant prepares against the same basis,
    /// pointer ownership and overlap checks, candidate construction, invariants, one replacement, notifications through the completion queue.
    /// Participant ownership is read from IStateParticipant.OwnedSlices; the store contains no domain branches.
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

        private sealed class Resolved
        {
            public readonly string Id;
            public readonly IStateParticipant Participant;
            public readonly List<JsonPointer> Owned;
            public Resolved(string id, IStateParticipant participant, List<JsonPointer> owned) { Id = id; Participant = participant; Owned = owned; }
        }

        private readonly object gate = new object();
        private readonly IStateParticipantRegistry registry;
        private readonly IStateInvariant[] invariants;
        private readonly ICompletionQueue queue;
        private readonly ICompletionReplayPolicy replayPolicy;
        private readonly Dictionary<string, Outcome<StateSnapshot>> outcomes = new Dictionary<string, Outcome<StateSnapshot>>(StringComparer.Ordinal);
        private readonly List<Action<StateCommitted>> listeners = new List<Action<StateCommitted>>();
        private StateSnapshot current;
        private bool busy;

        /// <param name="initial">Starting state; it is not run through invariants (load goes through ReplaceAll).</param>
        public StateStore(StateSnapshot initial, IStateParticipantRegistry registry, IEnumerable<IStateInvariant> invariants, ICompletionQueue queue, ICompletionReplayPolicy replayPolicy)
        {
            current = initial ?? throw new ArgumentNullException(nameof(initial));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.replayPolicy = replayPolicy ?? throw new ArgumentNullException(nameof(replayPolicy));
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

        public Outcome<StateSnapshot> Commit(IStatePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            TransactionId id;
            try { id = plan.TransactionId; }
            catch (Exception ex) { lock (gate) return Fail(ResultCodes.TransactionMismatch, Diag("PLAN_EXCEPTION", Describe(ex), null, "", "")); }
            return Run(id, false, () => CommitCore(plan));
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

        private Outcome<StateSnapshot> CommitCore(IStatePlan plan)
        {
            var basis = current;
            if (plan.ExpectedRevision != basis.Revision)
                return Fail(ResultCodes.RevisionConflict, Diag("REVISION_CONFLICT", "plan was built on a different revision", null, L(plan.ExpectedRevision), L(basis.Revision)));

            if (plan.Completion.HasValue)
            {
                var key = plan.Completion.Value;
                if (key.Value == null)
                    return Fail(ResultCodes.ContentInvalid, Diag("COMPLETION_KEY_MISSING", "default completion key", null, "", ""));
                bool done;
                try { done = replayPolicy.IsCompleted(basis, key); }
                catch (Exception ex) { return Fail(ResultCodes.InvariantViolation, Diag("REPLAY_POLICY_EXCEPTION", Describe(ex), null, "", key.Value)); }
                if (done)
                    return Fail(ResultCodes.ConditionFalse, Diag("COMPLETION_ALREADY_RECORDED", "completion key is already persisted", null, "", key.Value));
            }

            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var changes = plan.Changes;
            if (changes != null)
                foreach (var change in changes)
                {
                    if (change == null) return Fail(ResultCodes.ContentInvalid, Diag("CHANGE_NULL", "null planned change", null, "", ""));
                    if (seen.Add(change.ParticipantId)) ids.Add(change.ParticipantId);
                }

            var resolved = new List<Resolved>();
            foreach (var id in ids)
            {
                IStateParticipant participant;
                try { participant = registry.Find(id); }
                catch (Exception ex) { return Fail(ResultCodes.Unregistered, Diag("REGISTRY_EXCEPTION", Describe(ex), null, id, "")); }
                if (participant == null)
                    return Fail(ResultCodes.Unregistered, Diag("PARTICIPANT_UNREGISTERED", "no participant registered for this id", null, id, ""));
                if (!string.Equals(participant.ParticipantId, id, StringComparison.Ordinal))
                    return Fail(ResultCodes.Unregistered, Diag("PARTICIPANT_ID_MISMATCH", "registry returned a participant with another id", null, id, participant.ParticipantId ?? ""));

                var slices = participant.OwnedSlices;
                if (slices == null || slices.Count == 0)
                    return Fail(ResultCodes.ContentInvalid, Diag("OWNED_SLICES_EMPTY", "participant owns no slices", null, id, ""));
                var owned = new List<JsonPointer>();
                foreach (var s in slices)
                {
                    if (!JsonPointer.TryParse(s, out var ownedPointer, out var ownedError))
                        return Fail(ResultCodes.ContentInvalid, Diag("OWNED_SLICE_INVALID", ownedError, s, id, s ?? ""));
                    owned.Add(ownedPointer);
                }
                resolved.Add(new Resolved(id, participant, owned));
            }

            var entries = new List<Entry>();
            foreach (var r in resolved)
            {
                Outcome<StateReplacement> prepared;
                try { prepared = r.Participant.Prepare(basis, plan); }
                catch (Exception ex) { return Fail(ResultCodes.InvariantViolation, Diag("PARTICIPANT_EXCEPTION", r.Id + ": " + Describe(ex), null, "", "")); }
                if (prepared == null)
                    return Fail(ResultCodes.InvariantViolation, Diag("PARTICIPANT_NULL_OUTCOME", r.Id + " returned no outcome", null, "", ""));
                if (!prepared.Succeeded) return Reject(prepared.Result, r.Id);

                foreach (var kv in prepared.Value.Slices)
                {
                    if (!JsonPointer.TryParse(kv.Key, out var path, out var pathError))
                        return Fail(ResultCodes.ParseError, Diag("POINTER_MALFORMED", pathError, kv.Key, "canonical JSON pointer", kv.Key));
                    if (!Owns(r.Owned, path))
                        return Fail(ResultCodes.OutOfScope, Diag("SLICE_NOT_OWNED", r.Id + " does not own this path", kv.Key, OwnedText(r.Owned), kv.Key));
                    entries.Add(new Entry(r.Id, path, kv.Value));
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

            var candidate = new StateSnapshot(basis.Revision + 1, root);
            var problems = Validate(candidate);
            if (problems.Length > 0) return Outcome<StateSnapshot>.Fail(ResultCodes.InvariantViolation, basis.Revision, problems);

            var ok = Outcome<StateSnapshot>.Ok(candidate, candidate.Revision);
            var committed = new StateCommitted(plan.TransactionId, plan.Completion, basis.Revision, candidate.Revision);
            current = candidate;
            Notify(committed);
            return ok;
        }

        private Outcome<StateSnapshot> ReplaceAllCore(StateSnapshot candidate, long expectedRevision, TransactionId transactionId)
        {
            var basis = current;
            if (expectedRevision != basis.Revision)
                return Fail(ResultCodes.RevisionConflict, Diag("REVISION_CONFLICT", "replacement was prepared on a different revision", null, L(expectedRevision), L(basis.Revision)));

            var rebased = new StateSnapshot(basis.Revision + 1, candidate.Root);
            var problems = Validate(rebased);
            if (problems.Length > 0) return Outcome<StateSnapshot>.Fail(ResultCodes.InvariantViolation, basis.Revision, problems);

            var ok = Outcome<StateSnapshot>.Ok(rebased, rebased.Revision);
            var committed = new StateCommitted(transactionId, null, basis.Revision, rebased.Revision);
            current = rebased;
            Notify(committed);
            return ok;
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

        private static bool Owns(List<JsonPointer> owned, JsonPointer path)
        {
            foreach (var o in owned)
                if (o.IsPrefixOf(path)) return true;
            return false;
        }

        private static string OwnedText(List<JsonPointer> owned)
        {
            var parts = new string[owned.Count];
            for (var i = 0; i < parts.Length; i++) parts[i] = owned[i].Text;
            return string.Join(",", parts);
        }

        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
        private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;
    }
}
