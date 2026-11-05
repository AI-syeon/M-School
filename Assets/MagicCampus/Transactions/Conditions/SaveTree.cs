using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Transactions.Conditions
{
    /// <summary>Read helpers for the fixed save v2 fields used by conditions and event effects. Nothing here writes state.</summary>
    public static class SaveTree
    {
        public const string Flags = "flags", Completed = "completed", RewardLedger = "rewardLedger", Wallet = "wallet";
        public const string Discovered = "discoveredCaseIds", Evidence = "evidence", Learned = "learnedSpellIds";
        public const string Crowns = "crowns", Appointments = "appointments";
        // Original closed save schema wallet.maximum, not the storage width of DataNode.Integer.
        public const long WalletLimit = int.MaxValue;

        public static string Pointer(string field)
        {
            return "/" + field;
        }

        public static bool TryReadStrings(StateSnapshot snapshot, string field, out List<string> values, out Diagnostic error)
        {
            values = null;
            var node = snapshot.Slice(field);
            if (node == null || node.Kind != DataKind.List)
            {
                error = Diag.Make(ResultCodes.ContentInvalid, "save field " + Pointer(field) + " must be a list of strings", "list of strings", node == null ? "absent" : node.Kind.ToString());
                return false;
            }
            var list = new List<string>(node.Items.Count);
            foreach (var item in node.Items)
            {
                if (item.Kind != DataKind.String)
                {
                    error = Diag.Make(ResultCodes.ContentInvalid, "save field " + Pointer(field) + " holds a non-string element", "string", item.Kind.ToString());
                    return false;
                }
                list.Add(item.Text);
            }
            values = list;
            error = null;
            return true;
        }

        public static DataNode ToList(IEnumerable<string> values)
        {
            var items = new List<DataNode>();
            foreach (var v in values) items.Add(DataNode.FromString(v));
            return DataNode.FromList(items);
        }

        public static bool TryReadWallet(StateSnapshot snapshot, out DataNode wallet, out long crowns, out Diagnostic error)
        {
            wallet = snapshot.Slice(Wallet);
            crowns = 0;
            if (wallet == null || wallet.Kind != DataKind.Integer || wallet.Integer < 0)
            {
                error = Diag.Make(ResultCodes.ContentInvalid, "/wallet must be a non-negative integer", "wallet integer", wallet == null ? "absent" : Diag.Value(wallet));
                return false;
            }
            crowns = wallet.Integer;
            error = null;
            return true;
        }
    }
}
