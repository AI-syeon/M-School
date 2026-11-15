using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;

static partial class Program
{
    // ------------------------------------------------------------------ helpers

    static QuestDef QDef(string id, int day, string[] checks, int crowns = 0, params string[] itemCounts)
    {
        return new QuestDef
        {
            Id = id, Title = "의뢰 " + id, Description = "시험용", AvailableFromDay = day,
            Steps = checks.Select((c, i) => new QuestStepDef { Check = c, Label = "단계 " + (i + 1) }).ToList(),
            Reward = new QuestRewardDef { Crowns = crowns, Items = itemCounts.Select(s => new QuestItemReward { ItemId = s.Split('x')[0], Count = int.Parse(s.Split('x')[1]) }).ToList() }
        };
    }

    static Game QuestGame(string fixture, params QuestDef[] quests)
    {
        var g = NewGame(fixture);
        g.Content.Quests.Clear();
        g.Content.Quests.AddRange(quests);
        return g;
    }

    static void Stand(Game g, string map, double x, double y, string facing)
    {
        g.Player.MapId = map;
        g.Player.X = x;
        g.Player.Y = y;
        g.Player.Facing = facing;
    }

    static string Ids(IEnumerable<QuestProgress> list) => string.Join(",", list.Select(q => q.Def.Id));

    static string Kinds(IEnumerable<Notice> list) => string.Join(",", list.Select(n => n.Kind));

    // ------------------------------------------------------------------ tests

    static void QuestTests()
    {
        QuestRules();
        ShippedQuestsPlay();
        QuestLatching();
        QuestRewardOnce();
        QuestSaves();
        NoQuestsFile();
        QuestNotices();
        QuestDayReport();
        QuestCheckKinds();
    }

    static void QuestRules()
    {
        var shipped = Content.Load(Base);
        Check("shipped quests.json holds 3 quests that pass every rule", shipped.Quests.Count == 3 && Quests.Problems(shipped).Count == 0);
        Check("shipped quests: ids are the three samples",
            shipped.Quests.Select(q => q.Id).SequenceEqual(new[] { "quest.first_light", "quest.lounge_snack", "quest.dry_seedpot" }));
        var generated = Path.Combine(FindRoot(), "Content", "Fixtures", "FirstConnection", "generated", "base", "quests.json");
        Check("quests.json in generated/ matches the StreamingAssets copy", File.Exists(generated) && File.ReadAllText(generated) == File.ReadAllText(Path.Combine(Base, "quests.json")));

        List<string> ProblemsOf(params QuestDef[] quests)
        {
            var c = Content.Load(Base);
            c.Quests = quests.ToList();
            return Quests.Problems(c);
        }
        foreach (var good in new[] { "tag target.signboard lit", "tag target.seedpot husk_open", "visited map.lounge", "item item.lens 1", "item item.student_card 1", "done event.snack", "cast spell.lantern" })
            Check("quest check '" + good + "' is well formed", ProblemsOf(QDef("q", 0, new[] { good })).Count == 0);
        var bad = new[]
        {
            "hold target.signboard", "tag target.signboard", "tag target.signboard lit extra", "tag target.nope lit", "tag target.signboard never_added",
            "visited", "visited map.nowhere", "item item.lens", "item item.lens 0", "item item.lens x", "item item.nope 1", "done event.nope", "cast spell.nope",
            "!visited map.lounge", "visited map.lounge/done event.snack", "", null
        };
        foreach (var check in bad)
            Check("quest check '" + check + "' is reported", ProblemsOf(QDef("q", 0, new[] { check })).Count > 0);
        Check("quest rules: duplicate ids", ProblemsOf(QDef("q", 0, new[] { "visited map.lounge" }), QDef("q", 0, new[] { "visited map.lounge" })).Count > 0);
        Check("quest rules: no steps", ProblemsOf(QDef("q", 0, new string[0])).Count > 0);
        Check("quest rules: empty step label", ProblemsOf(new QuestDef { Id = "q", Title = "t", Steps = { new QuestStepDef { Check = "visited map.lounge", Label = " " } } }).Count > 0);
        Check("quest rules: no title", ProblemsOf(new QuestDef { Id = "q", Steps = { new QuestStepDef { Check = "visited map.lounge", Label = "l" } } }).Count > 0);
        Check("quest rules: unknown giver", ProblemsOf(new QuestDef { Id = "q", Title = "t", Giver = "npc.nobody", Steps = { new QuestStepDef { Check = "visited map.lounge", Label = "l" } } }).Count > 0);
        Check("quest rules: negative day", ProblemsOf(QDef("q", -1, new[] { "visited map.lounge" })).Count > 0);
        Check("quest rules: unknown reward item", ProblemsOf(QDef("q", 0, new[] { "visited map.lounge" }, 0, "item.nope" + "x1")).Count > 0);
        Check("quest rules: zero reward count", ProblemsOf(QDef("q", 0, new[] { "visited map.lounge" }, 0, "item.lensx0")).Count > 0);
        Check("quest rules: negative crowns", ProblemsOf(QDef("q", 0, new[] { "visited map.lounge" }, -5)).Count > 0);

        var g = NewGame("start-offered.json");
        Check("a check that does not parse never holds and never throws at run time",
            !g.Quests.Holds("hold x") && !g.Quests.Holds("") && !g.Quests.Holds(null));
        var broken = QuestGame("start-offered.json", QDef("q", 0, new[] { "hold x" }, 9));
        broken.Update(1 / 30.0);
        Check("a quest with a bad check stays open at run time", broken.Quests.Active.Count == 1 && broken.State.Wallet == 200);
    }

