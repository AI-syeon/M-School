using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Tests.PureDomain.Support;
using MagicCampus.Transactions;

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
                new KeyValuePair<string, Action>("ResubmittedClaimIsRefused", ResubmittedClaimIsRefused),
                new KeyValuePair<string, Action>("MissingTransactionIdRefused", MissingTransactionIdRefused),
                new KeyValuePair<string, Action>("OldSnapshotsStayIntact", OldSnapshotsStayIntact),
                new KeyValuePair<string, Action>("ReplaceInstallsWholeState", ReplaceInstallsWholeState),
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
            var f = Build();
            var o = f.Store.Commit(new TransactionId("t1"), Earn(10));
            Check.True(o.Succeeded, "commit succeeds");
            Check.Equal(1L, o.Result.Revision, "result revision");
            Check.Equal(1L, f.Store.Current.Revision, "current revision");
            Check.Equal(20L, Wallet(f.Store.Current), "wallet replaced");
            Check.Equal(0, f.Seen.Count, "no inline notification");
            Check.Equal(1, f.Queue.Pending, "notification queued");
            f.Queue.Drain();
            Check.Equal(1, f.Seen.Count, "delivered next tick");
            Check.Equal(0L, f.Seen[0].PreviousRevision, "previous revision");
            Check.Equal(1L, f.Seen[0].Revision, "revision");
            Check.True(ReferenceEquals(f.Store.Current, f.Seen[0].Snapshot), "the committed snapshot travels with the notification");
            Check.Equal("t1", f.Seen[0].TransactionId.Value, "transaction id");
        }

        private static void TransitionFailureAndThrowLeaveStateUntouched()
        {
            var f = Build();
            var o1 = ExpectFail(f, "t1", s => Outcome<GameState>.Fail(ResultCodes.ConditionFalse, 0, new Diagnostic("NOPE", "no", null, "", "")), ResultCodes.ConditionFalse, "transition failure");
            Check.True(HasDiag(o1, "NOPE"), "transition diagnostic propagated");
            var o2 = ExpectFail(f, "t2", s => throw new InvalidOperationException("boom"), ResultCodes.InvariantViolation, "transition throw");
            Check.True(HasDiag(o2, "STORE_EXCEPTION"), "throw becomes diagnostic");
            Check.Equal(10L, Wallet(f.Store.Current), "nothing applied");
            Check.Equal(0, f.Queue.Pending, "no notification for failures");
        }

        private static void ResubmittedClaimIsRefused()
        {
            var f = Build();
            Check.True(f.Store.Commit(new TransactionId("t1"), ClaimAndEarn("node.once", 10)).Succeeded, "first claim commits");
            var after = f.Store.Current;
            var again = f.Store.Commit(new TransactionId("t1"), ClaimAndEarn("node.once", 10));
            Check.True(!again.Succeeded && again.Result.Code == ResultCodes.ConditionFalse, "resubmitting the transaction is refused");
            Check.True(ReferenceEquals(after, f.Store.Current), "state untouched by the resubmission");
            Check.Equal(20L, Wallet(f.Store.Current), "wallet paid once");
            Check.Equal(1, f.Store.Current.State.Progress.RewardLedger.Count, "ledger holds the key once");
            Check.Equal(1L, f.Store.Current.Revision, "revision advanced once");
            Check.Equal(1, f.Queue.Pending, "one notification");
        }

        private static void MissingTransactionIdRefused()
        {
            var f = Build();
            var o = ExpectFail(f, null, Earn(10), ResultCodes.TransactionMismatch, "default transaction id");
            Check.True(HasDiag(o, "TRANSACTION_ID_MISSING"), "missing id diagnostic");
            var before = f.Store.Current;
            var r = f.Store.Replace(Initial().State, default(TransactionId));
            Check.True(!r.Succeeded && r.Result.Code == ResultCodes.TransactionMismatch && HasDiag(r, "TRANSACTION_ID_MISSING"), "replace without id refused");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched by the refused replace");
        }

        private static void OldSnapshotsStayIntact()
        {
            var f = Build();
            var old = f.Store.Current;
            Check.True(f.Store.Commit(new TransactionId("t1"), Earn(80)).Succeeded, "commit");
            var now = f.Store.Current;

            Check.True(!ReferenceEquals(old, now) && !ReferenceEquals(old.State, now.State), "new snapshot and state");
            Check.Equal(0L, old.Revision, "old revision");
            Check.Equal(10L, Wallet(old), "old wallet intact");
            Check.Equal(90L, Wallet(now), "new wallet");
            Check.True(ReferenceEquals(old.State.Known, now.State.Known), "untouched part shared");
        }

        private static void ReplaceInstallsWholeState()
        {
            var f = Build();
            Check.True(f.Store.Commit(new TransactionId("t1"), ClaimAndEarn("node.once", 10)).Succeeded, "setup commit");
            f.Queue.Drain();
            f.Seen.Clear();

            var loaded = Read(Tree(wallet: 77, claimed: "node.once").With("futureField", S("kept")));
            var r = f.Store.Replace(loaded, new TransactionId("load1"));
            Check.True(r.Succeeded, "replace");
            Check.Equal(2L, f.Store.Current.Revision, "store assigns current + 1");
            Check.True(ReferenceEquals(f.Store.Current.State, loaded), "whole state replaced as is");
            var tree = SaveDocument.Write(f.Store.Current.State);
            Check.Node(S("kept"), tree.Members["futureField"], "unknown field preserved");
            Check.Node(Obj(P("open", DataNode.True)), tree.Members["shopState"], "deferred field preserved");
            Check.Equal(0, f.Seen.Count, "not inline");
            f.Queue.Drain();
            Check.Equal(1, f.Seen.Count, "notified");
            Check.Equal(1L, f.Seen[0].PreviousRevision, "previous");
            Check.Equal(2L, f.Seen[0].Revision, "new");

            var before = f.Store.Current;
            var resubmitted = f.Store.Commit(new TransactionId("t1"), ClaimAndEarn("node.once", 10));
            Check.True(!resubmitted.Succeeded && resubmitted.Result.Code == ResultCodes.ConditionFalse, "resubmitting a claim the loaded state holds is refused");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched by the resubmission");
            Check.Equal(77L, Wallet(f.Store.Current), "loaded wallet unchanged");

            Check.True(f.Store.Commit(new TransactionId("t2"), Earn(10)).Succeeded, "ordinary commit after load");
            Check.Equal(3L, f.Store.Current.Revision, "revision after load");
            Check.Equal(87L, Wallet(f.Store.Current), "commit runs on the replaced state");
            Check.Node(S("kept"), SaveDocument.Write(f.Store.Current.State).Members["futureField"], "ordinary commit keeps unrelated state");
        }

        private static void NotificationsAreFifoAndCapturedAtCommit()
        {
            var f = Build();
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
            var f = Build();
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
            Outcome<StateSnapshot> inner = null, innerReplace = null;
            f = Build();
            Check.True(f.Store.Commit(new TransactionId("outer"), s =>
            {
                inner = f.Store.Commit(new TransactionId("inner"), Earn(1));
                innerReplace = f.Store.Replace(Initial().State, new TransactionId("inner.replace"));
                return Earn(2)(s);
            }).Succeeded, "outer commit");
            Check.True(inner != null && !inner.Succeeded && inner.Result.Code == ResultCodes.InvariantViolation, "inner refused");
            Check.True(HasDiag(inner, "REENTRANT_COMMIT"), "reentrancy diagnostic");
            Check.True(innerReplace != null && !innerReplace.Succeeded && HasDiag(innerReplace, "REENTRANT_COMMIT"), "inner replace refused");
            Check.Equal(1L, f.Store.Current.Revision, "only the outer commit applied");
            Check.Equal(12L, Wallet(f.Store.Current), "only the outer earn applied");
        }

        // ------------------------------------------------------------------ builders and fakes

        private static DataNode I(long v) => DataNode.FromInteger(v);
        private static DataNode S(string v) => DataNode.FromString(v);
        private static KeyValuePair<string, DataNode> P(string k, DataNode v) => new KeyValuePair<string, DataNode>(k, v);
        private static DataNode Obj(params KeyValuePair<string, DataNode>[] members) => DataNode.FromObject(members);
        private static long Wallet(StateSnapshot s) => s.State.Progress.Wallet.Value;

        private static Func<GameState, Outcome<GameState>> Earn(long crowns) => s =>
        {
            var earned = s.Progress.Earn(crowns);
            return earned.Succeeded ? Outcome<GameState>.Ok(s.With(earned.Value), 0) : Outcome<GameState>.Fail(earned.Result.Code, 0, new List<Diagnostic>(earned.Result.Details).ToArray());
        };

        /// <summary>A once reward: claims the key in both ledgers, then earns. Refused with CONDITION_FALSE when the key is already claimed.</summary>
        private static Func<GameState, Outcome<GameState>> ClaimAndEarn(string key, long crowns) => s =>
        {
            var claimed = s.Progress.Claim(new CompletionKey(key));
            if (!claimed.Succeeded) return Outcome<GameState>.Fail(claimed.Result.Code, 0, new List<Diagnostic>(claimed.Result.Details).ToArray());
            return Earn(crowns)(s.With(claimed.Value));
        };

        /// <summary>A readable save tree with fields no rule reads (shopState) kept beside the typed ones.</summary>
        private static DataNode Tree(long wallet = 10, string claimed = null)
        {
            var m = new Dictionary<string, DataNode>(StringComparer.Ordinal)
            {
                { "saveVersion", I(2) },
                { "wallet", I(wallet) },
                { "completed", DataNode.FromList(claimed == null ? new[] { S("quest.q1") } : new[] { S("quest.q1"), S(claimed) }) },
                { "rewardLedger", DataNode.FromList(claimed == null ? new DataNode[0] : new[] { S(claimed) }) },
                { "shopState", Obj(P("open", DataNode.True)) }
            };
            TypedFields.FillMissing(m);
            return DataNode.FromObject(m);
        }

        private static GameState Read(DataNode tree)
        {
            var read = SaveDocument.Read(tree);
            Check.True(read.Succeeded, "test tree is a readable state");
            return read.Value;
        }

        private static StateSnapshot Initial() => new StateSnapshot(0, Read(Tree()));

        private sealed class Fixture
        {
            public FakeQueue Queue;
            public GameStore Store;
            public readonly List<StateCommitted> Seen = new List<StateCommitted>();
        }

        private static Fixture Build()
        {
            var f = new Fixture { Queue = new FakeQueue() };
            f.Store = new GameStore(Initial(), f.Queue);
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
