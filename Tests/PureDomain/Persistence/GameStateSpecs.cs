using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Content.Parsing;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.State;
using MagicCampus.Transactions;
using MagicCampus.UnityAdapters.ContentIO;
using MagicCampus.UnityAdapters.Storage;

namespace MagicCampus.Tests.PureDomain.PersistenceTests
{
    public static class GameStateSpecs
    {
        private const string Fixtures = "Content/Fixtures/FirstConnection/generated/";
        private static readonly JsonDataCodec Codec = new JsonDataCodec();
        private static readonly StandardSchemaValidator Schema = new StandardSchemaValidator(Codec);

        private static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException("GameState: " + what); }

        private static DataNode Parse(string label, ReadOnlyMemory<byte> bytes)
        {
            var r = Codec.Parse(label, bytes);
            Check(r.Succeeded, "parse " + label);
            return r.Value.Root;
        }

        private static ContentBundle Bundle(string file) { var r = Codec.Parse(file, File.ReadAllBytes(file)); Check(r.Succeeded, "parse " + file); return r.Value; }
        private static DataNode ReadJson(string file) => Bundle(file).Root;
        private static byte[] Bytes(DataNode tree) => Codec.Serialize(tree).ToArray();

        private static GameState Load(DataNode tree)
        {
            var r = SaveDocument.Read(tree);
            Check(r.Succeeded, "read " + (r.Succeeded ? "" : r.Result.Details[0].Location.Pointer + " " + r.Result.Details[0].Message));
            return r.Value;
        }

        private static GameState Offered() => Load(ReadJson(Fixtures + "base/save-offered.json"));

        private static void RoundTrip(string label, DataNode tree)
        {
            var back = Bytes(SaveDocument.Write(Load(tree)));
            Check(back.SequenceEqual(Bytes(tree)), "round trip bytes differ for " + label);
        }

        /// <summary>Top-level save fields whose written value differs between two states.</summary>
        private static string Changed(GameState before, GameState after)
        {
            var a = SaveDocument.Write(before);
            var b = SaveDocument.Write(after);
            return string.Join(",", a.Members.Keys.Where(k => !a.Members[k].Equals(b.Members[k])).OrderBy(k => k, StringComparer.Ordinal));
        }

        private static string Code<T>(Outcome<T> o) where T : class => o.Result.Code;

        public static void RunRoundTrip()
        {
            var fixtures = new List<string>();
            foreach (var dir in new[] { Fixtures, "Assets/StreamingAssets/FirstConnection/" })
                fixtures.AddRange(Directory.GetFiles(dir, "save-*.json", SearchOption.AllDirectories));
            Check(fixtures.Count == 12, "12 committed fixture saves, found " + fixtures.Count);
            foreach (var f in fixtures) RoundTrip(f, ReadJson(f));

            var envelopes = new SaveCodec(Codec, new NoValidation());
            int decoded = 0, unreadable = 0;
            var frames = new List<string>();
            foreach (var dir in new[] { "Tests/Results", "design/implementation-tasks/execution" })
                if (Directory.Exists(dir)) frames.AddRange(Directory.GetFiles(dir, "gen-*.mcgen", SearchOption.AllDirectories));
            foreach (var f in frames)
            {
                var generation = long.Parse(Path.GetFileNameWithoutExtension(f).Substring(4));
                byte[] body;
                string checksum;
                Outcome<SaveEnvelope> envelope = null;
                if (GenerationFraming.TryDecode(File.ReadAllBytes(f), generation, out body, out checksum) == null) envelope = envelopes.Decode(body);
                if (envelope == null || !envelope.Succeeded) { unreadable++; continue; }
                RoundTrip(f, Parse(f, envelope.Value.Payload));
                decoded++;
            }
            Check(decoded >= 17, "the 17 committed generation files decode, decoded " + decoded);
            Console.WriteLine("  round trip: " + fixtures.Count + " fixtures, " + decoded + " generation files (" + unreadable + " unreadable frames skipped)");

            AdmitChecks();
            ReadRefusals();
        }

