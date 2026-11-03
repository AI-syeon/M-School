// Stage5 composition root for the first-connection slice: a plain C# object that wires the real production services.
// A scene MonoBehaviour calls Pump.AdvanceTick() and polls input first (or sets PumpInStep), then Step(dt).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicCampus.Application.Reservations;
using MagicCampus.Application.Schedules;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Content.Parsing;
using MagicCampus.Contracts;
using MagicCampus.Events;
using MagicCampus.Persistence;
using MagicCampus.Presentation.Cutscene;
using MagicCampus.Presentation.Dialogue;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Transactions.Conditions;
using MagicCampus.Transactions.Core;
using MagicCampus.Transactions.Effects;
using MagicCampus.UnityAdapters.Collision;
using MagicCampus.UnityAdapters.ContentIO;
using MagicCampus.UnityAdapters.Maps;
using MagicCampus.UnityAdapters.Storage;
using MagicCampus.World.Movement;
using Tk = MagicCampus.Timekeeping;

namespace MagicCampus.Bootstrap.FirstConnection
{
  public sealed class FirstConnectionRuntimeConfig
  {
    /// <summary>Generated fixture folder (contains base/ and variants/).</summary>
    public string FixtureRoot;
    /// <summary>base, variants/a10-mira, variants/dest-moved, ...</summary>
    public string Variant = "base";
    /// <summary>Save file name looked up in the variant folder, then in base/.</summary>
    public string InitialSave = "save-offered.json";
    public string SaveDirectory;
    public DialoguePresentation DialogueUi;
    public IActorVisualResolver VisualResolver;
    public MapAdapterConfig MapConfig;
    public Action<string> Trace;
    /// <summary>When the save directory already holds a restorable generation, start from it (process restart).</summary>
    public bool ResumeFromDisk;
    /// <summary>Default false: the caller pumps (Pump.AdvanceTick) before Step.</summary>
    public bool PumpInStep;
    public double StartupTimeoutSeconds = 30;
    public double ManualMoveTimeoutSeconds = 60;
    public double ActorRadius = 0.2;
    public double PlayerSpeed = 3;
  }

  public enum RuntimePhase { Starting, Ready, Loading, Faulted, Disposed }

  public sealed class FirstConnectionRuntime : IDisposable
  {
    public const string PlayerId = "player";
    public const string PlayerResource = "input.player";
    public const string SceneOwnerPrefix = "scene:";
    const string RuntimeOwner = "runtime:bootstrap";
    const char Q = (char)34;

    sealed class ActiveNpc { public string Id; public Position Home; public double Speed; }

    readonly FirstConnectionRuntimeConfig config;
    readonly List<string> diagnostics = new List<string>();
    readonly List<string> traceLog = new List<string>();
    readonly List<ActiveNpc> activeNpcs = new List<ActiveNpc>();
    readonly Dictionary<string, Dictionary<string, Position>> recipes = new Dictionary<string, Dictionary<string, Position>>(StringComparer.Ordinal);
    readonly DataNode saveSchema;
    long traceSeq, opSeq, inputSeq, txnSeq;
    bool inStep, acceptUnsaved, cancelSent;
    double opElapsed;
    string lastClockFailure = "";
    IHandle currentOp, saveOp;
    IHandle<MoveArrival> manualHandle;

    // Fault injection (read at the moment the map verifier / disk hook runs).
    public string FailMapId;
    public bool FailDisk;

    public event Action<bool> HiddenChanged;

    // Composed services and state, exposed for the scene view and acceptance probe.
    public JsonDataCodec Codec { get; }
    public StandardSchemaValidator Schema { get; }
    public ICatalog Compiled { get; }
    CatalogProvider Provider { get; }
    public MapLayout Layout { get; }
    public MapAdapter Maps { get; }
    GridCollisionPort CollisionPort { get; }
    public ActorWorld Actors { get; }
    public CompletionPump Pump { get; }
    HandleFactory Handles { get; }
    public PauseService Pause { get; }
    public LeaseService Leases { get; }
    public TickClock Ticks { get; }
    MoveService Moves { get; }
    public StateStore Store { get; }
    Tk.ClockService Clock { get; }
    WorldStateProjector Projector { get; }
    public CatalogDestinationResolver Destinations { get; }
    public ReservationService Reservations { get; }
    ScheduleService Schedule { get; }
    ScheduleController Scheduler { get; }
    public FileGenerationStore Disk { get; }
    public SafeBoundaryPolicy Policy { get; }
    SaveService Saves { get; }
    public ReservedStagingService Staging { get; }
    EventRunner Runner { get; }

    public RuntimePhase Phase { get; private set; }
    public string LastError { get; private set; }
    public bool Hidden { get; private set; }
    IHandle<EventCompletion> LastEventHandle { get; set; }

    public bool Ready { get { return Phase == RuntimePhase.Ready; } }
    bool Busy
    {
      get
      {
        return Phase != RuntimePhase.Ready || Runner.ActiveCount > 0 || Runner.PendingSaveCount > 0 || Staging.ActiveCount > 0
          || saveOp != null || (manualHandle != null && manualHandle.Terminal == null) || config.DialogueUi.UiCaptured;
      }
    }
    /// <summary>True while a committed change (accept or finished scene) has not reached the disk.</summary>
    public bool Unsaved
    {
      get
      {
        var latest = Policy.Latest;
        return acceptUnsaved || (latest != null && latest.Boundary == SafeBoundary.SceneComplete && !latest.Saved);
      }
    }
    public IReadOnlyList<string> Diagnostics { get { return diagnostics.ToArray(); } }
    public IReadOnlyList<string> TraceLog { get { return traceLog.ToArray(); } }
    public GameTime Now { get { return Clock.Now; } }

