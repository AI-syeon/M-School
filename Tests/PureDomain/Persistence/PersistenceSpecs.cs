using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Runtime;

namespace MagicCampus.Tests.PureDomain.PersistenceTests
{
    public static class PersistenceSpecs
    {
        private static int checks;
        private static readonly string H64 = new string('a', 64);
        private static readonly JsonCodec Json = new JsonCodec();

        public static int Checks { get { return checks; } }

        public static void Run()
        {
            CodecChecks();
            PolicyChecks();
            MigrationChecks();
            RestorerChecks();
            ServiceChecks();
            RecoveryChecks();
        }

        /// <summary>Integration entry: the factory binds the real disk store to the handle factory pumped here.</summary>
        public static void RunRealDisk(Func<IHandleFactory, IFileGenerationStore> storeFactory)
        {
            if (storeFactory == null) throw new ArgumentNullException(nameof(storeFactory));
            var rig = new Rig(storeFactory);
            rig.Policy.Notify(SafeBoundary.WorldSafe, new StateSnapshot(3, Full(wallet: 321)));
            var s1 = rig.Service.SaveLatestSafe();
            Await(rig.Pump, s1);
            Check(s1.Terminal != null && s1.Terminal.Status == OperationStatus.Succeeded, "real disk: first save succeeds");
            var s2 = rig.Service.SaveLatestSafe();
            Await(rig.Pump, s2);
            Check(s2.Terminal != null && s2.Terminal.Status == OperationStatus.Succeeded && s2.Value.Generation > s1.Value.Generation, "real disk: generation increases");
            var list = rig.FilePort.ListCandidates();
            Check(list.Count >= 2 && list[0].Generation > list[1].Generation, "real disk: candidates newest first");
            var ld = rig.Service.LoadLatest();
            Await(rig.Pump, ld);
            Check(ld.Terminal != null && ld.Terminal.Status == OperationStatus.Succeeded, "real disk: load succeeds");
            Check(rig.Store.ReplaceCalls == 1 && rig.Store.Current.Root.Members["wallet"].Integer == 321, "real disk: single replace with saved state");
            Check(ld.Value.Generation == s2.Value.Generation, "real disk: newest generation loaded");
        }

        // ------------------------------------------------------------------ helpers

        private static void Check(bool ok, string what)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("PersistenceSpecs failed: " + what);
        }

        private static void Eq(object expected, object actual, string what)
        {
            Check(string.Equals(Convert.ToString(expected, CultureInfo.InvariantCulture), Convert.ToString(actual, CultureInfo.InvariantCulture), StringComparison.Ordinal),
                what + " expected=" + expected + " actual=" + actual);
        }

        private static void Throws<T>(Action a, string what) where T : Exception
        {
            try { a(); }
            catch (T) { checks++; return; }
            throw new InvalidOperationException("PersistenceSpecs failed: expected exception " + what);
        }

        private static void Fails(Outcome<StateSnapshot> o, string code, string detailCode, string what)
        {
            Check(!o.Succeeded && o.Result.Code == code && o.Result.Details.Any(d => d.Code == detailCode), what + " (got " + o.Result.Code + ")");
        }

        private static void Await(CompletionPump pump, IHandle h)
        {
            for (var i = 0; i < 4000 && h.Terminal == null; i++)
            {
                pump.AdvanceTick();
                if (h.Terminal == null && i > 20) Thread.Sleep(1);
            }
        }

        private static Diagnostic Dx(string code) { return new Diagnostic(code, "m", null, "e", "a"); }
        private static DataNode S(string s) { return DataNode.FromString(s); }
        private static DataNode I(long v) { return DataNode.FromInteger(v); }
        private static DataNode N(double v) { return DataNode.FromNumber(v); }
        private static DataNode L(params DataNode[] a) { return DataNode.FromList(a); }
        private static DataNode O(params KeyValuePair<string, DataNode>[] m) { return DataNode.FromObject(m); }
        private static KeyValuePair<string, DataNode> P(string k, DataNode v) { return new KeyValuePair<string, DataNode>(k, v); }

        private static DataNode Stack(string item, int quantity, string provenance)
        {
            return O(P("itemId", S(item)), P("quantity", I(quantity)), P("provenance", S(provenance)), P("quality", S("standard")), P("scopeId", DataNode.Null));
        }

        private static DataNode Without(DataNode root, string key)
        {
            var d = new Dictionary<string, DataNode>();
            foreach (var kv in root.Members) if (kv.Key != key) d[kv.Key] = kv.Value;
            return DataNode.FromObject(d);
        }

