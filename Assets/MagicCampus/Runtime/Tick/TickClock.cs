using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// Frame counter and the clock behind the event `wait` command. Advance(seconds) counts one tick and moves the delay clock;
    /// the caller passes 0 while the Scene channel is paused, so waits stall with it. Delays complete in due-time order, then creation order.
    /// </summary>
    public sealed class TickClock
    {
        private const double Epsilon = 1e-9;

        private sealed class Delayed
        {
            public double Due;
            public IHandleController<object> Handle;
        }

        private readonly IHandleFactory factory;
        private readonly List<Delayed> delays = new List<Delayed>();
        private double now;
        private long seq;

        public TickClock(IHandleFactory factory)
        {
            this.factory = PortGuard.Required(factory, nameof(factory));
        }

        public long TickCount { get; private set; }

        public void Advance(double seconds)
        {
            PortGuard.NonNegative(seconds, nameof(seconds));
            TickCount++;
            now += seconds;
            while (delays.Count > 0 && delays[0].Due <= now + Epsilon)
            {
                var d = delays[0];
                delays.RemoveAt(0);
                d.Handle.Succeed(null, 0);
            }
        }

        public IHandle Delay(double seconds)
        {
            PortGuard.NonNegative(seconds, nameof(seconds));
            seq++;
            var handle = factory.Create<object>("delay." + seq);
            handle.Start();
            if (seconds == 0)
            {
                handle.Succeed(null, 0);
                return handle;
            }
            var entry = new Delayed { Due = now + seconds, Handle = handle };
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
