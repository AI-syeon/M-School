using System;
using MagicCampus.Content.Catalog;
using MagicCampus.Contracts;
using MagicCampus.Runtime;
using MagicCampus.Transactions;

namespace MagicCampus.Events
{
    public sealed class EventStartRequest
    {
        public string EventId { get; }
        public string OwnerToken { get; }
        public long InputSequence { get; }
        public TransactionId TransactionId { get; }

        public EventStartRequest(string eventId, string ownerToken, long inputSequence, TransactionId transactionId)
        {
            EventId = Ids.Require(eventId, nameof(eventId)); OwnerToken = Ids.Require(ownerToken, nameof(ownerToken));
            InputSequence = PortGuard.Min(inputSequence, 0, nameof(inputSequence));
            if (transactionId.Value == null) throw new ArgumentException("default id", nameof(transactionId));
            TransactionId = transactionId;
        }
    }

    public sealed class EventCompletion
    {
        public string EventId { get; }
        public CompletionKey? Completion { get; }
        public long CommittedRevision { get; }

        public EventCompletion(string eventId, CompletionKey? completion, long committedRevision)
        {
            EventId = Ids.Require(eventId, nameof(eventId)); Completion = completion;
            CommittedRevision = PortGuard.Min(committedRevision, 0, nameof(committedRevision));
        }
    }

    /// <summary>Called only after the runner holds all event resources and durably records scene entry.
    /// Current staging must not warp actors. Reserved staging may join actors under a hidden transition.
    /// Cancellation finishes child work and removes the hidden transition before completing.</summary>
    public interface IEventStagingPort
    {
        IHandle Stage(CompiledEvent definition, Lease lease);
    }
}
