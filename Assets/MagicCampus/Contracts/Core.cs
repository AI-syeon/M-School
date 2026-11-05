using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MagicCampus.Domain")]

namespace MagicCampus.Contracts
{
    // Units: tile (position), simulation-second (real-time sim), game-minute (calendar; first day = 420), crown (integer currency).
    // All types here are immutable; collection arguments are copied and never exposed as writable.

    internal static class Ids
    {
        internal static string Require(string v, string name)
        {
            if (v == null) throw new ArgumentNullException(name);
            if (v.Length == 0) throw new ArgumentException("must not be empty", name);
            foreach (var c in v)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) throw new ArgumentException("whitespace/control characters not allowed", name);
            return v;
        }

        internal static string Opt(string v) => v ?? string.Empty;

        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> src, string name) where T : class
        {
            if (src == null) throw new ArgumentNullException(name);
            var a = new List<T>(src).ToArray();
            foreach (var e in a)
                if (e == null) throw new ArgumentException("null element", name);
            return Array.AsReadOnly(a);
        }
    }

    public readonly struct Position : IEquatable<Position>
    {
        public string MapId { get; }
        public double X { get; }
        public double Y { get; }

        public Position(string mapId, double x, double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (double.IsNaN(y) || double.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(y));
            MapId = Ids.Require(mapId, nameof(mapId));
            X = x;
            Y = y;
        }

        public bool Equals(Position o) => MapId == o.MapId && X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Position p && Equals(p);
        public override int GetHashCode() { unchecked { return ((MapId?.GetHashCode() ?? 0) * 31 + X.GetHashCode()) * 31 + Y.GetHashCode(); } }
        public override string ToString() => MapId + "@" + X + "," + Y;
    }

    public sealed class SourceLocation
    {
        public string File { get; }
        public string Pointer { get; }
        public string EntityId { get; }
        public string Call { get; }
        public int Offset { get; }

        public SourceLocation(string file, string pointer, string entityId, string call, int offset)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            File = Ids.Opt(file);
            Pointer = Ids.Opt(pointer);
            EntityId = Ids.Opt(entityId);
            Call = Ids.Opt(call);
            Offset = offset;
        }

        public override string ToString() => File + Pointer + "#" + EntityId + " " + Call + "+" + Offset;
    }

    public sealed class Diagnostic
    {
        public string Code { get; }
        public string Message { get; }
        public SourceLocation Location { get; }   // may be null
        public string Expected { get; }
        public string Actual { get; }

        public Diagnostic(string code, string message, SourceLocation location, string expected, string actual)
        {
            Code = Ids.Require(code, nameof(code));
            Message = Ids.Opt(message);
            Location = location;
            Expected = Ids.Opt(expected);
            Actual = Ids.Opt(actual);
        }
    }

    public enum OperationStatus { Pending, Running, Succeeded, Failed, Cancelled }

    public static class OperationStatusExtensions
    {
        public static bool IsTerminal(this OperationStatus s) =>
            s == OperationStatus.Succeeded || s == OperationStatus.Failed || s == OperationStatus.Cancelled;
    }

    public sealed class Result
    {
        public OperationStatus Status { get; }
        public string Code { get; }
        public long Revision { get; }
        public IReadOnlyList<Diagnostic> Details { get; }

        public Result(OperationStatus status, string code, long revision, IEnumerable<Diagnostic> details)
        {
            if (!Enum.IsDefined(typeof(OperationStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            Status = status;
            Code = Ids.Require(code, nameof(code));
            Revision = revision;
            Details = Ids.Copy(details ?? new Diagnostic[0], nameof(details));
        }
    }

    public interface ICancelable
    {
        /// <summary>Idempotent request to cancel. Has no effect once the operation is terminal.</summary>
        void Cancel(string reason);
    }

    /// <summary>
    /// Handle to an asynchronous operation.
    /// Terminal contract: an operation reaches exactly one terminal status (Succeeded, Failed or Cancelled)
    /// exactly once; Terminal is null until then and never changes afterwards. Every callback registered via
    /// OnTerminal is invoked exactly once with that Result, delivered through ICompletionQueue (never inline
    /// on the registering call stack), including callbacks registered after the operation is already terminal.
    /// </summary>
    public interface IHandle : ICancelable
    {
        string OperationId { get; }
        OperationStatus Status { get; }
        Result Terminal { get; }
        void OnTerminal(Action<Result> callback);
    }

    /// <summary>Per-channel pause flags; true means the channel is paused.</summary>
    public interface IPausePolicy
    {
        bool Calendar { get; }
        bool Ambient { get; }
        bool Player { get; }
        bool World { get; }
        bool Scene { get; }
    }
}
