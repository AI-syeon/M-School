using System;
using System.Collections.Generic;

namespace MagicCampus.Content.Parsing
{
    /// <summary>First syntax error found while scanning a call cell. Offsets are UTF-16 indexes into the original cell text.</summary>
    internal sealed class CallSyntaxException : Exception
    {
        public int CallIndex { get; }   // 1-based; 0 when the error is not tied to a call
        public int Offset { get; }
        public string Expected { get; }
        public string Actual { get; }

        public CallSyntaxException(int callIndex, int offset, string message, string expected, string actual)
            : base(message)
        {
            CallIndex = callIndex;
            Offset = offset;
            Expected = expected ?? string.Empty;
            Actual = actual ?? string.Empty;
        }
    }

    /// <summary>One space-separated token. Offset/End delimit the source range, quotes included.</summary>
    internal sealed class CallToken
    {
        public string Raw { get; }
        public bool Quoted { get; }
        public int Offset { get; }
        public int End { get; }

        public CallToken(string raw, bool quoted, int offset, int end)
        {
            Raw = raw;
            Quoted = quoted;
            Offset = offset;
            End = end;
        }

        public CallToken WithRaw(string raw, int offset) => new CallToken(raw, Quoted, offset, End);
    }

    /// <summary>Tokens between two top-level slashes. Offset is the index just after the previous slash.</summary>
    internal sealed class RawCall
    {
        public IReadOnlyList<CallToken> Tokens { get; }
        public int Offset { get; }

        public RawCall(IReadOnlyList<CallToken> tokens, int offset)
        {
            Tokens = tokens;
            Offset = offset;
        }
    }

    /// <summary>Character classes of the call grammar. ASCII-only where the grammar says so.</summary>
    internal static class CallLexicon
    {
        public const char Quote = (char)34;
        public const char Backslash = (char)92;
        public const char Slash = (char)47;
        public const char Bang = (char)33;
        public const char Colon = (char)58;
        public const char EqualsSign = (char)61;

        /// <summary>Same set as the reference parser's whitespace test.</summary>
        public static bool IsSpace(char c)
        {
            int n = c;
            return n == 9 || n == 10 || n == 11 || n == 12 || n == 13 || n == 32 || n == 0xA0 || n == 0x1680
                || (n >= 0x2000 && n <= 0x200A) || n == 0x2028 || n == 0x2029 || n == 0x202F || n == 0x205F
                || n == 0x3000 || n == 0xFEFF;
        }

        public static bool IsBlank(string text)
        {
            foreach (var c in text)
                if (!IsSpace(c)) return false;
            return true;
        }

        public static bool IsLower(char c) => c >= 'a' && c <= 'z';
        public static bool IsLetter(char c) => IsLower(c) || (c >= 'A' && c <= 'Z');
        public static bool IsDigit(char c) => c >= '0' && c <= '9';
        public static bool IsIdTail(char c) => IsLower(c) || IsDigit(c) || c == '_';
        public static bool IsNameChar(char c) => IsLetter(c) || IsDigit(c) || c == '_' || c == '.' || c == '-';

        /// <summary>
        /// Label forms: a whole token like device: (whole = true), or a glued prefix like device:setState / device:!name.
        /// colon is the index of the label colon.
        /// </summary>
        public static bool TrySplitLabel(string raw, out int colon, out bool whole)
        {
            colon = -1;
            whole = false;
            if (raw.Length < 2 || !IsLower(raw[0])) return false;
            var j = 1;
            while (j < raw.Length && IsIdTail(raw[j])) j++;
            if (j >= raw.Length || raw[j] != Colon) return false;
            if (j == raw.Length - 1)
            {
                colon = j;
                whole = true;
                return true;
            }
            var next = raw[j + 1];
            if (next == Bang || IsLower(next))
            {
                colon = j;
                return true;
            }
            return false;
        }

        /// <summary>Letter-led name of letters, digits, underscore, dot and hyphen; all-digit names only where numeric is allowed.</summary>
        public static bool IsFunctionName(string name, bool allowNumeric)
        {
            if (name.Length == 0) return false;
            if (IsLetter(name[0]))
            {
                for (var i = 1; i < name.Length; i++)
                    if (!IsNameChar(name[i])) return false;
                return true;
            }
            if (!allowNumeric) return false;
            foreach (var c in name)
                if (!IsDigit(c)) return false;
            return true;
        }

        /// <summary>Optional minus, then digits with optional fraction, or a leading-dot fraction.</summary>
        public static bool IsNumber(string s, out bool hasFraction)
        {
            hasFraction = false;
            var i = 0;
            var n = s.Length;
            if (i < n && s[i] == '-') i++;
            var digitsStart = i;
            while (i < n && IsDigit(s[i])) i++;
            var intDigits = i - digitsStart;
            if (i < n && s[i] == '.')
            {
                hasFraction = true;
                i++;
                var fracStart = i;
                while (i < n && IsDigit(s[i])) i++;
                if (i == fracStart) return false;
            }
            else if (intDigits == 0)
            {
                return false;
            }
            return i == n;
        }
    }
}
