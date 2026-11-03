using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    internal sealed class BoundaryWork
    {
        internal BoundaryWork(DayBoundary boundary, IDayBoundaryConsumer consumer, PlannedChange change)
        {
            Boundary = boundary;
            Consumer = consumer;
            Change = change;
        }

        internal DayBoundary Boundary { get; }
        internal IDayBoundaryConsumer Consumer { get; }
        internal PlannedChange Change { get; }
    }

    internal static class BoundaryJobs
    {
        internal static string ConsumerKey(string consumerId, long day)
        {
            return "consumer." + consumerId + ".day_d" + day.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Asks every registered consumer, in date then id order, for its pure plan for each boundary it has not yet completed.
        /// Consumers that return null have nothing to do and are not marked. Nothing is written.
        /// </summary>
        internal static bool TryCollect(DayBoundaryConsumerRegistry consumers, StateSnapshot basis, HashSet<string> done,
            IReadOnlyList<DayBoundary> boundaries, out List<BoundaryWork> work, out List<Diagnostic> problems)
        {
            work = new List<BoundaryWork>();
            problems = new List<Diagnostic>();
            var all = consumers.Consumers;
            foreach (var boundary in boundaries)
            {
                foreach (var consumer in all)
                {
                    var key = ConsumerKey(consumer.ConsumerId, boundary.Day);
                    if (done.Contains(key)) continue;

                    PlannedChange change;
                    try
                    {
                        change = consumer.Plan(basis, boundary);
                    }
                    catch (Exception ex)
                    {
                        problems.Add(TimeDiagnostics.Make("CONSUMER_FAILED", string.Empty,
                            "consumer " + consumer.ConsumerId + " failed while planning " + boundary.Key.Value + ": " + ex.Message,
                            "a planned change or null", ex.GetType().Name));
                        continue;
                    }

                    if (change == null) continue;
                    if (change.ParticipantId == ClockParticipant.Id)
                    {
                        problems.Add(TimeDiagnostics.Make("CONSUMER_TARGETS_CLOCK", string.Empty,
                            "consumer " + consumer.ConsumerId + " may not change the clock", "a change for another participant", change.ParticipantId));
                        continue;
                    }

                    work.Add(new BoundaryWork(boundary, consumer, change));
                }
            }

            return problems.Count == 0;
        }
    }
}
