using System;
using System.Collections.Generic;
using MagicCampus.Content.Compilation;
using MagicCampus.Contracts;
using MagicCampus.Transactions;

namespace MagicCampus.Content.Catalog
{
    /// <summary>Validated call cells retained by the compiler, including schedules and non-event tables.
    /// Null means no such cell; an empty list is a valid empty cell. Consumers never parse source text.</summary>
    public interface ICompiledContentLookup
    {
        IReadOnlyList<CompiledCall> FindCalls(string source, string table, string rowId, string column);
    }

    public sealed class CompiledColumn
    {
        public string Source { get; }
        public string Table { get; }
        public string RowId { get; }
        public string Column { get; }
        public IReadOnlyList<CompiledCall> Calls { get; }
        public CompiledColumn(string source, string table, string rowId, string column, IEnumerable<CompiledCall> calls)
        {
            Source = Ids.Require(source, nameof(source)); Table = Ids.Require(table, nameof(table));
            RowId = rowId ?? ""; Column = Ids.Require(column, nameof(column)); Calls = Ids.Copy(calls, nameof(calls));
        }
    }

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

        /// <summary>Shared completion key of a once event; without an explicit key the event completes under its own id.</summary>
        public CompletionKey OnceKey => Completion ?? new CompletionKey(Id);

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

    public interface ICatalog
    {
        string ContentVersion { get; }
        string CatalogHash { get; }
        IReadOnlyList<CompiledEvent> Events { get; }
        CompiledEvent FindEvent(string id);                    // null when unknown
        DataNode FindDefinition(string kind, string id);       // raw definition Row, null when unknown
    }

    public interface ICatalogProvider
    {
        ICatalog Current { get; }
    }
}