        private static void AdmitChecks()
        {
            foreach (var variant in new[] { "base", "variants/a10-mira" })
            {
                var catalog = new CatalogCompiler(new CallParser(), Schema).Compile(Bundle(Fixtures + "base/full-catalog.json"), Bundle(Fixtures + variant + "/runner-bundle.json"),
                    Bundle(Fixtures + "base/schemas/functions.json"), Bundle(Fixtures + "base/schemas/catalog.schema.json"), Bundle(Fixtures + "base/schemas/runner.schema.json"));
                Check(catalog.Succeeded, "compile " + variant);
                var validator = new SaveStateValidator(catalog.Value, ReadJson(Fixtures + "base/schemas/save.schema.json"), Schema);
                foreach (var f in Directory.GetFiles(Fixtures + variant, "save-*.json"))
                {
                    var tree = ReadJson(f);
                    var admitted = SaveDocument.Admit(tree, validator);
                    Check(admitted.Succeeded, "admit " + f);
                    Check(Bytes(SaveDocument.Write(admitted.Value)).SequenceEqual(Bytes(tree)), "admitted state writes the same bytes " + f);
                }
                if (variant != "base") continue;
                var offered = ReadJson(Fixtures + "base/save-offered.json");
                var missing = DataNode.FromObject(offered.Members.Where(kv => kv.Key != "mail"));
                var noMail = SaveDocument.Admit(missing, validator);
                Check(Code(noMail) == ResultCodes.ContentInvalid && noMail.Result.Details.Any(d => d.Code == "SAVE_FIELD_MISSING"), "admit refuses a save without /mail");
                var badX = SaveDocument.Admit(offered.With("player", offered.Members["player"].With("x", DataNode.FromString("far"))), validator);
                Check(Code(badX) == ResultCodes.ContentInvalid, "admit refuses a schema-invalid player.x");
            }
        }

        private static void ReadRefusals()
        {
            var offered = ReadJson(Fixtures + "base/save-offered.json");
            var flag = DataNode.FromObject(new[] { new KeyValuePair<string, DataNode>("id", DataNode.FromString("story.intro_done")), new KeyValuePair<string, DataNode>("value", DataNode.True) });
            var twice = offered.With("flags", DataNode.FromList(offered.Members["flags"].Items.Concat(new[] { flag })));
            var dup = SaveDocument.Read(twice);
            Check(!dup.Succeeded && Code(dup) == ResultCodes.ContentInvalid && dup.Result.Details[0].Location.Pointer == "/flags", "read refuses a duplicate flag id");
            var ledger = SaveDocument.Read(offered.With("rewardLedger", DataNode.FromList(new[] { DataNode.FromString("reward.a"), DataNode.FromString("reward.a") })));
            Check(Code(ledger) == ResultCodes.ContentInvalid, "read refuses a duplicate rewardLedger entry");
            var big = SaveDocument.Read(offered.With("wallet", DataNode.FromInteger(Crowns.Limit + 1)));
            Check(Code(big) == ResultCodes.ContentInvalid, "read refuses a wallet over 2147483647");
        }

