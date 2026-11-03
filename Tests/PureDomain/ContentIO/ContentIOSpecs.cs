using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MagicCampus.Contracts;
using MagicCampus.UnityAdapters.ContentIO;

namespace MagicCampus.Tests.ContentIO
{
    public static class ContentIOSpecs
    {
        private static readonly JsonDataCodec Codec = new JsonDataCodec();
        private static readonly StandardSchemaValidator Validator = new StandardSchemaValidator(Codec);
        private static int failures;

        /// <summary>Runs every spec and returns the failure count (0 = all passed).</summary>
        public static int Run()
        {
            failures = 0;
            Case("roundtrip keeps every field", RoundTrip);
            Case("deep immutability and deterministic order", Immutable);
            Case("hash covers raw bytes incl. BOM and whitespace", RawHash);
            Case("malformed input refused", Malformed);
            Case("duplicate keys refused", Duplicates);
            Case("non-finite and out-of-range numbers refused", NonFinite);
            Case("schema required/unknown/const/range", SchemaBasics);
            Case("schema oneOf/unique/integer/boolean", SchemaMore);
            Case("schema refs and malformed schema", SchemaRefs);
            Case("pointer diagnostics on root data fixture", Pointers);
            Console.WriteLine(failures == 0 ? "ContentIOSpecs: all passed" : "ContentIOSpecs: " + failures + " failed");
            return failures;
        }

        private static void Case(string name, Action body)
        {
            try { body(); Console.WriteLine("PASS " + name); }
            catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }

        private static void Expect(bool ok, string what) { if (!ok) throw new Exception(what); }

        private static void Throws(Action a)
        {
            try { a(); } catch (NotSupportedException) { return; }
            throw new Exception("expected NotSupportedException");
        }

        // Fixtures use single quotes for readability.
        private static string Q(string s) => s.Replace('\'', '"');
        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(Q(s));
        private static string Str(ReadOnlyMemory<byte> m) => Encoding.UTF8.GetString(m.ToArray());
        private static string Sha(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }

        private static DataNode Obj(string json)
        {
            var o = Codec.Parse("t.json", Utf8(json));
            Expect(o.Succeeded, "fixture rejected: " + json);
            return o.Value.Root;
        }

        private static void RoundTrip()
        {
            var root = Obj("{'s':'h\u00e9llo \\'q\\' \\n','i':-42,'max':9223372036854775807,'n':7.5,'one':1.0,'e':1e2,'t':true,'f':false,'z':null,'l':[1,[2,{'k':[]}],'x'],'o':{'b':{},'a':0}}");
            Expect(root.Members.Count == 11, "member count");
            Expect(root.Members["s"].Text == "h\u00e9llo \"q\" \n", "string escapes");
            Expect(root.Members["i"].Kind == DataKind.Integer && root.Members["i"].Integer == -42, "integer");
            Expect(root.Members["max"].Integer == long.MaxValue, "long max");
            Expect(root.Members["n"].Kind == DataKind.Number && root.Members["n"].Number == 7.5, "decimal kept");
            Expect(root.Members["one"].Kind == DataKind.Number, "1.0 stays Number");
            Expect(root.Members["e"].Kind == DataKind.Number && root.Members["e"].Number == 100, "exponent is Number");
            Expect(root.Members["t"].Bool && !root.Members["f"].Bool && root.Members["z"].Kind == DataKind.Null, "bool/null");
            Expect(root.Members["l"].Items[1].Items[1].Members["k"].Items.Count == 0, "deep empty list");
            var bytes = Codec.Serialize(root);
            Expect(Str(bytes).Contains(Q("'one':1.0")), "Number keeps decimal point");
            var again = Codec.Parse("t.json", bytes);
            Expect(again.Succeeded && again.Value.Root.Equals(root), "round trip equal");
            Expect(Codec.Serialize(again.Value.Root).ToArray().SequenceEqual(bytes.ToArray()), "stable bytes");
            Expect(Str(Codec.Serialize(DataNode.False)) == "false", "non-object nodes serialize");
        }

