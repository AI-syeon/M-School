using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;

static partial class Program
{
    // ------------------------------------------------------------------ bundle rules for spells, reactions, targets

    static List<string> MagicProblems(Content c)
    {
        var p = new List<string>();
        void Rule(bool ok, string what) { if (!ok) p.Add(what); }
        bool Text(string s) => !string.IsNullOrWhiteSpace(s);
        bool Spell(string id) => c.Spells.Any(s => s.Id == id);

        Rule(c.Spells.Select(s => s.Id).Distinct().Count() == c.Spells.Count, "spell ids are unique");
        foreach (var s in c.Spells)
        {
            Rule(Text(s.Id) && Text(s.Name), s.Id + " has an id and a name");
            Rule(s.Runes != null && s.Runes.Length >= 2 && s.Runes.Length <= Magic.MaxRunes && s.Runes.All(r => r >= Magic.MinRune && r <= Magic.MaxRune), s.Id + " has 2-5 runes of 1-4");
            Rule(s.Range > 0, s.Id + " range > 0");
            Rule(!(s.UnlocksQuickCast && s.AllowQuickCast), s.Id + " unlocks quick cast so it cannot sit in a slot");
            foreach (var r in s.Requirements)
            {
                Rule(Magic.RequirementKinds.Contains(r.Kind), s.Id + " requirement kind " + r.Kind + " is known");
                Rule(Magic.ProvidedKinds.Contains(r.Kind), s.Id + " requirement kind " + r.Kind + " has no provider");
                Rule(Text(r.Id) && r.Min >= 1, s.Id + " requirement " + r.Id + " has an id and min >= 1");
            }
        }
        foreach (var group in c.Spells.Where(s => s.Runes != null).GroupBy(s => string.Join(",", s.Runes)).Where(g => g.Count() > 1))
            Rule(false, "duplicate rune array " + group.Key + " on " + string.Join(" and ", group.Select(s => s.Id)));

        Rule(c.Reactions.Select(r => r.Id).Distinct().Count() == c.Reactions.Count, "reaction ids are unique");
        foreach (var r in c.Reactions)
        {
            Rule(Text(r.Id) && Text(r.Observe), r.Id + " has an id and observe text");
            Rule(r.Spell == "*" ? r.Kind == ReactionKind.Protect : Spell(r.Spell), r.Id + " names a known spell (\"*\" only on Protect)");
            Rule(!c.Spells.Any(s => s.Id == r.Spell && s.UnlocksQuickCast), r.Id + " is for a self spell, which never reaches the table");
            Rule(r.Needs.All(Text) && r.Remove.All(Text) && r.Add.All(a => Text(a.Tag) && (!a.Seconds.HasValue || a.Seconds.Value > 0)), r.Id + " tags are named and windows are positive");
            Rule(r.Kind != ReactionKind.Protect || (r.Needs.Count > 0 && r.Add.Count == 0 && r.Remove.Count == 0 && r.Context == null), r.Id + " protection needs a tag and changes nothing");
        }

        Rule(c.MagicTargets.Select(t => t.Id).Distinct().Count() == c.MagicTargets.Count, "target ids are unique");
        foreach (var t in c.MagicTargets)
        {
            var map = c.Maps.FirstOrDefault(m => m.Id == t.MapId);
            Rule(Text(t.Id) && Text(t.Name) && t.Tags.All(Text), t.Id + " has an id, a name and named tags");
            Rule(map != null && World.Passable(map, (int)Math.Floor(t.X), (int)Math.Floor(t.Y)), t.Id + " stands on a passable cell");
        }
        return p;
    }

    // ------------------------------------------------------------------ helpers

    const string SpellStart = "start-spells.json";

    /// <summary>A start-spells game whose only target "t" stands one tile north of the player, who faces it.</summary>
    static Game MagicGame(params string[] tags)
    {
        var g = NewGame(SpellStart);
        g.Content.MagicTargets.Clear();
        g.Content.MagicTargets.Add(new MagicTargetDef { Id = "t", Name = "시험대", MapId = "map.courtyard", X = 1.5, Y = 2.5, Tags = tags.ToList() });
        g.ResetMagicTargets();
        g.Player.Facing = "north";
        return g;
    }

    static CastResult CastSpell(Game g, string name) => g.TryCastSpell("spell." + name);

    static string TagsOf(Game g) => string.Join(",", g.Magic.Tags("t").OrderBy(x => x));

    static string Snapshot(Game g) => SaveFile.ToJson(g.State) + "|" + string.Join(";", g.Content.MagicTargets.Select(t => t.Id + "=" + string.Join(",", g.Magic.Tags(t.Id).OrderBy(x => x))));

    static void Type(Game g, params int[] runes)
    {
        g.Magic.Begin();
        foreach (var r in runes) g.Magic.AddRune(r);
    }

    static ReactionDef Row(string id, ReactionKind kind, string spell, string[] needs, string[] remove = null, string context = null)
    {
        return new ReactionDef { Id = id, Kind = kind, Spell = spell, Needs = needs.ToList(), Remove = (remove ?? new string[0]).ToList(), Observe = id, Context = context };
    }

