using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MagicCampus.Contracts;
using MagicCampus.Content.Catalog;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Transactions.Core;
using MagicCampus.Application.Reservations;
using MagicCampus.World.Movement;
using MagicCampus.Persistence;

namespace MagicCampus.Tests.FirstConnectionData
{
    /// <summary>
    /// Migration diff spec (stage 3, units 4b to 4c): scene entry save, invitation accept and the waiting re-save through
    /// WorldProjection and ReservationService.Accept must leave the same save bytes, revisions and refusals as the world and
    /// reservation participant plans they replaced. Event success still commits through those participants until 4c, which deletes
    /// them together with this spec.
    /// </summary>
    public static partial class FirstConnectionDataSpecs
    {
        const int ParitySteps = 150;

        sealed class ParityMaps : IMapGeometry
        {
            readonly Dictionary<string, MapGeometry> maps = new Dictionary<string, MapGeometry>(StringComparer.Ordinal);
            public void Add(string id, int width, int height) { maps[id] = new MapGeometry(id, width, height); }
            public MapGeometry Find(string id) { return id != null && maps.TryGetValue(id, out var m) ? m : null; }
            public IEnumerable<MapGeometry> All => maps.Values;
        }

        sealed class Parity
        {
            public long Steps, Commits, Unchanged, KindFlips, RowsAppended, InactiveKept, ByteDiffs, RevisionDiffs, ResultDiffs;
            public readonly SortedDictionary<string, long> Ops = new SortedDictionary<string, long>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, long> Refusals = new SortedDictionary<string, long>(StringComparer.Ordinal);
            public string FirstDiff = "";
            public void Diff(ref long count, string what) { if (count++ == 0 && FirstDiff.Length == 0) FirstDiff = " first=" + what; }
        }

        sealed class Pair
        {
            public ActorWorld Actors;
            public StateStore Old, New;
            public WorldStateProjector Projector;
            public WorldProjection Projection;
            public ReservationService Reservations;
            public int Txn;
        }

        public static void RunWorldParity()
        {
            var tally = new Parity();
            var schemaRoot = SchemaRoot;
            var cases = new[]
            {
                ("base/runner-bundle.json", new[] { "base/save-accepted-all.json", "base/save-accepted.json", "base/save-intro-false.json", "base/save-offered.json" }),
                ("variants/a10-mira/runner-bundle.json", new[] { "variants/a10-mira/save-accepted.json", "variants/a10-mira/save-offered.json" })
            };
            var seed = 7001;
            var roots = 0;
            foreach (var (runner, saves) in cases)
            {
                var compiled = Compile(runner).Value;
                var provider = new CatalogProvider(compiled);
                var strict = new ParityMaps();
                var loose = new ParityMaps();
                foreach (var row in compiled.FindDefinition("runner", "root").Members["maps"].Items)
                {
                    var w = (int)row.Members["width"].Integer;
                    var h = (int)row.Members["height"].Integer;
                    strict.Add(S(row, "id"), w, h);
                    loose.Add(S(row, "id"), w + 2, h + 2);
                }
                loose.Add("map.void", 4, 4);
                var eventIds = compiled.Events.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
                eventIds.Add("event.nope");
                eventIds.Add("Bad Id");
                foreach (var save in saves)
                    foreach (var (shape, root) in Shapes(Read(Root + save).Root))
                    {
                        roots++;
                        var label = save + "/" + shape;
                        var pair = NewPair(root, compiled, provider, strict, loose, schemaRoot);
                        Prelude(pair, eventIds[0], tally, label);
                        Wander(pair, strict, eventIds, compiled, new Random(seed++), tally, label);
                    }
            }

            Console.WriteLine("WorldProjectionParity roots=" + roots + " steps=" + tally.Steps + " commits=" + tally.Commits + " unchanged=" + tally.Unchanged
                + " ops=" + string.Join(",", tally.Ops.Select(kv => kv.Key + ":" + kv.Value))
                + " refusals=" + string.Join(",", tally.Refusals.Select(kv => kv.Key + ":" + kv.Value))
                + " kindFlips=" + tally.KindFlips + " rowsAppended=" + tally.RowsAppended + " inactiveKept=" + tally.InactiveKept
                + " byteDiffs=" + tally.ByteDiffs + " revisionDiffs=" + tally.RevisionDiffs + " resultDiffs=" + tally.ResultDiffs + tally.FirstDiff);
            Check(tally.KindFlips > 0 && tally.RowsAppended > 0 && tally.InactiveKept > 0 && tally.Unchanged > 0, "parity covers kind flips, appended and inactive npc rows and no-op entries");
            foreach (var key in new[] { "accept:CONDITION_FALSE", "accept:REF_NOT_FOUND", "accept:CONTENT_INVALID", "entry:CONTENT_INVALID/POSITION_INVALID", "resave:CONTENT_INVALID/POSITION_INVALID", "accept:CONTENT_INVALID/POSITION_INVALID" })
                Check(tally.Refusals.ContainsKey(key), "parity covers the refusal " + key);
            Check(tally.ByteDiffs == 0 && tally.RevisionDiffs == 0 && tally.ResultDiffs == 0, "world projection and typed accept match the participant plans" + tally.FirstDiff);
        }