        private static void Immutable()
        {
            var root = Obj("{'b':[1,{'x':1}],'a':{'y':[]}}");
            Throws(() => ((IDictionary<string, DataNode>)root.Members).Add("c", DataNode.Null));
            Throws(() => ((IList<DataNode>)root.Members["b"].Items).Add(DataNode.Null));
            Throws(() => ((IDictionary<string, DataNode>)root.Members["b"].Items[1].Members).Remove("x"));
            var changed = root.With("c", DataNode.True);
            Expect(root.Members.Count == 2 && changed.Members.Count == 3, "With copies");
            Expect(Str(Codec.Serialize(root)) == Q("{'a':{'y':[]},'b':[1,{'x':1}]}"), "ordinal key order");
        }

        private static void RawHash()
        {
            var plain = Utf8("{'a':1}");
            var spaced = Utf8("  {\n 'a' : 1 }\r\n");
            var bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(plain).ToArray();
            var p = Codec.Parse("h.json", plain).Value;
            var s = Codec.Parse("h.json", spaced).Value;
            var b = Codec.Parse("h.json", bom);
            Expect(b.Succeeded, "BOM must be accepted");
            Expect(p.SourceHash == Sha(plain) && p.SourceHash.Length == 64 && p.SourceHash == p.SourceHash.ToLowerInvariant(), "plain hash");
            Expect(s.SourceHash == Sha(spaced) && s.SourceHash != p.SourceHash, "whitespace changes hash");
            Expect(b.Value.SourceHash == Sha(bom) && b.Value.SourceHash != p.SourceHash, "BOM is hashed");
            Expect(s.Root.Equals(p.Root) && b.Value.Root.Equals(p.Root), "same tree");
            Expect(Str(Codec.Serialize(p.Root)) == Q("{'a':1}"), "hash is outside payload");
        }

        private static void Malformed()
        {
            var cases = new List<byte[]>
            {
                Utf8(""), Utf8("{"), Utf8("[1]"), Utf8("1"), Utf8("null"), Utf8("{'a':1,}"), Utf8("{/*c*/'a':1}"),
                Utf8("{'a':NaN}"), Utf8("{'a':01}"), Utf8("{'a':1} x"), Utf8("{a:1}"),
                new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x22, 0xFF, 0x22, 0x7D }
            };
            foreach (var c in cases)
            {
                var o = Codec.Parse("bad.json", c);
                Expect(!o.Succeeded && o.Value == null && o.Result.Details.Count > 0, "accepted: " + BitConverter.ToString(c));
                Expect(o.Result.Details[0].Location.File == "bad.json", "diagnostic file");
            }
            Expect(Codec.Parse("a.json", Utf8("[1]")).Result.Details[0].Expected == "object", "root array reason");
        }

        private static void Duplicates()
        {
            var o = Codec.Parse("d.json", Utf8("{'x':{'k':1,'k':2}}"));
            Expect(!o.Succeeded && o.Result.Details[0].Location.Pointer == "/x/k", "nested duplicate pointer");
            Expect(!Codec.Parse("d.json", Utf8("{'a':1,'a':2}")).Succeeded, "top-level duplicate");
            Expect(!Codec.Parse("d.json", Utf8("{'a':1,'\\u0061':2}")).Succeeded, "escaped duplicate");
        }

        private static void NonFinite()
        {
            foreach (var j in new[] { "{'a':1e999}", "{'a':-1e999}", "{'a':99999999999999999999}" })
                Expect(!Codec.Parse("n.json", Utf8(j)).Succeeded, "accepted " + j);
            var o = Codec.Parse("n.json", Utf8("{'a':[0,{'b':1e400}]}"));
            Expect(!o.Succeeded && o.Result.Details[0].Location.Pointer == "/a/1/b", "nonfinite pointer");
        }

        private static IReadOnlyList<Diagnostic> Check(string schema, string inst) => Validator.Validate(Obj(schema), Obj(inst), "inst.json");

        private static void Rejects(string schema, string inst, string pointer)
        {
            var d = Check(schema, inst);
            Expect(d.Count > 0 && d.All(x => x.Code == "SCHEMA_ERROR"), "expected SCHEMA_ERROR for " + inst);
            Expect(pointer == null || d.Any(x => x.Location.Pointer == pointer), "no diagnostic at '" + pointer + "' for " + inst);
        }