    // ------------------------------------------------------------------ tests

    static void MagicTests()
    {
        BundleAndFixtures();
        CastOrder();
        Combos();
        Situations();
        RejectedCastsChangeNothing();
        RequirementThresholds();
        IncantationRules();
        IncantationResets();
        Aiming();
        QuickCast();
        MagicSaves();
    }

    static void BundleAndFixtures()
    {
        var c = Content.Load(Base);
        Check("magic bundle: 13 spells, the prefix pair [1,2] and [1,2,3] coexists", c.Spells.Count == 13
            && c.Spell("spell.lantern").Runes.SequenceEqual(new[] { 1, 2 }) && c.Spell("spell.levitate").Runes.SequenceEqual(new[] { 1, 2, 3 }) && MagicProblems(c).Count == 0);
        Check("magic bundle: the base bundle ships no requirement because no system provides one", Magic.ProvidedKinds.Length == 0 && c.Spells.All(s => s.Requirements.Count == 0));
        var start = SaveFile.Read(Path.Combine(Base, SpellStart));
        Check("magic fixture: start-spells knows lantern and the unlock spell, with the slots still locked",
            start.KnownSpells.Contains("spell.lantern") && start.KnownSpells.Contains("spell.quickcast") && !start.QuickCastUnlocked && c.MagicTargets.Any(t => t.Id == "target.signboard" && t.Tags.SequenceEqual(new[] { "dark" })));
        var sign = c.MagicTargets.First(t => t.Id == "target.signboard");
        Check("magic fixture: the dark signboard is within 3 tiles of the start position", World.Distance(new Actor { MapId = start.Player.MapId, X = start.Player.X, Y = start.Player.Y }, new Spot { MapId = sign.MapId, X = sign.X, Y = sign.Y }) <= 3 && sign.MapId == start.Player.MapId);

        string Broken(Action<Content> edit)
        {
            var copy = Content.Load(Base);
            edit(copy);
            return string.Join("; ", Problems(copy));
        }
        Check("magic rules catch an identical rune array", Broken(x => x.Spells[2].Runes = new[] { 1, 2 }).Contains("duplicate rune array 1,2"));
        Check("magic rules catch a requirement kind no system provides", Broken(x => x.Spell("spell.lantern").Requirements.Add(new RequirementDef { Kind = "itemCountAtLeast", Id = "item.lens", Min = 1 })).Contains("has no provider"));
        Check("magic rules catch an unknown requirement kind and a min below 1", Broken(x => x.Spell("spell.lantern").Requirements.Add(new RequirementDef { Kind = "weird", Id = "x", Min = 0 })).Contains("kind weird is known"));
        Check("magic rules catch runes out of range", Broken(x => x.Spell("spell.lantern").Runes = new[] { 1 }).Contains("2-5 runes") && Broken(x => x.Spell("spell.lantern").Runes = new[] { 1, 5 }).Contains("2-5 runes"));
        Check("magic rules catch a protection that changes state", Broken(x => x.Reactions[0].Remove.Add("person")).Contains("protection"));
        Check("magic rules catch \"*\" outside Protect and unknown spells", Broken(x => x.Reactions.Add(Row("bad", ReactionKind.React, "*", new string[0]))).Contains("known spell")
            && Broken(x => x.Reactions.Add(Row("bad2", ReactionKind.React, "spell.nope", new string[0]))).Contains("known spell"));
        Check("magic rules catch a target off its map", Broken(x => x.MagicTargets[0].MapId = "map.nowhere").Contains("passable"));
        Check("magic rules catch a quick-castable unlock spell", Broken(x => x.Spell("spell.quickcast").AllowQuickCast = true).Contains("cannot sit in a slot"));
    }

