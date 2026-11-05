using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.State;
using MagicCampus.Tests.PureDomain.Support;

namespace MagicCampus.Tests.State
{
    public static class StateSpecs
    {
        public static void Run()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("CommitAdvancesRevisionAndNotifiesNextTick", CommitAdvancesRevisionAndNotifiesNextTick),
                new KeyValuePair<string, Action>("TransitionFailureAndThrowLeaveStateUntouched", TransitionFailureAndThrowLeaveStateUntouched),
                new KeyValuePair<string, Action>("InvariantRejection", InvariantRejection),
                new KeyValuePair<string, Action>("SameTransactionReplay", SameTransactionReplay),
                new KeyValuePair<string, Action>("MissingTransactionIdRefused", MissingTransactionIdRefused),
                new KeyValuePair<string, Action>("OldSnapshotsStayIntact", OldSnapshotsStayIntact),
                new KeyValuePair<string, Action>("ReplaceAllSuccessAndFailure", ReplaceAllSuccessAndFailure),
                new KeyValuePair<string, Action>("NotificationsAreFifoAndCapturedAtCommit", NotificationsAreFifoAndCapturedAtCommit),
                new KeyValuePair<string, Action>("UnchangedStateDoesNotAdvance", UnchangedStateDoesNotAdvance),
                new KeyValuePair<string, Action>("ReentrantCommitRefused", ReentrantCommitRefused),
            };