    // Token / leak counts (all zero when idle).
    public int ActiveLeaseCount { get { return Leases.ActiveLeaseCount; } }
    public int PauseTokenCount { get { return Pause.LiveTokenCount; } }
    public int LiveHandleCount { get { return Handles.LiveCount; } }
    public int PresentationLocks { get { return config.DialogueUi.OutstandingLocks; } }
    public int PendingDiskOperations { get { return Disk.PendingOperations; } }
    public int ActiveMoveCount { get { return Moves.ActiveMoveCount; } }
    public int ActiveEventCount { get { return Runner.ActiveCount; } }
    public int PendingSaveCount { get { return Runner.PendingSaveCount; } }

    public string TokenReport()
    {
      return "phase=" + Phase + " hidden=" + Hidden + " busy=" + Busy + " unsaved=" + Unsaved + " rev=" + Store.Current.Revision
        + " map=" + Maps.ActiveMapId + " leases=" + ActiveLeaseCount + " pauses=" + PauseTokenCount + " handles=" + LiveHandleCount
        + " uiLocks=" + PresentationLocks + " disk=" + PendingDiskOperations + " moves=" + ActiveMoveCount + " events=" + ActiveEventCount
        + " saves=" + PendingSaveCount;
    }

    // ------------------------------------------------------------------ construction