    static void CastOrder()
    {
        foreach (var tag in new[] { "person", "private", "noMagic" })
        {
            var g = MagicGame(tag, "wet");
            var before = Snapshot(g);
            var r = CastSpell(g, "frost");
            Check("protection " + tag + ": rejects frost before the wet combo and changes nothing", !r.Ok && r.Failure == CastFailure.Protected && r.ReactionId.StartsWith("protect.") && Snapshot(g) == before);
        }
        var ember = MagicGame("plant", "seed_dry", "germinating");
        Check("protection: ember on a living plant is rejected even when the R4 combo tags are present", CastSpell(ember, "ember").Failure == CastFailure.Protected && TagsOf(ember) == "germinating,plant,seed_dry");
        Check("protection: lantern is rejected on a person too", CastSpell(MagicGame("person", "dark"), "lantern").Failure == CastFailure.Protected);

        var dark = MagicGame("dark");
        var r1 = CastSpell(dark, "lantern");
        Check("lantern: allowed on dark, removes dark and lights the target, and logs the situation",
            r1.Ok && r1.ReactionId == "react.lantern_dark" && TagsOf(dark) == "lit" && dark.State.SituationLog["spell.lantern"].SequenceEqual(new[] { "lantern.dark" }));
        var record = MagicGame("record");
        Check("lantern: allowed on record", CastSpell(record, "lantern").ReactionId == "react.lantern_record" && TagsOf(record) == "lit,record");
        var again = CastSpell(dark, "lantern");
        Check("lantern: a second cast on a lit target only observes", again.Ok && again.ReactionId == "react.lantern_lit" && TagsOf(dark) == "lit" && dark.State.SituationLog["spell.lantern"].Count == 1);

        var plain = MagicGame("wet");
        var none = CastSpell(plain, "lantern");
        Check("no reaction: a plain observation, success, no state change", none.Ok && none.ReactionId == null && none.Observe == "아무 반응이 없습니다." && TagsOf(plain) == "wet" && plain.State.SituationLog.Count == 0);

        var kinds = MagicGame("wet", "dark");
        kinds.Content.Reactions.Insert(0, Row("late.protect", ReactionKind.Protect, "spell.ripple", new[] { "wet" }));
        kinds.Content.Reactions.Insert(0, Row("early.react", ReactionKind.React, "spell.ripple", new[] { "wet" }));
        kinds.Content.Reactions.Insert(0, Row("early.combo", ReactionKind.Combo, "spell.ripple", new[] { "wet" }));
        Check("table order: Protect beats Combo beats React whatever the file order", CastSpell(kinds, "ripple").ReactionId == "late.protect");
        kinds.Content.Reactions.RemoveAll(r => r.Id == "late.protect");
        Check("table order: Combo beats React", CastSpell(kinds, "ripple").ReactionId == "early.combo");
        kinds.Content.Reactions.RemoveAll(r => r.Id == "early.combo");
        Check("table order: React rows go by file order", CastSpell(kinds, "ripple").ReactionId == "early.react");
    }

    static void Combos()
    {
        // R1 ripple + frost
        var g = MagicGame();
        CastSpell(g, "ripple");
        var r = CastSpell(g, "frost");
        Check("R1 ripple then frost: the wet surface freezes", r.ReactionId == "combo.r1_frost_wet" && TagsOf(g) == "frozen");
        Run(g, 19);
        var stillFrozen = TagsOf(g) == "frozen";
        Run(g, 2);
        Check("R1 window: frozen lasts 20 seconds then the surface is plain again", stillFrozen && TagsOf(g) == "");
        var rev = MagicGame();
        var first = CastSpell(rev, "frost");
        CastSpell(rev, "ripple");
        Check("R1 reverse: frost first observes no wet surface, ripple then only wets", first.ReactionId == "react.frost_dry" && first.Message.Contains("젖은 표면이 없습니다") && TagsOf(rev) == "wet");

        // R2 gust + echo, both orders, 6 second window
        var a = MagicGame("resonant");
        CastSpell(a, "gust");
        Run(a, 5);
        Check("R2 gust then echo inside 6 seconds aligns the sound", CastSpell(a, "echo").ReactionId == "combo.r2_echo_wind" && TagsOf(a) == "resonant,sound_aligned");
        var b = MagicGame("resonant");
        CastSpell(b, "echo");
        Run(b, 5);
        Check("R2 echo then gust inside 6 seconds gives the same result", CastSpell(b, "gust").ReactionId == "combo.r2_gust_sound" && TagsOf(b) == "resonant,sound_aligned");
        var late = MagicGame("resonant");
        CastSpell(late, "gust");
        Run(late, 7);
        Check("R2 window: after 6 seconds the wind path is gone and echo is only a single reaction", CastSpell(late, "echo").ReactionId == "react.echo_resonant" && TagsOf(late) == "resonant,sound_source");

        // R3 levitate + ward
        var lift = MagicGame("liftable");
        CastSpell(lift, "levitate");
        Check("R3 levitate then ward pins the floating object", CastSpell(lift, "ward").ReactionId == "combo.r3_ward_floating" && TagsOf(lift) == "liftable,pinned");
        Run(lift, 21);
        Check("R3 window: the pin ends after 20 seconds", TagsOf(lift) == "liftable");
        var ward = MagicGame("liftable");
        CastSpell(ward, "ward");
        var blocked = CastSpell(ward, "levitate");
        Check("R3 reverse: ward first, the barrier stops levitate", blocked.ReactionId == "react.levitate_warded" && blocked.Message.Contains("막이") && TagsOf(ward) == "liftable,warded");

        // R4 sprout + ember
        var seed = MagicGame("seed_dry");
        CastSpell(seed, "sprout");
        Check("R4 sprout then ember opens the husk", CastSpell(seed, "ember").ReactionId == "combo.r4_ember_germinating" && TagsOf(seed) == "husk_open");
        var soot = MagicGame("seed_dry");
        var sooted = CastSpell(soot, "ember");
        Check("R4 reverse: ember first leaves soot and loses nothing", sooted.ReactionId == "react.ember_seed_dry" && TagsOf(soot) == "seed_dry,soot");
        CastSpell(soot, "sprout");
        Check("R4 after the soot, sprout then ember still opens the husk", CastSpell(soot, "ember").ReactionId == "combo.r4_ember_germinating");

        // R5 lantern + mirage
        var rec = MagicGame("record");
        CastSpell(rec, "lantern");
        Check("R5 lantern then mirage shows the record", CastSpell(rec, "mirage").ReactionId == "combo.r5_mirage_lit_record" && TagsOf(rec) == "lit,record,scene_shown");
        var dim = MagicGame("record");
        var blur = CastSpell(dim, "mirage");
        Check("R5 reverse: mirage first is too dim", blur.ReactionId == "react.mirage_record" && blur.Message.Contains("흐릿") && TagsOf(dim) == "record");

        // R6 mend + stonecut, any order, until reset
        var m1 = MagicGame("broken");
        var mend = CastSpell(m1, "mend");
        Check("R6 one half alone says parts are missing", mend.ReactionId == "react.mend_broken" && mend.Message.Contains("부품이 부족") && TagsOf(m1) == "broken,mended");
        Run(m1, 120);
        Check("R6 accumulation has no time limit", CastSpell(m1, "stonecut").ReactionId == "combo.r6_stonecut_mended" && TagsOf(m1) == "restored");
        var m2 = MagicGame("broken");
        var cut = CastSpell(m2, "stonecut");
        Check("R6 stonecut alone says there is no join", cut.Message.Contains("결합이 없습니다") && TagsOf(m2) == "broken,shaped");
        Check("R6 stonecut then mend restores too", CastSpell(m2, "mend").ReactionId == "combo.r6_mend_shaped" && TagsOf(m2) == "restored");
        var m3 = MagicGame("broken");
        CastSpell(m3, "mend");
        m3.ResetMagicTargets();
        var afterReset = CastSpell(m3, "stonecut");
        Check("R6 reset: ResetMagicTargets drops the half-done work", afterReset.ReactionId == "react.stonecut_broken" && TagsOf(m3) == "broken,shaped");
        var moved = MagicGame("wet");
        CastSpell(moved, "frost");
        moved.ResetMagicTargets();
        Check("reset: every target is back to its starting tags", TagsOf(moved) == "wet");
    }

