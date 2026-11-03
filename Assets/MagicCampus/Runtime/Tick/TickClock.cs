using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// Tick clock. Advance(simulationDelta, unpausedDelta): UnpausedSeconds always accumulates the second delta (watchdog clock),
    /// SimulationSeconds only the first. Delays complete only from simulation time, ordered by due time then creation order.
    /// ITickClock.Tick and IRuntimeTick.Tick share a name, so both are explicit interface members; use TickCount / Advance directly.
    /// </summary>
    public sealed class TickClock : ITickClock, IRuntimeTick
    {
        private const double Epsilon = 1e-9;

        private sealed class Delayed
        {
            public double Due;
            public long Seq;
            public string Owner;
            public IHandleController<object> Handle;
        }

        private readonly IHandleFactory factory;
        private readonly List<Delayed> delays = new List<Delayed>();
        private long tick;
        private double simulation;
        private double unpaused;
        private long seq;

        public TickClock(IHandleFactory factory)
        {
            this.factory = RuntimeGuard.Required(factory, nameof(factory));
        }

        public long TickCount => tick;
        public int PendingDelayCount => delays.Count;
        public double SimulationSeconds => simulation;
        public double UnpausedSeconds => unpaused;

        long ITickClock.Tick => tick;

        void IRuntimeTick.Tick(double simulationSeconds, double unpausedSeconds)
        {
            Advance(simulationSeconds, unpausedSeconds);
        }

        public void Advance(double simulationSeconds, double unpausedSeconds)
        {
            RuntimeGuard.NonNegative(simulationSeconds, nameof(simulationSeconds));
            RuntimeGuard.NonNegative(unpausedSeconds, nameof(unpausedSeconds));
            tick++;
            simulation += simulationSeconds;
            unpaused += unpausedSeconds;
            while (delays.Count > 0 && delays[0].Due <= simulation + Epsilon)
            {
                var d = delays[0];
                delays.RemoveAt(0);
                d.Handle.Succeed(null, 0);
            }
        }

        public IHandle Delay(double seconds, string ownerToken)
        {
            RuntimeGuard.NonNegative(seconds, nameof(seconds));
            RuntimeGuard.Id(ownerToken, nameof(ownerToken));
            seq++;
            var handle = factory.Create<object>("delay." + seq);
            handle.Start();
            if (seconds == 0)
            {
                handle.Succeed(null, 0);
                return handle;
            }
            var entry = new Delayed { Due = simulation + seconds, Seq = seq, Owner = ownerToken, Handle = handle };
            handle.SetCancellationHandler(reason =>
            {
                delays.Remove(entry);
                handle.Cancelled(reason, 0);
            });
            var index = delays.Count;
            for (var i = 0; i < delays.Count; i++)
            {
                if (delays[i].Due > entry.Due) { index = i; break; }
            }
            delays.Insert(index, entry);
            return handle;
        }

        /// <summary>Cancels every pending delay of the owner; returns how many were cancelled.</summary>
        public int CancelOwner(string ownerToken)
        {
            RuntimeGuard.Id(ownerToken, nameof(ownerToken));
            var mine = new List<Delayed>();
            foreach (var d in delays)
                if (d.Owner == ownerToken) mine.Add(d);
            foreach (var d in mine) d.Handle.Cancel("owner cancelled");
            return mine.Count;
        }
    }

    /// <summary>Timeout policy independent of pause: measured on ITickClock.UnpausedSeconds.</summary>
    public sealed class Watchdog
    {
        private readonly ITickClock clock;
        private readonly double deadline;

        public Watchdog(ITickClock clock, double timeoutSeconds)
        {
            this.clock = RuntimeGuard.Required(clock, nameof(clock));
            deadline = clock.UnpausedSeconds + RuntimeGuard.Positive(timeoutSeconds, nameof(timeoutSeconds));
        }

        public bool Expired => clock.UnpausedSeconds + 1e-9 >= deadline;
        public double RemainingSeconds => Math.Max(0, deadline - clock.UnpausedSeconds);
    }

    /// <summary>
    /// Deterministic per-frame order: drain the completion pump first (callbacks of earlier ticks, so completions are always next-tick),
    /// then run the steps in the supplied order. Re-entrant Tick throws.
    /// </summary>
    public sealed class TickDriver : IRuntimeTick
    {
        private readonly IRuntimeTick[] steps;
        private readonly ICompletionPump pump;
        private bool running;

        public TickDriver(IEnumerable<IRuntimeTick> orderedSteps, ICompletionPump pump)
        {
            RuntimeGuard.Required(orderedSteps, nameof(orderedSteps));
            this.pump = RuntimeGuard.Required(pump, nameof(pump));
            var list = new List<IRuntimeTick>(orderedSteps);
            foreach (var s in list)
                if (s == null) throw new ArgumentException("null step", nameof(orderedSteps));
            steps = list.ToArray();
        }

        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (running) throw new InvalidOperationException("re-entrant tick");
            RuntimeGuard.NonNegative(simulationSeconds, nameof(simulationSeconds));
            RuntimeGuard.NonNegative(unpausedSeconds, nameof(unpausedSeconds));
            running = true;
            try
            {
                pump.AdvanceTick();
                foreach (var step in steps) step.Tick(simulationSeconds, unpausedSeconds);
            }
            finally
            {
                running = false;
            }
        }
    }
}
