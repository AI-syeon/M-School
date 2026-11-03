using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;
using MagicCampus.Transactions.Core;

namespace MagicCampus.Tests.State
{
    public static class StateSpecs
    {
        public static void Run()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("JsonPointerDecoding", JsonPointerDecoding),
                new KeyValuePair<string, Action>("CommitAdvancesRevisionAndNotifiesNextTick", CommitAdvancesRevisionAndNotifiesNextTick),
                new KeyValuePair<string, Action>("ConflictingRevisions", ConflictingRevisions),
                new KeyValuePair<string, Action>("ParticipantFailureAndThrowLeaveStateUntouched", ParticipantFailureAndThrowLeaveStateUntouched),
                new KeyValuePair<string, Action>("InvariantRejection", InvariantRejection),
                new KeyValuePair<string, Action>("SameTransactionReplay", SameTransactionReplay),
                new KeyValuePair<string, Action>("DisjointParticipantsCommitAtomically", DisjointParticipantsCommitAtomically),
                new KeyValuePair<string, Action>("OwnershipAndOverlapRejected", OwnershipAndOverlapRejected),
                new KeyValuePair<string, Action>("MalformedAndArrayPaths", MalformedAndArrayPaths),
                new KeyValuePair<string, Action>("RegistryRefusesConflicts", RegistryRefusesConflicts),
                new KeyValuePair<string, Action>("OldSnapshotsStayIntact", OldSnapshotsStayIntact),
                new KeyValuePair<string, Action>("ReplaceAllSuccessAndFailure", ReplaceAllSuccessAndFailure),
                new KeyValuePair<string, Action>("CompletionReplayUsesPersistedLedger", CompletionReplayUsesPersistedLedger),
                new KeyValuePair<string, Action>("UnregisteredParticipantRefusesWholePlan", UnregisteredParticipantRefusesWholePlan),
                new KeyValuePair<string, Action>("NotificationsAreFifoAndCapturedAtCommit", NotificationsAreFifoAndCapturedAtCommit),
                new KeyValuePair<string, Action>("EmptyPlanDoesNotAdvance", EmptyPlanDoesNotAdvance),
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

        private static void JsonPointerDecoding()
        {
            var p = JsonPointer.Parse("/a~1b/c~0d/~01");
            Check.Equal(3, p.Tokens.Count, "token count");
            Check.Equal("a/b", p.Tokens[0], "~1 decodes to slash");
            Check.Equal("c~d", p.Tokens[1], "~0 decodes to tilde");
            Check.Equal("~1", p.Tokens[2], "~01 decodes to literal ~1");

            var bad = new[] { null, "", "a", "/", "/a/", "/a//b", "/a~", "/a~2", "/a~x/b" };
            foreach (var b in bad)
                Check.True(!JsonPointer.TryParse(b, out var _, out var err) && !string.IsNullOrEmpty(err), "must be malformed: " + (b ?? "null"));

            var parent = JsonPointer.Parse("/player");
            var child = JsonPointer.Parse("/player/hp");
            var other = JsonPointer.Parse("/players");
            Check.True(parent.IsPrefixOf(child) && parent.Overlaps(child) && child.Overlaps(parent), "ancestor overlap");
            Check.True(!child.IsPrefixOf(parent), "child is not a prefix of parent");
            Check.True(!parent.Overlaps(other), "token wise, not string wise");
            Check.True(parent.IsPrefixOf(parent), "equal path is a prefix");
        }

