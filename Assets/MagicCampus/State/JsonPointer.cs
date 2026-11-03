using System;
using System.Collections.Generic;
using System.Text;

namespace MagicCampus.State
{
    /// <summary>Non-root RFC 6901 JSON Pointer. Tokens are decoded exactly (~1 to slash, ~0 to tilde); empty tokens and unknown escapes are malformed.</summary>
    public sealed class JsonPointer : IEquatable<JsonPointer>
    {
        private readonly string[] tokens;

        public string Text { get; }
        public IReadOnlyList<string> Tokens { get; }

        private JsonPointer(string text, string[] decoded)
        {
            Text = text;
            tokens = decoded;
            Tokens = Array.AsReadOnly(decoded);
        }

        public static bool TryParse(string text, out JsonPointer pointer, out string error)
        {
            pointer = null;
            error = null;
            if (text == null) { error = "pointer is null"; return false; }
            if (text.Length == 0 || text[0] != '/') { error = "pointer must start with '/' and must not address the root"; return false; }

            var decoded = new List<string>();
            var sb = new StringBuilder();
            for (var i = 1; i <= text.Length; i++)
            {
                if (i == text.Length || text[i] == '/')
                {
                    if (sb.Length == 0) { error = "empty pointer token"; return false; }
                    decoded.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                var c = text[i];
                if (c == '~')
                {
                    if (i + 1 >= text.Length) { error = "dangling '~' escape"; return false; }
                    var n = text[i + 1];
                    if (n == '0') sb.Append('~');
                    else if (n == '1') sb.Append('/');
                    else { error = "unknown '~' escape"; return false; }
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }

            pointer = new JsonPointer(text, decoded.ToArray());
            return true;
        }

        public static JsonPointer Parse(string text)
        {
            if (!TryParse(text, out var pointer, out var error)) throw new FormatException(error);
            return pointer;
        }

        /// <summary>True when this pointer equals other or is one of its ancestors.</summary>
        public bool IsPrefixOf(JsonPointer other)
        {
            if (other == null || tokens.Length > other.tokens.Length) return false;
            for (var i = 0; i < tokens.Length; i++)
                if (!string.Equals(tokens[i], other.tokens[i], StringComparison.Ordinal)) return false;
            return true;
        }

        public bool Overlaps(JsonPointer other) => IsPrefixOf(other) || (other != null && other.IsPrefixOf(this));

        public bool Equals(JsonPointer other) => other != null && string.Equals(Text, other.Text, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as JsonPointer);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);
        public override string ToString() => Text;
    }
}
