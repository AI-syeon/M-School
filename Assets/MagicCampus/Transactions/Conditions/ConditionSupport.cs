using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Transactions.Conditions
{
    /// <summary>Builds diagnostics and renders values using only public Contracts types.</summary>
    public static class Diag
    {
        public static Diagnostic Make(string code, string message, string expected, string actual)
        {
            return new Diagnostic(code, message, null, expected, actual);
        }

        public static string Num(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Value(DataNode node)
        {
            if (node == null) return "<absent>";
            switch (node.Kind)
            {
                case DataKind.Null: return "null";
                case DataKind.Bool: return node.Bool ? "true" : "false";
                case DataKind.Integer: return Num(node.Integer);
                case DataKind.Number: return node.Number.ToString("R", CultureInfo.InvariantCulture);
                case DataKind.String: return node.Text;
                default: return node.Kind.ToString().ToLowerInvariant();
            }
        }
    }

    /// <summary>Positional access to compiled arguments through the function dictionary parameter names.</summary>
    public static class CallArgs
    {
        public static bool TryGet(CompiledCall call, int index, out DataNode node)
        {
            node = null;
            var parameters = call.Function.Params;
            return index >= 0 && index < parameters.Count && call.Args.TryGetValue(parameters[index].Name, out node);
        }
    }
}