        private static void CommitAdvancesRevisionAndNotifiesNextTick()
        {
            var wallet = new FakeParticipant("wallet", new[] { "/wallet" }, Set(P("/wallet", I(20))));
            var f = Build(null, wallet);
            var o = f.Store.Commit(Plan("t1", 0, "wallet"));
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

        private static void ConflictingRevisions()
        {
            var a = new FakeParticipant("a", new[] { "/wallet" }, Set(P("/wallet", I(11))));
            var b = new FakeParticipant("b", new[] { "/flags" }, Set(P("/flags/x", I(1))));
            var f = Build(null, a, b);
            var first = f.Store.Commit(Plan("t1", 0, "a"));
            var stale = Plan("t2", 0, "b");
            var second = f.Store.Commit(stale);
            Check.True(first.Succeeded, "first wins");
            Check.True(!second.Succeeded, "second conflicts");
            Check.Equal(ResultCodes.RevisionConflict, second.Result.Code, "conflict code");
            Check.Equal(0, b.Calls, "stale plan never reaches its participant");
            Check.Equal(0, f.Store.Current.Slice("flags").Members.Count, "flags untouched");
            Check.Node(I(11), f.Store.Current.Slice("wallet"), "first change kept");

            var retry = f.Store.Commit(Plan("t3", 1, "b"));
            Check.True(retry.Succeeded, "new plan on new revision succeeds");
            Check.Equal(2L, f.Store.Current.Revision, "revision 2");
            Check.Node(I(1), f.Store.Current.Slice("flags").Members["x"], "member added under owned prefix");
        }

        private static void ParticipantFailureAndThrowLeaveStateUntouched()
        {
            var ok = new FakeParticipant("a", new[] { "/wallet" }, Set(P("/wallet", I(99))));
            var failing = new FakeParticipant("b", new[] { "/flags" },
                b => Outcome<StateReplacement>.Fail(ResultCodes.ConditionFalse, b.Revision, new Diagnostic("NOPE", "no", null, "", "")));
            var throwing = new FakeParticipant("c", new[] { "/completed" }, b => throw new InvalidOperationException("boom"));
            var f = Build(null, ok, failing, throwing);

            var o1 = ExpectFail(f, Plan("t1", 0, "a", "b"), ResultCodes.ConditionFalse, "participant failure");
            Check.True(HasDiag(o1, "NOPE"), "participant diagnostic propagated");
            var o2 = ExpectFail(f, Plan("t2", 0, "a", "c"), ResultCodes.InvariantViolation, "participant throw");
            Check.True(HasDiag(o2, "PARTICIPANT_EXCEPTION"), "throw becomes diagnostic");
            Check.Node(I(10), f.Store.Current.Slice("wallet"), "healthy participant's change not applied");
            Check.Equal(0, f.Queue.Pending, "no notification for failures");
        }

        private static void InvariantRejection()
        {
            var wallet = new FakeParticipant("wallet", new[] { "/wallet" }, Set(P("/wallet", I(-1))));
            var f = Build(new IStateInvariant[] { new WalletNonNegative() }, wallet);
            var o = ExpectFail(f, Plan("t1", 0, "wallet"), ResultCodes.InvariantViolation, "invariant");
            Check.True(HasDiag(o, "WALLET_NEGATIVE"), "invariant diagnostic");
            Check.Equal(0L, f.Store.Current.Revision, "revision unchanged");
            Check.Equal(0, f.Queue.Pending, "no notification");
        }

        private static void SameTransactionReplay()
        {
            var wallet = new FakeParticipant("wallet", new[] { "/wallet" }, Set(P("/wallet", I(20))));
            var failing = new FakeParticipant("bad", new[] { "/flags" },
                b => Outcome<StateReplacement>.Fail(ResultCodes.ConditionFalse, b.Revision, new Diagnostic("NOPE", "no", null, "", "")));
            var f = Build(null, wallet, failing);

            var first = f.Store.Commit(Plan("t1", 0, "wallet"));
            var again = f.Store.Commit(Plan("t1", 0, "wallet"));
            Check.True(ReferenceEquals(first, again), "same outcome object returned");
            Check.Equal(1, wallet.Calls, "applied once");
            Check.Equal(1L, f.Store.Current.Revision, "revision advanced once");
            Check.Equal(1, f.Queue.Pending, "one notification");

            var fail1 = f.Store.Commit(Plan("tf", 1, "bad"));
            var fail2 = f.Store.Commit(Plan("tf", 1, "bad"));
            Check.True(!fail1.Succeeded && ReferenceEquals(fail1, fail2), "failure is replayed as first result");
            Check.Equal(1, failing.Calls, "failed plan not prepared again");
        }

        private static void DisjointParticipantsCommitAtomically()
        {
            var world = new FakeParticipant("world", new[] { "/player/x", "/player/y" }, Set(P("/player/x", DataNode.FromNumber(3.5)), P("/player/y", I(4))));
            var vitals = new FakeParticipant("vitals", new[] { "/player/hp" }, Set(P("/player/hp", I(90))));
            var f = Build(null, world, vitals);
            var start = f.Store.Current;

            var o = f.Store.Commit(Plan("t1", 0, "world", "vitals"));
            Check.True(o.Succeeded, "commit");
            Check.Equal(1L, f.Store.Current.Revision, "exactly one revision advance");
            Check.True(ReferenceEquals(world.Bases[0], start) && ReferenceEquals(vitals.Bases[0], start), "all participants saw the same basis");
            var player = f.Store.Current.Slice("player");
            Check.Node(DataNode.FromNumber(3.5), player.Members["x"], "x");
            Check.Node(I(4), player.Members["y"], "y");
            Check.Node(I(90), player.Members["hp"], "hp");
            Check.Node(I(50), player.Members["focus"], "unowned sibling preserved");
            Check.Node(S("A"), player.Members["name"], "unowned sibling preserved");
            f.Queue.Drain();
            Check.Equal(1, f.Seen.Count, "one notification for the whole commit");

            var broken = new FakeParticipant("broken", new[] { "/wallet" }, b => throw new InvalidOperationException("x"));
            var g = Build(null, new FakeParticipant("world", new[] { "/player/x" }, Set(P("/player/x", I(1)))), broken);
            ExpectFail(g, Plan("t1", 0, "world", "broken"), ResultCodes.InvariantViolation, "second participant throws");
            Check.Node(DataNode.FromNumber(7.5), g.Store.Current.Slice("player").Members["x"], "first participant's write not applied");
        }

        private static void OwnershipAndOverlapRejected()
        {
            var wrongOwner = new FakeParticipant("p", new[] { "/wallet" }, Set(P("/flags/x", I(1))));
            ExpectFail(Build(null, wrongOwner), Plan("t1", 0, "p"), ResultCodes.OutOfScope, "unowned path");

            var ancestor = new FakeParticipant("p", new[] { "/player/hp" }, Set(P("/player", Obj())));
            ExpectFail(Build(null, ancestor), Plan("t1", 0, "p"), ResultCodes.OutOfScope, "ancestor of owned path");

            var sibling = new FakeParticipant("p", new[] { "/player/hp" }, Set(P("/player/hpx", I(1))));
            ExpectFail(Build(null, sibling), Plan("t1", 0, "p"), ResultCodes.OutOfScope, "prefix match is token wise");

            var self = new FakeParticipant("p", new[] { "/player" }, Set(P("/player", Obj()), P("/player/hp", I(1))));
            ExpectFail(Build(null, self), Plan("t1", 0, "p"), ResultCodes.SliceOverlap, "overlap inside one participant");

            var a = new FakeParticipant("a", new[] { "/player" }, Set(P("/player/focus", I(1))));
            var b = new FakeParticipant("b", new[] { "/player/focus" }, Set(P("/player/focus", I(2))));
            var reg = new LooseRegistry();
            reg.Add(a);
            reg.Add(b);
            var store = new StateStore(Initial(), reg, null, new FakeQueue(), new PersistedCompletionReplayPolicy());
            var before = store.Current;
            var o = store.Commit(Plan("t1", 0, "a", "b"));
            Check.Equal(ResultCodes.SliceOverlap, o.Result.Code, "cross participant overlap");
            Check.True(ReferenceEquals(before, store.Current), "state untouched");
        }

        private static void MalformedAndArrayPaths()
        {
            var owned = new[] { "/wallet", "/quickSlots", "/weird", "/flags" };
            var cases = new[]
            {
                new[] { "wallet", ResultCodes.ParseError },
                new[] { "/wallet/", ResultCodes.ParseError },
                new[] { "/weird/a~2b", ResultCodes.ParseError },
                new[] { "/quickSlots/0", ResultCodes.ParseError },
                new[] { "/quickSlots/-", ResultCodes.ParseError },
                new[] { "/flags/missing/deeper", ResultCodes.RefNotFound },
                new[] { "/wallet/x", ResultCodes.RefNotFound },
                new[] { "/player/hp", ResultCodes.OutOfScope },
            };
            for (var i = 0; i < cases.Length; i++)
            {
                var p = new FakeParticipant("p", owned, Set(P(cases[i][0], I(1))));
                ExpectFail(Build(null, p), Plan("t" + i, 0, "p"), cases[i][1], "path " + cases[i][0]);
            }

            var whole = new FakeParticipant("p", owned, Set(P("/quickSlots", Arr(S("a"), S("b"), S("c")))));
            var f = Build(null, whole);
            Check.True(f.Store.Commit(Plan("t1", 0, "p")).Succeeded, "array replaced as a unit");
            Check.Equal(3, f.Store.Current.Slice("quickSlots").Items.Count, "array length");

            var escaped = new FakeParticipant("p", owned, Set(P("/weird/a~1b", I(5)), P("/weird/m~0n", I(6))));
            var g = Build(null, escaped);
            Check.True(g.Store.Commit(Plan("t1", 0, "p")).Succeeded, "escaped keys");
            var weird = g.Store.Current.Slice("weird");
            Check.Node(I(5), weird.Members["a/b"], "a/b");
            Check.Node(I(6), weird.Members["m~n"], "m~n");
        }

        private static void RegistryRefusesConflicts()
        {
            var reg = new StateParticipantRegistry();
            var ownedList = new List<string> { "/player" };
            var p1 = new FakeParticipant("p1", ownedList, Set());
            Check.True(reg.Register(p1).Status == OperationStatus.Succeeded, "first registration");

            Check.Equal(ResultCodes.SliceOverlap, reg.Register(new FakeParticipant("p2", new[] { "/player/hp" }, Set())).Code, "descendant overlap");
            Check.Equal(ResultCodes.SliceOverlap, reg.Register(new FakeParticipant("p3", new[] { "/player" }, Set())).Code, "equal overlap");
            Check.Equal(ResultCodes.HandleDuplicate, reg.Register(new FakeParticipant("p1", new[] { "/other" }, Set())).Code, "duplicate id");
            Check.Equal(ResultCodes.ContentInvalid, reg.Register(new FakeParticipant("p4", new[] { "player" }, Set())).Code, "malformed owned pointer");
            Check.Equal(ResultCodes.ContentInvalid, reg.Register(new FakeParticipant("p5", new string[0], Set())).Code, "no owned slices");
            Check.Equal(ResultCodes.ContentInvalid, reg.Register(new FakeParticipant("bad id", new[] { "/q" }, Set())).Code, "invalid id");
            Check.True(reg.Register(new FakeParticipant("p6", new[] { "/wallet" }, Set())).Status == OperationStatus.Succeeded, "disjoint participant");

            ownedList.Add("/wallet");
            Check.Equal(1, reg.Find("p1").OwnedSlices.Count, "ownership frozen at registration");

            reg.Seal();
            Check.Equal(ResultCodes.ContentInvalid, reg.Register(new FakeParticipant("p7", new[] { "/late" }, Set())).Code, "sealed registry");
            Check.True(reg.Find("nope") == null && reg.Find(null) == null, "unknown id is null");
        }

        private static void OldSnapshotsStayIntact()
        {
            var vitals = new FakeParticipant("vitals", new[] { "/player/hp" }, Set(P("/player/hp", I(90))));
            var f = Build(null, vitals);
            var old = f.Store.Current;
            Check.True(f.Store.Commit(Plan("t1", 0, "vitals")).Succeeded, "commit");
            var now = f.Store.Current;

            Check.True(!ReferenceEquals(old, now) && !ReferenceEquals(old.Root, now.Root), "new snapshot and root");
            Check.Equal(0L, old.Revision, "old revision");
            Check.Node(I(100), old.Slice("player").Members["hp"], "old hp intact");
            Check.Node(I(90), now.Slice("player").Members["hp"], "new hp");
            Check.True(ReferenceEquals(old.Slice("shopState"), now.Slice("shopState")), "untouched branch shared");
            var dict = old.Root.Members as IDictionary<string, DataNode>;
            Check.True(dict == null || dict.IsReadOnly, "members are not writable");
        }

        private static void ReplaceAllSuccessAndFailure()
        {
            var wallet = new FakeParticipant("wallet", new[] { "/wallet" }, Set(P("/wallet", I(20))));
            var f = Build(new IStateInvariant[] { new WalletNonNegative() }, wallet);
            Check.True(f.Store.Commit(Plan("t1", 0, "wallet")).Succeeded, "setup commit");
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

            var reuse = f.Store.Commit(Plan("t1", 2, "wallet"));
            Check.True(reuse.Succeeded && wallet.Calls == 2, "memory replay cache was cleared by the full replacement");
            Check.Equal(3L, f.Store.Current.Revision, "ordinary commit after load");
            Check.Node(S("kept"), f.Store.Current.Slice("futureField"), "ordinary commit keeps unrelated state");

            var before = f.Store.Current;
            var bad = new StateSnapshot(0, Initial().Root.With("wallet", I(-5)));
            var rejected = f.Store.ReplaceAll(bad, 3, new TransactionId("load2"));
            Check.Equal(ResultCodes.InvariantViolation, rejected.Result.Code, "invariant rejects candidate");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched after failed replacement");
            Check.True(ReferenceEquals(rejected, f.Store.ReplaceAll(bad, 3, new TransactionId("load2"))), "failed replacement replays");

            var stale = f.Store.ReplaceAll(cand, 1, new TransactionId("load3"));
            Check.Equal(ResultCodes.RevisionConflict, stale.Result.Code, "expected revision required");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched after conflict");
        }

        private static void CompletionReplayUsesPersistedLedger()
        {
            var ev = new FakeParticipant("ev", new[] { "/wallet", "/completed", "/rewardLedger" },
                Set(P("/wallet", I(50)), P("/completed", Arr(S("quest.q1"), S("quest.q2")))));
            var f = Build(null, ev);

            var done = ExpectFail(f, PlanWith("t1", 0, "quest.q1", "ev"), ResultCodes.ConditionFalse, "already completed key");
            Check.True(HasDiag(done, "COMPLETION_ALREADY_RECORDED"), "replay diagnostic");
            Check.Equal(0, ev.Calls, "participant not asked");

            Check.True(f.Store.Commit(PlanWith("t2", 0, "quest.q2", "ev")).Succeeded, "fresh key commits");
            ExpectFail(f, PlanWith("t3", 1, "quest.q2", "ev"), ResultCodes.ConditionFalse, "same key with a new transaction id");
            Check.Equal(2, f.Store.Current.Slice("completed").Items.Count, "no transaction ids written into schema fields");
            Check.Equal(0, f.Store.Current.Slice("rewardLedger").Items.Count, "ledger untouched");
        }

        private static void UnregisteredParticipantRefusesWholePlan()
        {
            var wallet = new FakeParticipant("wallet", new[] { "/wallet" }, Set(P("/wallet", I(1))));
            var f = Build(null, wallet);
            ExpectFail(f, Plan("t1", 0, "wallet", "ghost"), ResultCodes.Unregistered, "unknown participant");
            Check.Equal(0, wallet.Calls, "no participant prepared");
        }

        private static void NotificationsAreFifoAndCapturedAtCommit()
        {
            var wallet = new FakeParticipant("wallet", new[] { "/wallet" }, b => Outcome<StateReplacement>.Ok(new StateReplacement(new[] { P("/wallet", I(b.Revision + 100)) }), b.Revision));
            var f = Build(null, wallet);
            var order = new List<string>();
            f.Store.OnCommitted(c => order.Add("early@" + c.Revision));
            Check.True(f.Store.Commit(Plan("t1", 0, "wallet")).Succeeded, "first");
            f.Store.OnCommitted(c => order.Add("late@" + c.Revision));
            Check.True(f.Store.Commit(Plan("t2", 1, "wallet")).Succeeded, "second");
            Check.Equal(0, order.Count, "nothing inline");
            f.Queue.Drain();
            Check.Equal("early@1,early@2,late@2", string.Join(",", order), "commit order, listeners captured at commit");
        }

        private static void EmptyPlanDoesNotAdvance()
        {
            var wallet = new FakeParticipant("wallet", new[] { "/wallet" }, Set(P("/wallet", I(1))));
            var f = Build(null, wallet);
            var before = f.Store.Current;
            var o = f.Store.Commit(new StatePlan(new TransactionId("t1"), null, 0, null));
            Check.True(o.Succeeded, "empty plan succeeds");
            Check.Equal(0L, o.Result.Revision, "no revision advance");
            Check.True(ReferenceEquals(before, f.Store.Current), "state untouched");
            Check.Equal(0, f.Queue.Pending, "no notification");
        }

        private static void ReentrantCommitRefused()
        {
            Fixture f = null;
            Outcome<StateSnapshot> inner = null;
            var p = new FakeParticipant("wallet", new[] { "/wallet" }, b =>
            {
                inner = f.Store.Commit(Plan("inner", 0, "wallet"));
                return Outcome<StateReplacement>.Ok(new StateReplacement(new[] { P("/wallet", I(2)) }), b.Revision);
            });
            f = Build(null, p);
            Check.True(f.Store.Commit(Plan("outer", 0, "wallet")).Succeeded, "outer commit");
            Check.True(inner != null && !inner.Succeeded && inner.Result.Code == ResultCodes.InvariantViolation, "inner refused");
            Check.True(HasDiag(inner, "REENTRANT_COMMIT"), "reentrancy diagnostic");
            Check.Equal(1L, f.Store.Current.Revision, "only the outer commit applied");
        }

        // ------------------------------------------------------------------ builders and fakes

        private static DataNode I(long v) => DataNode.FromInteger(v);
        private static DataNode S(string v) => DataNode.FromString(v);
        private static KeyValuePair<string, DataNode> P(string k, DataNode v) => new KeyValuePair<string, DataNode>(k, v);
        private static DataNode Obj(params KeyValuePair<string, DataNode>[] members) => DataNode.FromObject(members);
        private static DataNode Arr(params DataNode[] items) => DataNode.FromList(items);

        private static StateSnapshot Initial()
        {
            return new StateSnapshot(0, Obj(
                P("saveVersion", I(2)),
                P("player", Obj(P("hp", I(100)), P("focus", I(50)), P("x", DataNode.FromNumber(7.5)), P("y", I(2)), P("name", S("A")))),
                P("wallet", I(10)),
                P("flags", Obj()),
                P("completed", Arr(S("quest.q1"))),
                P("rewardLedger", Arr()),
                P("quickSlots", Arr(DataNode.Null, DataNode.Null)),
                P("weird", Obj(P("a/b", I(1)), P("m~n", I(2)))),
                P("shopState", Obj(P("open", DataNode.True)))));
        }

        private static StatePlan Plan(string tx, long rev, params string[] participants)
        {
            var list = new List<PlannedChange>();
            foreach (var p in participants) list.Add(new PlannedChange(p, "op", null));
            return new StatePlan(new TransactionId(tx), null, rev, list);
        }

        private static StatePlan PlanWith(string tx, long rev, string completion, params string[] participants)
        {
            var list = new List<PlannedChange>();
            foreach (var p in participants) list.Add(new PlannedChange(p, "op", null));
            return new StatePlan(new TransactionId(tx), new CompletionKey(completion), rev, list);
        }

        private static Func<StateSnapshot, Outcome<StateReplacement>> Set(params KeyValuePair<string, DataNode>[] slices)
            => b => Outcome<StateReplacement>.Ok(new StateReplacement(slices), b.Revision);

        private sealed class Fixture
        {
            public StateParticipantRegistry Registry;
            public FakeQueue Queue;
            public StateStore Store;
            public readonly List<StateCommitted> Seen = new List<StateCommitted>();
        }

        private static Fixture Build(IEnumerable<IStateInvariant> invariants, params FakeParticipant[] participants)
        {
            var f = new Fixture { Registry = new StateParticipantRegistry(), Queue = new FakeQueue() };
            foreach (var p in participants)
            {
                var r = f.Registry.Register(p);
                Check.True(r.Status == OperationStatus.Succeeded, "register " + p.ParticipantId);
            }
            f.Registry.Seal();
            f.Store = new StateStore(Initial(), f.Registry, invariants, f.Queue, new PersistedCompletionReplayPolicy());
            f.Store.OnCommitted(c => f.Seen.Add(c));
            return f;
        }

        private static Outcome<StateSnapshot> ExpectFail(Fixture f, IStatePlan plan, string code, string what)
        {
            var before = f.Store.Current;
            var o = f.Store.Commit(plan);
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

        private sealed class FakeParticipant : IStateParticipant
        {
            private readonly Func<StateSnapshot, Outcome<StateReplacement>> behavior;
            public string ParticipantId { get; }
            public IReadOnlyList<string> OwnedSlices { get; }
            public int Calls;
            public readonly List<StateSnapshot> Bases = new List<StateSnapshot>();

            public FakeParticipant(string id, IReadOnlyList<string> owned, Func<StateSnapshot, Outcome<StateReplacement>> behavior)
            {
                ParticipantId = id;
                OwnedSlices = owned;
                this.behavior = behavior;
            }

            public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan)
            {
                Calls++;
                Bases.Add(basis);
                return behavior(basis);
            }
        }

        private sealed class LooseRegistry : IStateParticipantRegistry
        {
            private readonly Dictionary<string, IStateParticipant> map = new Dictionary<string, IStateParticipant>();
            public void Add(IStateParticipant p) { map[p.ParticipantId] = p; }
            public IStateParticipant Find(string participantId) => participantId != null && map.TryGetValue(participantId, out var p) ? p : null;
        }

        private sealed class FakeQueue : ICompletionQueue
        {
            private readonly List<Action> pending = new List<Action>();
            public int Pending => pending.Count;
            public void Post(Action action) { pending.Add(action); }
            public void Post(CompletionKey key, Action action) { pending.Add(action); }
            public void Drain()
            {
                var run = pending.ToArray();
                pending.Clear();
                foreach (var a in run) a();
            }
        }

        private sealed class WalletNonNegative : IStateInvariant
        {
            public IReadOnlyList<Diagnostic> Validate(StateSnapshot candidate)
            {
                var w = candidate.Slice("wallet");
                if (w != null && w.Kind == DataKind.Integer && w.Integer < 0)
                    return new[] { new Diagnostic("WALLET_NEGATIVE", "wallet must be >= 0", null, ">=0", w.Integer.ToString()) };
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
