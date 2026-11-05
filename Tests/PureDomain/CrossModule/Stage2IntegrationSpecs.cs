using System;
using System.IO;
using System.Runtime.InteropServices;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Runtime;
using MagicCampus.UnityAdapters.Storage;

namespace MagicCampus.Tests.CrossModule
{
    // Codex review regression: exercise actual sibling implementations together.
    public static class Stage2IntegrationSpecs
    {
        public static void Run()
        {
            var bytes = new byte[] { 1, 2, 3 };
            var envelope = new SaveEnvelope(2, new string('a', 64), 1, new string('b', 64), bytes);
            if (MemoryMarshal.TryGetArray(envelope.Payload, out ArraySegment<byte> exposed)) exposed.Array[exposed.Offset] = 99;
            Check(envelope.Payload.Span[0] == 1, "envelope cannot expose its backing array");
            var generation = new FileGeneration(1, new string('b', 64), bytes);
            if (MemoryMarshal.TryGetArray(generation.Bytes, out ArraySegment<byte> exposedFile)) exposedFile.Array[exposedFile.Offset] = 99;
            Check(generation.Bytes.Span[0] == 1, "file generation cannot expose its backing array");

            var parent = Path.GetFullPath("Tests/Temporary");
            Directory.CreateDirectory(parent);
            var directory = Path.GetFullPath(Path.Combine(parent, "stage2-integration-" + Guid.NewGuid().ToString("N")));
            Check(directory.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "test directory scope");
            Directory.CreateDirectory(directory);
            try
            {
                var pump = new CompletionPump();
                var handles = new HandleFactory(pump);
                IHandle<FileGeneration> active = null;
                var store = new FileGenerationStore(directory, handles, 3, context => {
                    if (context.Generation == 2 && context.Phase == StorageFaultPhase.PostReplaceVerify) active.Cancel("after actual replace");
                    if (context.Generation == 3 && context.Phase == StorageFaultPhase.Backup) { active.Cancel("before replace"); active.Cancel("duplicate"); }
                });
                active = store.Write(1, bytes);
                Drive(store, active);
                Check(active.Status == OperationStatus.Succeeded, "initial real write");
                active = store.Write(2, new byte[] { 4, 5, 6 });
                var callbacks = 0;
                active.OnTerminal(_ => callbacks++);
                Drive(store, active);
                Check(active.Status == OperationStatus.Succeeded && active.Value.Generation == 2, "late cancel retains committed generation");
                Check(callbacks == 0, "completion is deferred to next tick");
                pump.AdvanceTick();
                Check(callbacks == 1, "one completion callback");
                active = store.Write(3, new byte[] { 7, 8, 9 });
                Drive(store, active);
                Check(active.Status == OperationStatus.Cancelled, "early duplicate cancel terminates once");
                Check(store.ListCandidates()[0].Generation == 2, "early cancel keeps last good disk generation");
                Check(handles.LiveCount == 0, "no handle leak across disk cancellation");
            }
            finally
            {
                // Only this verified unique directory; no recursive deletion or path enumeration across shells.
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory, false);
            }
        }

        static void Drive(FileGenerationStore store, IHandle handle)
        {
            for (var i = 0; i < 32 && !handle.Status.IsTerminal(); i++) store.Tick();
            Check(handle.Status.IsTerminal(), "bounded storage termination");
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
