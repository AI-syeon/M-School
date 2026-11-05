using System;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.State;

namespace MagicCampus.Tests.PureDomain.Support
{
    /// <summary>Test views of a snapshot as the save tree it would write, and snapshots built from test trees.</summary>
    internal static class SnapshotTrees
    {
        public static DataNode Tree(this StateSnapshot snapshot) => SaveDocument.Write(snapshot.State);

        /// <summary>Top-level save field of the written tree, or null when absent.</summary>
        public static DataNode Slice(this StateSnapshot snapshot, string name) => snapshot.Tree().TryGet(name, out var v) ? v : null;

        public static GameState State(DataNode tree)
        {
            var read = SaveDocument.Read(tree);
            if (!read.Succeeded) throw new InvalidOperationException("test tree is not a readable state: " + read.Result.Details[0].Location.Pointer + " " + read.Result.Details[0].Message);
            return read.Value;
        }

        public static StateSnapshot Snapshot(long revision, DataNode tree) => new StateSnapshot(revision, State(tree));
    }
}
