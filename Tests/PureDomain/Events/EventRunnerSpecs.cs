using System;
using System.Collections.Generic;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Contracts;
using MagicCampus.Events;
using MagicCampus.Persistence;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Tests.PureDomain.Support;
using MagicCampus.Transactions;
using MagicCampus.Transactions.Conditions;
using MagicCampus.World.Movement;

namespace MagicCampus.Tests.Events
{
    public static class EventRunnerSpecs
    {
        public static void Run()
        {
            BusyRefusalTouchesNothing();
            ReentrantStartIsBusy();
            IntroFalseRefusesWithoutSideEffects();
            ProjectionThenCaptureThenNotifyThenSave();
            EntrySaveDelaysEverythingElse();
            EntrySaveFailureAbortsBeforeAnyMovement();
            ActualArrivalIsRequired();
            OnlySelectedForkRuns();
            NestedForkResumesParent();
            BranchPicksThenElseAndBoundsRecursion();
            AsyncFailureWaitsForSiblings();
            ImmediateInstructionsYieldAt64();
            PerformIsRefusedAsUnregistered();
            OnceGuardRefusesBeforeLeasing();
            SingleCommitWithLatestConditionRecheck();
            SuccessKeepsMemoryWhenSaveFails();
            SceneAbortRestoresMapThenStateAndKeepsAccepted();
            MapRestoreFailureIsExposed();
            MapRestoreTimeoutCancelsChildBeforeCleanup();
            RoomAbortNeverReplacesGlobalState();
            TimeoutCancelsAndRecovers();
            ReplayDoesNotRerunAndRefusalIsNotCached();
            LateCallbacksAreIgnored();
        }

        // ------------------------------------------------------------------ specs

        private static void BusyRefusalTouchesNothing()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.a", ExecutionMode.Scene, new[] { "res.x" }, new CompiledCall[0]));
            rig.Catalog.Add(Ev("ev.b", ExecutionMode.Scene, new[] { "res.y" }, new CompiledCall[0]));
            var a = rig.Start("ev.a", "owner.a");
            var b = rig.Start("ev.b", "owner.b");
            Check(b.Terminal != null && b.Terminal.Status == OperationStatus.Failed && b.Terminal.Code == ResultCodes.ResourceBusy, "second scene is busy on snapshot.world");
            Check(a.Status == OperationStatus.Running, "old run untouched");
            Check(rig.Recovery.Captures == 1 && rig.Pauses.Acquired == 1 && rig.Leases.ActiveLeaseCount == 1, "refusal captured/paused/leased nothing");
            Check(rig.Runner.ActiveOwner == "owner.a" && rig.Runner.ActiveCount == 1, "active owner exposed");
            Check(Contains(rig.Runner.ActiveResources, EventRunner.WorldResource) && Contains(rig.Runner.ActiveResources, "res.x"), "scene leases snapshot.world and declared resources");
            rig.Until(() => a.Terminal != null, "first run");
            Check(a.Terminal.Status == OperationStatus.Succeeded, "first run succeeds");
            rig.AssertClean();
        }

