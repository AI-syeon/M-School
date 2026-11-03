using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MagicCampus.Contracts;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Version 2 passes through. Version 1 is a REPRESENTATIVE fixture format (full v2 minus the fields in V1AddedFields); it is not a
    /// claim about any historically deployed save. Missing V1AddedFields receive the documented defaults and saveVersion becomes 2.
    /// Present fields are never overwritten and no field is ever deleted. Unknown versions fail with SAVE_INCOMPATIBLE.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>Fields that the representative v1 lacks, with their v2 initial values.</summary>
        public static readonly IReadOnlyDictionary<string, DataNode> V1AddedFields = BuildDefaults();

        public static Outcome<DataNode> Migrate(DataNode save, int fromVersion)
        {
            if (fromVersion != 1 && fromVersion != SaveFields.CurrentVersion)
                return Fail(ResultCodes.SaveIncompatible, "VERSION_UNSUPPORTED", "no migration path from this version", "1 or " + SaveFields.CurrentVersion, fromVersion.ToString());
            if (save == null || save.Kind != DataKind.Object)
                return Fail(ResultCodes.ContentInvalid, "SAVE_NOT_OBJECT", "save must be an object", "object", save == null ? "null" : save.Kind.ToString());
            DataNode ver;
            if (!save.TryGet("saveVersion", out ver) || ver.Kind != DataKind.Integer || ver.Integer != fromVersion)
                return Fail(ResultCodes.ContentInvalid, "VERSION_FIELD_MISMATCH", "saveVersion field does not match the declared version", fromVersion.ToString(), ver == null ? "absent" : ver.Kind.ToString());
            if (fromVersion == SaveFields.CurrentVersion) return Outcome<DataNode>.Ok(save, 0);

            var r = save;
            foreach (var kv in V1AddedFields)
                if (!r.Members.ContainsKey(kv.Key)) r = r.With(kv.Key, kv.Value);
            r = r.With("saveVersion", DataNode.FromInteger(SaveFields.CurrentVersion));
            return Outcome<DataNode>.Ok(r, 0);
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
