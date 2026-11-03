#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Presentation.Cutscene;
using UnityEngine;

namespace MagicCampus.Presentation.Editor
{
    // Test-only local fakes. They implement the frozen ports and are never used outside the editor test entry.

    internal sealed class FakeQueue
    {
        readonly Queue<Action> q = new Queue<Action>();
        public void Enqueue(Action a) { q.Enqueue(a); }
        public int Pending { get { return q.Count; } }

        public void Drain()
        {
            var guard = 0;
            while (q.Count > 0)
            {
                q.Dequeue()();
                if (++guard > 10000) throw new InvalidOperationException("fake queue did not settle");
            }
        }
    }

    internal sealed class FakeHandle<T> : IHandleController<T>
    {
        readonly FakeQueue queue;
        readonly List<Action<Result>> callbacks = new List<Action<Result>>();
        Action<string> cancelHandler;

        public FakeHandle(string id, FakeQueue queue)
        {
            OperationId = id;
            this.queue = queue;
            Status = OperationStatus.Pending;
        }

        public string OperationId { get; private set; }
        public OperationStatus Status { get; private set; }
        public Result Terminal { get; private set; }
        public T Value { get; private set; }

        public void Start() { if (Status == OperationStatus.Pending) Status = OperationStatus.Running; }

        bool Finish(OperationStatus status, string code, long revision, IEnumerable<Diagnostic> details, T value)
        {
            if (Terminal != null) return false;
            Terminal = new Result(status, code, revision, details);
            Status = status;
            Value = value;
            foreach (var cb in callbacks)
            {
                var captured = cb;
                var result = Terminal;
                queue.Enqueue(() => captured(result));
            }
            callbacks.Clear();
            return true;
        }

        public bool Succeed(T value, long revision) { return Finish(OperationStatus.Succeeded, ResultCodes.Ok, revision, null, value); }
        public bool Fail(string code, long revision, IReadOnlyList<Diagnostic> details) { return Finish(OperationStatus.Failed, code, revision, details, default(T)); }

        public bool Cancelled(string reason, long revision)
        {
            return Finish(OperationStatus.Cancelled, ResultCodes.Cancelled, revision, new[] { new Diagnostic(ResultCodes.Cancelled, reason, null, string.Empty, string.Empty) }, default(T));
        }

        public void SetCancellationHandler(Action<string> handler) { cancelHandler = handler; }

        public void Cancel(string reason)
        {
            if (Terminal != null) return;
            if (cancelHandler != null) cancelHandler(reason);
            else Cancelled(reason, 0);
        }

        public void OnTerminal(Action<Result> callback)
        {
            if (Terminal != null)
            {
                var result = Terminal;
                queue.Enqueue(() => callback(result));
            }
            else callbacks.Add(callback);
        }
    }

    internal sealed class FakeHandleFactory : IHandleFactory
    {
        readonly FakeQueue queue;
        public FakeHandleFactory(FakeQueue queue) { this.queue = queue; }
        public IHandleController<T> Create<T>(string operationId) { return new FakeHandle<T>(operationId, queue); }
    }

    internal sealed class FakePause : IPauseService
    {
        readonly List<PauseToken> live = new List<PauseToken>();
        int serial;
        public int Live { get { return live.Count; } }

        public IPausePolicy Effective
        {
            get
            {
                bool c = false, a = false, p = false, w = false, s = false;
                foreach (var t in live)
                {
                    c |= t.Policy.Calendar; a |= t.Policy.Ambient; p |= t.Policy.Player; w |= t.Policy.World; s |= t.Policy.Scene;
                }
                return new PauseSet(c, a, p, w, s);
            }
        }

        public PauseToken Acquire(string owner, IPausePolicy requested)
        {
            serial++;
            var t = new PauseToken("fake.token." + serial, owner, requested);
            live.Add(t);
            return t;
        }

        public void Release(PauseToken token) { if (token != null) live.Remove(token); }
    }

    internal sealed class FakeMap : IMapPort
    {
        public FakeMap(string active) { ActiveMapId = active; }
        public string ActiveMapId { get; private set; }
        public long NavRevision { get { return 0; } }
        public bool TryGetPortal(string portalId, out Portal portal) { portal = null; return false; }
        public IReadOnlyList<Portal> PortalsOn(string mapId) { return new Portal[0]; }
        public IHandle Load(string mapId, string ownerToken) { throw new NotSupportedException("FakeMap does not load maps"); }
    }

    internal sealed class FakeActors : IActorVisualResolver
    {
        public readonly Dictionary<string, Transform> Map = new Dictionary<string, Transform>(StringComparer.Ordinal);
        public bool TryResolveVisual(string actorId, out Transform visual) { return Map.TryGetValue(actorId, out visual); }
    }

    internal sealed class FakeInputPort : IInputPort
    {
        public InputFrame Next;
        public InputFrame Poll(long tick) { return Next; }
    }
}
#endif