    static void Situations()
    {
        var g = MagicGame("wet");
        for (var i = 0; i < 3; i++) CastSpell(g, "ripple");
        Check("situations: casting in the same context again adds nothing", g.State.SituationLog["spell.ripple"].SequenceEqual(new[] { "ripple.wet" }) && !g.Magic.AppliedUnlocked("spell.ripple"));

        var s = MagicGame("x1", "x2", "x3");
        s.Content.Reactions.Insert(0, Row("s3", ReactionKind.React, "spell.lantern", new[] { "x3" }, new[] { "x3" }, "ctx.three"));
        s.Content.Reactions.Insert(0, Row("s2", ReactionKind.React, "spell.lantern", new[] { "x2" }, new[] { "x2" }, "ctx.two"));
        s.Content.Reactions.Insert(0, Row("s1", ReactionKind.React, "spell.lantern", new[] { "x1" }, new[] { "x1" }, "ctx.one"));
        CastSpell(s, "lantern");
        CastSpell(s, "lantern");
        s.ResetMagicTargets();
        CastSpell(s, "lantern");
        Check("situations: a repeated context after a reset still counts once (2 distinct)", s.Magic.ContextCount("spell.lantern") == 2 && !s.Magic.AppliedUnlocked("spell.lantern"));
        CastSpell(s, "lantern");
        CastSpell(s, "lantern");
        Check("situations: three distinct contexts unlock the applied use", s.Magic.ContextCount("spell.lantern") == 3 && s.Magic.AppliedUnlocked("spell.lantern"));
    }

    static void RejectedCastsChangeNothing()
    {
        void Case(string name, Game g, string spell, CastFailure expected, Action before = null)
        {
            before?.Invoke();
            var snapshot = Snapshot(g);
            var r = g.TryCastSpell(spell);
            Check("rejected cast (" + name + "): " + expected + ", no state, no unlock, no log", !r.Ok && r.Failure == expected && Snapshot(g) == snapshot && !g.State.QuickCastUnlocked && g.State.SituationLog.Count == 0);
        }
        var notNow = MagicGame("dark");
        notNow.StartEvent("event.snack");
        Case("event running", notNow, "spell.lantern", CastFailure.NotCastableNow);
        Case("unknown id", MagicGame("dark"), "spell.nope", CastFailure.Unknown);
        var unlearned = MagicGame("dark");
        unlearned.State.KnownSpells.Remove("spell.lantern");
        Case("not learned", unlearned, "spell.lantern", CastFailure.NotLearned);
        var needs = MagicGame("dark");
        needs.Content.Spell("spell.lantern").Requirements.Add(new RequirementDef { Kind = "itemCountAtLeast", Id = "item.lens", Label = "실습용 렌즈", Min = 1 });
        needs.Content.Spell("spell.quickcast").Requirements.Add(new RequirementDef { Kind = "statAtLeast", Id = "stat.focus", Label = "집중", Min = 3 });
        needs.RequirementProvider = r => 0;
        Case("requirement", needs, "spell.lantern", CastFailure.Requirement);
        Case("requirement on the unlock spell", needs, "spell.quickcast", CastFailure.Requirement);
        var away = MagicGame("dark");
        away.Player.Facing = "south";
        Case("no target (behind the player)", away, "spell.lantern", CastFailure.NoTarget);
        Case("protected", MagicGame("person", "dark"), "spell.lantern", CastFailure.Protected);

        var hidden = MagicGame("dark");
        hidden.State.KnownSpells.Remove("spell.lantern");
        var message = hidden.TryCastSpell("spell.lantern").Message;
        Check("unlearned spell names stay hidden in the message", !message.Contains("등불") && message.Contains("배우지 않은"));
    }