    static void ShippedQuestsPlay()
    {
        var g = NewGame(SpellStart, withQuests: true);
        Check("day 0 lists the two day-0 quests, not the day-1 one", Ids(g.Quests.Active) == "quest.first_light,quest.lounge_snack");
        Check("a fresh game has no notices", g.Notices.Count == 0 && g.LastDayReport == null);
        g.Player.Facing = "north";
        Type(g, 1, 2);
        g.Magic.Confirm();
        Check("incantation cast of the lantern lit the real signboard", g.Magic.Last.Ok && g.Magic.Last.TargetId == "target.signboard");
        g.Update(1 / 30.0);
        Check("first_light: the cast is seen on the next frame and the reward arrives once",
            g.Quests.IsComplete("quest.first_light") && g.State.Wallet == 210 && Ids(g.Quests.Active) == "quest.lounge_snack");
        var notes = g.DrainNotices();
        Check("first_light: notices are the step then the completion, in content words",
            Kinds(notes) == "QuestStep,QuestDone" && notes[0].Text == "안뜰 안내판에 불 켜기 · 등불 주문으로 안내판에 불 켜기" && notes[1].Text == "의뢰 완료 · 안뜰 안내판에 불 켜기 · 10 크라운"
            && notes.All(n => n.QuestId == "quest.first_light"));
        Check("the lantern is recorded as cast", g.State.SpellsCast.SequenceEqual(new[] { "spell.lantern" }));

        var s = NewGame("start-accepted.json", withQuests: true);
        s.StartEvent("event.snack");
        Play(s, 0);
        Check("lounge_snack: the lounge visit and the finished event complete it, paid on top of the event",
            s.Quests.IsComplete("quest.lounge_snack") && s.State.Wallet == 200 + 20 + 15);
        Check("lounge_snack: notices run lounge step, event step, completion",
            Kinds(s.DrainNotices()) == "QuestStep,QuestStep,QuestDone" && !s.Quests.IsComplete("quest.first_light"));

        var d = NewGame(SpellStart, withQuests: true);
        Stand(d, "map.courtyard", 5.5, 6.5, "north");
        d.TryCastSpell("spell.sprout");
        Check("dry_seedpot: not available on day 0, so the cast counts for nothing", d.Quests.Active.All(q => q.Def.Id != "quest.dry_seedpot") && !d.State.QuestSteps.ContainsKey("quest.dry_seedpot"));
        d.EndDay(DayEnd.Slept);
        Check("dry_seedpot: day 1 opens it", Ids(d.Quests.Active).Contains("quest.dry_seedpot"));
        d.DrainNotices();
        Stand(d, "map.courtyard", 5.5, 6.5, "north");
        d.TryCastSpell("spell.sprout");
        var progress = d.Quests.Active.First(q => q.Def.Id == "quest.dry_seedpot");
        Check("dry_seedpot: sprout wakes the seed, one step of two", progress.DoneCount == 1 && progress.StepDone[0] && !progress.StepDone[1]);
        d.TryCastSpell("spell.ember");
        Check("dry_seedpot: ember opens the husk and finishes it with crowns and crystal dust",
            d.Quests.IsComplete("quest.dry_seedpot") && d.State.Wallet == 225 && d.Inventory.Count("item.crystal_dust") == 2);
        var seedNotes = d.DrainNotices();
        Check("dry_seedpot: notices run step, step, completion, item",
            Kinds(seedNotes) == "QuestStep,QuestStep,QuestDone,ItemReceived" && seedNotes[3].ItemId == "item.crystal_dust" && seedNotes[3].Text == "수정 가루 2개");
    }

