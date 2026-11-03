using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Application.Schedules
{
    public sealed class ReservedStagingOptions
    {
        public static readonly ReservedStagingOptions Default = new ReservedStagingOptions("snapshot.world", "player", "input.player", 60, "stage:");

        public string WorldResource { get; }
        public string PlayerActorId { get; }
        public string PlayerResource { get; }
        public double TimeoutSeconds { get; }
        public string OwnerTokenPrefix { get; }

        public ReservedStagingOptions(string worldResource, string playerActorId, string playerResource, double timeoutSeconds, string ownerTokenPrefix)
        {
            WorldResource = IdGuard.Require(worldResource, nameof(worldResource));
            PlayerActorId = IdGuard.Require(playerActorId, nameof(playerActorId));
            PlayerResource = IdGuard.Require(playerResource, nameof(playerResource));
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            TimeoutSeconds = timeoutSeconds;
            OwnerTokenPrefix = IdGuard.Require(ownerTokenPrefix, nameof(ownerTokenPrefix));
        }
    }

    /// <summary>
    /// Staging after the runner holds the lease. The event row's staging field selects the policy:
    /// current only cancels ambient moves of leased NPCs and succeeds without moving anyone;
    /// reserved validates the lease and the planned positions, hides the view, loads the target map and only then
    /// replaces the complete actor list in one Restore call, then shows the view again.
    /// Failure and cancellation always unhide and never leave partial actor writes.
    /// </summary>
    public sealed class ReservedStagingService : IEventStagingPort, IRuntimeTick
    {
        public const string StagingField = "staging";
        public const string CurrentStaging = "current";
        public const string ReservedStaging = "reserved";

        private enum AbortKind { None, Cancel, Timeout }

        private sealed class StageOp
        {
            public IHandleController<bool> Handle;
            public CompiledEvent Definition;
            public Lease Lease;
            public ReservedJoinPlan Plan;   // null for current staging
            public IHandle Child;
            public bool Hidden;
            public AbortKind Abort;
            public string AbortReason = "";
            public double StartedAt;
        }

        private readonly IActorWorld world;
        private readonly IMapPort map;
        private readonly ICollisionPort collision;
        private readonly IHandleFactory handles;
        private readonly Func<CompiledEvent, Lease, Outcome<ReservedJoinPlan>> planner;
        private readonly Action<bool> setHidden;
        private readonly Func<IReadOnlyList<string>, IHandle> stopAmbient;
        private readonly ReservedStagingOptions options;
        private readonly List<StageOp> ops = new List<StageOp>();
        private double elapsed;

        /// <param name="planner">Explicit join policy supplied by the composition root; required for reserved staging.</param>
        /// <param name="setHidden">Hides/shows the staged view; required for reserved staging.</param>
        /// <param name="stopAmbient">Optional; cancels ambient moves of the given actor ids and completes when they are terminal.</param>
        public ReservedStagingService(IActorWorld world, IMapPort map, ICollisionPort collision, IHandleFactory handles,
            Func<CompiledEvent, Lease, Outcome<ReservedJoinPlan>> planner, Action<bool> setHidden,
            Func<IReadOnlyList<string>, IHandle> stopAmbient, ReservedStagingOptions options = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.collision = collision ?? throw new ArgumentNullException(nameof(collision));
            this.handles = handles ?? throw new ArgumentNullException(nameof(handles));
            this.planner = planner;
            this.setHidden = setHidden;
            this.stopAmbient = stopAmbient;
            this.options = options ?? ReservedStagingOptions.Default;
        }

        public int ActiveCount => ops.Count;

        public IHandle Stage(CompiledEvent definition, Lease lease)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (lease == null) throw new ArgumentNullException(nameof(lease));

            var controller = handles.Create<bool>("stage." + definition.Id);
            controller.Start();

            if (!definition.Raw.TryGet(StagingField, out var kind) || kind.Kind != DataKind.String)
                return Refuse(controller, ResultCodes.ContentInvalid, D(ResultCodes.ContentInvalid, "event row has no staging field", definition.Id, "current|reserved", "missing"));

            switch (kind.Text)
            {
                case CurrentStaging: return StageCurrent(controller, definition, lease);
                case ReservedStaging: return StageReserved(controller, definition, lease);
                default:
                    return Refuse(controller, ResultCodes.ContentInvalid, D(ResultCodes.ContentInvalid, "unknown staging policy", definition.Id, "current|reserved", kind.Text));
            }
        }

        /// <summary>Watchdog on the unpaused clock: a stage that runs too long is cancelled and ends with TIMEOUT.</summary>
        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (double.IsNaN(unpausedSeconds) || double.IsInfinity(unpausedSeconds) || unpausedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(unpausedSeconds));
            elapsed += unpausedSeconds;
            foreach (var op in ops.ToArray())
                if (op.Abort == AbortKind.None && elapsed - op.StartedAt >= options.TimeoutSeconds)
                    Abort(op, AbortKind.Timeout, "stage watchdog expired");
        }

        /// <summary>Cancels every running stage; each one unhides and ends Cancelled once its child work is terminal.</summary>
        public void Teardown(string reason)
        {
            foreach (var op in ops.ToArray()) Abort(op, AbortKind.Cancel, reason ?? "teardown");
        }

        private IHandle StageCurrent(IHandleController<bool> controller, CompiledEvent definition, Lease lease)
        {
            var op = Begin(controller, definition, lease, null);
            var npcs = LeasedActors(lease);
            if (stopAmbient == null || npcs.Count == 0)
            {
                Complete(op);
                return controller;
            }
            Await(op, stopAmbient(npcs), result =>
            {
                if (result.Status == OperationStatus.Succeeded) Complete(op);
                else FailOp(op, result.Code, result.Details);
            });
            return controller;
        }

        private IHandle StageReserved(IHandleController<bool> controller, CompiledEvent definition, Lease lease)
        {
            if (planner == null || setHidden == null)
                return Refuse(controller, ResultCodes.Unregistered, D(ResultCodes.Unregistered, "reserved staging needs a join planner and a hide callback", definition.Id, "planner and setHidden", "missing"));
            foreach (var running in ops)
                if (running.Plan != null)
                    return Refuse(controller, ResultCodes.ResourceBusy, D(ResultCodes.ResourceBusy, "another reserved stage is running", definition.Id, "idle", running.Definition.Id));

            var held = new HashSet<string>(lease.Resources, StringComparer.Ordinal);
            var required = new SortedSet<string>(definition.Resources, StringComparer.Ordinal) { options.WorldResource };
            var missing = new List<Diagnostic>();
            foreach (var resource in required)
                if (!held.Contains(resource))
                    missing.Add(D(ResultCodes.ResourceBusy, "reserved staging requires this resource in the lease", resource, resource, "not leased"));
            if (missing.Count > 0) return Refuse(controller, ResultCodes.ResourceBusy, missing.ToArray());

            var planned = planner(definition, lease);
            if (planned == null)
                return Refuse(controller, ResultCodes.ContentInvalid, D(ResultCodes.ContentInvalid, "join planner returned no outcome", definition.Id, "outcome", "null"));
            if (!planned.Succeeded)
                return Refuse(controller, planned.Result.Code, new List<Diagnostic>(planned.Result.Details).ToArray());

            var plan = planned.Value;
            var problems = Validate(plan, lease);
            if (problems.Count > 0) return Refuse(controller, problems[0].Code, problems.ToArray());

            var op = Begin(controller, definition, lease, plan);
            var npcs = LeasedActors(lease);
            if (stopAmbient != null && npcs.Count > 0)
            {
                Await(op, stopAmbient(npcs), result =>
                {
                    if (result.Status == OperationStatus.Succeeded) BeginHide(op);
                    else FailOp(op, result.Code, result.Details);
                });
            }
            else
            {
                BeginHide(op);
            }
            return controller;
        }

        private void BeginHide(StageOp op)
        {
            setHidden(true);
            op.Hidden = true;

            if (string.Equals(map.ActiveMapId, op.Plan.TargetMapId, StringComparison.Ordinal))
            {
                ApplyJoin(op);
                return;
            }

            Await(op, map.Load(op.Plan.TargetMapId, options.OwnerTokenPrefix + op.Definition.Id), result =>
            {
                if (result.Status == OperationStatus.Succeeded) ApplyJoin(op);
                else FailOp(op, result.Code, result.Details);
            });
        }

        private void ApplyJoin(StageOp op)
        {
            // Re-check on the loaded map: passability is only authoritative there.
            var problems = Validate(op.Plan, op.Lease);
            if (problems.Count > 0)
            {
                FailOp(op, problems[0].Code, problems);
                return;
            }

            var planned = new Dictionary<string, Position>(StringComparer.Ordinal);
            foreach (var p in op.Plan.Placements) planned[p.ActorId] = p.Position;

            var actors = new List<ActorState>();
            foreach (var actor in world.All)
                actors.Add(planned.TryGetValue(actor.ActorId, out var position) ? actor.WithPosition(position) : actor);

            var restored = world.Restore(actors);
            if (restored.Status != OperationStatus.Succeeded)
            {
                FailOp(op, restored.Code, restored.Details);
                return;
            }

            op.Hidden = false;
            setHidden(false);
            Complete(op);
        }

        private List<Diagnostic> Validate(ReservedJoinPlan plan, Lease lease)
        {
            var problems = new List<Diagnostic>();
            var held = new HashSet<string>(lease.Resources, StringComparer.Ordinal);
            foreach (var placement in plan.Placements)
            {
                var actor = world.Find(placement.ActorId);
                if (actor == null)
                {
                    problems.Add(D(ResultCodes.RefNotFound, "planned actor is not in the world", placement.ActorId, "known actor", "missing"));
                    continue;
                }
                var leased = held.Contains(placement.ActorId)
                    || (placement.ActorId == options.PlayerActorId && held.Contains(options.PlayerResource));
                if (!leased)
                {
                    problems.Add(D(ResultCodes.ResourceBusy, "planned actor is not covered by the lease", placement.ActorId, "leased actor", "not leased"));
                    continue;
                }
                if (!collision.IsPassable(placement.Position, actor.Radius))
                    problems.Add(D(ResultCodes.TargetBlocked, "planned position is not passable", placement.ActorId, "passable position", placement.Position.ToString()));
            }
            return problems;
        }

        private List<string> LeasedActors(Lease lease)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var resource in lease.Resources)
                if (resource != options.PlayerActorId && world.Find(resource) != null) ids.Add(resource);
            return new List<string>(ids);
        }

        private StageOp Begin(IHandleController<bool> controller, CompiledEvent definition, Lease lease, ReservedJoinPlan plan)
        {
            var op = new StageOp { Handle = controller, Definition = definition, Lease = lease, Plan = plan, StartedAt = elapsed };
            ops.Add(op);
            controller.SetCancellationHandler(reason => Abort(op, AbortKind.Cancel, reason));
            return op;
        }

        private void Await(StageOp op, IHandle child, Action<Result> next)
        {
            if (child == null)
            {
                FailOp(op, ResultCodes.ContentInvalid, new[] { D(ResultCodes.ContentInvalid, "port returned no handle", op.Definition.Id, "handle", "null") });
                return;
            }
            op.Child = child;
            child.OnTerminal(result =>
            {
                op.Child = null;
                if (op.Abort != AbortKind.None) Finish(op);
                else next(result);
            });
        }

        private void Abort(StageOp op, AbortKind kind, string reason)
        {
            if (op.Abort != AbortKind.None) return;
            op.Abort = kind;
            op.AbortReason = reason ?? "cancelled";
            if (op.Child != null) op.Child.Cancel(op.AbortReason);
            else Finish(op);
        }

        private void Finish(StageOp op)
        {
            Unhide(op);
            ops.Remove(op);
            if (op.Abort == AbortKind.Timeout)
                op.Handle.Fail(ResultCodes.Timeout, 0, new[] { D(ResultCodes.Timeout, "stage watchdog expired", op.Definition.Id, "finish within " + options.TimeoutSeconds + "s", "timed out") });
            else
                op.Handle.Cancelled(op.AbortReason, 0);
        }

        private void FailOp(StageOp op, string code, IReadOnlyList<Diagnostic> details)
        {
            Unhide(op);
            ops.Remove(op);
            op.Handle.Fail(code, 0, details);
        }

        private void Complete(StageOp op)
        {
            ops.Remove(op);
            op.Handle.Succeed(true, 0);
        }

        private void Unhide(StageOp op)
        {
            if (!op.Hidden) return;
            op.Hidden = false;
            setHidden(false);
        }

        private static IHandle Refuse(IHandleController<bool> controller, string code, params Diagnostic[] details)
        {
            controller.Fail(code, 0, details);
            return controller;
        }

        private static Diagnostic D(string code, string message, string entity, string expected, string actual)
        {
            return new Diagnostic(code, message, new SourceLocation("", "", entity, "", 0), expected, actual);
        }
    }
}
