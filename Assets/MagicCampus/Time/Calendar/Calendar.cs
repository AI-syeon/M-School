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
        /// <summary>Reads /clock of the snapshot. Throws InvalidOperationException when the clock tree is malformed (no silent default).</summary>
        public GameTime FromSnapshot(StateSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var clock = snapshot.Slice("clock");
            if (clock == null || clock.Kind != DataKind.Object)
                throw new InvalidOperationException("/clock must be an object");
            if (!TryReadClock(clock, out var time, out var problem))
                throw new InvalidOperationException(problem.Message);
            return time;
        }

        public static bool TryReadClock(DataNode clock, out GameTime time, out Diagnostic problem)
        {
            time = default(GameTime);
            problem = null;
            if (clock == null || clock.Kind != DataKind.Object)
            {
                problem = TimeDiagnostics.Make("CLOCK_INVALID", "/clock", "/clock must be an object", "object", "missing or wrong kind");
                return false;
            }

            if (!clock.TryGet("absoluteMinute", out var minuteNode) || minuteNode.Kind != DataKind.Integer || minuteNode.Integer < 0)
            {
                problem = TimeDiagnostics.Make("CLOCK_INVALID", "/clock/absoluteMinute", "absoluteMinute must be a non-negative integer", "integer >= 0", "missing or invalid");
                return false;
            }

            if (!clock.TryGet("fraction", out var fractionNode) || (fractionNode.Kind != DataKind.Number && fractionNode.Kind != DataKind.Integer))
            {
                problem = TimeDiagnostics.Make("CLOCK_INVALID", "/clock/fraction", "fraction must be a number", "number in [0,1)", "missing or wrong kind");
                return false;
            }

            var fraction = fractionNode.Kind == DataKind.Integer ? (double)fractionNode.Integer : fractionNode.Number;
            if (double.IsNaN(fraction) || fraction < 0 || fraction >= 1)
            {
                problem = TimeDiagnostics.Make("CLOCK_INVALID", "/clock/fraction", "fraction must be in [0,1)", "number in [0,1)", fraction.ToString("R", CultureInfo.InvariantCulture));
                return false;
            }

            time = new GameTime(minuteNode.Integer, fraction);
            return true;
        }

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