        /// <summary>A complete save v2 object with all 45 root fields.</summary>
        private static DataNode Full(long gen = 1, string safePoint = "world-safe", string hash = null, long wallet = 200)
        {
            var d = new Dictionary<string, DataNode>();
            foreach (var f in SaveFields.Root) d[f] = L();
            d["saveVersion"] = I(2);
            d["contentVersion"] = S("test");
            d["generation"] = I(gen);
            d["safePoint"] = S(safePoint);
            d["clock"] = O(P("absoluteMinute", I(420)), P("fraction", I(0)));
            d["player"] = O(P("name", S("item.a")), P("pronounKey", S("neutral")), P("appearanceId", S("appearance.default")), P("hp", I(100)), P("focus", I(100)),
                P("mapId", S("map.a")), P("x", N(1.5)), P("y", N(1.5)), P("quickSlots", L(S("spell.lantern"), DataNode.Null, DataNode.Null, DataNode.Null)));
            d["wallet"] = I(wallet);
            var stacks = new List<DataNode> { Stack("item.a", 2, "normal"), Stack("item.g", 3, "grant") };
            d["stacks"] = DataNode.FromList(stacks);
            var att = new List<DataNode> { Stack("item.g", 1, "grant") };
            d["mail"] = L(O(P("id", S("mail.a")), P("attachments", DataNode.FromList(att)), P("kind", S("info")), P("createdAt", I(420)), P("reservationId", DataNode.Null), P("state", S("unread"))));
            d["completed"] = L(S("node.old"));
            d["rewardLedger"] = L(S("reward.old"));
            d["appointments"] = L(O(P("id", S("event.a")), P("status", S("Accepted")), P("acceptedAt", I(430))));
            d["settings"] = O(P("textScale", I(1)), P("flash", DataNode.False), P("shake", DataNode.False), P("damageMultiplier", I(1)), P("telegraphMultiplier", I(1)),
                P("aimAssist", DataNode.True), P("textSpeed", I(30)), P("particleDensity", N(0.7)), P("holdToToggle", DataNode.True), P("masterVolume", I(1)),
                P("subtitles", DataNode.True), P("aimAssistStrength", N(0.5)), P("preset", S("basic")));
            d["npcPositions"] = L(O(P("npcId", S("npc.a")), P("mapId", S("map.a")), P("x", N(1.5)), P("y", N(4.5))));
            d["flags"] = L(O(P("id", S("story.x")), P("value", DataNode.True)));
            d["returnPending"] = DataNode.False;
            d["finalState"] = O(P("status", S("locked")), P("choice", DataNode.Null), P("archiveGeneration", DataNode.Null));
            d["companion"] = O(P("npcId", DataNode.Null), P("usedDays", L()));
            d["catalogHash"] = S(hash ?? H64);
            return DataNode.FromObject(d);
        }