    public FirstConnectionRuntime(FirstConnectionRuntimeConfig config)
    {
      if (config == null) throw new ArgumentNullException(nameof(config));
      if (string.IsNullOrWhiteSpace(config.FixtureRoot) || !Directory.Exists(config.FixtureRoot)) throw new DirectoryNotFoundException("fixture root not found: " + config.FixtureRoot);
      if (string.IsNullOrWhiteSpace(config.Variant)) throw new ArgumentException("variant is required", nameof(config));
      if (string.IsNullOrWhiteSpace(config.InitialSave)) throw new ArgumentException("initial save is required", nameof(config));
      if (string.IsNullOrWhiteSpace(config.SaveDirectory)) throw new ArgumentException("save directory is required", nameof(config));
      if (config.DialogueUi == null) throw new ArgumentNullException("config.DialogueUi");
      if (config.VisualResolver == null) throw new ArgumentNullException("config.VisualResolver");
      if (config.ActorRadius < 0 || config.PlayerSpeed <= 0 || config.StartupTimeoutSeconds <= 0 || config.ManualMoveTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(config));
      this.config = config;
      Phase = RuntimePhase.Starting;
      try
      {
        var root = Path.GetFullPath(config.FixtureRoot);
        var baseDir = Path.Combine(root, "base");
        var variantDir = Path.Combine(root, config.Variant);
        if (!Directory.Exists(baseDir) || !Directory.Exists(variantDir)) throw new DirectoryNotFoundException("base or variant folder missing under " + root);
        Codec = new JsonDataCodec();
        Schema = new StandardSchemaValidator(Codec);
        var runnerBundle = ReadBundle(Path.Combine(variantDir, "runner-bundle.json"));
        var compiler = new CatalogCompiler(new CallParser(), Schema);
        var compiledOutcome = compiler.Compile(ReadBundle(Path.Combine(baseDir, "full-catalog.json")), runnerBundle,
          ReadBundle(Path.Combine(baseDir, "schemas", "functions.json")), ReadBundle(Path.Combine(baseDir, "schemas", "catalog.schema.json")),
          ReadBundle(Path.Combine(baseDir, "schemas", "runner.schema.json")));
        if (!compiledOutcome.Succeeded) throw new InvalidOperationException("catalog compile failed: " + Describe(compiledOutcome.Result));
        Compiled = compiledOutcome.Value;
        saveSchema = ReadBundle(Path.Combine(baseDir, "schemas", "save.schema.json")).Root;
        Provider = new CatalogProvider(compiler, Compiled);

        var runnerRoot = Compiled.FindDefinition("runner", "root");
        DataNode mapsNode;
        if (runnerRoot == null || !runnerRoot.TryGet("maps", out mapsNode)) throw new InvalidOperationException("runner root has no maps");
        var layoutOutcome = MapLayout.Parse(mapsNode, runnerBundle.File);
        if (!layoutOutcome.Succeeded) throw new InvalidOperationException("map layout invalid: " + Describe(layoutOutcome.Result));
        Layout = layoutOutcome.Value;
        ReadActiveNpcs(runnerRoot);

        var savePath = Path.Combine(variantDir, config.InitialSave);
        if (!File.Exists(savePath)) savePath = Path.Combine(baseDir, config.InitialSave);
        if (!File.Exists(savePath)) throw new FileNotFoundException("initial save not found", config.InitialSave);
        var initial = new StateSnapshot(0, ReadBundle(savePath).Root);
        var shape = SaveFields.CheckRoot(initial.Root);
        if (shape.Count > 0) throw new InvalidOperationException("initial save is not the 45-field v2 object: " + shape[0].Code + " " + shape[0].Actual);
        var problems = new SaveStateValidator(Compiled, saveSchema, Schema).Validate(initial);
        if (problems.Count > 0) throw new InvalidOperationException("initial save invalid: " + problems[0].Code + " " + problems[0].Message);

        Pump = new CompletionPump();
        Handles = new HandleFactory(Pump);
        Pause = new PauseService();
        Leases = new LeaseService();
        Ticks = new TickClock(Handles);
        var userMap = config.MapConfig;
        Maps = new MapAdapter(Layout, Handles, new MapAdapterConfig(userMap == null ? null : userMap.TileFactory, (mapId, go) =>
        {
          if (FailMapId != null && FailMapId == mapId) return "injected map failure";
          return userMap == null || userMap.LoadVerifier == null ? null : userMap.LoadVerifier(mapId, go);
        }));
        CollisionPort = new GridCollisionPort(Layout);
        Actors = new ActorWorld(Maps, BuildActors(initial));
        Moves = new MoveService(Actors, CollisionPort, Maps, Maps, Handles, Pause, isSceneOwner: IsSceneOwner, revisionSource: () => Store.Current.Revision, requiresVisibleMap: NeedsVisibleMap);

        var calendar = new Tk.Calendar();
        var ledger = new StateCompletionLedger();
        var registry = new StateParticipantRegistry();
        registry.Register(new EventEffectsParticipant(Compiled));
        registry.Register(new WorldCommitParticipant(Actors, Maps));
        registry.Register(new Tk.ClockParticipant(calendar));
        registry.Register(new ReservationParticipant(Provider, ledger));
        registry.Seal();
        Store = new StateStore(initial, registry, new IStateInvariant[] { new SaveStateValidator(Compiled, saveSchema, Schema) }, Pump, new PersistedCompletionReplayPolicy());
        Store.OnCommitted(c => { if (!c.TransactionId.Value.StartsWith("clock.advance", StringComparison.Ordinal)) Emit("commit txn=" + c.TransactionId.Value + " " + c.PreviousRevision + "->" + c.Revision); });
        Clock = new Tk.ClockService(Store, calendar);
        Projector = new WorldStateProjector(Actors);
        Destinations = new CatalogDestinationResolver(Provider);
        Reservations = new ReservationService(Provider, calendar, ledger);

        Disk = new FileGenerationStore(config.SaveDirectory, Handles, 0, ctx => { if (FailDisk && ctx.Phase == StorageFaultPhase.CreateTemp) throw new IOException("injected disk failure"); });
        Policy = new SafeBoundaryPolicy();
        var restorer = new SaveRestorer(Codec, Schema, saveSchema, c => new SaveStateValidator(c, saveSchema, Schema), null);
        Saves = new SaveService(Policy, new SaveCodec(Codec), Disk, restorer, Store, Provider, Handles);

        var lookup = Compiled as ICompiledContentLookup;
        if (lookup == null) throw new InvalidOperationException("compiled catalog does not expose compiled call lookup");
        var scheduleCalls = new Dictionary<string, IReadOnlyList<CompiledCall>>(StringComparer.Ordinal);
        DataNode scheduleRows;
        if (runnerRoot.TryGet("schedules", out scheduleRows) && scheduleRows.Kind == DataKind.List)
        {
          foreach (var sr in scheduleRows.Items)
          {
            DataNode sid;
            if (sr.Kind != DataKind.Object || !sr.TryGet("id", out sid) || sid.Kind != DataKind.String) continue;
            var calls = lookup.FindCalls("runner", "schedules", sid.Text, "script");
            if (calls != null) scheduleCalls[sid.Text] = calls;
          }
        }
        Schedule = new ScheduleService(Compiled, scheduleCalls, Destinations);
        Scheduler = new ScheduleController(Schedule, Clock, Pause, Actors, Moves, Handles, activeNpcs.Select(n => n.Id).ToList(), npc => Leases.HolderOf(npc) != null);
        Staging = new ReservedStagingService(Actors, Maps, CollisionPort, Handles, PlanJoin, SetHidden, ids => Scheduler.StopActors(ids));
        LoadRecipes(variantDir, baseDir);

        var planner = new EventCommitPlanner(EffectPlanner.CreateMinimal(Compiled), Clock, Projector, Compiled);
        var deps = new EventRunnerDependencies
        {
          Catalog = Provider,
          Conditions = new ConditionEvaluator(Provider),
          Leases = Leases,
          Pauses = Pause,
          Moves = Moves,
          Destinations = Destinations,
          Presentation = config.DialogueUi,
          PresentationLifetime = config.DialogueUi,
          Clock = Ticks,
          CommitPlanner = planner,
          Store = Store,
          Projector = Projector,
          Recovery = new SceneRecovery(Actors),
          Boundaries = Policy,
          Saves = Saves,
          Staging = Staging,
          Maps = Maps,
          Handles = Handles,
          RepeatBlocked = (e, s) => ledger.IsCompleted(s, e.Completion ?? new CompletionKey(e.Id)),
          Trace = OnRunnerTrace
        };
        Runner = new EventRunner(deps);
        config.DialogueUi.Configure(Handles, Pause, Maps, config.VisualResolver);

        Policy.Renew(SafeBoundary.WorldSafe, Store.Current, false);
        Emit("runtime.created variant=" + config.Variant + " catalog=" + Compiled.CatalogHash);
        if (config.ResumeFromDisk && Disk.ListCandidates().Count > 0) StartLoad(null, true);
        else LoadCurrentMap(FinishStartup);
      }
      catch
      {
        if (Maps != null) Maps.Dispose();
        Phase = RuntimePhase.Faulted;
        throw;
      }
    }

    // ------------------------------------------------------------------ frame

