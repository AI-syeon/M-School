using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Asynchronous save/load over the injected file store. One operation at a time: a second call is refused with RESOURCE_BUSY.
    /// Disk failure never changes the in-memory state. Completion callbacks are delivered by the handle factory on the next tick.
    /// </summary>
    public sealed class SaveService : ISaveService
    {
        private readonly object gate = new object();
        private readonly ISafeBoundaryRenewal policy;
        private readonly SaveCodec codec;
        private readonly IFileGenerationStore files;
        private readonly SaveRestorer restorer;
        private readonly GameStore store;
        private readonly ICatalogProvider catalogs;
        private readonly IHandleFactory handles;
        private bool busy;
        private long sequence;
        private long lastKnownGeneration;
        private long lastSavedRevision = -1;
        private Result lastSave;
        private Result lastLoad;

        public SaveService(ISafeBoundaryRenewal policy, SaveCodec codec, IFileGenerationStore files, SaveRestorer restorer,
            GameStore store, ICatalogProvider catalogs, IHandleFactory handles)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.files = files ?? throw new ArgumentNullException(nameof(files));
            this.restorer = restorer ?? throw new ArgumentNullException(nameof(restorer));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
            this.handles = handles ?? throw new ArgumentNullException(nameof(handles));
        }

        /// <summary>Last finished save (null before the first); for the unsaved indicator and retry UI.</summary>
        public Result LastSaveResult { get { lock (gate) return lastSave; } }
        /// <summary>Last finished load (null before the first); Details lists rejected newer generations on fallback.</summary>
        public Result LastLoadResult { get { lock (gate) return lastLoad; } }
        public long LastKnownGeneration { get { lock (gate) return lastKnownGeneration; } }
        /// <summary>State revision of the last successful write, or -1.</summary>
        public long LastSavedRevision { get { lock (gate) return lastSavedRevision; } }

        private bool TryBegin(out long seq)
        {
            lock (gate)
            {
                seq = ++sequence;
                if (busy) return false;
                busy = true;
                return true;
            }
        }

        private IHandleController<FileGeneration> Refuse(string id, string what)
        {
            var h = handles.Create<FileGeneration>(id);
            h.Start();
            h.Fail(ResultCodes.ResourceBusy, 0, new[] { new Diagnostic("OPERATION_IN_PROGRESS", what + " refused: another save or load is in progress", null, "idle", "busy") });
            return h;
        }

        // ------------------------------------------------------------------ save

        public IHandle<FileGeneration> SaveLatestSafe()
        {
            long seq;
            if (!TryBegin(out seq)) return Refuse("save." + seq, "save");
            var h = handles.Create<FileGeneration>("save." + seq);
            h.Start();

            var latest = policy.Latest;
            if (latest == null)
                return FailSave(h, ResultCodes.ConditionFalse, 0, new Diagnostic("NO_SAFE_BOUNDARY", "no safe boundary has been reached yet", null, "safe snapshot", "none"));

            var rev = latest.Snapshot.Revision;

            long highest;
            lock (gate) highest = lastKnownGeneration;
            foreach (var g in files.ListCandidates())
                if (g.Generation > highest) highest = g.Generation;
            var generation = highest + 1;
            lock (gate) lastKnownGeneration = generation;

            ReadOnlyMemory<byte> bytes;
            try
            {
                bytes = codec.Encode(codec.Seal(latest.Snapshot.State, latest.Boundary, generation));
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                return FailSave(h, ResultCodes.ContentInvalid, rev, new Diagnostic("SEAL_FAILED", e.Message, null, "complete save v2 snapshot", "invalid"));
            }

            var child = files.Write(generation, bytes);
            string cancelReason = null;
            h.SetCancellationHandler(reason =>
            {
                cancelReason = reason;
                child.Cancel(reason);
            });
            child.OnTerminal(r =>
            {
                if (r.Status == OperationStatus.Succeeded)
                {
                    var stored = child.Value;
                    lock (gate)
                    {
                        busy = false;
                        if (stored.Generation > lastKnownGeneration) lastKnownGeneration = stored.Generation;
                        lastSavedRevision = rev;
                        lastSave = new Result(OperationStatus.Succeeded, ResultCodes.Ok, rev, null);
                    }
                    policy.MarkSaved(rev);
                    h.Succeed(stored, rev);
                }
                else if (r.Status == OperationStatus.Cancelled)
                {
                    lock (gate)
                    {
                        busy = false;
                        lastSave = new Result(OperationStatus.Cancelled, ResultCodes.Cancelled, rev, null);
                    }
                    h.Cancelled(cancelReason ?? "write cancelled", rev);
                }
                else
                {
                    lock (gate)
                    {
                        busy = false;
                        lastSave = new Result(OperationStatus.Failed, r.Code, rev, r.Details);
                    }
                    h.Fail(r.Code, rev, r.Details);
                }
            });
            return h;
        }

        private IHandle<FileGeneration> FailSave(IHandleController<FileGeneration> h, string code, long revision, params Diagnostic[] details)
        {
            lock (gate)
            {
                busy = false;
                lastSave = new Result(OperationStatus.Failed, code, revision, details);
            }
            h.Fail(code, revision, details);
            return h;
        }

        // ------------------------------------------------------------------ load

        public IHandle<FileGeneration> LoadLatest()
        {
            long seq;
            if (!TryBegin(out seq)) return Refuse("load." + seq, "load");
            var h = handles.Create<FileGeneration>("load." + seq);
            h.Start();

            var catalog = catalogs.Current;
            if (catalog == null)
                return FailLoad(h, ResultCodes.ContentInvalid, 0, new Diagnostic("NO_CATALOG", "no valid catalog is installed", null, "catalog", "none"));
            var candidates = files.ListCandidates();
            if (candidates.Count == 0)
                return FailLoad(h, ResultCodes.ConditionFalse, 0, new Diagnostic("NO_SAVE_FOUND", "no valid save generation exists", null, "save generation", "none"));
            new LoadRun(this, h, catalog, candidates, seq).Next();
            return h;
        }

        private IHandle<FileGeneration> FailLoad(IHandleController<FileGeneration> h, string code, long revision, params Diagnostic[] details)
        {
            lock (gate)
            {
                busy = false;
                lastLoad = new Result(OperationStatus.Failed, code, revision, details);
            }
            h.Fail(code, revision, details);
            return h;
        }

        private sealed class LoadRun
        {
            private readonly SaveService owner;
            private readonly IHandleController<FileGeneration> h;
            private readonly ICatalog catalog;
            private readonly IReadOnlyList<FileGeneration> candidates;
            private readonly long seq;
            private readonly List<Diagnostic> skipped = new List<Diagnostic>();
            private int index;
            private string firstCode;
            private IHandle<FileGeneration> child;
            private bool cancelRequested;
            private string cancelReason;

            public LoadRun(SaveService owner, IHandleController<FileGeneration> h, ICatalog catalog, IReadOnlyList<FileGeneration> candidates, long seq)
            {
                this.owner = owner; this.h = h; this.catalog = catalog; this.candidates = candidates; this.seq = seq;
                h.SetCancellationHandler(reason =>
                {
                    cancelRequested = true;
                    cancelReason = reason;
                    var c = child;
                    if (c != null) c.Cancel(reason);
                });
            }

            public void Next()
            {
                if (index >= candidates.Count)
                {
                    owner.FailLoad(h, firstCode ?? ResultCodes.ContentInvalid, 0, skipped.ToArray());
                    return;
                }
                var cand = candidates[index];
                var c = owner.files.Read(cand.Generation);
                child = c;
                c.OnTerminal(r => OnRead(cand.Generation, c, r));
            }

            private void OnRead(long generation, IHandle<FileGeneration> c, Result r)
            {
                if (cancelRequested)
                {
                    lock (owner.gate)
                    {
                        owner.busy = false;
                        owner.lastLoad = new Result(OperationStatus.Cancelled, ResultCodes.Cancelled, 0, null);
                    }
                    h.Cancelled(cancelReason ?? "load cancelled", 0);
                    return;
                }
                if (r.Status != OperationStatus.Succeeded)
                {
                    Skip(generation, "READ_FAILED", r.Code, r.Details);
                    return;
                }
                var fg = c.Value;
                var dec = owner.codec.Decode(fg.Bytes);
                if (!dec.Succeeded) { Skip(generation, "DECODE_FAILED", dec.Result.Code, dec.Result.Details); return; }
                if (dec.Value.Generation != generation)
                {
                    Skip(generation, "GENERATION_MISMATCH", ResultCodes.ContentInvalid, new[] { new Diagnostic("GENERATION_MISMATCH", "file generation differs from the listed generation", null, generation.ToString(), dec.Value.Generation.ToString()) });
                    return;
                }
                var res = owner.restorer.Restore(dec.Value, catalog);
                if (!res.Succeeded) { Skip(generation, "RESTORE_FAILED", res.Result.Code, res.Result.Details); return; }

                var tx = new TransactionId("load." + seq + ".g" + generation);
                var rep = owner.store.Replace(res.Value, tx);
                if (!rep.Succeeded)
                {
                    var d = new List<Diagnostic>(rep.Result.Details);
                    d.AddRange(skipped);
                    owner.FailLoad(h, rep.Result.Code, rep.Result.Revision, d.ToArray());
                    return;
                }
                var loaded = rep.Value;
                owner.policy.Renew(SaveDocument.SafePoint(loaded.State), loaded, true);
                lock (owner.gate)
                {
                    owner.busy = false;
                    if (generation > owner.lastKnownGeneration) owner.lastKnownGeneration = generation;
                    owner.lastSavedRevision = loaded.Revision;
                    owner.lastLoad = new Result(OperationStatus.Succeeded, ResultCodes.Ok, loaded.Revision, skipped);
                }
                h.Succeed(fg, loaded.Revision);
            }

            private void Skip(long generation, string stage, string code, IReadOnlyList<Diagnostic> details)
            {
                if (firstCode == null) firstCode = code;
                skipped.Add(new Diagnostic("GENERATION_REJECTED", "generation " + generation + " rejected at " + stage, null, "restorable generation", code));
                skipped.AddRange(details);
                index++;
                Next();
            }
        }
    }
}
