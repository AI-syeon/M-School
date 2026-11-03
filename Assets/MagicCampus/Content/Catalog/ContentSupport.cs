using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Catalog
{
  // Semantic diagnostic codes of the static catalog/save validation that are not part of Contracts.ResultCodes.
  public static class ContentCodes
  {
    public const string DuplicateId = "DUPLICATE_ID", ProjectOr = "PROJECT_OR", CrossCaseGate = "CROSS_CASE_GATE", RouteMismatch = "ROUTE_MISMATCH", NodeOrder = "NODE_ORDER", RomanceScope = "ROMANCE_SCOPE", RelationOrder = "RELATION_ORDER";
    public const string IngredientQuantity = "INGREDIENT_QUANTITY", CraftProfit = "CRAFT_PROFIT", SubstituteMissing = "SUBSTITUTE_MISSING", SubstituteSlot = "SUBSTITUTE_SLOT", SubstituteProfit = "SUBSTITUTE_PROFIT", AlchemyProperties = "ALCHEMY_PROPERTIES";
    public const string RoomHelp = "ROOM_HELP", AttackerLimit = "ATTACKER_LIMIT", EmptyReactionPrerequisite = "EMPTY_REACTION_PREREQUISITE", ReactionWindow = "REACTION_WINDOW", ComboBypass = "COMBO_BYPASS", IntroGate = "INTRO_GATE", ClassGrant = "CLASS_GRANT", ProjectGrant = "PROJECT_GRANT";
    public const string RecipeAmbiguous = "RECIPE_AMBIGUOUS", RecipePropertiesUnsatisfied = "RECIPE_PROPERTIES_UNSATISFIED", CaseDerivation = "CASE_DERIVATION", FinalGate = "FINAL_GATE", RoomSceneConflict = "ROOM_SCENE_CONFLICT", PracticeReward = "PRACTICE_REWARD", DoubleClassTime = "DOUBLE_CLASS_TIME", CaseRewardBoundary = "CASE_REWARD_BOUNDARY", RouteRewardMismatch = "ROUTE_REWARD_MISMATCH";
    public const string FineUnreachable = "FINE_UNREACHABLE", LanternRecordUnreachable = "LANTERN_RECORD_UNREACHABLE", BudgetCount = "BUDGET_COUNT", ActivityCount = "ACTIVITY_COUNT";
    public const string StackLimit = "STACK_LIMIT", LoanScope = "LOAN_SCOPE", NonLoanScope = "NON_LOAN_SCOPE", BagCapacity = "BAG_CAPACITY", SaveRomanceScope = "SAVE_ROMANCE_SCOPE", SaveCompletionMismatch = "SAVE_COMPLETION_MISMATCH", SaveScalarMissing = "SAVE_SCALAR_MISSING", NegativeWallet = "NEGATIVE_WALLET", LedgerDuplicate = "LEDGER_DUPLICATE";
  }

  public static class Diags
  {
    public static Diagnostic Make(string code, string file, string pointer, string entity, string call, int offset, string message, string expected, string actual)
    {
      return new Diagnostic(code, message, new SourceLocation(file, pointer, entity, call, offset < 0 ? 0 : offset), expected, actual);
    }
  }

  // Null-tolerant read helpers over the immutable DataNode tree (never mutate, never throw on shape mismatch).
  public static class Dn
  {
    public static DataNode G(this DataNode n, string key)
    {
      DataNode v;
      return n != null && n.Kind == DataKind.Object && n.Members.TryGetValue(key, out v) ? v : null;
    }

    public static string Str(this DataNode n) { return n != null && n.Kind == DataKind.String ? n.Text : null; }
    public static string S(this DataNode n, string key) { return n.G(key).Str(); }

    public static IReadOnlyList<DataNode> L(this DataNode n, string key)
    {
      var v = n.G(key);
      if (v != null && v.Kind == DataKind.List) return v.Items;
      return new DataNode[0];
    }

    public static double? Num(this DataNode n)
    {
      if (n == null) return null;
      if (n.Kind == DataKind.Integer) return (double)n.Integer;
      if (n.Kind == DataKind.Number) return n.Number;
      return null;
    }

    public static double? Num(this DataNode n, string key) { return n.G(key).Num(); }

    public static bool? Flag(this DataNode n, string key)
    {
      var v = n.G(key);
      return v != null && v.Kind == DataKind.Bool ? (bool?)v.Bool : null;
    }

    public static List<string> Strs(this DataNode n)
    {
      var r = new List<string>();
      if (n != null && n.Kind == DataKind.List)
        foreach (var i in n.Items) if (i.Kind == DataKind.String) r.Add(i.Text);
      return r;
    }

    public static List<string> Strs(this DataNode n, string key) { return n.G(key).Strs(); }

    public static IEnumerable<KeyValuePair<string, DataNode>> Pairs(this DataNode n)
    {
      if (n != null && n.Kind == DataKind.Object) return n.Members;
      return new KeyValuePair<string, DataNode>[0];
    }

    public static string Esc(string key) { return key.Replace("~", "~0").Replace("/", "~1"); }
  }

  // Optional capability of a catalog: which definition kind an id belongs to (null when unknown).
  public interface ICatalogIndex
  {
    string KindOf(string id);
  }

  // Binding derived from assetKey columns. Address defaults to PLACEHOLDER/<key> until assetAddresses supplies one.
  public sealed class AssetBinding
  {
    public string Id { get; }
    public string Kind { get; }
    public string Address { get; }
    public bool Required { get; }
    public bool Placeholder { get; }

    public AssetBinding(string id, string kind, string address, bool required, bool placeholder)
    {
      if (string.IsNullOrEmpty(id)) throw new ArgumentException("id required", nameof(id));
      Id = id; Kind = kind ?? string.Empty; Address = address ?? string.Empty; Required = required; Placeholder = placeholder;
    }
  }
}
