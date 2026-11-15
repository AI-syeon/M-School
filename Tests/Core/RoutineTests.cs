using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;
using Newtonsoft.Json.Linq;

static partial class Program
{
    static readonly string[] Facings = { "north", "south", "east", "west" };
    static Dictionary<string, HashSet<string>> southClips;

    /// <summary>The action clips an NPC's character manifest draws facing south; the view falls back to these for every facing.</summary>
    static HashSet<string> SouthClips(string npcId)
    {
        if (southClips == null)
        {
            southClips = new Dictionary<string, HashSet<string>>();
            var characters = Path.Combine(Fc, "..", "UiArt", "Characters");
            if (Directory.Exists(characters))
                foreach (var file in Directory.GetFiles(characters, "manifest.json", SearchOption.AllDirectories))
                {
                    var manifest = JObject.Parse(File.ReadAllText(file));
                    var south = manifest["animations"]?["south"] as JObject;
                    southClips[(string)manifest["id"]] = new HashSet<string>(south == null ? new string[0] : south.Properties().Select(x => x.Name));
                }
        }
        return southClips.TryGetValue(npcId, out var clips) ? clips : new HashSet<string>();
    }

    /// <summary>Every rule the routine runtime trusts without checking. Returns one line per broken rule.</summary>
    static List<string> RoutineProblems(Content c)
    {
        var p = new List<string>();
        void Rule(bool ok, string what) { if (!ok) p.Add(what); }
        foreach (var npc in c.Npcs)
            Rule(c.Routines.TryGetValue(npc.Id, out var own) && own != null && own.Count > 0, npc.Id + " has a routine");
        foreach (var pair in c.Routines)
        {
            Rule(c.Npcs.Any(n => n.Id == pair.Key), pair.Key + " routine belongs to a known npc");
            var clips = SouthClips(pair.Key);
            var places = new List<string>();
            var last = -1;
            foreach (var step in pair.Value ?? new List<RoutineStep>())
            {
                if (step == null) { Rule(false, pair.Key + " has an empty step"); continue; }
                var what = pair.Key + " step " + step.At;
                Rule(step.Minute >= 0, what + " has a time HH:MM before 26:00");
                Rule(step.Minute < 0 || step.Minute > last, what + " comes after the step before it");
                if (step.Minute >= 0) last = Math.Max(last, step.Minute);
                var known = step.Place == RoutineStep.Home || c.Destinations.Any(d => d.Id == step.Place);
                Rule(known, what + " place " + step.Place + " is home or a destination");
                if (known) places.Add(step.Place == RoutineStep.Home ? (c.Npcs.FirstOrDefault(n => n.Id == pair.Key)?.Home ?? RoutineStep.Home) : step.Place);
                Rule(step.Action == Actor.Idle || (step.Action != "walk" && clips.Contains(step.Action)), what + " action " + step.Action + " has a south clip in the character manifest");
                Rule(step.Facing == null || Facings.Contains(step.Facing), what + " facing " + step.Facing);
            }
            foreach (var a in places.Distinct().Where(id => c.Destinations.Any(d => d.Id == id)))
                foreach (var b in places.Distinct().Where(id => c.Destinations.Any(d => d.Id == id)))
                    Rule(World.FindPath(c, new Actor { MapId = c.Destination(a).Position.MapId, X = c.Destination(a).Position.X, Y = c.Destination(a).Position.Y }, c.Destination(b).Position) != null,
                        pair.Key + " walks " + a + " -> " + b);
        }
        return p;
    }

    static Content WithRoutines(string json) => Content.Parse(File.ReadAllText(Path.Combine(Base, "runner-bundle.json")), File.ReadAllText(Path.Combine(Base, "staging-recipes.json")), null, null, json);