    static void RequirementThresholds()
    {
        var have = new Dictionary<string, int>();
        Game With(RequirementDef req)
        {
            var g = MagicGame("dark");
            g.Content.Spell("spell.lantern").Requirements.Add(req);
            g.RequirementProvider = r => have.TryGetValue(r.Id, out var v) ? v : (int?)null;
            return g;
        }
        var item = new RequirementDef { Kind = "itemCountAtLeast", Id = "item.lens", Label = "실습용 렌즈", Min = 1 };
        have["item.lens"] = 1;
        Check("requirement item: exactly the minimum passes", CastSpell(With(item), "lantern").Ok);
        have["item.lens"] = 0;
        var low = CastSpell(With(item), "lantern");
        Check("requirement item: one below fails with the item message", low.Failure == CastFailure.Requirement && low.Unmet.Single().Text == "실습용 렌즈 1개 필요 · 가방 0개" && low.Message.Contains("실습용 렌즈 1개 필요 · 가방 0개"));
        var stat = new RequirementDef { Kind = "statAtLeast", Id = "stat.focus", Label = "집중", Min = 3 };
        have["stat.focus"] = 3;
        Check("requirement stat: exactly the minimum passes", CastSpell(With(stat), "lantern").Ok);
        have["stat.focus"] = 2;
        Check("requirement stat: one below fails with the stat message", CastSpell(With(stat), "lantern").Unmet.Single().Text == "집중 스탯 3 필요 · 현재 2");
        var level = new RequirementDef { Kind = "levelAtLeast", Id = "level.practice", Label = "실습", Min = 2 };
        have["level.practice"] = 2;
        Check("requirement level: exactly the minimum passes", CastSpell(With(level), "lantern").Ok);
        have["level.practice"] = 1;
        Check("requirement level: one below fails with the level message", CastSpell(With(level), "lantern").Unmet.Single().Text == "실습 레벨 2 필요 · 현재 1");

        have["item.lens"] = 1;
        have["stat.focus"] = 0;
        var both = With(item);
        both.Content.Spell("spell.lantern").Requirements.Add(stat);
        Check("requirement: every unmet condition is listed", CastSpell(both, "lantern").Unmet.Select(u => u.Def.Id).SequenceEqual(new[] { "stat.focus" }));

        var unwired = MagicGame("dark");
        unwired.Content.Spell("spell.lantern").Requirements.Add(item);
        var unsupported = CastSpell(unwired, "lantern");
        Check("requirement: a kind nothing provides fails instead of passing", unsupported.Failure == CastFailure.Requirement && unsupported.Unmet.Single().Have == null && unsupported.Message.Contains("확인할 수 없습니다"));

        have["item.lens"] = 1;
        var mid = With(item);
        Type(mid, 1, 2);
        have["item.lens"] = 0;
        var before = Snapshot(mid);
        var step = mid.Magic.Confirm();
        Check("requirement: an item lost during the incantation fails at confirm and closes it",
            step == IncantationStep.Cast && mid.Magic.Last.Failure == CastFailure.Requirement && !mid.Magic.Open && Snapshot(mid) == before && mid.Magic.Last.Message.Contains("가방 0개"));
        have["item.lens"] = 1;
        Type(mid, 1, 2);
        mid.Magic.Confirm();
        Check("requirement: the item is not consumed and the spell works again once it is back", mid.Magic.Last.Ok && TagsOf(mid) == "lit");
    }

