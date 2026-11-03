using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MagicCampus.Content.Catalog;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Compilation
{
  // Shape classification of parsed arguments, re-derived from the raw text: quoted strings always stay text.
  internal static class Shapes
  {
    static readonly Regex IdRe = new Regex("^[a-z][a-z0-9_]*([.][a-z0-9_]+)+$", RegexOptions.CultureInvariant);
    static readonly Regex WordRe = new Regex("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);
    static readonly Regex RefRe = new Regex("^[A-Za-z0-9_.-]+:[a-z0-9_]+$", RegexOptions.CultureInvariant);
    static readonly Dictionary<string, Regex> cache = new Dictionary<string, Regex>(StringComparer.Ordinal);

    public static bool IsId(string s) { return s != null && IdRe.IsMatch(s); }
    public static bool IsWord(string s) { return s != null && WordRe.IsMatch(s); }
    public static bool IsLabelRef(string s) { return s != null && RefRe.IsMatch(s); }
    public static bool IsIntegral(double d) { return !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d && Math.Abs(d) < 9.2e18; }
    public static string F(double d) { return d.ToString("R", CultureInfo.InvariantCulture); }

    public static bool Rx(string pattern, string text)
    {
      Regex re;
      lock (cache)
      {
        if (!cache.TryGetValue(pattern, out re))
        {
          try { re = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ECMAScript); }
          catch (ArgumentException) { re = new Regex(pattern, RegexOptions.CultureInvariant); }
          cache[pattern] = re;
        }
      }
      return re.IsMatch(text ?? string.Empty);
    }

    // text | bool | int | number | id | labelref | word | raw. A finite integral Number token (7.0) is an int.
    public static string Of(CallArgument a)
    {
      switch (a.Kind)
      {
        case ArgKind.String: return "text";
        case ArgKind.Bool: return "bool";
        case ArgKind.Integer: return "int";
        case ArgKind.Number: return IsIntegral(a.Number) ? "int" : "number";
        default:
          if (IsId(a.Text)) return "id";
          if (IsLabelRef(a.Text)) return "labelref";
          if (IsWord(a.Text)) return "word";
          return "raw";
      }
    }

    public static double? NumOf(CallArgument a)
    {
      if (a == null) return null;
      if (a.Kind == ArgKind.Integer) return (double)a.Integer;
      if (a.Kind == ArgKind.Number) return a.Number;
      return null;
    }

    public static long AsLong(CallArgument a) { return a.Kind == ArgKind.Integer ? a.Integer : (long)a.Number; }
  }

  // Structural mapping of the closed catalog/runner schemas to definition kinds (what the schemas do not state themselves).
  internal static class CatalogShape
  {
    static KeyValuePair<string, string> KV(string a, string b) { return new KeyValuePair<string, string>(a, b); }
    public static readonly KeyValuePair<string, string>[] TableKind = {
      KV("spells", "spell"), KV("classes", "class"), KV("reactions", "reaction"), KV("enemies", "enemy"), KV("regions", "region"), KV("rooms", "room"), KV("npcs", "npc"),
      KV("relationships", "relationship"), KV("story", "case"), KV("activities", "activity"), KV("projects", "project"), KV("plants", "plant"), KV("items", "item"), KV("recipes", "recipe"),
      KV("shops", "shop"), KV("creatures", "creature"), KV("furniture", "furniture"), KV("objects", "object"), KV("scenarios", "scenario"), KV("dialogues", "dialogue"), KV("endings", "ending"),
      KV("onboarding", "onboarding"), KV("encounters", "encounter"), KV("truthScenes", "truth"), KV("difficultyPresets", "difficulty"), KV("facilities", "facility") };
    public static readonly KeyValuePair<string, string>[] SingleKind = { KV("finalDefinition", "final"), KV("schoolRule", "schoolRule") };
    public static readonly string[][] Defining = {
      new[] { "story", "resolutionFlag", "flag" }, new[] { "story", "worldChangeId", "projection" }, new[] { "story", "combatOutcome", "outcome" }, new[] { "classes", "practiceContextId", "context" },
      new[] { "classes", "rewardKey", "reward" }, new[] { "projects", "worldChangeId", "projection" }, new[] { "rooms", "completionKey", "completion" }, new[] { "relationships", "completionKey", "completion" },
      new[] { "regions", "mapId", "map" }, new[] { "regions", "safePointId", "safePoint" }, new[] { "objects", "applicationContextId", "context" }, new[] { "endings", "worldChangeId", "projection" },
      new[] { "endings", "rewardKey", "reward" }, new[] { "onboarding", "completionKey", "completion" }, new[] { "encounters", "successOutcomeId", "outcome" } };
    public static readonly string[][] NodeDefining = { new[] { "id", "node" }, new[] { "evidenceId", "evidence" }, new[] { "roomId", "room" } };
    public static readonly KeyValuePair<string, string>[] AssetTables = { KV("spells", "effect"), KV("enemies", "sprite"), KV("furniture", "prefab") };
    public static readonly KeyValuePair<string, string>[] RunnerKind = { KV("variables", "variable"), KV("maps", "map"), KV("destinations", "destination"), KV("npcs", "npc"), KV("schedules", "schedule"), KV("events", "event") };
  }

  internal enum NameRule { Registered, Pattern, Id, Word }

  internal sealed class ParamSpec
  {
    public string Name, Type, RefKind, Defines, Pattern, Parse, LabelIn, FromParam;
    public bool Optional, Rest, Fork, HasDefault;
    public DataNode Default;
    public double? Min, Max;
    public int RestMin;
    public List<string> Allow = new List<string>();
    public Dictionary<string, string> FromMap = new Dictionary<string, string>(StringComparer.Ordinal);
    public List<ParamSpec> Pairs;
  }

  internal sealed class SigSpec
  {
    public string Name, Writes, Owner, Capability;
    public bool Negatable, RequiresClosedHandles;
    public List<ParamSpec> Params = new List<ParamSpec>();
    public List<KeyValuePair<string, ParamSpec>> NamedList = new List<KeyValuePair<string, ParamSpec>>();
    public Dictionary<string, ParamSpec> Named = new Dictionary<string, ParamSpec>(StringComparer.Ordinal);
    public FunctionDef Def;
  }

  internal sealed class KindSpec
  {
    public string Name, Pattern, RefKind;
    public FunctionRole Role;
    public NameRule Rule;
    public double? Min, Max;
    public bool Ascending, LabelRequired;
    public List<string> Allowed = new List<string>();
    public Dictionary<string, SigSpec> Functions = new Dictionary<string, SigSpec>(StringComparer.Ordinal);
    public DataNode Raw;

    public bool NameMatches(string name)
    {
      switch (Rule)
      {
        case NameRule.Registered: return Functions.ContainsKey(name);
        case NameRule.Pattern:
          double v;
          if (!Shapes.Rx(Pattern, name) || !double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
          return !(Min.HasValue && v < Min.Value) && !(Max.HasValue && v > Max.Value);
        case NameRule.Id: return Shapes.IsId(name);
        default: return Shapes.IsWord(name) && (Allowed.Count == 0 || Allowed.Contains(name));
      }
    }
  }

  internal sealed class ColumnSpec
  {
    public string File, Table, Column, Kind, LabelOwner;
    public bool NonEmpty;
    public int MaxCalls;
  }

  internal sealed class Registry
  {
    public DataNode Source;
    public Dictionary<string, KindSpec> Kinds = new Dictionary<string, KindSpec>(StringComparer.Ordinal);
    public List<ColumnSpec> Columns = new List<ColumnSpec>();
    public Dictionary<string, List<string>> RefKinds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    public List<FunctionDef> All = new List<FunctionDef>();
  }

  internal static class RegistryBuilder
  {
    static readonly HashSet<string> BaseTypes = new HashSet<string>(StringComparer.Ordinal) { "id", "int", "number", "bool", "text", "word", "value", "labelref", "idOrLabel" };

    public static Registry Build(DataNode root, string file, List<Diagnostic> diags)
    {
      void E(string ptr, string msg) { diags.Add(Diags.Make(ResultCodes.ContentInvalid, file, ptr, "functions", "", 0, msg, null, null)); }
      if (root == null || root.Kind != DataKind.Object) { E("", "functions root must be an object"); return null; }
      var kindsNode = root.G("kinds"); var colsNode = root.G("columns");
      if (kindsNode == null || kindsNode.Kind != DataKind.Object) { E("/kinds", "kinds object required"); return null; }
      if (colsNode == null || colsNode.Kind != DataKind.List) { E("/columns", "columns list required"); return null; }
      var reg = new Registry { Source = root };
      foreach (var kv in root.G("refKinds").Pairs()) reg.RefKinds[kv.Key] = kv.Value.Strs();
      Action<string, string> err = E;
      foreach (var kv in kindsNode.Members)
      {
        var kn = kv.Value; string kp = "/kinds/" + Dn.Esc(kv.Key);
        FunctionRole role;
        if (!Enum.TryParse(kv.Key, true, out role)) { E(kp, "kind has no function role"); continue; }
        var ks = new KindSpec { Name = kv.Key, Role = role, Raw = kn, LabelRequired = kn.Flag("labelRequired") == true };
        var nr = kn.G("nameRule");
        if (nr == null) { E(kp + "/nameRule", "nameRule required"); continue; }
        if (nr.Kind == DataKind.String)
        {
          if (nr.Text != "registered") { E(kp + "/nameRule", "unknown nameRule " + nr.Text); continue; }
          ks.Rule = NameRule.Registered;
        }
        else if (nr.Kind == DataKind.Object)
        {
          string pat = nr.S("pattern"), ty = nr.S("type");
          if (pat != null) { ks.Rule = NameRule.Pattern; ks.Pattern = pat; ks.Min = nr.Num("min"); ks.Max = nr.Num("max"); ks.Ascending = nr.Flag("ascending") == true; }
          else if (ty == "id") { ks.Rule = NameRule.Id; ks.RefKind = nr.S("refKind"); }
          else if (ty == "word") { ks.Rule = NameRule.Word; ks.Allowed = nr.Strs("enum"); }
          else { E(kp + "/nameRule", "unsupported nameRule"); continue; }
        }
        else { E(kp + "/nameRule", "nameRule must be a string or object"); continue; }
        var fns = kn.G("functions");
        if (fns == null || fns.Kind != DataKind.Object) { E(kp + "/functions", "functions object required"); continue; }
        foreach (var fv in fns.Members)
        {
          var sig = BuildSig(fv.Key, fv.Value, role, kp + "/functions/" + Dn.Esc(fv.Key), err);
          if (sig == null) continue;
          ks.Functions[fv.Key] = sig; reg.All.Add(sig.Def);
        }
        if (ks.Rule != NameRule.Registered && !ks.Functions.ContainsKey("*")) E(kp + "/functions/*", "wildcard signature required");
        reg.Kinds[kv.Key] = ks;
      }
      foreach (FunctionRole role in Enum.GetValues(typeof(FunctionRole)))
        if (!reg.Kinds.Values.Any(k => k.Role == role)) E("/kinds", "no kind registered for role " + role);
      for (int i = 0; i < colsNode.Items.Count; i++)
      {
        var cn = colsNode.Items[i]; string cp = "/columns/" + i;
        var spec = new ColumnSpec { File = cn.S("file"), Table = cn.S("table"), Column = cn.S("column"), Kind = cn.S("kind"), LabelOwner = cn.S("labelOwner"), NonEmpty = cn.Flag("nonEmpty") == true, MaxCalls = (int)(cn.Num("maxCalls") ?? 0) };
        if (spec.File == null || spec.Table == null || spec.Column == null || spec.Kind == null) { E(cp, "column needs file, table, column and kind"); continue; }
        if (spec.File != "catalog" && spec.File != "runner") { E(cp + "/file", "column file must be catalog or runner"); continue; }
        if (!reg.Kinds.ContainsKey(spec.Kind)) { E(cp + "/kind", "column kind " + spec.Kind + " is not registered"); continue; }
        reg.Columns.Add(spec);
      }
      return reg;
    }

    static SigSpec BuildSig(string name, DataNode n, FunctionRole role, string ptr, Action<string, string> err)
    {
      if (n == null || n.Kind != DataKind.Object) { err(ptr, "signature must be an object"); return null; }
      var sig = new SigSpec { Name = name, Negatable = n.Flag("negatable") == true, RequiresClosedHandles = n.Flag("requiresClosedHandles") == true, Writes = n.S("writes"), Owner = n.S("owner"), Capability = n.S("capability") };
      if (string.IsNullOrWhiteSpace(sig.Owner)) { err(ptr + "/owner", "owner label is required"); return null; }
      bool ok = true;
      var pl = n.L("params");
      for (int i = 0; i < pl.Count; i++)
      {
        var p = BuildParam("p" + i, pl[i], ptr + "/params/" + i, err);
        if (p == null) { ok = false; continue; }
        if (p.Rest && i != pl.Count - 1) { err(ptr + "/params/" + i, "only the last parameter may be rest"); ok = false; }
        sig.Params.Add(p);
      }
      foreach (var kv in n.G("named").Pairs())
      {
        var p = BuildParam(kv.Key, kv.Value, ptr + "/named/" + Dn.Esc(kv.Key), err);
        if (p == null) { ok = false; continue; }
        p.Name = kv.Key;
        sig.Named[kv.Key] = p; sig.NamedList.Add(new KeyValuePair<string, ParamSpec>(kv.Key, p));
      }
      if (!ok) return null;
      var defs = new List<ParamDef>();
      foreach (var p in sig.Params) defs.Add(ToDef(p, false, name));
      foreach (var kv in sig.NamedList) defs.Add(ToDef(kv.Value, true, name));
      sig.Def = new FunctionDef(name, role, sig.Owner, defs, n);
      return sig;
    }

    static ParamSpec BuildParam(string defaultName, DataNode n, string ptr, Action<string, string> err)
    {
      if (n == null || n.Kind != DataKind.Object) { err(ptr, "parameter must be an object"); return null; }
      var p = new ParamSpec { Name = n.S("name") ?? defaultName, Type = n.S("type") };
      if (p.Type == null || !(BaseTypes.Contains(p.Type) || p.Type.StartsWith("enum:", StringComparison.Ordinal))) { err(ptr + "/type", "unsupported parameter type " + p.Type); return null; }
      p.Optional = n.Flag("optional") == true; p.Rest = n.Flag("rest") == true; p.Fork = n.Flag("fork") == true;
      var d = n.G("default");
      if (d != null) { p.HasDefault = true; p.Default = d; }
      var mn = n.Num("min");
      if (p.Rest) { if (mn.HasValue) p.RestMin = (int)mn.Value; } else p.Min = mn;
      p.Max = n.Num("max");
      p.RefKind = n.S("refKind"); p.Defines = n.S("defines"); p.Pattern = n.S("pattern"); p.Parse = n.S("parse"); p.LabelIn = n.S("labelIn"); p.Allow = n.Strs("allow");
      var rf = n.G("refKindFrom");
      if (rf != null)
      {
        p.FromParam = rf.S("param");
        foreach (var kv in rf.G("map").Pairs()) if (kv.Value.Kind == DataKind.String) p.FromMap[kv.Key] = kv.Value.Text;
      }
      var pairs = n.G("pairs");
      if (pairs != null && pairs.Kind == DataKind.List)
      {
        p.Pairs = new List<ParamSpec>();
        for (int j = 0; j < pairs.Items.Count; j++)
        {
          var q = BuildParam("", pairs.Items[j], ptr + "/pairs/" + j, err);
          if (q == null) return null;
          p.Pairs.Add(q);
        }
        if (p.Pairs.Count != 2) { err(ptr + "/pairs", "pairs must contain exactly two entries"); return null; }
      }
      return p;
    }

    static ParamDef ToDef(ParamSpec p, bool named, string fn)
    {
      ParamType t;
      bool isEnum = p.Type.StartsWith("enum:", StringComparison.Ordinal);
      if (p.Parse == "condition") t = ParamType.ConditionList;
      else if (p.RefKind != null || p.FromParam != null || p.Type == "id" || p.Type == "labelref" || p.Type == "idOrLabel") t = ParamType.Ref;
      else if (isEnum) t = ParamType.Enum;
      else switch (p.Type)
        {
          case "bool": t = ParamType.Bool; break;
          case "int": t = ParamType.Int; break;
          case "number": t = ParamType.Number; break;
          case "text": t = ParamType.Text; break;
          case "value": t = ParamType.Any; break;
          default: t = named && p.Name == "async" ? ParamType.HandleOpen : (p.Rest && fn == "await" ? ParamType.HandleUse : ParamType.Word); break;
        }
      var ev = isEnum ? p.Type.Substring(5).Split('|') : new string[0];
      return new ParamDef(p.Name, t, !named && !p.Optional, p.Rest, p.RefKind ?? string.Empty, ev, p.Default, p.Min);
    }
  }

  public sealed class FunctionDictionary : IFunctionDictionary
  {
    internal Registry Registry { get; }
    public DataNode Source { get; }
    public IReadOnlyList<FunctionDef> All { get; }

    internal FunctionDictionary(DataNode source, Registry registry)
    {
      Source = source; Registry = registry;
      All = registry.All.ToArray();
    }

    public FunctionDef Find(FunctionRole role, string name)
    {
      if (name == null) return null;
      foreach (var ks in Registry.Kinds.Values)
      {
        if (ks.Role != role) continue;
        SigSpec s;
        if (ks.Rule == NameRule.Registered) return ks.Functions.TryGetValue(name, out s) ? s.Def : null;
        return ks.NameMatches(name) && ks.Functions.TryGetValue("*", out s) ? s.Def : null;
      }
      return null;
    }
  }

  public sealed class FunctionDictionaryLoader : IFunctionDictionaryLoader
  {
    public Outcome<IFunctionDictionary> Load(DataNode functionsJson)
    {
      var diags = new List<Diagnostic>();
      var reg = RegistryBuilder.Build(functionsJson, "functions", diags);
      if (reg == null || diags.Count > 0)
      {
        if (diags.Count == 0) diags.Add(Diags.Make(ResultCodes.ContentInvalid, "functions", "", "functions", "", 0, "function dictionary could not be built", null, null));
        return Outcome<IFunctionDictionary>.Fail(ResultCodes.ContentInvalid, 0, diags.ToArray());
      }
      return Outcome<IFunctionDictionary>.Ok(new FunctionDictionary(functionsJson, reg), 0);
    }
  }
}