        static IEnumerable<(string, DataNode)> Shapes(DataNode root)
        {
            yield return ("as-saved", root);
            var player = root.Members["player"];
            var integral = root.With("player", player.With("x", DataNode.FromInteger((long)Math.Floor(Num(player, "x")))).With("y", DataNode.FromInteger((long)Math.Floor(Num(player, "y")))))
                .With("npcPositions", DataNode.FromList(root.Members["npcPositions"].Items.Select(n => n.With("x", DataNode.FromInteger((long)Math.Floor(Num(n, "x")))).With("y", DataNode.FromInteger((long)Math.Floor(Num(n, "y"))))).ToList()));
            yield return ("integral", integral);
            yield return ("no-npc-rows", root.With("npcPositions", DataNode.FromList(new DataNode[0])));
        }

        static double Num(DataNode n, string key) => n.Members[key].Kind == DataKind.Integer ? n.Members[key].Integer : n.Members[key].Number;

        static Position At(DataNode n) => new Position(S(n, "mapId"), Num(n, "x"), Num(n, "y"));

        static Pair NewPair(DataNode root, ICatalog compiled, CatalogProvider provider, ParityMaps strict, ParityMaps loose, DataNode schemaRoot)
        {
            var actors = new List<ActorState> { new ActorState("player", At(root.Members["player"]), .2, 3) };
            var rows = root.Members["npcPositions"].Items;
            for (var i = 0; i < rows.Count; i += 2) actors.Add(new ActorState(S(rows[i], "npcId"), At(rows[i]), .2, 3));
            var pair = new Pair { Actors = new ActorWorld(loose, actors) };
            StateStore Store()
            {
                var registry = new StateParticipantRegistry();
                registry.Register(new WorldCommitParticipant(pair.Actors, strict));
                registry.Register(new ReservationParticipant(provider, new StateCompletionLedger()));
                registry.Seal();
                return new StateStore(new StateSnapshot(0, root), registry, new[] { new SaveStateValidator(compiled, schemaRoot, Schema) }, new CompletionPump());
            }
            pair.Old = Store();
            pair.New = Store();
            pair.Projector = new WorldStateProjector(pair.Actors);
            pair.Projection = new WorldProjection(pair.Actors, strict);
            pair.Reservations = new ReservationService(provider, new MagicCampus.Timekeeping.Calendar(), new StateCompletionLedger());
            return pair;
        }

        /// <summary>The fixed walk: x 1.5, 2.0, 1.5 through re-saves, then an npc added at entry, moved on re-save and made inactive.</summary>
        static void Prelude(Pair p, string firstEvent, Parity tally, string label)
        {
            var map = p.Actors.Find("player").Position.MapId;
            foreach (var x in new[] { 1.5, 2.0, 1.5 })
            {
                Move(p, "player", new Position(map, x, 1.5));
                Step(p, "resave", null, tally, label + " prelude x=" + x);
            }
            var extra = p.New.Current.TypedState().World.Npcs.Select(n => n.NpcId).FirstOrDefault(id => p.Actors.Find(id) == null) ?? "npc.serin";
            if (p.Actors.Find(extra) == null) Restore(p, p.Actors.All.Concat(new[] { new ActorState(extra, new Position(map, 2.5, 2.5), .2, 3) }));
            Step(p, "entry", null, tally, label + " prelude add " + extra);
            Move(p, extra, new Position(map, 3.0, 2.0));
            Step(p, "resave", null, tally, label + " prelude move " + extra);
            Restore(p, p.Actors.All.Where(a => a.ActorId != extra));
            Step(p, "resave", null, tally, label + " prelude inactive " + extra);

            var everyone = p.Actors.All.ToList();
            Restore(p, new ActorState[0]);
            Step(p, "entry", null, tally, label + " prelude no actors");
            Step(p, "resave", null, tally, label + " prelude no actors");
            Step(p, "accept", firstEvent, tally, label + " prelude no actors");
            Restore(p, everyone);
        }

