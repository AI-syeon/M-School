using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// All-or-none resource leases. Resources come canonical (distinct, ordinal-sorted) from LeaseRequest.
    /// TryAcquire grants the whole set or refuses with RESOURCE_BUSY; it never preempts an active holder.
    /// </summary>
    public sealed class LeaseService : ILeaseService
    {
        private readonly Dictionary<string, string> holders = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Lease> active = new Dictionary<string, Lease>(StringComparer.Ordinal);
        private long nextLease;

        // Debug / assertion surface.
        public int ActiveLeaseCount => active.Count;
        public int HeldResourceCount => holders.Count;

        public string HolderOf(string resource)
        {
            string id;
            return resource != null && holders.TryGetValue(resource, out id) ? id : null;
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

        public Result Release(string leaseId)
        {
            if (leaseId == null) throw new ArgumentNullException(nameof(leaseId));
            Lease lease;
            if (active.TryGetValue(leaseId, out lease))
            {
                active.Remove(leaseId);
                foreach (var r in lease.Resources) holders.Remove(r);
            }
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
    }
}
