using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MagicCampus.Contracts;
using MagicCampus.UnityAdapters.Storage;

namespace MagicCampus.Tests.Storage
{
    /// <summary>Real-disk specs for FileGenerationStore. Every case uses its own unique temp directory and asserts actual file bytes.</summary>
    public static class StorageSpecs
    {
        private static readonly StorageFaultPhase[] PreReplace =
        {
            StorageFaultPhase.CreateTemp, StorageFaultPhase.WriteTemp, StorageFaultPhase.FlushTemp, StorageFaultPhase.ReReadTemp,
            StorageFaultPhase.VerifyChecksum, StorageFaultPhase.Backup, StorageFaultPhase.Replace
        };

        public static void Run()
        {
            var failures = new List<string>();
            Case(failures, nameof(RoundTripAndPhasing), RoundTripAndPhasing);
            Case(failures, nameof(EmptyAndLargePayload), EmptyAndLargePayload);
            Case(failures, nameof(GenerationsDoNotMix), GenerationsDoNotMix);
            Case(failures, nameof(FaultAtEveryPhaseKeepsOldGood), FaultAtEveryPhaseKeepsOldGood);
            Case(failures, nameof(CancelAtEveryPhaseBeforeReplace), CancelAtEveryPhaseBeforeReplace);
            Case(failures, nameof(DoubleCancelIsIdempotent), DoubleCancelIsIdempotent);
            Case(failures, nameof(CancelAfterReplaceStillSucceeds), CancelAfterReplaceStillSucceeds);
            Case(failures, nameof(TornAndCorruptTempIsRejected), TornAndCorruptTempIsRejected);
            Case(failures, nameof(PostReplaceCorruptionIsRolledBack), PostReplaceCorruptionIsRolledBack);
            Case(failures, nameof(PostReplaceIoFaultNeverMixes), PostReplaceIoFaultNeverMixes);
            Case(failures, nameof(CorruptFilesAreNotCandidates), CorruptFilesAreNotCandidates);
            Case(failures, nameof(MissingGenerationAndDirectory), MissingGenerationAndDirectory);
            Case(failures, nameof(DirectoryOccupyingPathFailsSafely), DirectoryOccupyingPathFailsSafely);
            Case(failures, nameof(ReadOnlyTargetKeepsDiskConsistent), ReadOnlyTargetKeepsDiskConsistent);
            Case(failures, nameof(RealFileLockWindows), RealFileLockWindows);
            Case(failures, nameof(RetentionPrunesOnlyOwnedFiles), RetentionPrunesOnlyOwnedFiles);
            Case(failures, nameof(ShutdownCancelsAndCleans), ShutdownCancelsAndCleans);
            Case(failures, nameof(BusyAndInterleavedWrites), BusyAndInterleavedWrites);
            Case(failures, nameof(ConstructorValidation), ConstructorValidation);
            if (failures.Count > 0)
                throw new InvalidOperationException("Storage specs failed:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception e) { failures.Add(name + ": " + e); }
        }

        // ------------------------------------------------------------------ cases

        private static void RoundTripAndPhasing()
        {
            using (var r = new Rig())
            {
                var p = Bytes(1, 5000);
                var h = r.Store.Write(1, p);
                r.Store.Tick(0.1, 0.1);
                Check.True(!h.Status.IsTerminal(), "write must not finish in one tick");
                Check.Equal(1, Directory.GetFiles(r.Box.Root, "*.tmp").Length, "temp file visible while writing");
                Check.True(!File.Exists(r.Store.PathFor(1)), "final file must not exist before Replace");
                var ticks = 1;
                while (!h.Status.IsTerminal() && ticks < 64) { r.Store.Tick(0.1, 0.1); ticks++; }
                Check.Equal(8, ticks, "one phase per tick");
                Check.Equal(OperationStatus.Succeeded, h.Status, "write status");
                Check.Equal(1L, h.Terminal.Revision, "revision is the generation");
                Check.Equal("gen-" + new string('0', 18) + "1.mcgen", Path.GetFileName(r.Store.PathFor(1)), "file name");
                var disk = File.ReadAllBytes(r.Store.PathFor(1));
                Check.True(disk.Length > p.Length, "framing adds a header");
                var tail = new byte[p.Length];
                Array.Copy(disk, disk.Length - p.Length, tail, 0, p.Length);
                Check.Bytes(p, tail, "payload stored verbatim after the header");
                Check.Equal(0, Directory.GetFiles(r.Box.Root, "*.tmp").Length, "no temp left");
                var rd = r.ReadDone(1);
                Check.Succeeded(rd, "read");
                Check.Bytes(p, rd.Value.Bytes.ToArray(), "read bytes");
                Check.Equal(Hex(p), rd.Value.Checksum, "checksum is SHA-256 of payload");
                var list = r.Store.ListCandidates();
                Check.Equal("1", Gens(list), "candidates");
                Check.Equal(0, list[0].Bytes.Length, "list results carry no bytes");
                Check.Equal(Hex(p), list[0].Checksum, "list checksum");
            }
        }

        private static void EmptyAndLargePayload()
        {
            using (var r = new Rig())
            {
                r.WriteDone(1, new byte[0]);
                Check.Equal(0, r.ReadDone(1).Value.Bytes.Length, "empty payload");
                var big = Bytes(3, 1500000);
                r.WriteDone(2, big);
                Check.Bytes(big, r.ReadDone(2).Value.Bytes.ToArray(), "large payload");
            }
        }

        private static void GenerationsDoNotMix()
        {
            using (var r = new Rig())
            {
                var p1 = Bytes(1, 100);
                var p2 = Bytes(2, 200);
                var p3 = Bytes(3, 300);
                r.WriteDone(1, p1);
                r.WriteDone(2, p2);
                r.WriteDone(3, p3);
                Check.Equal("3,2,1", Gens(r.Store.ListCandidates()), "newest first");
                Check.Bytes(p1, r.ReadDone(1).Value.Bytes.ToArray(), "gen 1");
                Check.Bytes(p2, r.ReadDone(2).Value.Bytes.ToArray(), "gen 2");
                Check.Bytes(p3, r.ReadDone(3).Value.Bytes.ToArray(), "gen 3");
            }
        }

        private static void FaultAtEveryPhaseKeepsOldGood()
        {
            foreach (var overwrite in new[] { false, true })
            {
                foreach (var phase in PreReplace)
                {
                    using (var r = new Rig())
                    {
                        var good = Bytes(10, 300);
                        r.WriteDone(1, good);
                        var before = Snap(r.Box.Root);
                        var target = overwrite ? 1L : 2L;
                        var label = (overwrite ? "overwrite/" : "new/") + phase;
                        r.Hook = c => { if (c.Phase == phase) throw new IOException("injected " + phase); };
                        var h = r.Store.Write(target, Bytes(11, 400));
                        r.Drive(h);
                        r.Hook = null;
                        Check.Failed(h, StorageCodes.IoFailed, label);
                        SameSnap(before, Snap(r.Box.Root), label);
                        Check.Equal("1", Gens(r.Store.ListCandidates()), label + " candidates");
                        Check.Bytes(good, r.ReadDone(1).Value.Bytes.ToArray(), label + " old good");
                        Check.Equal(0, r.Store.PendingOperations, label + " pending");
                    }
                }
            }
        }

        private static void CancelAtEveryPhaseBeforeReplace()
        {
            foreach (var overwrite in new[] { false, true })
            {
                foreach (var phase in PreReplace)
                {
                    using (var r = new Rig())
                    {
                        var good = Bytes(12, 250);
                        r.WriteDone(1, good);
                        var before = Snap(r.Box.Root);
                        var label = (overwrite ? "overwrite/" : "new/") + phase;
                        IHandle<FileGeneration> h = null;
                        r.Hook = c => { if (c.Phase == phase && h != null) h.Cancel("test cancel"); };
                        h = r.Store.Write(overwrite ? 1L : 2L, Bytes(13, 500));
                        r.Drive(h);
                        r.Hook = null;
                        Check.Equal(OperationStatus.Cancelled, h.Status, label + " status");
                        Check.Equal(ResultCodes.Cancelled, h.Terminal.Code, label + " code");
                        SameSnap(before, Snap(r.Box.Root), label);
                        Check.Bytes(good, r.ReadDone(1).Value.Bytes.ToArray(), label + " old good");
                    }
                }
            }
        }

        private static void DoubleCancelIsIdempotent()
        {
            using (var r = new Rig())
            {
                r.WriteDone(1, Bytes(14, 64));
                IHandle<FileGeneration> h = null;
                r.Hook = c =>
                {
                    if (c.Phase == StorageFaultPhase.WriteTemp && h != null) { h.Cancel("first"); h.Cancel("second"); }
                };
                h = r.Store.Write(2, Bytes(15, 64));
                var terminalCalls = 0;
                h.OnTerminal(res => terminalCalls++);
                r.Drive(h);
                var terminal = h.Terminal;
                h.Cancel("third after terminal");
                r.Store.Tick(0.1, 0.1);
                Check.Equal(OperationStatus.Cancelled, h.Status, "cancelled");
                Check.Equal(1, terminalCalls, "terminal callback exactly once");
                Check.True(ReferenceEquals(terminal, h.Terminal), "terminal never changes");
                Check.Equal("1", Gens(r.Store.ListCandidates()), "only gen 1");
                Check.Equal(0, Directory.GetFiles(r.Box.Root, "*.tmp").Length, "no temp");
            }
        }

        private static void CancelAfterReplaceStillSucceeds()
        {
            foreach (var overwrite in new[] { false, true })
            {
                using (var r = new Rig())
                {
                    var oldBytes = Bytes(16, 128);
                    var fresh = Bytes(17, 256);
                    r.WriteDone(1, oldBytes);
                    var target = overwrite ? 1L : 2L;
                    IHandle<FileGeneration> h = null;
                    r.Hook = c => { if (c.Phase == StorageFaultPhase.PostReplaceVerify && h != null) h.Cancel("late"); };
                    h = r.Store.Write(target, fresh);
                    r.Drive(h);
                    r.Hook = null;
                    Check.Succeeded(h, "cancel after replace");
                    Check.Equal(target, h.Value.Generation, "reports the generation actually stored");
                    Check.Bytes(fresh, h.Value.Bytes.ToArray(), "reported bytes");
                    Check.Bytes(fresh, r.ReadDone(target).Value.Bytes.ToArray(), "disk has the new bytes");
                    if (!overwrite) Check.Bytes(oldBytes, r.ReadDone(1).Value.Bytes.ToArray(), "old generation untouched");
                    Check.Equal(0, Directory.GetFiles(r.Box.Root, "*.tmp").Length, "no temp");
                }
            }
        }

        private static void TornAndCorruptTempIsRejected()
        {
            foreach (var torn in new[] { false, true })
            {
                using (var r = new Rig())
                {
                    var good = Bytes(18, 300);
                    r.WriteDone(1, good);
                    var before = Snap(r.Box.Root);
                    r.Hook = c =>
                    {
                        if (c.Phase != StorageFaultPhase.ReReadTemp) return;
                        var b = File.ReadAllBytes(c.TempPath);
                        if (torn) { var cut = new byte[b.Length / 2]; Array.Copy(b, cut, cut.Length); b = cut; }
                        else b[b.Length - 1] ^= 0xFF;
                        File.WriteAllBytes(c.TempPath, b);
                    };
                    var h = r.Store.Write(2, Bytes(19, 300));
                    r.Drive(h);
                    r.Hook = null;
                    Check.Failed(h, StorageCodes.Corrupt, torn ? "torn" : "flipped");
                    SameSnap(before, Snap(r.Box.Root), "disk after rejected candidate");
                    Check.Equal("1", Gens(r.Store.ListCandidates()), "candidates");
                }
            }
        }

        private static void PostReplaceCorruptionIsRolledBack()
        {
            foreach (var overwrite in new[] { false, true })
            {
                using (var r = new Rig())
                {
                    r.WriteDone(1, Bytes(20, 200));
                    var before = Snap(r.Box.Root);
                    r.Hook = c =>
                    {
                        if (c.Phase != StorageFaultPhase.PostReplaceVerify) return;
                        var b = File.ReadAllBytes(c.FinalPath);
                        b[b.Length - 1] ^= 0xFF;
                        File.WriteAllBytes(c.FinalPath, b);
                    };
                    var h = r.Store.Write(overwrite ? 1L : 2L, Bytes(21, 200));
                    r.Drive(h);
                    r.Hook = null;
                    Check.Failed(h, StorageCodes.Corrupt, overwrite ? "overwrite" : "new");
                    SameSnap(before, Snap(r.Box.Root), "previous state restored");
                    Check.Equal("1", Gens(r.Store.ListCandidates()), "candidates");
                }
            }
        }

        private static void PostReplaceIoFaultNeverMixes()
        {
            using (var r = new Rig())
            {
                var old = Bytes(22, 100);
                var fresh = Bytes(23, 150);
                r.WriteDone(1, old);
                r.Hook = c => { if (c.Phase == StorageFaultPhase.PostReplaceVerify) throw new IOException("injected after replace"); };
                var h = r.Store.Write(2, fresh);
                r.Drive(h);
                r.Hook = null;
                Check.Failed(h, StorageCodes.IoFailed, "post-replace fault is reported");
                Check.Bytes(old, r.ReadDone(1).Value.Bytes.ToArray(), "gen 1 intact");
                var rd = r.ReadDone(2);
                Check.Succeeded(rd, "gen 2 was already replaced and stays whole");
                Check.Bytes(fresh, rd.Value.Bytes.ToArray(), "gen 2 is entirely the new payload");
            }
        }

        private static void CorruptFilesAreNotCandidates()
        {
            using (var r = new Rig())
            {
                var unrelated = Path.Combine(r.Box.Root, "notes.txt");
                var lookalike = Path.Combine(r.Box.Root, "gen-notes.mcgen");
                File.WriteAllText(unrelated, "keep me");
                File.WriteAllText(lookalike, "keep me too");
                var p1 = Bytes(30, 120);
                r.WriteDone(1, p1);
                r.WriteDone(2, Bytes(31, 120));
                r.WriteDone(3, Bytes(32, 120));
                var f1 = File.ReadAllBytes(r.Store.PathFor(1));
                var f3 = File.ReadAllBytes(r.Store.PathFor(3));
                var torn = new byte[f3.Length / 2];
                Array.Copy(f3, torn, torn.Length);
                File.WriteAllBytes(r.Store.PathFor(3), torn);
                var f2 = File.ReadAllBytes(r.Store.PathFor(2));
                f2[f2.Length - 1] ^= 0xFF;
                File.WriteAllBytes(r.Store.PathFor(2), f2);
                File.WriteAllBytes(r.Store.PathFor(4), f1);                       // valid frame of gen 1 under gen 4's name
                var extended = new byte[f1.Length + 3];
                Array.Copy(f1, extended, f1.Length);
                File.WriteAllBytes(r.Store.PathFor(5), extended);                   // trailing garbage
                File.WriteAllBytes(r.Store.PathFor(7), new byte[0]);                // empty file
                var unfinished = Path.Combine(r.Box.Root, "gen-" + new string('0', 18) + "9." + new string('a', 32) + ".tmp");
                File.WriteAllBytes(unfinished, f1);                                 // unfinished temp of a crashed writer
                Check.Equal("1", Gens(r.Store.ListCandidates()), "only the untouched generation is a candidate");
                foreach (var bad in new long[] { 2, 3, 4, 5, 7 })
                    Check.Failed(r.ReadDone(bad), StorageCodes.Corrupt, "read of corrupt generation " + bad);
                Check.Bytes(p1, r.ReadDone(1).Value.Bytes.ToArray(), "good generation reads");
                var p6 = Bytes(33, 77);
                r.WriteDone(6, p6);
                Check.Equal("6,1", Gens(r.Store.ListCandidates()), "new write alongside corrupt files");
                Check.Equal("keep me", File.ReadAllText(unrelated), "unrelated file untouched");
                Check.Equal("keep me too", File.ReadAllText(lookalike), "look-alike file untouched");
                Check.True(File.Exists(unfinished), "foreign temp file not deleted");
            }
        }

        private static void MissingGenerationAndDirectory()
        {
            using (var box = new Sandbox())
            {
                var sub = Path.Combine(box.Root, "does-not-exist-yet");
                var factory = new FakeFactory();
                var store = new FileGenerationStore(sub, factory);
                Check.Equal(0, store.ListCandidates().Count, "no directory, no candidates");
                var h = store.Read(5);
                Drive(store, h);
                Check.Failed(h, StorageCodes.NotFound, "missing generation");
                var w = store.Write(5, Bytes(40, 10));
                Drive(store, w);
                Check.Succeeded(w, "write creates the directory");
                Check.True(File.Exists(store.PathFor(5)), "file exists");
            }
        }

        private static void DirectoryOccupyingPathFailsSafely()
        {
            using (var r = new Rig())
            {
                var good = Bytes(41, 90);
                r.WriteDone(1, good);
                var blocker = r.Store.PathFor(2);
                Directory.CreateDirectory(blocker);
                var before = Snap(r.Box.Root);
                var h = r.Store.Write(2, Bytes(42, 90));
                r.Drive(h);
                Check.Failed(h, StorageCodes.IoFailed, "real filesystem refusal");
                Check.True(Directory.Exists(blocker), "foreign directory untouched");
                SameSnap(before, Snap(r.Box.Root), "files unchanged");
                Check.Equal("1", Gens(r.Store.ListCandidates()), "candidates");
                Directory.Delete(blocker, false);
            }
        }

        private static void ReadOnlyTargetKeepsDiskConsistent()
        {
            using (var r = new Rig())
            {
                var good = Bytes(43, 120);
                var fresh = Bytes(44, 130);
                r.WriteDone(1, good);
                var path = r.Store.PathFor(1);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try
                {
                    var h = r.Store.Write(1, fresh);
                    r.Drive(h);
                    var rd = r.ReadDone(1);
                    Check.Succeeded(rd, "generation remains readable");
                    var got = rd.Value.Bytes.ToArray();
                    if (h.Status == OperationStatus.Succeeded) Check.Bytes(fresh, got, "success means the new bytes are on disk");
                    else
                    {
                        Check.Equal(OperationStatus.Failed, h.Status, "otherwise the write failed");
                        Check.Bytes(good, got, "failure means the old bytes are on disk");
                    }
                    Check.Equal(0, Directory.GetFiles(r.Box.Root, "*.tmp").Length, "no temp left");
                }
                finally
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                }
            }
        }

