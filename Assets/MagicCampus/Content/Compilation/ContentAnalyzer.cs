using System;
using System.Collections.Generic;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Parsing;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Compilation
{
  internal sealed class IndexEntry { public string Kind, File, Pointer; public DataNode Raw; }
  internal sealed class LabelInfo { public string Kind; }

  internal sealed class Column
  {
    public string File, Pointer, RowPointer, EntityId, ColumnName, Kind, Text, Loc, Owner, ForkName;
    public ColumnSpec Spec;
    public DataNode Row;
    public bool IsEvent, Failed;
    public IReadOnlyList<CallNode> Calls = new CallNode[0];
    public List<CompiledCall> Compiled = new List<CompiledCall>();
    public HashSet<string> Used = new HashSet<string>(StringComparer.Ordinal);
  }

  internal sealed class CheckContext
  {
    public Column Col;
    public string Loc;
    public int Offset;
    public HashSet<string> Forks;
    public HashSet<string> Used;
  }

  // Runs the compile stages after schema and dictionary loading: index, columns, labels, calls, forks, assets, semantics.
  // It only reads the immutable source trees and accumulates diagnostics; whether the bundle is accepted is decided by the caller.
  internal sealed partial class ContentAnalyzer
  {
    readonly CallParser parser;
    readonly FunctionDictionary reg;
    readonly DataNode c, r;
    readonly Dictionary<string, string> files;
    readonly List<Diagnostic> diags = new List<Diagnostic>();
    readonly List<Column> columns = new List<Column>();
    readonly Dictionary<string, IndexEntry> index = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, DataNode>> rows = new Dictionary<string, Dictionary<string, DataNode>>(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, LabelInfo>> labels = new Dictionary<string, Dictionary<string, LabelInfo>>(StringComparer.Ordinal);
    readonly Dictionary<string, string> varTypes = new Dictionary<string, string>(StringComparer.Ordinal);
    readonly Dictionary<string, IReadOnlyList<CallNode>> parsed = new Dictionary<string, IReadOnlyList<CallNode>>(StringComparer.Ordinal);
    public readonly List<AssetBinding> Bindings = new List<AssetBinding>();

    public ContentAnalyzer(CallParser parser, FunctionDictionary reg, DataNode catalog, DataNode runner, Dictionary<string, string> files)
    {
      this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
      this.reg = reg ?? throw new ArgumentNullException(nameof(reg));
      c = catalog ?? throw new ArgumentNullException(nameof(catalog));
      r = runner ?? throw new ArgumentNullException(nameof(runner));
      this.files = files ?? new Dictionary<string, string>();
    }

    public List<Diagnostic> Diagnostics { get { return diags; } }
    public List<Column> Columns { get { return columns; } }
    public Dictionary<string, Dictionary<string, DataNode>> Rows { get { return rows; } }

    public Dictionary<string, string> KindById()
    {
      var m = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (var kv in index) m[kv.Key] = kv.Value.Kind;
      return m;
    }

    public void Run()
    {
      Stage("index", BuildIndex);
      Stage("columns", GatherColumns);
      Stage("labels", DeclareLabels);
      Stage("calls", CheckColumns);
      Stage("forks", CheckForks);
      Stage("assets", CheckAssets);
      Stage("semantic", CheckSemantics);
    }

    void Stage(string name, Action a)
    {
      try { a(); }
      catch (Exception e)
      {
        Bad(ResultCodes.ContentInvalid, "catalog", "", "", "analysis stage '" + name + "' aborted: " + e.GetType().Name + " " + e.Message);
      }
    }

    string FileOf(string key)
    {
      string f;
      return files.TryGetValue(key, out f) ? f : key;
    }

    void Dg(string code, string file, string pointer, string entity, string call, int offset, string message, string expected, string actual)
    {
      diags.Add(Diags.Make(code, FileOf(file), pointer, entity, call, offset, message, expected, actual));
    }

    void D(string code, Column col, string call, string message, string expected, string actual, int offset)
    {
      Dg(code, col.File, col.Pointer, col.EntityId, call, offset, message, expected, actual);
    }

    void Bad(string code, string file, string pointer, string entity, string message)
    {
      Dg(code, file, pointer ?? string.Empty, entity ?? string.Empty, string.Empty, 0, message, null, null);
    }

    // ------------------------------------------------------------------ definition index

    void SetRow(string kind, string id, DataNode raw)
    {
      Dictionary<string, DataNode> m;
      if (!rows.TryGetValue(kind, out m)) { m = new Dictionary<string, DataNode>(StringComparer.Ordinal); rows[kind] = m; }
      m[id] = raw;
    }

    // The same id declared again with the same kind is one definition (catalog NPC metadata plus runner binding);
    // a runner declaration then becomes the preferred read row. Another kind is a collision.
    void Define(string id, string kind, string file, string pointer, DataNode raw)
    {
      if (id == null) return;
      IndexEntry old;
      if (index.TryGetValue(id, out old))
      {
        if (old.Kind != kind) Bad(ContentCodes.DuplicateId, file, pointer, id, id + " " + old.Kind + "@" + old.Pointer + " vs " + kind + "@" + pointer);
        else if (file == "runner") SetRow(kind, id, raw);
        return;
      }
      index[id] = new IndexEntry { Kind = kind, File = file, Pointer = pointer, Raw = raw };
      SetRow(kind, id, raw);
    }

    void BuildIndex()
    {
      foreach (var kv in CatalogShape.TableKind)
      {
        var list = c.L(kv.Key);
        for (int i = 0; i < list.Count; i++)
        {
          var x = list[i]; string id = x.S("id");
          if (id == null) continue;
          string ptr = "/" + kv.Key + "/" + i;
          if (index.ContainsKey(id)) Bad(ContentCodes.DuplicateId, "catalog", ptr, id, "duplicate id " + id);
          else
          {
            index[id] = new IndexEntry { Kind = kv.Value, File = "catalog", Pointer = ptr, Raw = x };
            SetRow(kv.Value, id, x);
            if (kv.Key == "npcs") SetRow("npcMetadata", id, x);
          }
        }
      }
      foreach (var kv in CatalogShape.SingleKind)
      {
        var t = c.G(kv.Key);
        if (t != null) Define(t.S("id"), kv.Value, "catalog", "/" + kv.Key, t);
      }
      var story = c.L("story");
      for (int ai = 0; ai < story.Count; ai++)
      {
        var nodes = story[ai].L("nodes");
        for (int ni = 0; ni < nodes.Count; ni++)
          foreach (var nd in CatalogShape.NodeDefining)
            Define(nodes[ni].S(nd[0]), nd[1], "catalog", "/story/" + ai + "/nodes/" + ni + "/" + nd[0], nodes[ni]);
      }
      foreach (var d in CatalogShape.Defining)
      {
        var list = c.L(d[0]);
        for (int i = 0; i < list.Count; i++) Define(list[i].S(d[1]), d[2], "catalog", "/" + d[0] + "/" + i + "/" + d[1], list[i]);
      }
      var fd = c.G("finalDefinition");
      if (fd != null) Define(fd.S("roomId"), "room", "catalog", "/finalDefinition/roomId", fd);
      foreach (var kv in CatalogShape.AssetTables)
      {
        var list = c.L(kv.Key);
        for (int i = 0; i < list.Count; i++) Define(list[i].S("assetKey"), "asset", "catalog", "/" + kv.Key + "/" + i + "/assetKey", list[i]);
      }
      foreach (var kv in CatalogShape.RunnerKind)
      {
        var list = r.L(kv.Key);
        for (int i = 0; i < list.Count; i++) Define(list[i].S("id"), kv.Value, "runner", "/" + kv.Key + "/" + i, list[i]);
      }
      var maps = r.L("maps");
      for (int i = 0; i < maps.Count; i++)
      {
        var ps = maps[i].L("portals");
        for (int j = 0; j < ps.Count; j++) Define(ps[j].S("id"), "portal", "runner", "/maps/" + i + "/portals/" + j, ps[j]);
      }
      foreach (var v in r.L("variables"))
      {
        string vid = v.S("id"), vt = v.S("type");
        if (vid != null && vt != null) varTypes[vid] = vt;
      }
      KindSpec goal;
      if (reg.Kinds.TryGetValue("goal", out goal))
        foreach (var f in goal.Functions)
          if (f.Value.Capability != null) Define(f.Value.Capability, "capability", "functions", "/kinds/goal/functions/" + Dn.Esc(f.Key), goal.Raw);
    }

    // ------------------------------------------------------------------ call columns

    static CallKind CallKindOf(string kind, string column)
    {
      switch (kind)
      {
        case "trigger": return CallKind.Trigger;
        case "condition": return CallKind.When;
        case "command": return column.StartsWith("forks/", StringComparison.Ordinal) ? CallKind.Fork : CallKind.Script;
        case "effect": return CallKind.Effects;
        case "scheduleEntry": return CallKind.ScheduleEntry;
        case "goal": return CallKind.Goal;
        case "hint": return CallKind.Hint;
        case "behavior": return CallKind.Behavior;
        case "weakness": return CallKind.Weakness;
        case "reactionPath": return CallKind.ReactionPath;
        case "encounterEnemy": return CallKind.EncounterEnemy;
        case "itemQuantity": return CallKind.ItemQuantity;
        case "itemGrant": return CallKind.ItemGrant;
        case "substitute": return CallKind.Substitute;
        case "finalStage": return CallKind.FinalStage;
        default: return CallKind.Script;
      }
    }

    // Parser diagnostics keep their code and offset; location is re-anchored to the row entity.
    void Rewrap(IReadOnlyList<Diagnostic> details, string file, string pointer, string entity, string callPrefix, int baseOffset)
    {
      foreach (var d in details)
      {
        string call = d.Location != null ? d.Location.Call : string.Empty;
        int h = call.LastIndexOf('#');
        string idx = h >= 0 ? call.Substring(h + 1) : "1";
        diags.Add(Diags.Make(d.Code, FileOf(file), pointer, entity, callPrefix + idx, baseOffset + (d.Location != null ? d.Location.Offset : 0), d.Message, d.Expected, d.Actual));
      }
    }

    void AddColumn(ColumnSpec spec, string pointer, string rowPointer, string rowId, string colName, DataNode cell, DataNode row, string loc, string owner, string forkName)
    {
      var col = new Column { File = spec.File, Pointer = pointer, RowPointer = rowPointer, EntityId = rowId ?? string.Empty, ColumnName = colName, Kind = spec.Kind, Spec = spec, Row = row, Loc = loc, Owner = owner, ForkName = forkName, IsEvent = spec.File == "runner" && spec.Table == "events" };
      columns.Add(col);
      if (cell.Kind != DataKind.String)
      {
        D(ResultCodes.ParseError, col, loc + "#0", "call column must be a string", "string", cell.Kind.ToString(), 0);
        col.Failed = true;
        return;
      }
      col.Text = cell.Text;
      var o = parser.Parse(CallKindOf(spec.Kind, colName), col.Text, FileOf(spec.File), pointer, loc);
      if (!o.Succeeded || o.Value == null)
      {
        Rewrap(o.Result.Details, spec.File, pointer, col.EntityId, loc + "#", 0);
        col.Failed = true;
        return;
      }
      col.Calls = o.Value.Calls;
      if (spec.NonEmpty && col.Calls.Count == 0) D(ResultCodes.ArgCount, col, loc + "#0", colName + " needs at least one call", ">=1", "0", 0);
      if (spec.MaxCalls > 0 && col.Calls.Count > spec.MaxCalls)
        D(ResultCodes.ArgCount, col, loc + "#" + (spec.MaxCalls + 1), colName + " allows " + spec.MaxCalls + " call", spec.MaxCalls.ToString(), col.Calls.Count.ToString(), col.Calls[spec.MaxCalls].NameSpan.Offset);
    }

    void GatherColumns()
    {
      foreach (var spec in reg.Columns)
      {
        var data = spec.File == "runner" ? r : c;
        var list = new List<KeyValuePair<DataNode, string>>();
        if (spec.Table == "story/*/nodes")
        {
          var st = c.L("story");
          for (int ai = 0; ai < st.Count; ai++)
          {
            var nodes = st[ai].L("nodes");
            for (int ni = 0; ni < nodes.Count; ni++) list.Add(new KeyValuePair<DataNode, string>(nodes[ni], "/story/" + ai + "/nodes/" + ni));
          }
        }
        else
        {
          var t = data.G(spec.Table);
          if (t != null && t.Kind == DataKind.List) for (int i = 0; i < t.Items.Count; i++) list.Add(new KeyValuePair<DataNode, string>(t.Items[i], "/" + spec.Table + "/" + i));
          else if (t != null && t.Kind == DataKind.Object) list.Add(new KeyValuePair<DataNode, string>(t, "/" + spec.Table));
        }
        foreach (var item in list)
        {
          var row = item.Key; string bp = item.Value;
          string rowId = row.S("id");
          string owner = spec.LabelOwner != null ? row.S(spec.LabelOwner) : rowId;
          if (spec.Column == "forks/*")
          {
            foreach (var kv in row.G("forks").Pairs())
              AddColumn(spec, bp + "/forks/" + Dn.Esc(kv.Key), bp, rowId, "forks/" + kv.Key, kv.Value, row, rowId + ":" + kv.Key, owner, kv.Key);
            continue;
          }
          var cell = row.G(spec.Column);
          if (cell == null) continue;
          string loc = (spec.Table == "events" && spec.Column == "script") || spec.Table == "schedules" ? rowId : rowId + "[" + spec.Column + "]";
          AddColumn(spec, bp + "/" + spec.Column, bp, rowId, spec.Column, cell, row, loc, owner, null);
        }
      }
    }

    static List<CallArgument> Pos(CallNode call)
    {
      var l = new List<CallArgument>();
      foreach (var a in call.Arguments) if (a.Name.Length == 0) l.Add(a);
      return l;
    }

    static CallArgument Named(CallNode call, string key)
    {
      foreach (var a in call.Arguments) if (a.Name == key) return a;
      return null;
    }

    // Labels are unique per owner; defining parameters declare ids (goal objects and capabilities).
    void DeclareLabels()
    {
      foreach (var col in columns)
      {
        KindSpec ks;
        if (!reg.Kinds.TryGetValue(col.Kind, out ks)) continue;
        foreach (var call in col.Calls)
        {
          string loc = col.Loc + "#" + call.Index; int off = call.NameSpan.Offset;
          if (ks.LabelRequired && call.Label.Length == 0) D(ResultCodes.ParseError, col, loc, col.Kind + " call needs a label 'name:'", "label:", "none", off);
          if (call.Label.Length > 0)
          {
            string ow = col.Owner ?? string.Empty;
            Dictionary<string, LabelInfo> m;
            if (!labels.TryGetValue(ow, out m)) { m = new Dictionary<string, LabelInfo>(StringComparer.Ordinal); labels[ow] = m; }
            if (m.ContainsKey(call.Label)) D(ResultCodes.LabelDup, col, loc, "label '" + call.Label + "' already used in " + ow, "unique label", call.Label, off);
            else m[call.Label] = new LabelInfo { Kind = col.Kind };
          }
          SigSpec sig;
          if (ks.Functions.TryGetValue(call.Function, out sig))
          {
            var pos = Pos(call);
            for (int i = 0; i < sig.Params.Count && i < pos.Count; i++)
              if (sig.Params[i].Defines != null && Shapes.Of(pos[i]) == "id") Define(pos[i].Text, sig.Params[i].Defines, col.File, col.Pointer, col.Row);
          }
        }
      }
    }

    void CheckColumns()
    {
      foreach (var col in columns)
      {
        if (col.Failed) continue;
        var ctx = new CheckContext { Col = col, Loc = col.Loc, Offset = 0 };
        if (col.IsEvent)
        {
          ctx.Forks = new HashSet<string>(StringComparer.Ordinal);
          foreach (var kv in col.Row.G("forks").Pairs()) ctx.Forks.Add(kv.Key);
          ctx.Used = col.Used;
        }
        col.Compiled = CheckList(col.Calls, col.Kind, ctx);
      }
    }

    // Forks: every fork must be reachable from the main columns of its event, and forks must not reach themselves.
    void CheckForks()
    {
      var evs = r.L("events");
      for (int i = 0; i < evs.Count; i++)
      {
        var e = evs[i]; string id = e.S("id") ?? string.Empty; string rp = "/events/" + i;
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var col in columns)
        {
          if (!col.IsEvent || col.RowPointer != rp) continue;
          if (col.ForkName == null) roots.UnionWith(col.Used); else edges[col.ForkName] = col.Used;
        }
        var names = new List<string>();
        foreach (var kv in e.G("forks").Pairs()) names.Add(kv.Key);
        var reach = new HashSet<string>(roots, StringComparer.Ordinal);
        var queue = new Queue<string>(roots);
        while (queue.Count > 0)
        {
          var cur = queue.Dequeue();
          HashSet<string> nexts;
          if (edges.TryGetValue(cur, out nexts)) foreach (var nx in nexts) if (reach.Add(nx)) queue.Enqueue(nx);
        }
        foreach (var k in names)
          if (!reach.Contains(k)) Dg(ResultCodes.ForkUnused, "runner", rp + "/forks/" + Dn.Esc(k), id, id + ":" + k + "#0", 0, "fork '" + k + "' is never called", "choice/branch reference", k);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        void Dfs(string fk)
        {
          state[fk] = 1;
          HashSet<string> targets;
          if (edges.TryGetValue(fk, out targets))
            foreach (var tg in targets)
            {
              if (!names.Contains(tg)) continue;
              int st;
              if (!state.TryGetValue(tg, out st)) Dfs(tg);
              else if (st == 1) Dg(ResultCodes.ForkCycle, "runner", rp + "/forks/" + Dn.Esc(tg), id, id + ":" + tg + "#0", 0, "fork '" + tg + "' reaches itself", "acyclic forks", fk + "->" + tg);
            }
          state[fk] = 2;
        }
        foreach (var k in names) if (!state.ContainsKey(k)) Dfs(k);
      }
    }

    void CheckAssets()
    {
      var addr = c.G("assetAddresses");
      foreach (var kv in CatalogShape.AssetTables)
        foreach (var x in c.L(kv.Key))
        {
          string key = x.S("assetKey");
          if (string.IsNullOrEmpty(key)) continue;
          bool dup = false;
          foreach (var b in Bindings) if (b.Id == key) dup = true;
          if (dup) continue;
          string a = addr.S(key);
          Bindings.Add(new AssetBinding(key, kv.Value, a ?? "PLACEHOLDER/" + key, true, a == null));
        }
      foreach (var kv in addr.Pairs())
      {
        bool found = false;
        foreach (var b in Bindings) if (b.Id == kv.Key) found = true;
        if (!found) Bad(ResultCodes.RefNotFound, "catalog", "/assetAddresses/" + Dn.Esc(kv.Key), "", "assetAddresses " + kv.Key + " has no assetKey");
      }
    }
  }
}
