using System;
using MagicCampus.State;

namespace MagicCampus.Persistence
{
    public sealed class SafeSnapshot
    {
        public SafeBoundary Boundary { get; }
        public StateSnapshot Snapshot { get; }
        public bool Saved { get; }
        public SafeSnapshot(SafeBoundary boundary, StateSnapshot snapshot, bool saved)
        { Boundary = boundary; Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); Saved = saved; }
    }

    /// <summary>Decides which snapshot a save may use. Knows nothing about files.</summary>
    public interface ISafeBoundaryPolicy
    {
        void Notify(SafeBoundary boundary, StateSnapshot snapshot);
        SafeSnapshot Latest { get; }   // null until the first boundary
        void MarkSaved(long revision);
    }

    /// <summary>Resumes normal safe-boundary tracking after load or an aborted scene, without publishing scene success.</summary>
    public interface ISafeBoundaryRenewal : ISafeBoundaryPolicy
    {
        void Renew(SafeBoundary boundary, StateSnapshot snapshot, bool saved);
    }

    /// <summary>
    /// Decides which snapshot a save may use. While a scene is active (SceneEntry until SceneComplete) WorldSafe and RoomSafe
    /// notifications never replace the scene entry. Older revisions are ignored. MarkSaved only flags the matching revision.
    /// </summary>
    public sealed class SafeBoundaryPolicy : ISafeBoundaryRenewal
    {
        private readonly object gate = new object();
        private SafeSnapshot latest;
        private bool sceneActive;

        public SafeSnapshot Latest { get { lock (gate) return latest; } }

        public bool SceneActive { get { lock (gate) return sceneActive; } }

        public void Notify(SafeBoundary boundary, StateSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (gate)
            {
                if (latest != null && snapshot.Revision < latest.Snapshot.Revision) return;
                switch (boundary)
                {
                    case SafeBoundary.SceneEntry:
                        sceneActive = true;
                        break;
                    case SafeBoundary.SceneComplete:
                        sceneActive = false;
                        break;
                    case SafeBoundary.WorldSafe:
                    case SafeBoundary.RoomSafe:
                        if (sceneActive) return;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(boundary));
                }
                var keepSaved = latest != null && latest.Saved && latest.Boundary == boundary && latest.Snapshot.Revision == snapshot.Revision;
                latest = new SafeSnapshot(boundary, snapshot, keepSaved);
            }
        }

        public void MarkSaved(long revision)
        {
            lock (gate)
            {
                if (latest == null || latest.Saved || latest.Snapshot.Revision != revision) return;
                latest = new SafeSnapshot(latest.Boundary, latest.Snapshot, true);
            }
        }

        public void Renew(SafeBoundary boundary, StateSnapshot snapshot, bool saved)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (gate)
            {
                latest = new SafeSnapshot(boundary, snapshot, saved);
                sceneActive = false;
            }
        }
    }
}