    static void IncantationRules()
    {
        var g = MagicGame("dark");
        Check("incantation: runes are ignored while closed", g.Magic.AddRune(1) == IncantationStep.Ignored && g.Magic.Runes.Count == 0 && g.Magic.Confirm() == IncantationStep.Ignored);
        Check("incantation: Begin opens an empty line", g.Magic.Begin() == IncantationStep.Opened && g.Magic.Open && g.Magic.Runes.Count == 0 && g.Magic.Begin() == IncantationStep.Ignored);
        Check("incantation: a rune outside 1-4 is ignored", g.Magic.AddRune(5) == IncantationStep.Ignored && g.Magic.AddRune(0) == IncantationStep.Ignored);
        Check("incantation: empty Confirm keeps it open", g.Magic.Confirm() == IncantationStep.Incomplete && g.Magic.Open && g.Magic.Casts == 0);
        g.Magic.AddRune(1);
        Check("incantation: a prefix of a longer spell cannot be confirmed yet", g.Magic.Exact == null && g.Magic.Confirm() == IncantationStep.Incomplete && g.Magic.Open && g.Magic.Runes.Count == 1 && g.Magic.Casts == 0);
        Check("incantation: a rune no spell continues clears the line and stays open", g.Magic.AddRune(1) == IncantationStep.Cleared && g.Magic.Open && g.Magic.Runes.Count == 0 && g.Magic.Casts == 0);
        g.Magic.AddRune(1);
        g.Magic.AddRune(2);
        Check("incantation: 1,2 is exactly the lantern", g.Magic.Exact.Id == "spell.lantern");
        Check("incantation: Confirm casts once, closes and empties", g.Magic.Confirm() == IncantationStep.Cast && g.Magic.Last.Ok && g.Magic.Casts == 1 && !g.Magic.Open && g.Magic.Runes.Count == 0 && TagsOf(g) == "lit");

        var longer = MagicGame("liftable");
        Type(longer, 1, 2, 3);
        longer.Magic.Confirm();
        Check("incantation: the longer spell sharing the prefix is chosen by its full array", longer.Magic.Last.SpellId == "spell.levitate" && TagsOf(longer) == "floating,liftable");

        var order = MagicGame();
        Type(order, 2, 1);
        order.Magic.Confirm();
        Check("incantation: order matters, 2,1 is ripple and not lantern", order.Magic.Last.SpellId == "spell.ripple");
        var twice = MagicGame("seed_dry");
        Type(twice, 3, 3);
        Check("incantation: the same rune twice in a row is allowed", twice.Magic.Runes.SequenceEqual(new[] { 3, 3 }) && twice.Magic.Exact.Id == "spell.ember");

        var over = MagicGame();
        Type(over, 1, 2, 3);
        Check("incantation: a sixth rune clears the line", over.Magic.AddRune(1) == IncantationStep.Cleared && over.Magic.Runes.Count == 0 && over.Magic.Open);
        Type(over, 3, 2, 1);
        Check("incantation: five runes fit when a spell is that long", MaxLength(over) <= Magic.MaxRunes);

        var unlearned = MagicGame("seed_dry");
        unlearned.State.KnownSpells.Remove("spell.ember");
        Type(unlearned, 3, 3);
        unlearned.Magic.Confirm();
        Check("incantation: an unlearned exact match is rejected, closes, and hides the name",
            unlearned.Magic.Last.Failure == CastFailure.NotLearned && !unlearned.Magic.Open && !unlearned.Magic.Last.Message.Contains("불씨"));

        var cancel = MagicGame();
        Type(cancel, 1);
        Check("incantation: cancel closes and empties", cancel.Magic.Cancel() == IncantationStep.Closed && !cancel.Magic.Open && cancel.Magic.Runes.Count == 0 && cancel.Magic.Cancel() == IncantationStep.Ignored);

        var evt = MagicGame("dark");
        Type(evt, 1);
        evt.StartEvent("event.snack");
        Check("incantation: starting an event closes it, and none opens during the event", !evt.Magic.Open && evt.Magic.Runes.Count == 0 && evt.Magic.Begin() == IncantationStep.Ignored);
    }

    static int MaxLength(Game g) => g.Content.Spells.Max(s => s.Runes.Length);

    static void IncantationResets()
    {
        var portal = NewGame(SpellStart);
        World.Place(portal.Player, new Spot { MapId = "map.courtyard", X = 6.5, Y = 2.5 });
        Type(portal, 1);
        portal.SetPlayerInput(1, 0);
        Run(portal, 1, () => portal.MapId == "map.lounge");
        Check("incantation: passing a portal clears it", portal.MapId == "map.lounge" && !portal.Magic.Open && portal.Magic.Runes.Count == 0);

        var day = MagicGame("dark");
        CastSpell(day, "lantern");
        Type(day, 1);
        day.EndDay(DayEnd.Slept);
        Check("incantation: ending the day clears it and resets the targets", !day.Magic.Open && day.Magic.Runes.Count == 0 && TagsOf(day) == "dark");

        var night = MagicGame("dark");
        CastSpell(night, "lantern");
        Type(night, 1);
        night.State.Minute = Clock.PassOutAt - 1;
        Run(night, 2);
        Check("incantation: passing out at 02:00 clears it too", night.State.Day == 1 && !night.Magic.Open && TagsOf(night) == "dark");

        var refused = MagicGame("dark");
        refused.StartEvent("event.snack");
        Check("day end refused during an event leaves the targets alone", !refused.EndDay(DayEnd.Slept) && TagsOf(refused) == "dark");

        var loaded = MagicGame("dark");
        CastSpell(loaded, "lantern");
        Type(loaded, 1);
        var reopened = new Game(Content.Load(Base), SaveFile.Read(Path.Combine(Base, SpellStart)), loaded.SavePath);
        Check("incantation and target tags are not carried into a new Game (load)", !reopened.Magic.Open && reopened.Magic.Tags("target.signboard").SetEquals(new[] { "dark" }));

        var paused = MagicGame("wet");
        CastSpell(paused, "frost");
        var clock = paused.MagicSeconds;
        paused.StartEvent("event.snack");
        Run(paused, 1);
        Check("tag windows count world seconds, which an event holds", paused.MagicSeconds == clock);
    }

