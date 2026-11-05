using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    /// <summary>
    /// Pure calendar arithmetic. Time is an absolute game-minute (first day starts at 420) plus a fraction in [0,1).
    /// Plan never mutates anything and lists every crossed midnight in ascending date order.
    /// </summary>
    public sealed class Calendar : ICalendar
    {
        /// <summary>Validates a cost or delta in game-minutes: finite, not negative, within CalendarLimits.</summary>
        public static bool TryCheckMinutes(double minutes, out Diagnostic problem)
        {
            problem = null;
            if (double.IsNaN(minutes) || double.IsInfinity(minutes))
            {
                problem = TimeDiagnostics.Make("CLOCK_MINUTES_INVALID", string.Empty, "minutes must be finite", "finite number", minutes.ToString("R", CultureInfo.InvariantCulture));
                return false;
            }

            if (minutes < 0)
            {
                problem = TimeDiagnostics.Make("CLOCK_MINUTES_INVALID", string.Empty, "minutes must not be negative", ">= 0", minutes.ToString("R", CultureInfo.InvariantCulture));
                return false;
            }

            if (minutes > CalendarLimits.MaxMinutesPerAdvance)
            {
                problem = TimeDiagnostics.Make("CLOCK_MINUTES_INVALID", string.Empty, "minutes exceed the per-advance limit", "<= " + CalendarLimits.MaxMinutesPerAdvance.ToString("R", CultureInfo.InvariantCulture), minutes.ToString("R", CultureInfo.InvariantCulture));
                return false;
            }

            return true;
        }

        /// <summary>Pure. Throws ArgumentOutOfRangeException for negative, non-finite, over-limit or overflowing costs; nothing is clamped.</summary>
        public ClockAdvance Plan(GameTime from, double minutes)
        {
            if (!TryPlan(from, minutes, out var advance, out var problem))
                throw new ArgumentOutOfRangeException(nameof(minutes), problem.Message);
            return advance;
        }

        public bool TryPlan(GameTime from, double minutes, out ClockAdvance advance, out Diagnostic problem)
        {
            advance = null;
            if (!TryCheckMinutes(minutes, out problem)) return false;

            if (minutes == 0)
            {
                advance = new ClockAdvance(from, from, null);
                return true;
            }

            var whole = Math.Floor(minutes);
            var part = minutes - whole;   // exact in binary floating point
            var wholeMinutes = (long)whole;
            if (from.AbsoluteMinute > long.MaxValue - wholeMinutes - 1)
            {
                problem = TimeDiagnostics.Make("CLOCK_OVERFLOW", "/clock/absoluteMinute", "absolute minute would overflow", "fits in a 64-bit minute counter", from.AbsoluteMinute.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            var absolute = from.AbsoluteMinute + wholeMinutes;
            var fraction = from.Fraction + part;
            if (fraction >= 1.0 - CalendarLimits.FractionEpsilon)
            {
                absolute++;
                fraction = Math.Max(0.0, fraction - 1.0);
            }

            var to = new GameTime(absolute, fraction);
            var boundaries = new List<DayBoundary>();
            for (var day = from.Day + 1; day <= to.Day; day++) boundaries.Add(new DayBoundary(day));
            advance = new ClockAdvance(from, to, boundaries);
            return true;
        }
    }
}
