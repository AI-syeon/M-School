using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MagicCampus.Contracts;

namespace MagicCampus.UnityAdapters.Storage
{
    /// <summary>
    /// Disk implementation of IFileGenerationStore. One file per generation: gen-&lt;19 digit generation&gt;.mcgen inside the root
    /// directory. A write never touches other generations, so a failed or cancelled write cannot mix state.
    /// Write phases (one per Tick): CreateTemp, WriteTemp, FlushTemp, ReReadTemp, VerifyChecksum, Backup, Replace, PostReplaceVerify.
    /// Cancellation takes effect at the next phase boundary and only before Replace; after Replace the handle succeeds.
    /// Single-threaded: Write/Read/ListCandidates/Tick/Shutdown must be called from the same thread (Cancel may come from anywhere).
    /// Only files this class names (gen-*.mcgen, its .bak and its own .tmp) are ever created, replaced or deleted; no recursive deletes.
    /// </summary>
    public sealed class FileGenerationStore : IFileGenerationStore, IRuntimeTick
    {
        private const string FilePrefix = "gen-";
        private const string FileExtension = ".mcgen";
        private const int GenerationDigits = 19;

        private static readonly StorageFaultPhase[] WritePhases =
        {
            StorageFaultPhase.CreateTemp, StorageFaultPhase.WriteTemp, StorageFaultPhase.FlushTemp, StorageFaultPhase.ReReadTemp,
            StorageFaultPhase.VerifyChecksum, StorageFaultPhase.Backup, StorageFaultPhase.Replace, StorageFaultPhase.PostReplaceVerify
        };

        private static readonly StorageFaultPhase[] ReadPhases = { StorageFaultPhase.ReadFile, StorageFaultPhase.VerifyRead };

        private readonly string root;
        private readonly IHandleFactory handles;
        private readonly int retained;
        private readonly Action<StorageFaultContext> hook;
        private readonly List<Operation> active = new List<Operation>();

        /// <param name="rootDirectory">Directory that holds the generation files (created on first write).</param>
        /// <param name="handleFactory">Creates the handles returned by Write/Read.</param>
        /// <param name="retainedGenerations">0 keeps every generation; otherwise at least 2. After a verified write, generations older than the newest N verified ones are deleted.</param>
        /// <param name="faultHook">Test seam called before each phase; null by default (no faults).</param>
        public FileGenerationStore(string rootDirectory, IHandleFactory handleFactory, int retainedGenerations = 3, Action<StorageFaultContext> faultHook = null)
        {
            if (rootDirectory == null) throw new ArgumentNullException(nameof(rootDirectory));
            if (rootDirectory.Trim().Length == 0) throw new ArgumentException("must not be empty", nameof(rootDirectory));
            if (handleFactory == null) throw new ArgumentNullException(nameof(handleFactory));
            if (retainedGenerations != 0 && retainedGenerations < 2) throw new ArgumentOutOfRangeException(nameof(retainedGenerations));
            root = Path.GetFullPath(rootDirectory);
            handles = handleFactory;
            retained = retainedGenerations;
            hook = faultHook;
        }

        public int PendingOperations => active.Count;

        public string PathFor(long generation)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            return Path.Combine(root, FilePrefix + generation.ToString("D" + GenerationDigits, CultureInfo.InvariantCulture) + FileExtension);
        }

        private string BackupPathFor(long generation) => PathFor(generation) + ".bak";