    static Game GameWith(string routinesJson, int minute = Clock.DayStart)
    {
        var state = SaveFile.Read(Path.Combine(Base, "start-offered.json"));
        state.Minute = minute;
        return new Game(WithRoutines(routinesJson), state, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
    }

    static bool Broken(string json, string expected)
    {
        var problems = RoutineProblems(WithRoutines(json));
        return problems.Any(x => x.Contains(expected));
    }

    static void RoutineTests()
    {
        // Time text.
        Check("routine time: HH:MM, hours past 24 for the small hours, nothing at or after 26:00",
            Clock.ParseTime("07:00") == 420 && Clock.ParseTime("7:05") == 425 && Clock.ParseTime("24:30") == 1470 && Clock.ParseTime("25:59") == 1559
            && Clock.ParseTime("26:00") == -1 && Clock.ParseTime("12:60") == -1 && Clock.ParseTime("noon") == -1 && Clock.ParseTime("1200") == -1 && Clock.ParseTime(null) == -1
            && Clock.FormatTime(1470) == "24:30");

        // The shipped routines.
        foreach (var dir in new[] { "base", "variants/a10-mira", "variants/dest-moved" })
        {
            var c = Content.Load(Path.Combine(Fc, dir));
            var problems = RoutineProblems(c);
            Check("routines " + dir + ": every NPC has one and it passes validation" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""),
                problems.Count == 0 && c.Npcs.All(n => c.Routine(n.Id).Count >= 5));
            Check("routines " + dir + ": the day starts at home", c.Npcs.All(n => c.Routine(n.Id)[0].Minute == Clock.DayStart && c.Routine(n.Id)[0].Place == RoutineStep.Home));
        }
        Check("routines: the a10 variant gives Mira a day that runs past midnight", Content.Load(Path.Combine(Fc, "variants", "a10-mira")).Routine("npc.mira").Last().Minute > Clock.MinutesPerDay);

        // Which step is in force.
        var b = Content.Load(Base);
        string In(int minute) => Schedule.StepAt(b, "npc.serin", minute).Place + "/" + Schedule.StepAt(b, "npc.serin", minute).Action;
        Check("routine step: the latest step at or before the minute", In(420) == "home/idle" && In(539) == "home/idle" && In(540) == "dest.library_desk/read" && In(659) == "dest.library_desk/read"
            && In(660) == "dest.classroom_seat/write" && In(750) == "dest.cafeteria_table/eat" && In(989) == "dest.greenhouse_bed/write" && In(990) == "dest.hall_board/chat");
        Check("routine step: before the first step it is the last step of the day, and so is every minute after it",
            In(300) == "home/idle" && In(1319) == "home/write" && In(1320) == "home/idle" && In(1500) == "home/idle" && In(1559) == "home/idle");
        var sorted = WithRoutines(@"{ ""npc.serin"": [
            { ""at"": ""10:00"", ""place"": ""dest.library_desk"", ""action"": ""read"" },
            { ""at"": ""08:00"", ""place"": ""home"" },
            { ""at"": ""bad"", ""place"": ""home"" },
            { ""at"": ""09:00"", ""place"": ""dest.nowhere"" } ] }");
        Check("routine step: steps run in time order, and a step with an unreadable time or an unknown place is dropped, not fatal",
            sorted.Routine("npc.serin").Select(s => s.At).SequenceEqual(new[] { "08:00", "10:00" }) && Schedule.StepAt(sorted, "npc.serin", 570).Place == RoutineStep.Home
            && Schedule.StepAt(sorted, "npc.serin", 600).Action == "read" && sorted.Routine("npc.unknown").Count == 0);
        Check("routine step: an action left out is idle", WithRoutines(@"{ ""npc.serin"": [ { ""at"": ""08:00"", ""place"": ""home"" } ] }").Routine("npc.serin")[0].Action == Actor.Idle);

