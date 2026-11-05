using System;
using System.Collections.Generic;
using MagicCampus.Content.Catalog;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Order: version, catalog hash, payload parse + envelope/payload consistency, migration, then SaveDocument.Admit
    /// (root shape, the catalog validator once for schema and references, typed read).
    /// The envelope checksum is verified once, by SaveCodec.Decode, and is not repeated here.
    /// The first failing stage ends the restore. Nothing is applied here; the caller uses GameStore.Replace.
    /// </summary>
    public sealed class SaveRestorer
    {
        private readonly IDataCodec codec;
        private readonly Func<ICatalog, ISaveValidator> validatorFor;

        public SaveRestorer(IDataCodec codec, Func<ICatalog, ISaveValidator> validatorFor)
        {
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.validatorFor = validatorFor ?? throw new ArgumentNullException(nameof(validatorFor));
        }

        public Outcome<GameState> Restore(SaveEnvelope envelope, ICatalog catalog)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            if (envelope.SaveVersion < 1 || envelope.SaveVersion > SaveFields.CurrentVersion)
                return Bad(ResultCodes.SaveIncompatible, D("VERSION_UNSUPPORTED", "save version is not supported by this build", "1.." + SaveFields.CurrentVersion, envelope.SaveVersion.ToString()));
            if (!string.Equals(envelope.CatalogHash, catalog.CatalogHash, StringComparison.Ordinal))
                return Bad(ResultCodes.SaveIncompatible, D("CATALOG_HASH_MISMATCH", "save was written against a different catalog", catalog.CatalogHash, envelope.CatalogHash));

            var parsed = codec.Parse("save-payload", envelope.Payload);
            if (!parsed.Succeeded)
            {
                var d = new List<Diagnostic>(parsed.Result.Details);
                d.Add(D("PAYLOAD_PARSE_FAILED", "payload is not a JSON object", "JSON object", parsed.Result.Code));
                return Bad(ResultCodes.ContentInvalid, d.ToArray());
            }
            var root = parsed.Value.Root;
            var mismatch = CheckMetadata(root, envelope);
            if (mismatch != null) return Bad(ResultCodes.ContentInvalid, mismatch);

            var migrated = SaveMigration.Migrate(root, envelope.SaveVersion);
            if (!migrated.Succeeded)
            {
                var d = new List<Diagnostic>(migrated.Result.Details);
                if (d.Count == 0) d.Add(D("MIGRATION_FAILED", "migration refused the save", "migratable save", migrated.Result.Code));
                return Bad(migrated.Result.Code, d.ToArray());
            }
            return SaveDocument.Admit(migrated.Value, validatorFor(catalog));
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

        private static Outcome<GameState> Bad(string code, params Diagnostic[] details)
        {
            return Outcome<GameState>.Fail(code, 0, details);
        }
    }
}