        private static DataNode V1Of(DataNode full)
        {
            var d = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            foreach (var kv in full.Members)
                if (!SaveMigration.V1AddedFields.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
            d["saveVersion"] = I(1);
            return DataNode.FromObject(d);
        }

        private static SaveEnvelope Env(DataNode root, int version, string hash, long gen, string checksum = null)
        {
            var bytes = Json.Serialize(root).ToArray();
            return new SaveEnvelope(version, hash, gen, checksum ?? SaveHash.Sha256Hex(bytes), bytes);
        }

        private static byte[] Tamper(byte[] file)
        {
            var node = Json.Parse("e", file).Value.Root;
            var payload = Convert.FromBase64String(node.Members["payload"].Text);
            payload[payload.Length / 2] ^= 1;
            return Json.Serialize(node.With("payload", S(Convert.ToBase64String(payload)))).ToArray();
        }

        private static void Seed(Rig rig, long gen, DataNode root)
        {
            rig.Files.Put(gen, rig.Codec.Encode(rig.Codec.Seal(new StateSnapshot(1, root), gen)).ToArray());
        }

        // ------------------------------------------------------------------ codec, sealing, tamper

        private static void CodecChecks()
        {
            var codec = new SaveCodec(Json);
            var snap = new StateSnapshot(7, Full());
            var env = codec.Seal(snap, 3);
            Check(env.Generation == 3 && env.SaveVersion == 2 && env.CatalogHash == H64, "envelope metadata");
            Eq(SaveHash.Sha256Hex(env.Payload.ToArray()), env.Checksum, "checksum is SHA-256 of the exact payload bytes");
            var bytes = codec.Encode(env);
            var dec = codec.Decode(bytes);
            Check(dec.Succeeded && dec.Value.Payload.ToArray().SequenceEqual(env.Payload.ToArray()), "encode/decode roundtrip");

            var payload = Json.Parse("p", env.Payload).Value.Root;
            Check(SaveFields.Root.Count == 45 && payload.Members.Count == 45, "payload keeps all 45 root fields");
            Eq(3, payload.Members["generation"].Integer, "generation sealed into payload");
            Eq(1, snap.Root.Members["generation"].Integer, "seal does not mutate the snapshot tree");
            Check(payload.Members["wallet"].Kind == DataKind.Integer && payload.Members["wallet"].Integer == 200, "wallet stays an integer");
            Check(payload.Members["settings"].Equals(snap.Root.Members["settings"]), "nested values survive without truncation");

            var copy = env.Payload.ToArray();
            copy[0] ^= 0xFF;
            Check(env.Payload.ToArray()[0] != copy[0], "envelope payload is a defensive copy");

            var node = Json.Parse("e", bytes).Value.Root;
            var bad = codec.Decode(Tamper(bytes.ToArray()));
            Check(!bad.Succeeded && bad.Result.Code == ResultCodes.ContentInvalid && bad.Result.Details.Any(d => d.Code == "CHECKSUM_MISMATCH"), "payload tamper detected");
            Check(!codec.Decode(Json.Serialize(node.With("checksum", S(new string('0', 64))))).Succeeded, "checksum tamper detected");
            Check(!codec.Decode(Json.Serialize(node.With("extra", I(1)))).Succeeded, "unknown envelope member refused");
            Check(!codec.Decode(Encoding.UTF8.GetBytes("not json")).Succeeded, "garbage refused");
            Check(!codec.Decode(Json.Serialize(node.With("payload", S("@@@")))).Succeeded, "non base64 payload refused");
            Check(!codec.Decode(Json.Serialize(node.With("saveVersion", N(2.5)))).Succeeded, "non integer version refused");
            Throws<InvalidOperationException>(() => codec.Seal(new StateSnapshot(1, O(P("saveVersion", I(2)))), 1), "incomplete snapshot is not sealed");
            Throws<InvalidOperationException>(() => codec.Seal(new StateSnapshot(1, Full().With("wallet", N(2.5))), 1), "fractional wallet is not sealed");
        }

        // ------------------------------------------------------------------ safe boundary policy

        private static void PolicyChecks()
        {
            var p = new SafeBoundaryPolicy();
            Check(p.Latest == null, "no boundary yet");
            p.Notify(SafeBoundary.WorldSafe, new StateSnapshot(5, Full()));
            Check(p.Latest.Boundary == SafeBoundary.WorldSafe && !p.Latest.Saved, "world safe accepted");
            var entry = new StateSnapshot(6, Full(2, "scene-entry"));
            p.Notify(SafeBoundary.SceneEntry, entry);
            p.Notify(SafeBoundary.WorldSafe, new StateSnapshot(7, Full()));
            p.Notify(SafeBoundary.RoomSafe, new StateSnapshot(8, Full()));
            Check(ReferenceEquals(p.Latest.Snapshot, entry) && p.Latest.Boundary == SafeBoundary.SceneEntry, "mid-scene world-safe and room-safe do not replace the entry");
            p.Notify(SafeBoundary.SceneComplete, new StateSnapshot(9, Full()));
            Check(p.Latest.Boundary == SafeBoundary.SceneComplete && p.Latest.Snapshot.Revision == 9 && !p.SceneActive, "scene complete replaces entry");
            p.Notify(SafeBoundary.WorldSafe, new StateSnapshot(10, Full()));
            p.Notify(SafeBoundary.RoomSafe, new StateSnapshot(11, Full()));
            Check(p.Latest.Boundary == SafeBoundary.RoomSafe && p.Latest.Snapshot.Revision == 11, "room safe is independent of scene recovery");
            p.Notify(SafeBoundary.WorldSafe, new StateSnapshot(3, Full()));
            Eq(11, p.Latest.Snapshot.Revision, "older revision ignored");
            p.MarkSaved(10);
            Check(!p.Latest.Saved, "mark saved for another revision is ignored");
            p.MarkSaved(11);
            Check(p.Latest.Saved, "matching revision marked saved");
            p.Notify(SafeBoundary.WorldSafe, new StateSnapshot(12, Full()));
            Check(!p.Latest.Saved, "newer boundary starts unsaved");
            p.Notify(SafeBoundary.SceneEntry, new StateSnapshot(13, Full()));
            p.Renew(SafeBoundary.WorldSafe, new StateSnapshot(14, Full()), true);
            Check(p.Latest.Saved && !p.SceneActive, "renew replaces latest and clears the scene flag");
            p.Notify(SafeBoundary.WorldSafe, new StateSnapshot(15, Full()));
            Eq(15, p.Latest.Snapshot.Revision, "world safe accepted after renew");
        }

        // ------------------------------------------------------------------ migration

        private static void MigrationChecks()
        {
            var full = Full();
            var v1 = V1Of(full);
            Check(v1.Members.Count < 45 && v1.Members["saveVersion"].Integer == 1, "v1 fixture lacks the named fields");
            var m = SaveMigration.Migrate(v1, 1);
            Check(m.Succeeded && m.Value.Members.Count == 45 && m.Value.Members["saveVersion"].Integer == 2, "v1 migrates to a complete v2");
            foreach (var kv in SaveMigration.V1AddedFields)
                Check(m.Value.Members[kv.Key].Equals(kv.Value), "documented default for " + kv.Key);
            foreach (var kv in full.Members)
                if (kv.Key != "saveVersion" && !SaveMigration.V1AddedFields.ContainsKey(kv.Key))
                    Check(m.Value.Members[kv.Key].Equals(kv.Value), "retained v1 field " + kv.Key);
            Eq(1, v1.Members["saveVersion"].Integer, "source v1 tree untouched");
            Check(ReferenceEquals(SaveMigration.Migrate(full, 2).Value, full), "v2 passes through");
            Eq(ResultCodes.SaveIncompatible, SaveMigration.Migrate(full, 3).Result.Code, "newer version refused");
            Eq(ResultCodes.SaveIncompatible, SaveMigration.Migrate(V1Of(full).With("saveVersion", I(0)), 0).Result.Code, "version zero refused");
            Eq(ResultCodes.ContentInvalid, SaveMigration.Migrate(v1, 2).Result.Code, "declared version must match the field");
        }

        // ------------------------------------------------------------------ restorer

        private static void RestorerChecks()
        {
            var codec = new SaveCodec(Json);
            var schema = new FakeSchema();
            var refs = new FakeInvariant();
            var rs = new SaveRestorer(Json, schema, O(), c => refs);
            var cat = new FakeCatalog(H64);

            var good = codec.Seal(new StateSnapshot(1, Full(3)), 3);
            var ok = rs.Restore(good, cat);
            Check(ok.Succeeded && ok.Value.Root.Members.Count == 45 && schema.Calls == 1 && refs.Calls == 1, "valid save passes every stage");

            Fails(rs.Restore(good, new FakeCatalog(new string('b', 64))), ResultCodes.SaveIncompatible, "CATALOG_HASH_MISMATCH", "catalog hash mismatch");
            Fails(rs.Restore(Env(Full(), 3, new string('b', 64), 1), cat), ResultCodes.SaveIncompatible, "VERSION_UNSUPPORTED", "newer version refused before the hash is considered");
            Fails(rs.Restore(Env(Full(1), 2, H64, 5), cat), ResultCodes.ContentInvalid, "ENVELOPE_PAYLOAD_MISMATCH", "envelope/payload generation mismatch");
            Fails(rs.Restore(Env(Full(1, hash: new string('c', 64)), 2, H64, 1), cat), ResultCodes.ContentInvalid, "ENVELOPE_PAYLOAD_MISMATCH", "envelope/payload hash mismatch");
            Fails(rs.Restore(Env(Full().With("extra", I(1)), 2, H64, 1), cat), ResultCodes.ContentInvalid, "SAVE_FIELD_UNKNOWN", "unknown field refused, not dropped");
            Fails(rs.Restore(Env(Without(Full(), "wallet"), 2, H64, 1), cat), ResultCodes.ContentInvalid, "SAVE_FIELD_MISSING", "missing field refused");
            Fails(rs.Restore(Env(Full().With("wallet", N(1.5)), 2, H64, 1), cat), ResultCodes.ContentInvalid, "SAVE_WALLET_NOT_INTEGER", "fractional wallet refused");

            refs.Calls = 0;
            schema.Next.Add(Dx("SCHEMA_FAULT"));
            Fails(rs.Restore(good, cat), ResultCodes.ContentInvalid, "SCHEMA_FAULT", "schema fault");
            Check(refs.Calls == 0, "later stages are not run after a schema fault");
            schema.Next.Clear();
            refs.Next.Add(Dx("REF_NOT_FOUND"));
            Fails(rs.Restore(good, cat), ResultCodes.ContentInvalid, "REF_NOT_FOUND", "reference fault");
            refs.Next.Clear();

            var v1 = rs.Restore(Env(V1Of(Full()), 1, H64, 1), cat);
            Check(v1.Succeeded && v1.Value.Root.Members.Count == 45 && v1.Value.Root.Members["saveVersion"].Integer == 2, "v1 envelope restores as v2");
        }

        // ------------------------------------------------------------------ save service

        private static void ServiceChecks()
        {
            // success, generations above the highest on disk
            var rig = new Rig();
            rig.Policy.Notify(SafeBoundary.WorldSafe, new StateSnapshot(3, Full()));
            var h = rig.Service.SaveLatestSafe();
            Await(rig.Pump, h);
            Check(h.Terminal != null && h.Terminal.Status == OperationStatus.Succeeded && h.Terminal.Revision == 3 && h.Value.Generation == 1, "first save succeeds as generation 1");
            Check(rig.Policy.Latest.Saved && rig.Service.LastSavedRevision == 3, "matching revision marked saved");
            var saved = rig.Codec.Decode(rig.Files.Disk[1]);
            Check(saved.Succeeded && Json.Parse("p", saved.Value.Payload).Value.Root.Members["safePoint"].Text == "world-safe", "file records the boundary name");
            var h2 = rig.Service.SaveLatestSafe();
            Await(rig.Pump, h2);
            Eq(2, h2.Value.Generation, "generation increases");
            rig.Files.Put(9, rig.Files.Disk[2]);
            var h3 = rig.Service.SaveLatestSafe();
            Await(rig.Pump, h3);
            Eq(10, h3.Value.Generation, "generation is above the highest on disk");

            // async write finishing after a newer boundary
            rig = new Rig();
            rig.Files.Manual = true;
            rig.Policy.Notify(SafeBoundary.WorldSafe, new StateSnapshot(3, Full()));
            h = rig.Service.SaveLatestSafe();
            Check(h.Terminal == null, "manual write is pending");
            rig.Policy.Notify(SafeBoundary.WorldSafe, new StateSnapshot(4, Full()));
            var busy = rig.Service.SaveLatestSafe();
            Check(busy.Terminal != null && busy.Terminal.Code == ResultCodes.ResourceBusy, "second save while one is running is refused");
            Check(rig.Service.LoadLatest().Terminal.Code == ResultCodes.ResourceBusy, "load while saving is refused");
            rig.Files.Finish();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Succeeded && h.Terminal.Revision == 3, "save reports the captured revision");
            Check(!rig.Policy.Latest.Saved && rig.Policy.Latest.Snapshot.Revision == 4, "newer boundary is not marked saved by an older write");

            // disk failure keeps memory
            rig = new Rig();
            rig.Files.FailWrites = true;
            rig.Policy.Notify(SafeBoundary.WorldSafe, new StateSnapshot(3, Full()));
            var before = rig.Store.Current.Revision;
            h = rig.Service.SaveLatestSafe();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Failed && h.Terminal.Code == "DISK_FULL", "write failure surfaces");
            Check(rig.Store.Current.Revision == before && rig.Store.ReplaceCalls == 0, "memory state unchanged by write failure");
            Check(!rig.Policy.Latest.Saved && rig.Service.LastSaveResult.Status == OperationStatus.Failed, "unsaved flag and last error exposed");
            rig.Files.FailWrites = false;
            h = rig.Service.SaveLatestSafe();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Succeeded && rig.Policy.Latest.Saved, "retry after failure succeeds");

            // no boundary
            rig = new Rig();
            Eq(ResultCodes.ConditionFalse, rig.Service.SaveLatestSafe().Terminal.Code, "no safe boundary refuses the save");

            // cancellation
            rig = new Rig();
            rig.Files.Manual = true;
            rig.Policy.Notify(SafeBoundary.WorldSafe, new StateSnapshot(3, Full()));
            h = rig.Service.SaveLatestSafe();
            h.Cancel("user");
            Check(h.Terminal == null, "cancel waits for the child to terminate");
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Cancelled && rig.Files.Disk.Count == 0 && !rig.Policy.Latest.Saved, "cancel before replace stores nothing");
            rig = new Rig();
            rig.Files.Manual = true;
            rig.Files.CommitOnCancel = true;
            rig.Policy.Notify(SafeBoundary.WorldSafe, new StateSnapshot(3, Full()));
            h = rig.Service.SaveLatestSafe();
            h.Cancel("late");
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Succeeded && rig.Files.Disk.ContainsKey(1) && rig.Policy.Latest.Saved, "late cancel still reports the generation actually stored");

            // load: newest wins, single replace, boundary and generation renewed
            rig = new Rig();
            Seed(rig, 1, Full(1, wallet: 111));
            Seed(rig, 2, Full(2, wallet: 222));
            before = rig.Store.Current.Revision;
            h = rig.Service.LoadLatest();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Succeeded && h.Value.Generation == 2, "newest generation loaded");
            Check(rig.Store.ReplaceCalls == 1 && rig.Store.Current.Root.Members["wallet"].Integer == 222 && rig.Store.Current.Revision == before + 1, "exactly one ReplaceAll with the newest state");
            Check(rig.Policy.Latest.Saved && rig.Policy.Latest.Snapshot.Revision == rig.Store.Current.Revision && rig.Service.LastKnownGeneration == 2, "load renews boundary and generation");
            h = rig.Service.SaveLatestSafe();
            Await(rig.Pump, h);
            Eq(3, h.Value.Generation, "save after load continues above the loaded generation");

            // fallback to an older valid generation, never merged
            rig = new Rig();
            Seed(rig, 1, Full(1, wallet: 111));
            Seed(rig, 2, Full(2, wallet: 222));
            rig.Files.Put(2, Tamper(rig.Files.Disk[2]));
            h = rig.Service.LoadLatest();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Succeeded && h.Value.Generation == 1 && rig.Store.Current.Root.Members["wallet"].Integer == 111, "invalid newest falls back to older valid");
            Check(rig.Store.ReplaceCalls == 1 && rig.Service.LastLoadResult.Details.Any(d => d.Code == "GENERATION_REJECTED"), "rejected generation reported, one replace");

            // everything invalid
            rig = new Rig();
            Seed(rig, 1, Full(1));
            Seed(rig, 2, Full(2));
            rig.Files.Put(1, Tamper(rig.Files.Disk[1]));
            rig.Files.Put(2, Tamper(rig.Files.Disk[2]));
            before = rig.Store.Current.Revision;
            h = rig.Service.LoadLatest();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Failed && rig.Store.ReplaceCalls == 0 && rig.Store.Current.Revision == before, "all invalid: nothing replaced");
            Check(rig.Service.LastLoadResult.Details.Count(d => d.Code == "GENERATION_REJECTED") == 2, "all rejections kept for the UI");

            // no saves, catalog mismatch
            rig = new Rig();
            Eq(ResultCodes.ConditionFalse, Settle(rig, rig.Service.LoadLatest()).Code, "no generation to load");
            Seed(rig, 1, Full(1));
            rig.Catalogs.Current = new FakeCatalog(new string('d', 64));
            Eq(ResultCodes.SaveIncompatible, Settle(rig, rig.Service.LoadLatest()).Code, "catalog mismatch fails the load");

            // CAS failure: no fallback, no retry
            rig = new Rig();
            Seed(rig, 1, Full(1));
            Seed(rig, 2, Full(2));
            rig.Store.FailCode = ResultCodes.RevisionConflict;
            h = rig.Service.LoadLatest();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Failed && h.Terminal.Code == ResultCodes.RevisionConflict && rig.Store.ReplaceCalls == 1, "replace conflict fails the load without trying an older generation");

            // load cancellation
            rig = new Rig();
            Seed(rig, 1, Full(1));
            rig.Files.HoldReads = true;
            h = rig.Service.LoadLatest();
            Check(h.Terminal == null && rig.Service.SaveLatestSafe().Terminal.Code == ResultCodes.ResourceBusy, "load in flight blocks save");
            h.Cancel("user");
            Check(h.Terminal == null, "load cancel waits for the child read");
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Cancelled && rig.Store.ReplaceCalls == 0, "cancelled load replaces nothing");
            rig.Files.HoldReads = false;
            h = rig.Service.LoadLatest();
            Await(rig.Pump, h);
            Check(h.Terminal.Status == OperationStatus.Succeeded, "load works again after cancel");
        }

        private static Result Settle(Rig rig, IHandle h)
        {
            Await(rig.Pump, h);
            return h.Terminal;
        }

        // ------------------------------------------------------------------ scene recovery

        private static void RecoveryChecks()
        {
            var world = new FakeWorld(new[]
            {
                new ActorState("npc.a", new Position("map.a", 1, 1), 0.3, 1),
                new ActorState("pc", new Position("map.a", 2, 2), 0.3, 2)
            });
            var rec = new SceneRecovery(world);
            var accepted = new StateSnapshot(5, Full());
            Check(rec.CaptureEntry(accepted, "scene.1").Status == OperationStatus.Succeeded, "capture entry");
            world.SetPosition("npc.a", new Position("map.b", 5, 5), "tok");
            var dup = rec.CaptureEntry(new StateSnapshot(6, Full()), "scene.1");
            Check(dup.Status == OperationStatus.Failed && dup.Code == ResultCodes.ResourceBusy, "capture only once per owner");

            world.FailRestore = true;
            var failed = rec.RestoreEntry("scene.1");
            Check(!failed.Succeeded && world.Find("npc.a").Position.MapId == "map.b", "failed actor restore keeps the world and reports the failure");
            world.FailRestore = false;
            var ok = rec.RestoreEntry("scene.1");
            Check(ok.Succeeded && ReferenceEquals(ok.Value, accepted), "entry snapshot returned unchanged");
            Check(world.Find("npc.a").Position.Equals(new Position("map.a", 1, 1)) && world.Find("pc").Position.Equals(new Position("map.a", 2, 2)), "actor positions restored");
            Eq("Accepted", ok.Value.Slice("appointments").Items[0].Members["status"].Text, "accepted reservation retained");
            Check(rec.RestoreEntry("scene.1").Succeeded, "entry stays available until cleared");

            Check(rec.CaptureEntry(new StateSnapshot(7, Full()), "scene.2").Status == OperationStatus.Succeeded, "other owner captures independently");
            Check(rec.ClearEntry("scene.1").Status == OperationStatus.Succeeded && rec.ClearEntry("scene.1").Status == OperationStatus.Succeeded, "clear is idempotent");
            var gone = rec.RestoreEntry("scene.1");
            Check(!gone.Succeeded && gone.Result.Code == ResultCodes.RefNotFound, "cleared entry cannot be restored");
            Check(rec.RestoreEntry("scene.2").Succeeded, "other owner unaffected by clear");

            // room-safe boundaries neither use nor disturb scene recovery
            var policy = new SafeBoundaryPolicy();
            policy.Notify(SafeBoundary.SceneEntry, accepted);
            policy.Notify(SafeBoundary.RoomSafe, new StateSnapshot(9, Full()));
            Check(ReferenceEquals(policy.Latest.Snapshot, accepted) && rec.RestoreEntry("scene.2").Succeeded, "room safe leaves the scene entry alone");
        }

        // ------------------------------------------------------------------ test doubles

        private sealed class Rig
        {
            public readonly CompletionPump Pump = new CompletionPump();
            public readonly IHandleFactory Handles;
            public readonly FakeFiles Files;
            public readonly IFileGenerationStore FilePort;
            public readonly FakeStore Store = new FakeStore(new StateSnapshot(10, Full()));
            public readonly SafeBoundaryPolicy Policy = new SafeBoundaryPolicy();
            public readonly SaveCodec Codec = new SaveCodec(Json);
            public readonly FakeCatalogs Catalogs = new FakeCatalogs { Current = new FakeCatalog(H64) };
            public readonly SaveService Service;

            public Rig(Func<IHandleFactory, IFileGenerationStore> factory = null)
            {
                Handles = new HandleFactory(Pump);
                if (factory == null) { Files = new FakeFiles(Handles); FilePort = Files; }
                else FilePort = factory(Handles);
                var restorer = new SaveRestorer(Json, new FakeSchema(), O(), c => new FakeInvariant());
                Service = new SaveService(Policy, Codec, FilePort, restorer, Store, Catalogs, Handles);
            }
        }

        private sealed class JsonCodec : IDataCodec
        {
            public Outcome<ContentBundle> Parse(string file, ReadOnlyMemory<byte> utf8)
            {
                try
                {
                    using (var doc = JsonDocument.Parse(utf8))
                        return Outcome<ContentBundle>.Ok(new ContentBundle(file, ToNode(doc.RootElement)), 0);
                }
                catch (JsonException e)
                {
                    return Outcome<ContentBundle>.Fail(ResultCodes.ParseError, 0, new Diagnostic("JSON", e.Message, null, "json", "invalid"));
                }
                catch (ArgumentException e)
                {
                    return Outcome<ContentBundle>.Fail(ResultCodes.ParseError, 0, new Diagnostic("JSON_ROOT", e.Message, null, "object", "other"));
                }
            }

            public ReadOnlyMemory<byte> Serialize(DataNode root)
            {
                var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms)) Write(w, root);
                return ms.ToArray();
            }

            private static DataNode ToNode(JsonElement e)
            {
                switch (e.ValueKind)
                {
                    case JsonValueKind.Null: return DataNode.Null;
                    case JsonValueKind.True: return DataNode.True;
                    case JsonValueKind.False: return DataNode.False;
                    case JsonValueKind.String: return DataNode.FromString(e.GetString());
                    case JsonValueKind.Number:
                    {
                        long l;
                        if (e.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && e.TryGetInt64(out l)) return DataNode.FromInteger(l);
                        return DataNode.FromNumber(e.GetDouble());
                    }
                    case JsonValueKind.Array: return DataNode.FromList(e.EnumerateArray().Select(ToNode).ToList());
                    case JsonValueKind.Object:
                        return DataNode.FromObject(e.EnumerateObject().Select(p => new KeyValuePair<string, DataNode>(p.Name, ToNode(p.Value))).ToList());
                    default: throw new JsonException("unsupported value");
                }
            }

            private static void Write(Utf8JsonWriter w, DataNode n)
            {
                switch (n.Kind)
                {
                    case DataKind.Null: w.WriteNullValue(); break;
                    case DataKind.Bool: w.WriteBooleanValue(n.Bool); break;
                    case DataKind.Integer: w.WriteNumberValue(n.Integer); break;
                    case DataKind.Number:
                    {
                        var s = n.Number.ToString("R", CultureInfo.InvariantCulture);
                        if (s.IndexOfAny(new[] { '.', 'E' }) < 0) s += ".0";
                        w.WriteRawValue(s);
                        break;
                    }
                    case DataKind.String: w.WriteStringValue(n.Text); break;
                    case DataKind.List:
                        w.WriteStartArray();
                        foreach (var i in n.Items) Write(w, i);
                        w.WriteEndArray();
                        break;
                    case DataKind.Object:
                        w.WriteStartObject();
                        foreach (var kv in n.Members) { w.WritePropertyName(kv.Key); Write(w, kv.Value); }
                        w.WriteEndObject();
                        break;
                }
            }
        }

        private sealed class FakeCatalog : ICatalog
        {
            public FakeCatalog(string hash) { CatalogHash = hash; }
            public string ContentVersion { get { return "t"; } }
            public string CatalogHash { get; }
            public IReadOnlyList<CompiledEvent> Events { get { return new CompiledEvent[0]; } }
            public CompiledEvent FindEvent(string id) { return null; }
            public DataNode FindDefinition(string kind, string id) { return null; }
        }

        private sealed class FakeCatalogs : ICatalogProvider
        {
            public ICatalog Current { get; set; }
            public Outcome<ICatalog> TryReplace(ContentBundle catalog, ContentBundle runner, ContentBundle functions, ContentBundle catalogSchema, ContentBundle runnerSchema)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakeSchema : ISchemaValidationPort
        {
            public readonly List<Diagnostic> Next = new List<Diagnostic>();
            public int Calls;
            public IReadOnlyList<Diagnostic> Validate(DataNode schema, DataNode instance, string file)
            {
                Calls++;
                return Next.ToArray();
            }
        }

        private sealed class FakeInvariant : IStateInvariant
        {
            public readonly List<Diagnostic> Next = new List<Diagnostic>();
            public int Calls;
            public IReadOnlyList<Diagnostic> Validate(StateSnapshot candidate)
            {
                Calls++;
                return Next.ToArray();
            }
        }

        private sealed class FakeStore : IStateStore
        {
            public FakeStore(StateSnapshot initial) { Current = initial; }
            public StateSnapshot Current { get; private set; }
            public int ReplaceCalls;
            public string FailCode;

            public Outcome<StateSnapshot> Commit(StatePlan plan) { throw new NotSupportedException(); }

            public Outcome<StateSnapshot> ReplaceAll(StateSnapshot candidate, long expectedRevision, TransactionId transactionId)
            {
                ReplaceCalls++;
                if (FailCode != null) return Outcome<StateSnapshot>.Fail(FailCode, Current.Revision, Dx("CAS"));
                if (expectedRevision != Current.Revision) return Outcome<StateSnapshot>.Fail(ResultCodes.RevisionConflict, Current.Revision, Dx("CAS"));
                Current = new StateSnapshot(Current.Revision + 1, candidate.Root);
                return Outcome<StateSnapshot>.Ok(Current, Current.Revision);
            }

            public void OnCommitted(Action<StateCommitted> listener) { }
        }

        private sealed class FakeFiles : IFileGenerationStore
        {
            private readonly IHandleFactory handles;
            public readonly SortedDictionary<long, byte[]> Disk = new SortedDictionary<long, byte[]>();
            public bool Manual, FailWrites, HoldReads, CommitOnCancel;
            private IHandleController<FileGeneration> pendingWrite;
            private long pendingGen;
            private byte[] pendingBytes;
            private int ops;

            public FakeFiles(IHandleFactory handles) { this.handles = handles; }

            public void Put(long generation, byte[] bytes) { Disk[generation] = bytes; }

            private FileGeneration Gen(long g) { return new FileGeneration(g, SaveHash.Sha256Hex(Disk[g]), Disk[g]); }

            public IHandle<FileGeneration> Write(long generation, ReadOnlyMemory<byte> fileBytes)
            {
                var h = handles.Create<FileGeneration>("fake.write." + (++ops));
                h.Start();
                var bytes = fileBytes.ToArray();
                if (FailWrites)
                {
                    h.Fail("DISK_FULL", 0, new[] { Dx("IO") });
                    return h;
                }
                if (Manual)
                {
                    pendingWrite = h; pendingGen = generation; pendingBytes = bytes;
                    h.SetCancellationHandler(reason =>
                    {
                        if (CommitOnCancel) Finish();
                        else { pendingWrite = null; h.Cancelled(reason, 0); }
                    });
                    return h;
                }
                Disk[generation] = bytes;
                h.Succeed(Gen(generation), 0);
                return h;
            }

            public void Finish()
            {
                var h = pendingWrite;
                if (h == null) return;
                pendingWrite = null;
                Disk[pendingGen] = pendingBytes;
                h.Succeed(Gen(pendingGen), 0);
            }

            public IHandle<FileGeneration> Read(long generation)
            {
                var h = handles.Create<FileGeneration>("fake.read." + (++ops));
                h.Start();
                if (HoldReads)
                {
                    h.SetCancellationHandler(reason => h.Cancelled(reason, 0));
                    return h;
                }
                if (Disk.ContainsKey(generation)) h.Succeed(Gen(generation), 0);
                else h.Fail("NOT_FOUND", 0, new[] { Dx("IO") });
                return h;
            }

            public IReadOnlyList<FileGeneration> ListCandidates()
            {
                return Disk.Keys.OrderByDescending(k => k).Select(k => new FileGeneration(k, SaveHash.Sha256Hex(Disk[k]), new byte[0])).ToList();
            }
        }

        private sealed class FakeWorld : IActorWorld
        {
            private List<ActorState> actors;
            public bool FailRestore;

            public FakeWorld(IEnumerable<ActorState> initial) { actors = new List<ActorState>(initial); }

            public ActorState Find(string id) { return actors.FirstOrDefault(a => a.ActorId == id); }
            public IReadOnlyList<ActorState> All { get { return actors.ToArray(); } }

            public Result SetPosition(string actorId, Position position, string ownerToken)
            {
                var i = actors.FindIndex(a => a.ActorId == actorId);
                if (i < 0) return new Result(OperationStatus.Failed, ResultCodes.RefNotFound, 0, null);
                actors[i] = actors[i].WithPosition(position);
                return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
            }

            public Result Restore(IReadOnlyList<ActorState> list)
            {
                if (FailRestore) return new Result(OperationStatus.Failed, ResultCodes.MapLoadFailed, 0, new[] { Dx("WORLD") });
                actors = new List<ActorState>(list);
                return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
            }
        }
    }
}
