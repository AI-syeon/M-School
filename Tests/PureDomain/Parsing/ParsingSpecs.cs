using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Content.Parsing;
using MagicCampus.Contracts;

namespace MagicCampus.Tests.Parsing
{
    public static class ParsingSpecs
    {
        private const string FileName = "content/events.json";
        private const string Pointer = "/events/0/script";
        private const string Entity = "evt.x";

        // Fixture text uses ' for a double quote and ` for a backslash so offsets stay readable.
        // Expected positions were counted by hand (UTF-16 index, 0-based) and do not come from the parser.
        private static string Q(string s) => s.Replace((char)39, (char)34).Replace((char)96, (char)92);

        private sealed class SpecFailure : Exception
        {
            public SpecFailure(string message) : base(message) { }
        }

        // Expected string format per call: index:label:[!]function@nameOffset+nameLength[callOffset+callLength](args)
        // arg: [name=]Kind:value@offset+length with kinds I(nteger) N(umber) B(ool) S(tring) W(ord); calls joined by ' | '.
        private sealed class Positive
        {
            public readonly string Name;
            public readonly CallKind Kind;
            public readonly string Text;
            public readonly string Expected;

            public Positive(string name, CallKind kind, string text, string expected)
            {
                Name = name; Kind = kind; Text = Q(text); Expected = Q(expected);
            }
        }

        // Call is the 1-based call number in Location.Call (entity#call); 0 means no call is named.
        private sealed class Negative
        {
            public readonly string Name;
            public readonly CallKind Kind;
            public readonly string Text;
            public readonly int Call;
            public readonly int Offset;

            public Negative(string name, CallKind kind, string text, int call, int offset)
            {
                Name = name; Kind = kind; Text = Q(text); Call = call; Offset = offset;
            }
        }