    static void Aiming()
    {
        var g = NewGame(SpellStart);
        g.Content.MagicTargets.Clear();
        MagicTargetDef Put(string id, double x, double y, string map = "map.courtyard")
        {
            var t = new MagicTargetDef { Id = id, Name = id, MapId = map, X = x, Y = y };
            g.Content.MagicTargets.Add(t);
            return t;
        }
        g.Player.Facing = "north";
        var ahead = Put("ahead", 1.5, 3.0);
        Put("beside", 2.0, 1.5);
        Put("behind", 1.5, 0.5);
        Put("far", 1.5, 5.4);
        Put("elsewhere", 1.5, 2.0, "map.lounge");
        Check("aim: the target in front beats a closer one at the side, and ignores behind, far and other maps", g.Magic.Aim(3) == ahead);
        Check("aim: a shorter range drops the target out of reach", g.Magic.Aim(1.2)?.Id == "beside");
        g.Player.Facing = "south";
        Check("aim: turning around picks the one behind", g.Magic.Aim(3).Id == "behind");
        g.Player.Facing = "west";
        Check("aim: with only the side target at 90 degrees it still counts, nothing in front means none", g.Magic.Aim(0.4) == null);
        g.Content.MagicTargets.Clear();
        Check("aim: no targets means none", g.Magic.Aim(3) == null);
    }

    static void QuickCast()
    {
        var g = MagicGame("dark");
        Check("quick: slots are inert before the unlock", g.Magic.CastSlot(0) == SlotOutcome.Locked && !g.Magic.SetSlot(0, "spell.lantern") && g.Magic.Casts == 0);
        g.Player.Facing = "south";
        var unlock = CastSpell(g, "quickcast");
        Check("quick: casting the unlock spell on self needs no target and opens the slots", unlock.Ok && unlock.UnlockedQuickCast && g.State.QuickCastUnlocked && g.State.QuickSlots.All(s => s == null));
        g.Player.Facing = "north";
        Check("quick: assign, replace and clear a slot", g.Magic.SetSlot(0, "spell.lantern") && g.State.QuickSlots[0] == "spell.lantern"
            && g.Magic.SetSlot(0, "spell.ripple") && g.State.QuickSlots[0] == "spell.ripple" && g.Magic.SetSlot(0, "spell.lantern") && g.Magic.SetSlot(1, "spell.ripple") && g.Magic.SetSlot(1, null) && g.State.QuickSlots[1] == null);
        Check("quick: the unlock spell, unlearned, unknown spells and bad slots are refused",
            !g.Magic.SetSlot(2, "spell.quickcast") && !g.Magic.SetSlot(2, "spell.nope") && !g.Magic.SetSlot(4, "spell.lantern") && !g.Magic.SetSlot(-1, "spell.lantern"));
        g.State.KnownSpells.Remove("spell.ember");
        Check("quick: an unlearned spell cannot be placed", !g.Magic.SetSlot(2, "spell.ember"));
        var again = CastSpell(g, "quickcast");
        Check("quick: casting the unlock spell again changes nothing", again.Ok && !again.UnlockedQuickCast && g.State.QuickSlots[0] == "spell.lantern");
        Check("quick: an empty slot reports empty without casting", g.Magic.CastSlot(3) == SlotOutcome.Empty && g.Magic.CastSlot(9) == SlotOutcome.Locked);

        var viaSlot = MagicGame("dark");
        viaSlot.State.QuickCastUnlocked = true;
        viaSlot.Magic.SetSlot(0, "spell.lantern");
        var casts = viaSlot.Magic.Casts;
        Check("quick: a slot casts the same rule", viaSlot.Magic.CastSlot(0) == SlotOutcome.Cast && viaSlot.Magic.Casts == casts + 1 && TagsOf(viaSlot) == "lit");
        var viaRunes = MagicGame("dark");
        Type(viaRunes, 1, 2);
        viaRunes.Magic.Confirm();
        Check("quick: incantation and slot give the same reaction, message and state",
            viaRunes.Magic.Last.ReactionId == viaSlot.Magic.Last.ReactionId && viaRunes.Magic.Last.Message == viaSlot.Magic.Last.Message && TagsOf(viaRunes) == TagsOf(viaSlot));

        viaSlot.ResetMagicTargets();
        viaSlot.State.Minute = Clock.DayStart;
        viaSlot.Player.Facing = "south";
        viaSlot.Magic.CastSlot(0);
        Check("quick: a slot cast with no target fails the same way as an incantation", viaSlot.Magic.Last.Failure == CastFailure.NoTarget);

        var busy = MagicGame("dark");
        busy.State.QuickCastUnlocked = true;
        busy.StartEvent("event.snack");
        Check("quick: slots cannot be edited and spells cannot be cast during an event", !busy.Magic.SetSlot(0, "spell.lantern") && busy.Magic.CastSlot(0) == SlotOutcome.Empty);
        busy.State.QuickSlots[0] = "spell.lantern";
        busy.Magic.CastSlot(0);
        Check("quick: a filled slot during an event is rejected as not castable now", busy.Magic.Last.Failure == CastFailure.NotCastableNow);
    }