        // Loading and starting a day put the NPC straight into the step in force.
        var loaded = NewGame("start-offered.json", minute: 560).Actor("npc.serin");
        Check("routine load: at 09:20 Serin already sits in the library reading, facing the way the place says",
            At(loaded, "map.library", 3.5, 4.5) && loaded.Action == "read" && loaded.Facing == "north" && !loaded.Moving && loaded.Goal == null);
        var noon = NewGame("start-offered.json", minute: 800).Actor("npc.serin");
        Check("routine load: at 13:20 she is eating in the cafeteria", At(noon, "map.cafeteria", 3.5, 3.5) && noon.Action == "eat");
        var night = NewGame("start-offered.json", minute: 300).Actor("npc.serin");
        Check("routine load: at 05:00 she is where the last step of the day left her", At(night, "map.courtyard", 8.5, 5.5) && night.Action == Actor.Idle);
        var next = NewGame("start-offered.json", minute: 560);
        next.EndDay(DayEnd.Slept);
        Check("routine day start: sleeping puts her at the 07:00 step at home with nothing going on",
            At(next.Actor("npc.serin"), "map.courtyard", 8.5, 5.5) && next.Actor("npc.serin").Action == Actor.Idle && next.State.Minute == Clock.DayStart && next.Actor("npc.serin").Goal == null);

        // Walking, then the action on arrival.
        var g = NewGame("start-offered.json", minute: 539);
        var serin = g.Actor("npc.serin");
        Run(g, 0.5);
        Check("routine walk: before 09:00 she stays home", At(serin, "map.courtyard", 8.5, 5.5) && !serin.Moving && serin.Goal == null);
        Run(g, 1);
        Check("routine walk: at 09:00 she starts walking, doing nothing, bound for the library", g.State.Minute == 540 && serin.Moving && serin.Action == Actor.Idle && serin.Goal != null && serin.Goal.Place == "dest.library_desk");
        var has = new HashSet<string> { "south.idle", "north.idle", "east.idle", "west.idle", "south.walk", "north.walk", "east.walk", "west.walk", "south.read", "south.eat" };
        Check("routine walk: the walking pose is the walk clip for her facing", ActorClips.KeyFor(serin, has.Contains) == serin.Facing + ".walk");
        Run(g, 60, () => serin.Goal == null);
        Check("routine arrive: she stands at the library desk, facing its way, reading", At(serin, "map.library", 3.5, 4.5) && !serin.Moving && serin.Goal == null && serin.Action == "read" && serin.Facing == "north");
        Check("routine pose: a standing action plays the south clip of that action for any facing", ActorClips.KeyFor(serin, has.Contains) == "south.read");
        serin.Action = "dance";
        Check("routine pose: an action without a clip falls back to idle", ActorClips.KeyFor(serin, has.Contains) == "north.idle");
        serin.Action = "read";
        serin.Facing = "west";
        var eastRead = new HashSet<string>(has) { "west.read" };
        Check("routine pose: an action clip for the exact facing wins; the Stage5 sheet's bare names are the last try",
            ActorClips.KeyFor(serin, eastRead.Contains) == "west.read" && ActorClips.KeyFor(new Actor { Moving = true, Facing = "east" }, new HashSet<string> { "walk", "idle" }.Contains) == "walk"
            && ActorClips.KeyFor(new Actor { Facing = "east" }, new HashSet<string> { "walk", "idle" }.Contains) == "idle");
        Run(g, 60 * 90, () => g.State.Minute >= 660 + 1);
        Check("routine walk: the next step (11:00) sends her on to class, writing on arrival", serin.Goal != null || (At(serin, "map.classroom", 3.5, 3.5) && serin.Action == "write"));
        Run(g, 60, () => serin.Goal == null);
        Check("routine arrive: she sits in the classroom writing", At(serin, "map.classroom", 3.5, 3.5) && serin.Action == "write");

        // A step may name its own facing.
        var turned = GameWith(@"{ ""npc.serin"": [ { ""at"": ""07:00"", ""place"": ""home"" }, { ""at"": ""07:05"", ""place"": ""dest.library_desk"", ""action"": ""read"", ""facing"": ""east"" } ] }");
        Run(turned, 60 * 5, () => turned.Actor("npc.serin").Action == "read");
        Check("routine facing: the step's facing wins over the place's", At(turned.Actor("npc.serin"), "map.library", 3.5, 4.5) && turned.Actor("npc.serin").Facing == "east");
        var placed = GameWith(@"{ ""npc.serin"": [ { ""at"": ""07:00"", ""place"": ""dest.library_desk"", ""action"": ""read"", ""facing"": ""west"" } ] }");
        Check("routine facing: and so does a load", placed.Actor("npc.serin").Facing == "west" && placed.Actor("npc.serin").Action == "read");