            var failures = new List<string>();
            foreach (var t in tests)
            {
                try { t.Value(); }
                catch (Exception ex) { failures.Add(t.Key + ": " + ex.Message); }
            }
            if (failures.Count > 0)
                throw new InvalidOperationException("StateSpecs failed (" + failures.Count + "/" + tests.Count + "):\n" + string.Join("\n", failures));
        }

        // ------------------------------------------------------------------ tests

        private static void CommitAdvancesRevisionAndNotifiesNextTick()
        {
            var f = Build(null);
            var o = f.Store.Commit(new TransactionId("t1"), Earn(10));
            Check.True(o.Succeeded, "commit succeeds");
            Check.Equal(1L, o.Result.Revision, "result revision");
            Check.Equal(1L, f.Store.Current.Revision, "current revision");
            Check.Node(I(20), f.Store.Current.Slice("wallet"), "wallet replaced");
            Check.Equal(0, f.Seen.Count, "no inline notification");
            Check.Equal(1, f.Queue.Pending, "notification queued");
            f.Queue.Drain();
            Check.Equal(1, f.Seen.Count, "delivered next tick");
            Check.Equal(0L, f.Seen[0].PreviousRevision, "previous revision");
            Check.Equal(1L, f.Seen[0].Revision, "revision");
            Check.Equal("t1", f.Seen[0].TransactionId.Value, "transaction id");
        }

        private static void TransitionFailureAndThrowLeaveStateUntouched()
        {
            var f = Build(null);
            var o1 = ExpectFail(f, "t1", s => Outcome<GameState>.Fail(ResultCodes.ConditionFalse, 0, new Diagnostic("NOPE", "no", null, "", "")), ResultCodes.ConditionFalse, "transition failure");
            Check.True(HasDiag(o1, "NOPE"), "transition diagnostic propagated");
            var o2 = ExpectFail(f, "t2", s => throw new InvalidOperationException("boom"), ResultCodes.InvariantViolation, "transition throw");
            Check.True(HasDiag(o2, "STORE_EXCEPTION"), "throw becomes diagnostic");
            Check.Node(I(10), f.Store.Current.Slice("wallet"), "nothing applied");
            Check.Equal(0, f.Queue.Pending, "no notification for failures");
        }

        private static void InvariantRejection()
        {
            var f = Build(new IStateInvariant[] { new WalletAtMost(25) });
            var o = ExpectFail(f, "t1", Earn(20), ResultCodes.InvariantViolation, "invariant");
            Check.True(HasDiag(o, "WALLET_TOO_RICH"), "invariant diagnostic");
            Check.Equal(0L, f.Store.Current.Revision, "revision unchanged");
            Check.Equal(0, f.Queue.Pending, "no notification");
        }

        private static void SameTransactionReplay()
        {
            var f = Build(null);
            var calls = 0;
            Func<GameState, Outcome<GameState>> earn = s => { calls++; return Earn(10)(s); };
            var first = f.Store.Commit(new TransactionId("t1"), earn);
            var again = f.Store.Commit(new TransactionId("t1"), earn);
            Check.True(ReferenceEquals(first, again), "same outcome object returned");
            Check.Equal(1, calls, "applied once");
            Check.Equal(1L, f.Store.Current.Revision, "revision advanced once");
            Check.Equal(1, f.Queue.Pending, "one notification");

            var failing = 0;
            Func<GameState, Outcome<GameState>> refuse = s => { failing++; return Outcome<GameState>.Fail(ResultCodes.ConditionFalse, 0, new Diagnostic("NOPE", "no", null, "", "")); };
            var fail1 = f.Store.Commit(new TransactionId("tf"), refuse);
            var fail2 = f.Store.Commit(new TransactionId("tf"), refuse);
            Check.True(!fail1.Succeeded && ReferenceEquals(fail1, fail2), "failure is replayed as first result");
            Check.Equal(1, failing, "failed transition not run again");
        }

        private static void MissingTransactionIdRefused()
        {
            var f = Build(null);
            var o = ExpectFail(f, null, Earn(10), ResultCodes.TransactionMismatch, "default transaction id");
            Check.True(HasDiag(o, "TRANSACTION_ID_MISSING"), "missing id diagnostic");
        }

        private static void OldSnapshotsStayIntact()
        {
            var f = Build(null);
            var old = f.Store.Current;
            Check.True(f.Store.Commit(new TransactionId("t1"), Earn(80)).Succeeded, "commit");
            var now = f.Store.Current;

            Check.True(!ReferenceEquals(old, now) && !ReferenceEquals(old.Root, now.Root), "new snapshot and root");
            Check.Equal(0L, old.Revision, "old revision");
            Check.Node(I(10), old.Slice("wallet"), "old wallet intact");
            Check.Node(I(90), now.Slice("wallet"), "new wallet");
            Check.True(ReferenceEquals(old.Slice("shopState"), now.Slice("shopState")), "untouched branch shared");
            var dict = old.Root.Members as IDictionary<string, DataNode>;
            Check.True(dict == null || dict.IsReadOnly, "members are not writable");
        }

        private static void ReplaceAllSuccessAndFailure()
        {
            var f = Build(new IStateInvariant[] { new WalletAtMost(100) });
            Check.True(f.Store.Commit(new TransactionId("t1"), Earn(10)).Succeeded, "setup commit");
            f.Queue.Drain();
            f.Seen.Clear();

            var cand = new StateSnapshot(99, Initial().Root.With("wallet", I(77)).With("futureField", S("kept")));
            var r = f.Store.ReplaceAll(cand, 1, new TransactionId("load1"));
            Check.True(r.Succeeded, "replace all");
            Check.Equal(2L, f.Store.Current.Revision, "store assigns current + 1");
            Check.True(ReferenceEquals(f.Store.Current.Root, cand.Root), "whole root replaced as is");
            Check.Node(S("kept"), f.Store.Current.Slice("futureField"), "unknown field preserved");
            Check.Node(Obj(P("open", DataNode.True)), f.Store.Current.Slice("shopState"), "deferred field preserved");
            Check.Equal(0, f.Seen.Count, "not inline");
            f.Queue.Drain();
            Check.Equal(1, f.Seen.Count, "notified");
            Check.Equal(1L, f.Seen[0].PreviousRevision, "previous");
            Check.Equal(2L, f.Seen[0].Revision, "new");
            Check.True(ReferenceEquals(r, f.Store.ReplaceAll(cand, 1, new TransactionId("load1"))), "replay returns first result");
            Check.Equal(2L, f.Store.Current.Revision, "replay does not apply again");

            var reuse = f.Store.Commit(new TransactionId("t1"), Earn(10));
            Check.True(reuse.Succeeded, "memory replay cache was cleared by the full replacement");
            Check.Equal(3L, f.Store.Current.Revision, "ordinary commit after load");
            Check.Node(I(87), f.Store.Current.Slice("wallet"), "commit runs on the replaced state");
            Check.Node(S("kept"), f.Store.Current.Slice("futureField"), "ordinary commit keeps unrelated state");

            var before = f.Store.Current;
            var bad = new StateSnapshot(0, Initial().Root.With("wallet", I(500)));
            var rejected = f.Store.ReplaceAll(bad, 3, new TransactionId("load2"));
            Check.Equal(ResultCodes.InvariantViolation, rejected.Result.Code, "invariant rejects candidate");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched after failed replacement");
            Check.True(ReferenceEquals(rejected, f.Store.ReplaceAll(bad, 3, new TransactionId("load2"))), "failed replacement replays");

            var stale = f.Store.ReplaceAll(cand, 1, new TransactionId("load3"));
            Check.Equal(ResultCodes.RevisionConflict, stale.Result.Code, "expected revision required");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched after conflict");
        }

        private static void NotificationsAreFifoAndCapturedAtCommit()
        {
            var f = Build(null);
            var order = new List<string>();
            f.Store.OnCommitted(c => order.Add("early@" + c.Revision));
            Check.True(f.Store.Commit(new TransactionId("t1"), Earn(1)).Succeeded, "first");
            f.Store.OnCommitted(c => order.Add("late@" + c.Revision));
            Check.True(f.Store.Commit(new TransactionId("t2"), Earn(1)).Succeeded, "second");
            Check.Equal(0, order.Count, "nothing inline");
            f.Queue.Drain();
            Check.Equal("early@1,early@2,late@2", string.Join(",", order), "commit order, listeners captured at commit");
        }

        private static void UnchangedStateDoesNotAdvance()
        {
            var f = Build(null);
            var before = f.Store.Current;
            var o = f.Store.Commit(new TransactionId("t1"), s => Outcome<GameState>.Ok(s, 0));
            Check.True(o.Succeeded, "identity transition succeeds");
            Check.Equal(0L, o.Result.Revision, "no revision advance");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched");
            Check.Equal(0, f.Queue.Pending, "no notification");
        }

        private static void ReentrantCommitRefused()
        {
            Fixture f = null;
            Outcome<StateSnapshot> inner = null;
            f = Build(null);
            Check.True(f.Store.Commit(new TransactionId("outer"), s =>
            {
                inner = f.Store.Commit(new TransactionId("inner"), Earn(1));
                return Earn(2)(s);
            }).Succeeded, "outer commit");
            Check.True(inner != null && !inner.Succeeded && inner.Result.Code == ResultCodes.InvariantViolation, "inner refused");
            Check.True(HasDiag(inner, "REENTRANT_COMMIT"), "reentrancy diagnostic");
            Check.Equal(1L, f.Store.Current.Revision, "only the outer commit applied");
            Check.Node(I(12), f.Store.Current.Slice("wallet"), "only the outer earn applied");
        }

        // ------------------------------------------------------------------ builders and fakes

        private static DataNode I(long v) => DataNode.FromInteger(v);
        private static DataNode S(string v) => DataNode.FromString(v);
        private static KeyValuePair<string, DataNode> P(string k, DataNode v) => new KeyValuePair<string, DataNode>(k, v);
        private static DataNode Obj(params KeyValuePair<string, DataNode>[] members) => DataNode.FromObject(members);

        private static Func<GameState, Outcome<GameState>> Earn(long crowns) => s =>
        {
            var earned = s.Progress.Earn(crowns);
            return earned.Succeeded ? Outcome<GameState>.Ok(s.With(earned.Value), 0) : Outcome<GameState>.Fail(earned.Result.Code, 0, new List<Diagnostic>(earned.Result.Details).ToArray());
        };

        /// <summary>A readable save state with fields no rule reads (shopState) kept beside the typed ones.</summary>
        private static StateSnapshot Initial()
        {
            var m = new Dictionary<string, DataNode>(StringComparer.Ordinal)
            {
                { "saveVersion", I(2) },
                { "wallet", I(10) },
                { "completed", DataNode.FromList(new[] { S("quest.q1") }) },
                { "shopState", Obj(P("open", DataNode.True)) }
            };
            TypedFields.FillMissing(m);
            var root = DataNode.FromObject(m);
            Check.True(SaveDocument.Read(root).Succeeded, "initial tree is a readable state");
            return new StateSnapshot(0, root);
        }

        private sealed class Fixture
        {
            public FakeQueue Queue;
            public StateStore Store;
            public readonly List<StateCommitted> Seen = new List<StateCommitted>();
        }

        private static Fixture Build(IEnumerable<IStateInvariant> invariants)
        {
            var f = new Fixture { Queue = new FakeQueue() };
            f.Store = new StateStore(Initial(), invariants, f.Queue);
            f.Store.OnCommitted(c => f.Seen.Add(c));
            return f;
        }

        private static Outcome<StateSnapshot> ExpectFail(Fixture f, string tx, Func<GameState, Outcome<GameState>> transition, string code, string what)
        {
            var before = f.Store.Current;
            var o = f.Store.Commit(tx == null ? default(TransactionId) : new TransactionId(tx), transition);
            Check.True(!o.Succeeded, what + ": must fail");
            Check.Equal(code, o.Result.Code, what + ": code");
            Check.True(o.Value == null, what + ": no value");
            Check.True(o.Result.Details.Count > 0, what + ": diagnostics present");
            Check.True(ReferenceEquals(before, f.Store.Current), what + ": state untouched");
            return o;
        }

        private static bool HasDiag(Outcome<StateSnapshot> o, string code)
        {
            foreach (var d in o.Result.Details)
                if (d.Code == code) return true;
            return false;
        }

        private sealed class FakeQueue : ICompletionQueue
        {
            private readonly List<Action> pending = new List<Action>();
            public int Pending => pending.Count;
            public void Post(Action action) { pending.Add(action); }
            public void Drain()
            {
                var run = pending.ToArray();
                pending.Clear();
                foreach (var a in run) a();
            }
        }

        private sealed class WalletAtMost : IStateInvariant
        {
            private readonly long limit;
            public WalletAtMost(long limit) { this.limit = limit; }

            public IReadOnlyList<Diagnostic> Validate(StateSnapshot candidate)
            {
                var w = candidate.Slice("wallet");
                if (w != null && w.Kind == DataKind.Integer && w.Integer > limit)
                    return new[] { new Diagnostic("WALLET_TOO_RICH", "wallet above the test limit", null, "<=" + limit, w.Integer.ToString()) };
                return Array.Empty<Diagnostic>();
            }
        }

        private static class Check
        {
            public static void True(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException("assertion failed: " + message);
            }

            public static void Equal<T>(T expected, T actual, string message)
            {
                if (!Equals(expected, actual))
                    throw new InvalidOperationException("assertion failed: " + message + " (expected " + expected + ", actual " + actual + ")");
            }

            public static void Node(DataNode expected, DataNode actual, string message)
            {
                if (actual == null || !expected.Equals(actual))
                    throw new InvalidOperationException("assertion failed: " + message + " (data nodes differ)");
            }
        }
    }
}
