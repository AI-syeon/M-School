using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    public interface ICompletionQueue
    {
        /// <summary>Runs action on the next tick, never synchronously inside Post.</summary>
        void Post(Action action);
    }

    public sealed class Lease
    {
        public string LeaseId { get; }
        public string Owner { get; }
        public IReadOnlyList<string> Resources { get; }   // defensive copy, read-only

        public Lease(string leaseId, string owner, IEnumerable<string> resources)
        {
            LeaseId = Ids.Require(leaseId, nameof(leaseId));
            Owner = Ids.Require(owner, nameof(owner));
            Resources = Ids.Copy(resources, nameof(resources));
        }
    }

    public sealed class LeaseRequest
    {
        public string Owner { get; }
        public IReadOnlyList<string> Resources { get; }   // distinct, ordinal-sorted

        public LeaseRequest(string owner, IEnumerable<string> resources)
        {
            Owner = Ids.Require(owner, nameof(owner));
            Resources = PortGuard.IdSet(resources, nameof(resources));
        }
    }

    public interface ILeaseService
    {
        /// <summary>Acquires every resource or none. Failure is RESOURCE_BUSY with one diagnostic per busy resource.</summary>
        Outcome<Lease> TryAcquire(LeaseRequest request);

        /// <summary>Idempotent; unknown or already released ids succeed.</summary>
        Result Release(string leaseId);
    }
}