        private static void RealFileLockWindows()
        {
            if (!OperatingSystem.IsWindows()) return;
            using (var r = new Rig())
            {
                var good = Bytes(45, 200);
                r.WriteDone(1, good);
                var before = Snap(r.Box.Root);
                using (new FileStream(r.Store.PathFor(1), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var rd = r.Store.Read(1);
                    r.Drive(rd);
                    Check.Failed(rd, StorageCodes.IoFailed, "read of locked file");
                    Check.Equal(0, r.Store.ListCandidates().Count, "locked file is skipped");
                    var w = r.Store.Write(1, Bytes(46, 100));
                    r.Drive(w);
                    Check.Failed(w, StorageCodes.IoFailed, "write over locked file");
                }
                SameSnap(before, Snap(r.Box.Root), "after unlock");
                Check.Bytes(good, r.ReadDone(1).Value.Bytes.ToArray(), "old good readable");

                FileStream hold = null;
                try
                {
                    r.Hook = c =>
                    {
                        if (c.Phase == StorageFaultPhase.Replace) hold = new FileStream(c.FinalPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    };
                    var w2 = r.Store.Write(1, Bytes(47, 100));
                    r.Drive(w2);
                    r.Hook = null;
                    Check.Failed(w2, StorageCodes.IoFailed, "replace over locked file");
                }
                finally
                {
                    if (hold != null) hold.Dispose();
                }
                SameSnap(before, Snap(r.Box.Root), "after locked replace");
                Check.Bytes(good, r.ReadDone(1).Value.Bytes.ToArray(), "old good survives locked replace");
            }
        }

        private static void RetentionPrunesOnlyOwnedFiles()
        {
            using (var r = new Rig(2))
            {
                var unrelated = Path.Combine(r.Box.Root, "notes.txt");
                File.WriteAllText(unrelated, "keep");
                for (var g = 1L; g <= 4; g++) r.WriteDone(g, Bytes((int)g, 50));
                Check.Equal("4,3", Gens(r.Store.ListCandidates()), "newest two kept");
                Check.True(!File.Exists(r.Store.PathFor(1)) && !File.Exists(r.Store.PathFor(2)), "older files pruned");
                Check.Equal("keep", File.ReadAllText(unrelated), "unrelated file untouched");
                var old = Bytes(60, 40);
                r.WriteDone(1, old);
                Check.Bytes(old, r.ReadDone(1).Value.Bytes.ToArray(), "a generation just written is never pruned by its own write");
            }
            using (var r = new Rig(0))
            {
                for (var g = 1L; g <= 4; g++) r.WriteDone(g, Bytes((int)g, 50));
                Check.Equal("4,3,2,1", Gens(r.Store.ListCandidates()), "retention 0 keeps everything");
            }
        }

        private static void ShutdownCancelsAndCleans()
        {
            using (var r = new Rig())
            {
                var h = r.Store.Write(1, Bytes(50, 80));
                for (var i = 0; i < 3; i++) r.Store.Tick(0.1, 0.1);
                Check.Equal(1, Directory.GetFiles(r.Box.Root, "*.tmp").Length, "temp present mid-write");
                r.Store.Shutdown();
                Check.Equal(OperationStatus.Cancelled, h.Status, "cancelled by shutdown");
                Check.Equal(0, Directory.GetFiles(r.Box.Root).Length, "directory clean");
                Check.Equal(0, r.Store.PendingOperations, "nothing pending");
            }
        }

        private static void BusyAndInterleavedWrites()
        {
            using (var r = new Rig())
            {
                var a = Bytes(51, 100);
                var b = Bytes(52, 100);
                var first = r.Store.Write(1, a);
                var second = r.Store.Write(1, b);
                Check.Failed(second, ResultCodes.ResourceBusy, "same generation in flight");
                r.Drive(first);
                Check.Bytes(a, r.ReadDone(1).Value.Bytes.ToArray(), "first write wins intact");

                var ha = r.Store.Write(2, a);
                var hb = r.Store.Write(3, b);
                for (var i = 0; i < 64 && (!ha.Status.IsTerminal() || !hb.Status.IsTerminal()); i++) r.Store.Tick(0.1, 0.1);
                Check.Succeeded(ha, "interleaved A");
                Check.Succeeded(hb, "interleaved B");
                Check.Bytes(a, r.ReadDone(2).Value.Bytes.ToArray(), "gen 2 is A");
                Check.Bytes(b, r.ReadDone(3).Value.Bytes.ToArray(), "gen 3 is B");
            }
        }

        private static void ConstructorValidation()
        {
            var f = new FakeFactory();
            Check.Throws<ArgumentNullException>(() => new FileGenerationStore(null, f), "null root");
            Check.Throws<ArgumentException>(() => new FileGenerationStore("  ", f), "blank root");
            Check.Throws<ArgumentNullException>(() => new FileGenerationStore(Path.GetTempPath(), null), "null factory");
            Check.Throws<ArgumentOutOfRangeException>(() => new FileGenerationStore(Path.GetTempPath(), f, 1), "retention 1");
        }

        // ------------------------------------------------------------------ helpers

        private static byte[] Bytes(int seed, int length)
        {
            var b = new byte[length];
            new Random(seed).NextBytes(b);
            return b;
        }

        private static string Hex(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Gens(IReadOnlyList<FileGeneration> list)
        {
            var parts = new List<string>();
            foreach (var g in list) parts.Add(g.Generation.ToString());
            return string.Join(",", parts);
        }

        private static Dictionary<string, byte[]> Snap(string dir)
        {
            var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(dir)) d[Path.GetFileName(f)] = File.ReadAllBytes(f);
            return d;
        }

        private static void SameSnap(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual, string label)
        {
            Check.Equal(expected.Count, actual.Count, label + ": file count (" + string.Join(",", actual.Keys) + ")");
            foreach (var kv in expected)
            {
                Check.True(actual.TryGetValue(kv.Key, out var bytes), label + ": missing " + kv.Key);
                Check.Bytes(kv.Value, bytes, label + ": bytes of " + kv.Key);
            }
        }

        private static void Drive(FileGenerationStore store, IHandle h)
        {
            for (var i = 0; i < 64; i++)
            {
                if (h.Status.IsTerminal()) return;
                store.Tick(0.016, 0.016);
            }
            if (!h.Status.IsTerminal()) throw new InvalidOperationException("operation did not reach a terminal state");
        }

        private sealed class Rig : IDisposable
        {
            public readonly Sandbox Box = new Sandbox();
            public readonly FileGenerationStore Store;
            public Action<StorageFaultContext> Hook;

            public Rig(int retained = 3)
            {
                Store = new FileGenerationStore(Box.Root, new FakeFactory(), retained, c => { if (Hook != null) Hook(c); });
            }

            public void Drive(IHandle h) { StorageSpecs.Drive(Store, h); }

            public IHandle<FileGeneration> WriteDone(long generation, byte[] payload)
            {
                var h = Store.Write(generation, payload);
                Drive(h);
                Check.Succeeded(h, "write of generation " + generation);
                return h;
            }

            public IHandle<FileGeneration> ReadDone(long generation)
            {
                var h = Store.Read(generation);
                Drive(h);
                return h;
            }

            public void Dispose() { Box.Dispose(); }
        }

        private sealed class Sandbox : IDisposable
        {
            public readonly string Root;

            public Sandbox()
            {
                Root = Path.Combine(Path.GetTempPath(), "mc-storage-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            // Removes only what this test created, one level deep and non-recursively.
            public void Dispose()
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(Root))
                    {
                        foreach (var f in Directory.GetFiles(d)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                        Directory.Delete(d, false);
                    }
                    foreach (var f in Directory.GetFiles(Root)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                    Directory.Delete(Root, false);
                }
                catch (Exception) { }
            }
        }

        private static class Check
        {
            public static void True(bool condition, string message)
            {
                if (!condition) throw new Exception("Assertion failed: " + message);
            }

            public static void Equal<T>(T expected, T actual, string message)
            {
                if (!EqualityComparer<T>.Default.Equals(expected, actual))
                    throw new Exception(message + ": expected <" + expected + "> but was <" + actual + ">");
            }

            public static void Bytes(byte[] expected, byte[] actual, string message)
            {
                if (expected == null || actual == null || expected.Length != actual.Length)
                    throw new Exception(message + ": byte arrays differ in length");
                for (var i = 0; i < expected.Length; i++)
                    if (expected[i] != actual[i]) throw new Exception(message + ": byte arrays differ at " + i);
            }

            public static void Succeeded(IHandle h, string message)
            {
                var detail = string.Empty;
                if (h.Terminal != null) foreach (var diagnostic in h.Terminal.Details) detail += " | " + diagnostic.Message + " (" + diagnostic.Actual + ")";
                if (h.Status != OperationStatus.Succeeded)
                    throw new Exception(message + ": expected Succeeded but was " + h.Status + " " + (h.Terminal == null ? string.Empty : h.Terminal.Code) + detail);
            }

            public static void Failed(IHandle h, string code, string message)
            {
                if (h.Status != OperationStatus.Failed) throw new Exception(message + ": expected Failed but was " + h.Status);
                if (h.Terminal.Code != code) throw new Exception(message + ": expected code " + code + " but was " + h.Terminal.Code);
                if (h.Terminal.Details.Count == 0) throw new Exception(message + ": failure carries no diagnostics");
            }

            public static void Throws<TException>(Action action, string message) where TException : Exception
            {
                try { action(); }
                catch (TException) { return; }
                throw new Exception(message + ": expected " + typeof(TException).Name);
            }
        }

        private sealed class FakeFactory : IHandleFactory
        {
            public IHandleController<T> Create<T>(string operationId) { return new FakeHandle<T>(operationId); }
        }

        private sealed class FakeHandle<T> : IHandleController<T>
        {
            private readonly List<Action<Result>> callbacks = new List<Action<Result>>();
            private Action<string> onCancel;

            public FakeHandle(string operationId) { OperationId = operationId; Status = OperationStatus.Pending; }

            public string OperationId { get; }
            public OperationStatus Status { get; private set; }
            public Result Terminal { get; private set; }
            public T Value { get; private set; }

            public void Cancel(string reason)
            {
                if (Status.IsTerminal()) return;
                if (onCancel != null) onCancel(reason);
            }

            public void OnTerminal(Action<Result> callback)
            {
                if (Terminal != null) callback(Terminal);
                else callbacks.Add(callback);
            }

            public void Start() { if (Status == OperationStatus.Pending) Status = OperationStatus.Running; }
            public bool Succeed(T value, long revision) { return Finish(OperationStatus.Succeeded, ResultCodes.Ok, revision, null, value); }
            public bool Fail(string code, long revision, IReadOnlyList<Diagnostic> details) { return Finish(OperationStatus.Failed, code, revision, details, default(T)); }
            public bool Cancelled(string reason, long revision) { return Finish(OperationStatus.Cancelled, ResultCodes.Cancelled, revision, null, default(T)); }
            public void SetCancellationHandler(Action<string> handler) { onCancel = handler; }

            private bool Finish(OperationStatus status, string code, long revision, IReadOnlyList<Diagnostic> details, T value)
            {
                if (Terminal != null) return false;
                Value = value;
                Terminal = new Result(status, code, revision, details);
                Status = status;
                var pending = callbacks.ToArray();
                callbacks.Clear();
                foreach (var cb in pending) cb(Terminal);
                return true;
            }
        }
    }
}
