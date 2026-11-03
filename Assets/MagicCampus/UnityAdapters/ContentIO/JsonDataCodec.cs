using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MagicCampus.Contracts;

namespace MagicCampus.UnityAdapters.ContentIO
{
    /// <summary>System.Text.Json adapter for IDataCodec. Roots must be JSON objects; every failure comes back as diagnostics.</summary>
    public sealed class JsonDataCodec : IDataCodec
    {
        private const int MaxDepth = 64;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private sealed class ContentException : Exception
        {
            public string Code { get; }
            public string Pointer { get; }
            public int Offset { get; }
            public string Expected { get; }
            public string Actual { get; }

            public ContentException(string code, string message, string pointer, int offset, string expected, string actual)
                : base(message)
            {
                Code = code; Pointer = pointer; Offset = offset; Expected = expected; Actual = actual;
            }
        }

        public Outcome<ContentBundle> Parse(string file, ReadOnlyMemory<byte> utf8)
        {
            file = file ?? string.Empty;
            byte[] bytes = null;
            var start = 0;
            try
            {
                bytes = utf8.ToArray();
                var hash = Sha256Hex(bytes);   // exact original bytes, BOM included
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3;
                CheckUtf8(bytes, start);
                var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = MaxDepth };
                using (var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, start, bytes.Length - start), options))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        throw new ContentException(ResultCodes.ContentInvalid, "root must be a JSON object", "", start, "object", root.ValueKind.ToString().ToLowerInvariant());
                    return Outcome<ContentBundle>.Ok(new ContentBundle(file, ToNode(root, ""), hash), 0);
                }
            }
            catch (ContentException e)
            {
                return Fail(file, e.Code, e.Message, e.Pointer, e.Offset, e.Expected, e.Actual);
            }
            catch (JsonException e)
            {
                return Fail(file, ResultCodes.ParseError, e.Message, "", OffsetOf(bytes, start, e.LineNumber, e.BytePositionInLine), "well-formed JSON object", e.Message);
            }
            catch (Exception e)
            {
                return Fail(file, ResultCodes.ContentInvalid, e.GetType().Name + ": " + e.Message, "", 0, "valid content", "unexpected failure");
            }
        }

        public ReadOnlyMemory<byte> Serialize(DataNode root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            using (var ms = new MemoryStream())
            {
                var options = new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                using (var w = new Utf8JsonWriter(ms, options)) Write(w, root);
                return ms.ToArray();
            }
        }

        /// <summary>Lowercase hex SHA-256 of exactly the given bytes.</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        internal static string EscapeToken(string s) => s.Replace("~", "~0").Replace("/", "~1");

        private static Outcome<ContentBundle> Fail(string file, string code, string message, string pointer, int offset, string expected, string actual)
            => Outcome<ContentBundle>.Fail(code, 0, new Diagnostic(code, message, new SourceLocation(file, pointer, "", "", Math.Max(0, offset)), expected, actual));

        private static void CheckUtf8(byte[] b, int start)
        {
            try { StrictUtf8.GetCharCount(new ReadOnlySpan<byte>(b, start, b.Length - start)); }
            catch (DecoderFallbackException e)
            {
                var at = (int)Math.Min((long)b.Length, (long)start + Math.Max(0, e.Index));
                throw new ContentException(ResultCodes.ParseError, "invalid UTF-8", "", at, "valid UTF-8", "invalid byte sequence");
            }
        }

        private static int OffsetOf(byte[] b, int start, long? line, long? pos)
        {
            if (b == null || !line.HasValue || !pos.HasValue) return 0;
            long off = start, l = 0;
            while (l < line.Value && off < b.Length) { if (b[off++] == (byte)'\n') l++; }
            off += pos.Value;
            return (int)Math.Max(0, Math.Min(off, b.Length));
        }

        private static DataNode ToNode(JsonElement e, string ptr)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Null: return DataNode.Null;
                case JsonValueKind.True: return DataNode.True;
                case JsonValueKind.False: return DataNode.False;
                case JsonValueKind.String: return DataNode.FromString(e.GetString());
                case JsonValueKind.Number: return ToNumber(e, ptr);
                case JsonValueKind.Array:
                {
                    var items = new List<DataNode>();
                    var i = 0;
                    foreach (var x in e.EnumerateArray())
                        items.Add(ToNode(x, ptr + "/" + (i++).ToString(CultureInfo.InvariantCulture)));
                    return DataNode.FromList(items);
                }
                case JsonValueKind.Object:
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    var members = new List<KeyValuePair<string, DataNode>>();
                    foreach (var p in e.EnumerateObject())
                    {
                        var name = p.Name;
                        var child = ptr + "/" + EscapeToken(name);
                        if (!seen.Add(name))
                            throw new ContentException(ResultCodes.ContentInvalid, "duplicate object key", child, 0, "unique key", name);
                        members.Add(new KeyValuePair<string, DataNode>(name, ToNode(p.Value, child)));
                    }
                    return DataNode.FromObject(members);
                }
                default:
                    throw new ContentException(ResultCodes.ContentInvalid, "unsupported JSON value", ptr, 0, "JSON value", e.ValueKind.ToString());
            }
        }

        private static DataNode ToNumber(JsonElement e, string ptr)
        {
            var raw = e.GetRawText();
            if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0)
            {
                if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return DataNode.FromInteger(l);
                throw new ContentException(ResultCodes.ContentInvalid, "integer outside 64-bit range", ptr, 0, "integer within long range", raw);
            }
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && !double.IsInfinity(d))
                return DataNode.FromNumber(d);
            throw new ContentException(ResultCodes.ContentInvalid, "non-finite number", ptr, 0, "finite number", raw);
        }

        private static void Write(Utf8JsonWriter w, DataNode n)
        {
            switch (n.Kind)
            {
                case DataKind.Null: w.WriteNullValue(); break;
                case DataKind.Bool: w.WriteBooleanValue(n.Bool); break;
                case DataKind.Integer: w.WriteNumberValue(n.Integer); break;
                case DataKind.Number: w.WriteRawValue(FormatNumber(n.Number)); break;
                case DataKind.String: w.WriteStringValue(n.Text); break;
                case DataKind.List:
                    w.WriteStartArray();
                    foreach (var item in n.Items) Write(w, item);
                    w.WriteEndArray();
                    break;
                case DataKind.Object:
                    w.WriteStartObject();
                    foreach (var key in n.Members.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        w.WritePropertyName(key);
                        Write(w, n.Members[key]);
                    }
                    w.WriteEndObject();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(n));
            }
        }

        // Shortest round-trip text; always carries '.' or an exponent so a Number never re-parses as Integer.
        private static string FormatNumber(double d)
        {
            var s = d.ToString("R", CultureInfo.InvariantCulture);
            return s.IndexOfAny(new[] { '.', 'E', 'e' }) < 0 ? s + ".0" : s;
        }
    }
}
