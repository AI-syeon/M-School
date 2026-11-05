using MagicCampus.Contracts;
using MagicCampus.Persistence;
namespace MagicCampus.Application.Reservations
{
    /// <summary>Reads the typed progress of a snapshot; either consumed ledger blocks another award.</summary>
    public sealed class StateCompletionLedger : ICompletionLedger
    {
        public bool IsCompleted(StateSnapshot snapshot, CompletionKey key)
        {
            return snapshot.TypedState().Progress.IsConsumed(key);
        }
    }
}