        static void Wander(Pair p, ParityMaps strict, List<string> eventIds, ICatalog compiled, Random random, Parity tally, string label)
        {
            var maps = strict.All.ToList();
            var rowIds = p.New.Current.TypedState().World.Npcs.Select(n => n.NpcId).Concat(new[] { "npc.serin", "npc.parity" }).Distinct().ToList();
            for (var i = 0; i < ParitySteps; i++)
            {
                var roll = random.Next(100);
                var actors = p.Actors.All;
                if (roll < 40)
                {
                    var who = actors[random.Next(actors.Count)].ActorId;
                    Move(p, who, Somewhere(random, maps));
                    Step(p, random.Next(2) == 0 ? "entry" : "resave", null, tally, label + " step " + i);
                }
                else if (roll < 50)
                {
                    var id = rowIds[random.Next(rowIds.Count)];
                    if (p.Actors.Find(id) == null) Restore(p, actors.Concat(new[] { new ActorState(id, Somewhere(random, maps), .2, 3) }));
                    else if (id != "player") Restore(p, actors.Where(a => a.ActorId != id));
                    Step(p, "entry", null, tally, label + " step " + i);
                }
                else if (roll < 55)
                {
                    var who = actors[random.Next(actors.Count)].ActorId;
                    var bad = random.Next(2) == 0 ? new Position("map.void", 1.5, 1.5) : OffEdge(random, maps);
                    Move(p, who, bad);
                    var op = new[] { "entry", "resave", "accept" }[random.Next(3)];
                    Step(p, op, op == "accept" ? eventIds[random.Next(eventIds.Count)] : null, tally, label + " step " + i);
                    Move(p, who, Somewhere(random, maps));
                }
                else if (roll < 85)
                {
                    Step(p, "accept", eventIds[random.Next(eventIds.Count)], tally, label + " step " + i);
                }
                else if (roll < 92)
                {
                    var ev = compiled.Events[random.Next(compiled.Events.Count)];
                    Consume(p, ev.OnceKey.Value, random.Next(3), tally, label + " step " + i);
                }
                else
                {
                    Step(p, random.Next(2) == 0 ? "entry" : "resave", null, tally, label + " step " + i);
                }
            }
        }

        static Position Somewhere(Random random, List<MapGeometry> maps)
        {
            var map = maps[random.Next(maps.Count)];
            double Coord(int size)
            {
                var whole = random.Next(size);
                switch (random.Next(4))
                {
                    case 0: return whole;
                    case 1: return whole + 0.5;
                    case 2: return whole + 0.25;
                    default: return Math.Min(size - 0.001, whole + random.NextDouble());
                }
            }
            return new Position(map.MapId, Coord(map.Width), Coord(map.Height));
        }

        static Position OffEdge(Random random, List<MapGeometry> maps)
        {
            var map = maps[random.Next(maps.Count)];
            return random.Next(2) == 0 ? new Position(map.MapId, map.Width + 0.5, 1.5) : new Position(map.MapId, 1.5, map.Height + 1.0);
        }

        static void Move(Pair p, string id, Position to)
        {
            Check(p.Actors.SetPosition(id, to, "parity").Status == OperationStatus.Succeeded, "parity move " + id + " to " + to);
        }

        static void Restore(Pair p, IEnumerable<ActorState> actors)
        {
            Check(p.Actors.Restore(actors.ToList()).Status == OperationStatus.Succeeded, "parity actor restore");
        }

        /// <summary>Records the key in the completed ledger (ledgers 0), the reward ledger (1) or both (2), as a load would.</summary>
        static void Consume(Pair p, string key, int ledgers, Parity tally, string what)
        {
            var oldRoot = p.Old.Current.Root;
            DataNode Add(DataNode list) => list.Items.Any(n => n.Text == key) ? list : DataNode.FromList(list.Items.Concat(new[] { DataNode.FromString(key) }).ToList());
            var root = ledgers == 1 ? oldRoot : oldRoot.With("completed", Add(oldRoot.Members["completed"]));
            if (ledgers > 0) root = root.With("rewardLedger", Add(root.Members["rewardLedger"]));
            var candidate = new StateSnapshot(0, root);
            var txn = new TransactionId("parity.consume." + (++p.Txn));
            var a = p.Old.ReplaceAll(candidate, p.Old.Current.Revision, txn);
            var b = p.New.ReplaceAll(candidate, p.New.Current.Revision, txn);
            Compare(p, "consume", a.Result, b.Result, null, tally, what + " ledgers=" + ledgers);
        }

