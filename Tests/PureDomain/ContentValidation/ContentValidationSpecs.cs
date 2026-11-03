using System;
using MagicCampus.Content.Parsing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Contracts;

namespace MagicCampus.Tests.ContentValidation
{
  // Reads the original design examples and schemas from the repository and the captured 60 negative patch sets.
  // System.Text.Json is used here only to apply RFC 6902 patches; the domain code under test is stdlib-only.
  public static class ContentValidationSpecs
  {
    const string CatalogFile = "examples/full-catalog.json", RunnerFile = "examples/runner-bundle.json", SaveFile = "examples/safe-save.json";

    sealed class Env
    {
      public IDataCodec Codec; public ISchemaValidationPort Schema; public CallParser Parser = new CallParser(); public CatalogCompiler Compiler;
      public byte[] CatalogBytes, RunnerBytes, SaveBytes;
      public ContentBundle Catalog, Runner, Save, Functions, CatalogSchema, RunnerSchema, SaveSchema;
      public string CatalogHash, FixturesPath;
      public ICatalog Baseline;

      public ContentBundle Bundle(string file, byte[] bytes, bool hash)
      {
        var o = Codec.Parse(file, bytes);
        Check(o.Succeeded && o.Value != null, "codec failed to parse " + file);
        return new ContentBundle(file, o.Value.Root, hash ? Sha256Hex(bytes) : "");
      }
    }

    public static void Run(IDataCodec codec, ISchemaValidationPort schema)
    {
      if (codec == null) throw new ArgumentNullException(nameof(codec));
      if (schema == null) throw new ArgumentNullException(nameof(schema));
      var e = Load(codec, schema);
      ParserSpecs();
      DictionarySpecs(e);
      NormalSpecs(e);
      HashSpecs(e);
      ProviderSpecs(e);
      NegativeSpecs(e);
      DetailSpecs(e);
      RealTransactionSpecs(e);
    }

    static void Check(bool cond, string msg) { if (!cond) throw new InvalidOperationException("ContentValidationSpecs: " + msg); }

    sealed class FixtureGeometry : IMapGeometry
    {
      readonly ICatalog catalog;
      public FixtureGeometry(ICatalog catalog) { this.catalog = catalog; }
      public MapGeometry Find(string id)
      {
        var row = catalog.FindDefinition("map", id);
        return row == null ? null : new MapGeometry(id, (int)row.G("width").Integer, (int)row.G("height").Integer);
      }
    }