    /// <summary>Call after the external completion pump and input polling. dt is the unpaused frame delta; each service gates itself by pause channel.</summary>
    public void Step(double dt)
    {
      if (Phase == RuntimePhase.Disposed) throw new ObjectDisposedException(nameof(FirstConnectionRuntime));
      if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
      if (inStep) throw new InvalidOperationException("Step re-entered");
      inStep = true;
      try
      {
        if (config.PumpInStep) Pump.AdvanceTick();
        Ticks.Advance(Pause.Effective.Scene ? 0.0 : dt);
        Watchdog(dt);
        Disk.Tick();
        Maps.Tick();
        Staging.Tick(dt);
        Moves.Tick(dt);
        config.DialogueUi.Tick(dt);
        Runner.Tick(dt);
        if (Phase == RuntimePhase.Ready)
        {
          Clock.Advance(dt, Pause.Effective);
          var failure = Clock.LastFailure;
          var code = failure == null ? "" : failure.Code;
          if (code != lastClockFailure) { lastClockFailure = code; if (failure != null) Diag("clock advance failed: " + Describe(failure)); }
          Scheduler.Tick(dt);
        }
      }
      finally { inStep = false; }
    }

    void Watchdog(double dt)
    {
      if (Phase != RuntimePhase.Starting && Phase != RuntimePhase.Loading) return;
      opElapsed += dt;
      if (opElapsed < config.StartupTimeoutSeconds || cancelSent) return;
      cancelSent = true;
      Diag("watchdog: " + Phase + " exceeded " + config.StartupTimeoutSeconds + "s");
      var op = currentOp;
      if (op != null && op.Terminal == null) op.Cancel("watchdog");
      else Fault("watchdog expired without a pending operation");
    }

    // ------------------------------------------------------------------ user operations

    /// <summary>Commits the reservation accept (plus a world projection), notifies WorldSafe and saves to disk. The handle succeeds only after the physical save.</summary>
    public IHandle<bool> Accept(string eventId)
    {
      var h = NewOp("accept");
      var blocked = Block();
      if (blocked != null) return Refuse(h, ResultCodes.ResourceBusy, blocked);
      if (string.IsNullOrEmpty(eventId)) return Refuse(h, ResultCodes.ContentInvalid, "event id is required");
      var basis = Store.Current;
      var plan = Reservations.PlanAccept(eventId, basis);
      if (!plan.Succeeded) return FailWith(h, plan.Result);
      var changes = new List<PlannedChange> { plan.Value };
      var projection = Projector.PlanProjection(basis);
      if (!projection.Succeeded) return FailWith(h, projection.Result);
      changes.AddRange(projection.Value.Changes);
      var committed = Store.Commit(new StatePlan(new TransactionId("accept." + eventId + "." + (++txnSeq)), null, basis.Revision, changes));
      if (!committed.Succeeded) return FailWith(h, committed.Result);
      acceptUnsaved = true;
      Policy.Notify(SafeBoundary.WorldSafe, Store.Current);
      Emit("accept.commit " + eventId);
      BeginSave(h, "accept");
      return h;
    }

    /// <summary>Explicit start; one runner start per call with a unique input sequence, owner token and transaction. Never retried automatically.</summary>
    public IHandle<EventCompletion> StartEvent(string eventId)
    {
      var blocked = Block();
      if (blocked != null) return Failed<EventCompletion>(ResultCodes.ResourceBusy, blocked);
      if (acceptUnsaved) return Failed<EventCompletion>(ResultCodes.ConditionFalse, "the accept has not been saved to disk; retry the save first");
      if (string.IsNullOrEmpty(eventId)) return Failed<EventCompletion>(ResultCodes.ContentInvalid, "event id is required");
      var seq = ++inputSeq;
      IHandle<EventCompletion> handle;
      try
      {
        handle = Runner.Start(new EventStartRequest(eventId, SceneOwnerPrefix + eventId + "#" + seq, seq, new TransactionId("start." + eventId + "." + seq)));
      }
      catch (ArgumentException e) { return Failed<EventCompletion>(ResultCodes.ContentInvalid, e.Message); }
      LastEventHandle = handle;
      Emit("start " + eventId + " seq=" + seq);
      handle.OnTerminal(r => Emit("event.terminal " + eventId + " " + r.Status + " " + r.Code));
      return handle;
    }

    /// <summary>Cancels the newest event. Safe to call repeatedly.</summary>
    public bool Cancel(string reason = "cancel")
    {
      var h = LastEventHandle;
      if (h == null || h.Terminal != null) return false;
      h.Cancel(reason ?? "cancel");
      return true;
    }

    public IHandle<bool> RetrySave()
    {
      var h = NewOp("retry-save");
      if (Phase != RuntimePhase.Ready) return Refuse(h, ResultCodes.ResourceBusy, "runtime is not ready: " + Phase);
      if (saveOp != null) return Refuse(h, ResultCodes.ResourceBusy, "a save is in progress");
      if (Policy.Latest == null) return Refuse(h, ResultCodes.ConditionFalse, "no safe boundary has been reached yet");
      if (!Policy.SceneActive && Runner.ActiveCount == 0 && Runner.PendingSaveCount == 0)
      {
        if (manualHandle != null && manualHandle.Terminal == null) return Refuse(h, ResultCodes.ResourceBusy, "stop walking before saving");
        var basis = Store.Current;
        var projection = Projector.PlanProjection(basis);
        if (!projection.Succeeded) return FailWith(h, projection.Result);
        var commit = Store.Commit(new StatePlan(new TransactionId("world.save." + (++txnSeq)), null, basis.Revision, projection.Value.Changes));
        if (!commit.Succeeded) return FailWith(h, commit.Result);
        Policy.Notify(SafeBoundary.WorldSafe, Store.Current);
      }
      BeginSave(h, "retry");
      return h;
    }

