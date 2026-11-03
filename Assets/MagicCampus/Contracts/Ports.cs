using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace MagicCampus.Contracts
{
    // Task 1-1 port contracts: immutable DTOs and interfaces only, no implementations.
    // Units as in Core.cs. Collection arguments are copied; async work is only exposed through IHandle.

    internal static class PortGuard
    {
        internal static double Finite(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentOutOfRangeException(name);
            return v;
        }

        internal static double NonNegative(double v, string name)
        {
            if (Finite(v, name) < 0) throw new ArgumentOutOfRangeException(name);
            return v;
        }

        internal static double Positive(double v, string name)
        {
            if (Finite(v, name) <= 0) throw new ArgumentOutOfRangeException(name);
            return v;
        }

        internal static long Min(long v, long min, string name)
        {
            if (v < min) throw new ArgumentOutOfRangeException(name);
            return v;
        }

        internal static T Required<T>(T v, string name) where T : class
        {
            if (v == null) throw new ArgumentNullException(name);
            return v;
        }

        internal static Position Placed(Position p, string name)
        {
            if (p.MapId == null) throw new ArgumentException("default position", name);
            return p;
        }

        internal static IReadOnlyList<T> Opt<T>(IEnumerable<T> src, string name) where T : class
            => Ids.Copy(src ?? Array.Empty<T>(), name);

        /// <summary>Validated, distinct, ordinal-sorted id set.</summary>
        internal static IReadOnlyList<string> IdSet(IEnumerable<string> src, string name)
        {
            var s = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var e in Ids.Copy(src ?? Array.Empty<string>(), name)) s.Add(Ids.Require(e, name));
            return Array.AsReadOnly(new List<string>(s).ToArray());
        }

        internal static IReadOnlyDictionary<string, T> Map<T>(IEnumerable<KeyValuePair<string, T>> src, string name) where T : class
        {
            var d = new SortedDictionary<string, T>(StringComparer.Ordinal);
            if (src != null)
                foreach (var kv in src)
                {
                    if (kv.Key == null || kv.Value == null) throw new ArgumentException("null key or value", name);
                    if (d.ContainsKey(kv.Key)) throw new ArgumentException("duplicate key", name);
                    d.Add(kv.Key, kv.Value);
                }
            return new ReadOnlyDictionary<string, T>(d);
        }

        internal static IReadOnlyDictionary<string, IReadOnlyList<T>> Lists<T>(IEnumerable<KeyValuePair<string, IReadOnlyList<T>>> src, string name) where T : class
        {
            var tmp = new List<KeyValuePair<string, IReadOnlyList<T>>>();
            if (src != null)
                foreach (var kv in src) tmp.Add(new KeyValuePair<string, IReadOnlyList<T>>(kv.Key, Ids.Copy(kv.Value, name)));
            return Map(tmp, name);
        }

        internal static IReadOnlyList<IReadOnlyList<T>> Nested<T>(IEnumerable<IReadOnlyList<T>> src, string name) where T : class
        {
            var tmp = new List<IReadOnlyList<T>>();
            if (src != null)
                foreach (var s in src) tmp.Add(Ids.Copy(s, name));
            return Array.AsReadOnly(tmp.ToArray());
        }
    }

    public static class ResultCodes
    {
        public const string Ok = "OK", ContentInvalid = "CONTENT_INVALID", ConditionFalse = "CONDITION_FALSE", ResourceBusy = "RESOURCE_BUSY", NoRoute = "NO_ROUTE", TargetBlocked = "TARGET_BLOCKED";
        public const string MapLoadFailed = "MAP_LOAD_FAILED", Timeout = "TIMEOUT", Cancelled = "CANCELLED", InsufficientItems = "INSUFFICIENT_ITEMS", InsufficientFunds = "INSUFFICIENT_FUNDS";
        public const string OutOfScope = "OUT_OF_SCOPE", SaveIncompatible = "SAVE_INCOMPATIBLE", RevisionConflict = "REVISION_CONFLICT", Unregistered = "UNREGISTERED";
        public const string TransactionMismatch = "TRANSACTION_MISMATCH", InvariantViolation = "INVARIANT_VIOLATION", SliceOverlap = "SLICE_OVERLAP";
        public const string ParseError = "PARSE_ERROR", UnknownFunction = "UNKNOWN_FUNCTION", ArgCount = "ARG_COUNT", ArgType = "ARG_TYPE", RefNotFound = "REF_NOT_FOUND", RefKind = "REF_KIND";
        public const string LabelDup = "LABEL_DUP", ForkNotFound = "FORK_NOT_FOUND", ForkUnused = "FORK_UNUSED", ForkCycle = "FORK_CYCLE";
        public const string HandleUnawaited = "HANDLE_UNAWAITED", HandleUndefined = "HANDLE_UNDEFINED", HandleDuplicate = "HANDLE_DUPLICATE", HandleConflict = "HANDLE_CONFLICT";
    }

    /// <summary>Result plus a value that is non-null only on success. Failures always carry diagnostics in Result.Details.</summary>
    public sealed class Outcome<T> where T : class
    {
        public Result Result { get; }
        public T Value { get; }
        public bool Succeeded => Result.Status == OperationStatus.Succeeded;

        private Outcome(Result result, T value) { Result = result; Value = value; }

        public static Outcome<T> Ok(T value, long revision) =>
            new Outcome<T>(new Result(OperationStatus.Succeeded, ResultCodes.Ok, revision, null), PortGuard.Required(value, nameof(value)));

        public static Outcome<T> Fail(string code, long revision, params Diagnostic[] details) =>
            new Outcome<T>(new Result(OperationStatus.Failed, code, revision, details), null);
    }

    /// <summary>Handle that carries a typed result. Value is meaningful only once Terminal.Status is Succeeded.</summary>
    public interface IHandle<out T> : IHandle { T Value { get; } }

    // ---------------------------------------------------------------- immutable JSON tree (original save v2 values)

    public enum DataKind { Null, Bool, Integer, Number, String, List, Object }

    /// <summary>Immutable JSON value. Integer and Number are distinct kinds (7.5 stays Number). Object members are ordinal-sorted and unique.</summary>
    public sealed class DataNode : IEquatable<DataNode>
    {
        public static readonly DataNode Null = new DataNode(DataKind.Null, false, 0, 0, null, null, null);
        public static readonly DataNode True = new DataNode(DataKind.Bool, true, 0, 0, null, null, null);
        public static readonly DataNode False = new DataNode(DataKind.Bool, false, 0, 0, null, null, null);

        public DataKind Kind { get; }
        public bool Bool { get; }
        public long Integer { get; }
        public double Number { get; }
        public string Text { get; }
        public IReadOnlyList<DataNode> Items { get; }
        public IReadOnlyDictionary<string, DataNode> Members { get; }

        private DataNode(DataKind kind, bool b, long i, double n, string text, IEnumerable<DataNode> items, IEnumerable<KeyValuePair<string, DataNode>> members)
        {
            Kind = kind; Bool = b; Integer = i; Number = n; Text = text;
            Items = PortGuard.Opt(items, nameof(items));
            Members = PortGuard.Map(members, nameof(members));
        }

        public static DataNode FromBool(bool v) => v ? True : False;
        public static DataNode FromInteger(long v) => new DataNode(DataKind.Integer, false, v, 0, null, null, null);
        public static DataNode FromNumber(double v) => new DataNode(DataKind.Number, false, 0, PortGuard.Finite(v, nameof(v)), null, null, null);
        public static DataNode FromString(string v) => new DataNode(DataKind.String, false, 0, 0, PortGuard.Required(v, nameof(v)), null, null);
        public static DataNode FromList(IEnumerable<DataNode> items) => new DataNode(DataKind.List, false, 0, 0, null, PortGuard.Required(items, nameof(items)), null);
        public static DataNode FromObject(IEnumerable<KeyValuePair<string, DataNode>> members) => new DataNode(DataKind.Object, false, 0, 0, null, null, PortGuard.Required(members, nameof(members)));

        public bool TryGet(string key, out DataNode value) => Members.TryGetValue(key, out value);

        /// <summary>Copy of an Object node with one member added or replaced.</summary>
        public DataNode With(string key, DataNode value)
        {
            if (Kind != DataKind.Object) throw new InvalidOperationException();
            var m = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            foreach (var kv in Members) m[kv.Key] = kv.Value;
            m[PortGuard.Required(key, nameof(key))] = PortGuard.Required(value, nameof(value));
            return FromObject(m);
        }

        public bool Equals(DataNode o)
        {
            if (ReferenceEquals(o, this)) return true;
            if (o == null || o.Kind != Kind) return false;
            switch (Kind)
            {
                case DataKind.Bool: return Bool == o.Bool;
                case DataKind.Integer: return Integer == o.Integer;
                case DataKind.Number: return Number == o.Number;
                case DataKind.String: return Text == o.Text;
                case DataKind.List:
                    if (Items.Count != o.Items.Count) return false;
                    for (var i = 0; i < Items.Count; i++) if (!Items[i].Equals(o.Items[i])) return false;
                    return true;
                case DataKind.Object:
                    if (Members.Count != o.Members.Count) return false;
                    foreach (var kv in Members)
                        if (!o.Members.TryGetValue(kv.Key, out var v) || !kv.Value.Equals(v)) return false;
                    return true;
                default: return true;
            }
        }

        public override bool Equals(object obj) => Equals(obj as DataNode);

        public override int GetHashCode()
        {
            unchecked { return ((((((int)Kind * 31 + Bool.GetHashCode()) * 31 + Integer.GetHashCode()) * 31 + Number.GetHashCode()) * 31 + (Text?.GetHashCode() ?? 0)) * 31 + Items.Count) * 31 + Members.Count; }
        }
    }

    // ---------------------------------------------------------------- state snapshot, change plans, participants, store

    public sealed class StateSnapshot
    {
        public long Revision { get; }
        public DataNode Root { get; }   // the complete save v2 object; there is no reduced format

        public StateSnapshot(long revision, DataNode root)
        {
            Revision = PortGuard.Min(revision, 0, nameof(revision));
            if (PortGuard.Required(root, nameof(root)).Kind != DataKind.Object) throw new ArgumentException("not an object", nameof(root));
            Root = root;
        }

        /// <summary>Top-level save field, or null when absent.</summary>
        public DataNode Slice(string name) => Root.TryGet(name, out var v) ? v : null;
    }

    /// <summary>One domain operation addressed to a registered participant; Payload is validated by that participant only.</summary>
    public sealed class PlannedChange
    {
        public string ParticipantId { get; }
        public string Operation { get; }
        public DataNode Payload { get; }

        public PlannedChange(string participantId, string operation, DataNode payload)
        {
            ParticipantId = Ids.Require(participantId, nameof(participantId));
            Operation = Ids.Require(operation, nameof(operation));
            Payload = payload ?? DataNode.Null;
        }
    }

    /// <summary>Replacement values keyed by canonical JSON Pointer paths, prepared without touching the store.
    /// Arrays are replaced as units; no array-index writes. Ancestor/descendant ownership overlap is rejected.</summary>
    public sealed class StateReplacement
    {
        public IReadOnlyDictionary<string, DataNode> Slices { get; }
        public StateReplacement(IEnumerable<KeyValuePair<string, DataNode>> slices) { Slices = PortGuard.Map(slices, nameof(slices)); }
    }

    public interface IStatePlan
    {
        TransactionId TransactionId { get; }
        CompletionKey? Completion { get; }
        long ExpectedRevision { get; }
        IReadOnlyList<PlannedChange> Changes { get; }
    }

    /// <summary>A plan that failed is never reused; build a new plan with a new expected revision.</summary>
    public sealed class StatePlan : IStatePlan
    {
        public TransactionId TransactionId { get; }
        public CompletionKey? Completion { get; }
        public long ExpectedRevision { get; }
        public IReadOnlyList<PlannedChange> Changes { get; }

        public StatePlan(TransactionId transactionId, CompletionKey? completion, long expectedRevision, IEnumerable<PlannedChange> changes)
        {
            if (transactionId.Value == null) throw new ArgumentException("default id", nameof(transactionId));
            if (completion.HasValue && completion.Value.Value == null) throw new ArgumentException("default key", nameof(completion));
            TransactionId = transactionId;
            Completion = completion;
            ExpectedRevision = PortGuard.Min(expectedRevision, 0, nameof(expectedRevision));
            Changes = PortGuard.Opt(changes, nameof(changes));
        }
    }

    public sealed class StateCommitted
    {
        public TransactionId TransactionId { get; }
        public CompletionKey? Completion { get; }
        public long PreviousRevision { get; }
        public long Revision { get; }

        public StateCommitted(TransactionId transactionId, CompletionKey? completion, long previousRevision, long revision)
        {
            TransactionId = transactionId; Completion = completion;
            PreviousRevision = PortGuard.Min(previousRevision, 0, nameof(previousRevision));
            Revision = PortGuard.Min(revision, previousRevision, nameof(revision));
        }
    }

    /// <summary>Domain owner of canonical JSON Pointer paths. Prepare never writes the store.
    /// Replacements must be within an OwnedSlices prefix; disjoint /player fields may have different owners.</summary>
    public interface IStateParticipant
    {
        string ParticipantId { get; }
        IReadOnlyList<string> OwnedSlices { get; }
        Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan);
    }

    public interface IStateParticipantRegistry
    {
        /// <summary>Null when unregistered; the store then refuses the whole plan with ResultCodes.Unregistered.</summary>
        IStateParticipant Find(string participantId);
    }

    public interface IStateReader { StateSnapshot Current { get; } }

    /// <summary>The only commit authority. Order: expectedRevision check, every participant prepares, slice ownership and invariants verified, one replacement, listeners.</summary>
    public interface IStateStore : IStateReader
    {
        /// <summary>All-or-nothing. A repeated TransactionId returns the first result without applying again. Success Result.Revision is the new revision.</summary>
        Outcome<StateSnapshot> Commit(IStatePlan plan);

        /// <summary>Full replacement for load/restore. The store assigns the new revision as Current.Revision + 1.</summary>
        Outcome<StateSnapshot> ReplaceAll(StateSnapshot candidate, long expectedRevision, TransactionId transactionId);

        /// <summary>Listeners run through ICompletionQueue after the replacement, in commit order.</summary>
        void OnCommitted(Action<StateCommitted> listener);
    }

    // ---------------------------------------------------------------- leases, pause, ticks

    public sealed class LeaseRequest
    {
        public string Owner { get; }
        public IReadOnlyList<string> Resources { get; }   // distinct, ordinal-sorted
        public int Priority { get; }                      // see CandidatePriority
        public long ReservedAt { get; }
        public long Sequence { get; }

        public LeaseRequest(string owner, IEnumerable<string> resources, int priority, long reservedAt, long sequence)
        {
            Owner = Ids.Require(owner, nameof(owner));
            Resources = PortGuard.IdSet(resources, nameof(resources));
            Priority = priority;
            ReservedAt = PortGuard.Min(reservedAt, 0, nameof(reservedAt));
            Sequence = PortGuard.Min(sequence, 0, nameof(sequence));
        }
    }

    public interface ILeaseService
    {
        /// <summary>Acquires every resource or none. Failure is RESOURCE_BUSY with one diagnostic per busy resource.</summary>
        Outcome<Lease> TryAcquire(LeaseRequest request);

        /// <summary>Idempotent; unknown or already released ids succeed.</summary>
        Result Release(string leaseId);
    }

    public sealed class PauseSet : IPausePolicy
    {
        public static readonly PauseSet None = new PauseSet(false, false, false, false, false);
        public static readonly PauseSet All = new PauseSet(true, true, true, true, true);
        public bool Calendar { get; }
        public bool Ambient { get; }
        public bool Player { get; }
        public bool World { get; }
        public bool Scene { get; }

        public PauseSet(bool calendar, bool ambient, bool player, bool world, bool scene)
        { Calendar = calendar; Ambient = ambient; Player = player; World = world; Scene = scene; }

        public static PauseSet Copy(IPausePolicy p)
        {
            PortGuard.Required(p, nameof(p));
            return new PauseSet(p.Calendar, p.Ambient, p.Player, p.World, p.Scene);
        }
    }

    public sealed class PauseToken
    {
        public string TokenId { get; }
        public string Owner { get; }
        public IPausePolicy Policy { get; }

        public PauseToken(string tokenId, string owner, IPausePolicy policy)
        { TokenId = Ids.Require(tokenId, nameof(tokenId)); Owner = Ids.Require(owner, nameof(owner)); Policy = PauseSet.Copy(policy); }
    }

    public interface IPauseService
    {
        /// <summary>Union of all live tokens. Tokens are never saved; they are recreated after load.</summary>
        IPausePolicy Effective { get; }
        PauseToken Acquire(string owner, IPausePolicy requested);
        void Release(PauseToken token);   // idempotent
    }

    public interface ITickClock
    {
        long Tick { get; }
        /// <summary>Simulation time; stops while the relevant channel is paused.</summary>
        double SimulationSeconds { get; }
        /// <summary>Advances every tick even while paused. Timeouts and watchdogs use this clock.</summary>
        double UnpausedSeconds { get; }
        /// <summary>Handle that succeeds after the given simulation seconds.</summary>
        IHandle Delay(double seconds, string ownerToken);
    }

    // ---------------------------------------------------------------- logical input, collision, maps

    public static class InputActions
    {
        public const string Interact = "interact", Confirm = "confirm", Cancel = "cancel", Menu = "menu", Journal = "journal", Cast = "cast", Dodge = "dodge";
    }

    public sealed class InputFrame
    {
        public long Tick { get; }
        public long InputSequence { get; }
        public double MoveX { get; }   // dead zone applied, |(MoveX, MoveY)| <= 1
        public double MoveY { get; }
        public bool UiCaptured { get; }
        public IReadOnlyList<string> Pressed { get; }
        public IReadOnlyList<string> Held { get; }

        public InputFrame(long tick, long inputSequence, double moveX, double moveY, bool uiCaptured, IEnumerable<string> pressed, IEnumerable<string> held)
        {
            Tick = PortGuard.Min(tick, 0, nameof(tick));
            InputSequence = PortGuard.Min(inputSequence, 0, nameof(inputSequence));
            if (PortGuard.Finite(moveX, nameof(moveX)) * moveX + PortGuard.Finite(moveY, nameof(moveY)) * moveY > 1.000001) throw new ArgumentOutOfRangeException(nameof(moveX));
            MoveX = moveX; MoveY = moveY; UiCaptured = uiCaptured;
            Pressed = PortGuard.IdSet(pressed, nameof(pressed));
            Held = PortGuard.IdSet(held, nameof(held));
        }
    }

    public interface IInputPort { InputFrame Poll(long tick); }

    public sealed class SweepResult
    {
        public Position End { get; }   // farthest reachable point; equals the target when not blocked
        public bool Blocked { get; }
        public string BlockerId { get; }

        public SweepResult(Position end, bool blocked, string blockerId)
        { End = PortGuard.Placed(end, nameof(end)); Blocked = blocked; BlockerId = Ids.Opt(blockerId); }
    }

    public interface ICollisionPort
    {
        /// <summary>Both positions must be on the same map. Radius is the actor radius in tiles.</summary>
        SweepResult Sweep(Position from, Position to, double radius);
        bool IsPassable(Position at, double radius);
    }

    public sealed class Portal
    {
        public string PortalId { get; }
        public Position From { get; }
        public Position Arrival { get; }
        public bool Open { get; }

        public Portal(string portalId, Position from, Position arrival, bool open)
        { PortalId = Ids.Require(portalId, nameof(portalId)); From = PortGuard.Placed(from, nameof(from)); Arrival = PortGuard.Placed(arrival, nameof(arrival)); Open = open; }
    }

    public interface IMapPort
    {
        /// <summary>Last valid map. A failed or cancelled Load leaves it unchanged and cleans up the temporary scene.</summary>
        string ActiveMapId { get; }
        long NavRevision { get; }
        bool TryGetPortal(string portalId, out Portal portal);
        IReadOnlyList<Portal> PortalsOn(string mapId);
        /// <summary>Failure code MAP_LOAD_FAILED or CANCELLED.</summary>
        IHandle Load(string mapId, string ownerToken);
    }

    // ---------------------------------------------------------------- movement

    public sealed class Destination
    {
        public string DestinationId { get; }   // empty for ad-hoc targets
        public Position Target { get; }
        public double Tolerance { get; }       // tiles

        public Destination(string destinationId, Position target, double tolerance)
        { DestinationId = Ids.Opt(destinationId); Target = PortGuard.Placed(target, nameof(target)); Tolerance = PortGuard.NonNegative(tolerance, nameof(tolerance)); }
    }

    public interface IDestinationResolver
    {
        /// <summary>Resolves a dest id (including home) for an actor; unknown ids fail with REF_NOT_FOUND.</summary>
        Outcome<Destination> Resolve(string actorId, string destinationId);
    }

    public sealed class MoveRequest
    {
        public string ActorId { get; }
        public Destination Destination { get; }
        public string OwnerToken { get; }
        public double TimeoutSeconds { get; }   // measured on ITickClock.UnpausedSeconds
        public bool UsePortals { get; }

        public MoveRequest(string actorId, Destination destination, string ownerToken, double timeoutSeconds, bool usePortals)
        {
            ActorId = Ids.Require(actorId, nameof(actorId));
            Destination = PortGuard.Required(destination, nameof(destination));
            OwnerToken = Ids.Require(ownerToken, nameof(ownerToken));
            TimeoutSeconds = PortGuard.Positive(timeoutSeconds, nameof(timeoutSeconds));
            UsePortals = usePortals;
        }
    }

    /// <summary>Arrival is judged from the actual final position, never from the request.</summary>
    public sealed class MoveArrival
    {
        public Position Actual { get; }
        public Destination Target { get; }
        public double Distance { get; }   // PositiveInfinity when on a different map
        public bool Arrived { get; }

        public MoveArrival(Position actual, Destination target)
        {
            Actual = PortGuard.Placed(actual, nameof(actual));
            Target = PortGuard.Required(target, nameof(target));
            var t = target.Target;
            Distance = actual.MapId == t.MapId ? Math.Sqrt((actual.X - t.X) * (actual.X - t.X) + (actual.Y - t.Y) * (actual.Y - t.Y)) : double.PositiveInfinity;
            Arrived = Distance <= target.Tolerance;
        }
    }

    public interface IMoveService
    {
        /// <summary>Succeeds only with Value.Arrived true. Failures: NO_ROUTE, TARGET_BLOCKED, MAP_LOAD_FAILED, TIMEOUT, CANCELLED. Never teleports.</summary>
        IHandle<MoveArrival> Move(MoveRequest request);
    }

    // ---------------------------------------------------------------- call parser AST

    public enum CallKind { Trigger, When, Script, Fork, Effects, ScheduleEntry, Goal, Hint, Behavior, Weakness, ReactionPath, EncounterEnemy, ItemQuantity, ItemGrant, Substitute, FinalStage }
    public enum ArgKind { Word, Integer, Number, Bool, String }

    /// <summary>Character range inside the original cell text.</summary>
    public sealed class SourceSpan
    {
        public int Offset { get; }
        public int Length { get; }
        public SourceSpan(int offset, int length)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Offset = offset; Length = length;
        }
    }

    public sealed class CallArgument
    {
        public string Name { get; }   // empty for positional arguments
        public ArgKind Kind { get; }
        public string Text { get; }   // unquoted and unescaped for String, raw token otherwise
        public long Integer { get; }  // valid for Integer
        public double Number { get; } // valid for Number
        public bool Bool { get; }     // valid for Bool
        public SourceSpan Span { get; }

        public CallArgument(string name, ArgKind kind, string text, SourceSpan span)
        {
            Name = Ids.Opt(name); Kind = kind; Text = PortGuard.Required(text, nameof(text)); Span = PortGuard.Required(span, nameof(span));
            if (kind == ArgKind.Bool && text != "true" && text != "false") throw new FormatException(nameof(text));
            Integer = kind == ArgKind.Integer ? long.Parse(text, CultureInfo.InvariantCulture) : 0;
            Number = kind == ArgKind.Number ? PortGuard.Finite(double.Parse(text, CultureInfo.InvariantCulture), nameof(text)) : 0;
            Bool = kind == ArgKind.Bool && text == "true";
        }
    }

    public sealed class CallNode
    {
        public int Index { get; }   // 1-based position in the list
        public string Label { get; }
        public bool Negated { get; }
        public string Function { get; }
        public SourceSpan NameSpan { get; }
        public IReadOnlyList<CallArgument> Arguments { get; }   // source order
        public SourceSpan Span { get; }

        public CallNode(int index, string label, bool negated, string function, SourceSpan nameSpan, IEnumerable<CallArgument> arguments, SourceSpan span)
        {
            if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
            Index = index; Label = Ids.Opt(label); Negated = negated; Function = Ids.Require(function, nameof(function));
            NameSpan = PortGuard.Required(nameSpan, nameof(nameSpan)); Span = PortGuard.Required(span, nameof(span));
            Arguments = PortGuard.Opt(arguments, nameof(arguments));
        }
    }

    public sealed class CallList
    {
        public CallKind Kind { get; }
        public string Text { get; }
        public string File { get; }
        public string Pointer { get; }
        public string EntityId { get; }   // for forks: event.id:forkName
        public IReadOnlyList<CallNode> Calls { get; }

        public CallList(CallKind kind, string text, string file, string pointer, string entityId, IEnumerable<CallNode> calls)
        {
            Kind = kind; Text = PortGuard.Required(text, nameof(text)); File = Ids.Opt(file); Pointer = Ids.Opt(pointer); EntityId = Ids.Opt(entityId);
            Calls = PortGuard.Opt(calls, nameof(calls));
        }

        /// <summary>Location in the form entity#index plus the offset of the call (or of a point inside it).</summary>
        public SourceLocation Locate(CallNode node, int offsetInCall)
            => new SourceLocation(File, Pointer, EntityId, EntityId + "#" + node.Index, node.Span.Offset + offsetInCall);
    }

    public interface ICallParser
    {
        /// <summary>Syntax only. Any error yields a failed Outcome with diagnostics and no partial list.</summary>
        Outcome<CallList> Parse(CallKind kind, string text, string file, string pointer, string entityId);
    }

    // ---------------------------------------------------------------- function dictionary

    public enum FunctionRole { Trigger, Command, Condition, Effect, ScheduleEntry, Goal, Hint, Behavior, Weakness, ReactionPath, EncounterEnemy, ItemQuantity, ItemGrant, Substitute, FinalStage }
    public enum ParamType { Bool, Int, Number, Word, Text, Ref, Enum, HandleOpen, HandleUse, ConditionList, Any }

    public sealed class ParamDef
    {
        public string Name { get; }
        public ParamType Type { get; }
        public bool Required { get; }
        public bool Variadic { get; }
        public string RefKind { get; }                  // for Ref
        public IReadOnlyList<string> EnumValues { get; }
        public DataNode Default { get; }                // null when none
        public double? Min { get; }

        public ParamDef(string name, ParamType type, bool required, bool variadic, string refKind, IEnumerable<string> enumValues, DataNode defaultValue, double? min)
        {
            Name = Ids.Require(name, nameof(name)); Type = type; Required = required; Variadic = variadic; RefKind = Ids.Opt(refKind);
            EnumValues = PortGuard.Opt(enumValues, nameof(enumValues)); Default = defaultValue;
            if (min.HasValue) PortGuard.Finite(min.Value, nameof(min));
            Min = min;
        }
    }

    public sealed class FunctionDef
    {
        public DataNode Raw { get; }
        public string Name { get; }
        public FunctionRole Role { get; }
        public string Owner { get; }   // owning service id
        public IReadOnlyList<ParamDef> Params { get; }   // positional order

        public FunctionDef(string name, FunctionRole role, string owner, IEnumerable<ParamDef> parameters, DataNode raw = null)
        {
            Name = Ids.Require(name, nameof(name)); Role = role;
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("owner label is required", nameof(owner));
            Owner = owner; // functions.json includes human-readable labels such as "도메인 서비스".
            Params = PortGuard.Opt(parameters, nameof(parameters)); Raw = raw ?? DataNode.Null;
        }
    }

    public interface IFunctionDictionary
    {
        DataNode Source { get; } // full registry, including kind nameRule, named params, groups and all validation metadata
        IReadOnlyList<FunctionDef> All { get; }
        FunctionDef Find(FunctionRole role, string name);   // null when unknown
    }

    public interface IFunctionDictionaryLoader { Outcome<IFunctionDictionary> Load(DataNode functionsJson); }

    // ---------------------------------------------------------------- compiled calls, events, catalog

    /// <summary>Validated call. Args holds every parameter by name with defaults applied; variadic parameters are List nodes.</summary>
    public sealed class CompiledCall
    {
        public FunctionDef Function { get; }
        /// <summary>Validated source head; retains numeric schedules and ID heads matched by a wildcard signature.</summary>
        public string InvocationName { get; }
        public int Index { get; }
        public string Label { get; }
        public bool Negated { get; }
        public IReadOnlyDictionary<string, DataNode> Args { get; }
        public IReadOnlyList<IReadOnlyList<CompiledCall>> Alternatives { get; }   // any: one list per alternative
        public SourceLocation Source { get; }

        public CompiledCall(FunctionDef function, int index, string label, bool negated, IEnumerable<KeyValuePair<string, DataNode>> args, IEnumerable<IReadOnlyList<CompiledCall>> alternatives, SourceLocation source, string invocationName = null)
        {
            Function = PortGuard.Required(function, nameof(function));
            InvocationName = Ids.Require(invocationName ?? function.Name, nameof(invocationName));
            if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
            Index = index; Label = Ids.Opt(label); Negated = negated;
            Args = PortGuard.Map(args, nameof(args));
            Alternatives = PortGuard.Nested(alternatives, nameof(alternatives));
            Source = PortGuard.Required(source, nameof(source));
        }
    }

    public enum ExecutionMode { Scene, RoomActivity }
    public enum RepeatPolicy { Once, Practice }

    public sealed class CompiledEvent
    {
        public string Id { get; }
        public ExecutionMode Mode { get; }
        public RepeatPolicy Repeat { get; }
        public IReadOnlyList<string> Resources { get; }
        public CompiledCall Trigger { get; }
        public IReadOnlyList<CompiledCall> When { get; }
        public int CostMinutes { get; }   // game-minutes, applied once on success
        public IReadOnlyList<CompiledCall> Script { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<CompiledCall>> Forks { get; }
        public IReadOnlyList<CompiledCall> Effects { get; }
        public CompletionKey? Completion { get; }
        public DataNode Raw { get; }   // original Row, so no field is dropped (category, staging, pause, ...)

        public CompiledEvent(string id, ExecutionMode mode, RepeatPolicy repeat, IEnumerable<string> resources, CompiledCall trigger, IEnumerable<CompiledCall> when,
            int costMinutes, IEnumerable<CompiledCall> script, IEnumerable<KeyValuePair<string, IReadOnlyList<CompiledCall>>> forks, IEnumerable<CompiledCall> effects,
            CompletionKey? completion, DataNode raw)
        {
            Id = Ids.Require(id, nameof(id)); Mode = mode; Repeat = repeat;
            Resources = PortGuard.IdSet(resources, nameof(resources));
            Trigger = PortGuard.Required(trigger, nameof(trigger));
            When = PortGuard.Opt(when, nameof(when));
            if (costMinutes < 0) throw new ArgumentOutOfRangeException(nameof(costMinutes));
            CostMinutes = costMinutes;
            Script = PortGuard.Opt(script, nameof(script));
            Forks = PortGuard.Lists(forks, nameof(forks));
            Effects = PortGuard.Opt(effects, nameof(effects));
            Completion = completion;
            Raw = PortGuard.Required(raw, nameof(raw));
        }
    }

    public sealed class ContentBundle
    {
        public string File { get; }
        public DataNode Root { get; }
        public string SourceHash { get; }
        public ContentBundle(string file, DataNode root, string sourceHash = "")
        {
            File = Ids.Opt(file);
            SourceHash = Ids.Opt(sourceHash);
            if (PortGuard.Required(root, nameof(root)).Kind != DataKind.Object) throw new ArgumentException("not an object", nameof(root));
            Root = root;
        }
    }

    public interface ICatalog
    {
        string ContentVersion { get; }
        string CatalogHash { get; }
        IReadOnlyList<CompiledEvent> Events { get; }
        CompiledEvent FindEvent(string id);                    // null when unknown
        DataNode FindDefinition(string kind, string id);       // raw definition Row, null when unknown
    }

    public interface ICatalogCompiler
    {
        Outcome<ICatalog> Compile(ContentBundle catalog, ContentBundle runner, ContentBundle functions,
            ContentBundle catalogSchema, ContentBundle runnerSchema);
    }

    public interface ICatalogProvider
    {
        ICatalog Current { get; }
        /// <summary>A failed bundle is rejected whole and Current stays the previous valid catalog.</summary>
        Outcome<ICatalog> TryReplace(ContentBundle catalog, ContentBundle runner, ContentBundle functions,
            ContentBundle catalogSchema, ContentBundle runnerSchema);
    }

    // ---------------------------------------------------------------- conditions and effects

    public sealed class ConditionReport
    {
        public bool Satisfied { get; }
        public long Revision { get; }
        public IReadOnlyList<Diagnostic> Reasons { get; }   // why false; includes UNREGISTERED for unknown functions

        public ConditionReport(bool satisfied, long revision, IEnumerable<Diagnostic> reasons)
        { Satisfied = satisfied; Revision = PortGuard.Min(revision, 0, nameof(revision)); Reasons = PortGuard.Opt(reasons, nameof(reasons)); }
    }

    public interface IConditionEvaluator
    {
        /// <summary>Pure read of one snapshot; call again with the newest snapshot just before commit.</summary>
        ConditionReport Evaluate(IReadOnlyList<CompiledCall> when, StateSnapshot snapshot);
    }

    public sealed class ChangeSet
    {
        public IReadOnlyList<PlannedChange> Changes { get; }
        public ChangeSet(IEnumerable<PlannedChange> changes) { Changes = PortGuard.Opt(changes, nameof(changes)); }
    }

    public sealed class OperationRequest
    {
        public string Domain { get; }          // class, encounter, puzzle, craft, relationship, project, activity
        public string DefinitionId { get; }    // puzzle: room id or room:label
        public string EventId { get; }
        public string OwnerToken { get; }
        public bool Practice { get; }

        public OperationRequest(string domain, string definitionId, string eventId, string ownerToken, bool practice)
        {
            Domain = Ids.Require(domain, nameof(domain)); DefinitionId = Ids.Require(definitionId, nameof(definitionId));
            EventId = Ids.Require(eventId, nameof(eventId)); OwnerToken = Ids.Require(ownerToken, nameof(ownerToken)); Practice = practice;
        }
    }

    /// <summary>Staged perform result. Nothing is persisted when it is produced; effects read it at commit time.</summary>
    public sealed class OperationResult
    {
        public string Domain { get; }
        public string DefinitionId { get; }
        public DataNode Staged { get; }

        public OperationResult(string domain, string definitionId, DataNode staged)
        { Domain = Ids.Require(domain, nameof(domain)); DefinitionId = Ids.Require(definitionId, nameof(definitionId)); Staged = staged ?? DataNode.Null; }
    }

    public sealed class EffectContext
    {
        public StateSnapshot Basis { get; }
        public TransactionId TransactionId { get; }
        public CompletionKey? Completion { get; }
        public IReadOnlyList<OperationResult> Staged { get; }

        public EffectContext(StateSnapshot basis, TransactionId transactionId, CompletionKey? completion, IEnumerable<OperationResult> staged)
        {
            Basis = PortGuard.Required(basis, nameof(basis)); TransactionId = transactionId; Completion = completion;
            Staged = PortGuard.Opt(staged, nameof(staged));
        }
    }

    public interface IEffectHandler
    {
        string FunctionName { get; }
        string ParticipantId { get; }
        Outcome<ChangeSet> Plan(CompiledCall call, EffectContext context);
    }

    public interface IEffectPlanner
    {
        /// <summary>All effects or none. A function without a registered handler refuses the whole list with UNREGISTERED.</summary>
        Outcome<ChangeSet> Plan(IReadOnlyList<CompiledCall> effects, EffectContext context);
    }

    // ---------------------------------------------------------------- presentation

    public sealed class ChoiceOption
    {
        public string Key { get; }    // fork name
        public string Text { get; }
        public ChoiceOption(string key, string text) { Key = Ids.Require(key, nameof(key)); Text = Ids.Opt(text); }
    }

    public sealed class ChoiceSelection
    {
        public string Key { get; }
        public int Index { get; }
        public ChoiceSelection(string key, int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            Key = Ids.Require(key, nameof(key)); Index = index;
        }
    }

    public interface IPresentationPort
    {
        IHandle Say(string speaker, string text, string ownerToken);
        /// <summary>Succeeds with the selected fork key. Cancel closes the window and fails with CANCELLED.</summary>
        IHandle<ChoiceSelection> Choose(string prompt, IReadOnlyList<ChoiceOption> options, string ownerToken);
        IHandle MoveCamera(Destination target, double seconds, string ownerToken);
        IHandle Animate(string actorId, string clip, double seconds, string ownerToken);
    }

    // ---------------------------------------------------------------- calendar and clock

    public readonly struct GameTime : IEquatable<GameTime>
    {
        public const int MinutesPerDay = 1440, DaysPerCycle = 80, FirstDayStart = 420;
        public long AbsoluteMinute { get; }
        public double Fraction { get; }   // [0, 1)

        public GameTime(long absoluteMinute, double fraction)
        {
            if (absoluteMinute < 0) throw new ArgumentOutOfRangeException(nameof(absoluteMinute));
            if (double.IsNaN(fraction) || fraction < 0 || fraction >= 1) throw new ArgumentOutOfRangeException(nameof(fraction));
            AbsoluteMinute = absoluteMinute; Fraction = fraction;
        }

        public long Day => AbsoluteMinute / MinutesPerDay;
        public int MinuteOfDay => (int)(AbsoluteMinute % MinutesPerDay);
        public long Year => Day / DaysPerCycle + 1;
        public int CycleDay => (int)(Day % DaysPerCycle);
        public int Season => CycleDay / 20;
        public int Term => CycleDay / 40;
        public int Weekday => (int)(Day % 5);

        public bool Equals(GameTime o) => AbsoluteMinute == o.AbsoluteMinute && Fraction == o.Fraction;
        public override bool Equals(object obj) => obj is GameTime t && Equals(t);
        public override int GetHashCode() { unchecked { return AbsoluteMinute.GetHashCode() * 31 + Fraction.GetHashCode(); } }
    }

    /// <summary>Midnight that starts Day (Day >= 1). Key is stable so a boundary is consumed at most once.</summary>
    public sealed class DayBoundary
    {
        public long Day { get; }
        public long AbsoluteMinute => Day * GameTime.MinutesPerDay;
        public CompletionKey Key => new CompletionKey("boundary.day_d" + Day.ToString(CultureInfo.InvariantCulture));
        public DayBoundary(long day) { Day = PortGuard.Min(day, 1, nameof(day)); }
    }

    public sealed class ClockAdvance
    {
        public GameTime From { get; }
        public GameTime To { get; }
        public IReadOnlyList<DayBoundary> Boundaries { get; }   // strictly ascending, every crossed midnight
        public bool ReturnPending => Boundaries.Count > 0;

        public ClockAdvance(GameTime from, GameTime to, IEnumerable<DayBoundary> boundaries)
        {
            From = from; To = to;
            Boundaries = PortGuard.Opt(boundaries, nameof(boundaries));
            for (var i = 1; i < Boundaries.Count; i++)
                if (Boundaries[i].Day <= Boundaries[i - 1].Day) throw new ArgumentException("not ascending", nameof(boundaries));
        }
    }

    public interface ICalendar
    {
        GameTime FromSnapshot(StateSnapshot snapshot);
        /// <summary>Pure. Used for tick advance and for event time costs; the cost is applied only inside the success commit.</summary>
        ClockAdvance Plan(GameTime from, double minutes);
    }

    public interface IClockService
    {
        GameTime Now { get; }
        /// <summary>1 simulation-second = 1 game-minute. No advance when pause.Calendar is true.</summary>
        ClockAdvance Advance(double simulationSeconds, IPausePolicy pause);
    }

    // ---------------------------------------------------------------- event runner

    public sealed class EventStartRequest
    {
        public string EventId { get; }
        public string OwnerToken { get; }
        public long InputSequence { get; }
        public TransactionId TransactionId { get; }

        public EventStartRequest(string eventId, string ownerToken, long inputSequence, TransactionId transactionId)
        {
            EventId = Ids.Require(eventId, nameof(eventId)); OwnerToken = Ids.Require(ownerToken, nameof(ownerToken));
            InputSequence = PortGuard.Min(inputSequence, 0, nameof(inputSequence));
            if (transactionId.Value == null) throw new ArgumentException("default id", nameof(transactionId));
            TransactionId = transactionId;
        }
    }

    public sealed class EventCompletion
    {
        public string EventId { get; }
        public CompletionKey? Completion { get; }
        public long CommittedRevision { get; }
        public IReadOnlyList<OperationResult> Operations { get; }   // staged results consumed by the commit

        public EventCompletion(string eventId, CompletionKey? completion, long committedRevision, IEnumerable<OperationResult> operations)
        {
            EventId = Ids.Require(eventId, nameof(eventId)); Completion = completion;
            CommittedRevision = PortGuard.Min(committedRevision, 0, nameof(committedRevision));
            Operations = PortGuard.Opt(operations, nameof(operations));
        }
    }

    public interface IEventRunner
    {
        /// <summary>Refusals (RESOURCE_BUSY, CONDITION_FALSE, UNREGISTERED) arrive as a terminal Failed handle, never as success.</summary>
        IHandle<EventCompletion> Start(EventStartRequest request);
    }

    // ---------------------------------------------------------------- safe snapshot, save, restore, file generations

    public enum SafeBoundary { WorldSafe, SceneEntry, SceneComplete, RoomSafe }

    public sealed class SafeSnapshot
    {
        public SafeBoundary Boundary { get; }
        public StateSnapshot Snapshot { get; }
        public bool Saved { get; }
        public SafeSnapshot(SafeBoundary boundary, StateSnapshot snapshot, bool saved)
        { Boundary = boundary; Snapshot = PortGuard.Required(snapshot, nameof(snapshot)); Saved = saved; }
    }

    /// <summary>Decides which snapshot a save may use. Knows nothing about files.</summary>
    public interface ISafeBoundaryPolicy
    {
        void Notify(SafeBoundary boundary, StateSnapshot snapshot);
        SafeSnapshot Latest { get; }   // null until the first boundary
        void MarkSaved(long revision);
    }

    /// <summary>File bytes envelope. Checksum is lowercase hex SHA-256 of Payload (UTF-8 JSON of the full save v2 object).</summary>
    public sealed class SaveEnvelope
    {
        public int SaveVersion { get; }
        public string CatalogHash { get; }
        public long Generation { get; }
        public string Checksum { get; }
        private readonly byte[] payload;
        public ReadOnlyMemory<byte> Payload => new ReadOnlyMemory<byte>((byte[])payload.Clone());

        public SaveEnvelope(int saveVersion, string catalogHash, long generation, string checksum, ReadOnlyMemory<byte> payload)
        {
            SaveVersion = (int)PortGuard.Min(saveVersion, 1, nameof(saveVersion));
            CatalogHash = Ids.Require(catalogHash, nameof(catalogHash));
            Generation = PortGuard.Min(generation, 0, nameof(generation));
            Checksum = Ids.Require(checksum, nameof(checksum));
            this.payload = payload.ToArray();
        }
    }

    public interface ISaveCodec
    {
        SaveEnvelope Seal(StateSnapshot snapshot, long generation);
        ReadOnlyMemory<byte> Encode(SaveEnvelope envelope);
        /// <summary>Checks framing and checksum only; version, hash and schema are checked by restore.</summary>
        Outcome<SaveEnvelope> Decode(ReadOnlyMemory<byte> fileBytes);
    }

    public sealed class FileGeneration
    {
        public long Generation { get; }
        public string Checksum { get; }          // of the stored file bytes
        private readonly byte[] bytes;
        public ReadOnlyMemory<byte> Bytes => new ReadOnlyMemory<byte>((byte[])bytes.Clone()); // empty in ListCandidates results

        public FileGeneration(long generation, string checksum, ReadOnlyMemory<byte> bytes)
        {
            Generation = PortGuard.Min(generation, 0, nameof(generation));
            Checksum = Ids.Require(checksum, nameof(checksum));
            this.bytes = bytes.ToArray();
        }
    }

    /// <summary>Disk operations only: temp write, re-read and checksum, keep last good generation, replace.</summary>
    public interface IFileGenerationStore
    {
        /// <summary>If cancellation arrives after the replace, the handle still succeeds with the generation actually stored.</summary>
        IHandle<FileGeneration> Write(long generation, ReadOnlyMemory<byte> fileBytes);
        IHandle<FileGeneration> Read(long generation);
        IReadOnlyList<FileGeneration> ListCandidates();   // newest first, valid generations only
    }

    /// <summary>Explicit table, v1 to v2 and id/completionKey substitutions included. Unknown versions fail with SAVE_INCOMPATIBLE.</summary>
    public interface ISaveMigrationRegistry
    {
        int CurrentVersion { get; }
        Outcome<DataNode> Migrate(DataNode save, int fromVersion);
    }

    public interface ISaveRestorer
    {
        /// <summary>Order: checksum, version, catalog hash, migration, strict schema, references, invariants. Nothing is applied here; the caller uses IStateStore.ReplaceAll.</summary>
        Outcome<StateSnapshot> Restore(SaveEnvelope envelope, ICatalog catalog);
    }

    public interface ISaveService
    {
        /// <summary>Saves ISafeBoundaryPolicy.Latest. A disk failure does not undo the in-memory state.</summary>
        IHandle<FileGeneration> SaveLatestSafe();
        /// <summary>Loads the newest restorable generation and replaces the whole state.</summary>
        IHandle<FileGeneration> LoadLatest();
    }

    // ---------------------------------------------------------------- schedule and reservations

    public static class CandidatePriority
    {
        public const int Resolution = 400, Relationship = 300, Event = 200, General = 100, Schedule = 10;
    }

    public enum ReservationStatus { Offered, Accepted, Ready }

    public sealed class StartCandidate
    {
        public string EventId { get; }
        public string ReservationId { get; }   // empty when not reservation based
        public ReservationStatus Status { get; }
        public int Priority { get; }
        public long ReservedAt { get; }
        public long Sequence { get; }

        public StartCandidate(string eventId, string reservationId, ReservationStatus status, int priority, long reservedAt, long sequence)
        {
            EventId = Ids.Require(eventId, nameof(eventId)); ReservationId = Ids.Opt(reservationId); Status = status; Priority = priority;
            ReservedAt = PortGuard.Min(reservedAt, 0, nameof(reservedAt)); Sequence = PortGuard.Min(sequence, 0, nameof(sequence));
        }
    }

    public interface IReservationService
    {
        /// <summary>Ordered by priority descending, ReservedAt, Sequence, EventId (ordinal). Accepted reservations stay candidates after a failed start, a missing lease or a failed move; only a successful completion commit removes them.</summary>
        IReadOnlyList<StartCandidate> Candidates(StateSnapshot snapshot);
        Outcome<PlannedChange> PlanAccept(string reservationId, StateSnapshot basis);
    }

    public interface IScheduleService
    {
        /// <summary>Exactly one destination for the current time, recomputed from the clock rather than from a stored path.</summary>
        Outcome<Destination> CurrentDestination(string npcId, GameTime now);
    }
}