    static void RealTransactionSpecs(Env e)
    {
      var cat = e.Baseline;
      var geometry = new FixtureGeometry(cat);
      var player = e.Save.Root.G("player");
      var serin = e.Save.Root.L("npcPositions").First(n => n.S("npcId") == "npc.serin");
      Position PositionOf(DataNode n) => new Position(n.S("mapId"), n.Num("x").Value, n.Num("y").Value);
      var actors = new MagicCampus.World.Movement.ActorWorld(geometry, new[] {
        new ActorState("player", PositionOf(player), .2, 3), new ActorState("npc.serin", PositionOf(serin), .2, 3)
      });
      var pump = new MagicCampus.Runtime.CompletionPump();
      var registry = new MagicCampus.Transactions.Core.StateParticipantRegistry();
      var calendar = new MagicCampus.Timekeeping.Calendar();
      Check(registry.Register(new MagicCampus.Transactions.Effects.EventEffectsParticipant(cat)).Status == OperationStatus.Succeeded, "effects registration");
      Check(registry.Register(new MagicCampus.Timekeeping.ClockParticipant(calendar)).Status == OperationStatus.Succeeded, "clock registration");
      Check(registry.Register(new MagicCampus.World.Movement.WorldCommitParticipant(actors, geometry)).Status == OperationStatus.Succeeded, "world registration");
      registry.Seal();
      var invariant = new SaveStateValidator(cat, e.SaveSchema.Root, e.Schema);
      var store = new MagicCampus.State.StateStore(new StateSnapshot(0, e.Save.Root), registry, new[] { invariant }, pump);
      var before = store.Current;
      var clock = new MagicCampus.Timekeeping.ClockService(store, calendar);
      var eventDefinition = cat.FindEvent("event.snack");
      Check(new MagicCampus.Transactions.Conditions.ConditionEvaluator(cat).Evaluate(eventDefinition.When, before).Satisfied, "real intro condition");
      Check(actors.SetPosition("player", new Position("map.lounge", .5, 3.5), "integration").Status == OperationStatus.Succeeded, "actual player projection setup");
      Check(actors.SetPosition("npc.serin", new Position("map.lounge", 4.5, 3.5), "integration").Status == OperationStatus.Succeeded, "actual NPC projection setup");
      var planner = new MagicCampus.Transactions.Effects.EventCommitPlanner(MagicCampus.Transactions.Effects.EffectPlanner.CreateMinimal(cat), clock, new MagicCampus.World.Movement.WorldStateProjector(actors), cat);
      var context = new EffectContext(before, new TransactionId("integration.snack.1"), null, null);
      var plan = planner.PlanCommit(eventDefinition, context);
      Check(plan.Succeeded, "full original transaction planning");
      var committed = store.Commit(plan.Value);
      Check(committed.Succeeded, "full original save schema accepts combined effects/time/positions: " + Summ(committed.Result.Details));
      Check(store.Current.Root.Members.Count == before.Root.Members.Count, "all original root fields retained");
      Check(clock.Now.AbsoluteMinute == calendar.FromSnapshot(before).AbsoluteMinute + 10, "real clock event cost exactly ten");
      Check(store.Current.Slice("flags").Items.Any(n => n.S("id") == "memory.snack_done" && n.G("value").Bool), "real declared flag set");
      Check(store.Current.Slice("completed").Items.Any(n => n.Text == "event.snack") && store.Current.Slice("rewardLedger").Items.Any(n => n.Text == "event.snack"), "completion and reward ledger atomic");
      Check(store.Current.Slice("npcPositions").Items.Count == before.Slice("npcPositions").Items.Count, "inactive NPC positions retained");
      foreach(var row in before.Slice("npcPositions").Items.Where(n => n.S("npcId") != "npc.serin"))
        Check(store.Current.Slice("npcPositions").Items.Any(n => n.Equals(row)), "inactive NPC row unchanged");
      Check(store.Current.Slice("player").S("mapId") == "map.lounge" && store.Current.Slice("player").S("name") == player.S("name"), "player position and identity preserved");
      var after = store.Current;
      Check(store.Commit(plan.Value).Succeeded && ReferenceEquals(store.Current, after), "transaction replay no second state replacement");
      Check(!planner.PlanCommit(eventDefinition, new EffectContext(after, new TransactionId("integration.snack.2"), null, null)).Succeeded, "new transaction cannot repeat completed event");
      Console.WriteLine("PASS original full-save real condition/effect/clock/world/store/schema transaction");
    }

    static string Sha256Hex(byte[] b)
    {
      using (var h = SHA256.Create())
      {
        var sb = new StringBuilder();
        foreach (var x in h.ComputeHash(b)) sb.Append(x.ToString("x2"));
        return sb.ToString();
      }
    }