    /// <summary>Loads the newest restorable generation, rebuilds the actor world from the whole state and reloads the real map before Ready.</summary>
    public IHandle<bool> LoadLatest()
    {
      var h = NewOp("load");
      if (Phase == RuntimePhase.Disposed) return Refuse(h, ResultCodes.ResourceBusy, "disposed");
      if (Phase == RuntimePhase.Loading || Phase == RuntimePhase.Starting) return Refuse(h, ResultCodes.ResourceBusy, "runtime is " + Phase);
      if (Runner.ActiveCount > 0 || Staging.ActiveCount > 0) return Refuse(h, ResultCodes.ResourceBusy, "no loads while an event is running");
      if (saveOp != null) return Refuse(h, ResultCodes.ResourceBusy, "a save is in progress");
      StartLoad(h, false);
      return h;
    }

    /// <summary>Explicit retry of the real map load for the current player map (after an injected or real map failure).</summary>
    public IHandle<bool> RetryMapLoad()
    {
      var h = NewOp("map-retry");
      if (Phase != RuntimePhase.Faulted && Phase != RuntimePhase.Ready) return Refuse(h, ResultCodes.ResourceBusy, "runtime is " + Phase);
      if (Runner.ActiveCount > 0 || Staging.ActiveCount > 0) return Refuse(h, ResultCodes.ResourceBusy, "an event is running");
      Phase = RuntimePhase.Loading; opElapsed = 0; cancelSent = false;
      h.SetCancellationHandler(reason => { var op = currentOp; if (op != null) op.Cancel(reason); });
      LoadCurrentMap(r =>
      {
        if (Phase == RuntimePhase.Disposed) return;
        if (r.Status == OperationStatus.Succeeded) Phase = RuntimePhase.Ready; else Fault("map retry failed: " + Describe(r));
        Complete(h, r);
      });
      return h;
    }

    public IHandle<MoveArrival> ManualMove(string destinationId)
    {
      var dest = Destinations.Resolve(PlayerId, destinationId);
      if (!dest.Succeeded) return Failed<MoveArrival>(dest.Result.Code, Describe(dest.Result));
      return ManualMove(dest.Value);
    }

    public IHandle<MoveArrival> ManualMove(Position target, double tolerance = 0.2)
    {
      try { return ManualMove(new Destination(string.Empty, target, tolerance)); }
      catch (ArgumentException e) { return Failed<MoveArrival>(ResultCodes.ContentInvalid, e.Message); }
    }

    /// <summary>Walks the player through the same MoveService under a short-lived input.player lease.</summary>
    public IHandle<MoveArrival> ManualMove(Destination destination)
    {
      if (destination == null) return Failed<MoveArrival>(ResultCodes.ContentInvalid, "destination is required");
      if (Phase != RuntimePhase.Ready) return Failed<MoveArrival>(ResultCodes.ResourceBusy, "runtime is not ready: " + Phase);
      if (manualHandle != null && manualHandle.Terminal == null) return Failed<MoveArrival>(ResultCodes.ResourceBusy, "a manual move is already running");
      var seq = ++inputSeq;
      var owner = "player:move#" + seq;
      var lease = Leases.TryAcquire(new LeaseRequest(owner, new[] { PlayerResource }));
      if (!lease.Succeeded) return Failed<MoveArrival>(lease.Result.Code, Describe(lease.Result));
      var leaseId = lease.Value.LeaseId;
      IHandle<MoveArrival> move;
      try { move = Moves.Move(new MoveRequest(PlayerId, destination, owner, config.ManualMoveTimeoutSeconds, true)); }
      catch (Exception e)
      {
        Leases.Release(leaseId);
        return Failed<MoveArrival>(ResultCodes.ContentInvalid, e.Message);
      }
      manualHandle = move;
      Emit("manual.move " + owner + " to " + destination.Target);
      move.OnTerminal(r =>
      {
        Leases.Release(leaseId);
        if (ReferenceEquals(manualHandle, move)) manualHandle = null;
        Emit("manual.move.terminal " + owner + " " + r.Status + " " + r.Code);
      });
      return move;
    }

    public void CancelManualMove(string reason = "cancel")
    {
      var m = manualHandle;
      if (m != null && m.Terminal == null) m.Cancel(reason ?? "cancel");
    }

    // ------------------------------------------------------------------ staging (data driven)

