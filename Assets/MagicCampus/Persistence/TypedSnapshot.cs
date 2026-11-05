using System;
using System.Runtime.CompilerServices;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Bridge from the tree-based snapshot to the typed state. SaveDocument.Read runs once per snapshot instance and the outcome
    /// (state or refusal) is remembered for that instance. Removed with the tree-based store when GameStore takes over.
    /// </summary>
    public static class TypedSnapshot
    {
        private static readonly ConditionalWeakTable<StateSnapshot, Outcome<GameState>> Cache = new ConditionalWeakTable<StateSnapshot, Outcome<GameState>>();

        /// <summary>The typed state of the snapshot; throws InvalidOperationException when its tree is not a readable save v2 state.</summary>
        public static GameState TypedState(this StateSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var read = Cache.GetValue(snapshot, s => SaveDocument.Read(s.Root));
            if (read.Succeeded) return read.Value;
            var why = read.Result.Details[0];
            throw new InvalidOperationException("snapshot is not a readable save state: " + why.Location.Pointer + " " + why.Message);
        }

        /// <summary>Same read without the exception: false with the first refusal diagnostic when the tree is not readable.</summary>
        public static bool TryTypedState(this StateSnapshot snapshot, out GameState state, out Diagnostic problem)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var read = Cache.GetValue(snapshot, s => SaveDocument.Read(s.Root));
            state = read.Value;
            problem = read.Succeeded ? null : read.Result.Details[0];
            return read.Succeeded;
        }
    }
}
