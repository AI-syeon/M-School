using System;
using MagicCampus.Contracts;
using MagicCampus.Timekeeping;

namespace MagicCampus.Tests.Time
{
    internal static class CalendarSpecs
    {
        public static void Run()
        {
            Midnight();
            MultipleDays();
            YearSeasonTermWeekday();
            FractionAccumulation();
            InvalidCosts();
            SnapshotRead();
        }

        private static void Midnight()
        {
            var cal = new Calendar();
            var cross = cal.Plan(new GameTime(1430, 0), 20);
            Check.Equal(1, cross.Boundaries.Count, "one midnight crossed");
            Check.Equal(1L, cross.Boundaries[0].Day, "boundary day 1");
            Check.Equal("boundary.day_d1", cross.Boundaries[0].Key.Value, "boundary key");
            Check.Equal(1450L, cross.To.AbsoluteMinute, "target minute");
            Check.True(cross.ReturnPending, "return pending on crossed midnight");

            var exact = cal.Plan(new GameTime(1439, 0), 1);
            Check.Equal(1, exact.Boundaries.Count, "landing on midnight crosses it");
            Check.Equal(0, exact.To.MinuteOfDay, "midnight minute of day");
            Check.Equal(1L, exact.To.Day, "new day");

            var before = cal.Plan(new GameTime(1380, 0), 59.5);
            Check.Equal(0, before.Boundaries.Count, "just before midnight crosses nothing");
            Check.Equal(1439L, before.To.AbsoluteMinute, "minute before midnight");
            Check.Equal(0.5, before.To.Fraction, "half minute");

            var viaFraction = cal.Plan(new GameTime(1439, 0.75), 0.5);
            Check.Equal(1, viaFraction.Boundaries.Count, "fraction carry crosses midnight");
            Check.Equal(1440L, viaFraction.To.AbsoluteMinute, "carried minute");
            Check.Equal(0.25, viaFraction.To.Fraction, "carried fraction");

            var zero = cal.Plan(new GameTime(500, 0.25), 0);
            Check.True(zero.To.Equals(zero.From), "zero cost is identity");
            Check.Equal(0, zero.Boundaries.Count, "zero cost crosses nothing");

            var first = new GameTime(GameTime.FirstDayStart, 0);
            Check.Equal(0L, first.Day, "first day index");
            Check.Equal(1L, first.Year, "first year");
        }

        private static void MultipleDays()
        {
            var cal = new Calendar();
            var advance = cal.Plan(new GameTime(420, 0), 3 * 1440);
            Check.Equal(3, advance.Boundaries.Count, "three midnights");
            for (var i = 0; i < 3; i++)
                Check.Equal((long)(i + 1), advance.Boundaries[i].Day, "ascending day " + (i + 1));
            Check.Equal(420L + 3 * 1440, advance.To.AbsoluteMinute, "target after three days");

            var mixed = cal.Plan(new GameTime(1000, 0.5), 1440 * 2 + 600.25);
            Check.Equal(3, mixed.Boundaries.Count, "1000 + 2880 + 600 crosses three midnights");
            Check.Equal(2L, mixed.Boundaries[1].Day, "second boundary day");
            Check.Equal(3L, mixed.Boundaries[2].Day, "third boundary day");
        }

        private static void YearSeasonTermWeekday()
        {
            var cal = new Calendar();
            var lastDay = new GameTime(79L * 1440 + 1000, 0);
            Check.Equal(1L, lastDay.Year, "year 1 on day 79");
            Check.Equal(79, lastDay.CycleDay, "cycle day 79");
            Check.Equal(3, lastDay.Season, "last season");
            Check.Equal(1, lastDay.Term, "second term");

            var wrapped = cal.Plan(lastDay, 1440).To;
            Check.Equal(80L, wrapped.Day, "day 80");
            Check.Equal(2L, wrapped.Year, "year 2 after 80 days");
            Check.Equal(0, wrapped.CycleDay, "cycle restarts");
            Check.Equal(0, wrapped.Season, "season restarts");
            Check.Equal(0, wrapped.Term, "term restarts");

            var season = cal.Plan(new GameTime(19L * 1440 + 1400, 0), 100);
            Check.Equal(0, season.From.Season, "season before");
            Check.Equal(1, season.To.Season, "season after day 20");
            Check.Equal(20L, season.Boundaries[0].Day, "season boundary day");

            var term = cal.Plan(new GameTime(39L * 1440 + 1439, 0), 1);
            Check.Equal(0, term.From.Term, "term before");
            Check.Equal(1, term.To.Term, "term after day 40");
            Check.Equal(2, term.To.Season, "season at day 40");

            var weekday = cal.Plan(new GameTime(4L * 1440 + 1439, 0), 1);
            Check.Equal(4, weekday.From.Weekday, "weekday on day 4");
            Check.Equal(0, weekday.To.Weekday, "weekday wraps after five days");
        }