    static string FindRoot()
    {
      var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
      for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "design", "systems", "full-design"))) return dir.FullName;
      throw new InvalidOperationException("repository root containing design/systems/full-design not found from " + Directory.GetCurrentDirectory());
    }

    static Env Load(IDataCodec codec, ISchemaValidationPort schema)
    {
      string root = FindRoot(), fd = Path.Combine(root, "design", "systems", "full-design");
      var e = new Env { Codec = codec, Schema = schema };
      e.Compiler = new CatalogCompiler(e.Parser, schema);
      e.CatalogBytes = File.ReadAllBytes(Path.Combine(fd, "examples", "full-catalog.json"));
      e.RunnerBytes = File.ReadAllBytes(Path.Combine(fd, "examples", "runner-bundle.json"));
      e.SaveBytes = File.ReadAllBytes(Path.Combine(fd, "examples", "safe-save.json"));
      e.CatalogHash = Sha256Hex(e.CatalogBytes);
      e.Catalog = e.Bundle(CatalogFile, e.CatalogBytes, true);
      e.Runner = e.Bundle(RunnerFile, e.RunnerBytes, true);
      e.Save = e.Bundle(SaveFile, e.SaveBytes, false);
      e.Functions = e.Bundle("schemas/functions.json", File.ReadAllBytes(Path.Combine(fd, "schemas", "functions.json")), true);
      e.CatalogSchema = e.Bundle("schemas/catalog.schema.json", File.ReadAllBytes(Path.Combine(fd, "schemas", "catalog.schema.json")), true);
      e.RunnerSchema = e.Bundle("schemas/runner.schema.json", File.ReadAllBytes(Path.Combine(fd, "schemas", "runner.schema.json")), true);
      e.SaveSchema = e.Bundle("schemas/save.schema.json", File.ReadAllBytes(Path.Combine(fd, "schemas", "save.schema.json")), true);
      e.FixturesPath = Path.Combine(root, "Tests", "PureDomain", "ContentValidation", "negative-fixtures.json");
      return e;
    }

    // ------------------------------------------------------------------ parser

    static void ParserSpecs()
    {
      string bs = ((char)92).ToString(), q = ((char)34).ToString();
      var p = new CallParser();
      string text = "move npc.serin dest.snack timeout=30 / say npc.serin " + q + "a / b " + bs + q + "q" + bs + q + q + " / device: setState object.x supply_stopped count=2";
      var o = p.Parse(CallKind.Script, text, "f", "/p", "e1");
      Check(o.Succeeded, "parser rejected a valid list");
      var n = o.Value.Calls;
      Check(n.Count == 3, "expected 3 calls");
      Check(n[0].Arguments[2].Name == "timeout" && n[0].Arguments[2].Kind == ArgKind.Integer && n[0].Arguments[2].Integer == 30, "named integer argument");
      Check(n[1].Arguments[1].Kind == ArgKind.String && n[1].Arguments[1].Text == "a / b " + q + "q" + q, "escaped quoted string");
      Check(n[2].Label == "device" && n[2].Function == "setState", "label");
      var negated = p.Parse(CallKind.When, "!completed node.a2", "f", "/p", "e1").Value.Calls[0];
      Check(negated.Negated && negated.Function == "completed", "condition negation");
      Check(!p.Parse(CallKind.Script, "!completed node.a2", "f", "/p", "e1").Succeeded, "script negation refused");
      Check(p.Parse(CallKind.Script, "", "f", "/p", "e").Value.Calls.Count == 0, "empty list");
      Check(p.Parse(CallKind.ScheduleEntry, "420 dest.snack / 1080 home", "f", "/p", "e").Value.Calls[0].Function == "420", "numeric function name");
      var lr = p.Parse(CallKind.Script, "perform puzzle room.case_a3:device", "f", "/p", "e").Value.Calls[0];
      Check(lr.Arguments[1].Kind == ArgKind.Word && lr.Arguments[1].Text == "room.case_a3:device", "label reference stays a word");
      var seven = p.Parse(CallKind.When, "gte x.y 7.0", "f", "/p", "e").Value.Calls[0].Arguments[1];
      Check(seven.Kind == ArgKind.Number && seven.Number == 7.0, "7.0 is a Number token");
      string open = "say npc.a " + q + "open";
      var bad = p.Parse(CallKind.Script, open, "f", "/p", "e");
      Check(!bad.Succeeded && bad.Result.Details[0].Code == ResultCodes.ParseError && bad.Result.Details[0].Location.Offset == open.Length, "unclosed quote");
      Check(!p.Parse(CallKind.Script, "say a / / b", "f", "/p", "e").Succeeded, "empty call");
      Check(!p.Parse(CallKind.Script, "move a b timeout=1 timeout=2", "f", "/p", "e").Succeeded, "duplicate named argument");
      Check(!p.Parse(CallKind.Script, "move a timeout=1 b", "f", "/p", "e").Succeeded, "positional after named");
      Check(!p.Parse(CallKind.Script, "x" + q + "y" + q, "f", "/p", "e").Succeeded, "quote inside token");
    }

    // ------------------------------------------------------------------ dictionary

    static void DictionarySpecs(Env e)
    {
      var o = new FunctionDictionaryLoader().Load(e.Functions.Root);
      Check(o.Succeeded, "function dictionary rejected");
      var d = o.Value;
      Check(ReferenceEquals(d.Source, e.Functions.Root), "dictionary must keep the full raw registry");
      foreach (FunctionRole role in Enum.GetValues(typeof(FunctionRole))) Check(d.All.Any(f => f.Role == role), "no function for role " + role);
      var perform = d.Find(FunctionRole.Command, "perform");
      Check(perform != null && perform.Owner.Contains(" ") && perform.Raw.Kind == DataKind.Object, "owner label with spaces and raw metadata are preserved");
      Check(d.Find(FunctionRole.Command, "eval") == null, "unknown function must not resolve");
      Check(d.Find(FunctionRole.ScheduleEntry, "420") != null && d.Find(FunctionRole.ScheduleEntry, "abc") == null && d.Find(FunctionRole.ScheduleEntry, "1440") == null, "schedule nameRule");
      var members = new List<KeyValuePair<string, DataNode>>();
      foreach (var kv in e.Functions.Root.Members) if (kv.Key != "columns") members.Add(kv);
      var broken = new FunctionDictionaryLoader().Load(DataNode.FromObject(members));
      Check(!broken.Succeeded && broken.Result.Details.Count > 0, "dictionary without columns must fail with diagnostics");
    }

    // ------------------------------------------------------------------ normal examples

    static void NormalSpecs(Env e)
    {
      var o = e.Compiler.Compile(e.Catalog, e.Runner, e.Functions, e.CatalogSchema, e.RunnerSchema);
      Check(o.Succeeded, "normal examples rejected: " + Summ(o.Result.Details));
      var cat = o.Value;
      e.Baseline = cat;
      var lookup = cat as ICompiledContentLookup;
      var schedule = lookup == null ? null : lookup.FindCalls("runner", "schedules", "schedule.serin", "script");
      Check(schedule != null && schedule.Count == 3 && schedule[0].InvocationName == "420" && schedule[0].Function.Name == "*", "compiled schedule retains numeric invocation and wildcard signature");
      Check(schedule[0].Args["destination"].Text == "dest.snack", "compiled schedule destination retained");
      Check(!(schedule is List<CompiledCall>) && lookup.FindCalls("runner", "schedules", "missing", "script") == null, "call cells immutable and missing distinct");
      Check(cat.CatalogHash == e.CatalogHash, "CatalogHash must be the exact catalog SourceHash");
      Check(!(cat.Events is List<CompiledEvent>), "events must not be exposed as a mutable list");
      var evs = e.Runner.Root.L("events");
      Check(cat.Events.Count == evs.Count, "every runner event must compile");
      Check(cat.FindEvent(evs[0].S("id")) != null && cat.FindEvent("event.nope") == null, "FindEvent");
      Check(ReferenceEquals(cat.FindDefinition("catalog", "root"), e.Catalog.Root) && ReferenceEquals(cat.FindDefinition("runner", "root"), e.Runner.Root), "root trees");
      string npcId = e.Runner.Root.L("npcs")[0].S("id");
      Check(cat.FindDefinition("npc", npcId).Equals(e.Runner.Root.L("npcs")[0]), "npc reads the runner Row");
      var meta = cat.FindDefinition("npcMetadata", npcId);
      Check(meta != null && meta.S("id") == npcId, "npcMetadata reads the catalog Row");
      CompiledCall move = null, await = null, choice = null;
      foreach (var ev in cat.Events)
      {
        var all = new List<CompiledCall>(ev.Script);
        foreach (var f in ev.Forks.Values) all.AddRange(f);
        foreach (var c in all)
        {
          if (c.Function.Name == "move" && move == null) move = c;
          if (c.Function.Name == "await" && await == null) await = c;
          if (c.Function.Name == "choice" && choice == null) choice = c;
        }
      }
      Check(move != null && move.Args["timeout"].Kind == DataKind.Number && move.Args["portals"].Kind == DataKind.Bool, "move args are typed with defaults applied");
      Check(await != null && await.Args["handles"].Kind == DataKind.List && await.Args["handles"].Items.All(x => x.Kind == DataKind.String), "await handles are a list of strings");
      Check(choice != null && choice.Args["options"].Items.All(x => x.Kind == DataKind.Object && x.G("fork") != null && x.G("label") != null), "choice options are {fork,label} objects");
      var v = new SaveStateValidator(cat, e.SaveSchema.Root, e.Schema);
      var sd = v.Validate(new StateSnapshot(1, e.Save.Root));
      Check(sd.Count == 0, "normal save rejected: " + Summ(sd));
    }

    static void HashSpecs(Env e)
    {
      foreach (var h in new[] { "", "abc", e.CatalogHash.ToUpperInvariant() })
      {
        var o = e.Compiler.Compile(new ContentBundle(CatalogFile, e.Catalog.Root, h), e.Runner, e.Functions, e.CatalogSchema, e.RunnerSchema);
        Check(!o.Succeeded && o.Result.Details.Count > 0, "invalid catalog hash must be rejected: '" + h + "'");
      }
    }

    static void ProviderSpecs(Env e)
    {
      var provider = new CatalogProvider(e.Compiler);
      Check(provider.Current == null, "provider starts empty");
      var first = provider.TryReplace(e.Catalog, e.Runner, e.Functions, e.CatalogSchema, e.RunnerSchema);
      Check(first.Succeeded && ReferenceEquals(provider.Current, first.Value), "valid bundle replaces Current");
      var members = new List<KeyValuePair<string, DataNode>>(e.Catalog.Root.Members);
      var items = new List<DataNode>(e.Catalog.Root.Items.Count == 0 ? e.Catalog.Root.L("items") : new DataNode[0]);
      Check(items.Count > 1, "catalog items expected");
      items[1] = items[1].With("id", items[0].G("id"));
      var badRoot = e.Catalog.Root.With("items", DataNode.FromList(items));
      var bad = provider.TryReplace(new ContentBundle(CatalogFile, badRoot, e.CatalogHash), e.Runner, e.Functions, e.CatalogSchema, e.RunnerSchema);
      Check(!bad.Succeeded && bad.Result.Details.Any(d => d.Code == ContentCodes.DuplicateId), "duplicate id must be reported");
      Check(ReferenceEquals(provider.Current, first.Value), "failed bundle must keep the previous valid catalog");
      var again = provider.TryReplace(e.Catalog, e.Runner, e.Functions, e.CatalogSchema, e.RunnerSchema);
      Check(again.Succeeded && ReferenceEquals(provider.Current, again.Value), "valid bundle replaces Current again");
    }

    // ------------------------------------------------------------------ captured negatives

    static string Summ(IEnumerable<Diagnostic> d) { return string.Join(" | ", d.Take(4).Select(x => x.Code + "@" + (x.Location == null ? "" : x.Location.Pointer + " " + x.Location.Call + " +" + x.Location.Offset) + " " + x.Message)); }

    static string[] Segments(string path)
    {
      var l = new List<string>();
      foreach (var s in path.Split('/').Skip(1)) l.Add(s.Replace("~1", "/").Replace("~0", "~"));
      return l.ToArray();
    }

    static JsonNode Child(JsonNode parent, string seg)
    {
      var o = parent as JsonObject;
      if (o != null) return o[seg];
      return ((JsonArray)parent)[int.Parse(seg)];
    }

    static byte[] Patch(byte[] original, JsonNode patches)
    {
      var arr = patches as JsonArray;
      if (arr == null || arr.Count == 0) return original;
      var doc = JsonNode.Parse(new UTF8Encoding(false).GetString(original).TrimStart((char)0xFEFF));
      foreach (JsonNode opn in arr)
      {
        var op = (JsonObject)opn;
        string kind = (string)op["op"], path = (string)op["path"];
        var segs = Segments(path);
        Check(segs.Length > 0, "root patches are not used by the fixtures");
        JsonNode parent = doc;
        for (int i = 0; i < segs.Length - 1; i++) parent = Child(parent, segs[i]);
        string last = segs[segs.Length - 1];
        JsonNode value = op["value"] == null ? null : op["value"].DeepClone();
        var jo = parent as JsonObject; var ja = parent as JsonArray;
        switch (kind)
        {
          case "add":
            if (jo != null) jo[last] = value; else ja.Insert(last == "-" ? ja.Count : int.Parse(last), value);
            break;
          case "replace":
            if (jo != null) { Check(jo.ContainsKey(last), "replace target missing: " + path); jo[last] = value; } else ja[int.Parse(last)] = value;
            break;
          case "remove":
            if (jo != null) { Check(jo.Remove(last), "remove target missing: " + path); } else ja.RemoveAt(int.Parse(last));
            break;
          default: throw new InvalidOperationException("unsupported patch op " + kind);
        }
      }
      return Encoding.UTF8.GetBytes(doc.ToJsonString());
    }

    static List<Diagnostic> Evaluate(Env e, JsonNode patches)
    {
      var cat = e.Bundle(CatalogFile, Patch(e.CatalogBytes, patches["catalog"]), true);
      var run = e.Bundle(RunnerFile, Patch(e.RunnerBytes, patches["runner"]), true);
      var save = e.Bundle(SaveFile, Patch(e.SaveBytes, patches["save"]), false);
      var o = e.Compiler.Compile(cat, run, e.Functions, e.CatalogSchema, e.RunnerSchema);
      var all = new List<Diagnostic>();
      if (!o.Succeeded) all.AddRange(o.Result.Details);
      var basis = o.Succeeded ? o.Value : e.Baseline;
      all.AddRange(new SaveStateValidator(basis, e.SaveSchema.Root, e.Schema).Validate(new StateSnapshot(1, save.Root)));
      return all;
    }

    static JsonObject Cases(Env e)
    {
      var fx = JsonNode.Parse(File.ReadAllText(e.FixturesPath));
      var res = new JsonObject();
      foreach (JsonNode c in (JsonArray)fx["cases"]) res[(string)c["name"]] = c.DeepClone();
      return res;
    }

    static void NegativeSpecs(Env e)
    {
      var fx = JsonNode.Parse(File.ReadAllText(e.FixturesPath));
      var cases = (JsonArray)fx["cases"];
      Check(cases.Count == 60, "expected 60 captured negatives, found " + cases.Count);
      foreach (JsonNode c in cases)
      {
        string name = (string)c["name"];
        string expected = c["expectedCode"] == null ? null : (string)c["expectedCode"];
        var diags = Evaluate(e, c["patches"]);
        Check(diags.Count > 0, name + " was accepted");
        if (expected != null) Check(diags.Any(d => d.Code == expected), name + " expected " + expected + " got " + Summ(diags));
      }
    }

    static string PatchValue(JsonObject cases, string name, string file)
    {
      return (string)cases[name]["patches"][file][0]["value"];
    }

    static Diagnostic Find(List<Diagnostic> d, string code, string pointer)
    {
      var m = d.FirstOrDefault(x => x.Code == code && x.Location != null && x.Location.Pointer == pointer);
      Check(m != null, "no " + code + " at " + pointer + "; got " + Summ(d));
      return m;
    }

    static void DetailSpecs(Env e)
    {
      var cases = Cases(e);
      string t = PatchValue(cases, "call-unclosed-quote", "runner");
      var d = Find(Evaluate(e, cases["call-unclosed-quote"]["patches"]), ResultCodes.ParseError, "/events/0/script");
      Check(d.Location.Offset == t.Length && d.Location.Call.EndsWith("#2") && d.Location.File == RunnerFile, "unclosed quote location");

      t = PatchValue(cases, "call-schedule-not-ascending", "runner");
      d = Find(Evaluate(e, cases["call-schedule-not-ascending"]["patches"]), ResultCodes.ArgType, "/schedules/0/script");
      Check(d.Location.Offset == t.IndexOf("420", StringComparison.Ordinal), "schedule offset points at the offending entry");

      d = Find(Evaluate(e, cases["call-move-without-destination"]["patches"]), ResultCodes.ArgCount, "/events/1/script");
      Check(d.Location.Call.EndsWith("#1") && d.Location.Offset == 0, "missing destination call index and offset");

      Find(Evaluate(e, cases["call-say-extra-argument"]["patches"]), ResultCodes.ArgCount, "/events/0/forks/tea");
      Find(Evaluate(e, cases["call-unused-fork"]["patches"]), ResultCodes.ForkUnused, "/events/0/forks/extra");

      var reused = Evaluate(e, cases["call-handle-reused"]["patches"]);
      Find(reused, ResultCodes.HandleDuplicate, "/events/1/script");
      Find(reused, ResultCodes.HandleConflict, "/events/1/script");

      t = PatchValue(cases, "call-handle-left-open-at-end", "runner");
      d = Find(Evaluate(e, cases["call-handle-left-open-at-end"]["patches"]), ResultCodes.HandleUnawaited, "/events/2/script");
      Check(d.Location.Call.EndsWith("#end") && d.Location.Offset == t.Length, "open handle at the end of the list");
    }
  }
}
