using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;
using MagicCampus.World.Navigation;

namespace MagicCampus.World.Movement
{
    /// <summary>
    /// Common mover for player, NPC schedule and scenes. It is the only writer of simulation positions (through IActorWorld).
    /// Path: portal route, footprint A* per map, swept continuous steps at actor speed, portal crossing only after the actor actually
    /// stands on Portal.From and the target map is loaded. Arrival is judged from the actual position only. Failures never teleport.
    /// Called from the runtime tick on one thread. Tick(simulationSeconds, unpausedSeconds): the first value moves actors when the
    /// matching pause channel is open, the second always advances the timeout watchdog.
    /// </summary>
    public sealed class MoveService : IMoveService, IRuntimeTick
    {
        private enum Phase { Walking, Loading, RollingBack, SourceLoading }

        private enum ReplanResult { Planned, Deferred, Terminated }

        private sealed class Failure
        {
            public Failure(string code, string diagnosticCode, string message)
            {
                Code = code;
                Details = new List<Diagnostic> { Diag(diagnosticCode, message) };
            }

            public string Code { get; }
            public List<Diagnostic> Details { get; }
        }

        private sealed class ActiveMove
        {
            public ActiveMove(string operationId, MoveRequest request, IHandleController<MoveArrival> handle, Position start, bool visible)
            {
                OperationId = operationId;
                Request = request;
                Handle = handle;
                Visible = visible;
                Last = start;
                MapId = start.MapId;
                Waypoints = new Position[0];
            }

            public readonly string OperationId;
            public readonly MoveRequest Request;
            public readonly IHandleController<MoveArrival> Handle;
            public readonly bool Visible;
            public Phase State;
            public Position Last;
            public string MapId;
            public IReadOnlyList<Position> Waypoints;
            public int Next;
            public Portal Hop;
            public long NavRevision;
            public int Replans;
            public double Elapsed;
            public double BlockedSeconds;
            public double Cooldown;
            public IHandle Load;
            public int LoadToken;
            public string LoadFrom;
            public Position PendingArrival;
            public Failure Pending;
            public bool Finished;
            public double HeadingX;
            public double HeadingY;
        }

        private readonly IActorWorld actors;
        private readonly ICollisionPort collision;
        private readonly IMapPort maps;
        private readonly IMapGeometry geometry;
        private readonly IHandleFactory handles;
        private readonly IPauseService pauses;
        private readonly Func<string, bool> isSceneOwner;
        private readonly string playerActorId;
        private readonly MoveOptions options;
        private readonly Func<long> revisionSource;
        private readonly Func<MoveRequest, bool> requiresVisibleMap;
        private readonly GridPathPlanner planner;
        private readonly PortalRouter router;
        private readonly Dictionary<string, ActiveMove> byActor = new Dictionary<string, ActiveMove>(StringComparer.Ordinal);
        private readonly List<ActiveMove> ordered = new List<ActiveMove>();
        private readonly Queue<MoveTraceEntry> trace = new Queue<MoveTraceEntry>();
        private long sequence;
        private long traceSequence;

