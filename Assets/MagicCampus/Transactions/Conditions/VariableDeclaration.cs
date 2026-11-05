using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Transactions.Conditions
{
    public enum VariableType { Bool, Int }

    /// <summary>Ordered view of the /flags list ([{id,value}], never a dictionary).</summary>
    public sealed class FlagTable
    {
        private readonly List<string> order = new List<string>();
        private readonly Dictionary<string, DataNode> values = new Dictionary<string, DataNode>(StringComparer.Ordinal);

        public static bool TryParse(DataNode node, out FlagTable table, out Diagnostic error)
        {
            table = null;
            if (node == null || node.Kind != DataKind.List)
            {
                error = Diag.Make(ResultCodes.ContentInvalid, "/flags must be a list of {id,value}", "list", node == null ? "absent" : node.Kind.ToString());
                return false;
            }
            var result = new FlagTable();
            foreach (var item in node.Items)
            {
                DataNode id;
                DataNode value;
                if (item.Kind != DataKind.Object || item.Members.Count != 2 || !item.TryGet("id", out id) || !item.TryGet("value", out value)
                    || id.Kind != DataKind.String || id.Text.Length == 0 || (value.Kind != DataKind.Bool && value.Kind != DataKind.Integer))
                {
                    error = Diag.Make(ResultCodes.ContentInvalid, "/flags entries must be {id:string,value:bool|integer}", "{id,value}", Diag.Value(item));
                    return false;
                }
                if (result.values.ContainsKey(id.Text))
                {
                    error = Diag.Make(ResultCodes.ContentInvalid, "/flags holds a duplicate id", "unique id", id.Text);
                    return false;
                }
                result.order.Add(id.Text);
                result.values[id.Text] = value;
            }
            table = result;
            error = null;
            return true;
        }

        public bool TryGet(string id, out DataNode value)
        {
            return values.TryGetValue(id, out value);
        }

        public void Set(string id, DataNode value)
        {
            if (!values.ContainsKey(id)) order.Add(id);
            values[id] = value;
        }

        public DataNode ToNode()
        {
            var items = new List<DataNode>(order.Count);
            foreach (var id in order)
            {
                items.Add(DataNode.FromObject(new[]
                {
                    new KeyValuePair<string, DataNode>("id", DataNode.FromString(id)),
                    new KeyValuePair<string, DataNode>("value", values[id])
                }));
            }
            return DataNode.FromList(items);
        }
    }

    /// <summary>A bool/int variable declared in the catalog (ICatalog.FindDefinition("variable", id)). Derived clock.* names are never declarations.</summary>
    public sealed class VariableDeclaration
    {
        public const string ClockPrefix = "clock.";
        public const string MinuteOfDay = "clock.minute_of_day";

        public string Id { get; }
        public VariableType Type { get; }
        public long? Min { get; }
        public long? Max { get; }
        public DataNode Default { get; private set; }

        private VariableDeclaration(string id, VariableType type, long? min, long? max)
        {
            Id = id;
            Type = type;
            Min = min;
            Max = max;
        }

        public static bool IsDerived(string id)
        {
            return id != null && id.StartsWith(ClockPrefix, StringComparison.Ordinal);
        }

        public static bool TryResolve(ICatalog catalog, string id, out VariableDeclaration declaration, out Diagnostic error)
        {
            declaration = null;
            if (string.IsNullOrEmpty(id))
            {
                error = Diag.Make(ResultCodes.ContentInvalid, "variable name is empty", "declared variable", "empty");
                return false;
            }
            if (IsDerived(id))
            {
                error = Diag.Make(ResultCodes.ContentInvalid, "clock variables are derived from the calendar and read-only", "declared bool/int variable", id);
                return false;
            }
            var row = catalog == null ? null : catalog.FindDefinition("variable", id);
            if (row == null)
            {
                error = Diag.Make(ResultCodes.RefNotFound, "variable is not declared", "declared variable", id);
                return false;
            }
            DataNode typeNode;
            if (row.Kind != DataKind.Object || !row.TryGet("type", out typeNode) || typeNode.Kind != DataKind.String)
            {
                error = Diag.Make(ResultCodes.ContentInvalid, "variable declaration has no type", "type bool|int", id);
                return false;
            }
            VariableType type;
            switch (typeNode.Text)
            {
                case "bool": type = VariableType.Bool; break;
                case "int":
                case "integer": type = VariableType.Int; break;
                default:
                    error = Diag.Make(ResultCodes.ContentInvalid, "only declared bool/int variables are supported", "bool|int", typeNode.Text);
                    return false;
            }
            long? min;
            long? max;
            if (!TryBound(row, "min", type, id, out min, out error)) return false;
            if (!TryBound(row, "max", type, id, out max, out error)) return false;
            var result = new VariableDeclaration(id, type, min, max);
            DataNode declared;
            var initial = row.TryGet("initial", out declared) || row.TryGet("default", out declared) ? declared : (type == VariableType.Bool ? DataNode.False : DataNode.FromInteger(0));
            if (!result.TryAccept(initial, out error)) return false;
            result.Default = initial;
            declaration = result;
            error = null;
            return true;
        }

        private static bool TryBound(DataNode row, string key, VariableType type, string id, out long? bound, out Diagnostic error)
        {
            bound = null;
            error = null;
            DataNode node;
            if (!row.TryGet(key, out node)) return true;
            if (type != VariableType.Int || node.Kind != DataKind.Integer)
            {
                error = Diag.Make(ResultCodes.ContentInvalid, "variable bound '" + key + "' must be an integer on an int variable", "integer", id);
                return false;
            }
            bound = node.Integer;
            return true;
        }

        public bool TryAcceptKind(DataNode value, out Diagnostic error)
        {
            var wanted = Type == VariableType.Bool ? DataKind.Bool : DataKind.Integer;
            if (value == null || value.Kind != wanted)
            {
                error = Diag.Make(ResultCodes.ArgType, "variable '" + Id + "' is declared " + (Type == VariableType.Bool ? "bool" : "int"), wanted.ToString(), value == null ? "absent" : value.Kind.ToString());
                return false;
            }
            error = null;
            return true;
        }

        public bool TryAccept(DataNode value, out Diagnostic error)
        {
            if (!TryAcceptKind(value, out error)) return false;
            if (Type == VariableType.Int && ((Min.HasValue && value.Integer < Min.Value) || (Max.HasValue && value.Integer > Max.Value)))
            {
                error = Diag.Make(ResultCodes.InvariantViolation, "value is outside the declared range of '" + Id + "'",
                    "[" + (Min.HasValue ? Diag.Num(Min.Value) : "-inf") + ", " + (Max.HasValue ? Diag.Num(Max.Value) : "+inf") + "]", Diag.Value(value));
                return false;
            }
            return true;
        }

        public DataNode Current(MagicCampus.State.Progress progress)
        {
            if (progress.Flags.TryGet(Id, out var stored))
            {
                var node = stored.IsBool ? DataNode.FromBool(stored.Bool) : DataNode.FromInteger(stored.Int);
                if (TryAcceptKind(node, out var ignored)) return node;
            }
            return Default;
        }
    }
}
