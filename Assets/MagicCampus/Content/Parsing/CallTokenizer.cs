using System.Collections.Generic;
using System.Text;

namespace MagicCampus.Content.Parsing
{
    /// <summary>
    /// Splits a call cell into calls on slashes outside quotes and into tokens on whitespace outside quotes.
    /// A quote must start a token; inside quotes only an escaped quote and an escaped backslash are allowed.
    /// Throws CallSyntaxException for the first error.
    /// </summary>
    internal static class CallTokenizer
    {
        internal static List<RawCall> Tokenize(string text)
        {
            var calls = new List<RawCall>();
            var tokens = new List<CallToken>();
            var buf = new StringBuilder();
            var quoted = false;
            var inQuote = false;
            var tokenStart = -1;
            var tokenEnd = 0;
            var callStart = 0;

            void Push()
            {
                if (tokenStart >= 0) tokens.Add(new CallToken(buf.ToString(), quoted, tokenStart, tokenEnd));
                buf.Clear();
                quoted = false;
                tokenStart = -1;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inQuote)
                {
                    if (ch == CallLexicon.Backslash)
                    {
                        var has = i + 1 < text.Length;
                        var next = has ? text[i + 1] : ' ';
                        if (!has || (next != CallLexicon.Quote && next != CallLexicon.Backslash))
                            throw new CallSyntaxException(calls.Count + 1, i, "only escaped quote and escaped backslash are allowed", "quote or backslash after backslash", has ? next.ToString() : "end of text");
                        buf.Append(next);
                        i++;
                        tokenEnd = i + 1;
                    }
                    else if (ch == CallLexicon.Quote)
                    {
                        inQuote = false;
                        tokenEnd = i + 1;
                    }
                    else
                    {
                        buf.Append(ch);
                        tokenEnd = i + 1;
                    }
                    continue;
                }

                if (ch == CallLexicon.Quote)
                {
                    if (tokenStart >= 0)
                        throw new CallSyntaxException(calls.Count + 1, i, "quote must start a token", "whitespace before quote", "quote inside token");
                    tokenStart = i;
                    quoted = true;
                    inQuote = true;
                    tokenEnd = i + 1;
                    continue;
                }

                if (ch == CallLexicon.Slash)
                {
                    Push();
                    calls.Add(new RawCall(tokens, callStart));
                    tokens = new List<CallToken>();
                    callStart = i + 1;
                    continue;
                }

                if (CallLexicon.IsSpace(ch))
                {
                    Push();
                    continue;
                }

                if (tokenStart < 0) tokenStart = i;
                buf.Append(ch);
                tokenEnd = i + 1;
            }

            if (inQuote)
                throw new CallSyntaxException(calls.Count + 1, text.Length, "unclosed quote", "closing quote", "end of text");
            Push();
            calls.Add(new RawCall(tokens, callStart));
            return calls;
        }
    }
}