        /// <param name="isSceneOwner">Decides from the owner token whether a move is scene-owned; scene-owned moves follow the Scene pause channel.</param>
        /// <param name="playerActorId">Actor that obeys the Player channel; every other actor obeys Ambient.</param>
        /// <param name="revisionSource">Optional state revision stamped on terminal results; 0 when absent.</param>
        /// <param name="requiresVisibleMap">Per request: true (default when null) means the move drives the displayed map (source map is loaded first and every
        /// portal hop loads its target map). False means a logical move on any known map: no global map load, ActiveMapId is never changed.</param>
        public MoveService(IActorWorld actors, ICollisionPort collision, IMapPort maps, IMapGeometry geometry,
            IHandleFactory handles, IPauseService pauses, Func<string, bool> isSceneOwner = null,
            string playerActorId = "player", MoveOptions options = null, Func<long> revisionSource = null,
            Func<MoveRequest, bool> requiresVisibleMap = null)
        {
            this.actors = actors ?? throw new ArgumentNullException(nameof(actors));
            this.collision = collision ?? throw new ArgumentNullException(nameof(collision));
            this.maps = maps ?? throw new ArgumentNullException(nameof(maps));
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            this.handles = handles ?? throw new ArgumentNullException(nameof(handles));
            this.pauses = pauses ?? throw new ArgumentNullException(nameof(pauses));
            if (string.IsNullOrEmpty(playerActorId)) throw new ArgumentException("player actor id is required", nameof(playerActorId));
            this.playerActorId = playerActorId;
            this.isSceneOwner = isSceneOwner ?? (token => false);
            this.options = options ?? MoveOptions.Default;
            this.revisionSource = revisionSource;
            this.requiresVisibleMap = requiresVisibleMap;
            planner = new GridPathPlanner(collision, geometry, this.options.Navigation);
            router = new PortalRouter(maps, geometry);
        }

        public IReadOnlyList<MoveDebugInfo> ActiveMoves
        {
            get
            {
                var list = new List<MoveDebugInfo>();
                foreach (var m in ordered)
                    list.Add(new MoveDebugInfo(m.OperationId, m.Request.ActorId, m.Request.OwnerToken, m.Last, m.State.ToString(),
                        Math.Max(0, m.Waypoints.Count - m.Next), m.Replans, m.Elapsed, m.HeadingX, m.HeadingY));
                return list.AsReadOnly();
            }
        }

        public IReadOnlyList<MoveTraceEntry> Trace => Array.AsReadOnly(trace.ToArray());

        public IHandle<MoveArrival> Move(MoveRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var visible = requiresVisibleMap == null || requiresVisibleMap(request);
            var operationId = "move." + request.ActorId + "." + (++sequence).ToString(CultureInfo.InvariantCulture);
            var handle = handles.Create<MoveArrival>(operationId);
            handle.Start();

            var actor = actors.Find(request.ActorId);
            if (actor == null)
                return Refuse(handle, operationId, request, new Failure(ResultCodes.RefNotFound, "ACTOR_UNKNOWN", "actor " + request.ActorId + " is not in the world"));
            if (byActor.ContainsKey(request.ActorId))
                return Refuse(handle, operationId, request, new Failure(ResultCodes.ResourceBusy, "ACTOR_BUSY", "actor " + request.ActorId + " already has a running move"));

            var onActive = string.Equals(actor.Position.MapId, maps.ActiveMapId, StringComparison.Ordinal);
            if ((!visible || !onActive) && geometry.Find(actor.Position.MapId) == null)
                return Refuse(handle, operationId, request, new Failure(ResultCodes.NoRoute, "ACTOR_MAP_UNKNOWN", "actor map " + actor.Position.MapId + " is not a known map"));
            if (visible && !onActive)
                return BeginSourceLoad(handle, operationId, request, actor);

            var here = new MoveArrival(actor.Position, request.Destination);
            if (here.Arrived && collision.IsPassable(actor.Position, actor.Radius))
            {
                Record(operationId, request.ActorId, "succeeded", "already within tolerance");
                handle.Succeed(here, Rev());
                return handle;
            }

            var move = new ActiveMove(operationId, request, handle, actor.Position, visible);
            var failure = PlanLeg(move, actor);
            if (failure != null) return Refuse(handle, operationId, request, failure);

            byActor.Add(request.ActorId, move);
            ordered.Add(move);
            handle.SetCancellationHandler(reason => CancelMove(move, reason));
            Record(operationId, request.ActorId, "started", request.Destination.Target.ToString());
            return handle;
        }

        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (double.IsNaN(simulationSeconds) || double.IsInfinity(simulationSeconds) || simulationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
            if (double.IsNaN(unpausedSeconds) || double.IsInfinity(unpausedSeconds) || unpausedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(unpausedSeconds));
            var snapshot = ordered.ToArray();
            foreach (var move in snapshot)
                if (!move.Finished) TickMove(move, simulationSeconds, unpausedSeconds);
        }

