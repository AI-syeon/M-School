using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    /// <summary>Limits shared by the calendar, the clock service and the clock participant.</summary>
    public static class CalendarLimits
    {
        /// <summary>A fraction this close below the next whole minute is carried, absorbing double round-off without losing or duplicating time.</summary>
        public const double FractionEpsilon = 1e-9;

        /// <summary>One advance may cross at most this many midnights; larger costs are refused instead of looping.</summary>
        public const int MaxDaysPerAdvance = 4000;

        public const double MaxMinutesPerAdvance = GameTime.MinutesPerDay * (double)MaxDaysPerAdvance;
    }

    internal static class TimeDiagnostics
    {
        internal static Diagnostic Make(string code, string pointer, string message, string expected, string actual)
        {
            return new Diagnostic(code, message, new SourceLocation(string.Empty, pointer ?? string.Empty, "clock", string.Empty, 0), expected, actual);
        }

        internal static Diagnostic[] ToArray(IReadOnlyList<Diagnostic> list)
        {
            return new List<Diagnostic>(list).ToArray();
        }
    }
}