        private static void FractionAccumulation()
        {
            var cal = new Calendar();
            var t = new GameTime(420, 0);
            for (var i = 0; i < 60; i++)
            {
                var next = cal.Plan(t, 1.0 / 60.0).To;
                Check.True(next.AbsoluteMinute > t.AbsoluteMinute || next.Fraction > t.Fraction, "monotonic step " + i);
                t = next;
            }

            Check.Equal(421L, t.AbsoluteMinute, "sixty 1/60 steps make one minute");
            Check.True(t.Fraction < 1e-9, "no leftover after one minute");

            t = new GameTime(420, 0);
            for (var i = 0; i < 3600; i++) t = cal.Plan(t, 1.0 / 60.0).To;
            Check.Equal(480L, t.AbsoluteMinute, "3600 steps make 60 minutes");
            Check.True(t.Fraction < 1e-9, "no leftover after an hour");

            t = new GameTime(0, 0);
            for (var i = 0; i < 600; i++) t = cal.Plan(t, 0.1).To;
            Check.Equal(60L, t.AbsoluteMinute, "600 x 0.1 minutes");
            Check.True(t.Fraction < 1e-9, "0.1 accumulation leftover");

            t = new GameTime(0, 0);
            for (var i = 0; i < 3; i++) t = cal.Plan(t, 1.0 / 3.0).To;
            Check.Equal(1L, t.AbsoluteMinute, "three thirds make one minute");

            var nearWhole = cal.Plan(new GameTime(0, 0), 59.99999999999999);
            Check.Equal(60L, nearWhole.To.AbsoluteMinute, "round-off just below a whole minute carries");

            var half = cal.Plan(new GameTime(10, 0.5), 0.25).To;
            Check.Equal(10L, half.AbsoluteMinute, "fraction without carry keeps the minute");
            Check.Equal(0.75, half.Fraction, "fraction addition");
        }

        private static void InvalidCosts()
        {
            var cal = new Calendar();
            var start = new GameTime(420, 0);
            var bad = new[] { -1.0, -0.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity, CalendarLimits.MaxMinutesPerAdvance + 1 };
            foreach (var minutes in bad)
            {
                Check.Throws<ArgumentOutOfRangeException>(() => cal.Plan(start, minutes), "Plan rejects " + minutes);
                Check.True(!cal.TryPlan(start, minutes, out var advance, out var problem), "TryPlan fails for " + minutes);
                Check.True(advance == null, "no partial advance for " + minutes);
                Check.Equal("CLOCK_MINUTES_INVALID", problem.Code, "diagnostic code for " + minutes);
            }

            var limit = cal.Plan(start, CalendarLimits.MaxMinutesPerAdvance);
            Check.Equal(CalendarLimits.MaxDaysPerAdvance, limit.Boundaries.Count, "limit crosses exactly the maximum days");

            var nearMax = new GameTime(long.MaxValue - 1, 0);
            Check.True(!cal.TryPlan(nearMax, 5, out var overflow, out var overflowProblem), "overflow refused");
            Check.True(overflow == null, "no overflow advance");
            Check.Equal("CLOCK_OVERFLOW", overflowProblem.Code, "overflow diagnostic");
        }

        private static void SnapshotRead()
        {
            var cal = new Calendar();
            var ok = new StateSnapshot(1, TimeWorld.BuildRoot(900, 0.5, new string[0], false));
            var time = cal.FromSnapshot(ok);
            Check.Equal(900L, time.AbsoluteMinute, "snapshot minute");
            Check.Equal(0.5, time.Fraction, "snapshot fraction");

            var integerFraction = new StateSnapshot(1, Nodes.Obj(Nodes.P("clock", Nodes.Obj(Nodes.P("absoluteMinute", Nodes.Int(5)), Nodes.P("fraction", Nodes.Int(0))))));
            Check.Equal(0.0, cal.FromSnapshot(integerFraction).Fraction, "integer zero fraction accepted");

            var broken = new[]
            {
                Nodes.Obj(),
                Nodes.Obj(Nodes.P("clock", Nodes.Int(1))),
                Nodes.Obj(Nodes.P("clock", Nodes.Obj(Nodes.P("absoluteMinute", DataNode.FromNumber(5.5)), Nodes.P("fraction", DataNode.FromNumber(0))))),
                Nodes.Obj(Nodes.P("clock", Nodes.Obj(Nodes.P("absoluteMinute", Nodes.Int(-1)), Nodes.P("fraction", DataNode.FromNumber(0))))),
                Nodes.Obj(Nodes.P("clock", Nodes.Obj(Nodes.P("absoluteMinute", Nodes.Int(5)), Nodes.P("fraction", DataNode.FromNumber(1.0))))),
                Nodes.Obj(Nodes.P("clock", Nodes.Obj(Nodes.P("absoluteMinute", Nodes.Int(5)))))
            };
            foreach (var root in broken)
            {
                var snapshot = new StateSnapshot(1, root);
                Check.Throws<InvalidOperationException>(() => cal.FromSnapshot(snapshot), "malformed clock is explicit");
            }
        }
    }
}
