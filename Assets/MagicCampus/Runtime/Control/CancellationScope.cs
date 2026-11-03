using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    public enum ScopePhase { Open, Draining, Closed }

    /// <summary>
    /// Parent/child cancellation scope for one parent handle. Tracks child handles and cleanup actions.
    /// A failing (or externally cancelled) child cancels its siblings. The parent is finalized only after every child is terminal
    /// (observed through next-tick OnTerminal callbacks) and after cleanups ran in reverse registration order.
    /// Severity: a pending Success is overridden by a later Fail/Cancel; Fail/Cancel are never overridden.
    /// </summary>
    public sealed class CancellationScope<T>
    {
        private enum FinalKind { None, Success, Fail, Cancel }

        private readonly IHandleController<T> parent;
        private readonly List<IHandle> children = new List<IHandle>();
        private readonly List<Action> cleanups = new List<Action>();
        private ScopePhase phase = ScopePhase.Open;
        private FinalKind kind = FinalKind.None;
        private int outstanding;
        private int cleanupFaults;
        private T value;
        private string failCode = ResultCodes.Ok;
        private long revision;
        private IReadOnlyList<Diagnostic> details;
        private string reason = string.Empty;

        public CancellationScope(IHandleController<T> parent)
        {
            this.parent = RuntimeGuard.Required(parent, nameof(parent));
            parent.Start();
            parent.SetCancellationHandler(OnParentCancel);
        }

        public ScopePhase Phase => phase;
        public int OutstandingChildren => outstanding;
        public int ChildCount => children.Count;
        public int PendingCleanupCount => cleanups.Count;
        public int CleanupFaultCount => cleanupFaults;

        public THandle AddChild<THandle>(THandle child) where THandle : class, IHandle
        {
            RuntimeGuard.Required(child, nameof(child));
            if (phase == ScopePhase.Closed)
            {
                child.Cancel("scope closed");
                return child;
            }
            children.Add(child);
            outstanding++;
            child.OnTerminal(r => OnChildTerminal(child, r));
            if (phase == ScopePhase.Draining && kind != FinalKind.Success) child.Cancel(reason);
            return child;
        }

        public void AddCleanup(Action cleanup)
        {
            RuntimeGuard.Required(cleanup, nameof(cleanup));
            if (phase == ScopePhase.Closed)
            {
                try { cleanup(); } catch (Exception) { cleanupFaults++; }
                return;
            }
            cleanups.Add(cleanup);
        }

        /// <summary>Success once every child has succeeded; waits for children without cancelling them.</summary>
        public void Complete(T result, long committedRevision)
        {
            Request(FinalKind.Success, result, ResultCodes.Ok, committedRevision, null, string.Empty);
        }

        public void Fail(string code, long failedRevision, IReadOnlyList<Diagnostic> failDetails)
        {
            RuntimeGuard.Id(code, nameof(code));
            Request(FinalKind.Fail, default(T), code, failedRevision, failDetails, "scope failed: " + code);
        }

        /// <summary>Routes through the parent so the parent records the cancel request exactly once.</summary>
        public void Cancel(string why)
        {
            parent.Cancel(why);
        }

        private void OnParentCancel(string why)
        {
            Request(FinalKind.Cancel, default(T), ResultCodes.Cancelled, 0, null, why ?? string.Empty);
        }

        private void OnChildTerminal(IHandle child, Result r)
        {
            outstanding--;
            if (r.Status != OperationStatus.Succeeded)
                Request(FinalKind.Fail, default(T), r.Code, r.Revision, r.Details, "sibling " + child.OperationId + " ended " + r.Code);
            TryFinish();
        }

        private void Request(FinalKind k, T v, string code, long rev, IReadOnlyList<Diagnostic> d, string why)
        {
            if (phase == ScopePhase.Closed) return;
            if (phase == ScopePhase.Draining && (kind != FinalKind.Success || k == FinalKind.Success)) return;
            phase = ScopePhase.Draining;
            kind = k;
            value = v;
            failCode = code;
            revision = rev;
            details = d;
            reason = why;
            if (k != FinalKind.Success) CancelChildren(why);
            TryFinish();
        }

        private void CancelChildren(string why)
        {
            foreach (var child in new List<IHandle>(children))
            {
                if (!child.Status.IsTerminal()) child.Cancel(why);
            }
        }

        private void TryFinish()
        {
            if (phase != ScopePhase.Draining || outstanding > 0) return;
            phase = ScopePhase.Closed;
            var faults = new List<Diagnostic>();
            for (var i = cleanups.Count - 1; i >= 0; i--)
            {
                try
                {
                    cleanups[i]();
                }
                catch (Exception ex)
                {
                    cleanupFaults++;
                    faults.Add(new Diagnostic(ResultCodes.InvariantViolation, "cleanup threw: " + ex.Message, null, string.Empty, ex.GetType().Name));
                }
            }
            cleanups.Clear();
            children.Clear();
            switch (kind)
            {
                case FinalKind.Success:
                    if (faults.Count == 0) parent.Succeed(value, revision);
                    else parent.Fail(ResultCodes.InvariantViolation, revision, faults);
                    break;
                case FinalKind.Fail:
                    var merged = new List<Diagnostic>();
                    if (details != null) merged.AddRange(details);
                    merged.AddRange(faults);
                    parent.Fail(failCode, revision, merged);
                    break;
                default:
                    parent.Cancelled(reason, revision);
                    break;
            }
        }
    }
}
