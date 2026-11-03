using System;
using System.Collections.Generic;
using MagicCampus.Application.Reservations;
using MagicCampus.Contracts;
using MagicCampus.Runtime;

namespace MagicCampus.Tests.PureDomain.Schedules
{
    internal static class Spec
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("spec failed: " + message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("spec failed: " + message + " (expected " + expected + ", actual " + actual + ")");
        }
    }

    internal sealed class Rt
    {
        internal readonly CompletionPump Pump = new CompletionPump();
        internal readonly HandleFactory Handles;
        internal Rt() { Handles = new HandleFactory(Pump); }
        internal void Step() { Pump.AdvanceTick(); }
    }

    internal static class Kit
    {
        private static readonly FunctionDef Manual = new FunctionDef("manual", FunctionRole.Trigger, "owner", null);

        internal static KeyValuePair<string, DataNode> Kv(string key, DataNode value) { return new KeyValuePair<string, DataNode>(key, value); }
        internal static DataNode Obj(params KeyValuePair<string, DataNode>[] members) { return DataNode.FromObject(members); }
        internal static DataNode Str(string text) { return DataNode.FromString(text); }

        internal static DataNode Row(string id, string status, long acceptedAt)
        {
            return Obj(Kv("id", Str(id)), Kv("status", Str(status)), Kv("acceptedAt", DataNode.FromInteger(acceptedAt)));
        }

        internal static StateSnapshot Snapshot(long revision, params DataNode[] rows)
        {
            return new StateSnapshot(revision, Obj(Kv("appointments", DataNode.FromList(rows)), Kv("marker", DataNode.FromInteger(revision))));
        }

        internal static CompiledEvent Event(string id, string category = "interaction", string staging = "current", string completion = null,
            RepeatPolicy repeat = RepeatPolicy.Once, IEnumerable<string> resources = null)
        {
            var trigger = new CompiledCall(Manual, 1, "", false, null, null, new SourceLocation("runner.json", "/events", id, id + "#1", 0));
            var raw = Obj(Kv("id", Str(id)), Kv("category", Str(category)), Kv("staging", Str(staging)));
            CompletionKey? key = completion == null ? (CompletionKey?)null : new CompletionKey(completion);
            return new CompiledEvent(id, ExecutionMode.Scene, repeat, resources, trigger, null, 0, null, null, null, key, raw);
        }

        internal static ActorState Actor(string id, string mapId, double x, double y)
        {
            return new ActorState(id, new Position(mapId, x, y), 0.2, 3);
        }

        internal static DataNode Npc(string id)
        {
            return Obj(Kv("id", Str(id)), Kv("home", Str("dest.home_a")));
        }

        internal static DataNode Sched(string id, string npcId)
        {
            return Obj(Kv("id", Str(id)), Kv("npcId", Str(npcId)), Kv("script", Str("")));
        }

        internal static IReadOnlyList<CompiledCall> Calls(string scheduleId, params (string minute, string dest)[] entries)
        {
            var list = new List<CompiledCall>();
            for (var i = 0; i < entries.Length; i++)
            {
                var fn = new FunctionDef(entries[i].minute, FunctionRole.ScheduleEntry, "owner",
                    new[] { new ParamDef("destination", ParamType.Ref, true, false, "destination", null, null, null) });
                list.Add(new CompiledCall(fn, i + 1, "", false, new[] { Kv("destination", Str(entries[i].dest)) }, null,
                    new SourceLocation("runner.json", "/schedules", scheduleId, scheduleId + "#" + (i + 1), 0)));
            }
            return list;
        }

        internal static PlannedChange Accept(string eventId, long at)
        {
            return new PlannedChange(ReservationParticipant.Id, "accept", Obj(Kv("eventId", Str(eventId)), Kv("acceptedAt", DataNode.FromInteger(at))));
        }

        internal static PlannedChange Complete(string eventId)
        {
            return new PlannedChange(ReservationParticipant.Id, "complete", Obj(Kv("eventId", Str(eventId))));
        }

        internal static PlannedChange EventDone(string eventId, string key)
        {
            return new PlannedChange("event-effects", "event.complete", Obj(Kv("eventId", Str(eventId)), Kv("completionKey", Str(key)), Kv("repeatPolicy", Str("once"))));
        }
    }

    internal sealed class FakeCatalog : ICatalog
    {
        private readonly Dictionary<string, CompiledEvent> events = new Dictionary<string, CompiledEvent>(StringComparer.Ordinal);
        private readonly Dictionary<string, DataNode> definitions = new Dictionary<string, DataNode>(StringComparer.Ordinal);

        internal FakeCatalog(IEnumerable<CompiledEvent> evs, DataNode runnerRoot)
        {
            foreach (var e in evs) events[e.Id] = e;
            if (runnerRoot != null) definitions["runner/root"] = runnerRoot;
        }

        public string ContentVersion => "test";
        public string CatalogHash => "hash";
        public IReadOnlyList<CompiledEvent> Events => new List<CompiledEvent>(events.Values);
        public CompiledEvent FindEvent(string id) { return events.TryGetValue(id, out var e) ? e : null; }
        public DataNode FindDefinition(string kind, string id) { return definitions.TryGetValue(kind + "/" + id, out var d) ? d : null; }
    }

    internal sealed class FakeProvider : ICatalogProvider
    {
        internal FakeProvider(ICatalog current) { Current = current; }
        public ICatalog Current { get; }

        public Outcome<ICatalog> TryReplace(ContentBundle catalog, ContentBundle runner, ContentBundle functions, ContentBundle catalogSchema, ContentBundle runnerSchema)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class FakeLedger : ICompletionLedger
    {
        internal readonly HashSet<string> Done = new HashSet<string>(StringComparer.Ordinal);
        public bool IsCompleted(StateSnapshot snapshot, CompletionKey key) { return Done.Contains(key.Value); }
    }

    internal sealed class FakeCalendar : ICalendar
    {
        internal long Minute;
        public GameTime FromSnapshot(StateSnapshot snapshot) { return new GameTime(Minute, 0); }
        public ClockAdvance Plan(GameTime from, double minutes) { throw new NotSupportedException(); }
    }

    internal sealed class FakeState : IStateReader
    {
        public StateSnapshot Current { get; set; }
    }

    internal sealed class ReservationRig
    {
        internal readonly FakeProvider Catalog;
        internal readonly FakeLedger Ledger = new FakeLedger();
        internal readonly FakeCalendar Calendar = new FakeCalendar();
        internal readonly ReservationService Service;
        internal readonly ReservationParticipant Participant;

        internal ReservationRig(params CompiledEvent[] events)
        {
            Catalog = new FakeProvider(new FakeCatalog(events, null));
            Service = new ReservationService(Catalog, Calendar, Ledger);
            Participant = new ReservationParticipant(Catalog, Ledger);
        }
    }

    internal sealed class FakeResolver : IDestinationResolver
    {
        internal readonly List<string> Asked = new List<string>();

        public Outcome<Destination> Resolve(string actorId, string destinationId)
        {
            Asked.Add(destinationId);
            switch (destinationId)
            {
                case "dest.x": return Outcome<Destination>.Ok(new Destination(destinationId, new Position("map.t", 5.5, 1.5), 0.2), 0);
                case "dest.y": return Outcome<Destination>.Ok(new Destination(destinationId, new Position("map.t", 3.5, 2.5), 0.2), 0);
                case "dest.home_a": return Outcome<Destination>.Ok(new Destination(destinationId, new Position("map.t", 1.5, 1.5), 0.2), 0);
                default:
                    return Outcome<Destination>.Fail(ResultCodes.RefNotFound, 0, new Diagnostic(ResultCodes.RefNotFound, "unknown destination", null, "known destination", destinationId));
            }
        }
    }

    internal sealed class FakeClock : IClockService
    {
        public GameTime Now { get; set; }
        public ClockAdvance Advance(double simulationSeconds, IPausePolicy pause) { throw new NotSupportedException(); }
    }

    internal sealed class FakePause : IPauseService
    {
        public IPausePolicy Effective { get; set; } = PauseSet.None;
        public PauseToken Acquire(string owner, IPausePolicy requested) { throw new NotSupportedException(); }
        public void Release(PauseToken token) { throw new NotSupportedException(); }
    }

    internal sealed class FakeWorld : IActorWorld
    {
        private readonly List<string> log;
        private readonly Dictionary<string, ActorState> actors = new Dictionary<string, ActorState>(StringComparer.Ordinal);
        internal int SetPositionCalls;
        internal int RestoreCalls;
        internal bool FailRestore;

        internal FakeWorld(List<string> log, params ActorState[] initial)
        {
            this.log = log;
            foreach (var a in initial) actors[a.ActorId] = a;
        }

        public ActorState Find(string id) { return actors.TryGetValue(id, out var a) ? a : null; }

        public IReadOnlyList<ActorState> All
        {
            get
            {
                var list = new List<ActorState>(actors.Values);
                list.Sort((a, b) => string.CompareOrdinal(a.ActorId, b.ActorId));
                return list;
            }
        }

        public Result SetPosition(string actorId, Position position, string ownerToken)
        {
            SetPositionCalls++;
            log.Add("setpos");
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }

        public Result Restore(IReadOnlyList<ActorState> list)
        {
            RestoreCalls++;
            log.Add("restore");
            if (FailRestore) return new Result(OperationStatus.Failed, ResultCodes.InvariantViolation, 0, new Diagnostic[0]);
            actors.Clear();
            foreach (var a in list) actors[a.ActorId] = a;
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }
    }

    internal sealed class FakeMove : IMoveService
    {
        private readonly IHandleFactory handles;
        internal readonly List<MoveRequest> Requests = new List<MoveRequest>();
        internal readonly List<IHandleController<MoveArrival>> Handles = new List<IHandleController<MoveArrival>>();
        internal readonly List<string> CancelReasons = new List<string>();
        internal bool AutoFail;

        internal FakeMove(IHandleFactory handles) { this.handles = handles; }

        public IHandle<MoveArrival> Move(MoveRequest request)
        {
            Requests.Add(request);
            var c = handles.Create<MoveArrival>("move." + Requests.Count);
            c.Start();
            c.SetCancellationHandler(r => CancelReasons.Add(r));
            Handles.Add(c);
            if (AutoFail) c.Fail(ResultCodes.NoRoute, 0, new Diagnostic[0]);
            return c;
        }
    }

    internal sealed class FakeMap : IMapPort
    {
        private readonly List<string> log;
        private readonly IHandleFactory handles;
        internal string Active = "map.old";
        internal readonly List<IHandleController<bool>> Loads = new List<IHandleController<bool>>();
        internal readonly List<string> CancelReasons = new List<string>();

        internal FakeMap(List<string> log, IHandleFactory handles) { this.log = log; this.handles = handles; }

        public string ActiveMapId => Active;
        public long NavRevision => 0;
        public bool TryGetPortal(string portalId, out Portal portal) { portal = null; return false; }
        public IReadOnlyList<Portal> PortalsOn(string mapId) { return new Portal[0]; }

        public IHandle Load(string mapId, string ownerToken)
        {
            log.Add("load:" + mapId);
            var c = handles.Create<bool>("load." + mapId);
            c.Start();
            c.SetCancellationHandler(r => CancelReasons.Add(r));
            Loads.Add(c);
            return c;
        }
    }

    internal sealed class FakeCollision : ICollisionPort
    {
        internal Func<Position, bool> Passable = p => true;
        public SweepResult Sweep(Position from, Position to, double radius) { throw new NotSupportedException(); }
        public bool IsPassable(Position at, double radius) { return Passable(at); }
    }

    internal sealed class FakeRunner : IEventRunner
    {
        private readonly IHandleFactory handles;
        internal readonly List<EventStartRequest> Starts = new List<EventStartRequest>();
        internal readonly List<IHandleController<EventCompletion>> Handles = new List<IHandleController<EventCompletion>>();
        internal bool AutoFail;

        internal FakeRunner(IHandleFactory handles) { this.handles = handles; }

        public IHandle<EventCompletion> Start(EventStartRequest request)
        {
            Starts.Add(request);
            var c = handles.Create<EventCompletion>("run." + Starts.Count);
            c.Start();
            Handles.Add(c);
            if (AutoFail) c.Fail(ResultCodes.ResourceBusy, 0, new Diagnostic[0]);
            return c;
        }
    }
}