        /// <summary>
        /// Only SaveDocument opens the preserved fields (design Q1): a rule that wrote them would reach the disk with no typed check.
        /// GameState.cs declares them and passes them through its With copies.
        /// </summary>
        public static void RunPreservedSourceCheck()
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "Assets/MagicCampus/State/GameState.cs", "Assets/MagicCampus/Persistence/SaveDocument.cs" };
            var token = new System.Text.RegularExpressions.Regex(@"\b(Preserved|PreservedFields|TopLevel|PlayerRest)\b");
            var files = Directory.GetFiles("Assets/MagicCampus", "*.cs", SearchOption.AllDirectories);
            Check(files.Length > 50, "game sources found: " + files.Length);
            var offenders = new List<string>();
            foreach (var file in files)
            {
                var path = file.Replace(Path.DirectorySeparatorChar, (char)47);
                if (allowed.Contains(path)) continue;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                    if (token.IsMatch(lines[i])) offenders.Add(path + ":" + (i + 1));
            }
            Check(offenders.Count == 0, "preserved save fields are touched outside SaveDocument: " + string.Join(", ", offenders));
        }

        private sealed class NoValidation : ISaveValidator
        {
            public IReadOnlyList<Diagnostic> Validate(DataNode tree) => new Diagnostic[0];
        }

        public static void RunTransitions()
        {
            Reward();
            PracticeEarnsNothing();
            WalletLimit();
            DuplicateClaim();
            RewardLedgerOnlyKeyIsConsumed();
            Midnight();
            PositionKeepsNumberKind();
        }

        private static void Reward()
        {
            var s = Offered();
            var key = new CompletionKey("event.snack");
            Check(!s.Progress.IsConsumed(key), "event.snack starts unconsumed");
            var claimed = s.Progress.Claim(key);
            Check(claimed.Succeeded, "claim event.snack");
            var earned = claimed.Value.Earn(20);
            Check(earned.Succeeded, "earn 20");
            var after = s.With(earned.Value);
            var p = after.Progress;
            Check(p.Completed.Items.Last() == "event.snack" && p.RewardLedger.Items.Last() == "event.snack", "claim appends the key to both ledgers");
            Check(p.Wallet.Value == 220, "wallet 200 + 20 = 220, got " + p.Wallet.Value);
            Check(p.IsConsumed(key), "claimed key is consumed");
            Check(Changed(s, after) == "completed,rewardLedger,wallet", "reward writes completed, rewardLedger, wallet only; wrote " + Changed(s, after));
        }

        private static void PracticeEarnsNothing()
        {
            var s = Offered();
            var done = s.Progress.Set("memory.snack_done", FlagValue.Of(true)).Complete("practice.snack_notes");
            Check(done.Succeeded, "practice writes a flag and a complete id");
            var after = s.With(done.Value);
            Check(after.Progress.Wallet.Value == 200 && after.Progress.RewardLedger.Count == s.Progress.RewardLedger.Count, "practice leaves wallet and rewardLedger as they were");
            Check(!after.Progress.IsConsumed(new CompletionKey("event.snack")), "practice does not consume the event key");
            Check(Changed(s, after) == "completed,flags", "practice writes flags and completed only; wrote " + Changed(s, after));
        }

        private static void WalletLimit()
        {
            var s = Load(ReadJson(Fixtures + "base/save-offered.json").With("wallet", DataNode.FromInteger(Crowns.Limit - 5)));
            var full = s.Progress.Earn(5);
            Check(full.Succeeded && full.Value.Wallet.Value == 2147483647, "wallet reaches exactly 2147483647");
            Check(Bytes(SaveDocument.Write(s.With(full.Value))).SequenceEqual(Bytes(ReadJson(Fixtures + "base/save-offered.json").With("wallet", DataNode.FromInteger(2147483647)))), "a full wallet writes 2147483647");
            var over = full.Value.Earn(1);
            Check(!over.Succeeded && Code(over) == ResultCodes.InvariantViolation, "one crown past 2147483647 is INVARIANT_VIOLATION, got " + Code(over));
            Check(full.Value.Wallet.Value == 2147483647, "a refused earn leaves the wallet unchanged");
            Check(Code(s.Progress.Earn(6)) == ResultCodes.InvariantViolation, "earn past the limit in one step is INVARIANT_VIOLATION");
            Check(Code(s.Progress.Earn(0)) == ResultCodes.InvariantViolation, "earning zero crowns is INVARIANT_VIOLATION");
        }

        private static void DuplicateClaim()
        {
            var s = Offered();
            var key = new CompletionKey("event.snack");
            var first = s.Progress.Claim(key);
            Check(first.Succeeded, "first claim");
            var second = first.Value.Claim(key);
            Check(!second.Succeeded && Code(second) == ResultCodes.ConditionFalse, "second claim is CONDITION_FALSE, got " + Code(second));
            Check(second.Result.Details[0].Code == "COMPLETION_ALREADY_RECORDED", "second claim keeps the COMPLETION_ALREADY_RECORDED diagnostic");
            var again = s.Progress.Complete("class.lantern_basic");
            Check(Code(again) == ResultCodes.ConditionFalse, "complete(id) of a completed id is CONDITION_FALSE");
        }

        private static void RewardLedgerOnlyKeyIsConsumed()
        {
            var s = Offered();
            var key = new CompletionKey("reward.lantern_basic");
            Check(s.Progress.RewardLedger.Contains(key.Value) && !s.Progress.Completed.Contains(key.Value), "fixture holds reward.lantern_basic in rewardLedger only");
            Check(s.Progress.IsConsumed(key), "a key only in rewardLedger is consumed");
            var again = s.Progress.Claim(key);
            Check(!again.Succeeded && Code(again) == ResultCodes.ConditionFalse, "re-reward of a rewardLedger-only key is CONDITION_FALSE, got " + Code(again));
            var reloaded = SaveDocument.Read(SaveDocument.Write(s)).Value.Progress;
            Check(reloaded.IsConsumed(key), "a saved and reloaded state reads a rewardLedger-only key as consumed");
            Check(!reloaded.IsConsumed(new CompletionKey("event.snack")), "a saved and reloaded state leaves an unconsumed key open");
        }

        private static void Midnight()
        {
            var calendar = new MagicCampus.Timekeeping.Calendar();
            var s = Offered();
            Check(s.Clock.AbsoluteMinute == 420 && !s.Clock.ReturnPending && s.Clock.DayJobs.Count == 0, "fixture starts at 07:00 day 0 with no day jobs");

            var morning = s.With(s.Clock.Advance(calendar.Plan(s.Clock.Time, 30)));
            Check(morning.Clock.DayJobs.Count == 0 && !morning.Clock.ReturnPending, "an advance within the day records no boundary");
            Check(SaveDocument.Write(morning).Members["clock"].Members["fraction"].Kind == DataKind.Number, "a moved clock writes fraction as a number");

            var night = s.With(s.Clock.Advance(calendar.Plan(s.Clock.Time, 1440 - 420 + 30)));
            Check(night.Clock.AbsoluteMinute == 1470, "clock lands at 00:30 day 1, got " + night.Clock.AbsoluteMinute);
            Check(night.Clock.DayJobs.Items.SequenceEqual(new[] { "boundary.day_d1" }) && night.Clock.ReturnPending, "crossing midnight records boundary.day_d1 and returnPending");
            Check(Changed(s, night) == "clock,dayJobs,returnPending", "a clock advance writes clock, dayJobs, returnPending only; wrote " + Changed(s, night));

            var next = night.With(night.Clock.Advance(calendar.Plan(night.Clock.Time, 1440)));
            Check(next.Clock.DayJobs.Items.SequenceEqual(new[] { "boundary.day_d1", "boundary.day_d2" }) && next.Clock.ReturnPending, "each midnight is recorded once, in order");
        }

        private static void PositionKeepsNumberKind()
        {
            var s = Offered();
            Check(!s.World.Player.X.Integral && s.World.Player.X.Value == 1.5, "fixture player x is the number 1.5");
            var none = new KeyValuePair<string, Position>[0];
            var at2 = s.With(s.World.Project(new WorldPlaces(new Position("map.courtyard", 2.0, 1.5), none)));
            var x2 = SaveDocument.Write(at2).Members["player"].Members["x"];
            Check(x2.Kind == DataKind.Number && x2.Number == 2.0, "x moved to 2.0 is written as the number 2.0, not the integer 2");
            var back = at2.With(at2.World.Project(new WorldPlaces(new Position("map.courtyard", 1.5, 1.5), none)));
            Check(Bytes(SaveDocument.Write(back)).SequenceEqual(Bytes(SaveDocument.Write(s))), "moving back to 1.5 writes the original bytes");

            var integral = Load(ReadJson(Fixtures + "base/save-offered.json").With("player", ReadJson(Fixtures + "base/save-offered.json").Members["player"].With("x", DataNode.FromInteger(3))));
            var still = integral.With(integral.World.Project(new WorldPlaces(new Position("map.courtyard", 3.0, 1.5), none)));
            Check(SaveDocument.Write(still).Members["player"].Members["x"].Kind == DataKind.Integer, "an unmoved integer x stays an integer");

            var npc = s.With(s.World.Project(new WorldPlaces(null, new[] { new KeyValuePair<string, Position>("npc.serin", new Position("map.courtyard", 1.5, 6.0)), new KeyValuePair<string, Position>("npc.new", new Position("map.courtyard", 2.0, 2.0)) })));
            var rows = SaveDocument.Write(npc).Members["npcPositions"].Items;
            Check(rows[0].Members["npcId"].Text == "npc.serin" && rows[0].Members["y"].Kind == DataKind.Number && rows[0].Members["y"].Number == 6.0, "a known NPC row is updated in place");
            Check(rows.Count == s.World.Npcs.Count + 1 && rows.Last().Members["npcId"].Text == "npc.new" && rows.Last().Members["x"].Kind == DataKind.Number, "an unknown NPC is appended with number coordinates");
            Check(SaveDocument.Write(npc).Members["player"].Equals(SaveDocument.Write(s).Members["player"]), "a capture without a player leaves the player row");
        }
    }
}