        /// <summary>Runs one operation through the pre-4b participant path on Old and the typed path on New, then compares.</summary>
        static void Step(Pair p, string op, string eventId, Parity tally, string what)
        {
            var before = p.New.Current.TypedState();
            var txn = op + "." + (++p.Txn);
            Result oldResult, newResult;
            switch (op)
            {
                case "entry":
                    oldResult = OldProjection(p, txn + ".project", true);
                    newResult = p.New.Commit(new TransactionId(txn + ".project"), p.Projection.Project).Result;
                    break;
                case "resave":
                    oldResult = OldProjection(p, txn, false);
                    newResult = p.New.Commit(new TransactionId(txn), p.Projection.Project).Result;
                    break;
                default:
                    oldResult = OldAccept(p, txn, eventId);
                    newResult = p.New.Commit(new TransactionId(txn), s =>
                    {
                        var accepted = p.Reservations.Accept(s, eventId);
                        return accepted.Succeeded ? p.Projection.Project(accepted.Value) : accepted;
                    }).Result;
                    break;
            }
            Compare(p, op, oldResult, newResult, before, tally, what + " " + op + (eventId == null ? "" : " " + eventId));
        }

        static Result OldProjection(Pair p, string txn, bool skipEmpty)
        {
            var basis = p.Old.Current;
            var projection = p.Projector.PlanProjection(basis);
            if (!projection.Succeeded) return projection.Result;
            if (skipEmpty && projection.Value.Changes.Count == 0) return new Result(OperationStatus.Succeeded, ResultCodes.Ok, basis.Revision, null);
            return p.Old.Commit(new StatePlan(new TransactionId(txn), null, basis.Revision, projection.Value.Changes)).Result;
        }

        static Result OldAccept(Pair p, string txn, string eventId)
        {
            var basis = p.Old.Current;
            var plan = p.Reservations.PlanAccept(eventId, basis);
            if (!plan.Succeeded) return plan.Result;
            var changes = new List<PlannedChange> { plan.Value };
            var projection = p.Projector.PlanProjection(basis);
            if (!projection.Succeeded) return projection.Result;
            changes.AddRange(projection.Value.Changes);
            return p.Old.Commit(new StatePlan(new TransactionId(txn), null, basis.Revision, changes)).Result;
        }

        static void Compare(Pair p, string op, Result oldResult, Result newResult, GameState before, Parity tally, string what)
        {
            tally.Steps++;
            tally.Ops[op] = tally.Ops.TryGetValue(op, out var n) ? n + 1 : 1;
            if (Describe(oldResult) != Describe(newResult)) tally.Diff(ref tally.ResultDiffs, what + ": " + Describe(oldResult) + " vs " + Describe(newResult));
            if (p.Old.Current.Revision != p.New.Current.Revision) tally.Diff(ref tally.RevisionDiffs, what + ": revision " + p.Old.Current.Revision + " vs " + p.New.Current.Revision);
            if (!Codec.Serialize(p.Old.Current.Root).Span.SequenceEqual(Codec.Serialize(p.New.Current.Root).Span)) tally.Diff(ref tally.ByteDiffs, what + ": save bytes");

            if (newResult.Status != OperationStatus.Succeeded)
            {
                var key = op + ":" + newResult.Code + (newResult.Details.Count > 0 && newResult.Details[0].Code != newResult.Code ? "/" + newResult.Details[0].Code : "");
                tally.Refusals[key] = tally.Refusals.TryGetValue(key, out var r) ? r + 1 : 1;
                return;
            }
            if (before == null) return;
            var after = p.New.Current.TypedState();
            if (ReferenceEquals(after, before)) { tally.Unchanged++; return; }
            tally.Commits++;
            if (before.World.Player.X.Integral != after.World.Player.X.Integral || before.World.Player.Y.Integral != after.World.Player.Y.Integral) tally.KindFlips++;
            if (after.World.Npcs.Count > before.World.Npcs.Count) tally.RowsAppended++;
            if (after.World.Npcs.Any(n => p.Actors.Find(n.NpcId) == null)) tally.InactiveKept++;
        }

        static string Describe(Result r)
        {
            var sb = new StringBuilder(r.Status + ":" + r.Code + "@" + r.Revision);
            foreach (var d in r.Details) sb.Append("; ").Append(d.Code).Append(' ').Append(d.Message);
            return sb.ToString();
        }
    }
}