    Outcome<ReservedJoinPlan> PlanJoin(CompiledEvent def, Lease lease)
    {
      var rev = Store.Current.Revision;
      CompiledCall first = null;
      foreach (var c in def.Script)
        if (c.Function.Role == FunctionRole.Command && c.Function.Name == "move") { first = c; break; }
      DataNode actorArg, destArg;
      if (first == null || !first.Args.TryGetValue("actor", out actorArg) || actorArg.Kind != DataKind.String || !first.Args.TryGetValue("destination", out destArg) || destArg.Kind != DataKind.String)
        return JoinFail(rev, ResultCodes.ContentInvalid, "reserved staging needs a first move with actor and destination in " + def.Id);
      var dest = Destinations.Resolve(actorArg.Text, destArg.Text);
      if (!dest.Succeeded) return Outcome<ReservedJoinPlan>.Fail(dest.Result.Code, rev, new List<Diagnostic>(dest.Result.Details).ToArray());
      var target = dest.Value.Target.MapId;
      var player = Actors.Find(PlayerId);
      if (player != null && player.Position.MapId != target && FindArrival(target, player.Position.MapId) == null)
        return JoinFail(rev, ResultCodes.NoRoute, "no open incoming portal for reserved staging on " + target);
      Dictionary<string, Position> recipe;
      if (!recipes.TryGetValue(def.Id, out recipe)) recipe = null;
      var joiners = new List<string>();
      var held = new HashSet<string>(lease.Resources, StringComparer.Ordinal);
      foreach (var res in lease.Resources)
        if (res != PlayerId && Actors.Find(res) != null) joiners.Add(res);
      if (held.Contains(PlayerResource) && player != null) joiners.Add(PlayerId);
      Position? arrival = null;
      var placements = new List<ReservedActorPlacement>();
      foreach (var id in joiners)
      {
        var actor = Actors.Find(id);
        Position at;
        if (recipe != null && recipe.TryGetValue(id, out at)) { }
        else if (actor.Position.MapId == target) continue;
        else
        {
          if (arrival == null)
          {
            arrival = FindArrival(target, player == null ? actor.Position.MapId : player.Position.MapId);
            if (arrival == null) return JoinFail(rev, ResultCodes.NoRoute, "no open portal arrives on " + target);
          }
          at = arrival.Value;
        }
        placements.Add(new ReservedActorPlacement(id, at));
      }
      try { return Outcome<ReservedJoinPlan>.Ok(new ReservedJoinPlan(target, placements), rev); }
      catch (ArgumentException e) { return JoinFail(rev, ResultCodes.ContentInvalid, e.Message); }
    }

    Position? FindArrival(string target, string fromMap)
    {
      Portal best = null;
      foreach (var mapId in Layout.MapIds)
      {
        if (mapId == target) continue;
        foreach (var p in Layout.PortalsOn(mapId))
        {
          if (!p.Open || p.Arrival.MapId != target) continue;
          if (best == null || (p.From.MapId == fromMap && best.From.MapId != fromMap)) best = p;
        }
      }
      if (best == null) return null;
      return best.Arrival;
    }

    static Outcome<ReservedJoinPlan> JoinFail(long rev, string code, string message)
    {
      return Outcome<ReservedJoinPlan>.Fail(code, rev, new Diagnostic(code, message, null, string.Empty, string.Empty));
    }

    void LoadRecipes(string variantDir, string baseDir)
    {
      var path = Path.Combine(variantDir, "staging-recipes.json");
      if (!File.Exists(path)) path = Path.Combine(baseDir, "staging-recipes.json");
      if (!File.Exists(path)) { Diag("no staging-recipes.json; join positions use the incoming portal arrival only"); return; }
      var text = File.ReadAllText(path, Encoding.UTF8);
      var parsed = Codec.Parse(path, Encoding.UTF8.GetBytes("{" + Q + "rows" + Q + ":" + text + "}"));
      if (!parsed.Succeeded) { Diag("staging recipes unreadable: " + Describe(parsed.Result)); return; }
      DataNode rows;
      if (!parsed.Value.Root.TryGet("rows", out rows) || rows.Kind != DataKind.List) return;
      foreach (var row in rows.Items)
      {
        DataNode eid, join;
        if (row.Kind != DataKind.Object || !row.TryGet("eventId", out eid) || eid.Kind != DataKind.String) continue;
        var joinMap = new Dictionary<string, Position>(StringComparer.Ordinal);
        if (row.TryGet("join", out join) && join.Kind == DataKind.List)
        {
          foreach (var j in join.Items)
          {
            DataNode actor, at;
            if (j.Kind != DataKind.Object || !j.TryGet("actorId", out actor) || actor.Kind != DataKind.String || !j.TryGet("joinAt", out at)) continue;
            try { joinMap[actor.Text] = ReadPosition(at, actor.Text); }
            catch (InvalidOperationException e) { Diag(e.Message); }
          }
        }
        recipes[eid.Text] = joinMap;
      }
    }

    void SetHidden(bool hidden)
    {
      if (Hidden == hidden) return;
      Hidden = hidden;
      Emit("hidden " + hidden);
      try { var handler = HiddenChanged; if (handler != null) handler(hidden); }
      catch (Exception e) { Diag("HiddenChanged handler threw: " + e.Message); }
    }

    // ------------------------------------------------------------------ startup / load

    void LoadCurrentMap(Action<Result> done)
    {
      string mapId;
      try { mapId = PlayerMapId(); }
      catch (Exception e) { done(Failure("PLAYER_MAP_MISSING", e.Message)); return; }
      if (Maps.ActiveMapId == mapId) { done(new Result(OperationStatus.Succeeded, ResultCodes.Ok, Store.Current.Revision, null)); return; }
      IHandle load;
      try { load = Maps.Load(mapId, RuntimeOwner); }
      catch (Exception e) { done(Failure(ResultCodes.MapLoadFailed, e.Message)); return; }
      currentOp = load;
      Emit("map.load " + mapId);
      load.OnTerminal(r => { if (ReferenceEquals(currentOp, load)) currentOp = null; done(r); });
    }

    void FinishStartup(Result r)
    {
      if (Phase == RuntimePhase.Disposed) return;
      if (r.Status == OperationStatus.Succeeded) { Phase = RuntimePhase.Ready; Emit("ready map=" + Maps.ActiveMapId); }
      else Fault("startup map load failed: " + Describe(r));
    }