        // ------------------------------------------------------------------ per-tick processing

        private void TickMove(ActiveMove m, double sim, double unpaused)
        {
            m.Elapsed += unpaused;
            var actor = actors.Find(m.Request.ActorId);
            if (actor == null)
            {
                Fail(m, ResultCodes.RefNotFound, "ACTOR_REMOVED", "actor disappeared from the world");
                return;
            }
            if (!actor.Position.Equals(m.Last))
            {
                CancelMove(m, "ACTOR_POSITION_CHANGED_EXTERNALLY");
                return;
            }

            if (m.State == Phase.Walking)
            {
                if (m.Visible && !string.Equals(maps.ActiveMapId, m.MapId, StringComparison.Ordinal))
                {
                    Fail(m, ResultCodes.NoRoute, "ACTIVE_MAP_CHANGED", "active map changed to " + maps.ActiveMapId + " during the move");
                    return;
                }
                if (maps.NavRevision != m.NavRevision && Replan(m, actor, "NAV_REVISION", true) == ReplanResult.Terminated) return;
                if (!IsPaused(m)) Step(m, actor, sim);
            }

            if (!m.Finished && m.Elapsed >= m.Request.TimeoutSeconds)
                Fail(m, ResultCodes.Timeout, "WATCHDOG_TIMEOUT", "no arrival within " + m.Request.TimeoutSeconds.ToString(CultureInfo.InvariantCulture) + " unpaused seconds");
        }

        private bool IsPaused(ActiveMove m)
        {
            var pause = pauses.Effective;
            if (isSceneOwner(m.Request.OwnerToken)) return pause.Scene;
            if (string.Equals(m.Request.ActorId, playerActorId, StringComparison.Ordinal)) return pause.Player || pause.World;
            return pause.Ambient || pause.World;
        }

        private void Step(ActiveMove m, ActorState actor, double sim)
        {
            if (m.Cooldown > 0) m.Cooldown = Math.Max(0.0, m.Cooldown - sim);
            var budget = actor.Speed * sim;
            var moved = 0.0;
            var blocked = false;
            var guard = m.Waypoints.Count + 8;

            while (!m.Finished && guard-- > 0)
            {
                if (m.Next >= m.Waypoints.Count)
                {
                    OnLegEnd(m, actor);
                    break;
                }
                if (budget <= NavMath.Epsilon) break;

                var from = m.Last;
                var waypoint = m.Waypoints[m.Next];
                var dx = waypoint.X - from.X;
                var dy = waypoint.Y - from.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= NavMath.Epsilon)
                {
                    m.Next++;
                    continue;
                }

                var length = Math.Min(dist, budget);
                var to = length >= dist ? waypoint : new Position(from.MapId, from.X + dx / dist * length, from.Y + dy / dist * length);
                m.HeadingX = dx / dist;
                m.HeadingY = dy / dist;

                var sweep = collision.Sweep(from, to, actor.Radius);
                var sameMap = string.Equals(sweep.End.MapId, from.MapId, StringComparison.Ordinal);
                var end = sameMap ? sweep.End : from;
                var stopped = sweep.Blocked || !sameMap;
                if (!end.Equals(from))
                {
                    var write = actors.SetPosition(m.Request.ActorId, end, m.Request.OwnerToken);
                    if (write.Status == OperationStatus.Succeeded)
                    {
                        moved += NavMath.Distance(from, end);
                        m.Last = end;
                    }
                    else
                    {
                        stopped = true;
                        Record(m.OperationId, m.Request.ActorId, "write-rejected", write.Code);
                    }
                }
                if (stopped)
                {
                    blocked = true;
                    break;
                }
                budget -= length;
                if (length >= dist) m.Next++;
            }

            if (m.Finished || m.State != Phase.Walking) return;
            if (moved > options.MinProgress) m.BlockedSeconds = 0;
            if (!blocked) return;

