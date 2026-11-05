using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Contracts;
using MagicCampus.Timekeeping;
using MagicCampus.UnityAdapters.ContentIO;

namespace MagicCampus.Tests.Time
{
    /// <summary>
    /// Migration diff spec (stage 3, units 4a to 4c): the per-frame tick through ClockState.Advance must leave the same save bytes and
    /// revisions as the ClockParticipant plan it replaced. Event time costs still commit through ClockParticipant until 4c, which deletes
    /// the participant together with this spec.
    /// </summary>
    internal static class ClockParitySpecs
    {
        private const int StepsPerRoot = 1500;
        private static readonly JsonDataCodec Codec = new JsonDataCodec();

        private static readonly double[] Frames = { 1.0 / 60.0, (double)(1f / 60f), (double)0.0172f, 0.033, (double)(1f / 144f) };
        private static readonly double[] Jumps = { 1, 5, 30, 60, 90, 600.5, 1439, 1440, 1441.25, 2880.75 };

        public static void Run()
        {
            var roots = new List<KeyValuePair<string, DataNode>>();
            foreach (var dir in new[] { "Content/Fixtures/FirstConnection/generated/", "Assets/StreamingAssets/FirstConnection/" })
                foreach (var file in Directory.GetFiles(dir, "save-*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var parsed = Codec.Parse(file, File.ReadAllBytes(file));
                    Check.True(parsed.Succeeded, "parse " + file);
                    roots.Add(new KeyValuePair<string, DataNode>(file, parsed.Value.Root));
                }
            Check.Equal(12, roots.Count, "committed fixture saves");

            var integralStart = TimeWorld.BuildRoot(420, 0, new string[0], false)
                .With("clock", Nodes.Obj(Nodes.P("absoluteMinute", Nodes.Int(420)), Nodes.P("fraction", Nodes.Int(0))));
            roots.Add(new KeyValuePair<string, DataNode>("integral fraction", integralStart));
            roots.Add(new KeyValuePair<string, DataNode>("before midnight", TimeWorld.BuildRoot(1439, 0.999999, new[] { "legacy" }, false)));
            roots.Add(new KeyValuePair<string, DataNode>("pending half minute", TimeWorld.BuildRoot(1430, 0.5, new[] { "boundary.day_d1" }, true)));

            long steps = 0, commits = 0, unchanged = 0, midnights = 0, integralToReal = 0, byteDiffs = 0, revisionDiffs = 0, statusDiffs = 0;
            var firstDiff = "";
            for (var r = 0; r < roots.Count; r++)
            {
                var label = roots[r].Key;
                var plan = TimeWorld.Create(roots[r].Value, 1);
                var tick = TimeWorld.Create(roots[r].Value, 1);
                var random = new Random(4001 + r);
                for (var i = 0; i < StepsPerRoot; i++)
                {
                    var minutes = NextMinutes(random);
                    var integral = tick.Store.Current.Slice("clock").Members["fraction"].Kind == DataKind.Integer;
                    var planned = PlanTick(plan, minutes, label + " step " + i);
                    var advance = tick.Clock.Advance(minutes, PauseSet.None);
                    steps++;

                    var expected = planned == null ? ClockAdvanceStatus.ZeroDelta : ClockAdvanceStatus.Advanced;
                    if (tick.Clock.LastStatus != expected || (planned != null && !advance.To.Equals(planned.To)))
                        Diff(ref statusDiffs, ref firstDiff, label, i, "status " + tick.Clock.LastStatus + " vs " + expected);
                    if (planned != null)
                    {
                        commits++;
                        midnights += planned.Boundaries.Count;
                        if (integral) integralToReal++;
                    }
                    else if (minutes > 0) unchanged++;

                    if (plan.Store.Current.Revision != tick.Store.Current.Revision)
                        Diff(ref revisionDiffs, ref firstDiff, label, i, "revision " + plan.Store.Current.Revision + " vs " + tick.Store.Current.Revision);
                    if (!Codec.Serialize(plan.Store.Current.Root).Span.SequenceEqual(Codec.Serialize(tick.Store.Current.Root).Span))
                        Diff(ref byteDiffs, ref firstDiff, label, i, "save bytes");
                }
            }

            Console.WriteLine("ClockParity roots=" + roots.Count + " steps=" + steps + " commits=" + commits + " unchanged=" + unchanged + " midnights=" + midnights
                + " integralToReal=" + integralToReal + " byteDiffs=" + byteDiffs + " revisionDiffs=" + revisionDiffs + " statusDiffs=" + statusDiffs + firstDiff);
            Check.True(midnights > 0 && integralToReal > 0 && unchanged > 0, "the steps cross midnights, leave an integral fraction and include no-op ticks");
            Check.True(byteDiffs == 0 && revisionDiffs == 0 && statusDiffs == 0, "tick through ClockState.Advance matches the ClockParticipant plan" + firstDiff);
        }

        /// <summary>The pre-4a tick: plan from the stored time, commit a clock.advance plan only when the time moves.</summary>
        private static ClockAdvance PlanTick(TimeWorld w, double minutes, string what)
        {
            if (minutes == 0) return null;
            var step = w.Calendar.Plan(w.Calendar.FromSnapshot(w.Store.Current), minutes);
            if (step.To.Equals(step.From)) return null;
            var committed = w.Commit("plan.tick." + w.Store.Current.Revision, ClockParticipant.CreateAdvanceChange(minutes));
            Check.True(committed.Succeeded, "plan tick commits at " + what);
            return step;
        }

        private static double NextMinutes(Random random)
        {
            var roll = random.Next(100);
            if (roll < 70) return Frames[random.Next(Frames.Length)];
            if (roll < 85) return Jumps[random.Next(Jumps.Length)];
            if (roll < 93) return 1e-17;
            return 0;
        }

        private static void Diff(ref long count, ref string first, string label, int step, string what)
        {
            if (count++ == 0) first = " first=" + label + "@" + step + ":" + what;
        }
    }
}
