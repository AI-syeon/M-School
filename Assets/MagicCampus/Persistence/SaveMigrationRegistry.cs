using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MagicCampus.Contracts;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Exact one-hop substitutions injected by the caller. IdMap applies to the known id fields, CompletionKeyMap to the known
    /// completion-key lists (see SaveMigrationRegistry). Anything not listed in a map is left unchanged.
    /// </summary>
    public sealed class SaveSubstitutionTable
    {
        public static readonly SaveSubstitutionTable Empty = new SaveSubstitutionTable(null, null);

        public IReadOnlyDictionary<string, string> IdMap { get; }
        public IReadOnlyDictionary<string, string> CompletionKeyMap { get; }

        public SaveSubstitutionTable(IEnumerable<KeyValuePair<string, string>> ids, IEnumerable<KeyValuePair<string, string>> completionKeys)
        {
            IdMap = Copy(ids, nameof(ids));
            CompletionKeyMap = Copy(completionKeys, nameof(completionKeys));
        }

        private static IReadOnlyDictionary<string, string> Copy(IEnumerable<KeyValuePair<string, string>> src, string name)
        {
            var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (src != null)
                foreach (var kv in src)
                {
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) throw new ArgumentException("empty key or value", name);
                    if (d.ContainsKey(kv.Key)) throw new ArgumentException("duplicate key " + kv.Key, name);
                    d.Add(kv.Key, kv.Value);
                }
            return new ReadOnlyDictionary<string, string>(d);
        }
    }

    /// <summary>
    /// Explicit migration table. Version 2 passes through. Version 1 is a REPRESENTATIVE fixture format (full v2 minus the fields in
    /// V1AddedFields); it is not a claim about any historically deployed save. Missing V1AddedFields receive the documented
    /// defaults, saveVersion becomes 2, then exact substitutions are applied to a fixed list of paths ('*' = every list element).
    /// Present fields are never overwritten and no field is ever deleted.
    /// </summary>
    public sealed class SaveMigrationRegistry : ISaveMigrationRegistry
    {
        /// <summary>Fields that the representative v1 lacks, with their v2 initial values.</summary>
        public static readonly IReadOnlyDictionary<string, DataNode> V1AddedFields = BuildDefaults();

        private static readonly string[] IdPaths =
        {
            "player/mapId", "player/appearanceId", "player/quickSlots/*", "stacks/*/itemId", "mail/*/id", "mail/*/attachments/*/itemId",
            "mail/*/reservationId", "evidence/*", "learnedSpellIds/*", "spellContexts/*/spellId", "spellContexts/*/contextIds/*",
            "relations/*/npcId", "relations/*/activityKeys/*", "relations/*/r1BaselineActivityKeys/*", "relations/*/activityRecords/*/activityId",
            "plots/*/plotId", "plots/*/plantId", "installedProjects/*", "placements/*/id", "placements/*/furnitureId", "placements/*/mapId",
            "appointments/*/id", "roomCheckpoints/*/roomId", "roomCheckpoints/*/completedObjectIds/*", "discoveredCaseIds/*",
            "questStates/*/caseId", "questStates/*/activeNodeId", "classRecords/*/classId", "discoveredSafePointIds/*", "npcPositions/*/npcId",
            "npcPositions/*/mapId", "defeats/*/encounterId", "shopState/*/shopId", "shopState/*/remaining/*/itemId", "creatureState/*/creatureId",
            "journal/*/entryId", "dialogueHistory/*/npcId", "flags/*/id", "caseResolutionOrder/*", "truthReadKeys/*", "companion/npcId",
            "companion/usedDays/*/npcId", "trackedActivityIds/*", "discoveredRecipeIds/*", "spellApplicationSelections/*/spellId",
            "acceptedActivityIds/*", "invitedRelationIds/*", "preAcceptedEvidenceIds/*"
        };

        private static readonly string[] CompletionPaths = { "completed/*", "rewardLedger/*", "dayJobs/*", "roomCheckpoints/*/confirmedRewardKeys/*" };

        private readonly SaveSubstitutionTable substitutions;

        public SaveMigrationRegistry(SaveSubstitutionTable substitutions)
        {
            this.substitutions = substitutions ?? throw new ArgumentNullException(nameof(substitutions));
        }

        public int CurrentVersion { get { return SaveFields.CurrentVersion; } }

        public Outcome<DataNode> Migrate(DataNode save, int fromVersion)
        {
            if (fromVersion != 1 && fromVersion != SaveFields.CurrentVersion)
                return Fail(ResultCodes.SaveIncompatible, "VERSION_UNSUPPORTED", "no migration path from this version", "1 or " + SaveFields.CurrentVersion, fromVersion.ToString());
            if (save == null || save.Kind != DataKind.Object)
                return Fail(ResultCodes.ContentInvalid, "SAVE_NOT_OBJECT", "save must be an object", "object", save == null ? "null" : save.Kind.ToString());
            DataNode ver;
            if (!save.TryGet("saveVersion", out ver) || ver.Kind != DataKind.Integer || ver.Integer != fromVersion)
                return Fail(ResultCodes.ContentInvalid, "VERSION_FIELD_MISMATCH", "saveVersion field does not match the declared version", fromVersion.ToString(), ver == null ? "absent" : ver.Kind.ToString());
            if (fromVersion == SaveFields.CurrentVersion) return Outcome<DataNode>.Ok(save, 0);
            if (fromVersion != 1)
                return Fail(ResultCodes.SaveIncompatible, "VERSION_UNSUPPORTED", "no migration path from this version", "1 or " + SaveFields.CurrentVersion, fromVersion.ToString());

            var r = save;
            foreach (var kv in V1AddedFields)
                if (!r.Members.ContainsKey(kv.Key)) r = r.With(kv.Key, kv.Value);
            r = r.With("saveVersion", DataNode.FromInteger(SaveFields.CurrentVersion));
            if (substitutions.IdMap.Count > 0)
                foreach (var p in IdPaths) r = Rewrite(r, p.Split('/'), 0, substitutions.IdMap);
            if (substitutions.CompletionKeyMap.Count > 0)
                foreach (var p in CompletionPaths) r = Rewrite(r, p.Split('/'), 0, substitutions.CompletionKeyMap);
            return Outcome<DataNode>.Ok(r, 0);
        }

        private static DataNode Rewrite(DataNode n, string[] segs, int i, IReadOnlyDictionary<string, string> map)
        {
            if (i == segs.Length)
            {
                string to;
                return n.Kind == DataKind.String && map.TryGetValue(n.Text, out to) ? DataNode.FromString(to) : n;
            }
            if (segs[i] == "*")
            {
                if (n.Kind != DataKind.List) return n;
                var items = new List<DataNode>(n.Items.Count);
                foreach (var it in n.Items) items.Add(Rewrite(it, segs, i + 1, map));
                return DataNode.FromList(items);
            }
            DataNode child;
            if (n.Kind != DataKind.Object || !n.TryGet(segs[i], out child)) return n;
            return n.With(segs[i], Rewrite(child, segs, i + 1, map));
        }

        private static Outcome<DataNode> Fail(string code, string detail, string message, string expected, string actual)
        {
            return Outcome<DataNode>.Fail(code, 0, new Diagnostic(detail, message, new SourceLocation("save", "/saveVersion", "", "", 0), expected, actual));
        }

        private static IReadOnlyDictionary<string, DataNode> BuildDefaults()
        {
            var d = new SortedDictionary<string, DataNode>(StringComparer.Ordinal);
            var empty = DataNode.FromList(new DataNode[0]);
            var lists = new[]
            {
                "mail", "spellContexts", "relations", "plots", "installedProjects", "placements", "appointments", "roomCheckpoints", "dayJobs",
                "shopState", "creatureState", "journal", "dialogueHistory", "caseResolutionOrder", "truthReadKeys", "trackedActivityIds",
                "discoveredRecipeIds", "spellApplicationSelections", "acceptedActivityIds", "invitedRelationIds", "preAcceptedEvidenceIds"
            };
            foreach (var n in lists) d[n] = empty;
            d["returnPending"] = DataNode.False;
            d["finalState"] = DataNode.FromObject(new Dictionary<string, DataNode>
            {
                { "status", DataNode.FromString("locked") },
                { "choice", DataNode.Null },
                { "archiveGeneration", DataNode.Null }
            });
            d["companion"] = DataNode.FromObject(new Dictionary<string, DataNode>
            {
                { "npcId", DataNode.Null },
                { "usedDays", empty }
            });
            return new ReadOnlyDictionary<string, DataNode>(d);
        }
    }
}
