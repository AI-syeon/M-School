using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;
using MagicCampus.Transactions;

namespace MagicCampus.Timekeeping
{
    public readonly struct GameTime : IEquatable<GameTime>
    {
        public const int MinutesPerDay = 1440, DaysPerCycle = 80, FirstDayStart = 420;
        public long AbsoluteMinute { get; }
        public double Fraction { get; }   // [0, 1)

        public GameTime(long absoluteMinute, double fraction)
        {
            if (absoluteMinute < 0) throw new ArgumentOutOfRangeException(nameof(absoluteMinute));
            if (double.IsNaN(fraction) || fraction < 0 || fraction >= 1) throw new ArgumentOutOfRangeException(nameof(fraction));
            AbsoluteMinute = absoluteMinute; Fraction = fraction;
        }

        public long Day => AbsoluteMinute / MinutesPerDay;
        public int MinuteOfDay => (int)(AbsoluteMinute % MinutesPerDay);
        public long Year => Day / DaysPerCycle + 1;
        public int CycleDay => (int)(Day % DaysPerCycle);
        public int Season => CycleDay / 20;
        public int Term => CycleDay / 40;
        public int Weekday => (int)(Day % 5);

        public bool Equals(GameTime o) => AbsoluteMinute == o.AbsoluteMinute && Fraction == o.Fraction;
        public override bool Equals(object obj) => obj is GameTime t && Equals(t);
        public override int GetHashCode() { unchecked { return AbsoluteMinute.GetHashCode() * 31 + Fraction.GetHashCode(); } }
    }

    /// <summary>Midnight that starts Day (Day >= 1). Key is stable so a boundary is consumed at most once.</summary>
    public sealed class DayBoundary
    {
        public long Day { get; }
        public long AbsoluteMinute => Day * GameTime.MinutesPerDay;
        public CompletionKey Key => new CompletionKey("boundary.day_d" + Day.ToString(CultureInfo.InvariantCulture));
        public DayBoundary(long day) { Day = PortGuard.Min(day, 1, nameof(day)); }
    }

    public sealed class ClockAdvance
    {
        public GameTime From { get; }
        public GameTime To { get; }
        public IReadOnlyList<DayBoundary> Boundaries { get; }   // strictly ascending, every crossed midnight
        public bool ReturnPending => Boundaries.Count > 0;

        public ClockAdvance(GameTime from, GameTime to, IEnumerable<DayBoundary> boundaries)
        {
            From = from; To = to;
            Boundaries = PortGuard.Opt(boundaries, nameof(boundaries));
            for (var i = 1; i < Boundaries.Count; i++)
                if (Boundaries[i].Day <= Boundaries[i - 1].Day) throw new ArgumentException("not ascending", nameof(boundaries));
        }
    }

    public interface ICalendar
    {
        /// <summary>Pure. Used for tick advance and for event time costs; the cost is applied only inside the success commit.</summary>
        ClockAdvance Plan(GameTime from, double minutes);
    }
}
