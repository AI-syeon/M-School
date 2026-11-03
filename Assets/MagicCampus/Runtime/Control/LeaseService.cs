using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// All-or-none resource leases. Resources come canonical (distinct, ordinal-sorted) from LeaseRequest.
    /// Waiters are ordered priority desc, ReservedAt, Sequence, Owner (ordinal), arrival. A waiter never preempts an active holder,
    /// and a lower waiter cannot overtake a higher blocked waiter on overlapping resources.
    /// TryAcquire only looks at active holders.
    /// </summary>
    public sealed class LeaseService : ILeaseService
    {
        private sealed class Waiter
        {
            public LeaseRequest Request;
            public IHandleController<Lease> Handle;
            public long Arrival;
        }

        private readonly IHandleFactory factory;
        private readonly Dictionary<string, string> holders = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Lease> active = new Dictionary<string, Lease>(StringComparer.Ordinal);
        private readonly List<Waiter> queue = new List<Waiter>();
        private long nextLease;
        private long nextWaiter;

        public LeaseService(IHandleFactory factory)
        {
            this.factory = RuntimeGuard.Required(factory, nameof(factory));
        }

        // Debug / assertion surface.
        public int ActiveLeaseCount => active.Count;
        public int HeldResourceCount => holders.Count;
        public int QueuedCount => queue.Count;

        public string HolderOf(string resource)
        {
            string id;
            return resource != null && holders.TryGetValue(resource, out id) ? id : null;
        }

        public List<string> QueuedOwners()
        {
            var owners = new List<string>();
            foreach (var w in queue) owners.Add(w.Request.Owner);
            return owners;
        }

        public Outcome<Lease> TryAcquire(LeaseRequest request)
        {
            RuntimeGuard.Required(request, nameof(request));
            var busy = new List<Diagnostic>();
            foreach (var r in request.Resources)
            {
                string holder;
                if (holders.TryGetValue(r, out holder))
                    busy.Add(new Diagnostic(ResultCodes.ResourceBusy, "resource " + r + " is held by " + holder, null, "free", holder));
            }
            if (busy.Count > 0) return Outcome<Lease>.Fail(ResultCodes.ResourceBusy, 0, busy.ToArray());
            return Outcome<Lease>.Ok(Grant(request), 0);
        }

        public IHandle<Lease> Enqueue(LeaseRequest request)
        {
            RuntimeGuard.Required(request, nameof(request));
            nextWaiter++;
            var handle = factory.Create<Lease>("lease.wait." + nextWaiter);
            handle.Start();
            var waiter = new Waiter { Request = request, Handle = handle, Arrival = nextWaiter };
            handle.SetCancellationHandler(reason =>
            {
                queue.Remove(waiter);
                handle.Cancelled(reason, 0);
                Dispatch();
            });
            Insert(waiter);
            Dispatch();
            return handle;
        }

        public Result Release(string leaseId)
        {
            if (leaseId == null) throw new ArgumentNullException(nameof(leaseId));
            if (ReleaseInternal(leaseId)) Dispatch();
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }

        private Lease Grant(LeaseRequest request)
        {
            nextLease++;
            var lease = new Lease("lease-" + nextLease, request.Owner, request.Resources);
            active.Add(lease.LeaseId, lease);
            foreach (var r in lease.Resources) holders[r] = lease.LeaseId;
            return lease;
        }

        private bool ReleaseInternal(string leaseId)
        {
            Lease lease;
            if (!active.TryGetValue(leaseId, out lease)) return false;
            active.Remove(leaseId);
            foreach (var r in lease.Resources) holders.Remove(r);
            return true;
        }

        private void Insert(Waiter w)
        {
            var index = queue.Count;
            for (var i = 0; i < queue.Count; i++)
            {
                if (Compare(queue[i], w) > 0) { index = i; break; }
            }
            queue.Insert(index, w);
        }

        private static int Compare(Waiter a, Waiter b)
        {
            var c = b.Request.Priority.CompareTo(a.Request.Priority);
            if (c != 0) return c;
            c = a.Request.ReservedAt.CompareTo(b.Request.ReservedAt);
            if (c != 0) return c;
            c = a.Request.Sequence.CompareTo(b.Request.Sequence);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Request.Owner, b.Request.Owner);
            if (c != 0) return c;
            return a.Arrival.CompareTo(b.Arrival);
        }

        private void Dispatch()
        {
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var i = 0;
            while (i < queue.Count)
            {
                var w = queue[i];
                var free = true;
                foreach (var r in w.Request.Resources)
                {
                    if (holders.ContainsKey(r) || blocked.Contains(r)) { free = false; break; }
                }
                if (!free)
                {
                    foreach (var r in w.Request.Resources) blocked.Add(r);
                    i++;
                    continue;
                }
                queue.RemoveAt(i);
                var lease = Grant(w.Request);
                if (!w.Handle.Succeed(lease, 0)) ReleaseInternal(lease.LeaseId);
            }
        }
    }
}