    void StartLoad(IHandleController<bool> h, bool startup)
    {
      var prior = Phase;
      CancelManualMove("reload");
      config.DialogueUi.CloseAll("reload");
      Phase = RuntimePhase.Loading; opElapsed = 0; cancelSent = false;
      var stop = Scheduler.StopActors(activeNpcs.Select(n => n.Id).ToList());
      currentOp = stop;
      if (h != null) h.SetCancellationHandler(reason => { var op = currentOp; if (op != null) op.Cancel(reason); });
      stop.OnTerminal(r =>
      {
        if (Phase == RuntimePhase.Disposed) return;
        currentOp = null;
        if (r.Status != OperationStatus.Succeeded) { Fault("ambient stop before load failed: " + Describe(r)); Complete(h, r); return; }
        BeginDiskLoad(h, startup, prior);
      });
    }

    void BeginDiskLoad(IHandleController<bool> h, bool startup, RuntimePhase prior)
    {
      var save = Saves.LoadLatest();
      currentOp = save;
      if (h != null) h.SetCancellationHandler(reason => { var op = currentOp; if (op != null) op.Cancel(reason); });
      Emit("load.begin");
      save.OnTerminal(r =>
      {
        if (Phase == RuntimePhase.Disposed) return;
        currentOp = null;
        if (r.Status != OperationStatus.Succeeded)
        {
          if (startup) { Diag("resume found no restorable save: " + Describe(r)); Phase = RuntimePhase.Starting; opElapsed = 0; cancelSent = false; LoadCurrentMap(FinishStartup); }
          else { Phase = prior == RuntimePhase.Ready ? RuntimePhase.Ready : RuntimePhase.Faulted; Complete(h, r); }
          return;
        }
        Result restored;
        try { restored = Actors.Restore(BuildActors(Store.Current)); }
        catch (Exception e) { restored = Failure("ACTOR_REBUILD_FAILED", e.Message); }
        if (restored.Status != OperationStatus.Succeeded)
        {
          Fault("actor world restore failed: " + Describe(restored));
          Complete(h, restored);
          return;
        }
        SetHidden(false);
        acceptUnsaved = false;
        Emit("load.state-restored");
        LoadCurrentMap(mr =>
        {
          if (Phase == RuntimePhase.Disposed) return;
          if (mr.Status == OperationStatus.Succeeded) { Phase = RuntimePhase.Ready; Emit("load.ready map=" + Maps.ActiveMapId); }
          else Fault("map restore failed: " + Describe(mr));
          Complete(h, mr);
        });
      });
    }

    void BeginSave(IHandleController<bool> h, string why)
    {
      var save = Saves.SaveLatestSafe();
      saveOp = save;
      h.SetCancellationHandler(reason => save.Cancel(reason));
      save.OnTerminal(r =>
      {
        if (ReferenceEquals(saveOp, save)) saveOp = null;
        if (Phase == RuntimePhase.Disposed) return;
        if (r.Status == OperationStatus.Succeeded) { acceptUnsaved = false; Emit(why + ".saved generation=" + save.Value.Generation); }
        else Diag(why + " save failed (memory state kept, unsaved): " + Describe(r));
        Complete(h, r);
      });
    }

    // ------------------------------------------------------------------ world rebuild

    List<ActorState> BuildActors(StateSnapshot s)
    {
      var list = new List<ActorState>();
      var pl = s.Slice("player");
      if (pl == null) throw new InvalidOperationException("save has no player slice");
      list.Add(new ActorState(PlayerId, ReadPosition(pl, PlayerId), config.ActorRadius, config.PlayerSpeed));
      var saved = new Dictionary<string, DataNode>(StringComparer.Ordinal);
      var positions = s.Slice("npcPositions");
      if (positions != null && positions.Kind == DataKind.List)
      {
        foreach (var entry in positions.Items)
        {
          DataNode idNode;
          if (entry.Kind == DataKind.Object && entry.TryGet("npcId", out idNode) && idNode.Kind == DataKind.String) saved[idNode.Text] = entry;
        }
      }
      foreach (var npc in activeNpcs)
      {
        DataNode found;
        var at = saved.TryGetValue(npc.Id, out found) ? ReadPosition(found, npc.Id) : npc.Home;
        list.Add(new ActorState(npc.Id, at, config.ActorRadius, npc.Speed));
      }
      return list;
    }

    void ReadActiveNpcs(DataNode runnerRoot)
    {
      DataNode list;
      if (!runnerRoot.TryGet("npcs", out list) || list.Kind != DataKind.List) return;
      foreach (var n in list.Items)
      {
        DataNode id, pos, speedNode;
        if (n.Kind != DataKind.Object || !n.TryGet("id", out id) || id.Kind != DataKind.String) continue;
        if (!n.TryGet("position", out pos)) throw new InvalidOperationException("runner npc " + id.Text + " has no position");
        double speed = 3, parsed;
        if (n.TryGet("speed", out speedNode) && TryNum(speedNode, out parsed) && parsed > 0) speed = parsed;
        activeNpcs.Add(new ActiveNpc { Id = id.Text, Home = ReadPosition(pos, id.Text), Speed = speed });
      }
    }

    string PlayerMapId()
    {
      var pl = Store.Current.Slice("player");
      DataNode map;
      if (pl == null || !pl.TryGet("mapId", out map) || map.Kind != DataKind.String) throw new InvalidOperationException("save has no player mapId");
      return map.Text;
    }

    static Position ReadPosition(DataNode n, string who)
    {
      DataNode map, xn, yn;
      double x, y;
      if (n == null || n.Kind != DataKind.Object || !n.TryGet("mapId", out map) || map.Kind != DataKind.String || !n.TryGet("x", out xn) || !TryNum(xn, out x) || !n.TryGet("y", out yn) || !TryNum(yn, out y))
        throw new InvalidOperationException("invalid position for " + who);
      return new Position(map.Text, x, y);
    }