        private static void Accepts(string schema, string inst)
        {
            var d = Check(schema, inst);
            Expect(d.Count == 0, "unexpected: " + string.Join("; ", d.Select(x => x.Location.Pointer + " " + x.Message)));
        }

        private static void SchemaBasics()
        {
            Rejects("{'required':['a']}", "{}", "");
            Accepts("{'required':['a']}", "{'a':1}");
            const string closed = "{'properties':{'a':{}},'additionalProperties':false}";
            Rejects(closed, "{'a':1,'zz':2}", null);
            Accepts(closed, "{'a':1}");
            const string cst = "{'properties':{'v':{'const':3}}}";
            Rejects(cst, "{'v':4}", "/v");
            Accepts(cst, "{'v':3}");
            const string range = "{'properties':{'n':{'minimum':1,'maximum':5}}}";
            Rejects(range, "{'n':9}", "/n");
            Rejects(range, "{'n':0}", "/n");
            Accepts(range, "{'n':5}");
        }

        private static void SchemaMore()
        {
            const string one = "{'properties':{'v':{'oneOf':[{'type':'string'},{'type':'integer'}]}}}";
            Rejects(one, "{'v':true}", "/v");
            Accepts(one, "{'v':'s'}");
            Accepts(one, "{'v':1}");
            const string uniq = "{'properties':{'l':{'type':'array','uniqueItems':true}}}";
            Rejects(uniq, "{'l':[1,2,1]}", "/l");
            Accepts(uniq, "{'l':[1,2,3]}");
            const string integer = "{'properties':{'n':{'type':'integer'}}}";
            Accepts(integer, "{'n':7.0}");   // JSON Schema 2020-12: 7.0 is an integer
            Rejects(integer, "{'n':7.5}", "/n");
            Expect(Validator.Validate(DataNode.False, Obj("{}"), "f.json").Count > 0, "false schema must reject");
            Expect(Validator.Validate(DataNode.True, Obj("{}"), "f.json").Count == 0, "true schema must accept");
        }

        private static void SchemaRefs()
        {
            Rejects("{'$ref':'https://example.com/x.json'}", "{}", null);
            Rejects("{'properties':{'a':{'$dynamicRef':'other.json#x'}}}", "{}", null);
            Rejects("{'$schema':'http://json-schema.org/draft-07/schema#'}", "{}", null);
            Accepts("{'$schema':'https://json-schema.org/draft/2020-12/schema','type':'object'}", "{}");
            const string local = "{'$defs':{'p':{'type':'integer'}},'properties':{'n':{'$ref':'#/$defs/p'}}}";
            Accepts(local, "{'n':1}");
            Rejects(local, "{'n':'s'}", "/n");
            Accepts("{'properties':{'$ref':{'type':'string'}}}", "{'$ref':'x'}");
            var bad = Validator.Validate(Obj("{'type':5}"), Obj("{}"), "m.json");
            Expect(bad.Count > 0 && bad[0].Code == "SCHEMA_ERROR", "malformed schema must be reported");
        }

        private static void Pointers()
        {
            const string schema = "{'properties':{'items':{'items':{'required':['id'],'properties':{'qty':{'type':'integer'}}}},'m':{'additionalProperties':{'type':'integer'}}}}";
            var data = Obj("{'items':[{'id':'a','qty':2},{'id':'b','qty':'x'},{}],'m':{'a/b':'s'}}");
            var d = Validator.Validate(Obj(schema), data, "root.json");
            Expect(d.All(x => x.Location.File == "root.json"), "file on every diagnostic");
            var qty = d.FirstOrDefault(x => x.Location.Pointer == "/items/1/qty");
            Expect(qty != null && qty.Actual == Q("'x'"), "nested type error and actual");
            Expect(d.Any(x => x.Location.Pointer == "/items/2"), "required error on array element");
            var esc = d.FirstOrDefault(x => x.Location.Pointer == "/m/a~1b");
            Expect(esc != null && esc.Actual == Q("'s'"), "escaped pointer resolves actual: " + string.Join(" | ", d.Select(x => x.Location.Pointer + "=" + x.Actual)));
            Expect(d.All(x => x.Location.Pointer != "/items/0"), "valid element not reported");
        }
    }
}
