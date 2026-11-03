using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Application.Schedules
{
    /// <summary>
    /// Drives ambient NPC movement from the current schedule destination through the common move service.
    /// It never preempts a foreground lease: a leased NPC's ambient move is cancelled and the controller waits for the
    /// move to become terminal before anything else may take over. It never teleports. Repeated failures toward the
    /// same target back off exponentially (bounded); a different target is tried immediately.
    /// </summary>
    public sealed class ScheduleController : IRuntimeTick
    {
        private const string YieldReason = "foreground-lease";
        private const string TakeoverReason = "foreground-takeover";
        private const double MoveTimeoutSeconds = 120;
        private const double ReevaluateSeconds = 0.5;
        private const double BackoffBaseSeconds = 1;
        private const double BackoffMaxSeconds = 30;
        private const string OwnerTokenPrefix = "ambient:";

        private sealed class NpcState
        {
            public IHandle<MoveArrival> Move;
            public Destination Target;
            public bool Yielding;
            public double NextEvalAt;
            public string FailedKey = "";
            public int FailCount;
            public double NextAttemptAt;
            public Result Last;
        }

        private sealed class StopOp
        {
            public int Remaining;
            public bool CancelRequested;
            public IHandleController<bool> Handle;
        }

        private readonly IScheduleService schedule;
        private readonly IClockService clock;
        private readonly IPauseService pause;
        private readonly IActorWorld world;
        private readonly IMoveService move;
        private readonly IHandleFactory handles;
        private readonly Func<string, bool> foregroundLeased;
        private readonly string[] npcIds;
        private readonly Dictionary<string, NpcState> states = new Dictionary<string, NpcState>(StringComparer.Ordinal);
        private double now;

        public ScheduleController(IScheduleService schedule, IClockService clock, IPauseService pause, IActorWorld world, IMoveService move,
            IHandleFactory handles, IEnumerable<string> npcIds, Func<string, bool> foregroundLeased)
        {
            this.schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.move = move ?? throw new ArgumentNullException(nameof(move));
            this.handles = handles ?? throw new ArgumentNullException(nameof(handles));
            this.foregroundLeased = foregroundLeased ?? throw new ArgumentNullException(nameof(foregroundLeased));
            if (npcIds == null) throw new ArgumentNullException(nameof(npcIds));

            var sorted = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var id in npcIds) sorted.Add(IdGuard.Require(id, nameof(npcIds)));
            this.npcIds = new List<string>(sorted).ToArray();
            foreach (var id in this.npcIds) states[id] = new NpcState();
        }

        public bool IsMoving(string npcId)
        {
            return states.TryGetValue(npcId, out var st) && st.Move != null;
        }

        /// <summary>Last terminal move result or planning failure for the NPC, or null.</summary>
        public Result LastResult(string npcId)
        {
            return states.TryGetValue(npcId, out var st) ? st.Last : null;
        }

        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (double.IsNaN(unpausedSeconds) || double.IsInfinity(unpausedSeconds) || unpausedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(unpausedSeconds));
            now += unpausedSeconds;
            var ambientOpen = !pause.Effective.Ambient;

            foreach (var npc in npcIds)
            {
                var st = states[npc];
                if (st.Move != null) Observe(st);
                if (st.Move != null)
                {
                    if (!st.Yielding && foregroundLeased(npc))
                    {
                        st.Yielding = true;
                        st.Move.Cancel(YieldReason);
                    }
                    continue;
                }
                if (!ambientOpen || foregroundLeased(npc)) continue;
                Plan(npc, st);
            }
        }

        /// <summary>
        /// Cancels the ambient moves of every managed NPC named in resources. The returned handle succeeds only after all of
        /// those moves are terminal, so a foreground owner can take the actors afterwards. Matches the stage stop callback.
        /// </summary>
        public IHandle StopActors(IReadOnlyList<string> resources)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            var controller = handles.Create<bool>("schedule.stop");
            controller.Start();

            var waits = new List<IHandle>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resource in resources)
            {
                if (resource == null || !seen.Add(resource) || !states.TryGetValue(resource, out var st) || st.Move == null) continue;
                if (!st.Yielding)
                {
                    st.Yielding = true;
                    st.Move.Cancel(TakeoverReason);
                }
                waits.Add(st.Move);
            }

            if (waits.Count == 0)
            {
                controller.Succeed(true, 0);
                return controller;
            }

            var op = new StopOp { Remaining = waits.Count, Handle = controller };
            controller.SetCancellationHandler(reason => op.CancelRequested = true);
            foreach (var wait in waits)
            {
                wait.OnTerminal(result =>
                {
                    op.Remaining--;
                    if (op.Remaining > 0) return;
                    if (op.CancelRequested) op.Handle.Cancelled("stop cancelled", 0);
                    else op.Handle.Succeed(true, 0);
                });
            }
            return controller;
        }

        private void Observe(NpcState st)
        {
            if (!st.Move.Status.IsTerminal()) return;
            var result = st.Move.Terminal;
            var target = st.Target;
            st.Move = null;
            st.Yielding = false;
            st.Last = result;

            if (result.Status == OperationStatus.Succeeded)
            {
                st.FailedKey = "";
                st.FailCount = 0;
            }
            else if (result.Status == OperationStatus.Failed && result.Code != ResultCodes.Cancelled && target != null)
            {
                RecordFailure(st, KeyOf(target));
            }
        }

        private void RecordFailure(NpcState st, string key)
        {
            if (st.FailedKey == key) st.FailCount++;
            else { st.FailedKey = key; st.FailCount = 1; }
            var delay = BackoffBaseSeconds * Math.Pow(2, st.FailCount - 1);
            st.NextAttemptAt = now + Math.Min(delay, BackoffMaxSeconds);
        }

        private void Plan(string npc, NpcState st)
        {
            if (now < st.NextEvalAt) return;
            st.NextEvalAt = now + ReevaluateSeconds;

            var actor = world.Find(npc);
            if (actor == null)
            {
                st.Last = new Result(OperationStatus.Failed, ResultCodes.RefNotFound, 0,
                    new[] { new Diagnostic(ResultCodes.RefNotFound, "actor is not in the world", null, "known actor", npc) });
                return;
            }

            var outcome = schedule.CurrentDestination(npc, clock.Now);
            if (!outcome.Succeeded)
            {
                st.Last = outcome.Result;
                return;
            }

            var destination = outcome.Value;
            if (new MoveArrival(actor.Position, destination).Arrived)
            {
                st.FailedKey = "";
                st.FailCount = 0;
                return;
            }

            var key = KeyOf(destination);
            if (key == st.FailedKey && now < st.NextAttemptAt) return;

            var handle = move.Move(new MoveRequest(npc, destination, OwnerTokenPrefix + npc, MoveTimeoutSeconds, true));
            if (handle == null) throw new InvalidOperationException("move service returned no handle");
            st.Target = destination;
            st.Yielding = false;
            st.Move = handle;
        }

        private static string KeyOf(Destination d)
        {
            return d.Target.MapId + "|" + d.Target.X.ToString("R", CultureInfo.InvariantCulture) + "|" + d.Target.Y.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
