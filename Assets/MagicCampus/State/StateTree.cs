using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.State
{
    /// <summary>Path-copying replacement inside an immutable DataNode tree. Arrays are only ever replaced as whole units.</summary>
    public static class StateTree
    {
        /// <summary>Returns the new root, or null with code/message when the path cannot be written. The input root is never modified.</summary>
        public static DataNode TryReplace(DataNode root, JsonPointer path, DataNode value, out string code, out string message)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (value == null) throw new ArgumentNullException(nameof(value));
            code = null;
            message = null;
            return Replace(root, path.Tokens, 0, value, ref code, ref message);
        }

        private static DataNode Replace(DataNode node, IReadOnlyList<string> tokens, int depth, DataNode value, ref string code, ref string message)
        {
            if (node.Kind == DataKind.List)
            {
                code = ResultCodes.ParseError;
                message = "array index paths are not writable; replace the array as a whole unit";
                return null;
            }
            if (node.Kind != DataKind.Object)
            {
                code = ResultCodes.RefNotFound;
                message = "parent of the target is not an object";
                return null;
            }

            var token = tokens[depth];
            if (depth == tokens.Count - 1) return node.With(token, value);

            if (!node.Members.TryGetValue(token, out var child))
            {
                code = ResultCodes.RefNotFound;
                message = "intermediate member does not exist: " + token;
                return null;
            }

            var replaced = Replace(child, tokens, depth + 1, value, ref code, ref message);
            return replaced == null ? null : node.With(token, replaced);
        }
    }
}
