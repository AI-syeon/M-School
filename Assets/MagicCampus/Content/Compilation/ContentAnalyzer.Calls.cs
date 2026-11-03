using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Compilation
{
  // Per-call signature, type, default, reference, fork and async-handle checks; builds typed CompiledCall values.
  internal sealed partial class ContentAnalyzer
  {
    sealed class ArgError { public string Code, Message, Expected, Actual; public int Offset; }

    static bool TypeOk(CallArgument a, string type)
    {
      string sh = Shapes.Of(a);
      switch (type)
      {
        case "value": return sh == "bool" || sh == "int" || sh == "number";
        case "number": return sh == "int" || sh == "number";
        case "idOrLabel": return sh == "id" || sh == "labelref";
        case "word": return sh == "word" || sh == "id";
      }
      if (type.StartsWith("enum:", StringComparison.Ordinal))
      {
        if (a.Kind == ArgKind.String) return false;
        foreach (var v in type.Substring(5).Split('|')) if (v == a.Text) return true;
        return false;
      }
      return sh == type;
    }

    List<string> KindsOf(string k)
    {
      List<string> l;
      return reg.RefKinds.TryGetValue(k, out l) ? l : new List<string> { k };
    }

    string Ref(string value, string refKind, Column col, string call, int offset)
    {
      return Ref(value, refKind, col.File, col.Pointer, col.EntityId, call, offset);
    }

    string Ref(string value, string refKind, string file, string pointer, string entity, string call, int offset)
    {
      var kinds = KindsOf(refKind);
      if (value.IndexOf(':') >= 0 && !Shapes.IsId(value))
      {
        var parts = value.Split(':');
        string owner = parts[0], label = parts.Length > 1 ? parts[1] : string.Empty;
        if (!index.ContainsKey(owner)) { Dg(ResultCodes.RefNotFound, file, pointer, entity, call, offset, "label owner " + owner + " not found", "defined id", owner); return null; }
        Dictionary<string, LabelInfo> m; LabelInfo l = null;
        if (!labels.TryGetValue(owner, out m) || !m.TryGetValue(label, out l)) { Dg(ResultCodes.RefNotFound, file, pointer, entity, call, offset, "label " + value + " not found", "label in " + owner, value); return null; }
        if (!kinds.Contains(l.Kind)) Dg(ResultCodes.RefKind, file, pointer, entity, call, offset, value + " is a " + l.Kind + " label", string.Join("|", kinds), l.Kind);
        return l.Kind;
      }
      IndexEntry def;
      if (!index.TryGetValue(value, out def)) { Dg(ResultCodes.RefNotFound, file, pointer, entity, call, offset, "unknown id " + value, string.Join("|", kinds), value); return null; }
      if (!kinds.Contains(def.Kind)) Dg(ResultCodes.RefKind, file, pointer, entity, call, offset, value + " is " + def.Kind, string.Join("|", kinds), def.Kind);
      return def.Kind;
    }

    List<ArgError> CheckCall(CallNode call, List<CallArgument> pos, List<ParamSpec> pars, SigSpec sig)
    {
      var errs = new List<ArgError>();
      void At(string code, int offset, string msg, string exp, string act) { errs.Add(new ArgError { Code = code, Offset = offset, Message = msg, Expected = exp, Actual = act }); }
      ParamSpec rest = pars.Count > 0 && pars[pars.Count - 1].Rest ? pars[pars.Count - 1] : null;
      int nFixed = rest != null ? pars.Count - 1 : pars.Count, required = 0;
      for (int i = 0; i < nFixed; i++) if (!pars[i].Optional) required++;
      if (pos.Count < required || (rest == null && pos.Count > nFixed))
        At(ResultCodes.ArgCount, call.NameSpan.Offset, call.Function + ": wrong argument count", rest != null ? ">=" + required : required + "-" + nFixed, pos.Count.ToString());
      for (int i = 0; i < pos.Count; i++)
      {
        var p = i < nFixed ? pars[i] : rest;
        if (p == null) continue;
        if (!TypeOk(pos[i], p.Type)) At(ResultCodes.ArgType, pos[i].Span.Offset, call.Function + "." + p.Name + " type", p.Type, Shapes.Of(pos[i]));
      }
      foreach (var a in call.Arguments)
      {
        if (a.Name.Length == 0) continue;
        ParamSpec np;
        if (!sig.Named.TryGetValue(a.Name, out np)) { At(ResultCodes.ArgCount, a.Span.Offset, call.Function + ": unknown named argument '" + a.Name + "'", string.Join("|", sig.Named.Keys), a.Name); continue; }
        if (!TypeOk(a, np.Type)) At(ResultCodes.ArgType, a.Span.Offset, call.Function + "." + a.Name + " type", np.Type, Shapes.Of(a));
      }
      if (call.Negated && !sig.Negatable) At(ResultCodes.ParseError, call.NameSpan.Offset, "'!' is only allowed on conditions", "no !", "!");
      return errs;
    }

    static int IndexOfHandle(List<KeyValuePair<string, string>> open, string name)
    {
      for (int i = 0; i < open.Count; i++) if (open[i].Key == name) return i;
      return -1;
    }

    static string Keys(List<KeyValuePair<string, string>> open)
    {
      var k = new List<string>();
      foreach (var o in open) k.Add(o.Key);
      return string.Join(" ", k);
    }

    List<CompiledCall> CheckList(IReadOnlyList<CallNode> calls, string kind, CheckContext ctx)
    {
      var result = new List<CompiledCall>();
      var col = ctx.Col;
      KindSpec ks;
      if (!reg.Kinds.TryGetValue(kind, out ks)) { Bad(ResultCodes.ContentInvalid, col.File, col.Pointer, col.EntityId, "unknown call kind " + kind); return result; }
      var open = new List<KeyValuePair<string, string>>();
      var usedHandles = new HashSet<string>(StringComparer.Ordinal);
      double lastMinute = -1;
      Func<int, int> off = o => ctx.Offset + o;
      foreach (var call in calls)
      {
        int before = diags.Count;
        string loc = ctx.Loc + "#" + call.Index;
        int callOff = call.NameSpan.Offset;
        SigSpec sig = null;
        if (ks.Rule == NameRule.Registered)
        {
          if (!ks.Functions.TryGetValue(call.Function, out sig))
          {
            D(ResultCodes.UnknownFunction, col, loc, "'" + call.Function + "' is not a registered " + kind + " function", string.Join("|", ks.Functions.Keys), call.Function, off(callOff));
            continue;
          }
        }
        else
        {
          if (!ks.Functions.TryGetValue("*", out sig)) continue;
          if (ks.Rule == NameRule.Pattern)
          {
            if (!Shapes.Rx(ks.Pattern, call.Function)) { D(ResultCodes.UnknownFunction, col, loc, kind + " name must match " + ks.Pattern, ks.Pattern, call.Function, off(callOff)); continue; }
            double v;
            if (double.TryParse(call.Function, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
              if ((ks.Min.HasValue && v < ks.Min.Value) || (ks.Max.HasValue && v > ks.Max.Value))
                D(ResultCodes.ArgType, col, loc, "minute out of range", (ks.Min.HasValue ? Shapes.F(ks.Min.Value) : "") + "-" + (ks.Max.HasValue ? Shapes.F(ks.Max.Value) : ""), Shapes.F(v), off(callOff));
              if (ks.Ascending && v <= lastMinute) D(ResultCodes.ArgType, col, loc, "entries must ascend", ">" + Shapes.F(lastMinute), Shapes.F(v), off(callOff));
              lastMinute = v;
            }
          }
          else if (ks.Rule == NameRule.Id)
          {
            if (!Shapes.IsId(call.Function)) { D(ResultCodes.UnknownFunction, col, loc, kind + " name must be a " + ks.RefKind + " id", "id", call.Function, off(callOff)); continue; }
            Ref(call.Function, ks.RefKind, col, loc, off(callOff));
          }
          else
          {
            if (!Shapes.IsWord(call.Function)) { D(ResultCodes.UnknownFunction, col, loc, kind + " name must be a word", "word", call.Function, off(callOff)); continue; }
            if (ks.Allowed.Count > 0 && !ks.Allowed.Contains(call.Function)) D(ResultCodes.ArgType, col, loc, kind + " name not allowed", string.Join("|", ks.Allowed), call.Function, off(callOff));
          }
        }
        var pos = Pos(call);
        var pars = sig.Params;
        ParamSpec lastP = pars.Count > 0 ? pars[pars.Count - 1] : null;
        if (lastP != null && lastP.Pairs != null)
        {
          int nf = pars.Count - 1, extra = pos.Count - nf, minPairs = lastP.RestMin > 0 ? lastP.RestMin : 1;
          if (extra < 2 * minPairs || extra % 2 != 0)
          {
            D(ResultCodes.ArgCount, col, loc, call.Function + " needs " + lastP.Pairs[0].Name + "+" + lastP.Pairs[1].Name + " pairs", ">= " + minPairs + " pairs", extra.ToString(), off(callOff));
            continue;
          }
          var exp = new List<ParamSpec>();
          for (int i = 0; i < nf; i++) exp.Add(pars[i]);
          for (int i = 0; i < extra; i++) exp.Add(lastP.Pairs[i % 2]);
          pars = exp;
        }
        var errs = CheckCall(call, pos, pars, sig);
        bool countErr = false;
        foreach (var e in errs)
        {
          D(e.Code, col, loc, e.Message, e.Expected, e.Actual, off(e.Offset));
          if (e.Code == ResultCodes.ArgCount) countErr = true;
        }
        if (countErr) continue;
        int fixedCount = lastP != null && lastP.Rest && lastP.Pairs == null ? pars.Count - 1 : pars.Count;
        var alts = new List<List<CompiledCall>>();
        for (int i = 0; i < pos.Count; i++)
        {
          var arg = pos[i];
          var p = i < fixedCount ? pars[i] : lastP;
          if (p == null) continue;
          string sh = Shapes.Of(arg);
          int ao = off(arg.Span.Offset);
          if (p.Pattern != null && !Shapes.Rx(p.Pattern, arg.Text)) D(ResultCodes.ArgType, col, loc, call.Function + "." + p.Name + " format", p.Pattern, arg.Text, ao);
          var nv = Shapes.NumOf(arg);
          if (nv.HasValue && ((p.Min.HasValue && nv.Value < p.Min.Value) || (p.Max.HasValue && nv.Value > p.Max.Value)))
            D(ResultCodes.ArgType, col, loc, call.Function + "." + p.Name + " range", (p.Min.HasValue ? Shapes.F(p.Min.Value) : "") + ".." + (p.Max.HasValue ? Shapes.F(p.Max.Value) : ""), Shapes.F(nv.Value), ao);
          if (p.RefKind != null && !p.Allow.Contains(arg.Text) && (sh == "id" || sh == "labelref" || p.Type == "word")) Ref(arg.Text, p.RefKind, col, loc, ao);
          if (p.FromParam != null)
          {
            string dv = null;
            for (int j = 0; j < pars.Count && j < pos.Count; j++) if (pars[j].Name == p.FromParam) dv = pos[j].Text;
            string rk;
            if (dv != null && p.FromMap.TryGetValue(dv, out rk)) Ref(arg.Text, rk, col, loc, ao);
          }
          if (p.Fork && ctx.Forks != null)
          {
            if (!ctx.Forks.Contains(arg.Text))
              D(ResultCodes.ForkNotFound, col, loc, "fork '" + arg.Text + "' is not in this row", ctx.Forks.Count == 0 ? "no forks" : string.Join("|", ctx.Forks), arg.Text, ao);
            else ctx.Used.Add(arg.Text);
          }
          if (p.Parse != null && arg.Kind == ArgKind.String)
          {
            var inner = parser.Parse(CallKindOf(p.Parse, string.Empty), arg.Text, FileOf(col.File), col.Pointer, loc + ">");
            if (!inner.Succeeded || inner.Value == null) Rewrap(inner.Result.Details, col.File, col.Pointer, col.EntityId, loc + ">", off(arg.Span.Offset + 1));
            else alts.Add(CheckList(inner.Value.Calls, p.Parse, new CheckContext { Col = col, Loc = loc + ">", Offset = off(arg.Span.Offset + 1), Forks = ctx.Forks, Used = ctx.Used }));
          }
        }
        if (lastP != null && lastP.Rest && lastP.Pairs == null && lastP.RestMin > 0 && pos.Count - fixedCount < lastP.RestMin)
          D(ResultCodes.ArgCount, col, loc, call.Function + " needs " + lastP.RestMin + "+ " + lastP.Name, ">=" + lastP.RestMin, (pos.Count - fixedCount).ToString(), off(callOff));
        foreach (var a in call.Arguments)
        {
          if (a.Name.Length == 0) continue;
          ParamSpec np;
          if (!sig.Named.TryGetValue(a.Name, out np)) continue;
          var nv = Shapes.NumOf(a);
          if (nv.HasValue && np.Min.HasValue && nv.Value < np.Min.Value) D(ResultCodes.ArgType, col, loc, call.Function + "." + a.Name + " range", ">=" + Shapes.F(np.Min.Value), Shapes.F(nv.Value), off(a.Span.Offset));
          if (np.LabelIn != null)
          {
            Dictionary<string, LabelInfo> m;
            if (!labels.TryGetValue(col.Owner ?? string.Empty, out m) || !m.ContainsKey(a.Text))
              D(ResultCodes.RefNotFound, col, loc, a.Name + "=" + a.Text + " is not a label of " + col.Owner, "label in " + np.LabelIn, a.Text, off(a.Span.Offset));
          }
        }
        if ((call.Function == "eq" || call.Function == "gte" || call.Function == "set") && pos.Count > 1 && Shapes.Of(pos[0]) == "id")
        {
          string vt;
          if (varTypes.TryGetValue(pos[0].Text, out vt))
          {
            string vs = Shapes.Of(pos[1]);
            if ((vt == "bool") != (vs == "bool") || (vt == "int" && vs != "int")) D(ResultCodes.ArgType, col, loc, pos[0].Text + " is " + vt, vt, vs, off(pos[1].Span.Offset));
          }
        }
        if (kind == "command")
        {
          if (sig.RequiresClosedHandles && open.Count > 0) D(ResultCodes.HandleUnawaited, col, loc, "await " + Keys(open) + " before " + call.Function, "await", Keys(open), off(callOff));
          if (call.Function == "await")
            foreach (var h in pos)
            {
              int oi = IndexOfHandle(open, h.Text);
              if (oi >= 0) open.RemoveAt(oi);
              else D(usedHandles.Contains(h.Text) ? ResultCodes.HandleConflict : ResultCodes.HandleUndefined, col, loc, "handle '" + h.Text + "' is not open here", "open handle", h.Text, off(h.Span.Offset));
            }
          string resource = sig.Writes == "actor" ? (pos.Count > 0 ? pos[0].Text : null) : sig.Writes;
          if (resource != null)
            foreach (var oh in open)
              if (oh.Value == resource) D(ResultCodes.HandleConflict, col, loc, resource + " is already driven by async " + oh.Key, "one writer", resource, off(callOff));
          var ah = Named(call, "async");
          if (ah != null)
          {
            if (!usedHandles.Add(ah.Text))
            {
              D(ResultCodes.HandleDuplicate, col, loc, "handle '" + ah.Text + "' reused", "new handle", ah.Text, off(ah.Span.Offset));
              D(ResultCodes.HandleConflict, col, loc, "handle '" + ah.Text + "' reused", "new handle", ah.Text, off(ah.Span.Offset));
            }
            int ex = IndexOfHandle(open, ah.Text);
            if (ex >= 0) open.RemoveAt(ex);
            open.Add(new KeyValuePair<string, string>(ah.Text, resource));
          }
        }
        if (diags.Count == before) result.Add(BuildCall(call, sig, pos, alts, ctx, loc, off(callOff)));
      }
      if (kind == "command" && open.Count > 0)
        D(ResultCodes.HandleUnawaited, col, ctx.Loc + "#end", "list ends with open handles", "await", Keys(open), ctx.Offset + (col.Text == null ? 0 : col.Text.Length));
      return result;
    }

    static DataNode ToNode(CallArgument a, ParamSpec p)
    {
      string sh = Shapes.Of(a);
      switch (p.Type)
      {
        case "bool": return DataNode.FromBool(a.Bool);
        case "int": return DataNode.FromInteger(Shapes.AsLong(a));
        case "number": return DataNode.FromNumber(Shapes.NumOf(a).Value);
        case "value":
          if (sh == "bool") return DataNode.FromBool(a.Bool);
          if (sh == "int") return DataNode.FromInteger(Shapes.AsLong(a));
          return DataNode.FromNumber(a.Number);
        default: return DataNode.FromString(a.Text);
      }
    }

    static DataNode ToDefault(ParamSpec p)
    {
      var d = p.Default;
      if (p.Type == "number" && d.Kind == DataKind.Integer) return DataNode.FromNumber((double)d.Integer);
      if (p.Type == "int" && d.Kind == DataKind.Number && Shapes.IsIntegral(d.Number)) return DataNode.FromInteger((long)d.Number);
      return d;
    }

    CompiledCall BuildCall(CallNode call, SigSpec sig, List<CallArgument> pos, List<List<CompiledCall>> alts, CheckContext ctx, string loc, int offset)
    {
      var args = new Dictionary<string, DataNode>(StringComparer.Ordinal);
      var ps = sig.Params;
      for (int i = 0; i < ps.Count; i++)
      {
        var p = ps[i];
        if (p.Rest)
        {
          var items = new List<DataNode>();
          if (p.Pairs != null)
          {
            for (int j = i; j + 1 < pos.Count; j += 2)
              items.Add(DataNode.FromObject(new[] {
                new KeyValuePair<string, DataNode>(p.Pairs[0].Name, DataNode.FromString(pos[j].Text)),
                new KeyValuePair<string, DataNode>(p.Pairs[1].Name, DataNode.FromString(pos[j + 1].Text)) }));
          }
          else for (int j = i; j < pos.Count; j++) items.Add(ToNode(pos[j], p));
          args[p.Name] = DataNode.FromList(items);
          break;
        }
        if (i < pos.Count) args[p.Name] = ToNode(pos[i], p);
        else if (p.HasDefault) args[p.Name] = ToDefault(p);
      }
      foreach (var kv in sig.NamedList)
      {
        var a = Named(call, kv.Key);
        if (a != null) args[kv.Key] = ToNode(a, kv.Value);
        else if (kv.Value.HasDefault) args[kv.Key] = ToDefault(kv.Value);
      }
      return new CompiledCall(sig.Def, call.Index, call.Label, call.Negated, args, alts, new SourceLocation(FileOf(ctx.Col.File), ctx.Col.Pointer, ctx.Col.EntityId, loc, offset), call.Function);
    }
  }
}
