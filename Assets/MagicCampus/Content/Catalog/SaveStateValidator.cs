using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Catalog
{
  // Pure read-only static validation of an existing full save v2 tree: strict schema, references and invariants.
  // Not persistence and not migration: older versions are rejected here and must be migrated before they reach the store.
  public sealed class SaveStateValidator : IStateInvariant
  {
    public const int SupportedSaveVersion = 2;
    readonly ICatalog catalog;
    readonly DataNode saveSchema;
    readonly ISchemaValidationPort schema;
    readonly DataNode catRoot, runRoot;
    readonly Dictionary<string, DataNode> items = new Dictionary<string, DataNode>(StringComparer.Ordinal);
    readonly Dictionary<string, DataNode> npcs = new Dictionary<string, DataNode>(StringComparer.Ordinal);

    public SaveStateValidator(ICatalog catalog, DataNode saveSchema, ISchemaValidationPort schema)
    {
      this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
      this.saveSchema = saveSchema ?? throw new ArgumentNullException(nameof(saveSchema));
      this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
      catRoot = catalog.FindDefinition("catalog", "root");
      runRoot = catalog.FindDefinition("runner", "root");
      if (catRoot == null || runRoot == null) throw new ArgumentException("catalog must expose catalog/root and runner/root", nameof(catalog));
      foreach (var x in catRoot.L("items")) { var id = x.S("id"); if (id != null) items[id] = x; }
      foreach (var x in catRoot.L("npcs")) { var id = x.S("id"); if (id != null) npcs[id] = x; }
    }

    string KindOf(string id)
    {
      if (id == null) return null;
      var ix = catalog as ICatalogIndex;
      if (ix != null) return ix.KindOf(id);
      foreach (var k in new[] { "item", "plant", "npc", "encounter", "recipe", "activity", "class", "case" })
        if (catalog.FindDefinition(k, id) != null) return k;
      return null;
    }

    public IReadOnlyList<Diagnostic> Validate(StateSnapshot candidate)
    {
      if (candidate == null) throw new ArgumentNullException(nameof(candidate));
      var d = new List<Diagnostic>();
      var root = candidate.Root;
      d.AddRange(schema.Validate(saveSchema, root, "save"));

      void Bad(string code, string ptr, string msg) { d.Add(Diags.Make(code, "save", ptr, "", "", 0, msg, null, null)); }
      void Has(string id, string ptr) { if (id != null && KindOf(id) == null) Bad(ResultCodes.RefNotFound, ptr, "unknown id " + id); }
      void Typed(string id, string kind, string ptr)
      {
        if (id == null) return;
        var k = KindOf(id);
        if (k == null) Bad(ResultCodes.RefNotFound, ptr, "unknown id " + id);
        else if (k != kind) Bad(ResultCodes.RefKind, ptr, id + " expected " + kind + " got " + k);
      }

      var sv = root.G("saveVersion");
      if (sv != null && sv.Num() != SupportedSaveVersion) Bad(ResultCodes.SaveIncompatible, "/saveVersion", "only save version " + SupportedSaveVersion + " is supported");
      var hash = root.S("catalogHash");
      if (hash != null && hash != catalog.CatalogHash) Bad(ResultCodes.SaveIncompatible, "/catalogHash", "save was made for another catalog");
      var cv = root.S("contentVersion");
      if (cv != null && cv != catalog.ContentVersion) Bad(ResultCodes.SaveIncompatible, "/contentVersion", "save was made for another content version");
      var wallet = root.Num("wallet");
      if (wallet.HasValue && wallet.Value < 0) Bad(ContentCodes.NegativeWallet, "/wallet", "wallet must not be negative");

      var seen = new HashSet<string>(StringComparer.Ordinal);
      foreach (var key in root.Strs("rewardLedger"))
        if (!seen.Add(key)) Bad(ContentCodes.LedgerDuplicate, "/rewardLedger", "duplicate ledger key " + key);

      var stacks = root.L("stacks");
      for (int i = 0; i < stacks.Count; i++)
      {
        var x = stacks[i];
        string itemId = x.S("itemId"), ptr = "/stacks/" + i;
        Has(itemId, ptr + "/itemId");
        DataNode it = null;
        if (itemId != null) items.TryGetValue(itemId, out it);
        var q = x.Num("quantity");
        var mx = it.Num("stackMax");
        if (q > mx) Bad(ContentCodes.StackLimit, ptr + "/quantity", itemId + " exceeds stackMax");
        string prov = x.S("provenance"), scope = x.S("scopeId");
        bool hasScope = !string.IsNullOrEmpty(scope);
        if (prov == "loan" && !hasScope) Bad(ContentCodes.LoanScope, ptr, "loan stack needs scopeId");
        if (prov != "loan" && hasScope) Bad(ContentCodes.NonLoanScope, ptr, "only loan stacks carry scopeId");
      }
      var slots = catRoot.G("rules").Num("inventorySlots");
      if (stacks.Count > slots) Bad(ContentCodes.BagCapacity, "/stacks", "more stacks than inventory slots");

      var plots = root.L("plots");
      for (int i = 0; i < plots.Count; i++) Has(plots[i].S("plantId"), "/plots/" + i + "/plantId");

      var relations = root.L("relations");
      for (int i = 0; i < relations.Count; i++)
      {
        var x = relations[i];
        string npcId = x.S("npcId");
        Has(npcId, "/relations/" + i + "/npcId");
        DataNode npc = null;
        if (npcId != null) npcs.TryGetValue(npcId, out npc);
        if (x.S("expression") == "romance" && !(npc != null && npc.Flag("romanceAllowed") == true)) Bad(ContentCodes.SaveRomanceScope, "/relations/" + i, npcId + " does not allow romance");
      }

      var defeats = root.L("defeats");
      for (int i = 0; i < defeats.Count; i++) Typed(defeats[i].S("encounterId"), "encounter", "/defeats/" + i + "/encounterId");
      var recipes = root.L("discoveredRecipeIds");
      for (int i = 0; i < recipes.Count; i++) Typed(recipes[i].Str(), "recipe", "/discoveredRecipeIds/" + i);
      var acts = root.L("acceptedActivityIds");
      for (int i = 0; i < acts.Count; i++) Typed(acts[i].Str(), "activity", "/acceptedActivityIds/" + i);

      var completed = new HashSet<string>(root.Strs("completed"), StringComparer.Ordinal);
      var records = root.L("classRecords");
      for (int i = 0; i < records.Count; i++)
      {
        var x = records[i];
        string classId = x.S("classId");
        if (x.Flag("completed") != (classId != null && completed.Contains(classId))) Bad(ContentCodes.SaveCompletionMismatch, "/classRecords/" + i, "classRecords.completed disagrees with completed for " + classId);
      }

      var flagIds = new HashSet<string>(StringComparer.Ordinal);
      foreach (var f in root.L("flags")) { var fid = f.S("id"); if (fid != null) flagIds.Add(fid); }
      foreach (var v in runRoot.L("variables"))
      {
        var vid = v.S("id");
        if (vid != null && !vid.StartsWith("clock.", StringComparison.Ordinal) && !flagIds.Contains(vid)) Bad(ContentCodes.SaveScalarMissing, "/flags", "scalar variable " + vid + " is not saved");
      }
      return d;
    }
  }
}