            if (moved <= options.MinProgress) m.BlockedSeconds += sim;
            if (m.BlockedSeconds >= options.StuckSeconds)
            {
                Fail(m, ResultCodes.NoRoute, "STUCK", "no progress for " + m.BlockedSeconds.ToString(CultureInfo.InvariantCulture) + " simulation seconds");
                return;
            }
            if (m.Cooldown <= 0)
            {
                m.Cooldown = options.ReplanCooldownSeconds;
                Replan(m, actor, "BLOCKED", false);
            }
        }

        private void OnLegEnd(ActiveMove m, ActorState actor)
        {
            var here = m.Last;
            if (m.Hop == null)
            {
                var arrival = new MoveArrival(here, m.Request.Destination);
                if (arrival.Arrived && (!m.Visible || string.Equals(maps.ActiveMapId, here.MapId, StringComparison.Ordinal)) && collision.IsPassable(here, actor.Radius))
                {
                    Terminate(m, "succeeded", here.ToString(), () => m.Handle.Succeed(arrival, Rev()));
                    return;
                }
                Replan(m, actor, "NOT_ARRIVED", false);
                return;
            }

            var hop = m.Hop;
            if (!string.Equals(here.MapId, hop.From.MapId, StringComparison.Ordinal) || NavMath.Distance(here, hop.From) > options.PortalReachTolerance)
            {
                Replan(m, actor, "PORTAL_NOT_REACHED", false);
                return;
            }
            Portal current;
            var known = maps.TryGetPortal(hop.PortalId, out current);
            if (!known || current == null || !current.Open || !current.From.Equals(hop.From) || !current.Arrival.Equals(hop.Arrival))
            {
                Replan(m, actor, "PORTAL_CHANGED", true);
                return;
            }
            if (maps.NavRevision != m.NavRevision)
            {
                Replan(m, actor, "NAV_REVISION", true);
                return;
            }
            var target = geometry.Find(hop.Arrival.MapId);
            if (target == null || !target.Contains(hop.Arrival.X, hop.Arrival.Y))
            {
                Fail(m, ResultCodes.NoRoute, "PORTAL_ARRIVAL_INVALID", "portal " + hop.PortalId + " arrives outside its map");
                return;
            }

            if (!m.Visible)
            {
                CrossLogically(m, actor, hop);
                return;
            }

            m.State = Phase.Loading;
            m.LoadFrom = m.MapId;
            m.PendingArrival = hop.Arrival;
            Record(m.OperationId, m.Request.ActorId, "load", hop.Arrival.MapId);
            IHandle load;
            try
            {
                load = maps.Load(hop.Arrival.MapId, m.Request.OwnerToken);
            }
            catch (Exception ex)
            {
                Fail(m, ResultCodes.MapLoadFailed, "MAP_LOAD_THROWN", ex.Message);
                return;
            }
            if (load == null)
            {
                Fail(m, ResultCodes.MapLoadFailed, "MAP_LOAD_NO_HANDLE", "map port returned no load handle");
                return;
            }
            Watch(m, load);
        }

        // Ordinary logical portal travel for requests that do not drive the displayed map: no global load, no ActiveMapId change.
        private void CrossLogically(ActiveMove m, ActorState actor, Portal hop)
        {
            var arrival = hop.Arrival;
            if (!collision.IsPassable(arrival, actor.Radius))
            {
                Fail(m, ResultCodes.TargetBlocked, "ARRIVAL_BLOCKED", "portal arrival " + arrival + " is not standable");
                return;
            }
            var write = actors.SetPosition(actor.ActorId, arrival, m.Request.OwnerToken);
            if (write.Status != OperationStatus.Succeeded)
            {
                var rejected = new Failure(write.Code, "ARRIVAL_REJECTED", "actor world refused the portal arrival");
                rejected.Details.AddRange(write.Details);
                FailWith(m, rejected);
                return;
            }
            ContinueAfterCrossing(m, actor, arrival);
        }

