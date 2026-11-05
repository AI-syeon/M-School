using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
        public const string MapLoadFailed = "MAP_LOAD_FAILED", Timeout = "TIMEOUT", Cancelled = "CANCELLED";
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

    // ---------------------------------------------------------------- leases, pause, ticks

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

    // ---------------------------------------------------------------- compiled calls, events, catalog

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

    // ---------------------------------------------------------------- safe snapshot, save, restore, file generations

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
}
