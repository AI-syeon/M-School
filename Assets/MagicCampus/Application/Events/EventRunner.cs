using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Events
{
    /// <summary>
    /// Runs compiled events. Scene events lease declared resources plus snapshot.world, pause, project the actual world, capture the entry,
    /// notify SceneEntry and wait for a real SaveLatestSafe success before staging and script. Room activities add neither the world resource
    /// nor entry capture/restore. All async work is awaited through handles (callbacks arrive next tick); at most 64 immediate instructions run
    /// between two runner ticks. Success is one IEventCommitPlanner plan and one IStateStore.Commit after a latest-revision condition recheck.
    /// Failure, cancel and timeout cancel children, wait for every child to be terminal, close owned display, restore the entry map and state
    /// (scene only) and release pause then lease last. A commit that already happened is never undone.
    /// </summary>
    public sealed class EventRunner
    {
        public const int MaxImmediatePerTick = 64;
        public const string WorldResource = "snapshot.world";
        public const string CleanupFailed = "CLEANUP_FAILED";
        public const string PortFailure = "PORT_FAILURE";
        private const string ScriptName = "script";

        private enum Phase { Entry, Script, Committing, Closing, Done }

        private sealed class Failure
        {
            public string Code;
            public string Reason;
            public bool IsCancel;
            public IReadOnlyList<Diagnostic> Details;
        }

        private sealed class AsyncOp
        {
            public string Name;
            public IHandle Handle;
            public IHandle<MoveArrival> Move;
            public string[] Keys;
        }

        private sealed class Frame
        {
            public readonly string Name;
            public readonly IReadOnlyList<CompiledCall> List;
            public int Pc;
            public readonly Dictionary<string, AsyncOp> Open = new Dictionary<string, AsyncOp>(StringComparer.Ordinal);
            public Frame(string name, IReadOnlyList<CompiledCall> list) { Name = name; List = list; }
        }

        private sealed class Join
        {
            public int Pending;
            public Failure First;
        }

        private sealed class Run
        {
            public CompiledEvent Def;
            public string Owner;
            public TransactionId Txn;
            public IHandleController<EventCompletion> Handle;
            public Lease Lease;
            public PauseToken Pause;
            public bool Scene;
            public string EntryMapId;
            public bool EntryCaptured;
            public Phase Phase;
            public readonly List<Frame> Stack = new List<Frame>();
            public readonly HashSet<IHandle> Live = new HashSet<IHandle>();
            public readonly List<Diagnostic> Notes = new List<Diagnostic>();
            public int Gen;
            public bool Waiting, Driving, YieldPending, Closing, CleanupStarted, Committed;
            public int Budget;
            public long Executed;
            public double Elapsed;
            public double RecoveryElapsed;
            public IHandle RecoveryLoad;
            public bool RecoveryCancelRequested;
            public Failure Failure;
        }

        private readonly EventRunnerDependencies d;
        private readonly List<Run> active = new List<Run>();
        private readonly Dictionary<string, IHandleController<EventCompletion>> byTransaction = new Dictionary<string, IHandleController<EventCompletion>>(StringComparer.Ordinal);
        private readonly Dictionary<string, IHandleController<EventCompletion>> byInput = new Dictionary<string, IHandleController<EventCompletion>>(StringComparer.Ordinal);
        private readonly HashSet<IHandle> pendingSaves = new HashSet<IHandle>();
        private readonly List<Diagnostic> cleanupDiagnostics = new List<Diagnostic>();
        private long runSeq, traceSeq, refusals;
        private bool starting;

        public EventRunner(EventRunnerDependencies dependencies)
        {
            if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));
            d = dependencies.Validated();
        }

        // Observation surface.
        public int ActiveCount => active.Count;
        public string ActiveOwner => active.Count > 0 ? active[0].Owner : null;
        public IReadOnlyList<string> ActiveResources => active.Count > 0 ? active[0].Lease.Resources : (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<string> ActiveOwners
        {
            get
            {
                var owners = new List<string>();
                foreach (var r in active) owners.Add(r.Owner);
                return owners;
            }
        }
        /// <summary>Result of the newest post-commit autosave; null until one finished.</summary>
        public Result LastSaveResult { get; private set; }
        /// <summary>True from a successful commit until its autosave succeeded.</summary>
        public bool Unsaved { get; private set; }
        public int PendingSaveCount => pendingSaves.Count;
        public IReadOnlyList<Diagnostic> CleanupDiagnostics => cleanupDiagnostics.ToArray();

        // ------------------------------------------------------------------ start

        public IHandle<EventCompletion> Start(EventStartRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var inputKey = request.OwnerToken + "|" + request.InputSequence.ToString(CultureInfo.InvariantCulture) + "|" + request.EventId;
            IHandleController<EventCompletion> prior;
            if (byTransaction.TryGetValue(request.TransactionId.Value, out prior) || byInput.TryGetValue(inputKey, out prior))
            {
                Emit(request.EventId, EventPhases.Replay, request.TransactionId.Value);
                return prior;
            }
            if (starting) return Refuse(request, ResultCodes.ResourceBusy, "start re-entered while another start is in progress", "idle", "starting");
            starting = true;
            try { return StartCore(request, inputKey); }
            finally { starting = false; }
        }

        private IHandle<EventCompletion> StartCore(EventStartRequest request, string inputKey)
        {
            var catalog = d.Catalog.Current;
            var def = catalog == null ? null : catalog.FindEvent(request.EventId);
            if (def == null) return Refuse(request, ResultCodes.RefNotFound, "unknown event", "registered event id", request.EventId);
            foreach (var other in active)
                if (other.Owner == request.OwnerToken || other.Def.Id == def.Id)
                    return Refuse(request, ResultCodes.ResourceBusy, "owner or event is already running", "idle", other.Def.Id);

            var snapshot = d.Store.Current;
            var report = d.Conditions.Evaluate(def.When, snapshot);
            if (!report.Satisfied)
                return Refuse(request, HasCode(report.Reasons, ResultCodes.Unregistered) ? ResultCodes.Unregistered : ResultCodes.ConditionFalse, report.Reasons);

            string domain;
            if (FindPerformDomain(def, out domain))
                return Refuse(request, ResultCodes.Unregistered, "no operation registered for domain", "registered domain", domain);

            IPausePolicy pause;
            string pauseActual;
            if (!TryPause(def, out pause, out pauseActual))
                return Refuse(request, ResultCodes.ContentInvalid, "unknown pause policy", "standard|dialogue|none", pauseActual);

            if (def.Repeat == RepeatPolicy.Once && d.RepeatBlocked(def, snapshot))
                return Refuse(request, ResultCodes.ConditionFalse, "once event already completed", "not completed", def.Id);

            var scene = def.Mode == ExecutionMode.Scene;
            var resources = new List<string>(def.Resources);
            if (scene) resources.Add(WorldResource);
            var leased = d.Leases.TryAcquire(new LeaseRequest(request.OwnerToken, resources));
            if (!leased.Succeeded) return Refuse(request, leased.Result.Code, leased.Result.Details);

            PauseToken token = null;
            if (!ReferenceEquals(pause, PauseSet.None))
            {
                try { token = d.Pauses.Acquire(request.OwnerToken, pause); }
                catch (Exception e)
                {
                    d.Leases.Release(leased.Value.LeaseId);
                    return Refuse(request, PortFailure, "pause acquire failed: " + e.Message, "pause token", "exception");
                }
            }

            var handle = d.Handles.Create<EventCompletion>("event." + def.Id + "." + (++runSeq).ToString(CultureInfo.InvariantCulture));
            handle.Start();
            var run = new Run
            {
                Def = def,
                Owner = request.OwnerToken,
                Txn = request.TransactionId,
                Handle = handle,
                Lease = leased.Value,
                Pause = token,
                Scene = scene,
                Phase = Phase.Entry,
                EntryMapId = d.Maps.ActiveMapId
            };
            handle.SetCancellationHandler(reason => OnCancel(run, reason));
            byTransaction[request.TransactionId.Value] = handle;
            byInput[inputKey] = handle;
            active.Add(run);
            Trace(run, EventPhases.Start, string.Empty);
            if (scene) BeginEntry(run); else BeginScript(run);
            return handle;
        }

        private IHandle<EventCompletion> Refuse(EventStartRequest request, string code, string message, string expected, string actual)
        {
            return Refuse(request, code, new[] { new Diagnostic(code, message, null, expected, actual) });
        }

        private IHandle<EventCompletion> Refuse(EventStartRequest request, string code, IReadOnlyList<Diagnostic> details)
        {
            var handle = d.Handles.Create<EventCompletion>("event.refused." + (++refusals).ToString(CultureInfo.InvariantCulture));
            handle.Start();
            if (details == null || details.Count == 0)
                details = new[] { new Diagnostic(code, "event refused", null, "startable", request.EventId) };
            handle.Fail(code, d.Store.Current.Revision, details);
            Emit(request.EventId, EventPhases.Refused, code);
            return handle;
        }

        private static bool HasCode(IReadOnlyList<Diagnostic> list, string code)
        {
            if (list == null) return false;
            foreach (var x in list)
                if (x.Code == code) return true;
            return false;
        }

        private static IEnumerable<IReadOnlyList<CompiledCall>> Lists(CompiledEvent def)
        {
            yield return def.Script;
            foreach (var kv in def.Forks) yield return kv.Value;
        }

        private static bool IsPerform(CompiledCall c)
        {
            return c.Function.Role == FunctionRole.Command && c.Function.Name == "perform";
        }

        private static bool FindPerformDomain(CompiledEvent def, out string domain)
        {
            domain = null;
            foreach (var list in Lists(def))
                foreach (var c in list)
                {
                    DataNode n;
                    if (!IsPerform(c) || !c.Args.TryGetValue("domain", out n) || n.Kind != DataKind.String) continue;
                    domain = n.Text;
                    return true;
                }
            return false;
        }

        private static bool TryPause(CompiledEvent def, out IPausePolicy policy, out string actual)
        {
            var kind = def.Mode == ExecutionMode.Scene ? "standard" : "none";
            policy = PauseSet.None;
            actual = kind;
            DataNode n;
            if (def.Raw.TryGet("pause", out n) && n.Kind != DataKind.Null)
            {
                if (n.Kind != DataKind.String) { actual = n.Kind.ToString(); return false; }
                kind = n.Text;
                actual = kind;
            }
            switch (kind)
            {
                case "scene":
                case "standard": policy = new PauseSet(true, true, true, true, false); return true;
                case "dialogue": policy = new PauseSet(true, false, true, false, false); return true;
                case "none": policy = PauseSet.None; return true;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ tick

        public void Tick(double seconds)
        {
            PortGuard.NonNegative(seconds, nameof(seconds));
            var runs = active.ToArray();
            foreach (var run in runs) run.Budget = 0;
            foreach (var run in runs)
            {
                if (run.Phase == Phase.Done) continue;
                if (run.Closing && run.RecoveryLoad != null && !run.RecoveryCancelRequested)
                {
                    run.RecoveryElapsed += seconds;
                    if (run.RecoveryElapsed >= d.RecoveryTimeoutSeconds)
                    {
                        run.RecoveryCancelRequested = true;
                        Note(run, new Diagnostic(ResultCodes.Timeout, "entry map recovery timed out", null, "terminal map load", "pending"));
                        run.RecoveryLoad.Cancel("entry recovery timeout");
                    }
                }
                if (!run.Closing && !run.Committed)
                {
                    run.Elapsed += seconds;
                    if (run.Elapsed >= d.EventTimeoutSeconds)
                    {
                        Abort(run, Fail(ResultCodes.Timeout, "event watchdog expired", null, d.EventTimeoutSeconds.ToString(CultureInfo.InvariantCulture), run.Elapsed.ToString(CultureInfo.InvariantCulture)));
                        continue;
                    }
                }
                if (run.YieldPending && !run.Waiting && !run.Closing) Drive(run);
            }
        }

        // ------------------------------------------------------------------ scene entry

        private void BeginEntry(Run run)
        {
            Trace(run, EventPhases.EntryProject, string.Empty);
            var projected = d.ProjectWorld(new TransactionId(run.Txn.Value + ".project"));
            if (!projected.Succeeded) { Abort(run, FailureFrom(projected.Result)); return; }

            Trace(run, EventPhases.EntryCapture, string.Empty);
            var accepted = d.Store.Current;
            var captured = d.Recovery.CaptureEntry(accepted, run.Owner);
            if (captured.Status != OperationStatus.Succeeded) { Abort(run, FailureFrom(captured)); return; }
            run.EntryCaptured = true;

            Trace(run, EventPhases.EntryNotify, string.Empty);
            d.Boundaries.Notify(SafeBoundary.SceneEntry, accepted);

            Trace(run, EventPhases.EntrySave, string.Empty);
            var save = d.Saves.SaveLatestSafe();
            if (save == null) { PortNull(run, null, "SaveLatestSafe"); return; }
            WaitFor(run, save, r =>
            {
                if (r.Status != OperationStatus.Succeeded) Abort(run, FailureFrom(r));
                else BeginStaging(run);
            });
        }

        private void BeginStaging(Run run)
        {
            Trace(run, EventPhases.Stage, string.Empty);
            var staged = d.Staging.Stage(run.Def, run.Lease);
            if (staged == null) { PortNull(run, null, "Stage"); return; }
            WaitFor(run, staged, r =>
            {
                if (r.Status != OperationStatus.Succeeded) Abort(run, FailureFrom(r));
                else BeginScript(run);
            });
        }

        private void BeginScript(Run run)
        {
            run.Phase = Phase.Script;
            run.Stack.Add(new Frame(ScriptName, run.Def.Script));
            Drive(run);
        }

        // ------------------------------------------------------------------ interpreter

        private void Drive(Run run)
        {
            if (run.Closing || run.Driving || run.Phase == Phase.Done) return;
            run.Driving = true;
            try { Step(run); }
            finally { run.Driving = false; }
        }

        private void Step(Run run)
        {
            run.YieldPending = false;
            while (!run.Closing && !run.Waiting)
            {
                if (run.Stack.Count == 0) { Finish(run); return; }
                var frame = run.Stack[run.Stack.Count - 1];
                if (frame.Pc >= frame.List.Count)
                {
                    if (frame.Open.Count > 0) { AbortOpen(run, frame, null); return; }
                    run.Stack.RemoveAt(run.Stack.Count - 1);
                    if (run.Stack.Count > 0) Trace(run, EventPhases.ForkExit, run.Def.Id + ":" + frame.Name);
                    continue;
                }
                if (run.Budget >= MaxImmediatePerTick) { run.YieldPending = true; return; }
                if (++run.Executed > d.MaxInstructionsPerRun)
                {
                    Abort(run, Fail(ResultCodes.ContentInvalid, "instruction limit exceeded", null, d.MaxInstructionsPerRun.ToString(CultureInfo.InvariantCulture), run.Executed.ToString(CultureInfo.InvariantCulture)));
                    return;
                }
                run.Budget++;
                var call = frame.List[frame.Pc];
                frame.Pc++;
                Execute(run, frame, call);
            }
        }

        private void Execute(Run run, Frame frame, CompiledCall call)
        {
            Trace(run, EventPhases.Call, CallId(run, frame, call));
            if (call.Function.Role != FunctionRole.Command)
            {
                Abort(run, Fail(ResultCodes.ContentInvalid, "only command calls can run in a script", call.Source, "Command", call.Function.Role.ToString()));
                return;
            }
            switch (call.Function.Name)
            {
                case "move": ExecMove(run, frame, call); break;
                case "say": ExecSay(run, frame, call); break;
                case "choice": ExecChoice(run, frame, call); break;
                case "branch": ExecBranch(run, frame, call); break;
                case "wait": ExecWait(run, frame, call); break;
                case "camera": ExecCamera(run, frame, call); break;
                case "animate": ExecAnimate(run, frame, call); break;
                case "await": ExecAwait(run, frame, call); break;
                default:
                    Abort(run, Fail(ResultCodes.UnknownFunction, "command has no runtime implementation", call.Source, "registered command", call.Function.Name));
                    break;
            }
        }

        private static string CallId(Run run, Frame frame, CompiledCall call)
        {
            return run.Def.Id + (frame.Name == ScriptName ? string.Empty : ":" + frame.Name) + "#" + call.Index.ToString(CultureInfo.InvariantCulture);
        }

        private void ExecMove(Run run, Frame frame, CompiledCall call)
        {
            string actor, destination, asyncName;
            double timeout;
            bool portals;
            if (!ReqText(run, call, "actor", out actor) || !ReqText(run, call, "destination", out destination)) return;
            if (!OptNumber(run, call, "timeout", 30, out timeout) || !OptBool(run, call, "portals", true, out portals) || !OptText(run, call, "async", out asyncName)) return;
            var dest = d.Destinations.Resolve(actor, destination);
            if (!dest.Succeeded) { Abort(run, FailureFrom(dest.Result)); return; }
            MoveRequest request;
            try { request = new MoveRequest(actor, dest.Value, run.Owner, timeout, portals); }
            catch (ArgumentException e) { Abort(run, Fail(ResultCodes.ContentInvalid, e.Message, call.Source, "valid move request", "rejected")); return; }
            var h = d.Moves.Move(request);
            if (h == null) { PortNull(run, call, "Move"); return; }
            Launch(run, frame, call, h, h, asyncName, new[] { "actor:" + actor }, null);
        }

        private void ExecSay(Run run, Frame frame, CompiledCall call)
        {
            string speaker, text;
            if (!ReqText(run, call, "speaker", out speaker) || !ReqText(run, call, "text", out text)) return;
            if (!RequireClosed(run, frame, call)) return;
            var h = d.Presentation.Say(speaker, text, run.Owner);
            if (h == null) { PortNull(run, call, "Say"); return; }
            Launch(run, frame, call, h, null, null, null, null);
        }

        private void ExecChoice(Run run, Frame frame, CompiledCall call)
        {
            string prompt;
            if (!ReqText(run, call, "prompt", out prompt)) return;
            DataNode list;
            if (!call.Args.TryGetValue("options", out list) || list.Kind != DataKind.List || list.Items.Count == 0)
            {
                BadArg(run, call, "options", "non-empty List");
                return;
            }
            var options = new List<ChoiceOption>();
            foreach (var item in list.Items)
            {
                DataNode fork, label;
                if (item.Kind != DataKind.Object || !item.TryGet("fork", out fork) || fork.Kind != DataKind.String || !item.TryGet("label", out label) || label.Kind != DataKind.String)
                {
                    BadArg(run, call, "options", "{fork,label} objects");
                    return;
                }
                try { options.Add(new ChoiceOption(fork.Text, label.Text)); }
                catch (ArgumentException) { BadArg(run, call, "options", "valid fork key"); return; }
            }
            if (!RequireClosed(run, frame, call)) return;
            var choose = d.Presentation.Choose(prompt, options, run.Owner);
            if (choose == null) { PortNull(run, call, "Choose"); return; }
            Launch(run, frame, call, choose, null, null, null, r =>
            {
                var selection = choose.Value;
                if (selection == null || !HasOption(options, selection.Key))
                {
                    Abort(run, Fail(ResultCodes.ForkNotFound, "selection is not an offered option", call.Source, "offered fork", selection == null ? "none" : selection.Key));
                    return;
                }
                PushFork(run, call, selection.Key);
            });
        }

        private static bool HasOption(List<ChoiceOption> options, string key)
        {
            foreach (var o in options)
                if (o.Key == key) return true;
            return false;
        }

        private void ExecBranch(Run run, Frame frame, CompiledCall call)
        {
            string thenFork, elseFork;
            if (!ReqText(run, call, "then", out thenFork) || !OptText(run, call, "else", out elseFork)) return;
            if (call.Alternatives.Count < 1) { BadArg(run, call, "condition", "compiled condition list"); return; }
            if (!RequireClosed(run, frame, call)) return;
            var report = d.Conditions.Evaluate(call.Alternatives[0], d.Store.Current);
            if (!report.Satisfied && HasCode(report.Reasons, ResultCodes.Unregistered))
            {
                Abort(run, new Failure { Code = ResultCodes.Unregistered, Reason = "branch condition uses an unregistered function", Details = report.Reasons });
                return;
            }
            var fork = report.Satisfied ? thenFork : elseFork;
            if (fork == null) return;
            PushFork(run, call, fork);
        }

        private void ExecWait(Run run, Frame frame, CompiledCall call)
        {
            double seconds;
            if (!ReqNumber(run, call, "seconds", out seconds)) return;
            Launch(run, frame, call, d.Clock.Delay(seconds), null, null, null, null);
        }

        private void ExecCamera(Run run, Frame frame, CompiledCall call)
        {
            string destination, asyncName;
            double seconds;
            if (!ReqText(run, call, "destination", out destination) || !ReqNumber(run, call, "seconds", out seconds) || !OptText(run, call, "async", out asyncName)) return;
            var dest = d.Destinations.Resolve(run.Owner, destination);
            if (!dest.Succeeded) { Abort(run, FailureFrom(dest.Result)); return; }
            var h = d.Presentation.MoveCamera(dest.Value, seconds, run.Owner);
            if (h == null) { PortNull(run, call, "MoveCamera"); return; }
            Launch(run, frame, call, h, null, asyncName, new[] { "camera" }, null);
        }

        private void ExecAnimate(Run run, Frame frame, CompiledCall call)
        {
            string actor, clip, asyncName;
            double seconds;
            if (!ReqText(run, call, "actor", out actor) || !ReqText(run, call, "clip", out clip) || !ReqNumber(run, call, "seconds", out seconds) || !OptText(run, call, "async", out asyncName)) return;
            var h = d.Presentation.Animate(actor, clip, seconds, run.Owner);
            if (h == null) { PortNull(run, call, "Animate"); return; }
            Launch(run, frame, call, h, null, asyncName, new[] { "actor:" + actor }, null);
        }

        private void ExecAwait(Run run, Frame frame, CompiledCall call)
        {
            DataNode list;
            if (!call.Args.TryGetValue("handles", out list) || list.Kind != DataKind.List || list.Items.Count == 0)
            {
                BadArg(run, call, "handles", "non-empty List of names");
                return;
            }
            var ops = new List<AsyncOp>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list.Items)
            {
                if (item.Kind != DataKind.String) { BadArg(run, call, "handles", "String names"); return; }
                if (!seen.Add(item.Text))
                {
                    Abort(run, Fail(ResultCodes.HandleDuplicate, "handle named twice in one await", call.Source, "distinct names", item.Text));
                    return;
                }
                AsyncOp op;
                if (!frame.Open.TryGetValue(item.Text, out op))
                {
                    Abort(run, Fail(ResultCodes.HandleUndefined, "await names a handle that is not open in this list", call.Source, "open async handle", item.Text));
                    return;
                }
                ops.Add(op);
            }
            foreach (var op in ops) frame.Open.Remove(op.Name);
            var join = new Join { Pending = ops.Count };
            run.Waiting = true;
            foreach (var op in ops)
            {
                var current = op;
                WaitFor(run, current.Handle, r =>
                {
                    join.Pending--;
                    var failure = Verify(current.Move, r);
                    if (failure != null && join.First == null)
                    {
                        join.First = failure;
                        foreach (var sibling in ops)
                            if (!ReferenceEquals(sibling, current)) sibling.Handle.Cancel("sibling await failed");
                    }
                    if (join.Pending > 0) return;
                    run.Waiting = false;
                    if (join.First != null) Abort(run, join.First);
                    else Drive(run);
                });
            }
        }

        private void PushFork(Run run, CompiledCall call, string name)
        {
            IReadOnlyList<CompiledCall> list;
            if (!run.Def.Forks.TryGetValue(name, out list))
            {
                Abort(run, Fail(ResultCodes.ForkNotFound, "fork is not defined on the event", call.Source, "defined fork", name));
                return;
            }
            if (run.Stack.Count >= d.MaxScriptDepth)
            {
                Abort(run, Fail(ResultCodes.ContentInvalid, "fork depth limit exceeded", call.Source, d.MaxScriptDepth.ToString(CultureInfo.InvariantCulture), name));
                return;
            }
            run.Stack.Add(new Frame(name, list));
            Trace(run, EventPhases.ForkEnter, run.Def.Id + ":" + name);
        }

        /// <summary>Async: tracked and left open in the frame scope. Otherwise the run blocks until the handle is terminal and verified.</summary>
        private void Launch(Run run, Frame frame, CompiledCall call, IHandle h, IHandle<MoveArrival> move, string asyncName, string[] keys, Action<Result> onSuccess)
        {
            if (asyncName != null)
            {
                WaitFor(run, h, null);
                if (frame.Open.ContainsKey(asyncName))
                {
                    Abort(run, Fail(ResultCodes.HandleDuplicate, "handle name already open in this list", call.Source, "unused name", asyncName));
                    return;
                }
                foreach (var open in frame.Open.Values)
                    foreach (var a in open.Keys)
                        foreach (var b in keys)
                            if (a == b)
                            {
                                Abort(run, Fail(ResultCodes.HandleConflict, "two open async operations use the same actor or camera", call.Source, "exclusive resource", b));
                                return;
                            }
                frame.Open.Add(asyncName, new AsyncOp { Name = asyncName, Handle = h, Move = move, Keys = keys });
                return;
            }
            run.Waiting = true;
            WaitFor(run, h, r =>
            {
                run.Waiting = false;
                var failure = Verify(move, r);
                if (failure != null) { Abort(run, failure); return; }
                if (onSuccess != null) onSuccess(r);
                Drive(run);
            });
        }

        private static Failure Verify(IHandle<MoveArrival> move, Result r)
        {
            if (r.Status != OperationStatus.Succeeded) return FailureFrom(r);
            if (move != null)
            {
                var arrival = move.Value;
                if (arrival == null || !arrival.Arrived)
                    return Fail(ResultCodes.NoRoute, "move ended without reaching the destination", null, "arrived", arrival == null ? "none" : arrival.Distance.ToString(CultureInfo.InvariantCulture));
            }
            return null;
        }

        private bool RequireClosed(Run run, Frame frame, CompiledCall call)
        {
            if (frame.Open.Count == 0) return true;
            AbortOpen(run, frame, call);
            return false;
        }

        private void AbortOpen(Run run, Frame frame, CompiledCall call)
        {
            var names = new List<string>(frame.Open.Keys);
            names.Sort(StringComparer.Ordinal);
            Abort(run, Fail(ResultCodes.HandleUnawaited, "async handles are still open", call == null ? null : call.Source, "all awaited", string.Join(",", names)));
        }

        // ------------------------------------------------------------------ argument helpers

        private bool ReqText(Run run, CompiledCall call, string name, out string value)
        {
            DataNode n;
            if (call.Args.TryGetValue(name, out n) && n.Kind == DataKind.String) { value = n.Text; return true; }
            value = null;
            BadArg(run, call, name, "String");
            return false;
        }

        private bool OptText(Run run, CompiledCall call, string name, out string value)
        {
            value = null;
            DataNode n;
            if (!call.Args.TryGetValue(name, out n)) return true;
            if (n.Kind == DataKind.String && n.Text.Length > 0) { value = n.Text; return true; }
            BadArg(run, call, name, "non-empty String");
            return false;
        }

        private bool ReqNumber(Run run, CompiledCall call, string name, out double value)
        {
            DataNode n;
            if (call.Args.TryGetValue(name, out n) && NumberOf(n, out value) && value >= 0) return true;
            value = 0;
            BadArg(run, call, name, "number >= 0");
            return false;
        }

        private bool OptNumber(Run run, CompiledCall call, string name, double fallback, out double value)
        {
            value = fallback;
            DataNode n;
            if (!call.Args.TryGetValue(name, out n)) return true;
            if (NumberOf(n, out value)) return true;
            BadArg(run, call, name, "number");
            return false;
        }

        private bool OptBool(Run run, CompiledCall call, string name, bool fallback, out bool value)
        {
            value = fallback;
            DataNode n;
            if (!call.Args.TryGetValue(name, out n)) return true;
            if (n.Kind == DataKind.Bool) { value = n.Bool; return true; }
            BadArg(run, call, name, "Bool");
            return false;
        }

        private static bool NumberOf(DataNode n, out double value)
        {
            if (n.Kind == DataKind.Integer) { value = n.Integer; return true; }
            if (n.Kind == DataKind.Number) { value = n.Number; return true; }
            value = 0;
            return false;
        }

        private void BadArg(Run run, CompiledCall call, string name, string expected)
        {
            DataNode n;
            var actual = call.Args.TryGetValue(name, out n) ? n.Kind.ToString() : "missing";
            Abort(run, Fail(ResultCodes.ContentInvalid, "argument " + name + " of " + call.Function.Name + " is missing or malformed", call.Source, expected, actual));
        }

        private void PortNull(Run run, CompiledCall call, string port)
        {
            Abort(run, Fail(PortFailure, port + " returned no handle", call == null ? null : call.Source, "handle", "null"));
        }

        // ------------------------------------------------------------------ handle waiting

        /// <summary>Tracks the handle as a live child; next (optional) runs only while the run is not closing and the generation is unchanged.</summary>
        private void WaitFor(Run run, IHandle h, Action<Result> next)
        {
            var gen = run.Gen;
            run.Live.Add(h);
            h.OnTerminal(r =>
            {
                run.Live.Remove(h);
                if (next != null && run.Gen == gen && !run.Closing) next(r);
                TryFinishClose(run);
            });
        }

        // ------------------------------------------------------------------ commit

        private void Finish(Run run)
        {
            run.Phase = Phase.Committing;
            Trace(run, EventPhases.Commit, string.Empty);
            var snapshot = d.Store.Current;
            var report = d.Conditions.Evaluate(run.Def.When, snapshot);
            if (!report.Satisfied)
            {
                var code = HasCode(report.Reasons, ResultCodes.Unregistered) ? ResultCodes.Unregistered : ResultCodes.ConditionFalse;
                Abort(run, new Failure
                {
                    Code = code,
                    Reason = "conditions changed before commit",
                    Details = report.Reasons.Count > 0 ? report.Reasons : new[] { new Diagnostic(code, "conditions changed before commit", null, "satisfied", "unsatisfied") }
                });
                return;
            }
            var planned = d.CommitPlanner.PlanCommit(run.Def, new EffectContext(snapshot, run.Txn, run.Def.Completion));
            if (!planned.Succeeded) { Abort(run, FailureFrom(planned.Result)); return; }
            var plan = planned.Value;
            if (!plan.TransactionId.Equals(run.Txn) || plan.ExpectedRevision != snapshot.Revision)
            {
                Abort(run, Fail(ResultCodes.TransactionMismatch, "planned commit does not match the run", null, run.Txn.Value + "@" + snapshot.Revision.ToString(CultureInfo.InvariantCulture), plan.TransactionId.Value + "@" + plan.ExpectedRevision.ToString(CultureInfo.InvariantCulture)));
                return;
            }
            var committed = d.Store.Commit(plan);
            if (!committed.Succeeded) { Abort(run, FailureFrom(committed.Result)); return; }
            CompleteSuccess(run, committed.Result.Revision);
        }

        private void CompleteSuccess(Run run, long revision)
        {
            run.Committed = true;
            Trace(run, EventPhases.Done, "committed");
            if (run.Scene) Guard(run, "clear entry", () => Note(run, d.Recovery.ClearEntry(run.Owner), "ClearEntry"));
            Guard(run, "close owner", () => d.PresentationLifetime.CloseOwner(run.Owner));
            Guard(run, "release pause", () => { if (run.Pause != null) d.Pauses.Release(run.Pause); });
            Guard(run, "release lease", () => d.Leases.Release(run.Lease.LeaseId));
            run.Phase = Phase.Done;
            active.Remove(run);
            Guard(run, "notify boundary", () => d.Boundaries.Notify(run.Scene ? SafeBoundary.SceneComplete : SafeBoundary.RoomSafe, d.Store.Current));
            CompletionKey? completion = run.Def.Repeat == RepeatPolicy.Once ? run.Def.OnceKey : (CompletionKey?)null;
            run.Handle.Succeed(new EventCompletion(run.Def.Id, completion, revision), revision);
            AutoSave();
        }

        private void AutoSave()
        {
            Unsaved = true;
            IHandle<FileGeneration> save = null;
            try { save = d.Saves.SaveLatestSafe(); }
            catch (Exception e)
            {
                LastSaveResult = new Result(OperationStatus.Failed, "SAVE_FAILED", d.Store.Current.Revision, new[] { new Diagnostic("SAVE_FAILED", e.Message, null, "saved", "exception") });
                return;
            }
            if (save == null)
            {
                LastSaveResult = new Result(OperationStatus.Failed, "SAVE_FAILED", d.Store.Current.Revision, new[] { new Diagnostic("SAVE_FAILED", "SaveLatestSafe returned no handle", null, "handle", "null") });
                return;
            }
            pendingSaves.Add(save);
            save.OnTerminal(r =>
            {
                pendingSaves.Remove(save);
                LastSaveResult = r;
                Unsaved = r.Status != OperationStatus.Succeeded;
            });
        }

        // ------------------------------------------------------------------ abort and cleanup

        private void OnCancel(Run run, string reason)
        {
            if (run.Closing || run.Committed || run.Phase == Phase.Done) return;
            var text = reason ?? "cancelled";
            Abort(run, new Failure
            {
                Code = ResultCodes.Cancelled,
                Reason = text,
                IsCancel = true,
                Details = new[] { new Diagnostic(ResultCodes.Cancelled, text, null, "running", "cancelled") }
            });
        }

        private void Abort(Run run, Failure failure)
        {
            if (run.Closing || run.Phase == Phase.Done) return;
            run.Closing = true;
            run.Phase = Phase.Closing;
            run.Failure = failure;
            run.Gen++;
            run.Waiting = false;
            Trace(run, EventPhases.Abort, failure.Code);
            foreach (var h in new List<IHandle>(run.Live)) h.Cancel("event aborted");
            TryFinishClose(run);
        }

        private void TryFinishClose(Run run)
        {
            if (!run.Closing || run.CleanupStarted || run.Live.Count > 0) return;
            run.CleanupStarted = true;
            Trace(run, EventPhases.Cleanup, string.Empty);
            Guard(run, "close owner", () => d.PresentationLifetime.CloseOwner(run.Owner));
            if (run.Scene && run.EntryCaptured) RestoreMap(run); else Release(run);
        }

        private void RestoreMap(Run run)
        {
            var entryMap = run.EntryMapId;
            if (!string.IsNullOrEmpty(entryMap) && d.Maps.ActiveMapId != entryMap)
            {
                Trace(run, EventPhases.Restore, "map:" + entryMap);
                IHandle load = null;
                Guard(run, "map load", () => load = d.Maps.Load(entryMap, run.Owner));
                if (load != null)
                {
                    run.RecoveryLoad = load;
                    load.OnTerminal(r =>
                    {
                        if (run.Phase == Phase.Done) return;
                        run.RecoveryLoad = null;
                        if (r.Status != OperationStatus.Succeeded)
                            Note(run, new Diagnostic(ResultCodes.MapLoadFailed, "entry map could not be restored", null, entryMap, r.Code));
                        RestoreState(run);
                    });
                    return;
                }
            }
            RestoreState(run);
        }

        private void RestoreState(Run run)
        {
            Trace(run, EventPhases.Restore, "state");
            Guard(run, "restore entry", () =>
            {
                var restored = d.Recovery.RestoreEntry(run.Owner);
                if (!restored.Succeeded) { Note(run, restored.Result, "RestoreEntry"); return; }
                var replaced = d.Store.ReplaceAll(restored.Value, d.Store.Current.Revision, new TransactionId(run.Txn.Value + ".restore"));
                if (!replaced.Succeeded) Note(run, replaced.Result, "ReplaceAll");
                else if (d.Boundaries is ISafeBoundaryRenewal renewal) renewal.Renew(SafeBoundary.WorldSafe, replaced.Value, false);
            });
            Guard(run, "clear entry", () => Note(run, d.Recovery.ClearEntry(run.Owner), "ClearEntry"));
            Release(run);
        }

        private void Release(Run run)
        {
            Guard(run, "release pause", () => { if (run.Pause != null) d.Pauses.Release(run.Pause); });
            Guard(run, "release lease", () => d.Leases.Release(run.Lease.LeaseId));
            run.Phase = Phase.Done;
            active.Remove(run);
            var revision = d.Store.Current.Revision;
            var failure = run.Failure;
            Trace(run, EventPhases.Done, failure.Code);
            if (failure.IsCancel)
            {
                run.Handle.Cancelled(failure.Reason, revision);
                return;
            }
            var details = new List<Diagnostic>(failure.Details);
            details.AddRange(run.Notes);
            run.Handle.Fail(failure.Code, revision, details);
        }

        private void Guard(Run run, string label, Action action)
        {
            try { action(); }
            catch (Exception e) { Note(run, new Diagnostic(CleanupFailed, label + " failed: " + e.Message, null, "no exception", "exception")); }
        }

        private void Note(Run run, Result result, string what)
        {
            if (result.Status == OperationStatus.Succeeded) return;
            if (result.Details.Count == 0) Note(run, new Diagnostic(result.Code, what + " did not succeed", null, "Succeeded", result.Status.ToString()));
            foreach (var diag in result.Details) Note(run, diag);
        }

        private void Note(Run run, Diagnostic diag)
        {
            run.Notes.Add(diag);
            cleanupDiagnostics.Add(diag);
            if (cleanupDiagnostics.Count > 256) cleanupDiagnostics.RemoveAt(0);
        }

        // ------------------------------------------------------------------ failures and trace

        private static Failure Fail(string code, string message, SourceLocation at, string expected, string actual)
        {
            return new Failure { Code = code, Reason = message, Details = new[] { new Diagnostic(code, message, at, expected, actual) } };
        }

        private static Failure FailureFrom(Result r)
        {
            var details = r.Details.Count > 0 ? r.Details : new[] { new Diagnostic(r.Code, "operation ended " + r.Status, null, "Succeeded", r.Status.ToString()) };
            return new Failure { Code = r.Code, Reason = r.Code, IsCancel = r.Status == OperationStatus.Cancelled, Details = details };
        }

        private void Trace(Run run, string phase, string call)
        {
            Emit(run.Def.Id, phase, call);
        }

        private void Emit(string eventId, string phase, string call)
        {
            var trace = d.Trace;
            if (trace == null) return;
            trace(new EventTraceEntry(d.Clock.TickCount,++traceSeq, d.Store.Current.Revision, eventId, call, phase));
        }
    }
}