        private void ContinueAfterCrossing(ActiveMove m, ActorState actor, Position arrival)
        {
            actor = actor.WithPosition(arrival);
            m.Last = arrival;
            m.MapId = arrival.MapId;
            m.Hop = null;
            m.State = Phase.Walking;
            m.BlockedSeconds = 0;
            Record(m.OperationId, m.Request.ActorId, "portal-crossed", arrival.ToString());

            var failure = PlanLeg(m, actor);
            if (failure != null)
            {
                FailWith(m, failure);
                return;
            }
            if (m.Waypoints.Count == 0) OnLegEnd(m, actor);
        }

        // ------------------------------------------------------------------ map loading

        // Visible request whose actor stands on a known but not displayed map: the source map is loaded first; nothing moves before it succeeds.
        private IHandle<MoveArrival> BeginSourceLoad(IHandleController<MoveArrival> handle, string operationId, MoveRequest request, ActorState actor)
        {
            var move = new ActiveMove(operationId, request, handle, actor.Position, true);
            move.State = Phase.SourceLoading;
            byActor.Add(request.ActorId, move);
            ordered.Add(move);
            handle.SetCancellationHandler(reason => CancelMove(move, reason));
            Record(operationId, request.ActorId, "started", request.Destination.Target.ToString());
            Record(operationId, request.ActorId, "load", actor.Position.MapId);
            IHandle load;
            try
            {
                load = maps.Load(actor.Position.MapId, request.OwnerToken);
            }
            catch (Exception ex)
            {
                Fail(move, ResultCodes.MapLoadFailed, "MAP_LOAD_THROWN", ex.Message);
                return handle;
            }
            if (load == null)
            {
                Fail(move, ResultCodes.MapLoadFailed, "MAP_LOAD_NO_HANDLE", "map port returned no load handle");
                return handle;
            }
            Watch(move, load);
            return handle;
        }

        private void Watch(ActiveMove m, IHandle load)
        {
            m.Load = load;
            var token = ++m.LoadToken;
            load.OnTerminal(result => OnLoadTerminal(m, token, result));
        }

        private void OnLoadTerminal(ActiveMove m, int token, Result result)
        {
            if (m.Finished || m.LoadToken != token || m.Load == null) return;
            m.Load = null;

            if (m.State == Phase.RollingBack)
            {
                var pending = m.Pending;
                if (result.Status != OperationStatus.Succeeded) pending.Details.Add(Diag("MAP_ROLLBACK_FAILED", "restoring the previous map ended with " + result.Code));
                FailWith(m, pending);
                return;
            }
            if (m.State != Phase.Loading && m.State != Phase.SourceLoading) return;

            switch (result.Status)
            {
                case OperationStatus.Succeeded:
                    if (m.State == Phase.SourceLoading) ArriveOnSource(m);
                    else ArriveOnMap(m);
                    break;
                case OperationStatus.Cancelled:
                    CancelMove(m, "MAP_LOAD_CANCELLED");
                    break;
                default:
                    var failure = new Failure(ResultCodes.MapLoadFailed, "MAP_LOAD_FAILED", "map load ended with " + result.Code);
                    failure.Details.AddRange(result.Details);
                    FailWith(m, failure);
                    break;
            }
        }

        private void ArriveOnSource(ActiveMove m)
        {
            var actor = actors.Find(m.Request.ActorId);
            if (actor == null)
            {
                Fail(m, ResultCodes.RefNotFound, "ACTOR_REMOVED", "actor disappeared from the world");
                return;
            }
            if (!actor.Position.Equals(m.Last))
            {
                CancelMove(m, "ACTOR_POSITION_CHANGED_EXTERNALLY");
                return;
            }
            if (!string.Equals(maps.ActiveMapId, m.MapId, StringComparison.Ordinal))
            {
                Fail(m, ResultCodes.MapLoadFailed, "ACTIVE_MAP_NOT_SWITCHED", "load succeeded but the active map is " + maps.ActiveMapId);
                return;
            }

            m.State = Phase.Walking;
            Record(m.OperationId, m.Request.ActorId, "source-loaded", m.MapId);
            var here = new MoveArrival(actor.Position, m.Request.Destination);
            if (here.Arrived && collision.IsPassable(actor.Position, actor.Radius))
            {
                Terminate(m, "succeeded", "already within tolerance", () => m.Handle.Succeed(here, Rev()));
                return;
            }
            var failure = PlanLeg(m, actor);
            if (failure != null) FailWith(m, failure);
        }

