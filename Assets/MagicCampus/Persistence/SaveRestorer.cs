using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Order: checksum, version, catalog hash, payload parse + envelope/payload consistency, migration, root shape (45 fields, integer wallet),
    /// strict schema (injected port), references (injected validator factory, e.g. the catalog save validator), invariants.
    /// The first failing stage ends the restore. Nothing is applied here; the caller uses IStateStore.ReplaceAll.
    /// </summary>
    public sealed class SaveRestorer : ISaveRestorer
    {
        private readonly ISaveMigrationRegistry migrations;
        private readonly IDataCodec codec;
        private readonly ISchemaValidationPort schemaPort;
        private readonly DataNode saveSchema;
        private readonly Func<ICatalog, IStateInvariant> referenceValidatorFactory;
        private readonly IReadOnlyList<IStateInvariant> invariants;

        public SaveRestorer(ISaveMigrationRegistry migrations, IDataCodec codec, ISchemaValidationPort schemaPort, DataNode saveSchema,
            Func<ICatalog, IStateInvariant> referenceValidatorFactory, IEnumerable<IStateInvariant> invariants)
        {
            this.migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.schemaPort = schemaPort ?? throw new ArgumentNullException(nameof(schemaPort));
            this.saveSchema = saveSchema ?? throw new ArgumentNullException(nameof(saveSchema));
            this.referenceValidatorFactory = referenceValidatorFactory ?? throw new ArgumentNullException(nameof(referenceValidatorFactory));
            var list = new List<IStateInvariant>();
            if (invariants != null)
                foreach (var i in invariants)
                {
                    if (i == null) throw new ArgumentException("null invariant", nameof(invariants));
                    list.Add(i);
                }
            this.invariants = list.ToArray();
        }

        public Outcome<StateSnapshot> Restore(SaveEnvelope envelope, ICatalog catalog)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var payload = envelope.Payload.ToArray();
            var sum = SaveHash.Sha256Hex(payload);
            if (!string.Equals(sum, envelope.Checksum, StringComparison.Ordinal))
                return Bad(ResultCodes.ContentInvalid, D("CHECKSUM_MISMATCH", "payload checksum does not match", envelope.Checksum, sum));
            if (envelope.SaveVersion < 1 || envelope.SaveVersion > migrations.CurrentVersion)
                return Bad(ResultCodes.SaveIncompatible, D("VERSION_UNSUPPORTED", "save version is not supported by this build", "1.." + migrations.CurrentVersion, envelope.SaveVersion.ToString()));
            if (!string.Equals(envelope.CatalogHash, catalog.CatalogHash, StringComparison.Ordinal))
                return Bad(ResultCodes.SaveIncompatible, D("CATALOG_HASH_MISMATCH", "save was written against a different catalog", catalog.CatalogHash, envelope.CatalogHash));

            var parsed = codec.Parse("save-payload", payload);
            if (!parsed.Succeeded)
            {
                var d = new List<Diagnostic>(parsed.Result.Details);
                d.Add(D("PAYLOAD_PARSE_FAILED", "payload is not a JSON object", "JSON object", parsed.Result.Code));
                return Bad(ResultCodes.ContentInvalid, d.ToArray());
            }
            var root = parsed.Value.Root;
            var mismatch = CheckMetadata(root, envelope);
            if (mismatch != null) return Bad(ResultCodes.ContentInvalid, mismatch);

            var migrated = migrations.Migrate(root, envelope.SaveVersion);
            if (!migrated.Succeeded)
            {
                var d = new List<Diagnostic>(migrated.Result.Details);
                if (d.Count == 0) d.Add(D("MIGRATION_FAILED", "migration refused the save", "migratable save", migrated.Result.Code));
                return Outcome<StateSnapshot>.Fail(migrated.Result.Code, 0, d.ToArray());
            }
            var m = migrated.Value;
            DataNode ver;
            if (!m.TryGet("saveVersion", out ver) || ver.Kind != DataKind.Integer || ver.Integer != migrations.CurrentVersion)
                return Bad(ResultCodes.SaveIncompatible, D("MIGRATION_INCOMPLETE", "migration did not produce the current version", migrations.CurrentVersion.ToString(), ver == null ? "absent" : ver.Kind.ToString()));

            var shape = SaveFields.CheckRoot(m);
            if (shape.Count > 0) return Bad(ResultCodes.ContentInvalid, new List<Diagnostic>(shape).ToArray());

            var schemaDiagnostics = schemaPort.Validate(saveSchema, m, "save");
            if (schemaDiagnostics != null && schemaDiagnostics.Count > 0)
                return Bad(ResultCodes.ContentInvalid, new List<Diagnostic>(schemaDiagnostics).ToArray());

            var candidate = new StateSnapshot(0, m);
            var refDiagnostics = referenceValidatorFactory(catalog).Validate(candidate);
            if (refDiagnostics != null && refDiagnostics.Count > 0)
                return Bad(ResultCodes.ContentInvalid, new List<Diagnostic>(refDiagnostics).ToArray());

            var violations = new List<Diagnostic>();
            foreach (var inv in invariants)
            {
                var found = inv.Validate(candidate);
                if (found != null) violations.AddRange(found);
            }
            if (violations.Count > 0) return Bad(ResultCodes.InvariantViolation, violations.ToArray());
            return Outcome<StateSnapshot>.Ok(candidate, 0);
        }

        private static Diagnostic[] CheckMetadata(DataNode root, SaveEnvelope envelope)
        {
            DataNode v, g, h;
            var okV = root.TryGet("saveVersion", out v) && v.Kind == DataKind.Integer && v.Integer == envelope.SaveVersion;
            var okG = root.TryGet("generation", out g) && g.Kind == DataKind.Integer && g.Integer == envelope.Generation;
            var okH = root.TryGet("catalogHash", out h) && h.Kind == DataKind.String && string.Equals(h.Text, envelope.CatalogHash, StringComparison.Ordinal);
            if (okV && okG && okH) return null;
            return new[] { D("ENVELOPE_PAYLOAD_MISMATCH", "envelope metadata disagrees with the payload (saveVersion/generation/catalogHash)", "consistent metadata", (okV ? "" : "saveVersion ") + (okG ? "" : "generation ") + (okH ? "" : "catalogHash")) };
        }

        private static Diagnostic D(string code, string message, string expected, string actual)
        {
            return new Diagnostic(code, message, null, expected, actual);
        }

        private static Outcome<StateSnapshot> Bad(string code, params Diagnostic[] details)
        {
            return Outcome<StateSnapshot>.Fail(code, 0, details);
        }
    }
}
