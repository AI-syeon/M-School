using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Parsing
{
    public enum CallKind { Trigger, When, Script, Fork, Effects, ScheduleEntry, Goal, Hint, Behavior, Weakness, ReactionPath, EncounterEnemy, ItemQuantity, ItemGrant, Substitute, FinalStage }

    public enum ArgKind { Word, Integer, Number, Bool, String }

    /// <summary>Character range inside the original cell text.</summary>
    public sealed class SourceSpan
    {
        public int Offset { get; }
        public int Length { get; }
        public SourceSpan(int offset, int length)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Offset = offset; Length = length;
        }
    }

    public sealed class CallArgument
    {
        public string Name { get; }   // empty for positional arguments
        public ArgKind Kind { get; }
        public string Text { get; }   // unquoted and unescaped for String, raw token otherwise
        public long Integer { get; }  // valid for Integer
        public double Number { get; } // valid for Number
        public bool Bool { get; }     // valid for Bool
        public SourceSpan Span { get; }

        public CallArgument(string name, ArgKind kind, string text, SourceSpan span)
        {
            Name = Ids.Opt(name); Kind = kind; Text = PortGuard.Required(text, nameof(text)); Span = PortGuard.Required(span, nameof(span));
            if (kind == ArgKind.Bool && text != "true" && text != "false") throw new FormatException(nameof(text));
            Integer = kind == ArgKind.Integer ? long.Parse(text, CultureInfo.InvariantCulture) : 0;
            Number = kind == ArgKind.Number ? PortGuard.Finite(double.Parse(text, CultureInfo.InvariantCulture), nameof(text)) : 0;
            Bool = kind == ArgKind.Bool && text == "true";
        }
    }

    public sealed class CallNode
    {
        public int Index { get; }   // 1-based position in the list
        public string Label { get; }
        public bool Negated { get; }
        public string Function { get; }
        public SourceSpan NameSpan { get; }
        public IReadOnlyList<CallArgument> Arguments { get; }   // source order
        public SourceSpan Span { get; }

        public CallNode(int index, string label, bool negated, string function, SourceSpan nameSpan, IEnumerable<CallArgument> arguments, SourceSpan span)
        {
            if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
            Index = index; Label = Ids.Opt(label); Negated = negated; Function = Ids.Require(function, nameof(function));
            NameSpan = PortGuard.Required(nameSpan, nameof(nameSpan)); Span = PortGuard.Required(span, nameof(span));
            Arguments = PortGuard.Opt(arguments, nameof(arguments));
        }
    }

    public sealed class CallList
    {
        public CallKind Kind { get; }
        public string Text { get; }
        public string File { get; }
        public string Pointer { get; }
        public string EntityId { get; }   // for forks: event.id:forkName
        public IReadOnlyList<CallNode> Calls { get; }

        public CallList(CallKind kind, string text, string file, string pointer, string entityId, IEnumerable<CallNode> calls)
        {
            Kind = kind; Text = PortGuard.Required(text, nameof(text)); File = Ids.Opt(file); Pointer = Ids.Opt(pointer); EntityId = Ids.Opt(entityId);
            Calls = PortGuard.Opt(calls, nameof(calls));
        }

        /// <summary>Location in the form entity#index plus the offset of the call (or of a point inside it).</summary>
        public SourceLocation Locate(CallNode node, int offsetInCall)
            => new SourceLocation(File, Pointer, EntityId, EntityId + "#" + node.Index, node.Span.Offset + offsetInCall);
    }
}