        private void ArriveOnMap(ActiveMove m)
        {
            var arrival = m.PendingArrival;
            var actor = actors.Find(m.Request.ActorId);
            if (actor == null)
            {
                Fail(m, ResultCodes.RefNotFound, "ACTOR_REMOVED", "actor disappeared from the world");
                return;
            }
            if (!actor.Position.Equals(m.Last))
            {
                CancelMove(m, "ACTOR_POSITION_CHANGED_EXTERNALLY");
                return;
            }
            if (!string.Equals(maps.ActiveMapId, arrival.MapId, StringComparison.Ordinal))
            {
                Fail(m, ResultCodes.MapLoadFailed, "ACTIVE_MAP_NOT_SWITCHED", "load succeeded but the active map is " + maps.ActiveMapId);
                return;
            }
            if (!collision.IsPassable(arrival, actor.Radius))
            {
                StartRollback(m, new Failure(ResultCodes.TargetBlocked, "ARRIVAL_BLOCKED", "portal arrival " + arrival + " is not standable"));
                return;
            }
            // Explicit portal crossing: the only relocation this service performs.
            var write = actors.SetPosition(actor.ActorId, arrival, m.Request.OwnerToken);
            if (write.Status != OperationStatus.Succeeded)
            {
                var rejected = new Failure(write.Code, "ARRIVAL_REJECTED", "actor world refused the portal arrival");
                rejected.Details.AddRange(write.Details);
                StartRollback(m, rejected);
                return;
            }

            ContinueAfterCrossing(m, actor, arrival);
        }

        private void StartRollback(ActiveMove m, Failure pending)
        {
            m.Pending = pending;
            m.State = Phase.RollingBack;
            Record(m.OperationId, m.Request.ActorId, "rollback", m.LoadFrom);
            IHandle back;
            try
            {
                back = maps.Load(m.LoadFrom, m.Request.OwnerToken);
            }
            catch (Exception ex)
            {
                pending.Details.Add(Diag("MAP_ROLLBACK_THROWN", ex.Message));
                FailWith(m, pending);
                return;
            }
            if (back == null)
            {
                pending.Details.Add(Diag("MAP_ROLLBACK_FAILED", "map port returned no load handle"));
                FailWith(m, pending);
                return;
            }
            Watch(m, back);
        }

        // ------------------------------------------------------------------ planning

        private Failure PlanLeg(ActiveMove m, ActorState actor)
        {
            var destination = m.Request.Destination;
            var here = m.Last;
            Position goal;
            double tolerance;
            Portal hop = null;

            if (string.Equals(here.MapId, destination.Target.MapId, StringComparison.Ordinal))
            {
                goal = destination.Target;
                tolerance = destination.Tolerance;
            }
            else
            {
                if (!m.Request.UsePortals) return new Failure(ResultCodes.NoRoute, "PORTALS_DISABLED", "destination is on map " + destination.Target.MapId + " and portals are disabled");
                var route = router.Route(here, destination.Target.MapId);
                if (route == null || route.Count == 0) return new Failure(ResultCodes.NoRoute, "NO_PORTAL_ROUTE", "no open portal route from " + here.MapId + " to " + destination.Target.MapId);
                hop = route[0];
                goal = hop.From;
                tolerance = options.PortalReachTolerance;
            }

            var plan = planner.Plan(here, goal, tolerance, actor.Radius);
            switch (plan.Status)
            {
                case PathStatus.Found:
                    m.Waypoints = plan.Waypoints;
                    m.Next = 0;
                    m.Hop = hop;
                    m.NavRevision = maps.NavRevision;
                    return null;
                case PathStatus.StartBlocked:
                    return new Failure(ResultCodes.TargetBlocked, "START_BLOCKED", "actor stands on a position its footprint cannot occupy");
                case PathStatus.TargetBlocked:
                    return hop == null
                        ? new Failure(ResultCodes.TargetBlocked, "TARGET_BLOCKED", "destination cannot be stood on within its tolerance")
                        : new Failure(ResultCodes.NoRoute, "PORTAL_BLOCKED", "portal " + hop.PortalId + " entrance cannot be stood on");
                default:
                    return new Failure(ResultCodes.NoRoute, string.IsNullOrEmpty(plan.Detail) ? "NO_ROUTE" : plan.Detail, "no walkable route on map " + here.MapId);
            }
        }

