using MagicCampus.Contracts;
namespace MagicCampus.Application.Reservations
{
    /// <summary>Reads the original persisted once-completion lists; either consumed ledger blocks another award.</summary>
    public sealed class StateCompletionLedger : ICompletionLedger
    {
        public bool IsCompleted(StateSnapshot snapshot, CompletionKey key)
        {
            foreach (var field in new[] { "completed", "rewardLedger" })
            {
                var list = snapshot.Slice(field);
                if (list == null || list.Kind != DataKind.List) continue;
                foreach (var value in list.Items) if (value.Kind == DataKind.String && value.Text == key.Value) return true;
            }
            return false;
        }
    }
}
