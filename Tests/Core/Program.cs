using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MagicCampus.Core;
using Newtonsoft.Json.Linq;

static class Program
{
    static int failures;
    static readonly string Fc = Path.Combine(FindRoot(), "Assets", "StreamingAssets", "FirstConnection");
    static readonly string Base = Path.Combine(Fc, "base");
    static readonly string Temp = Path.Combine(Path.GetTempPath(), "magiccampus-core-tests-" + Guid.NewGuid().ToString("N"));

    static int Main()
    {
        ContentRules();
        StartFixtures();
        InteractAndAccept();
        EventSuccess(0, "같이 하니까 마음이 놓여.");
        EventSuccess(1, "좋아, 따뜻한 차가 필요했어.");
        IntroFalseRefused();
        CancelRestores();
        SchedulePicks();
        PathAcrossMaps();
        PlayerMovement();
        DayEndSaves();
        SaveFallsBackToBak();
        Directory.Delete(Temp, true);
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failures++;
    }

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(dir.FullName, "Assets", "MagicCampus"))) dir = dir.Parent;
        return dir.FullName;
    }

    // ------------------------------------------------------------------ content rules (R3)

    static readonly string[] Commands = { "move", "say", "choice" };

    /// <summary>Every rule the runtime trusts without checking. Returns one line per broken rule.</summary>
    static List<string> Problems(Content c)
    {
        var p = new List<string>();
        void Rule(bool ok, string what) { if (!ok) p.Add(what); }
        bool Npc(string id) => c.Npcs.Any(n => n.Id == id);
        bool Dest(string id) => c.Destinations.Any(d => d.Id == id);
        bool Var(string id) => c.Variables.Any(v => v.Id == id);
        bool Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        bool Standable(Spot s, string what)
        {
            var map = c.Maps.FirstOrDefault(m => m.Id == s.MapId);
            Rule(map != null && World.Passable(map, (int)Math.Floor(s.X), (int)Math.Floor(s.Y)), what + " stands on a passable cell");
            return map != null;
        }
        bool Reachable(Spot from, Spot to) => Standable(from, "path start") && Standable(to, "path end")
            && World.FindPath(c, new Actor { MapId = from.MapId, X = from.X, Y = from.Y }, to) != null;

        foreach (var map in c.Maps)
            foreach (var portal in map.Portals)
                Standable(portal.To, portal.Id + " arrival");
        foreach (var d in c.Destinations) Standable(d.Position, d.Id);
        Rule(c.Bed != null && Standable(c.Bed, "bed"), "bed exists");
        foreach (var npc in c.Npcs)
        {
            Rule(Dest(npc.Home), npc.Id + " home exists");
            Rule(npc.Speed > 0, npc.Id + " speed > 0");
            Rule(c.Schedules.Count(s => s.NpcId == npc.Id) == 1, npc.Id + " has one schedule");
        }
        foreach (var s in c.Schedules)
        {
            Rule(Npc(s.NpcId), s.Id + " npc exists");
            var entries = Command.Parse(s.Script);
            var targets = new List<string>();
            foreach (var e in entries)
            {
                Rule(int.TryParse(e.Name, out var minute) && minute >= 0 && minute < Clock.MinutesPerDay, s.Id + " entry minute " + e.Name);
                Rule(e.Args.Count == 1 && (e.Args[0] == "home" || Dest(e.Args[0])), s.Id + " entry destination " + string.Join(" ", e.Args));
                if (e.Args.Count == 1 && Npc(s.NpcId)) targets.Add(e.Args[0] == "home" ? c.Npc(s.NpcId).Home : e.Args[0]);
            }
            foreach (var a in targets.Where(Dest))
                foreach (var b in targets.Where(Dest))
                    Rule(Reachable(c.Destination(a).Position, c.Destination(b).Position), s.Id + " walks " + a + " -> " + b);
        }
        foreach (var e in c.Events)
        {
            Rule(e.RepeatPolicy == "once", e.Id + " is once");
            Rule(e.Staging == "reserved", e.Id + " uses reserved staging");
            var recipe = c.Recipes.FirstOrDefault(r => r.EventId == e.Id);
            Rule(recipe != null, e.Id + " has a staging recipe");
            var joins = recipe?.Join ?? new List<JoinDef>();
            foreach (var j in joins)
            {
                Rule(j.ActorId == Actor.PlayerId || Npc(j.ActorId), e.Id + " joins known actor " + j.ActorId);
                Standable(j.JoinAt, e.Id + " join of " + j.ActorId);
            }
            var trigger = Command.Parse(e.Trigger);
            Rule(trigger.Count == 1 && trigger[0].Name == "interact" && trigger[0].Args.Count == 3 && Npc(trigger[0].Args[0]) && Num(trigger[0].Args[2]),
                e.Id + " trigger is interact npc label range");
            foreach (var w in Command.Parse(e.When))
                Rule(w.Name == "eq" && w.Args.Count == 2 && Var(w.Args[0]), e.Id + " condition " + w.Name);
            foreach (var list in new[] { e.Script }.Concat(e.Forks.Values))
                foreach (var cmd in Command.Parse(list))
                {
                    var a = cmd.Args;
                    Rule(Commands.Contains(cmd.Name) && !cmd.Negated, e.Id + " command " + cmd.Name + " is handled");
                    if (cmd.Name == "move")
                    {
                        var join = joins.FirstOrDefault(j => a.Count == 2 && j.ActorId == a[0]);
                        Rule(a.Count == 2 && Npc(a[0]) && Dest(a[1]), e.Id + " move npc destination");
                        Rule(join != null && Dest(a[1]) && Reachable(join.JoinAt, c.Destination(a[1]).Position), e.Id + " move starts from a join and reaches its destination");
                    }
                    if (cmd.Name == "say") Rule(a.Count == 2 && Npc(a[0]), e.Id + " say speaker text");
                    if (cmd.Name == "choice")
                        Rule(a.Count >= 3 && a.Count % 2 == 1 && a.Skip(1).Where((_, i) => i % 2 == 0).All(e.Forks.ContainsKey), e.Id + " choice prompt fork label...");
                }
            foreach (var fx in Command.Parse(e.Effects))
            {
                var a = fx.Args;
                if (fx.Name == "set")
                    Rule(a.Count == 2 && Var(a[0]) && ValueFits(c.Variable(a[0]), a[1]), e.Id + " set variable value");
                else if (fx.Name == "currency.add")
                    Rule(a.Count == 1 && int.TryParse(a[0], out var n) && n > 0, e.Id + " currency.add amount");
                else Rule(false, e.Id + " effect " + fx.Name + " is handled");
            }
        }
        return p;
    }

    static bool ValueFits(VariableDef v, string text)
    {
        try { return JToken.Parse(text).Type == (v.Type == "bool" ? JTokenType.Boolean : JTokenType.Integer); }
        catch (Newtonsoft.Json.JsonException) { return false; }
    }

    static void ContentRules()
    {
        foreach (var dir in new[] { "base", "variants/a10-mira", "variants/dest-moved" })
        {
            var problems = Problems(Content.Load(Path.Combine(Fc, dir)));
            Check("content rules hold for " + dir + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""), problems.Count == 0);
        }
        var broken = Directory.GetDirectories(Path.Combine(Fc, "variants", "invalid")).Append(Path.Combine(Fc, "variants", "unregistered-effect"));
        var recipes = File.ReadAllText(Path.Combine(Base, "staging-recipes.json"));
        foreach (var dir in broken)
            Check("content rules catch " + Path.GetFileName(dir), Problems(Content.Parse(File.ReadAllText(Path.Combine(dir, "runner-bundle.json")), recipes)).Count > 0);
    }

    static void StartFixtures()
    {
        foreach (var dir in new[] { "base", "variants/a10-mira" })
        {
            var c = Content.Load(Path.Combine(Fc, dir));
            foreach (var file in Directory.GetFiles(Path.Combine(Fc, dir), "start-*.json"))
            {
                var s = SaveFile.Read(file);
                var map = c.Maps.FirstOrDefault(m => m.Id == s.Player.MapId);
                var ok = map != null && World.Passable(map, (int)Math.Floor(s.Player.X), (int)Math.Floor(s.Player.Y))
                    && s.Flags.All(f => c.Variables.Any(v => v.Id == f.Key && ValueFits(v, f.Value.ToString(Newtonsoft.Json.Formatting.None))))
                    && s.Appointments.Keys.All(id => c.Events.Any(e => e.Id == id))
                    && s.Completed.All(id => c.Events.Any(e => e.Id == id));
                Check("start fixture fits content: " + dir + "/" + Path.GetFileName(file), ok);
            }
        }
        foreach (var file in Directory.GetFiles(Fc, "start-*.json", SearchOption.AllDirectories))
        {
            var copy = file.Replace(Fc, Path.Combine(FindRoot(), "Content", "Fixtures", "FirstConnection", "generated"));
            Check("fixture copy matches " + Path.GetFileName(file), File.Exists(copy) && File.ReadAllText(copy) == File.ReadAllText(file));
        }
    }

    // ------------------------------------------------------------------ play helpers

    static Game NewGame(string fixture, string variant = "base", int? minute = null)
    {
        var dir = Path.Combine(Fc, variant);
        var state = SaveFile.Read(Path.Combine(dir, fixture));
        if (minute.HasValue) state.Minute = minute.Value;
        return new Game(Content.Load(dir), state, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
    }

    static void Run(Game g, double seconds, Func<bool> until = null)
    {
        for (var t = 0.0; t < seconds && (until == null || !until()); t += 1 / 30.0) g.Update(1 / 30.0);
    }

    /// <summary>Plays the event to its end, answering every choice with the given index. Returns every line shown.</summary>
    static List<string> Play(Game g, int choice)
    {
        var shown = new List<string>();
        for (var frame = 0; g.Event != null && frame < 30 * 120; frame++)
        {
            g.Update(1 / 30.0);
            if (g.Dialogue == null) continue;
            shown.Add(g.Dialogue.Text);
            if (g.Dialogue.Choices == null) g.Advance(); else g.Choose(choice);
        }
        return shown;
    }

    static bool At(Actor a, string map, double x, double y) => a.MapId == map && Math.Abs(a.X - x) < 1e-6 && Math.Abs(a.Y - y) < 1e-6;

    // ------------------------------------------------------------------ behavior

    static void InteractAndAccept()
    {
        var g = NewGame("start-offered.json");
        Check("interact far from the NPC lists nothing", g.Interact().Count == 0);
        g.SetPlayerInput(0, 1);
        Run(g, 5, () => g.Player.Y >= 3.2);
        g.SetPlayerInput(0, 0);
        var invites = g.Interact();
        Check("interact in range lists both invitations as Offered",
            invites.Select(i => i.EventId).SequenceEqual(new[] { "event.snack", "event.snack_later" })
            && invites.All(i => i.Status == Appointment.Offered && i.NpcId == "npc.serin") && invites[0].Label == "간식 모임 시작");
        Check("accept turns Offered into Accepted", g.Accept("event.snack") && g.Interact()[0].Status == Appointment.Accepted);
        Check("accepting twice is refused", !g.Accept("event.snack"));
    }

    static void EventSuccess(int choice, string forkLine)
    {
        var name = choice == 0 ? "notes" : "tea";
        var g = NewGame("start-accepted.json");
        Check(name + ": event starts and hides the screen", g.StartEvent("event.snack") && g.Hidden);
        g.Update(1 / 30.0);
        Check(name + ": staging places actors at the join while hidden",
            g.Hidden && At(g.Actor("npc.serin"), "map.lounge", 0.5, 2.5) && At(g.Player, "map.lounge", 0.5, 2.5) && g.MapId == "map.lounge");
        g.Update(1 / 30.0);
        Check(name + ": screen shows again before the script runs", !g.Hidden && g.Actor("npc.serin").Moving);
        var shown = Play(g, choice);
        var other = choice == 0 ? "좋아, 따뜻한 차가 필요했어." : "같이 하니까 마음이 놓여.";
        Check(name + ": says, asks, then runs only the chosen fork",
            shown.SequenceEqual(new[] { "실습 노트를 같이 정리해 볼래?", "서린과 무엇부터 할까?", forkLine }) && !shown.Contains(other));
        var s = g.State;
        Check(name + ": effects applied once (wallet +20, flag, clock +10, appointment removed, completed)",
            s.Wallet == 220 && g.Flag("memory.snack_done") == "true" && s.Minute == 430 && !s.Appointments.ContainsKey("event.snack")
            && s.Appointments.ContainsKey("event.snack_later") && s.Completed.SequenceEqual(new[] { "event.snack" }));
        Check(name + ": actors stay where the event left them",
            At(g.Actor("npc.serin"), "map.lounge", 4.5, 3.5) && g.Actor("npc.serin").Facing == "north" && g.MapId == "map.lounge");
        g.Update(1 / 30.0);
        Check(name + ": after the event the NPC walks back to its schedule spot", g.Actor("npc.serin").Moving && g.Actor("npc.serin").Path.Last().MapId == "map.courtyard");
        Check(name + ": replay is refused and pays nothing", !g.StartEvent("event.snack") && g.Event == null && s.Wallet == 220);
    }

    static void IntroFalseRefused()
    {
        var g = NewGame("start-intro-false.json");
        g.Accept("event.snack");
        Check("intro-false: event refused by its condition", !g.StartEvent("event.snack") && g.Event == null && !g.Hidden);
    }

    static void CancelRestores()
    {
        var g = NewGame("start-accepted.json");
        g.StartEvent("event.snack");
        Run(g, 10, () => g.Dialogue != null);
        Check("cancel: event reached its first line in the lounge", g.Dialogue != null && g.Actor("npc.serin").MapId == "map.lounge");
        g.CancelEvent();
        var s = g.State;
        Check("cancel restores actors, map and clock and applies nothing",
            At(g.Actor("npc.serin"), "map.courtyard", 1.5, 4.5) && At(g.Player, "map.courtyard", 1.5, 1.5) && g.MapId == "map.courtyard"
            && s.Minute == 420 && s.Wallet == 200 && s.Completed.Count == 0 && s.Appointments["event.snack"] == Appointment.Accepted
            && g.Event == null && g.Dialogue == null && !g.Hidden);
        Check("cancel: the event can start again", g.StartEvent("event.snack"));
    }

    static void SchedulePicks()
    {
        Check("schedule: load at 13:20 places the NPC at dest.notice", At(NewGame("start-offered.json", minute: 800).Actor("npc.serin"), "map.courtyard", 5.5, 1.5));
        Check("schedule: load at 05:00 uses the previous day's last entry (home)", At(NewGame("start-offered.json", minute: 300).Actor("npc.serin"), "map.courtyard", 1.5, 4.5));
        var g = NewGame("start-offered.json", minute: 715);
        var serin = g.Actor("npc.serin");
        Run(g, 4.5);
        Check("schedule: before 12:00 the NPC stays home", At(serin, "map.courtyard", 1.5, 4.5) && !serin.Moving);
        Run(g, 1);
        Check("schedule: at 12:00 the NPC starts walking", serin.Moving && g.State.Minute == 720);
        Run(g, 10, () => !serin.Moving);
        Check("schedule: the NPC walks to dest.notice", At(serin, "map.courtyard", 5.5, 1.5));
        var held = NewGame("start-accepted.json", minute: 719);
        held.StartEvent("event.snack");
        Run(held, 5, () => held.Dialogue != null);
        Check("schedule: an NPC held by an event skips its schedule", held.State.Minute == 719 && At(held.Actor("npc.serin"), "map.lounge", 4.5, 3.5));
    }

    static void PathAcrossMaps()
    {
        var c = Content.Load(Base);
        var walker = new Actor { Id = "walker", MapId = "map.courtyard", X = 1.5, Y = 4.5, Speed = 3 };
        walker.Path = World.FindPath(c, walker, c.Destination("dest.snack").Position);
        var maps = new HashSet<string>(walker.Path.Select(s => s.MapId));
        Check("path: courtyard home to lounge snack crosses both maps", maps.SetEquals(new[] { "map.courtyard", "map.lounge" }));
        for (var i = 0; i < 30 * 20 && walker.Path.Count > 0; i++) World.Walk(c, walker, 1 / 30.0);
        Check("path: walking it arrives through the portal", At(walker, "map.lounge", 4.5, 3.5));
        var boxed = new Actor { MapId = "map.courtyard", X = 1.5, Y = 1.5 };
        Check("path: an unreachable goal has no path", World.FindPath(c, boxed, new Spot { MapId = "map.region_1", X = 1.5, Y = 1.5 }) == null);
    }

    static void PlayerMovement()
    {
        var g = NewGame("start-offered.json");
        World.Place(g.Player, new Spot { MapId = "map.courtyard", X = 2.5, Y = 1.5 });
        g.SetPlayerInput(1, 0);
        Run(g, 1);
        Check("player: a blocked cell stops movement on that axis", g.Player.X <= 2.8 + 1e-9 && g.Player.X > 2.6 && g.Player.Facing == "east");
        World.Place(g.Player, new Spot { MapId = "map.courtyard", X = 6.5, Y = 2.5 });
        Run(g, 1, () => g.MapId == "map.lounge");
        Check("player: walking into the portal cell crosses to the lounge", At(g.Player, "map.lounge", 0.5, 2.5));
        g.SetPlayerInput(0, 0);
        Run(g, 0.5);
        Check("player: standing on the arrival cell does not bounce back", g.MapId == "map.lounge");
        g.SetPlayerInput(1, 0);
        Run(g, 0.4);
        g.SetPlayerInput(-1, 0);
        Run(g, 1, () => g.MapId == "map.courtyard");
        Check("player: leaving and re-entering the arrival cell crosses back", At(g.Player, "map.courtyard", 7.5, 2.5));
    }

    static void DayEndSaves()
    {
        var g = NewGame("start-accepted.json");
        g.StartEvent("event.snack");
        Check("day end: refused while an event runs (R4)", !g.EndDay(DayEnd.Slept) && g.State.Day == 0);
        Play(g, 0);
        Check("day end: finishing an event does not save (E2)", !File.Exists(g.SavePath));
        Check("bed: the player in the lounge is not near the bed", !g.NearBed);
        World.Place(g.Player, g.Content.Bed);
        Check("bed: standing on the bed is near it", g.NearBed);
        Check("sleep: EndDay(Slept) ends the day", g.EndDay(DayEnd.Slept) && g.LastDayEnd == DayEnd.Slept);
        Check("sleep: next day starts at 07:00 at the bed with the NPC home and the file saved",
            g.State.Day == 1 && g.State.Minute == 420 && At(g.Player, "map.courtyard", 0.5, 0.5)
            && At(g.Actor("npc.serin"), "map.courtyard", 1.5, 4.5) && File.Exists(g.SavePath));
        var reloaded = Game.Open(Base, Path.Combine(Base, "start-offered.json"), g.SavePath);
        Check("day end: reload gives the same state", SaveFile.ToJson(reloaded.State) == SaveFile.ToJson(g.State)
            && reloaded.State.Wallet == 220 && At(reloaded.Player, "map.courtyard", 0.5, 0.5));
        Check("day end: the event stays refused after reload", !reloaded.StartEvent("event.snack"));

        var night = NewGame("start-offered.json", minute: Clock.MinutesPerDay - 2);
        Run(night, 3);
        Check("clock: midnight does not end the day", night.State.Day == 0 && night.State.Minute > Clock.MinutesPerDay);
        night.State.Minute = Clock.PassOutAt - 2;
        Run(night, 1.5);
        Check("clock: one real second is one game minute", night.State.Minute == Clock.PassOutAt - 1 && night.State.Day == 0);
        Run(night, 1);
        Check("pass out: 02:00 ends the day at the bed and saves",
            night.State.Day == 1 && night.State.Minute == 420 && night.LastDayEnd == DayEnd.PassedOut
            && At(night.Player, "map.courtyard", 0.5, 0.5) && File.Exists(night.SavePath));
    }

    static void SaveFallsBackToBak()
    {
        var path = Path.Combine(Temp, "bak", "save.json");
        var first = SaveFile.Read(Path.Combine(Base, "start-offered.json"));
        SaveFile.Write(path, first);
        var second = SaveFile.Read(path);
        second.Wallet = 999;
        SaveFile.Write(path, second);
        Check("save: the second write keeps the first as .bak", File.Exists(path + ".bak") && SaveFile.Read(path).Wallet == 999);
        File.WriteAllText(path, "{ broken");
        Check("save: a broken file falls back to .bak", SaveFile.Read(path)?.Wallet == 200);
        Check("save: no file reads as null", SaveFile.Read(Path.Combine(Temp, "missing.json")) == null);
    }
}
