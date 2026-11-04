using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Parsing
{
    /// <summary>
    /// Syntax-only parser for function-call cells. It does not look at the function dictionary, references, forks or handles.
    /// The first syntax error fails the whole cell (PARSE_ERROR) and no partial list is returned.
    /// </summary>
    public sealed class CallParser
    {
        public Outcome<CallList> Parse(CallKind kind, string text, string file, string pointer, string entityId)
        {
            if (!Enum.IsDefined(typeof(CallKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            try
            {
                if (text == null) throw new CallSyntaxException(0, 0, "call list must be a string", "string", "null");
                var calls = new List<CallNode>();
                if (!CallLexicon.IsBlank(text))
                {
                    var raw = CallTokenizer.Tokenize(text);
                    for (var i = 0; i < raw.Count; i++) calls.Add(BuildCall(kind, raw[i], i + 1));
                }
                return Outcome<CallList>.Ok(new CallList(kind, text, file, pointer, entityId, calls), 0);
            }
            catch (CallSyntaxException e)
            {
                return Failure(file, pointer, entityId, e);
            }
        }

        private static Outcome<CallList> Failure(string file, string pointer, string entityId, CallSyntaxException e)
        {
            var call = e.CallIndex > 0 ? (entityId ?? string.Empty) + "#" + e.CallIndex.ToString(CultureInfo.InvariantCulture) : string.Empty;
            var location = new SourceLocation(file, pointer, entityId, call, e.Offset);
            return Outcome<CallList>.Fail(ResultCodes.ParseError, 0, new Diagnostic(ResultCodes.ParseError, e.Message, location, e.Expected, e.Actual));
        }

        private static CallNode BuildCall(CallKind kind, RawCall call, int index)
        {
            var tokens = new List<CallToken>(call.Tokens);
            if (tokens.Count == 0)
                throw new CallSyntaxException(index, call.Offset, "empty call between slashes", "function call", "nothing");

            var spanStart = tokens[0].Offset;
            var spanEnd = tokens[tokens.Count - 1].End;

            var label = string.Empty;
            var first = tokens[0];
            if (!first.Quoted && CallLexicon.TrySplitLabel(first.Raw, out var colon, out var whole))
            {
                label = first.Raw.Substring(0, colon);
                if (whole) tokens.RemoveAt(0);
                else tokens[0] = first.WithRaw(first.Raw.Substring(colon + 1), first.Offset + colon + 1);
            }
            if (tokens.Count == 0)
                throw new CallSyntaxException(index, call.Offset, "label without call", "function call after label", "nothing");

            var head = tokens[0];
            if (head.Quoted)
                throw new CallSyntaxException(index, head.Offset, "function name cannot be quoted", "function name", "quoted text");

            var negated = head.Raw.Length > 0 && head.Raw[0] == CallLexicon.Bang;
            var name = negated ? head.Raw.Substring(1) : head.Raw;
            if (!CallLexicon.IsFunctionName(name, kind == CallKind.ScheduleEntry))
                throw new CallSyntaxException(index, head.Offset, "bad function name '" + name + "'", "function name", name);
            if (negated && kind != CallKind.When)
                throw new CallSyntaxException(index, head.Offset, "'!' is only allowed on conditions", "no '!'", "!");

            var args = new List<CallArgument>();
            var named = new HashSet<string>(StringComparer.Ordinal);
            for (var k = 1; k < tokens.Count; k++)
            {
                var tok = tokens[k];
                var eq = tok.Quoted ? -1 : tok.Raw.IndexOf(CallLexicon.EqualsSign);
                if (eq > 0)
                {
                    var key = tok.Raw.Substring(0, eq);
                    if (!named.Add(key))
                        throw new CallSyntaxException(index, tok.Offset, "duplicate named argument '" + key + "'", "unique name", key);
                    args.Add(MakeArgument(key, tok.Raw.Substring(eq + 1), false, tok.Offset + eq + 1, tok.End, index));
                }
                else
                {
                    if (named.Count > 0)
                        throw new CallSyntaxException(index, tok.Offset, "positional argument after named argument", "named argument", "positional argument");
                    args.Add(MakeArgument(string.Empty, tok.Raw, tok.Quoted, tok.Offset, tok.End, index));
                }
            }

            var nameOffset = head.Offset + (negated ? 1 : 0);
            return new CallNode(index, label, negated, name, new SourceSpan(nameOffset, name.Length), args, new SourceSpan(spanStart, spanEnd - spanStart));
        }

        /// <summary>
        /// Quoted text is String; true/false is Bool; plain digits that fit a long are Integer; any other number (decimal point, or out of long range) is Number;
        /// everything else (ids, label refs, words, unclassifiable tokens) is Word with the raw token text.
        /// </summary>
        private static CallArgument MakeArgument(string name, string raw, bool quoted, int offset, int end, int callIndex)
        {
            var span = new SourceSpan(offset, end - offset);
            if (quoted) return new CallArgument(name, ArgKind.String, raw, span);
            if (raw == "true" || raw == "false") return new CallArgument(name, ArgKind.Bool, raw, span);
            if (CallLexicon.IsNumber(raw, out var hasFraction))
            {
                if (!hasFraction && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                    return new CallArgument(name, ArgKind.Integer, raw, span);
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && !double.IsInfinity(d))
                    return new CallArgument(name, ArgKind.Number, raw, span);
                throw new CallSyntaxException(callIndex, offset, "number out of range", "finite number", raw.Length > 32 ? raw.Substring(0, 32) : raw);
            }
            return new CallArgument(name, ArgKind.Word, raw, span);
        }
    }
}
