using System;
using System.IO;
using System.Text.Json.Nodes;
using Json.Schema;
using UnityEngine;

namespace MagicCampus.Editor
{
    public static class SchemaLibraryProbe
    {
        [Serializable] class ProbeResult { public string unity; public string schemaAssembly; public string jsonAssembly; public bool acceptsValid; public bool rejectsWrongType; public bool rejectsExtraField; }
        public static void Run()
        {
            var schema = JsonSchema.FromText("{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"required\":[\"value\"],\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"integer\"}}}");
            var result = new ProbeResult {
                unity = UnityEngine.Application.unityVersion,
                schemaAssembly = typeof(JsonSchema).Assembly.FullName,
                jsonAssembly = typeof(JsonNode).Assembly.FullName,
                acceptsValid = schema.Evaluate(JsonNode.Parse("{\"value\":1}")).IsValid,
                rejectsWrongType = !schema.Evaluate(JsonNode.Parse("{\"value\":\"wrong\"}")).IsValid,
                rejectsExtraField = !schema.Evaluate(JsonNode.Parse("{\"value\":1,\"typo\":true}")).IsValid
            };
            File.WriteAllText("design/implementation-tasks/execution/2026-11-02-stage3/schema-package-probe.json", JsonUtility.ToJson(result, true));
            if (!result.acceptsValid || !result.rejectsWrongType || !result.rejectsExtraField) throw new InvalidOperationException("Standard schema validator probe failed");
            Debug.Log("Standard schema validator actual Unity probe passed");
        }
    }
}