    static bool TryNum(DataNode n, out double v)
    {
      v = 0;
      if (n == null) return false;
      if (n.Kind == DataKind.Integer) { v = n.Integer; return true; }
      if (n.Kind == DataKind.Number) { v = n.Number; return true; }
      return false;
    }

    static bool IsSceneOwner(string owner) { return owner != null && owner.StartsWith(SceneOwnerPrefix, StringComparison.Ordinal); }

    // Only the player or a scene-owned move drives the displayed map; ambient NPC moves never change it.
    static bool NeedsVisibleMap(MoveRequest r) { return r.ActorId == PlayerId || IsSceneOwner(r.OwnerToken); }

    // ------------------------------------------------------------------ helpers

    ContentBundle ReadBundle(string path)
    {
      if (!File.Exists(path)) throw new FileNotFoundException("fixture file missing", path);
      var parsed = Codec.Parse(path, File.ReadAllBytes(path));
      if (!parsed.Succeeded) throw new InvalidOperationException("parse failed " + path + ": " + Describe(parsed.Result));
      return parsed.Value;
    }

    string Block()
    {
      if (Phase != RuntimePhase.Ready) return "runtime is not ready: " + Phase;
      if (saveOp != null) return "a save is in progress";
      if (Runner.ActiveCount > 0 || Staging.ActiveCount > 0) return "an event is running";
      return null;
    }

    IHandleController<bool> NewOp(string name)
    {
      var h = Handles.Create<bool>("runtime." + name + "." + (++opSeq));
      h.Start();
      return h;
    }

    IHandleController<bool> Refuse(IHandleController<bool> h, string code, string message)
    {
      h.Fail(code, Store.Current.Revision, new[] { new Diagnostic(code, message, null, string.Empty, string.Empty) });
      Emit("refused " + code + " " + message);
      return h;
    }

    IHandleController<bool> FailWith(IHandleController<bool> h, Result r)
    {
      h.Fail(r.Code, Store.Current.Revision, r.Details);
      Emit("failed " + Describe(r));
      return h;
    }

    IHandleController<T> Failed<T>(string code, string message)
    {
      var h = Handles.Create<T>("runtime.refused." + (++opSeq));
      h.Start();
      h.Fail(code, Store.Current.Revision, new[] { new Diagnostic(code, message, null, string.Empty, string.Empty) });
      Emit("refused " + code + " " + message);
      return h;
    }

    void Complete(IHandleController<bool> h, Result r)
    {
      if (h == null) return;
      var rev = Store.Current.Revision;
      if (r.Status == OperationStatus.Succeeded) h.Succeed(true, rev);
      else if (r.Status == OperationStatus.Cancelled) h.Cancelled("cancelled", rev);
      else h.Fail(r.Code, rev, r.Details);
    }

    Result Failure(string code, string message)
    {
      return new Result(OperationStatus.Failed, code, Store == null ? 0 : Store.Current.Revision, new[] { new Diagnostic(code, message, null, string.Empty, string.Empty) });
    }

    void Fault(string why)
    {
      Phase = RuntimePhase.Faulted;
      LastError = why;
      Diag(why);
    }

    void OnRunnerTrace(EventTraceEntry e)
    {
      Emit("runner tick=" + e.Tick + " rseq=" + e.Sequence + " rrev=" + e.Revision + " event=" + e.EventId + " phase=" + e.Phase + " call=" + e.Call);
    }

    void Diag(string message)
    {
      diagnostics.Add(message);
      if (diagnostics.Count > 512) diagnostics.RemoveAt(0);
      Emit("diag " + message);
    }

    void Emit(string message)
    {
      var line = "tick=" + (Ticks == null ? 0 : Ticks.TickCount) + " seq=" + (++traceSeq) + " rev=" + (Store == null ? 0 : Store.Current.Revision) + " " + message;
      traceLog.Add(line);
      if (traceLog.Count > 4000) traceLog.RemoveAt(0);
      var sink = config.Trace;
      if (sink == null) return;
      try { sink(line); }
      catch (Exception) { }
    }

    static string Describe(Result r)
    {
      var sb = new StringBuilder(r.Status + ":" + r.Code);
      foreach (var d in r.Details) sb.Append("; ").Append(d.Code).Append(' ').Append(d.Message);
      return sb.ToString();
    }

    // ------------------------------------------------------------------ cleanup

    public void Dispose()
    {
      if (Phase == RuntimePhase.Disposed) return;
      try
      {
        Cancel("dispose");
        CancelManualMove("dispose");
        config.DialogueUi.CloseAll("dispose");
        Staging.Teardown("dispose");
        var op = currentOp;
        if (op != null && op.Terminal == null) op.Cancel("dispose");
        var save = saveOp;
        if (save != null && save.Terminal == null) save.Cancel("dispose");
        for (var i = 0; i < 10; i++)
        {
          Pump.AdvanceTick();
          Disk.Tick();
          Staging.Tick(0.01);
          Moves.Tick(0.01);
          Runner.Tick(0.01);
        }
        Disk.Shutdown();
        Pump.AdvanceTick();
      }
      catch (Exception e) { Diag("dispose settle failed: " + e.Message); }
      Phase = RuntimePhase.Disposed;
      try { Maps.Dispose(); Pump.AdvanceTick(); }
      catch (Exception) { }
    }
  }
}
