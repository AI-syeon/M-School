using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// FIFO completion queue. AdvanceTick drains a snapshot taken at entry; items posted while draining wait for the next AdvanceTick.
    /// A throwing item is isolated and counted.
    /// </summary>
    public sealed class CompletionPump : ICompletionQueue
    {
        private readonly Action<Exception> onFault;
        private List<Action> pending = new List<Action>();
        private bool draining;
        private int tickCount;
        private int executed;
        private int faults;
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
        public Exception LastFault => lastFault;
        public bool IsDraining => draining;

        public void Post(Action action)
        {
            PortGuard.Required(action, nameof(action));
            pending.Add(action);
        }

        public void AdvanceTick()
        {
            if (draining) return;
            draining = true;
            tickCount++;
            var snapshot = pending;
            pending = new List<Action>();
            try
            {
                foreach (var action in snapshot)
                {
                    try
                    {
                        action();
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
