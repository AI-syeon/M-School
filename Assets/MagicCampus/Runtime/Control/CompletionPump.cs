using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// FIFO completion queue. AdvanceTick drains a snapshot taken at entry; items posted while draining wait for the next AdvanceTick.
    /// Keyed posts are deduplicated while pending (first wins); unkeyed posts all execute. A throwing item is isolated and counted.
    /// </summary>
    public sealed class CompletionPump : ICompletionPump
    {
        private sealed class Item
        {
            public readonly string Key;
            public readonly Action Action;
            public Item(string key, Action action) { Key = key; Action = action; }
        }

        private readonly HashSet<string> pendingKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Action<Exception> onFault;
        private List<Item> pending = new List<Item>();
        private bool draining;
        private int tickCount;
        private int executed;
        private int faults;
        private int deduplicated;
        private Exception lastFault;

        public CompletionPump() : this(null) { }

        public CompletionPump(Action<Exception> onFault)
        {
            this.onFault = onFault;
        }

        public int PendingCount => pending.Count;
        public int TickCount => tickCount;
        public int ExecutedCount => executed;
        public int FaultCount => faults;
        public int DeduplicatedCount => deduplicated;
        public Exception LastFault => lastFault;
        public bool IsDraining => draining;

        public void Post(Action action)
        {
            RuntimeGuard.Required(action, nameof(action));
            pending.Add(new Item(null, action));
        }

        public void Post(CompletionKey key, Action action)
        {
            RuntimeGuard.Required(action, nameof(action));
            if (key.Value == null) throw new ArgumentException("default key", nameof(key));
            if (!pendingKeys.Add(key.Value))
            {
                deduplicated++;
                return;
            }
            pending.Add(new Item(key.Value, action));
        }

        public void AdvanceTick()
        {
            if (draining) return;
            draining = true;
            tickCount++;
            var snapshot = pending;
            pending = new List<Item>();
            pendingKeys.Clear();
            try
            {
                foreach (var item in snapshot)
                {
                    try
                    {
                        item.Action();
                        executed++;
                    }
                    catch (Exception ex)
                    {
                        faults++;
                        lastFault = ex;
                        if (onFault != null)
                        {
                            try { onFault(ex); } catch (Exception) { }
                        }
                    }
                }
            }
            finally
            {
                draining = false;
            }
        }
    }
}
