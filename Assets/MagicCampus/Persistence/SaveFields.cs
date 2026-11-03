using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Persistence
{
    /// <summary>Fixed facts about the original save v2 root: the 45 field names, boundary names and a structural root check.</summary>
    public static class SaveFields
    {
        public const int CurrentVersion = 2;

        public static readonly IReadOnlyList<string> Root = Array.AsReadOnly(new[]
        {
            "saveVersion", "contentVersion", "generation", "safePoint", "clock", "player", "wallet", "stacks", "mail", "completed",
            "evidence", "learnedSpellIds", "spellContexts", "relations", "plots", "installedProjects", "placements", "appointments",
            "roomCheckpoints", "rewardLedger", "dayJobs", "settings", "discoveredCaseIds", "questStates", "classRecords",
            "discoveredSafePointIds", "npcPositions", "defeats", "shopState", "creatureState", "journal", "dialogueHistory", "flags",
            "returnPending", "caseResolutionOrder", "truthReadKeys", "finalState", "companion", "trackedActivityIds",
            "discoveredRecipeIds", "spellApplicationSelections", "acceptedActivityIds", "invitedRelationIds", "preAcceptedEvidenceIds",
            "catalogHash"
        });

        public static string BoundaryName(SafeBoundary boundary)
        {
            switch (boundary)
            {
                case SafeBoundary.WorldSafe: return "world-safe";
                case SafeBoundary.SceneEntry: return "scene-entry";
                case SafeBoundary.SceneComplete: return "scene-complete";
                case SafeBoundary.RoomSafe: return "room-safe";
                default: throw new ArgumentOutOfRangeException(nameof(boundary));
            }
        }

        public static bool TryParseBoundary(string name, out SafeBoundary boundary)
        {
            switch (name)
            {
                case "world-safe": boundary = SafeBoundary.WorldSafe; return true;
                case "scene-entry": boundary = SafeBoundary.SceneEntry; return true;
                case "scene-complete": boundary = SafeBoundary.SceneComplete; return true;
                case "room-safe": boundary = SafeBoundary.RoomSafe; return true;
                default: boundary = SafeBoundary.WorldSafe; return false;
            }
        }

        /// <summary>Exactly the 45 fields, no more, no fewer, and an integer wallet in range. Never repairs or deletes anything.</summary>
        public static IReadOnlyList<Diagnostic> CheckRoot(DataNode root)
        {
            var d = new List<Diagnostic>();
            if (root == null || root.Kind != DataKind.Object)
            {
                d.Add(new Diagnostic("SAVE_ROOT_NOT_OBJECT", "save root must be a JSON object", new SourceLocation("save", "", "", "", 0), "object", root == null ? "null" : root.Kind.ToString()));
                return d;
            }
            var known = new HashSet<string>(Root, StringComparer.Ordinal);
            foreach (var name in Root)
                if (!root.Members.ContainsKey(name))
                    d.Add(new Diagnostic("SAVE_FIELD_MISSING", "required save field is missing", new SourceLocation("save", Pointer(name), "", "", 0), name, "absent"));
            foreach (var kv in root.Members)
                if (!known.Contains(kv.Key))
                    d.Add(new Diagnostic("SAVE_FIELD_UNKNOWN", "unknown save field is refused, never dropped", new SourceLocation("save", Pointer(kv.Key), "", "", 0), "one of the 45 save v2 fields", kv.Key));
            DataNode wallet;
            if (root.TryGet("wallet", out wallet) && (wallet.Kind != DataKind.Integer || wallet.Integer < 0 || wallet.Integer > int.MaxValue))
                d.Add(new Diagnostic("SAVE_WALLET_NOT_INTEGER", "wallet must be an integer in 0..2147483647", new SourceLocation("save", "/wallet", "", "", 0), "integer", wallet.Kind.ToString()));
            return d;
        }

        private static string Pointer(string key)
        {
            return "/" + key.Replace("~", "~0").Replace("/", "~1");
        }
    }
}
