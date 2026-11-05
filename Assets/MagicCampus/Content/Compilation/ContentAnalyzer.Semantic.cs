using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Parsing;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Compilation
{
  // Catalog-wide semantic rules: scope, ordering, economy, completion, counts and event constraints.
  internal sealed partial class ContentAnalyzer
  {
    static readonly HashSet<string> RefKeys = new HashSet<string>(StringComparer.Ordinal) {
      "spellId", "recipeId", "itemId", "outputItemId", "failureItemId", "seedItemId", "harvestItemId", "npcId", "previousId", "regionId", "loanItemId", "reactionId", "assetKey", "homeDestinationId",
      "worldChangeId", "combatOutcome", "deviceOutcome", "requiredSpellId", "dailyYieldItemId", "roomId", "encounterId", "successOutcomeId", "finalEncounterId", "unlockProjectId", "capabilityId" };
    static readonly HashSet<string> RefListKeys = new HashSet<string>(StringComparer.Ordinal) {
      "spellIds", "prerequisiteClassIds", "loanItemIds", "recipeIds", "allowedFacilityIds", "endingIds", "nearbyActionIds" };

    sealed class Ing { public string Item; public double? Qty; }

    IReadOnlyList<CallNode> Calls(string text)
    {
      if (text == null) return new CallNode[0];
      IReadOnlyList<CallNode> l;
      if (parsed.TryGetValue(text, out l)) return l;
      var o = parser.Parse(CallKind.Script, text, string.Empty, string.Empty, string.Empty);
      l = o.Succeeded && o.Value != null ? o.Value.Calls : new CallNode[0];
      parsed[text] = l;
      return l;
    }

    void Has(string value, string pointer)
    {
      if (value != null && !index.ContainsKey(value)) Bad(ResultCodes.RefNotFound, "catalog", pointer, "", "unknown id " + value);
    }

    void Typed(string value, string kind, string pointer)
    {
      if (value == null) return;
      IndexEntry d;
      if (!index.TryGetValue(value, out d)) Bad(ResultCodes.RefNotFound, "catalog", pointer, "", "unknown id " + value);
      else if (d.Kind != kind) Bad(ResultCodes.RefKind, "catalog", pointer, "", value + " expected " + kind + " got " + d.Kind);
    }

    void Walk(DataNode v, string p)
    {
      if (v == null) return;
      if (v.Kind == DataKind.List) { for (int i = 0; i < v.Items.Count; i++) Walk(v.Items[i], p + "/" + i); return; }
      if (v.Kind != DataKind.Object) return;
      foreach (var kv in v.Members)
      {
        string q = p + "/" + Dn.Esc(kv.Key);
        if (RefKeys.Contains(kv.Key) && kv.Value.Kind == DataKind.String) Has(kv.Value.Text, q);
        if (RefListKeys.Contains(kv.Key)) foreach (var t in kv.Value.Strs()) Has(t, q);
        Walk(kv.Value, q);
      }
    }

    Dictionary<string, DataNode> ById(string table)
    {
      var m = new Dictionary<string, DataNode>(StringComparer.Ordinal);
      foreach (var x in c.L(table)) { var id = x.S("id"); if (id != null) m[id] = x; }
      return m;
    }

    List<Ing> Qty(string text)
    {
      var l = new List<Ing>();
      foreach (var call in Calls(text)) { var p = Pos(call); l.Add(new Ing { Item = call.Function, Qty = p.Count > 0 ? Shapes.NumOf(p[0]) : null }); }
      return l;
    }

    static double Buy(Dictionary<string, DataNode> items, string id, double dflt)
    {
      DataNode it; double? b;
      return id != null && items.TryGetValue(id, out it) && (b = it.Num("buyPrice")).HasValue ? b.Value : dflt;
    }

    void CheckSemantics()
    {
      Walk(c, "");
      foreach (var x in c.L("scenarios")) foreach (var y in x.L("steps")) Has(y.Str(), "/scenarios/" + x.S("id"));
      SemStory(); SemEconomy(); SemHelp(); SemClasses(); SemEvents(); SemCounts();
    }

    void SemStory()
    {
      var projects = c.L("projects");
      for (int i = 0; i < projects.Count; i++)
      {
        var x = projects[i]; string p = "/projects/" + i, id = x.S("id");
        var w = Calls(x.S("when"));
        if (w.Count != 1 || w[0].Function != "any" || w[0].Negated || Pos(w[0]).Count != 2) Bad(ContentCodes.ProjectOr, "catalog", p + "/when", id, "project gate must be one any with two alternatives");
        foreach (var rid in x.Strs("recipeIds")) Typed(rid, "recipe", p + "/recipeIds");
        if (Calls(x.S("grantOnAccept")).Count == 0) Bad(ContentCodes.ProjectGrant, "catalog", p + "/grantOnAccept", id, "project needs a grant");
      }
      var story = c.L("story");
      for (int ai = 0; ai < story.Count; ai++)
      {
        var a = story[ai]; string ap = "/story/" + ai, aid = a.S("id"), sid = a.S("sourceId") ?? string.Empty;
        if (a.L("externalCasePrerequisites").Count > 0) Bad(ContentCodes.CrossCaseGate, "catalog", ap + "/externalCasePrerequisites", aid, "cases must not gate each other");
        if (a.S("combatOutcome") != a.S("deviceOutcome")) Bad(ContentCodes.RouteMismatch, "catalog", ap, aid, "combat and device routes must give one outcome");
        var nodes = a.L("nodes");
        for (int ni = 0; ni < nodes.Count; ni++)
        {
          var n = nodes[ni]; string num = (ni + 1).ToString(CultureInfo.InvariantCulture);
          if (n.S("sourceId") != sid + num || n.S("id") != "node." + sid.ToLowerInvariant() + num) Bad(ContentCodes.NodeOrder, "catalog", ap + "/nodes/" + ni, n.S("id"), "node order/id does not follow the case source id");
        }
      }
      var npcs = ById("npcs"); var rels = c.L("relationships");
      for (int i = 0; i < rels.Count; i++)
      {
        var a = rels[i]; string rp = "/relationships/" + i, nid = a.S("npcId");
        DataNode npc = null;
        if (nid != null) npcs.TryGetValue(nid, out npc);
        if (a.Flag("romanceVariant") == true && !(npc != null && npc.Flag("romanceAllowed") == true)) Bad(ContentCodes.RomanceScope, "catalog", rp, a.S("id"), nid + " does not allow romance");
        double? idx = a.Num("index");
        if (idx > 1)
        {
          string want = null;
          foreach (var y in rels) if (y.S("npcId") == nid && y.Num("index") == idx - 1) { want = y.S("id"); break; }
          if (a.S("previousId") != want) Bad(ContentCodes.RelationOrder, "catalog", rp + "/previousId", a.S("id"), "previousId must be the preceding relationship of the same npc");
        }
      }
      var spells = ById("spells"); DataNode sp;
      if (!spells.TryGetValue("spell.sprout", out sp)) Bad(ResultCodes.RefNotFound, "catalog", "/spells", "spell.sprout", "required spell missing");
      else if (sp.S("resultTag") == "opened_seed") Bad(ContentCodes.ComboBypass, "catalog", "/spells", "spell.sprout", "sprout must not open a seed directly");
      if (!spells.TryGetValue("spell.illusion", out sp)) Bad(ResultCodes.RefNotFound, "catalog", "/spells", "spell.illusion", "required spell missing");
      else if (sp.S("resultTag") == "projected") Bad(ContentCodes.ComboBypass, "catalog", "/spells", "spell.illusion", "illusion must not project directly");
      if (!spells.TryGetValue("spell.lantern", out sp)) Bad(ResultCodes.RefNotFound, "catalog", "/spells", "spell.lantern", "required spell missing");
      else if (!sp.Strs("acceptTargetTags").Contains("record")) Bad(ContentCodes.LanternRecordUnreachable, "catalog", "/spells", "spell.lantern", "lantern must accept record targets");
    }

    void SemEconomy()
    {
      var items = ById("items"); var recipes = c.L("recipes");
      var keys = new HashSet<string>(StringComparer.Ordinal);
      for (int i = 0; i < recipes.Count; i++)
      {
        var x = recipes[i]; string p = "/recipes/" + i, id = x.S("id"), kind = x.S("kind");
        var ing = Qty(x.S("ingredients"));
        foreach (var z in ing) if (!(z.Qty.HasValue && z.Qty.Value >= 1)) { Bad(ContentCodes.IngredientQuantity, "catalog", p + "/ingredients", id, "ingredient quantity must be >= 1"); break; }
        double cost = (x.Num("feeCrowns") ?? double.NaN);
        foreach (var z in ing) cost += Buy(items, z.Item, double.NaN) * (z.Qty ?? double.NaN);
        DataNode outItem; double price = 0;
        if (x.S("outputItemId") != null && items.TryGetValue(x.S("outputItemId"), out outItem)) price = outItem.Num("saleBasePrice") ?? 0;
        double sale = Math.Floor(price * 0.4) * (x.Num("outputQuantity") ?? double.NaN);
        if (sale > cost) Bad(ContentCodes.CraftProfit, "catalog", p, id, "selling the output beats its cost");
        foreach (var sub in Calls(x.S("substitutes")))
        {
          var sp = Pos(sub); double sq = sp.Count > 0 ? (Shapes.NumOf(sp[0]) ?? double.NaN) : double.NaN;
          var slotArg = Named(sub, "slot"); double slot = slotArg != null ? (Shapes.NumOf(slotArg) ?? 0) : 0;
          double cheap = double.PositiveInfinity; int count = 0;
          foreach (var it in c.L("items"))
          {
            if (it.S("kind") != "material" || !it.Strs("properties").Contains(sub.Function)) continue;
            count++;
            double b = it.Num("buyPrice") ?? double.NaN;
            cheap = count == 1 ? b : Math.Min(cheap, b);
          }
          if (count == 0) Bad(ContentCodes.SubstituteMissing, "catalog", p + "/substitutes", id, "no material has property " + sub.Function);
          if (slot < 0 || slot >= ing.Count) Bad(ContentCodes.SubstituteSlot, "catalog", p + "/substitutes", id, "slot " + Shapes.F(slot) + " is outside the ingredients");
          else
          {
            var bs = ing[(int)slot];
            double alt = cost - (bs.Qty ?? double.NaN) * Buy(items, bs.Item, 0) + cheap * sq;
            if (count > 0 && sale > alt) Bad(ContentCodes.SubstituteProfit, "catalog", p + "/substitutes", id, "substitution makes crafting profitable");
          }
        }
        foreach (var fid in x.Strs("allowedFacilityIds")) Typed(fid, "facility", p + "/allowedFacilityIds");
        if (kind == "alchemy")
        {
          var rp = x.Strs("requiredProperties");
          if (new HashSet<string>(rp, StringComparer.Ordinal).Count != 2) Bad(ContentCodes.AlchemyProperties, "catalog", p + "/requiredProperties", id, "alchemy needs two distinct properties");
          var sorted = new List<string>(rp); sorted.Sort(StringComparer.Ordinal);
          string key = string.Join("+", sorted) + ":" + x.S("process");
          if (!keys.Add(key)) Bad(ContentCodes.RecipeAmbiguous, "catalog", p, id, "two recipes share " + key);
          var props = new HashSet<string>(StringComparer.Ordinal);
          foreach (var z in ing) { DataNode it; if (items.TryGetValue(z.Item, out it)) props.UnionWith(it.Strs("properties")); }
          foreach (var q in rp) if (!props.Contains(q)) { Bad(ContentCodes.RecipePropertiesUnsatisfied, "catalog", p, id, "ingredients lack property " + q); break; }
        }
        if ((kind == "alchemy" || kind == "cooking") && !x.Strs("allowedFacilityIds").Contains("facility.shared_p07")) Bad(ContentCodes.FineUnreachable, "catalog", p + "/allowedFacilityIds", id, "fine quality needs the shared facility");
      }
    }

    void SemHelp()
    {
      var story = c.L("story");
      for (int ai = 0; ai < story.Count; ai++)
      {
        var nodes = story[ai].L("nodes");
        for (int ni = 0; ni < nodes.Count; ni++)
        {
          var n = nodes[ni]; string p = "/story/" + ai + "/nodes/" + ni;
          Typed(n.S("regionId"), "region", p + "/regionId");
          Typed(n.S("encounterId"), "encounter", p + "/encounterId");
          if (Calls(n.S("goals")).Count != 3 || Calls(n.S("hints")).Count != 3) Bad(ContentCodes.RoomHelp, "catalog", p, n.S("roomId"), "a main room needs 3 goals and 3 hints");
        }
      }
      var rooms = c.L("rooms");
      for (int i = 0; i < rooms.Count; i++)
        if (Calls(rooms[i].S("hints")).Count != 3) Bad(ContentCodes.RoomHelp, "catalog", "/rooms/" + i + "/hints", rooms[i].S("id"), "a room needs 3 hints");
      var enc = c.L("encounters");
      for (int i = 0; i < enc.Count; i++)
      {
        var x = enc[i]; string p = "/encounters/" + i, id = x.S("id");
        Typed(x.S("roomId"), "room", p + "/roomId");
        Typed(x.S("successOutcomeId"), "outcome", p + "/successOutcomeId");
        foreach (var g in x.Strs("deviceGoalIds")) Ref(g, "goal", "catalog", p + "/deviceGoalIds", id, id + "[deviceGoalIds]", 0);
        if (x.Num("maxAttackers") > 3) Bad(ContentCodes.AttackerLimit, "catalog", p + "/maxAttackers", id, "at most 3 attackers");
      }
      var reactions = c.L("reactions");
      for (int i = 0; i < reactions.Count; i++)
      {
        var x = reactions[i]; string p = "/reactions/" + i;
        foreach (var call in Calls(x.S("paths"))) if (Pos(call).Count == 0) Bad(ContentCodes.EmptyReactionPrerequisite, "catalog", p + "/paths", x.S("id"), "a reaction path needs prerequisite tags");
        if (x.S("windowPolicy") == "seconds-since-prerequisite" && x.Num("windowSeconds") <= 0) Bad(ContentCodes.ReactionWindow, "catalog", p + "/windowSeconds", x.S("id"), "window must be positive");
      }
    }

    void SemClasses()
    {
      var classes = c.L("classes");
      for (int i = 0; i < classes.Count; i++)
      {
        var x = classes[i]; string p = "/classes/" + i;
        if (x.Flag("introduced") != true && x.Flag("requiresIntro") != true) Bad(ContentCodes.IntroGate, "catalog", p, x.S("id"), "class needs an introduction gate");
        if (Calls(x.S("grantOnFirstAccept")).Count == 0) Bad(ContentCodes.ClassGrant, "catalog", p + "/grantOnFirstAccept", x.S("id"), "class needs a first-accept grant");
      }
      var acts = c.L("activities");
      for (int i = 0; i < acts.Count; i++)
      {
        Has(acts[i].S("loanItemId"), "/activities/" + i + "/loanItemId");
        foreach (var pa in acts[i].Strs("prerequisiteActivityIds")) Typed(pa, "activity", "/activities/" + i + "/prerequisiteActivityIds");
      }
      var cd = c.G("caseDerivation"); var fd = c.G("finalDefinition");
      if (cd.S("owner") != "CaseService" || cd.Flag("atomicWithNodeCompletion") != true) Bad(ContentCodes.CaseDerivation, "catalog", "/caseDerivation", "", "case derivation must be owned by CaseService and atomic with node completion");
      if (fd.Num("requiredCaseCount") != 6 || fd.Flag("manualStart") != true) Bad(ContentCodes.FinalGate, "catalog", "/finalDefinition", "", "final needs 6 cases and a manual start");
    }

    void SemEvents()
    {
      var evs = r.L("events");
      var kindNames = new HashSet<string>(StringComparer.Ordinal);
      foreach (var ie in index.Values) kindNames.Add(ie.Kind);
      var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
      for (int i = 0; i < evs.Count; i++)
      {
        var e = evs[i]; string p = "/events/" + i, id = e.S("id");
        var script = Calls(e.S("script")); var effects = Calls(e.S("effects"));
        string ck = e.S("completionKey");
        if (ck != null && !index.ContainsKey(ck)) Bad(ResultCodes.RefNotFound, "runner", p + "/completionKey", id, "unknown completion key " + ck);
        var res = e.Strs("resources"); var seenRes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rs in res)
        {
          if (!Shapes.IsId(rs)) Bad(ResultCodes.ContentInvalid, "runner", p + "/resources", id, "resource id format: " + rs);
          if (!seenRes.Add(rs)) Bad(ResultCodes.ContentInvalid, "runner", p + "/resources", id, "duplicate resource " + rs);
          int dot = rs.IndexOf('.'); string head = dot > 0 ? rs.Substring(0, dot) : rs;
          IndexEntry de;
          if (kindNames.Contains(head))
          {
            if (!index.TryGetValue(rs, out de)) Bad(ResultCodes.RefNotFound, "runner", p + "/resources", id, "unknown resource " + rs);
            else if (de.Kind != head) Bad(ResultCodes.RefKind, "runner", p + "/resources", id, rs + " is " + de.Kind);
          }
        }
        if (e.S("executionMode") == "room-activity" && (res.Contains("input.player") || res.Contains("camera.main") || e.S("pause") != "none"))
          Bad(ContentCodes.RoomSceneConflict, "runner", p, id, "a room activity must not lock input or camera or pause");
        if (e.S("repeatPolicy") == "practice")
        {
          bool badEff = false, hasPractice = false;
          foreach (var ef in effects) { var po = Named(ef, "participationOnly"); if (ef.Function != "experience.record" || po == null || po.Kind != ArgKind.Bool || !po.Bool) badEff = true; }
          foreach (var sc in script) if (sc.Function == "perform") { var m = Named(sc, "mode"); if (m != null && m.Text == "practice") hasPractice = true; }
          if (badEff || !hasPractice) Bad(ContentCodes.PracticeReward, "runner", p, id, "practice may only record participation");
        }
        foreach (var sc in script)
        {
          var sp = Pos(sc);
          if (sc.Function == "perform" && sp.Count > 0 && sp[0].Text == "class" && (e.Num("costMinutes") ?? 0) != 0) { Bad(ContentCodes.DoubleClassTime, "runner", p + "/costMinutes", id, "class time is counted by perform"); break; }
        }
        foreach (var ef in effects)
        {
          if (ef.Function != "case.reward") continue;
          var ep = Pos(ef); string cid = ep.Count > 0 ? ep[0].Text : null;
          DataNode a = null;
          foreach (var s in c.L("story")) if (s.S("id") == cid) a = s;
          if (a == null || a.L("nodes").Count < 3) continue;
          string nid = a.L("nodes")[2].S("id"); bool ok = false;
          foreach (var e2 in effects) if (e2.Function == "complete") { var p2 = Pos(e2); if (p2.Count > 0 && p2[0].Text == nid) ok = true; }
          if (!ok) Bad(ContentCodes.CaseRewardBoundary, "runner", p + "/effects", id, "case.reward must complete the last node in the same commit");
        }
        if (ck != null)
        {
          string eff = e.S("effects") ?? string.Empty, prev;
          if (byKey.TryGetValue(ck, out prev) && prev != eff) Bad(ContentCodes.RouteRewardMismatch, "runner", p + "/effects", id, "routes sharing " + ck + " must give the same effects");
          byKey[ck] = eff;
        }
      }
    }

    void SemCounts()
    {
      var counts = new KeyValuePair<string, int>[] {
        new KeyValuePair<string, int>("spells", 12), new KeyValuePair<string, int>("classes", 24), new KeyValuePair<string, int>("enemies", 12), new KeyValuePair<string, int>("regions", 4),
        new KeyValuePair<string, int>("rooms", 12), new KeyValuePair<string, int>("npcs", 12), new KeyValuePair<string, int>("relationships", 36), new KeyValuePair<string, int>("story", 6),
        new KeyValuePair<string, int>("projects", 12), new KeyValuePair<string, int>("plants", 12), new KeyValuePair<string, int>("recipes", 48), new KeyValuePair<string, int>("encounters", 7),
        new KeyValuePair<string, int>("endings", 3), new KeyValuePair<string, int>("truthScenes", 2) };
      foreach (var kv in counts)
        if (c.L(kv.Key).Count != kv.Value) Bad(ContentCodes.BudgetCount, "catalog", "/" + kv.Key, "", kv.Key + " must have " + kv.Value + " rows, has " + c.L(kv.Key).Count);
      int nodes = 0;
      foreach (var a in c.L("story")) nodes += a.L("nodes").Count;
      if (nodes != 18) Bad(ContentCodes.BudgetCount, "catalog", "/story", "", "main room nodes must be 18, are " + nodes);
      var want = new KeyValuePair<string, int>[] { new KeyValuePair<string, int>("festival", 4), new KeyValuePair<string, int>("club", 8), new KeyValuePair<string, int>("request", 24) };
      foreach (var kv in want)
      {
        int n = 0;
        foreach (var a in c.L("activities")) if (a.S("kind") == kv.Key) n++;
        if (n != kv.Value) Bad(ContentCodes.ActivityCount, "catalog", "/activities", "", kv.Key + " activities must be " + kv.Value + ", are " + n);
      }
    }
  }
}
