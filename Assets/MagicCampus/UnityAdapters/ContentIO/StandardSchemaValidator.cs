using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MagicCampus.Contracts;
using EvaluationResults = Json.Schema.EvaluationResults;
using JsonSchema = Json.Schema.JsonSchema;

namespace MagicCampus.UnityAdapters.ContentIO
{
    /// <summary>JSON Schema 2020-12 validation through JsonSchema.Net 7.2. Never throws for content errors and never passes silently on failure.</summary>
    public sealed class StandardSchemaValidator : ISchemaValidationPort
    {
        public const string SchemaErrorCode = "SCHEMA_ERROR";
        private const string Draft2020 = "https://json-schema.org/draft/2020-12/schema";
        private const int MaxActual = 200;

        // Keywords whose object value is a map of name -> subschema (the names are not keywords).
        private static readonly HashSet<string> SchemaMaps = new HashSet<string>(StringComparer.Ordinal)
            { "properties", "patternProperties", "$defs", "definitions", "dependentSchemas" };
        // Keywords holding instance data, not subschemas.
        private static readonly HashSet<string> DataKeys = new HashSet<string>(StringComparer.Ordinal)
            { "const", "enum", "default", "examples" };

        private readonly IDataCodec codec;

        public StandardSchemaValidator(IDataCodec codec)
        {
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public IReadOnlyList<Diagnostic> Validate(DataNode schema, DataNode instance, string file)
        {
            file = file ?? string.Empty;
            var list = new List<Diagnostic>();
            try
            {
                if (schema == null || instance == null)
                {
                    list.Add(Make(file, "", "schema and instance are required", "non-null", "null"));
                    return list.AsReadOnly();
                }

                ScanRefs(schema, "", false, file, list);
                if (list.Count > 0) return list.AsReadOnly();

                JsonSchema compiled;
                try { compiled = JsonSchema.FromText(Text(schema)); }
                catch (Exception e)
                {
                    list.Add(Make(file, "", "malformed schema: " + e.Message, "valid Draft 2020-12 schema", Describe(schema)));
                    return list.AsReadOnly();
                }

                var result = compiled.Evaluate(JsonNode.Parse(Text(instance)), new Json.Schema.EvaluationOptions {
                    OutputFormat = Json.Schema.OutputFormat.Hierarchical
                });
                if (!result.IsValid)
                {
                    Collect(result, instance, file, list);
                    if (list.Count == 0)
                        list.Add(Make(file, "", "instance rejected by schema", "valid instance", Describe(instance)));
                }
            }
            catch (Exception e)
            {
                list.Add(Make(file, "", "schema validation failed: " + e.GetType().Name + ": " + e.Message, "successful evaluation", "exception"));
            }
            return list.AsReadOnly();
        }

        private string Text(DataNode n) => Encoding.UTF8.GetString(codec.Serialize(n).ToArray());

        private string Describe(DataNode n)
        {
            if (n == null) return "(absent)";
            var t = Text(n);
            return t.Length > MaxActual ? t.Substring(0, MaxActual) + "..." : t;
        }

        private static Diagnostic Make(string file, string pointer, string message, string expected, string actual)
            => new Diagnostic(SchemaErrorCode, message, new SourceLocation(file, pointer, "", "", 0), expected, actual);

        // Rejects anything that could make the evaluator resolve resources outside the document.
        private void ScanRefs(DataNode n, string ptr, bool isMap, string file, List<Diagnostic> list)
        {
            if (n.Kind == DataKind.List)
            {
                for (var i = 0; i < n.Items.Count; i++)
                    ScanRefs(n.Items[i], ptr + "/" + i.ToString(CultureInfo.InvariantCulture), false, file, list);
                return;
            }
            if (n.Kind != DataKind.Object) return;
            foreach (var kv in n.Members)
            {
                var child = ptr + "/" + JsonDataCodec.EscapeToken(kv.Key);
                if (isMap) { ScanRefs(kv.Value, child, false, file, list); continue; }
                if (DataKeys.Contains(kv.Key)) continue;
                if (kv.Value.Kind == DataKind.String)
                {
                    var v = kv.Value.Text;
                    if ((kv.Key == "$ref" || kv.Key == "$dynamicRef") && !v.StartsWith("#", StringComparison.Ordinal))
                        list.Add(Make(file, child, "external " + kv.Key + " is not allowed (schema location)", "reference starting with '#'", v));
                    else if (kv.Key == "$schema" && v != Draft2020)
                        list.Add(Make(file, child, "unsupported $schema (schema location)", Draft2020, v));
                }
                ScanRefs(kv.Value, child, SchemaMaps.Contains(kv.Key), file, list);
            }
        }

        private void Collect(EvaluationResults r, DataNode instance, string file, List<Diagnostic> list)
        {
            if (r == null || r.IsValid) return;
            if (r.Errors != null)
            {
                var ptr = PointerText(r.InstanceLocation);
                foreach (var kv in r.Errors)
                    list.Add(Make(file, ptr, kv.Value, "keyword '" + kv.Key + "' satisfied", Describe(Resolve(instance, ptr))));
            }
            if (r.Details != null)
                foreach (var d in r.Details) Collect(d, instance, file, list);
        }

        private static string PointerText(object location)
        {
            if (location is Json.Pointer.JsonPointer pointer)
            {
                var encoded = new StringBuilder();
                for (var i = 0; i < pointer.Count; i++) encoded.Append('/').Append(JsonDataCodec.EscapeToken(pointer[i].ToString()));
                return encoded.ToString();
            }
            var s = location == null ? "" : location.ToString();
            return s.StartsWith("#", StringComparison.Ordinal) ? s.Substring(1) : s;
        }

        private static DataNode Resolve(DataNode n, string ptr)
        {
            if (ptr.Length == 0) return n;
            if (ptr[0] != '/') return null;
            foreach (var raw in ptr.Substring(1).Split('/'))
            {
                var seg = raw.Replace("~1", "/").Replace("~0", "~");
                if (n.Kind == DataKind.Object)
                {
                    if (!n.Members.TryGetValue(seg, out n)) return null;
                }
                else if (n.Kind == DataKind.List)
                {
                    if (!int.TryParse(seg, NumberStyles.None, CultureInfo.InvariantCulture, out var i) || i >= n.Items.Count) return null;
                    n = n.Items[i];
                }
                else return null;
            }
            return n;
        }
    }
}
