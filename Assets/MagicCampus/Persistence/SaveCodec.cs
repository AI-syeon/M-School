using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using MagicCampus.Contracts;

namespace MagicCampus.Persistence
{
    public static class SaveHash
    {
        /// <summary>Lowercase hex SHA-256 of the exact bytes.</summary>
        public static string Sha256Hex(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            const string hex = "0123456789abcdef";
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(data);
                var c = new char[h.Length * 2];
                for (var i = 0; i < h.Length; i++)
                {
                    c[2 * i] = hex[h[i] >> 4];
                    c[2 * i + 1] = hex[h[i] & 15];
                }
                return new string(c);
            }
        }

        public static bool IsLowerHex64(string s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (var ch in s)
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))) return false;
            return true;
        }
    }

    /// <summary>
    /// Envelope framing through the injected IDataCodec. File layout (UTF-8 JSON object, exactly six members):
    /// format, saveVersion, catalogHash, generation, checksum (lowercase SHA-256 hex of the payload bytes), payload (strict canonical base64
    /// of the UTF-8 JSON of the full save v2 object). Decode checks framing and checksum only.
    /// </summary>
    public sealed class SaveCodec
    {
        public const string Format = "magic-campus.save-envelope.v1";

        private readonly IDataCodec codec;

        public SaveCodec(IDataCodec dataCodec)
        {
            codec = dataCodec ?? throw new ArgumentNullException(nameof(dataCodec));
        }

        /// <summary>Throws InvalidOperationException when the snapshot is not a complete save v2 object.</summary>
        public SaveEnvelope Seal(StateSnapshot snapshot, long generation)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            var shape = SaveFields.CheckRoot(snapshot.Root);
            if (shape.Count > 0) throw new InvalidOperationException("snapshot is not a complete save v2 object: " + shape[0].Code + " " + shape[0].Actual);
            DataNode version, hash;
            snapshot.Root.TryGet("saveVersion", out version);
            snapshot.Root.TryGet("catalogHash", out hash);
            if (version.Kind != DataKind.Integer || version.Integer != SaveFields.CurrentVersion)
                throw new InvalidOperationException("only save version " + SaveFields.CurrentVersion + " can be sealed");
            if (hash.Kind != DataKind.String || hash.Text.Length == 0)
                throw new InvalidOperationException("catalogHash must be a non-empty string");
            var root = snapshot.Root.With("generation", DataNode.FromInteger(generation));
            var bytes = codec.Serialize(root).ToArray();
            return new SaveEnvelope(SaveFields.CurrentVersion, hash.Text, generation, SaveHash.Sha256Hex(bytes), bytes);
        }

        public ReadOnlyMemory<byte> Encode(SaveEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            var members = new Dictionary<string, DataNode>(StringComparer.Ordinal)
            {
                { "format", DataNode.FromString(Format) },
                { "saveVersion", DataNode.FromInteger(envelope.SaveVersion) },
                { "catalogHash", DataNode.FromString(envelope.CatalogHash) },
                { "generation", DataNode.FromInteger(envelope.Generation) },
                { "checksum", DataNode.FromString(envelope.Checksum) },
                { "payload", DataNode.FromString(Convert.ToBase64String(envelope.Payload.ToArray())) }
            };
            return codec.Serialize(DataNode.FromObject(members));
        }

        public Outcome<SaveEnvelope> Decode(ReadOnlyMemory<byte> fileBytes)
        {
            var parsed = codec.Parse("save-envelope", fileBytes);
            if (!parsed.Succeeded)
                return Bad("ENVELOPE_PARSE_FAILED", "envelope is not parseable", "UTF-8 JSON object", parsed.Result.Code, parsed.Result.Details);
            var r = parsed.Value.Root;
            string format, hash, checksum, b64;
            long version, generation;
            if (r.Members.Count != 6 || !TryStr(r, "format", out format) || format != Format
                || !TryInt(r, "saveVersion", out version) || !TryInt(r, "generation", out generation)
                || !TryStr(r, "catalogHash", out hash) || !TryStr(r, "checksum", out checksum) || !TryStr(r, "payload", out b64))
                return Bad("ENVELOPE_FRAMING", "envelope must have exactly the six framing members with the expected types", Format, "malformed", null);
            if (version < 1 || version > int.MaxValue || generation < 0 || hash.Length == 0 || !SaveHash.IsLowerHex64(checksum))
                return Bad("ENVELOPE_FRAMING", "envelope member out of range", "version>=1, generation>=0, 64 lowercase hex checksum", "out of range", null);
            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                return Bad("PAYLOAD_ENCODING", "payload is not valid base64", "base64", "invalid", null);
            }
            if (!string.Equals(Convert.ToBase64String(payload), b64, StringComparison.Ordinal))
                return Bad("PAYLOAD_ENCODING", "payload base64 is not canonical", "canonical base64", "non-canonical", null);
            var actual = SaveHash.Sha256Hex(payload);
            if (!string.Equals(actual, checksum, StringComparison.Ordinal))
                return Bad("CHECKSUM_MISMATCH", "payload checksum does not match", checksum, actual, null);
            try
            {
                var env = new SaveEnvelope((int)version, hash, generation, checksum, payload);
                return Outcome<SaveEnvelope>.Ok(env, generation);
            }
            catch (ArgumentException e)
            {
                return Bad("ENVELOPE_FIELD", e.Message, "valid envelope fields", "invalid", null);
            }
        }

        private static Outcome<SaveEnvelope> Bad(string code, string message, string expected, string actual, IReadOnlyList<Diagnostic> inner)
        {
            var d = new List<Diagnostic>();
            if (inner != null) d.AddRange(inner);
            d.Add(new Diagnostic(code, message, new SourceLocation("save-envelope", "", "", "", 0), expected, actual));
            return Outcome<SaveEnvelope>.Fail(ResultCodes.ContentInvalid, 0, d.ToArray());
        }

        private static bool TryStr(DataNode r, string key, out string value)
        {
            DataNode n;
            if (r.TryGet(key, out n) && n.Kind == DataKind.String) { value = n.Text; return true; }
            value = null;
            return false;
        }

        private static bool TryInt(DataNode r, string key, out long value)
        {
            DataNode n;
            if (r.TryGet(key, out n) && n.Kind == DataKind.Integer) { value = n.Integer; return true; }
            value = 0;
            return false;
        }
    }
}