    static void QuestLatching()
    {
        var two = QDef("q.latch", 0, new[] { "tag target.signboard lit", "visited map.lounge" }, 12);
        var g = QuestGame(SpellStart, two);
        g.Player.Facing = "north";
        g.TryCastSpell("spell.lantern");
        Check("latch: the first step is done, the quest is not", g.Quests.StepDone("q.latch", 0) && !g.Quests.IsComplete("q.latch"));
        g.EndDay(DayEnd.Slept);
        Check("latch: the day reset put the signboard back to dark", g.Magic.Tags("target.signboard").SetEquals(new[] { "dark" }) && !g.Quests.Holds("tag target.signboard lit"));
        Check("latch: the step stays done across the reset", g.Quests.StepDone("q.latch", 0) && !g.Quests.StepDone("q.latch", 1));
        var reloaded = Game.Open(Base, Path.Combine(Base, SpellStart), g.SavePath);
        reloaded.Content.Quests.Clear();
        reloaded.Content.Quests.Add(two);
        Check("latch: the step also survives a save and load", reloaded.Quests.StepDone("q.latch", 0));
        Stand(reloaded, "map.lounge", 0.5, 2.5, "south");
        reloaded.Update(1 / 30.0);
        Check("latch: visiting the lounge on a later day completes the quest", reloaded.Quests.IsComplete("q.latch") && reloaded.State.Wallet == 212);
    }

    static void QuestRewardOnce()
    {
        var q = QDef("q.reward", 0, new[] { "item item.lens 1" }, 7, "item.crystal_dustx3");
        var g = QuestGame("start-items.json", q);
        FillBag(g);
        var mailbox0 = g.State.Mailbox.Where(l => l.ItemId == "item.crystal_dust").Sum(l => l.Count);
        var dust0 = g.Inventory.Count("item.crystal_dust");
        var wallet0 = g.State.Wallet;
        g.Update(1 / 30.0);
        Check("reward: crowns paid and the full bag sent the items to the mailbox",
            g.State.Wallet == wallet0 + 7 && g.State.Mailbox.Where(l => l.ItemId == "item.crystal_dust").Sum(l => l.Count) == mailbox0 + 3 && g.Inventory.Count("item.crystal_dust") == dust0);
        var notes = g.DrainNotices();
        Check("reward: the item notice says how many went to the mailbox", notes.Count == 3 && notes[2].Kind == NoticeKind.ItemReceived && notes[2].Text == "수정 가루 3개 · 3개는 우편함으로");
        for (var i = 0; i < 5; i++) g.Update(1 / 30.0);
        g.EndDay(DayEnd.Slept);
        g.EndDay(DayEnd.Slept);
        g.Update(1 / 30.0);
        Check("reward: later frames and days give nothing more", g.State.Wallet == wallet0 + 7 && g.State.Mailbox.Where(l => l.ItemId == "item.crystal_dust").Sum(l => l.Count) == mailbox0 + 3 && g.Notices.Count == 0);
        var reloaded = Game.Open(Base, Path.Combine(Base, "start-items.json"), g.SavePath);
        reloaded.Content.Quests.Add(q);
        reloaded.Update(1 / 30.0);
        Check("reward: a loaded game does not give it again", reloaded.State.Wallet == wallet0 + 7 && reloaded.Notices.Count == 0 && reloaded.Quests.IsComplete("q.reward"));

        var roomy = QuestGame("start-items.json", QDef("q.room", 0, new[] { "visited map.courtyard" }, 0, "item.honey_breadx2"));
        var before = roomy.Inventory.Count("item.honey_bread");
        roomy.Update(1 / 30.0);
        Check("reward: with room in the bag the items go to the bag", roomy.Inventory.Count("item.honey_bread") == before + 2 && roomy.DrainNotices().Last().Text == "꿀빵 2개");
    }

