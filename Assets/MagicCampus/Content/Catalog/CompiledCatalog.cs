using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Catalog
{
  // Immutable compiled catalog. Constructed only from a bundle that passed every compile stage.
  public sealed class CompiledCatalog : ICatalog, ICatalogIndex, ICompiledContentLookup
  {
    readonly Dictionary<string, CompiledEvent> byId = new Dictionary<string, CompiledEvent>(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, DataNode>> defs = new Dictionary<string, Dictionary<string, DataNode>>(StringComparer.Ordinal);
    readonly Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.Ordinal);
    readonly DataNode catalogRoot, runnerRoot;
    readonly Dictionary<string, IReadOnlyList<CompiledCall>> callColumns = new Dictionary<string, IReadOnlyList<CompiledCall>>(StringComparer.Ordinal);

    public string ContentVersion { get; }
    public string CatalogHash { get; }
    public string RunnerHash { get; }
    public string FunctionsHash { get; }
    public IReadOnlyList<CompiledEvent> Events { get; }
    public IReadOnlyList<AssetBinding> Bindings { get; }

    public CompiledCatalog(string contentVersion, string catalogHash, string runnerHash, string functionsHash, IEnumerable<CompiledEvent> events,
      IDictionary<string, Dictionary<string, DataNode>> definitions, IDictionary<string, string> kindById, IEnumerable<AssetBinding> bindings,
      DataNode catalogRoot, DataNode runnerRoot, IEnumerable<CompiledColumn> columns = null)
    {
      if (string.IsNullOrEmpty(contentVersion)) throw new ArgumentException("contentVersion required", nameof(contentVersion));
      if (string.IsNullOrEmpty(catalogHash)) throw new ArgumentException("catalogHash required", nameof(catalogHash));
      if (events == null) throw new ArgumentNullException(nameof(events));
      if (definitions == null) throw new ArgumentNullException(nameof(definitions));
      if (kindById == null) throw new ArgumentNullException(nameof(kindById));
      this.catalogRoot = catalogRoot ?? throw new ArgumentNullException(nameof(catalogRoot));
      this.runnerRoot = runnerRoot ?? throw new ArgumentNullException(nameof(runnerRoot));
      ContentVersion = contentVersion; CatalogHash = catalogHash; RunnerHash = runnerHash ?? string.Empty; FunctionsHash = functionsHash ?? string.Empty;
      var list = new List<CompiledEvent>();
      foreach (var e in events)
      {
        if (e == null) throw new ArgumentException("null event", nameof(events));
        if (byId.ContainsKey(e.Id)) throw new ArgumentException("duplicate event " + e.Id, nameof(events));
        byId[e.Id] = e; list.Add(e);
      }
      Events = new ReadOnlyCollection<CompiledEvent>(list);
      foreach (var kv in definitions)
      {
        var m = new Dictionary<string, DataNode>(StringComparer.Ordinal);
        foreach (var x in kv.Value) m[x.Key] = x.Value;
        defs[kv.Key] = m;
      }
      foreach (var kv in kindById) kinds[kv.Key] = kv.Value;
      var bl = new List<AssetBinding>();
      if (bindings != null) bl.AddRange(bindings);
      Bindings = new ReadOnlyCollection<AssetBinding>(bl);
      if (columns != null) foreach (var col in columns)
        callColumns.Add(ColumnKey(col.Source, col.Table, col.RowId, col.Column), Array.AsReadOnly(new List<CompiledCall>(col.Calls).ToArray()));
    }

    static string ColumnKey(string source, string table, string row, string column)
        => source.Length + ":" + source + table.Length + ":" + table + row.Length + ":" + row + column;

    public IReadOnlyList<CompiledCall> FindCalls(string source, string table, string rowId, string column)
    {
      if (source == null || table == null || rowId == null || column == null) return null;
      return callColumns.TryGetValue(ColumnKey(source, table, rowId, column), out var calls) ? calls : null;
    }

    public CompiledEvent FindEvent(string id)
    {
      CompiledEvent e;
      return id != null && byId.TryGetValue(id, out e) ? e : null;
    }

    // catalog/root and runner/root are the complete immutable source trees. map, destination, variable, schedule and npc prefer the
    // runner Row; npcMetadata is the full catalog NPC Row. Every other kind returns the Row that declared the id.
    public DataNode FindDefinition(string kind, string id)
    {
      if (kind == null || id == null) return null;
      if (id == "root")
      {
        if (kind == "catalog") return catalogRoot;
        if (kind == "runner") return runnerRoot;
      }
      Dictionary<string, DataNode> m;
      DataNode n;
      return defs.TryGetValue(kind, out m) && m.TryGetValue(id, out n) ? n : null;
    }

    public string KindOf(string id)
    {
      string k;
      return id != null && kinds.TryGetValue(id, out k) ? k : null;
    }
  }
}