        // Two steps at the same place: no walk, the action just changes.
        var stay = GameWith(@"{ ""npc.serin"": [ { ""at"": ""07:00"", ""place"": ""home"", ""action"": ""read"" }, { ""at"": ""07:02"", ""place"": ""home"", ""action"": ""write"" } ] }");
        Run(stay, 1.5);
        Check("routine same place: the step before reads", stay.Actor("npc.serin").Action == "read");
        Run(stay, 1.5, () => stay.State.Minute >= 422);
        Run(stay, 0.2);
        Check("routine same place: the next step changes the action without a walk", stay.Actor("npc.serin").Action == "write" && !stay.Actor("npc.serin").Moving);

        // No routine at all: stands at home.
        var bare = GameWith("{}");
        Run(bare, 5);
        Check("routine none: an NPC with no routine stays at home doing nothing", At(bare.Actor("npc.serin"), "map.courtyard", 8.5, 5.5) && bare.Actor("npc.serin").Action == Actor.Idle);

        // An NPC held by an event skips its routine; after the event it returns to the step in force and the action starts again.
        var held = NewGame("start-accepted.json", minute: 539);
        held.StartEvent("event.snack");
        Run(held, 5, () => held.Dialogue != null);
        Check("routine event: an NPC held by an event skips its routine", held.State.Minute == 539 && At(held.Actor("npc.serin"), "map.lounge", 5.5, 3.5) && held.Actor("npc.serin").Action == Actor.Idle);
        var ev = NewGame("start-accepted.json", minute: 560);
        Check("routine event: before the event she is reading in the library", ev.Actor("npc.serin").Action == "read" && At(ev.Actor("npc.serin"), "map.library", 3.5, 4.5));
        ev.StartEvent("event.snack");
        Run(ev, 0.2);
        Check("routine event: the event takes her out of the pose", ev.Actor("npc.serin").Action == Actor.Idle && ev.Actor("npc.serin").Goal == null);
        Play(ev, 0);
        ev.Update(1 / 30.0);
        Check("routine event: afterwards she walks back to the step in force", ev.Actor("npc.serin").Moving && ev.Actor("npc.serin").Goal?.Place == "dest.library_desk" && ev.Actor("npc.serin").Action == Actor.Idle);
        Run(ev, 90, () => ev.Actor("npc.serin").Goal == null);
        Check("routine event: and reads again on arrival", At(ev.Actor("npc.serin"), "map.library", 3.5, 4.5) && ev.Actor("npc.serin").Action == "read");
        var cancelled = NewGame("start-accepted.json", minute: 560);
        cancelled.StartEvent("event.snack");
        Run(cancelled, 3);
        cancelled.CancelEvent();
        cancelled.Update(1 / 30.0);
        Check("routine event: cancelling puts her back in the library, reading", At(cancelled.Actor("npc.serin"), "map.library", 3.5, 4.5) && cancelled.Actor("npc.serin").Action == "read" && cancelled.Actor("npc.serin").Goal == null);
        var restored = NewGame("start-accepted.json", minute: 539);
        Run(restored, 1.5);
        var walker = restored.Actor("npc.serin");
        restored.StartEvent("event.snack");
        Run(restored, 3);
        restored.CancelEvent();
        Check("routine event: cancelling mid-walk keeps her bound for the library, then she arrives reading",
            walker.Goal?.Place == "dest.library_desk" && walker.Action == Actor.Idle);
        Run(restored, 90, () => walker.Goal == null);
        Check("routine event: the walk completes after the cancel", At(walker, "map.library", 3.5, 4.5) && walker.Action == "read");