        private ReplanResult Replan(ActiveMove m, ActorState actor, string reason, bool fatal)
        {
            m.Replans++;
            Record(m.OperationId, m.Request.ActorId, "replan", reason);
            if (m.Replans > options.MaxReplans)
            {
                Fail(m, ResultCodes.NoRoute, "REPLAN_LIMIT", "replan limit reached after " + reason);
                return ReplanResult.Terminated;
            }
            var failure = PlanLeg(m, actor);
            if (failure == null) return ReplanResult.Planned;
            if (fatal || failure.Code == ResultCodes.TargetBlocked)
            {
                FailWith(m, failure);
                return ReplanResult.Terminated;
            }
            return ReplanResult.Deferred;
        }

        // ------------------------------------------------------------------ termination

        private IHandle<MoveArrival> Refuse(IHandleController<MoveArrival> handle, string operationId, MoveRequest request, Failure failure)
        {
            Record(operationId, request.ActorId, "refused", failure.Code);
            handle.Fail(failure.Code, Rev(), failure.Details);
            return handle;
        }

        private void CancelMove(ActiveMove m, string reason)
        {
            var text = string.IsNullOrEmpty(reason) ? ResultCodes.Cancelled : reason;
            Terminate(m, "cancelled", text, () => m.Handle.Cancelled(text, Rev()));
        }

        private void Fail(ActiveMove m, string code, string diagnosticCode, string message)
        {
            FailWith(m, new Failure(code, diagnosticCode, message));
        }

        private void FailWith(ActiveMove m, Failure failure)
        {
            Terminate(m, "failed", failure.Code, () => m.Handle.Fail(failure.Code, Rev(), failure.Details));
        }

        // Children are released before any terminal state is published; the first terminal wins.
        private void Terminate(ActiveMove m, string kind, string detail, Func<bool> publish)
        {
            if (m.Finished) return;
            m.Finished = true;
            ReleaseChildren(m);
            byActor.Remove(m.Request.ActorId);
            ordered.Remove(m);
            Record(m.OperationId, m.Request.ActorId, kind, detail);
            publish();
        }

        private void ReleaseChildren(ActiveMove m)
        {
            m.LoadToken++;
            var load = m.Load;
            m.Load = null;
            if (load == null) return;
            try
            {
                load.Cancel("move terminated");
            }
            catch (Exception ex)
            {
                Record(m.OperationId, m.Request.ActorId, "release-error", ex.Message);
            }
        }

        private long Rev()
        {
            if (revisionSource == null) return 0;
            var value = revisionSource();
            return value < 0 ? 0 : value;
        }

        private void Record(string operationId, string actorId, string kind, string detail)
        {
            trace.Enqueue(new MoveTraceEntry(++traceSequence, operationId, actorId, kind, detail));
            while (trace.Count > options.MaxTraceEntries) trace.Dequeue();
        }

        private static Diagnostic Diag(string code, string message)
        {
            return new Diagnostic(code, message, null, string.Empty, string.Empty);
        }
    }
}
