using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// Test and probe aid (decision D9): runs the full save validation on every committed state and reports each refused one.
    /// Normal play does not attach it; there the save and load boundaries are the only validation.
    /// </summary>
    public static class CommitValidation
    {
        public static void Watch(GameStore store, ISaveValidator validator, Action<string> invalid)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            if (invalid == null) throw new ArgumentNullException(nameof(invalid));
            store.OnCommitted(c =>
            {
                var where = "rev " + c.Revision + " txn " + c.TransactionId.Value;
                IReadOnlyList<Diagnostic> problems;
                try { problems = validator.Validate(SaveDocument.Write(c.Snapshot.State)); }
                catch (Exception e) { invalid(where + " could not be validated: " + e.GetType().Name + ": " + e.Message); return; }
                if (problems.Count == 0) return;
                var first = problems[0];
                invalid(where + " fails full validation (" + problems.Count + "): "
                    + first.Code + " " + (first.Location == null ? "" : first.Location.Pointer + " ") + first.Message);
            });
        }
    }
}