        private string NewTempPathFor(long generation)
            => Path.Combine(root, FilePrefix + generation.ToString("D" + GenerationDigits, CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        public IHandle<FileGeneration> Write(long generation, ReadOnlyMemory<byte> fileBytes)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            var payload = fileBytes.ToArray();
            var handle = handles.Create<FileGeneration>("storage.write.g" + generation.ToString(CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N"));
            handle.Start();
            foreach (var other in active)
            {
                if (other.IsWrite && other.Generation == generation)
                {
                    handle.Fail(ResultCodes.ResourceBusy, generation, new[]
                    {
                        new Diagnostic(ResultCodes.ResourceBusy, "a write of this generation is already in progress", null, "idle", "writing")
                    });
                    return handle;
                }
            }
            var op = new Operation(this, true, generation, handle, payload, NewTempPathFor(generation), PathFor(generation), BackupPathFor(generation));
            handle.SetCancellationHandler(op.RequestCancel);
            active.Add(op);
            return handle;
        }

        public IHandle<FileGeneration> Read(long generation)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            var handle = handles.Create<FileGeneration>("storage.read.g" + generation.ToString(CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N"));
            handle.Start();
            var op = new Operation(this, false, generation, handle, null, null, PathFor(generation), null);
            handle.SetCancellationHandler(op.RequestCancel);
            active.Add(op);
            return handle;
        }

        /// <summary>Newest first; only files whose frame, generation and checksum verify. Bytes are empty in the results.</summary>
        public IReadOnlyList<FileGeneration> ListCandidates()
        {
            var result = new List<FileGeneration>();
            foreach (var g in ListGenerationNumbers())
            {
                if (TryReadVerified(g, out _, out var checksum))
                    result.Add(new FileGeneration(g, checksum, ReadOnlyMemory<byte>.Empty));
            }
            return result.AsReadOnly();
        }

        /// <summary>Advances every active operation by exactly one phase. Operations started during this tick wait for the next one.</summary>
        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (active.Count == 0) return;
            var snapshot = active.ToArray();
            foreach (var op in snapshot)
            {
                if (op.Step()) active.Remove(op);
            }
        }

        /// <summary>Cancels operations that have not replaced yet (cleaning their temp files) and finishes those that already replaced.</summary>
        public void Shutdown()
        {
            var snapshot = active.ToArray();
            foreach (var op in snapshot)
            {
                op.RequestCancel("store shut down");
                while (!op.Step()) { }
                active.Remove(op);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static bool TryParseName(string fileName, out long generation)
        {
            generation = 0;
            if (fileName.Length != FilePrefix.Length + GenerationDigits + FileExtension.Length) return false;
            if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) || !fileName.EndsWith(FileExtension, StringComparison.Ordinal)) return false;
            var digits = fileName.Substring(FilePrefix.Length, GenerationDigits);
            foreach (var c in digits)
                if (c < '0' || c > '9') return false;
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out generation);
        }

        private List<long> ListGenerationNumbers()
        {
            var gens = new List<long>();
            string[] files;
            try { files = Directory.GetFiles(root, FilePrefix + "*"); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return gens; }
            foreach (var f in files)
                if (TryParseName(Path.GetFileName(f), out var g)) gens.Add(g);
            gens.Sort((a, b) => b.CompareTo(a));
            return gens;
        }

        private bool TryReadVerified(long generation, out byte[] payload, out string checksum)
        {
            payload = null;
            checksum = null;
            try
            {
                var bytes = File.ReadAllBytes(PathFor(generation));
                return GenerationFraming.TryDecode(bytes, generation, out payload, out checksum) == null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private bool IsActive(long generation)
        {
            foreach (var op in active)
                if (op.Generation == generation) return true;
            return false;
        }

        private void Prune()
        {
            if (retained == 0) return;
            var valid = 0;
            foreach (var g in ListGenerationNumbers())
            {
                if (valid < retained)
                {
                    if (TryReadVerified(g, out _, out _)) valid++;
                    continue;
                }
                if (IsActive(g)) continue;
                TryDelete(PathFor(g));
                TryDelete(BackupPathFor(g));
            }
        }

        private static void TryDelete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        private static bool TryCopy(string from, string to)
        {
            try { File.Copy(from, to, true); return true; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }

        // ------------------------------------------------------------------ one operation

        private sealed class Operation
        {
            private readonly FileGenerationStore owner;
            private readonly IHandleController<FileGeneration> handle;
            private readonly byte[] payload;
            private readonly StorageFaultPhase[] phases;
            private readonly string tempPath;
            private readonly string finalPath;
            private readonly string backupPath;
            private int index;
            private FileStream stream;
            private byte[] frame;
            private byte[] readBack;
            private string checksum;
            private bool replaced;
            private bool backupMade;
            private volatile bool cancelRequested;
            private string cancelReason = string.Empty;

            internal Operation(FileGenerationStore owner, bool isWrite, long generation, IHandleController<FileGeneration> handle,
                byte[] payload, string tempPath, string finalPath, string backupPath)
            {
                this.owner = owner;
                IsWrite = isWrite;
                Generation = generation;
                this.handle = handle;
                this.payload = payload;
                this.tempPath = tempPath;
                this.finalPath = finalPath;
                this.backupPath = backupPath;
                phases = isWrite ? WritePhases : ReadPhases;
            }

            internal bool IsWrite { get; }
            internal long Generation { get; }

            internal void RequestCancel(string reason)
            {
                if (!cancelRequested) cancelReason = reason ?? string.Empty;
                cancelRequested = true;
            }

            /// <summary>Runs one phase. Returns true once the handle reached a terminal state.</summary>
            internal bool Step()
            {
                var phase = phases[index];
                try
                {
                    owner.hook?.Invoke(new StorageFaultContext(phase, Generation, tempPath, finalPath));
                    if (cancelRequested && !replaced)
                    {
                        ReleaseUnreplaced();
                        handle.Cancelled(cancelReason, Generation);
                        return true;
                    }
                    Execute(phase);
                }
                catch (Exception ex)
                {
                    ReleaseUnreplaced();
                    var code = Classify(ex);
                    handle.Fail(code, Generation, new[] { new Diagnostic(code, ex.Message, null, string.Empty, ex.GetType().Name) });
                    return true;
                }
                index++;
                return index >= phases.Length;
            }

            private string Classify(Exception ex)
            {
                if (ex is InvalidDataException) return StorageCodes.Corrupt;
                if (!IsWrite && (ex is FileNotFoundException || ex is DirectoryNotFoundException)) return StorageCodes.NotFound;
                return StorageCodes.IoFailed;
            }

            private void Execute(StorageFaultPhase phase)
            {
                switch (phase)
                {
                    case StorageFaultPhase.CreateTemp:
                        frame = GenerationFraming.Encode(Generation, payload, out checksum);
                        Directory.CreateDirectory(owner.root);
                        stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                        break;
                    case StorageFaultPhase.WriteTemp:
                        stream.Write(frame, 0, frame.Length);
                        break;
                    case StorageFaultPhase.FlushTemp:
                        stream.Flush(true);
                        stream.Dispose();
                        stream = null;
                        break;
                    case StorageFaultPhase.ReReadTemp:
                        readBack = File.ReadAllBytes(tempPath);
                        break;
                    case StorageFaultPhase.VerifyChecksum:
                        VerifyWritten(readBack);
                        readBack = null;
                        break;
                    case StorageFaultPhase.Backup:
                        if (Directory.Exists(finalPath)) throw new IOException("a directory occupies the generation path");
                        if (File.Exists(finalPath))
                        {
                            backupMade = true;
                            File.Copy(finalPath, backupPath, true);
                        }
                        break;
                    case StorageFaultPhase.Replace:
                        if (File.Exists(finalPath)) File.Replace(tempPath, finalPath, null);
                        else File.Move(tempPath, finalPath);
                        replaced = true;
                        break;
                    case StorageFaultPhase.PostReplaceVerify:
                        PostReplaceVerify();
                        break;
                    case StorageFaultPhase.ReadFile:
                        readBack = File.ReadAllBytes(finalPath);
                        break;
                    case StorageFaultPhase.VerifyRead:
                        var error = GenerationFraming.TryDecode(readBack, Generation, out var body, out var sum);
                        if (error != null) throw new InvalidDataException(error);
                        handle.Succeed(new FileGeneration(Generation, sum, body), Generation);
                        break;
                }
            }

            private string CheckWritten(byte[] bytes)
            {
                var error = GenerationFraming.TryDecode(bytes, Generation, out var body, out var sum);
                if (error != null) return error;
                if (sum != checksum || !GenerationFraming.SameBytes(body, payload)) return "content differs from the requested payload";
                return null;
            }

            private void VerifyWritten(byte[] bytes)
            {
                var error = CheckWritten(bytes);
                if (error != null) throw new InvalidDataException(error);
            }

            private void PostReplaceVerify()
            {
                var error = CheckWritten(File.ReadAllBytes(finalPath));
                if (error != null)
                {
                    RollbackFinal();
                    throw new InvalidDataException("stored file failed verification: " + error);
                }
                try { owner.Prune(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                handle.Succeed(new FileGeneration(Generation, checksum, payload), Generation);
            }

            // The new file is in place but unreadable/invalid: put the previous bytes back, or remove the file we just created.
            private void RollbackFinal()
            {
                if (backupMade)
                {
                    if (TryCopy(backupPath, finalPath)) TryDelete(backupPath);
                }
                else
                {
                    TryDelete(finalPath);
                }
            }

            // Failure or cancellation before Replace: close and delete our temp, leave the old final file exactly as it was.
            private void ReleaseUnreplaced()
            {
                try { if (stream != null) stream.Dispose(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                stream = null;
                if (!IsWrite) return;
                TryDelete(tempPath);
                if (replaced || !backupMade) return;
                if (!File.Exists(finalPath)) TryCopy(backupPath, finalPath);
                if (File.Exists(finalPath)) TryDelete(backupPath);
            }
        }
    }
}
