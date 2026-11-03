using System.Collections.Generic;
namespace MagicCampus.Contracts
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
}