    static void QuestSaves()
    {
        var old = NewGame("start-offered.json");
        Check("old save: no quest fields means no progress", old.State.QuestSteps.Count == 0 && old.State.CompletedQuests.Count == 0 && old.State.SpellsCast.Count == 0);
        const string head = "{\"day\":0,\"minute\":420,\"player\":{\"id\":\"player\",\"mapId\":\"map.courtyard\",\"x\":1.5,\"y\":1.5,\"facing\":\"south\"},";
        Game FromJson(string tail) => new Game(Content.Load(Base), SaveFile.FromJson(head + tail), Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        var nulls = FromJson("\"questSteps\":null,\"completedQuests\":null,\"spellsCast\":null}").State;
        Check("old save: explicit nulls normalize to empty", nulls.QuestSteps.Count == 0 && nulls.CompletedQuests.Count == 0 && nulls.SpellsCast.Count == 0);
        var messy = FromJson("\"questSteps\":{\"quest.first_light\":null,\"\":[1],\"quest.lounge_snack\":[0,0,-1,1]},\"completedQuests\":[\"quest.x\",\"quest.x\",\"\"],\"spellsCast\":[\"spell.lantern\",\"spell.lantern\",null]}").State;
        Check("old save: nulls, repeats and negative indices are cleaned, unknown ids are kept",
            messy.QuestSteps["quest.first_light"].Count == 0 && !messy.QuestSteps.ContainsKey("") && messy.QuestSteps["quest.lounge_snack"].SequenceEqual(new[] { 0, 1 })
            && messy.CompletedQuests.SequenceEqual(new[] { "quest.x" }) && messy.SpellsCast.SequenceEqual(new[] { "spell.lantern" }));
        var written = new Game(Content.Load(Base), SaveFile.Read(Path.Combine(Base, "start-offered.json")), Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        Check("old save: the first save of a game with no quest progress writes the new fields",
            written.EndDay(DayEnd.Slept) && SaveFile.ToJson(SaveFile.Read(written.SavePath)).Contains("\"questSteps\": {}"));

        var g = NewGame(SpellStart, withQuests: true);
        g.Player.Facing = "north";
        g.TryCastSpell("spell.lantern");
        g.TryCastSpell("spell.ripple");
        g.EndDay(DayEnd.Slept);
        var reloaded = Game.Open(Base, Path.Combine(Base, SpellStart), g.SavePath);
        Check("save: completed quests, steps and spells cast round-trip through EndDay",
            reloaded.State.CompletedQuests.SequenceEqual(new[] { "quest.first_light" }) && reloaded.State.QuestSteps["quest.first_light"].SequenceEqual(new[] { 0 })
            && reloaded.State.SpellsCast.SequenceEqual(new[] { "spell.lantern", "spell.ripple" }) && SaveFile.ToJson(reloaded.State) == SaveFile.ToJson(g.State));
        reloaded.Update(1 / 30.0);
        Check("save: a finished quest is not given again after the load", reloaded.State.Wallet == g.State.Wallet && reloaded.Notices.Count == 0
            && Ids(reloaded.Quests.Active) == "quest.lounge_snack,quest.dry_seedpot");
    }

    static void NoQuestsFile()
    {
        var dir = Path.Combine(Temp, "no-quests");
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(Base, "runner-bundle.json"), Path.Combine(dir, "runner-bundle.json"));
        File.Copy(Path.Combine(Base, "staging-recipes.json"), Path.Combine(dir, "staging-recipes.json"));
        var content = Content.Load(dir);
        Check("no quests.json means no quests", content.Quests.Count == 0 && Quests.Problems(content).Count == 0);
        var g = new Game(content, SaveFile.Read(Path.Combine(Base, "start-spells.json")), Path.Combine(Temp, "no-quests-save", "save.json"));
        g.Player.Facing = "north";
        g.TryCastSpell("spell.lantern");
        Run(g, 2);
        g.EndDay(DayEnd.Slept);
        Check("a game without quests plays, casts and ends a day with nothing to report",
            g.Quests.Active.Count == 0 && g.Notices.Count == 0 && g.State.Wallet == 200 && g.LastDayReport.Quests.Count == 0 && g.LastDayReport.CrownsEarned == 0);
        var parsed = Content.Parse(File.ReadAllText(Path.Combine(Base, "runner-bundle.json")), File.ReadAllText(Path.Combine(Base, "staging-recipes.json")));
        Check("Content.Parse without a quests argument has none", parsed.Quests.Count == 0);
        Check("a variant without quests.json has none", Content.Load(Path.Combine(Fc, "variants", "a10-mira")).Quests.Count == 0);
    }

    static void QuestNotices()
    {
        var a = QDef("q.a", 0, new[] { "visited map.courtyard" }, 3, "item.crystal_dustx1");
        var b = QDef("q.b", 0, new[] { "visited map.courtyard", "item item.lens 1" }, 4);
        var g = QuestGame("start-items.json", a, b);
        Check("notices: nothing is announced before the first frame", g.Notices.Count == 0);
        g.Update(1 / 30.0);
        Check("notices: two quests finishing in one frame are announced in file order, step then done then item",
            Kinds(g.Notices) == "QuestStep,QuestDone,ItemReceived,QuestStep,QuestStep,QuestDone" && g.Notices.Select(n => n.QuestId).SequenceEqual(new[] { "q.a", "q.a", "q.a", "q.b", "q.b", "q.b" }));
        var drained = g.DrainNotices();
        Check("notices: texts are built from content names", drained[0].Text == "의뢰 q.a · 단계 1" && drained[1].Text == "의뢰 완료 · 의뢰 q.a · 3 크라운" && drained[2].Text == "수정 가루 1개"
            && drained[5].Text == "의뢰 완료 · 의뢰 q.b · 4 크라운");
        Check("notices: draining empties the list and a second drain is empty", g.Notices.Count == 0 && g.DrainNotices().Count == 0);
        g.Update(1 / 30.0);
        Check("notices: nothing repeats on later frames", g.Notices.Count == 0);
        var saved = SaveFile.ToJson(g.State);
        Check("notices: they are not part of the save", !saved.Contains("\"notices\""));
    }

    static void QuestDayReport()
    {
        var g = NewGame(SpellStart, withQuests: true);
        Check("day report: none before a day ends", g.LastDayReport == null);
        g.Player.Facing = "north";
        Type(g, 1, 2);
        g.Magic.Confirm();
        g.State.Wallet += 5;
        g.State.Minute = Clock.PassOutAt;
        g.Update(1 / 30.0);
        var report = g.LastDayReport;
        Check("day report: passing out in the same frame as a finished quest still counts that quest",
            g.State.Day == 1 && report.Day == 0 && report.How == DayEnd.PassedOut && report.Quests.Select(q => q.Id).SequenceEqual(new[] { "quest.first_light" }) && report.CrownsEarned == 15);
        g.EndDay(DayEnd.Slept);
        Check("day report: the next day starts from zero", g.LastDayReport.Day == 1 && g.LastDayReport.How == DayEnd.Slept && g.LastDayReport.Quests.Count == 0 && g.LastDayReport.CrownsEarned == 0);
        var s = NewGame("start-accepted.json", withQuests: true);
        s.StartEvent("event.snack");
        Play(s, 0);
        s.EndDay(DayEnd.Slept);
        Check("day report: event pay and quest pay add up", s.LastDayReport.Quests.Select(q => q.Id).SequenceEqual(new[] { "quest.lounge_snack" }) && s.LastDayReport.CrownsEarned == 35);
    }

    static void QuestCheckKinds()
    {
        var g = NewGame("start-items.json");
        Check("check visited: the start map holds, the lounge does not until the player stands there", g.Quests.Holds("visited map.courtyard") && !g.Quests.Holds("visited map.lounge"));
        Stand(g, "map.lounge", 0.5, 2.5, "south");
        g.Update(1 / 30.0);
        Check("check visited: standing in the lounge makes it hold", g.Quests.Holds("visited map.lounge"));
        Check("check item: bag count at least", g.Quests.Holds("item item.rune_chalk 3") && !g.Quests.Holds("item item.rune_chalk 4") && !g.Quests.Holds("item item.lens 2"));
        Check("check item: a key item in the notebook counts as one", g.Quests.Holds("item item.student_card 1") && !g.Quests.Holds("item item.student_card 2"));
        g.Inventory.Remove("item.rune_chalk", 1);
        Check("check item: using the item up makes it stop holding (the quest latches the step instead)", !g.Quests.Holds("item item.rune_chalk 3") && g.Quests.Holds("item item.rune_chalk 2"));
        Check("check done: only finished events", !g.Quests.Holds("done event.snack"));
        g.State.Completed.Add("event.snack");
        Check("check done: holds once the event is in the completed list", g.Quests.Holds("done event.snack") && !g.Quests.Holds("done event.snack_later"));

        var c = NewGame(SpellStart);
        Check("check cast: nothing cast yet", !c.Quests.Holds("cast spell.lantern"));
        Stand(c, "map.lounge", 0.5, 2.5, "north");
        var miss = c.TryCastSpell("spell.lantern");
        Check("check cast: a cast with no target is not a success", miss.Failure == CastFailure.NoTarget && !c.Quests.Holds("cast spell.lantern") && c.State.SpellsCast.Count == 0);
        var unlearned = NewGame("start-offered.json");
        unlearned.State.KnownSpells.Remove("spell.lantern");
        unlearned.TryCastSpell("spell.lantern");
        Check("check cast: a spell that was not learned is not a success", !unlearned.Quests.Holds("cast spell.lantern"));
        Stand(c, "map.courtyard", 5.5, 6.5, "north");
        c.TryCastSpell("spell.sprout");
        Check("check cast: a reaction-less success still counts", c.Quests.Holds("cast spell.sprout") && !c.Quests.Holds("cast spell.lantern"));
        c.Magic.CastSlot(0);
        c.TryCastSpell("spell.quickcast");
        Check("check cast: the quick-cast unlock spell counts too", c.Quests.Holds("cast spell.quickcast"));

        var later = QuestGame("start-offered.json", QDef("q.later", 2, new[] { "visited map.courtyard" }, 8));
        later.Update(1 / 30.0);
        Check("availability: a day-2 quest is not evaluated on day 0", later.Quests.Active.Count == 0 && !later.State.QuestSteps.ContainsKey("q.later") && later.State.Wallet == 200);
        later.EndDay(DayEnd.Slept);
        later.Update(1 / 30.0);
        Check("availability: still not on day 1", later.Quests.Active.Count == 0 && later.State.Wallet == 200);
        later.EndDay(DayEnd.Slept);
        Check("availability: it opens on day 2", Ids(later.Quests.Active) == "q.later");
        later.Update(1 / 30.0);
        Check("availability: and then completes", later.Quests.IsComplete("q.later") && later.State.Wallet == 208 && later.Quests.Completed.Select(q => q.Id).SequenceEqual(new[] { "q.later" }));
    }
}