        private static void ReentrantStartIsBusy()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.outer", ExecutionMode.Scene, new string[0], new CompiledCall[0]));
            rig.Catalog.Add(Ev("ev.inner", ExecutionMode.RoomActivity, new[] { "res.z" }, new CompiledCall[0]));
            IHandle<EventCompletion> inner = null;
            rig.Pauses.OnAcquire = () => { if (inner == null) inner = rig.Runner.Start(new EventStartRequest("ev.inner", "owner.i", 1, new TransactionId("tx.inner"))); };
            var outer = rig.Start("ev.outer", "owner.o");
            Check(inner != null && inner.Terminal != null && inner.Terminal.Code == ResultCodes.ResourceBusy, "re-entrant start is busy");
            Check(outer.Status == OperationStatus.Running, "outer start unaffected");
            rig.Until(() => outer.Terminal != null, "outer");
            Check(outer.Terminal.Status == OperationStatus.Succeeded, "outer succeeds");
            rig.AssertClean();
        }

        private static void IntroFalseRefusesWithoutSideEffects()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.a", ExecutionMode.Scene, new[] { "res.x" }, new[] { Say("ev.a", 1, "hi") }));
            rig.Conditions.Value = false;
            var h = rig.Start("ev.a");
            Check(h.Terminal.Code == ResultCodes.ConditionFalse && h.Terminal.Status == OperationStatus.Failed, "false condition refuses");
            Check(rig.Leases.ActiveLeaseCount == 0 && rig.Recovery.Captures == 0 && rig.Pauses.Acquired == 0 && rig.Saves.Requests.Count == 0 && rig.Store.Commits.Count == 0, "nothing was touched");
            rig.Conditions.Value = true;
            var retry = rig.Start("ev.a");
            rig.Until(() => retry.Terminal != null, "retry");
            Check(retry.Terminal.Status == OperationStatus.Succeeded, "refusal is not cached for a new transaction");
            rig.AssertClean();
        }

        private static void ProjectionThenCaptureThenNotifyThenSave()
        {
            var rig = new Rig();
            rig.Projector.Moved = true;
            rig.Catalog.Add(Ev("ev.p", ExecutionMode.Scene, new string[0], new CompiledCall[0]));
            var h = rig.Start("ev.p", "owner.a", 0, "tx.p");
            CheckOrder(rig.Log, "project", "commit:tx.p.project", "capture", "notify:SceneEntry", "save.request");
            rig.Until(() => h.Terminal != null, "run");
            Check(h.Terminal.Status == OperationStatus.Succeeded && rig.Store.Commits.Count == 2, "projection commit plus one event commit");
            rig.AssertClean();
        }

        private static void EntrySaveDelaysEverythingElse()
        {
            var rig = new Rig();
            rig.Saves.Auto = null;
            rig.Catalog.Add(Ev("ev.m", ExecutionMode.Scene, new string[0], new[] { Move("ev.m", 1, null) }));
            var h = rig.Start("ev.m");
            rig.Ticks(5);
            Check(rig.Saves.Requests.Count == 1, "entry save requested");
            Check(rig.Moves.Pending.Count == 0 && rig.Staging.Count == 0, "no staging or movement before the entry save succeeds");
            CheckOrder(rig.Log, "capture", "notify:SceneEntry", "save.request");
            rig.Saves.Requests[0].Succeed(FakeSaves.Gen(), 0);
            rig.Saves.Auto = n => true; // Later autosave is independent of the delayed entry write.
            rig.Until(() => rig.Moves.Pending.Count == 1, "move after save");
            CheckOrder(rig.Log, "save.request", "stage", "move.request");
            rig.Moves.Arrive(0, true);
            rig.Until(() => h.Terminal != null, "run");
            Check(h.Terminal.Status == OperationStatus.Succeeded, "run succeeds");
            rig.AssertClean();
        }

        private static void EntrySaveFailureAbortsBeforeAnyMovement()
        {
            var rig = new Rig();
            rig.Saves.Auto = n => false;
            rig.Catalog.Add(Ev("ev.m", ExecutionMode.Scene, new string[0], new[] { Move("ev.m", 1, null) }));
            var h = rig.Start("ev.m");
            rig.Until(() => h.Terminal != null, "abort");
            Check(h.Terminal.Status == OperationStatus.Failed && h.Terminal.Code == "DISK_ERROR", "entry save failure fails the event");
            Check(rig.Moves.Pending.Count == 0 && rig.Staging.Count == 0 && rig.Store.Commits.Count == 0, "nothing ran");
            Check(rig.Recovery.Restores == 1 && rig.Store.ReplaceCount == 1, "entry recovered");
            rig.AssertClean();
        }

        private static void ActualArrivalIsRequired()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.a", ExecutionMode.Scene, new string[0], new[] { Move("ev.a", 1, null), Say("ev.a", 2, "after") }));
            var h = rig.Start("ev.a");
            rig.Until(() => rig.Moves.Pending.Count == 1, "move");
            Check(rig.Presentation.Said.Count == 0, "no say before arrival");
            rig.Moves.Arrive(0, false);
            rig.Until(() => h.Terminal != null, "failure");
            Check(h.Terminal.Status == OperationStatus.Failed && h.Terminal.Code == ResultCodes.NoRoute, "success without arrival is a failure");
            Check(rig.Presentation.Said.Count == 0 && rig.Store.Commits.Count == 0, "nothing after a false arrival");
            rig.AssertClean();

            var ok = new Rig();
            ok.Catalog.Add(Ev("ev.a", ExecutionMode.Scene, new string[0], new[] { Move("ev.a", 1, null), Say("ev.a", 2, "after") }));
            var h2 = ok.Start("ev.a");
            ok.Until(() => ok.Moves.Pending.Count == 1, "move");
            ok.Moves.Arrive(0, true);
            ok.Until(() => h2.Terminal != null, "success");
            Check(h2.Terminal.Status == OperationStatus.Succeeded && ok.Presentation.Said.Count == 1, "say only after real arrival");
            ok.AssertClean();
        }

        private static void OnlySelectedForkRuns()
        {
            var rig = new Rig();
            var forks = new Dictionary<string, IReadOnlyList<CompiledCall>>
            {
                { "a", new[] { Say("ev.c", 1, "A") } },
                { "b", new[] { Say("ev.c", 1, "B") } }
            };
            rig.Catalog.Add(Ev("ev.c", ExecutionMode.Scene, new string[0], new[] { Choice("ev.c", 1, "a", "b"), Say("ev.c", 2, "after") }, forks));
            var h = rig.Start("ev.c");
            rig.Until(() => rig.Presentation.Choices.Count == 1, "choice");
            Check(rig.Presentation.Choices[0].Options.Count == 2, "both options offered");
            rig.Presentation.Choices[0].Handle.Succeed(new ChoiceSelection("b", 1), 0);
            rig.Until(() => h.Terminal != null, "run");
            Check(h.Terminal.Status == OperationStatus.Succeeded, "run succeeds");
            Check(string.Join(",", rig.Presentation.Said) == "B,after", "only the selected fork ran, then the parent resumed");
            rig.AssertClean();
        }

        private static void NestedForkResumesParent()
        {
            var rig = new Rig();
            var forks = new Dictionary<string, IReadOnlyList<CompiledCall>>
            {
                { "a", new[] { Choice("ev.n", 1, "b"), Say("ev.n", 2, "inA") } },
                { "b", new[] { Say("ev.n", 1, "inB") } }
            };
            rig.Catalog.Add(Ev("ev.n", ExecutionMode.Scene, new string[0], new[] { Choice("ev.n", 1, "a"), Say("ev.n", 2, "end") }, forks));
            var h = rig.Start("ev.n");
            rig.Until(() => rig.Presentation.Choices.Count == 1, "first choice");
            rig.Presentation.Choices[0].Handle.Succeed(new ChoiceSelection("a", 0), 0);
            rig.Until(() => rig.Presentation.Choices.Count == 2, "nested choice");
            rig.Presentation.Choices[1].Handle.Succeed(new ChoiceSelection("b", 0), 0);
            rig.Until(() => h.Terminal != null, "run");
            Check(string.Join(",", rig.Presentation.Said) == "inB,inA,end", "nested fork resumes each parent in order");
            rig.AssertClean();
        }

        private static void BranchPicksThenElseAndBoundsRecursion()
        {
            Check(RunBranch(false) == "N,tail", "else fork taken");
            Check(RunBranch(true) == "Y,tail", "then fork taken");

            var rig = new Rig();
            var empty = new CompiledCall[0];
            var forks = new Dictionary<string, IReadOnlyList<CompiledCall>> { { "loop", new[] { Branch("ev.l", 1, empty, "loop", null) } } };
            rig.Catalog.Add(Ev("ev.l", ExecutionMode.Scene, new string[0], new[] { Branch("ev.l", 1, empty, "loop", null) }, forks));
            var h = rig.Start("ev.l");
            rig.Until(() => h.Terminal != null, "bounded recursion");
            Check(h.Terminal.Status == OperationStatus.Failed && h.Terminal.Code == ResultCodes.ContentInvalid, "fork recursion fails explicitly");
            Check(rig.Recovery.Restores == 1 && rig.Store.Commits.Count == 0, "recursion failure recovers the entry");
            rig.AssertClean();
        }

        private static string RunBranch(bool value)
        {
            var rig = new Rig();
            rig.Conditions.Rule = w => w.Count == 0 || value;
            var cond = new[] { Cmd("ev.br", 9, "eq") };
            var forks = new Dictionary<string, IReadOnlyList<CompiledCall>>
            {
                { "yes", new[] { Say("ev.br", 1, "Y") } },
                { "no", new[] { Say("ev.br", 1, "N") } }
            };
            rig.Catalog.Add(Ev("ev.br", ExecutionMode.Scene, new string[0], new[] { Branch("ev.br", 1, cond, "yes", "no"), Say("ev.br", 2, "tail") }, forks));
            var h = rig.Start("ev.br");
            rig.Until(() => h.Terminal != null, "branch run");
            Check(h.Terminal.Status == OperationStatus.Succeeded, "branch run succeeds");
            rig.AssertClean();
            return string.Join(",", rig.Presentation.Said);
        }

        private static void AsyncFailureWaitsForSiblings()
        {
            var rig = new Rig();
            rig.Presentation.DeferCancel = true;
            var script = new[] { Animate("ev.s", 1, "npc.a1", "h1"), Animate("ev.s", 2, "npc.a2", "h2"), Await("ev.s", 3, "h1", "h2") };
            rig.Catalog.Add(Ev("ev.s", ExecutionMode.Scene, new string[0], script));
            var h = rig.Start("ev.s");
            rig.Until(() => rig.Presentation.OfKind("animate").Count == 2, "both animations");
            var ops = rig.Presentation.OfKind("animate");
            ops[0].Handle.Fail("ANIM_FAILED", 0, new Diagnostic[0]);
            rig.Until(() => ops[1].CancelRequested, "sibling cancel requested");
            rig.Ticks(5);
            Check(h.Terminal == null, "parent waits for the cancelled sibling to become terminal");
            Check(rig.Lifetime.Closed == 0 && rig.Leases.ActiveLeaseCount == 1 && rig.Pauses.Live == 1, "nothing released before every child is terminal");
            ops[1].Handle.Cancelled("done", 0);
            rig.Until(() => h.Terminal != null, "abort completes");
            Check(h.Terminal.Status == OperationStatus.Failed && h.Terminal.Code == "ANIM_FAILED", "first failure is reported");
            Check(rig.Lifetime.Closed == 1 && rig.Store.Commits.Count == 0, "display closed, nothing committed");
            rig.AssertClean();
        }

        private static void ImmediateInstructionsYieldAt64()
        {
            var rig = new Rig();
            var calls = new List<CompiledCall>();
            var names = new List<DataNode>();
            for (var i = 0; i < 70; i++)
            {
                calls.Add(Animate("ev.y", i + 1, "npc.n" + i, "h" + i));
                names.Add(S("h" + i));
            }
            calls.Add(Cmd("ev.y", 71, "await", A("handles", DataNode.FromList(names))));
            rig.Catalog.Add(Ev("ev.y", ExecutionMode.RoomActivity, new[] { "room.r" }, calls.ToArray()));
            var h = rig.Start("ev.y");
            Check(rig.Presentation.OfKind("animate").Count == 64, "exactly 64 immediate instructions, then yield");
            Check(!Contains(rig.Runner.ActiveResources, EventRunner.WorldResource), "room activity does not lease snapshot.world");
            rig.Runner.Tick(0);
            Check(rig.Presentation.OfKind("animate").Count == 70, "remaining instructions run on the next tick");
            foreach (var op in rig.Presentation.OfKind("animate")) op.Handle.Succeed(null, 0);
            rig.Until(() => h.Terminal != null, "run");
            Check(h.Terminal.Status == OperationStatus.Succeeded, "run succeeds");
            Check(rig.Recovery.Captures == 0 && rig.Staging.Count == 0, "room activity has no entry capture or staging");
            rig.AssertClean();
        }

        private static void PerformIsRefusedAsUnregistered()
        {
            var none = new Rig();
            none.Catalog.Add(Ev("ev.p", ExecutionMode.Scene, new string[0], new[] { Perform("ev.p", 1, "class", "class.alpha") }));
            none.Catalog.Add(Ev("ev.r", ExecutionMode.RoomActivity, new[] { "room.r" }, new[] { Perform("ev.r", 1, "class", "class.alpha") }));
            var refused = none.Start("ev.p");
            Check(refused.Terminal.Code == ResultCodes.Unregistered && refused.Terminal.Status == OperationStatus.Failed, "unregistered scene domain refused");
            var refusedRoom = none.Start("ev.r", "owner.r", 1, null);
            Check(refusedRoom.Terminal.Code == ResultCodes.Unregistered, "unregistered room domain refused");
            Check(none.Leases.ActiveLeaseCount == 0 && none.Recovery.Captures == 0 && none.Pauses.Acquired == 0, "refusal before any mutation");
            none.AssertClean();
        }

        private static void OnceGuardRefusesBeforeLeasing()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.o", ExecutionMode.Scene, new string[0], new CompiledCall[0], null, RepeatPolicy.Once));
            rig.Completed.Add("done.ev.o");
            var h = rig.Start("ev.o");
            Check(h.Terminal.Code == ResultCodes.ConditionFalse && rig.Store.Commits.Count == 0, "once ledger guard refuses before any commit");
            Check(rig.Leases.ActiveLeaseCount == 0 && rig.Recovery.Captures == 0, "nothing leased or captured");
            rig.Completed.Remove("done.ev.o");
            var ok = rig.Start("ev.o");
            rig.Until(() => ok.Terminal != null, "run");
            Check(ok.Terminal.Status == OperationStatus.Succeeded, "once event runs when not completed");
            rig.AssertClean();
        }

        private static void SingleCommitWithLatestConditionRecheck()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.c", ExecutionMode.Scene, new string[0], new[] { Say("ev.c", 1, "x") }));
            var h = rig.Start("ev.c", "owner.a", 0, "tx.c");
            rig.Until(() => h.Terminal != null, "run");
            Check(h.Terminal.Status == OperationStatus.Succeeded, "run succeeds");
            var mine = 0;
            foreach (var committed in rig.Store.Commits)
                if (committed.Value == "tx.c") mine++;
            Check(mine == 1 && rig.Store.Commits.Count == 1, "exactly one commit");
            Check(rig.Store.EventBases.Count == 1 && rig.Store.EventBases[0] == rig.Conditions.LastRevision, "the event commits on the revision whose conditions were just rechecked");
            rig.AssertClean();

            var flip = new Rig();
            flip.Catalog.Add(Ev("ev.c", ExecutionMode.Scene, new string[0], new[] { Say("ev.c", 1, "x") }));
            var f = flip.Start("ev.c", "owner.a", 0, "tx.f");
            flip.Until(() => flip.Presentation.Said.Count == 1, "say");
            flip.Conditions.Value = false;
            flip.Until(() => f.Terminal != null, "abort");
            Check(f.Terminal.Status == OperationStatus.Failed && f.Terminal.Code == ResultCodes.ConditionFalse, "changed condition aborts before commit");
            Check(flip.Store.Commits.Count == 0 && flip.Recovery.Restores == 1 && flip.Store.ReplaceCount == 1, "no commit, entry recovered");
            flip.AssertClean();
        }

        private static void SuccessKeepsMemoryWhenSaveFails()
        {
            var rig = new Rig();
            rig.Saves.Auto = n => n == 1 || n == 3 || n == 4;
            rig.Catalog.Add(Ev("ev.s", ExecutionMode.Scene, new string[0], new CompiledCall[0]));
            var h = rig.Start("ev.s", "owner.a", 0, "tx.s1");
            rig.Until(() => h.Terminal != null, "run");
            Check(h.Terminal.Status == OperationStatus.Succeeded, "memory success is kept");
            rig.Until(() => rig.Runner.LastSaveResult != null, "autosave result");
            Check(rig.Runner.LastSaveResult.Status == OperationStatus.Failed && rig.Runner.Unsaved, "disk failure exposed as unsaved");
            Check(rig.Store.ReplaceCount == 0 && rig.Store.Commits.Count == 1, "no rollback after the commit");
            CheckOrder(rig.Log, "commit:tx.s1", "clear", "lifetime.close", "pause.release", "lease.release", "notify:SceneComplete", "save.request");

            var again = rig.Start("ev.s", "owner.a", 1, "tx.s2");
            rig.Until(() => again.Terminal != null, "second run");
            rig.Until(() => !rig.Runner.Unsaved, "saved again");
            Check(rig.Runner.LastSaveResult.Status == OperationStatus.Succeeded, "successful autosave clears unsaved");
            rig.AssertClean();
        }

        private static void SceneAbortRestoresMapThenStateAndKeepsAccepted()
        {
            var rig = new Rig();
            rig.Maps.Auto = null;
            rig.Catalog.Add(Ev("ev.a", ExecutionMode.Scene, new string[0], new[] { Move("ev.a", 1, null) }));
            var h = rig.Start("ev.a");
            rig.Until(() => rig.Moves.Pending.Count == 1, "move");
            var appointments = DataNode.FromList(new[] { Obj(A("id", S("ev.later")), A("status", S("Accepted")), A("acceptedAt", DataNode.FromInteger(3))) });
            rig.Recovery.RestoreOverride = SnapshotTrees.Snapshot(1, rig.Store.Current.Tree().With("appointments", appointments));
            rig.Maps.ActiveMapId = "map.b";
            h.Cancel("player");
            rig.Until(() => rig.Maps.Loads.Count == 1, "entry map load");
            Check(rig.Recovery.Restores == 0 && h.Terminal == null, "state restore waits for the map");
            rig.Maps.ActiveMapId = "map.a";
            rig.Maps.Loads[0].Succeed(null, 0);
            rig.Until(() => h.Terminal != null, "cancel completes");
            Check(h.Terminal.Status == OperationStatus.Cancelled, "cancelled status");
            CheckOrder(rig.Log, "load:map.a", "restore", "clear", "pause.release", "lease.release");
            Check(rig.Store.ReplaceCount == 1 && rig.Store.Current.State.Appointments.Find("ev.later") != null, "one replacement keeps the accepted reservation");
            Check(rig.Store.Commits.Count == 0 && rig.Runner.CleanupDiagnostics.Count == 0, "no commit, clean recovery");
            Check(rig.Log.Contains("renew:WorldSafe"), "cancel renews normal safe snapshot tracking without publishing success");
            rig.AssertClean();
        }

        private static void MapRestoreTimeoutCancelsChildBeforeCleanup()
        {
            var rig = new Rig();
            rig.Maps.Auto = null;
            rig.Catalog.Add(Ev("ev.recovery_timeout", ExecutionMode.Scene, new string[0], new[] { Move("ev.recovery_timeout",1,null) }));
            var handle = rig.Start("ev.recovery_timeout");
            rig.Until(() => rig.Moves.Pending.Count == 1,"move before recovery timeout");
            rig.Maps.ActiveMapId = "map.b";
            handle.Cancel("player");
            rig.Until(() => rig.Maps.Loads.Count == 1,"pending recovery load");
            rig.Until(() => handle.Terminal != null,"recovery watchdog");
            Check(rig.Maps.Loads[0].Status == OperationStatus.Cancelled,"recovery child terminal before parent");
            Check(rig.Runner.CleanupDiagnostics.Count > 0,"recovery timeout diagnostic retained");
            rig.AssertClean();
        }

        private static void MapRestoreFailureIsExposed()
        {
            var rig = new Rig();
            rig.Maps.Auto = false;
            rig.Catalog.Add(Ev("ev.a", ExecutionMode.Scene, new string[0], new[] { Move("ev.a", 1, null) }));
            var h = rig.Start("ev.a");
            rig.Until(() => rig.Moves.Pending.Count == 1, "move");
            rig.Maps.ActiveMapId = "map.b";
            h.Cancel("player");
            rig.Until(() => h.Terminal != null, "cleanup");
            var seen = false;
            foreach (var diag in rig.Runner.CleanupDiagnostics)
                if (diag.Code == ResultCodes.MapLoadFailed) seen = true;
            Check(seen, "map restore failure exposed");
            Check(rig.Store.ReplaceCount == 1 && rig.Recovery.Clears == 1, "state and recovery still cleaned up");
            rig.AssertClean();
        }

        private static void RoomAbortNeverReplacesGlobalState()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.r", ExecutionMode.RoomActivity, new[] { "room.r" }, new[] { Move("ev.r", 1, null) }));
            var h = rig.Start("ev.r");
            Check(rig.Moves.Pending.Count == 1, "room starts immediately without entry save");
            rig.Maps.ActiveMapId = "map.b";
            h.Cancel("leave");
            rig.Until(() => h.Terminal != null, "cancel");
            Check(h.Terminal.Status == OperationStatus.Cancelled, "cancelled");
            Check(rig.Store.ReplaceCount == 0 && rig.Recovery.Captures == 0 && rig.Recovery.Restores == 0 && rig.Recovery.Clears == 0 && rig.Maps.Loads.Count == 0, "no global rollback for rooms");
            Check(rig.Lifetime.Closed == 1, "display closed");
            rig.AssertClean();
        }

        private static void TimeoutCancelsAndRecovers()
        {
            var rig = new Rig(5);
            rig.Catalog.Add(Ev("ev.t", ExecutionMode.Scene, new string[0], new[] { Move("ev.t", 1, null) }));
            var h = rig.Start("ev.t");
            rig.Until(() => rig.Moves.Pending.Count == 1, "move");
            rig.Runner.Tick(6);
            rig.Until(() => h.Terminal != null, "timeout");
            Check(h.Terminal.Status == OperationStatus.Failed && h.Terminal.Code == ResultCodes.Timeout, "watchdog fails with TIMEOUT");
            Check(rig.Moves.Pending[0].CancelRequested && rig.Recovery.Restores == 1, "child cancelled and entry recovered");
            rig.AssertClean();
        }

        private static void ReplayDoesNotRerunAndRefusalIsNotCached()
        {
            var rig = new Rig();
            rig.Catalog.Add(Ev("ev.a", ExecutionMode.Scene, new string[0], new CompiledCall[0]));
            rig.Catalog.Add(Ev("ev.b", ExecutionMode.Scene, new string[0], new CompiledCall[0]));
            var a = rig.Start("ev.a", "owner.a", 0, "tx.1");
            var sameTxn = rig.Start("ev.a", "owner.a", 5, "tx.1");
            var sameInput = rig.Start("ev.a", "owner.a", 0, "tx.9");
            Check(ReferenceEquals(a, sameTxn) && ReferenceEquals(a, sameInput), "replays return the original handle");
            Check(rig.Recovery.Captures == 1 && rig.Leases.ActiveLeaseCount == 1, "replay neither reran nor regranted");
            var refused = rig.Start("ev.b", "owner.b", 1, "tx.2");
            Check(refused.Terminal.Code == ResultCodes.ResourceBusy, "busy while a runs");
            rig.Until(() => a.Terminal != null, "a");
            var retry = rig.Start("ev.b", "owner.b", 1, "tx.2");
            Check(!ReferenceEquals(refused, retry), "refusal was not cached");
            rig.Until(() => retry.Terminal != null, "retry");
            Check(retry.Terminal.Status == OperationStatus.Succeeded, "retry runs");
            var captures = rig.Recovery.Captures;
            Check(ReferenceEquals(rig.Start("ev.a", "owner.a", 0, "tx.1"), a) && rig.Recovery.Captures == captures, "finished run replay does not rerun");
            rig.AssertClean();
        }

        private static void LateCallbacksAreIgnored()
        {
            var rig = new Rig();
            var forks = new Dictionary<string, IReadOnlyList<CompiledCall>> { { "a", new[] { Say("ev.c", 1, "A") } } };
            rig.Catalog.Add(Ev("ev.c", ExecutionMode.Scene, new string[0], new[] { Choice("ev.c", 1, "a") }, forks));
            var h = rig.Start("ev.c");
            rig.Until(() => rig.Presentation.Choices.Count == 1, "choice");
            rig.Presentation.Choices[0].Handle.Succeed(new ChoiceSelection("a", 0), 0);
            h.Cancel("late");
            rig.Until(() => h.Terminal != null, "cancel");
            rig.Ticks(3);
            Check(h.Terminal.Status == OperationStatus.Cancelled, "cancel wins over a queued callback");
            Check(rig.Presentation.Said.Count == 0 && rig.Store.Commits.Count == 0, "late callback did nothing");
            rig.AssertClean();
        }

        // ------------------------------------------------------------------ builders

        private static readonly FunctionDef TriggerFn = new FunctionDef("manual", FunctionRole.Trigger, "Scene/Control", null);

        private static void Check(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("EventRunnerSpecs failed: " + what);
        }

        private static void Throws(Action action, string what)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("EventRunnerSpecs failed: " + what);
        }

        private static bool Contains(IReadOnlyList<string> list, string item)
        {
            foreach (var x in list)
                if (x == item) return true;
            return false;
        }

        private static void CheckOrder(List<string> log, params string[] items)
        {
            var at = -1;
            foreach (var item in items)
            {
                var next = -1;
                for (var i = at + 1; i < log.Count; i++)
                    if (log[i] == item) { next = i; break; }
                Check(next >= 0, "log order: " + item + " after index " + at + " in [" + string.Join(" ", log) + "]");
                at = next;
            }
        }

        private static SourceLocation Src(string ev, int i) { return new SourceLocation("runner", "/events/0", ev, ev + "#" + i, 0); }
        private static KeyValuePair<string, DataNode> A(string k, DataNode v) { return new KeyValuePair<string, DataNode>(k, v); }
        private static DataNode S(string v) { return DataNode.FromString(v); }
        private static DataNode N(double v) { return DataNode.FromNumber(v); }
        private static DataNode Obj(params KeyValuePair<string, DataNode>[] members) { return DataNode.FromObject(members); }

        private static CompiledCall Cmd(string ev, int i, string fn, params KeyValuePair<string, DataNode>[] args)
        {
            return new CompiledCall(new FunctionDef(fn, FunctionRole.Command, "Scene/Control", null), i, string.Empty, false, args, null, Src(ev, i));
        }

        private static CompiledCall Say(string ev, int i, string text)
        {
            return Cmd(ev, i, "say", A("speaker", S("npc.x")), A("text", S(text)));
        }

        private static CompiledCall Move(string ev, int i, string asyncName)
        {
            if (asyncName == null) return Cmd(ev, i, "move", A("actor", S("npc.x")), A("destination", S("dest.y")));
            return Cmd(ev, i, "move", A("actor", S("npc.x")), A("destination", S("dest.y")), A("async", S(asyncName)));
        }

        private static CompiledCall Animate(string ev, int i, string actor, string handle)
        {
            return Cmd(ev, i, "animate", A("actor", S(actor)), A("clip", S("wave")), A("seconds", N(1)), A("async", S(handle)));
        }

        private static CompiledCall Await(string ev, int i, params string[] names)
        {
            var nodes = new List<DataNode>();
            foreach (var n in names) nodes.Add(S(n));
            return Cmd(ev, i, "await", A("handles", DataNode.FromList(nodes)));
        }

        private static CompiledCall Choice(string ev, int i, params string[] forks)
        {
            var options = new List<DataNode>();
            foreach (var f in forks) options.Add(Obj(A("fork", S(f)), A("label", S("label." + f))));
            return Cmd(ev, i, "choice", A("prompt", S("pick")), A("options", DataNode.FromList(options)));
        }

        private static CompiledCall Branch(string ev, int i, IReadOnlyList<CompiledCall> cond, string then, string otherwise)
        {
            var args = new List<KeyValuePair<string, DataNode>> { A("condition", S("raw")), A("then", S(then)) };
            if (otherwise != null) args.Add(A("else", S(otherwise)));
            return new CompiledCall(new FunctionDef("branch", FunctionRole.Command, "Scene/Control", null), i, string.Empty, false, args, new IReadOnlyList<CompiledCall>[] { cond }, Src(ev, i));
        }

        private static CompiledCall Perform(string ev, int i, string domain, string definition)
        {
            return Cmd(ev, i, "perform", A("domain", S(domain)), A("definition", S(definition)));
        }

        private static CompiledEvent Ev(string id, ExecutionMode mode, string[] resources, CompiledCall[] script)
        {
            return Ev(id, mode, resources, script, null, RepeatPolicy.Practice);
        }

        private static CompiledEvent Ev(string id, ExecutionMode mode, string[] resources, CompiledCall[] script, Dictionary<string, IReadOnlyList<CompiledCall>> forks)
        {
            return Ev(id, mode, resources, script, forks, RepeatPolicy.Practice);
        }

        private static CompiledEvent Ev(string id, ExecutionMode mode, string[] resources, CompiledCall[] script, Dictionary<string, IReadOnlyList<CompiledCall>> forks, RepeatPolicy repeat)
        {
            var trigger = new CompiledCall(TriggerFn, 1, string.Empty, false, null, null, Src(id, 1));
            return new CompiledEvent(id, mode, repeat, resources, trigger, null, 0, script, forks, null, new CompletionKey("done." + id), DataNode.FromObject(new Dictionary<string, DataNode>()));
        }
    }

    // ---------------------------------------------------------------------- rig and fakes

    internal sealed class Rig
    {
        public readonly List<string> Log = new List<string>();
        public readonly CompletionPump Pump = new CompletionPump();
        public readonly HandleFactory Handles;
        public readonly LoggingLeases Leases;
        public readonly FakeStore Store;
        public readonly FakeConditions Conditions = new FakeConditions();
        public readonly FakePauses Pauses;
        public readonly FakeMoves Moves;
        public readonly FakePresentation Presentation;
        public readonly FakeLifetime Lifetime;
        public readonly TickClock Clock;
        public readonly HashSet<string> Completed = new HashSet<string>(StringComparer.Ordinal);
        public readonly FakeProjector Projector;
        public readonly FakeRecovery Recovery;
        public readonly FakeBoundaries Boundaries;
        public readonly FakeSaves Saves;
        public readonly FakeStaging Staging;
        public readonly FakeMaps Maps;
        public readonly FakeCatalog Catalog = new FakeCatalog();
        public readonly EventRunner Runner;
        private int txn;

        public Rig() : this(3600) { }

        public Rig(double timeoutSeconds)
        {
            Handles = new HandleFactory(Pump);
            Leases = new LoggingLeases(new LeaseService(), Log);
            Store = new FakeStore(Log, Pump);
            Pauses = new FakePauses(Log);
            Moves = new FakeMoves(Handles, Log);
            Presentation = new FakePresentation(Handles, Log);
            Lifetime = new FakeLifetime(Log);
            Clock = new TickClock(Handles);
            Projector = new FakeProjector(Log, Store);
            Recovery = new FakeRecovery(Log);
            Boundaries = new FakeBoundaries(Log);
            Saves = new FakeSaves(Handles, Log);
            Staging = new FakeStaging(Handles, Log);
            Maps = new FakeMaps(Handles, Log);
            Runner = new EventRunner(new EventRunnerDependencies
            {
                Catalog = new FakeCatalogProvider(Catalog),
                Conditions = Conditions,
                Leases = Leases,
                Pauses = Pauses,
                Moves = Moves,
                Destinations = new FakeDestinations(),
                Presentation = Presentation,
                PresentationLifetime = Lifetime,
                Clock = Clock,
                Store = Store.Inner,
                CommitEvent = Store.CommitEvent,
                ProjectWorld = Projector.Project,
                Recovery = Recovery,
                Boundaries = Boundaries,
                Saves = Saves,
                Staging = Staging,
                Maps = Maps,
                Handles = Handles,
                RepeatBlocked = (e, s) => Completed.Contains(e.OnceKey.Value),
                EventTimeoutSeconds = timeoutSeconds
            });
        }

        public IHandle<EventCompletion> Start(string ev) { return Start(ev, "owner.a", 0, null); }
        public IHandle<EventCompletion> Start(string ev, string owner) { return Start(ev, owner, 0, null); }

        public IHandle<EventCompletion> Start(string ev, string owner, long sequence, string tx)
        {
            return Runner.Start(new EventStartRequest(ev, owner, sequence, new TransactionId(tx ?? "tx." + (++txn))));
        }

        public void Ticks(int n)
        {
            for (var i = 0; i < n; i++)
            {
                Pump.AdvanceTick();
                Clock.Advance(1);
                Runner.Tick(1);
            }
        }

        public void Until(Func<bool> done, string what)
        {
            for (var i = 0; i < 60; i++)
            {
                if (done()) return;
                Ticks(1);
            }
            if (!done()) throw new InvalidOperationException("EventRunnerSpecs timed out waiting for " + what);
        }

        public void AssertClean()
        {
            Ticks(2);
            if (Runner.ActiveCount != 0) throw new InvalidOperationException("runner still has active runs");
            if (Leases.ActiveLeaseCount != 0 || Leases.HeldResourceCount != 0) throw new InvalidOperationException("leaked lease");
            if (Pauses.Live != 0) throw new InvalidOperationException("leaked pause token");
            if (Runner.PendingSaveCount != 0) throw new InvalidOperationException("autosave still pending");
            if (Handles.LiveCount != 0) throw new InvalidOperationException("leaked non-terminal handles: " + Handles.LiveCount);
        }
    }

    internal sealed class LoggingLeases : ILeaseService
    {
        private readonly LeaseService inner;
        private readonly List<string> log;
        public LoggingLeases(LeaseService inner, List<string> log) { this.inner = inner; this.log = log; }
        public int ActiveLeaseCount { get { return inner.ActiveLeaseCount; } }
        public int HeldResourceCount { get { return inner.HeldResourceCount; } }
        public Outcome<Lease> TryAcquire(LeaseRequest request) { return inner.TryAcquire(request); }
        public Result Release(string leaseId) { log.Add("lease.release"); return inner.Release(leaseId); }
    }

    internal sealed class FakeStore
    {
        private readonly List<string> log;
        public readonly List<TransactionId> Commits = new List<TransactionId>();
        public readonly List<long> EventBases = new List<long>();
        public readonly GameStore Inner;

        public FakeStore(List<string> log, ICompletionQueue queue)
        {
            this.log = log;
            var root = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            TypedFields.FillMissing(root);
            Inner = new GameStore(SnapshotTrees.Snapshot(1, DataNode.FromObject(root)), queue);
        }

        public StateSnapshot Current { get { return Inner.Current; } }

        /// <summary>Every commit here changes the state, so the revisions not explained by commits are replacements.</summary>
        public int ReplaceCount { get { return (int)(Current.Revision - 1) - Commits.Count; } }

        public Outcome<StateSnapshot> CommitEvent(CompiledEvent definition, TransactionId transaction)
        {
            EventBases.Add(Current.Revision);
            return Commit(transaction);
        }

        public Outcome<StateSnapshot> Commit(TransactionId transaction)
        {
            Commits.Add(transaction);
            log.Add("commit:" + transaction);
            return Inner.Commit(transaction, s => Outcome<GameState>.Ok(s.With(s.Clock), 0));
        }
    }

    internal sealed class FakeConditions : IConditionEvaluator
    {
        public bool Value = true;
        public Func<IReadOnlyList<CompiledCall>, bool> Rule;
        public long LastRevision = -1;

        public ConditionReport Evaluate(IReadOnlyList<CompiledCall> when, StateSnapshot snapshot)
        {
            LastRevision = snapshot.Revision;
            var ok = Rule != null ? Rule(when) : Value;
            return new ConditionReport(ok, snapshot.Revision, ok ? null : new[] { new Diagnostic(ResultCodes.ConditionFalse, "condition is false", null, "true", "false") });
        }
    }

    internal sealed class FakePauses : IPauseService
    {
        private readonly List<string> log;
        private readonly HashSet<string> live = new HashSet<string>();
        private int n;
        public int Acquired;
        public Action OnAcquire;
        public FakePauses(List<string> log) { this.log = log; }
        public int Live { get { return live.Count; } }
        public IPausePolicy Effective { get { return PauseSet.None; } }

        public PauseToken Acquire(string owner, IPausePolicy requested)
        {
            Acquired++;
            var token = new PauseToken("pause." + (++n), owner, requested);
            live.Add(token.TokenId);
            log.Add("pause.acquire");
            if (OnAcquire != null) OnAcquire();
            return token;
        }

        public void Release(PauseToken token)
        {
            if (live.Remove(token.TokenId)) log.Add("pause.release");
        }
    }

    internal sealed class PendingMove
    {
        public MoveRequest Request;
        public IHandleController<MoveArrival> Handle;
        public bool CancelRequested;
    }

    internal sealed class FakeMoves : IMoveService
    {
        private readonly IHandleFactory factory;
        private readonly List<string> log;
        public readonly List<PendingMove> Pending = new List<PendingMove>();
        public FakeMoves(IHandleFactory factory, List<string> log) { this.factory = factory; this.log = log; }

        public IHandle<MoveArrival> Move(MoveRequest request)
        {
            var h = factory.Create<MoveArrival>("move." + (Pending.Count + 1));
            h.Start();
            var pending = new PendingMove { Request = request, Handle = h };
            h.SetCancellationHandler(reason => { pending.CancelRequested = true; h.Cancelled(reason, 0); });
            Pending.Add(pending);
            log.Add("move.request");
            return h;
        }

        public void Arrive(int index, bool arrived)
        {
            var p = Pending[index];
            var at = arrived ? new Position("map.a", 1.2, 1) : new Position("map.a", 9, 9);
            p.Handle.Succeed(new MoveArrival(at, p.Request.Destination), 0);
        }
    }

    internal sealed class FakeDestinations : IDestinationResolver
    {
        public Outcome<Destination> Resolve(string actorId, string destinationId)
        {
            return Outcome<Destination>.Ok(new Destination(destinationId, new Position("map.a", 1, 1), 0.5), 0);
        }
    }

    internal sealed class PendingOp
    {
        public string Kind;
        public string Text;
        public IHandleController<object> Handle;
        public bool CancelRequested;
    }

    internal sealed class PendingChoice
    {
        public IReadOnlyList<ChoiceOption> Options;
        public IHandleController<ChoiceSelection> Handle;
    }

    internal sealed class FakePresentation : IPresentationPort
    {
        private readonly IHandleFactory factory;
        private readonly List<string> log;
        public readonly List<string> Said = new List<string>();
        public readonly List<PendingOp> Ops = new List<PendingOp>();
        public readonly List<PendingChoice> Choices = new List<PendingChoice>();
        public bool DeferCancel;
        public FakePresentation(IHandleFactory factory, List<string> log) { this.factory = factory; this.log = log; }

        public List<PendingOp> OfKind(string kind)
        {
            var result = new List<PendingOp>();
            foreach (var op in Ops)
                if (op.Kind == kind) result.Add(op);
            return result;
        }

        private PendingOp Open(string kind, string text)
        {
            var op = new PendingOp { Kind = kind, Text = text, Handle = factory.Create<object>("pres." + kind + "." + (Ops.Count + 1)) };
            op.Handle.Start();
            op.Handle.SetCancellationHandler(reason => { op.CancelRequested = true; if (!DeferCancel) op.Handle.Cancelled(reason, 0); });
            Ops.Add(op);
            return op;
        }

        public IHandle Say(string speaker, string text, string ownerToken)
        {
            Said.Add(text);
            log.Add("say:" + text);
            var op = Open("say", text);
            op.Handle.Succeed(null, 0);
            return op.Handle;
        }

        public IHandle<ChoiceSelection> Choose(string prompt, IReadOnlyList<ChoiceOption> options, string ownerToken)
        {
            var h = factory.Create<ChoiceSelection>("pres.choice." + (Choices.Count + 1));
            h.Start();
            h.SetCancellationHandler(reason => h.Cancelled(reason, 0));
            Choices.Add(new PendingChoice { Options = options, Handle = h });
            log.Add("choose:" + prompt);
            return h;
        }

        public IHandle MoveCamera(Destination target, double seconds, string ownerToken) { return Open("camera", target.DestinationId).Handle; }
        public IHandle Animate(string actorId, string clip, double seconds, string ownerToken) { return Open("animate", actorId).Handle; }
    }

    internal sealed class FakeLifetime : IPresentationLifetime
    {
        private readonly List<string> log;
        public int Closed;
        public FakeLifetime(List<string> log) { this.log = log; }
        public void CloseOwner(string ownerToken) { Closed++; log.Add("lifetime.close"); }
    }

    internal sealed class FakeProjector
    {
        private readonly List<string> log;
        private readonly FakeStore store;
        public bool Moved;
        public FakeProjector(List<string> log, FakeStore store) { this.log = log; this.store = store; }

        public Outcome<StateSnapshot> Project(TransactionId txn)
        {
            log.Add("project");
            if (!Moved) return Outcome<StateSnapshot>.Ok(store.Current, store.Current.Revision);
            return store.Commit(txn);
        }
    }

    internal sealed class FakeRecovery : ISceneRecovery
    {
        private readonly List<string> log;
        public StateSnapshot Captured;
        public StateSnapshot RestoreOverride;
        public int Captures, Restores, Clears;
        public FakeRecovery(List<string> log) { this.log = log; }

        public Result CaptureEntry(StateSnapshot acceptedSnapshot, string ownerToken)
        {
            Captures++;
            Captured = acceptedSnapshot;
            log.Add("capture");
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, acceptedSnapshot.Revision, null);
        }

        public Outcome<StateSnapshot> RestoreEntry(string ownerToken)
        {
            Restores++;
            log.Add("restore");
            var snapshot = RestoreOverride ?? Captured;
            return Outcome<StateSnapshot>.Ok(snapshot, snapshot.Revision);
        }

        public Result ClearEntry(string ownerToken)
        {
            Clears++;
            log.Add("clear");
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }
    }

    internal sealed class FakeBoundaries : ISafeBoundaryRenewal
    {
        private readonly List<string> log;
        public FakeBoundaries(List<string> log) { this.log = log; }
        public SafeSnapshot Latest { get { return null; } }
        public void Notify(SafeBoundary boundary, StateSnapshot snapshot) { log.Add("notify:" + boundary); }
        public void MarkSaved(long revision) { }
        public void Renew(SafeBoundary boundary, StateSnapshot snapshot, bool saved) { log.Add("renew:" + boundary); }
    }

    internal sealed class FakeSaves : ISaveService
    {
        private readonly IHandleFactory factory;
        private readonly List<string> log;
        public readonly List<IHandleController<FileGeneration>> Requests = new List<IHandleController<FileGeneration>>();
        /// <summary>Per request number (1-based): true succeeds, false fails; null Auto leaves every save pending.</summary>
        public Func<int, bool> Auto = n => true;
        public FakeSaves(IHandleFactory factory, List<string> log) { this.factory = factory; this.log = log; }

        public static FileGeneration Gen() { return new FileGeneration(1, "sum", new ReadOnlyMemory<byte>(new byte[0])); }

        public IHandle<FileGeneration> SaveLatestSafe()
        {
            var h = factory.Create<FileGeneration>("save." + (Requests.Count + 1));
            h.Start();
            h.SetCancellationHandler(reason => h.Cancelled(reason, 0));
            Requests.Add(h);
            log.Add("save.request");
            if (Auto != null)
            {
                if (Auto(Requests.Count)) h.Succeed(Gen(), 0);
                else h.Fail("DISK_ERROR", 0, new Diagnostic[0]);
            }
            return h;
        }

        public IHandle<FileGeneration> LoadLatest() { throw new NotSupportedException(); }
    }

    internal sealed class FakeStaging : IEventStagingPort
    {
        private readonly IHandleFactory factory;
        private readonly List<string> log;
        public int Count;
        public FakeStaging(IHandleFactory factory, List<string> log) { this.factory = factory; this.log = log; }

        public IHandle Stage(CompiledEvent definition, Lease lease)
        {
            Count++;
            log.Add("stage");
            var h = factory.Create<object>("stage." + Count);
            h.Start();
            h.SetCancellationHandler(reason => h.Cancelled(reason, 0));
            h.Succeed(null, 0);
            return h;
        }
    }

    internal sealed class FakeMaps : IMapPort
    {
        private readonly IHandleFactory factory;
        private readonly List<string> log;
        public readonly List<IHandleController<object>> Loads = new List<IHandleController<object>>();
        /// <summary>true loads and switches map, false fails, null leaves the load pending.</summary>
        public bool? Auto = true;
        public FakeMaps(IHandleFactory factory, List<string> log) { this.factory = factory; this.log = log; ActiveMapId = "map.a"; }
        public string ActiveMapId { get; set; }
        public long NavRevision { get { return 1; } }
        public bool TryGetPortal(string portalId, out Portal portal) { portal = null; return false; }
        public IReadOnlyList<Portal> PortalsOn(string mapId) { return new Portal[0]; }

        public IHandle Load(string mapId, string ownerToken)
        {
            log.Add("load:" + mapId);
            var h = factory.Create<object>("load." + (Loads.Count + 1));
            h.Start();
            Loads.Add(h);
            h.SetCancellationHandler(reason => h.Cancelled(reason, 0));
            if (Auto == true) { ActiveMapId = mapId; h.Succeed(null,0); }
            else if (Auto == false) h.Fail(ResultCodes.MapLoadFailed,0,new Diagnostic[0]);
            return h;
        }
    }
    // Codex completes the quota-truncated test doubles below; original fragment retained in evidence.
    internal sealed class FakeCatalog : ICatalog
    {
        readonly List<CompiledEvent> events = new List<CompiledEvent>();
        public string ContentVersion => "test"; public string CatalogHash => "hash";
        public IReadOnlyList<CompiledEvent> Events => events.AsReadOnly();
        public void Add(CompiledEvent e) => events.Add(e);
        public CompiledEvent FindEvent(string id) => events.Find(e=>e.Id==id);
        public DataNode FindDefinition(string kind,string id) => null;
    }
    internal sealed class FakeCatalogProvider : ICatalogProvider
    {
        public ICatalog Current {get;}
        public FakeCatalogProvider(ICatalog catalog) {Current=catalog;}
        public Outcome<ICatalog> TryReplace(ContentBundle a,ContentBundle b,ContentBundle c,ContentBundle d,ContentBundle e) => throw new NotSupportedException();
    }
}
