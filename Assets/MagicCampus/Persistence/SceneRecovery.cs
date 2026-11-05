using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;
using MagicCampus.World.Movement;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Scene-entry recovery only: owns the entry snapshot plus volatile actor positions (IActorWorld)
    /// and retains Accepted reservations. Not file IO and not a global room rollback.
    /// </summary>
    public interface ISceneRecovery
    {
        Result CaptureEntry(StateSnapshot acceptedSnapshot, string ownerToken);
        Outcome<StateSnapshot> RestoreEntry(string ownerToken);
        Result ClearEntry(string ownerToken);
    }

    /// <summary>
    /// Scene-entry recovery: keeps the accepted entry snapshot (unchanged, including Accepted reservations) plus the immutable actor
    /// states seen at capture, once per owner. RestoreEntry puts the actors back through IActorWorld.Restore and returns the snapshot;
    /// it never touches the state store. The runner restores the real map first and then calls GameStore.Replace itself.
    /// Room activities do not use this class.
    /// </summary>
    public sealed class SceneRecovery : ISceneRecovery
    {
        private sealed class Entry
        {
            public readonly StateSnapshot Snapshot;
            public readonly IReadOnlyList<ActorState> Actors;

            public Entry(StateSnapshot snapshot, IReadOnlyList<ActorState> actors)
            {
                Snapshot = snapshot;
                Actors = actors;
            }
        }

        private readonly object gate = new object();
        private readonly IActorWorld world;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public SceneRecovery(IActorWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public Result CaptureEntry(StateSnapshot acceptedSnapshot, string ownerToken)
        {
            if (acceptedSnapshot == null) throw new ArgumentNullException(nameof(acceptedSnapshot));
            RequireOwner(ownerToken);
            lock (gate)
            {
                if (entries.ContainsKey(ownerToken))
                    return new Result(OperationStatus.Failed, ResultCodes.ResourceBusy, acceptedSnapshot.Revision,
                        new[] { new Diagnostic("ENTRY_ALREADY_CAPTURED", "scene entry is captured once per owner; clear it first", null, "single capture", ownerToken) });
                var actors = new List<ActorState>(world.All);
                entries[ownerToken] = new Entry(acceptedSnapshot, Array.AsReadOnly(actors.ToArray()));
                return new Result(OperationStatus.Succeeded, ResultCodes.Ok, acceptedSnapshot.Revision, null);
            }
        }

        public Outcome<StateSnapshot> RestoreEntry(string ownerToken)
        {
            RequireOwner(ownerToken);
            Entry entry;
            lock (gate)
            {
                if (!entries.TryGetValue(ownerToken, out entry))
                    return Outcome<StateSnapshot>.Fail(ResultCodes.RefNotFound, 0,
                        new Diagnostic("ENTRY_NOT_CAPTURED", "no scene entry captured for this owner", null, "captured entry", ownerToken));
            }
            var restored = world.Restore(entry.Actors);
            if (restored.Status != OperationStatus.Succeeded)
            {
                var d = new List<Diagnostic>(restored.Details);
                if (d.Count == 0) d.Add(new Diagnostic("ACTOR_RESTORE_FAILED", "actor world refused the entry positions", null, "restored actors", restored.Code));
                return Outcome<StateSnapshot>.Fail(restored.Code, restored.Revision, d.ToArray());
            }
            return Outcome<StateSnapshot>.Ok(entry.Snapshot, entry.Snapshot.Revision);
        }

        public Result ClearEntry(string ownerToken)
        {
            RequireOwner(ownerToken);
            lock (gate) entries.Remove(ownerToken);
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }

        private static void RequireOwner(string ownerToken)
        {
            if (string.IsNullOrWhiteSpace(ownerToken)) throw new ArgumentException("owner token required", nameof(ownerToken));
        }
    }
}