        // The whole day, on the real clock, in every shipped bundle: a minute before the next step each NPC is settled in the step in force.
        foreach (var dir in new[] { "base", "variants/a10-mira", "variants/dest-moved" })
        {
            var state = SaveFile.Read(Path.Combine(Base, "start-offered.json"));
            var day = new Game(Content.Load(Path.Combine(Fc, dir)), state, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
            day.Content.Quests.Clear();
            var late = new List<string>();
            var seen = new HashSet<string>();
            var lastMinute = day.State.Minute;
            while (day.State.Minute < Clock.PassOutAt - 1)
            {
                day.Update(1 / 30.0);
                if (day.State.Minute == lastMinute) continue;
                lastMinute = day.State.Minute;
                foreach (var npc in day.Npcs)
                {
                    var due = day.Content.Routine(npc.Id).FirstOrDefault(s => s.Minute == day.State.Minute + 1);
                    if (due == null || !seen.Add(npc.Id + due.At)) continue;
                    var settled = Schedule.StepAt(day.Content, npc.Id, day.State.Minute);
                    var place = Schedule.PlaceOf(day.Content, npc.Id, settled);
                    if (!(At(npc, place.Position.MapId, place.Position.X, place.Position.Y) && npc.Action == settled.Action && !npc.Moving))
                        late.Add(npc.Id + " not settled in " + settled.Place + "/" + settled.Action + " at " + Clock.FormatTime(day.State.Minute) + " (" + npc.MapId + " " + npc.X + "," + npc.Y + " " + npc.Action + ")");
                }
            }
            Check("routine day " + dir + ": a minute before every step each NPC has arrived and is doing its action" + (late.Count > 0 ? ": " + string.Join("; ", late) : ""), late.Count == 0 && seen.Count >= day.Npcs.Sum(n => day.Content.Routine(n.Id).Count - 1));
        }

        // Validation catches a hand-edit mistake.
        string Day(string step) => @"{ ""npc.serin"": [ { ""at"": ""07:00"", ""place"": ""home"" }, " + step + " ] }";
        Check("routine rules: an NPC with no routine", Broken("{}", "npc.serin has a routine"));
        Check("routine rules: an unknown NPC", Broken(@"{ ""npc.serin"": [ { ""at"": ""07:00"", ""place"": ""home"" } ], ""npc.ghost"": [ { ""at"": ""07:00"", ""place"": ""home"" } ] }", "known npc"));
        Check("routine rules: an unreadable time", Broken(Day(@"{ ""at"": ""8am"", ""place"": ""home"" }"), "has a time"));
        Check("routine rules: 26:00 is outside the day", Broken(Day(@"{ ""at"": ""26:00"", ""place"": ""home"" }"), "has a time"));
        Check("routine rules: steps out of order", Broken(Day(@"{ ""at"": ""09:00"", ""place"": ""home"" }, { ""at"": ""08:00"", ""place"": ""home"" }"), "comes after"));
        Check("routine rules: two steps at one time", Broken(Day(@"{ ""at"": ""07:00"", ""place"": ""home"" }"), "comes after"));
        Check("routine rules: an unknown place", Broken(Day(@"{ ""at"": ""08:00"", ""place"": ""dest.nowhere"" }"), "is home or a destination"));
        Check("routine rules: an action the manifest has no clip for", Broken(Day(@"{ ""at"": ""08:00"", ""place"": ""home"", ""action"": ""dance"" }"), "action dance"));
        Check("routine rules: walk is not an action", Broken(Day(@"{ ""at"": ""08:00"", ""place"": ""home"", ""action"": ""walk"" }"), "action walk"));
        Check("routine rules: an unknown facing", Broken(Day(@"{ ""at"": ""08:00"", ""place"": ""home"", ""facing"": ""up"" }"), "facing up"));
        Check("routine rules: an action that has a clip passes", !RoutineProblems(WithRoutines(Day(@"{ ""at"": ""08:00"", ""place"": ""dest.hall_board"", ""action"": ""chat"", ""facing"": ""south"" }"))).Any());
    }
}