    static void MagicSaves()
    {
        var g = NewGame(SpellStart);
        g.Player.Facing = "north";
        CastSpell(g, "quickcast");
        g.Magic.SetSlot(2, "spell.ripple");
        var lit = CastSpell(g, "lantern");
        Check("save: the real signboard is lit from the start position facing it", lit.Ok && lit.TargetId == "target.signboard" && g.Magic.Tags("target.signboard").SetEquals(new[] { "lit" }));
        g.EndDay(DayEnd.Slept);
        var reloaded = Game.Open(Base, Path.Combine(Base, SpellStart), g.SavePath);
        var s = reloaded.State;
        Check("save: known spells, the unlock, the slots and the situation log round-trip through EndDay",
            s.KnownSpells.SequenceEqual(g.State.KnownSpells) && s.QuickCastUnlocked && s.QuickSlots.SequenceEqual(new[] { null, null, "spell.ripple", null })
            && s.SituationLog["spell.lantern"].SequenceEqual(new[] { "lantern.dark" }) && SaveFile.ToJson(s) == SaveFile.ToJson(g.State));
        Check("save: loading resets the targets and the incantation", reloaded.Magic.Tags("target.signboard").SetEquals(new[] { "dark" }) && !reloaded.Magic.Open);
        Check("save: the loaded slot casts", reloaded.Magic.CastSlot(2) == SlotOutcome.Cast && reloaded.Magic.Last.SpellId == "spell.ripple");

        var old = NewGame("start-offered.json");
        Check("old save: no spell fields means defaults, nothing learned, four empty slots",
            old.State.KnownSpells.Count == 0 && !old.State.QuickCastUnlocked && old.State.QuickSlots.Length == 4 && old.State.QuickSlots.All(x => x == null) && old.State.SituationLog.Count == 0
            && CastSpell(old, "lantern").Failure == CastFailure.NotLearned);

        const string head = "{\"day\":0,\"minute\":420,\"player\":{\"id\":\"player\",\"mapId\":\"map.courtyard\",\"x\":1.5,\"y\":1.5,\"facing\":\"south\"},";
        Game FromJson(string tail) => new Game(Content.Load(Base), SaveFile.FromJson(head + tail), Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        var nulls = FromJson("\"knownSpells\":null,\"quickSlots\":null,\"situationLog\":null}").State;
        Check("old save: explicit nulls normalize to defaults", nulls.KnownSpells.Count == 0 && nulls.QuickSlots.Length == 4 && nulls.SituationLog.Count == 0);
        var stale = FromJson("\"knownSpells\":[\"spell.lantern\",\"spell.lantern\"],\"quickCastUnlocked\":true,\"quickSlots\":[\"spell.lantern\",\"spell.quickcast\",\"spell.gone\",null,\"spell.ripple\"],\"situationLog\":{\"spell.lantern\":null}}").State;
        Check("old save: slots are cut to four and drop a gone spell and one that cannot be quick cast; learned ids are deduplicated; a null log list becomes empty",
            stale.QuickSlots.SequenceEqual(new[] { "spell.lantern", null, null, null }) && stale.KnownSpells.SequenceEqual(new[] { "spell.lantern" }) && stale.SituationLog["spell.lantern"].Count == 0);
        var locked = FromJson("\"knownSpells\":[\"spell.lantern\"],\"quickCastUnlocked\":false,\"quickSlots\":[\"spell.lantern\",null]}").State;
        Check("old save: slots in a save that never unlocked quick cast are emptied and padded to four", locked.QuickSlots.Length == 4 && locked.QuickSlots.All(x => x == null));
        var variant = new Game(Content.Load(Path.Combine(Fc, "variants", "a10-mira")), SaveFile.FromJson(head + "\"knownSpells\":[\"spell.lantern\"],\"quickCastUnlocked\":true,\"quickSlots\":[\"spell.lantern\"]}"), Path.Combine(Temp, "variant", "save.json"));
        Check("a bundle without spells keeps learned ids but empties the slots", variant.State.KnownSpells.Contains("spell.lantern") && variant.State.QuickSlots.All(x => x == null));
    }
}
