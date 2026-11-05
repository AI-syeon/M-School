using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Tests.PureDomain.Support
{
    /// <summary>The save fields the typed state reads, as valid empty values, so a test tree built from them is a readable state.</summary>
    internal static class TypedFields
    {
        public static readonly string[] Names =
        {
            "clock", "dayJobs", "returnPending", "player", "npcPositions", "flags", "completed", "rewardLedger", "wallet",
            "appointments", "evidence", "learnedSpellIds", "discoveredCaseIds", "caseResolutionOrder"
        };

        /// <summary>Adds each typed field the map does not already hold.</summary>
        public static void FillMissing(IDictionary<string, DataNode> m)
        {
            Add(m, "clock", DataNode.FromObject(new[]
            {
                new KeyValuePair<string, DataNode>("absoluteMinute", DataNode.FromInteger(420)),
                new KeyValuePair<string, DataNode>("fraction", DataNode.FromNumber(0))
            }));
            Add(m, "dayJobs", DataNode.FromList(new DataNode[0]));
            Add(m, "returnPending", DataNode.False);
            Add(m, "player", DataNode.FromObject(new[]
            {
                new KeyValuePair<string, DataNode>("mapId", DataNode.FromString("map.a")),
                new KeyValuePair<string, DataNode>("x", DataNode.FromNumber(1.5)),
                new KeyValuePair<string, DataNode>("y", DataNode.FromNumber(1.5))
            }));
            foreach (var list in new[] { "npcPositions", "flags", "completed", "rewardLedger", "appointments", "evidence", "learnedSpellIds", "discoveredCaseIds", "caseResolutionOrder" })
                Add(m, list, DataNode.FromList(new DataNode[0]));
            Add(m, "wallet", DataNode.FromInteger(0));
        }

        private static void Add(IDictionary<string, DataNode> m, string key, DataNode value)
        {
            if (!m.ContainsKey(key)) m[key] = value;
        }
    }
}