        public static void Run()
        {
            var failures = new List<string>();
            Case(failures, "positive fixture table", PositiveFixtures);
            Case(failures, "negative fixture table", NegativeFixtures);
            Case(failures, "empty and blank cells parse zero calls for every kind", EmptyCells);
            Case(failures, "null text fails without a call", NullText);
            Case(failures, "undefined kind is a programming error", UndefinedKind);
            Case(failures, "numeric heads only for schedule entries", NumericHeads);
            Case(failures, "bang only for conditions", BangOnlyWhen);
            Case(failures, "integer and number classification limits", NumberLimits);
            Case(failures, "list metadata and Locate", MetadataAndLocate);
            Case(failures, "parsing is deterministic and keeps duplicate labels", Deterministic);
            if (failures.Count > 0) throw new SpecFailure("ParsingSpecs failed:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception e) { failures.Add("[" + name + "] " + e.Message); }
        }

        // ------------------------------------------------------------------ cases

        private static Positive[] Positives()
        {
            return new[]
            {
                new Positive("slash and escaped quotes inside quotes", CallKind.Script,
                    "say npc.serin 'a / b `'q`'' / wait 5",
                    "1::say@0+3[0+27](W:npc.serin@4+9,S:a / b 'q'@14+13) | 2::wait@30+4[30+6](I:5@35+1)"),
                new Positive("escaped backslash", CallKind.Script,
                    "say 'a``b'",
                    "1::say@0+3[0+10](S:a`b@4+6)"),
                new Positive("escaped quote alone", CallKind.Script,
                    "say '`''",
                    "1::say@0+3[0+8](S:'@4+4)"),
                new Positive("korean text with slash in quotes", CallKind.Script,
                    "say npc.serin '안녕 / 하세요'",
                    "1::say@0+3[0+24](W:npc.serin@4+9,S:안녕 / 하세요@14+10)"),
                new Positive("korean bare word", CallKind.Script,
                    "say 안녕",
                    "1::say@0+3[0+6](W:안녕@4+2)"),
                new Positive("empty quoted string", CallKind.Script,
                    "say ''",
                    "1::say@0+3[0+6](S:@4+2)"),
                new Positive("named arguments after positional", CallKind.Script,
                    "move npc.serin dest.snack timeout=30",
                    "1::move@0+4[0+36](W:npc.serin@5+9,W:dest.snack@15+10,timeout=I:30@34+2)"),
                new Positive("label with space", CallKind.Script,
                    "device: setState object.x supply_stopped count=2",
                    "1:device:setState@8+8[0+48](W:object.x@17+8,W:supply_stopped@26+14,count=I:2@47+1)"),
                new Positive("label glued to function", CallKind.Script,
                    "device:setState x",
                    "1:device:setState@7+8[0+17](W:x@16+1)"),
                new Positive("negated condition", CallKind.When,
                    "!completed node.a2",
                    "1::!completed@1+9[0+18](W:node.a2@11+7)"),
                new Positive("label then negated condition", CallKind.When,
                    "ok: !completed node.a2",
                    "1:ok:!completed@5+9[0+22](W:node.a2@15+7)"),
                new Positive("glued label and bang", CallKind.When,
                    "ok:!completed x",
                    "1:ok:!completed@4+9[0+15](W:x@14+1)"),
                new Positive("numeric schedule heads", CallKind.ScheduleEntry,
                    "420 dest.snack / 1080 home",
                    "1::420@0+3[0+14](W:dest.snack@4+10) | 2::1080@17+4[17+9](W:home@22+4)"),
                new Positive("typed scalars", CallKind.Effects,
                    "give 3 -2 7.5 .5 7.0 true false",
                    "1::give@0+4[0+31](I:3@5+1,I:-2@7+2,N:7.5@10+3,N:0.5@14+2,N:7@17+3,B:true@21+4,B:false@26+5)"),
                new Positive("bare label reference stays a word", CallKind.Script,
                    "perform puzzle room.case_a3:device",
                    "1::perform@0+7[0+34](W:puzzle@8+6,W:room.case_a3:device@15+19)"),
                new Positive("registry and references are not checked", CallKind.Script,
                    "zzz_unknown npc.nobody",
                    "1::zzz_unknown@0+11[0+22](W:npc.nobody@12+10)"),
                new Positive("id head for item kinds", CallKind.ItemQuantity,
                    "item.herb_a 2",
                    "1::item.herb_a@0+11[0+13](I:2@12+1)"),
                new Positive("id heads for encounter enemies", CallKind.EncounterEnemy,
                    "enemy.slime 3 / enemy.bat 1",
                    "1::enemy.slime@0+11[0+13](I:3@12+1) | 2::enemy.bat@16+9[16+11](I:1@26+1)"),
                new Positive("same label on two calls is still syntax-valid", CallKind.Script,
                    "a: x1 / a: x2",
                    "1:a:x1@3+2[0+5]() | 2:a:x2@11+2[8+5]()"),
                new Positive("tab and newline separate tokens", CallKind.Script,
                    "say" + (char)9 + "a" + (char)10 + "/" + (char)10 + "wait",
                    "1::say@0+3[0+5](W:a@4+1) | 2::wait@8+4[8+4]()"),
                new Positive("slash without spaces", CallKind.Script,
                    "a/b",
                    "1::a@0+1[0+1]() | 2::b@2+1[2+1]()"),
                new Positive("named value keeps later equals sign", CallKind.Script,
                    "say k=a=b",
                    "1::say@0+3[0+9](k=W:a=b@6+3)"),
                new Positive("leading equals is positional", CallKind.Script,
                    "say =x",
                    "1::say@0+3[0+6](W:=x@4+2)"),
                new Positive("leading whitespace shifts offsets", CallKind.Script,
                    "  say a",
                    "1::say@2+3[2+5](W:a@6+1)"),
            };
        }

        private static Negative[] Negatives()
        {
            return new[]
            {
                new Negative("unclosed quote", CallKind.Script, "say npc.a 'open", 1, 15),
                new Negative("unclosed quote in second call", CallKind.Script, "say a / say 'x", 2, 14),
                new Negative("backslash at end inside quote", CallKind.Script, "say 'a`", 1, 6),
                new Negative("unsupported escape", CallKind.Script, "say 'a`n'", 1, 6),
                new Negative("quote inside token", CallKind.Script, "say a'b'", 1, 5),
                new Negative("quoted named value", CallKind.Script, "say x='y'", 1, 6),
                new Negative("empty call in the middle", CallKind.Script, "say a / / b", 2, 7),
                new Negative("trailing slash", CallKind.Script, "say a /", 2, 7),
                new Negative("leading slash", CallKind.Script, "/ say a", 1, 0),
                new Negative("double slash", CallKind.Script, "say a //b", 2, 7),
                new Negative("label without call", CallKind.Script, "device:", 1, 0),
                new Negative("label without call in second call", CallKind.Script, "say a / dev:", 2, 7),
                new Negative("quoted function name", CallKind.Script, "'say' x", 1, 0),
                new Negative("quoted function name after label", CallKind.Script, "d: 'say' x", 1, 3),
                new Negative("function name with bad character", CallKind.Script, "sa@y", 1, 0),
                new Negative("digit led function name", CallKind.Script, "9x a", 1, 0),
                new Negative("numeric head outside schedule", CallKind.Script, "420 x", 1, 0),
                new Negative("uppercase label is not a label", CallKind.Script, "Dev:x a", 1, 0),
                new Negative("glued label then bad name", CallKind.Script, "a:b:c x", 1, 2),
                new Negative("lone bang", CallKind.When, "!", 1, 0),
                new Negative("double bang", CallKind.When, "!!x", 1, 0),
                new Negative("bang outside when", CallKind.Script, "!completed x", 1, 0),
                new Negative("bang after label outside when", CallKind.Effects, "ok: !completed x", 1, 4),
                new Negative("bang in second call", CallKind.Script, "a / !b", 2, 4),
                new Negative("positional after named", CallKind.Script, "move a timeout=30 b", 1, 18),
                new Negative("duplicate named", CallKind.Script, "move a t=1 t=2", 1, 11),
                new Negative("quoted positional after named", CallKind.Script, "say x=1 'y'", 1, 8),
                new Negative("tokenizer error beats earlier syntax error", CallKind.Script, "!x / say 'open", 2, 14),
                new Negative("first syntax error wins", CallKind.Script, "9x / !y", 1, 0),
                new Negative("number out of range", CallKind.Effects, "give " + new string('9', 400), 1, 5),
            };
        }

        private static void PositiveFixtures()
        {
            foreach (var f in Positives())
            {
                var o = P(f.Kind, f.Text);
                True(o.Succeeded, f.Name + ": must succeed but was " + o.Result.Code);
                Equal(f.Expected, Describe(o.Value), f.Name);
            }
        }

        private static void NegativeFixtures()
        {
            foreach (var f in Negatives())
                AssertParseError(f.Name, P(f.Kind, f.Text), f.Call, f.Offset);
        }

        private static void EmptyCells()
        {
            foreach (CallKind k in Enum.GetValues(typeof(CallKind)))
            {
                foreach (var text in new[] { "", "   ", " " + (char)9 + (char)13 + (char)10 + " " })
                {
                    var o = P(k, text);
                    True(o.Succeeded, k + " blank must succeed");
                    Equal(0, o.Value.Calls.Count, k + " blank call count");
                    Equal(text, o.Value.Text, k + " blank text kept");
                }
            }
        }

        private static void NullText()
        {
            AssertParseError("null text", P(CallKind.Script, null), 0, 0);
        }

        private static void UndefinedKind()
        {
            var threw = false;
            try { P((CallKind)999, "say a"); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            True(threw, "undefined kind must throw ArgumentOutOfRangeException");
        }

        private static void NumericHeads()
        {
            foreach (CallKind k in Enum.GetValues(typeof(CallKind)))
            {
                var o = P(k, "420 x");
                if (k == CallKind.ScheduleEntry)
                {
                    True(o.Succeeded, "schedule numeric head must succeed");
                    Equal("420", o.Value.Calls[0].Function, "schedule head");
                }
                else
                {
                    AssertParseError(k + " numeric head", o, 1, 0);
                }
            }
        }

        private static void BangOnlyWhen()
        {
            foreach (CallKind k in Enum.GetValues(typeof(CallKind)))
            {
                var o = P(k, "!completed x");
                if (k == CallKind.When)
                {
                    True(o.Succeeded, "when bang must succeed");
                    True(o.Value.Calls[0].Negated, "negated flag");
                    Equal("completed", o.Value.Calls[0].Function, "function without bang");
                    Equal(1, o.Value.Calls[0].NameSpan.Offset, "name offset skips bang");
                }
                else
                {
                    AssertParseError(k + " bang", o, 1, 0);
                }
            }
        }

        private static void NumberLimits()
        {
            var o = P(CallKind.Effects, "give 9223372036854775807 -9223372036854775808 9223372036854775808 99999999999999999999");
            True(o.Succeeded, "extreme numbers must parse");
            var a = o.Value.Calls[0].Arguments;
            Equal(ArgKind.Integer, a[0].Kind, "long max kind");
            Equal(long.MaxValue, a[0].Integer, "long max value");
            Equal(ArgKind.Integer, a[1].Kind, "long min kind");
            Equal(long.MinValue, a[1].Integer, "long min value");
            Equal(ArgKind.Number, a[2].Kind, "2^63 falls back to number");
            Equal(Math.Pow(2, 63), a[2].Number, "2^63 value");
            Equal(ArgKind.Number, a[3].Kind, "1e20 falls back to number");
            Equal(1e20, a[3].Number, "1e20 value");
        }

        private static void MetadataAndLocate()
        {
            var text = Q("say npc.serin 'a / b `'q`'' / wait 5");
            var o = P(CallKind.Script, text);
            True(o.Succeeded, "parse");
            var list = o.Value;
            Equal(CallKind.Script, list.Kind, "kind");
            Equal(text, list.Text, "text");
            Equal(FileName, list.File, "file");
            Equal(Pointer, list.Pointer, "pointer");
            Equal(Entity, list.EntityId, "entity");
            Equal(2, list.Calls.Count, "call count");
            Equal(1, list.Calls[0].Index, "first index");
            Equal(2, list.Calls[1].Index, "second index");
            Equal(Q("a / b 'q'"), list.Calls[0].Arguments[1].Text, "unescaped string");
            var loc = list.Locate(list.Calls[1], 2);
            Equal(Entity + "#2", loc.Call, "locate call");
            Equal(32, loc.Offset, "locate offset");
            Equal(FileName, loc.File, "locate file");
            Equal(Pointer, loc.Pointer, "locate pointer");
        }

        private static void Deterministic()
        {
            var text = "a: x1 / a: x2 / b: y count=1";
            var one = Describe(P(CallKind.Script, text).Value);
            var two = Describe(new CallParser().Parse(CallKind.Script, text, FileName, Pointer, Entity).Value);
            Equal(one, two, "two parses");
            Equal("a", P(CallKind.Script, text).Value.Calls[1].Label, "duplicate label preserved for stage 3");
        }

        // ------------------------------------------------------------------ helpers

        private static Outcome<CallList> P(CallKind kind, string text)
        {
            return new CallParser().Parse(kind, text, FileName, Pointer, Entity);
        }

        private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

        private static string Value(CallArgument a)
        {
            switch (a.Kind)
            {
                case ArgKind.Integer: return "I:" + I(a.Integer);
                case ArgKind.Number: return "N:" + a.Number.ToString("R", CultureInfo.InvariantCulture);
                case ArgKind.Bool: return "B:" + (a.Bool ? "true" : "false");
                case ArgKind.String: return "S:" + a.Text;
                default: return "W:" + a.Text;
            }
        }

        private static string Describe(CallList list)
        {
            var calls = new List<string>();
            foreach (var c in list.Calls)
            {
                var args = new List<string>();
                foreach (var a in c.Arguments)
                    args.Add((a.Name.Length > 0 ? a.Name + "=" : string.Empty) + Value(a) + "@" + I(a.Span.Offset) + "+" + I(a.Span.Length));
                calls.Add(I(c.Index) + ":" + c.Label + ":" + (c.Negated ? "!" : string.Empty) + c.Function
                    + "@" + I(c.NameSpan.Offset) + "+" + I(c.NameSpan.Length)
                    + "[" + I(c.Span.Offset) + "+" + I(c.Span.Length) + "](" + string.Join(",", args) + ")");
            }
            return string.Join(" | ", calls);
        }

        private static void AssertParseError(string what, Outcome<CallList> o, int call, int offset)
        {
            True(!o.Succeeded, what + ": must fail");
            Equal(OperationStatus.Failed, o.Result.Status, what + " status");
            Equal(ResultCodes.ParseError, o.Result.Code, what + " code");
            True(o.Value == null, what + ": no partial tree");
            Equal(1, o.Result.Details.Count, what + " diagnostic count");
            var d = o.Result.Details[0];
            Equal(ResultCodes.ParseError, d.Code, what + " diagnostic code");
            True(d.Location != null, what + ": location present");
            Equal(FileName, d.Location.File, what + " file");
            Equal(Pointer, d.Location.Pointer, what + " pointer");
            Equal(Entity, d.Location.EntityId, what + " entity");
            Equal(call > 0 ? Entity + "#" + I(call) : string.Empty, d.Location.Call, what + " call");
            Equal(offset, d.Location.Offset, what + " offset");
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new SpecFailure(what + ": expected <" + expected + "> but was <" + actual + ">");
        }

        private static void True(bool condition, string what)
        {
            if (!condition) throw new SpecFailure(what);
        }
    }
}
