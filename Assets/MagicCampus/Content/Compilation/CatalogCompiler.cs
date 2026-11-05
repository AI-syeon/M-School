using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Parsing;
using MagicCampus.Contracts;
using MagicCampus.Transactions;

namespace MagicCampus.Content.Compilation
{
  // Order: schema -> function dictionary -> parsed cells -> signatures/types/defaults -> typed refs/index -> labels/forks/async -> semantics.
  // Any diagnostic rejects the whole bundle. CatalogHash is exactly catalog.SourceHash (lowercase hex SHA-256 of the original bytes).
  public sealed class CatalogCompiler
  {
    static readonly Regex HashRe = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    readonly CallParser parser;
    readonly ISchemaValidationPort schema;

    public CatalogCompiler(CallParser parser, ISchemaValidationPort schema)
    {
      this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
      this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    static Outcome<ICatalog> Failed(IEnumerable<Diagnostic> d) { return Outcome<ICatalog>.Fail(ResultCodes.ContentInvalid, 0, new List<Diagnostic>(d).ToArray()); }
    static Diagnostic Plain(string file, string pointer, string entity, string msg) { return Diags.Make(ResultCodes.ContentInvalid, file, pointer, entity, "", 0, msg, null, null); }

    void Validate(ContentBundle schemaBundle, ContentBundle instance, List<Diagnostic> diags)
    {
      try { var r = schema.Validate(schemaBundle.Root, instance.Root, instance.File); if (r != null) diags.AddRange(r); }
      catch (Exception ex) { diags.Add(Plain(instance.File, "", "", "schema validation failed: " + ex.GetType().Name + " " + ex.Message)); }
    }

    public Outcome<ICatalog> Compile(ContentBundle catalog, ContentBundle runner, ContentBundle functions, ContentBundle catalogSchema, ContentBundle runnerSchema)
    {
      string missing = catalog == null ? "catalog" : runner == null ? "runner" : functions == null ? "functions" : catalogSchema == null ? "catalogSchema" : runnerSchema == null ? "runnerSchema" : null;
      if (missing != null) return Failed(new[] { Plain("", "", "", "bundle " + missing + " is required") });
      if (string.IsNullOrEmpty(catalog.SourceHash) || !HashRe.IsMatch(catalog.SourceHash))
        return Failed(new[] { Plain(catalog.File, "", "", "catalog.SourceHash must be the lowercase SHA-256 of the exact original catalog bytes") });
      var diags = new List<Diagnostic>();
      Validate(catalogSchema, catalog, diags);
      Validate(runnerSchema, runner, diags);
      var dl = FunctionDictionary.Load(functions.Root);
      if (!dl.Succeeded) { diags.AddRange(dl.Result.Details); return Failed(diags); }
      var reg = dl.Value;
      var files = new Dictionary<string, string> { { "catalog", catalog.File }, { "runner", runner.File }, { "functions", functions.File } };
      var an = new ContentAnalyzer(parser, reg, catalog.Root, runner.Root, files);
      an.Run();
      diags.AddRange(an.Diagnostics);
      var version = catalog.Root.S("contentVersion");
      if (string.IsNullOrEmpty(version)) diags.Add(Plain(catalog.File, "/contentVersion", "", "contentVersion is required"));
      var events = new List<CompiledEvent>();
      BuildEvents(runner.Root, an, runner.File, diags, events);
      if (diags.Count > 0) return Failed(diags);
      try
      {
        var columns = new List<CompiledColumn>();
        foreach (var cl in an.Columns) columns.Add(new CompiledColumn(cl.File, cl.Spec.Table, cl.EntityId, cl.ColumnName, cl.Compiled));
        var cat = new CompiledCatalog(version, catalog.SourceHash, runner.SourceHash, functions.SourceHash, events, an.Rows, an.KindById(), an.Bindings, catalog.Root, runner.Root, columns);
        return Outcome<ICatalog>.Ok(cat, 0);
      }
      catch (ArgumentException ex) { return Failed(new[] { Plain(catalog.File, "", "", "catalog cannot be assembled: " + ex.Message) }); }
    }

    static List<CompiledCall> Of(List<Column> cols, string name)
    {
      foreach (var cl in cols) if (cl.ColumnName == name) return cl.Compiled;
      return new List<CompiledCall>();
    }

    static void BuildEvents(DataNode runner, ContentAnalyzer an, string file, List<Diagnostic> diags, List<CompiledEvent> output)
    {
      var rows = runner.L("events");
      for (int i = 0; i < rows.Count; i++)
      {
        var row = rows[i]; string ptr = "/events/" + i, id = row.S("id") ?? string.Empty;
        try
        {
          var cols = new List<Column>();
          foreach (var cl in an.Columns) if (cl.IsEvent && cl.RowPointer == ptr) cols.Add(cl);
          ExecutionMode mode = ExecutionMode.Scene; RepeatPolicy repeat = RepeatPolicy.Once; bool ok = true;
          string ms = row.S("executionMode"), rs = row.S("repeatPolicy");
          if (ms == "room-activity") mode = ExecutionMode.RoomActivity; else if (ms != null && ms != "scene") { diags.Add(Plain(file, ptr + "/executionMode", id, "unknown executionMode " + ms)); ok = false; }
          if (rs == "practice") repeat = RepeatPolicy.Practice; else if (rs != null && rs != "once") { diags.Add(Plain(file, ptr + "/repeatPolicy", id, "unknown repeatPolicy " + rs)); ok = false; }
          double? cm = row.Num("costMinutes"); int cost = 0;
          if (cm.HasValue)
          {
            if (cm.Value < 0 || Math.Floor(cm.Value) != cm.Value || cm.Value > int.MaxValue) { diags.Add(Plain(file, ptr + "/costMinutes", id, "costMinutes must be a non-negative integer")); ok = false; }
            else cost = (int)cm.Value;
          }
          CompletionKey? completion = null;
          string ck = row.S("completionKey");
          if (ck != null) completion = new CompletionKey(ck);
          var trig = Of(cols, "trigger");
          if (trig.Count != 1) { diags.Add(Plain(file, ptr + "/trigger", id, "event needs exactly one trigger")); ok = false; }
          if (!ok) continue;
          var forks = new Dictionary<string, IReadOnlyList<CompiledCall>>(StringComparer.Ordinal);
          foreach (var cl in cols) if (cl.ForkName != null) forks[cl.ForkName] = cl.Compiled;
          output.Add(new CompiledEvent(id, mode, repeat, row.Strs("resources"), trig[0], Of(cols, "when"), cost, Of(cols, "script"), forks, Of(cols, "effects"), completion, row));
        }
        catch (ArgumentException ex) { diags.Add(Plain(file, ptr, id, "event cannot be compiled: " + ex.Message)); }
      }
    }
  }
}
