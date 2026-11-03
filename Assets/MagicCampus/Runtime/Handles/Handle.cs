using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// Single-threaded handle: Pending -> Running -> exactly one terminal state. Late completions and repeated cancels are ignored.
    /// Every OnTerminal callback (including late registrations) is delivered exactly once through the completion queue, never inline.
    /// </summary>
    public sealed class Handle<T> : IHandleController<T>
    {
        private readonly ICompletionQueue queue;
        private readonly Action terminalNotify;
        private List<Action<Result>> callbacks = new List<Action<Result>>();
        private Action<string> cancelHandler;
        private OperationStatus status = OperationStatus.Pending;
        private Result terminal;
        private T resultValue;
        private bool cancelRequested;
        private string cancelReason = string.Empty;

        public Handle(string operationId, ICompletionQueue queue) : this(operationId, queue, null) { }

        public Handle(string operationId, ICompletionQueue queue, Action terminalNotify)
        {
            OperationId = Ids.Require(operationId, nameof(operationId));
            this.queue = PortGuard.Required(queue, nameof(queue));
            this.terminalNotify = terminalNotify;
        }

        public string OperationId { get; }
        public OperationStatus Status => status;
        public Result Terminal => terminal;
        public T Value => resultValue;

        // Debug / assertion surface; reading never mutates state.
        public bool CancelRequested => cancelRequested;
        public string CancelReason => cancelReason;
        public int PendingCallbackCount => callbacks.Count;

        public void Start()
        {
            if (status == OperationStatus.Pending) status = OperationStatus.Running;
        }

        public void OnTerminal(Action<Result> callback)
        {
            PortGuard.Required(callback, nameof(callback));
            if (terminal != null)
            {
                Deliver(callback, terminal);
                return;
            }
            callbacks.Add(callback);
        }

        public void SetCancellationHandler(Action<string> handler)
        {
            if (terminal == null) cancelHandler = handler;
        }

        public void Cancel(string reason)
        {
            if (terminal != null || cancelRequested) return;
            cancelRequested = true;
            cancelReason = reason ?? string.Empty;
            var handler = cancelHandler;
            if (handler == null)
            {
                Cancelled(cancelReason, 0);
                return;
            }
            try
            {
                handler(cancelReason);
            }
            catch (Exception ex)
            {
                Fail(ResultCodes.InvariantViolation, 0,
                    new[] { new Diagnostic(ResultCodes.InvariantViolation, "cancellation handler threw: " + ex.Message, null, string.Empty, ex.GetType().Name) });
            }
        }

        public bool Succeed(T value, long revision)
        {
            // Producers decide cancellation versus an already irreversible commit (for example file replace).
            // Once terminal, every late completion is ignored.
            if (terminal != null) return false;
            return Finish(OperationStatus.Succeeded, new Result(OperationStatus.Succeeded, ResultCodes.Ok, revision, null), value);
        }

        public bool Fail(string code, long revision, IReadOnlyList<Diagnostic> details)
        {
            if (terminal != null) return false;
            return Finish(OperationStatus.Failed, new Result(OperationStatus.Failed, code, revision, details), default(T));
        }

        public bool Cancelled(string reason, long revision)
        {
            if (terminal != null) return false;
            var detail = new Diagnostic(ResultCodes.Cancelled, reason ?? string.Empty, null, string.Empty, string.Empty);
            return Finish(OperationStatus.Cancelled, new Result(OperationStatus.Cancelled, ResultCodes.Cancelled, revision, new[] { detail }), default(T));
        }

        private bool Finish(OperationStatus s, Result r, T v)
        {
            if (terminal != null) return false;
            resultValue = v;
            terminal = r;
            status = s;
            cancelHandler = null;
            var pending = callbacks;
            callbacks = new List<Action<Result>>();
            terminalNotify?.Invoke();
            foreach (var cb in pending) Deliver(cb, r);
            return true;
        }

        private void Deliver(Action<Result> callback, Result r)
        {
            queue.Post(() => callback(r));
        }
    }
}
