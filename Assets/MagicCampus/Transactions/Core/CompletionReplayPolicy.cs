using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Transactions.Core
{
    /// <summary>Explicit policy telling the store whether a completion key was already persisted. Transaction ids are never consulted here.</summary>
    public interface ICompletionReplayPolicy
    {
        bool IsCompleted(StateSnapshot snapshot, CompletionKey key);
    }

    /// <summary>
    /// Looks the key up in persisted top-level ledgers (default: completed and rewardLedger).
    /// A ledger matches when it is a list containing the key as a string, or an object having the key as a member name.
    /// </summary>
    public sealed class PersistedCompletionReplayPolicy : ICompletionReplayPolicy
    {
        private readonly string[] fields;

        public PersistedCompletionReplayPolicy() : this(new[] { "completed", "rewardLedger" }) { }

        public PersistedCompletionReplayPolicy(IEnumerable<string> topLevelFields)
        {
            if (topLevelFields == null) throw new ArgumentNullException(nameof(topLevelFields));
            var list = new List<string>();
            foreach (var f in topLevelFields)
            {
                if (string.IsNullOrEmpty(f)) throw new ArgumentException("empty field name", nameof(topLevelFields));
                list.Add(f);
            }
            fields = list.ToArray();
        }

        public bool IsCompleted(StateSnapshot snapshot, CompletionKey key)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (key.Value == null) return false;
            foreach (var field in fields)
            {
                var node = snapshot.Slice(field);
                if (node != null && Contains(node, key.Value)) return true;
            }
            return false;
        }

        private static bool Contains(DataNode node, string key)
        {
            switch (node.Kind)
            {
                case DataKind.List:
                    foreach (var item in node.Items)
                        if (item.Kind == DataKind.String && string.Equals(item.Text, key, StringComparison.Ordinal)) return true;
                    return false;
                case DataKind.Object:
                    return node.Members.ContainsKey(key);
                default:
                    return false;
            }
        }
    }
}
