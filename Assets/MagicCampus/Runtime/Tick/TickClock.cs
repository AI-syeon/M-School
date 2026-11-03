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
            var entry = new Delayed { Due = simulation + seconds, Handle = handle };
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
    }
}
