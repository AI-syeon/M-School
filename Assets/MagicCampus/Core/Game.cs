using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace MagicCampus.Core
{
    /// <summary>
    /// The game root (R6: plain state, a few rule methods). One thread, one frame loop (R2): the Unity side calls Update once per
    /// frame, reads the public state to draw, and forwards input to the methods below.
    /// </summary>
    public sealed class Game
    {
        public const double SecondsPerMinute = 1;
        public const double BedReach = 1;

        public readonly Content Content;
        public readonly GameState State;
        public readonly List<Actor> Npcs;
        /// <summary>The session's incantation, target tags and last cast. Not saved.</summary>
        public readonly Magic Magic;
        /// <summary>The bag, notebook key items and mailbox; its state is in State and saved with the day.</summary>
        public readonly Inventory Inventory;
        /// <summary>Daily requests: what is open, and the checks that complete them. Progress is in State and saved with the day.</summary>
        public readonly Quests Quests;
        /// <summary>The player's furniture arrangement of the rooms that allow it; the state is in State and saved with the day.</summary>
        public readonly Furniture Furniture;
        public readonly string SavePath;
        public EventRun Event { get; private set; }
        public Dialogue Dialogue { get; internal set; }
        /// <summary>True while reserved staging moves actors behind a hidden screen.</summary>
        public bool Hidden { get; internal set; }
        /// <summary>How the last day ended in this session; null until a day ends.</summary>
        public DayEnd? LastDayEnd { get; private set; }
        /// <summary>What the day that just ended brought (quests done, crowns); null until a day ends in this session. Not saved.</summary>
        public DayReport LastDayReport { get; private set; }
        /// <summary>Player-facing notices in the order they happened, waiting for the UI. Not saved.</summary>
        public IReadOnlyList<Notice> Notices => notices;
        /// <summary>
        /// Reads the current value of a spell requirement, or null when no system provides that kind. By default it reads the bag for
        /// itemCountAtLeast and nothing else (see Magic.ProvidedKinds); tests replace it to prove the thresholds.
        /// </summary>
        public Func<RequirementDef, int?> RequirementProvider { get; set; }
        /// <summary>World seconds that have run in this session; the clock for tag windows. Paused while an event or a menu holds the world.</summary>
        public double MagicSeconds { get; private set; }

        double inputX, inputY, seconds;
        int scheduledThrough, walletAtDayStart;
        readonly List<Notice> notices = new List<Notice>();

        public Game(Content content, GameState state, string savePath)
        {
            Content = content;
            State = state;
            SavePath = savePath;
            State.NormalizeMagic(content);
            State.NormalizeInventory(content);
            State.NormalizeQuests();
            walletAtDayStart = state.Wallet;
            Magic = new Magic(this);
            Inventory = new Inventory(this);
            Quests = new Quests(this);
            RequirementProvider = r => r.Kind == "itemCountAtLeast" ? Inventory.Count(r.Id) : (int?)null;
            MarkVisited();
            Npcs = content.Npcs.Select(n => new Actor { Id = n.Id, Speed = n.Speed }).ToList();
            Schedule.PlaceAll(content, Npcs, state.Minute);
            scheduledThrough = state.Minute;
            Furniture = new Furniture(this);
        }

        /// <summary>Continues from the save when one exists, otherwise from the start fixture.</summary>
        public static Game Open(string contentDir, string startFixture, string savePath)
        {
            return new Game(Content.Load(contentDir), SaveFile.Read(savePath) ?? SaveFile.Read(startFixture), savePath);
        }

        /// <summary>Casts a spell by id: the one rule behind both the incantation and the quick slots.</summary>
        public CastResult TryCastSpell(string spellId)
        {
            var result = Magic.Cast(spellId);
            Quests.Evaluate();
            return result;
        }

        /// <summary>Takes every waiting notice, oldest first. The UI calls it once per frame and turns them into toasts.</summary>
        public List<Notice> DrainNotices()
        {
            var taken = notices.ToList();
            notices.Clear();
            return taken;
        }

        internal void Notify(NoticeKind kind, string text, string questId = null, string itemId = null)
        {
            notices.Add(new Notice { Kind = kind, Text = text, QuestId = questId, ItemId = itemId });
        }

        /// <summary>Puts every magic target back to its starting tags. Task 6-1's room reset will call this.</summary>
        public void ResetMagicTargets() => Magic.ResetTargets();

        public Actor Player => State.Player;
        public string MapId => Player.MapId;
        public IEnumerable<Actor> Actors => new[] { Player }.Concat(Npcs);
        public Actor Actor(string id) => Actors.First(a => a.Id == id);

        public string Flag(string id)
        {
            return (State.Flags.TryGetValue(id, out var value) ? value : Content.Variable(id).Initial).ToString(Formatting.None);
        }

        public void SetPlayerInput(double x, double y)
        {
            inputX = x;
            inputY = y;
        }

        public void Update(double dt)
        {
            Step(dt);
            Quests.Evaluate();
        }

        void Step(double dt)
        {
            MarkVisited();
            if (Event != null)
            {
                if (Event.Update(this, dt)) EndEvent();
                return;
            }
            var mapBefore = Player.MapId;
            World.MovePlayer(Content, Player, inputX, inputY, dt);
            if (Player.MapId != mapBefore) Magic.Cancel();
            MarkVisited();
            foreach (var npc in Npcs)
            {
                World.Walk(Content, npc, dt);
                Schedule.Arrive(Content, npc);
            }
            MagicSeconds += dt;
            seconds += dt;
            var minutes = (int)(seconds / SecondsPerMinute);
            seconds -= minutes * SecondsPerMinute;
            State.Minute += minutes;
            Schedule.Run(Content, Npcs, scheduledThrough, State.Minute);
            scheduledThrough = State.Minute;
            if (State.Minute >= Clock.PassOutAt) EndDay(DayEnd.PassedOut);
        }

        /// <summary>Invitations of the nearest NPC whose interact trigger reaches the player.</summary>
        public List<Invite> Interact()
        {
            var inRange = Content.Events
                .Select(e => (Event: e, Trigger: Command.Parse(e.Trigger)[0]))
                .Where(x => x.Trigger.Name == "interact")
                .Select(x => (x.Event, Npc: x.Trigger.Args[0], Label: x.Trigger.Args[1],
                    Distance: World.Distance(Actor(x.Trigger.Args[0]), new Spot { MapId = Player.MapId, X = Player.X, Y = Player.Y }),
                    Range: double.Parse(x.Trigger.Args[2], System.Globalization.CultureInfo.InvariantCulture)))
                .Where(x => x.Distance <= x.Range)
                .ToList();
            if (inRange.Count == 0) return new List<Invite>();
            var npc = inRange.OrderBy(x => x.Distance).First().Npc;
            return inRange
                .Where(x => x.Npc == npc && State.Appointments.ContainsKey(x.Event.Id))
                .Select(x => new Invite { EventId = x.Event.Id, Label = x.Label, NpcId = npc, Status = State.Appointments[x.Event.Id] })
                .ToList();
        }

        public bool Accept(string eventId)
        {
            if (!State.Appointments.TryGetValue(eventId, out var status) || status != Appointment.Offered) return false;
            State.Appointments[eventId] = Appointment.Accepted;
            return true;
        }

        /// <summary>Starts the event when none is running, it has not completed (once) and its conditions hold.</summary>
        public bool StartEvent(string eventId)
        {
            var def = Content.Event(eventId);
            if (Event != null || State.Completed.Contains(eventId) || !EventRun.Holds(this, def.When)) return false;
            Event = new EventRun(this, def);
            Magic.Cancel();
            foreach (var actor in Actors) actor.Moving = false;
            Hidden = true;
            return true;
        }

        public void Advance()
        {
            if (Dialogue != null && Dialogue.Choices == null) Dialogue = null;
        }

        public void Choose(int index)
        {
            if (Dialogue?.Choices != null) Event.Choose(this, index);
        }

        public void CancelEvent()
        {
            if (Event == null) return;
            Event.Restore(this);
            Dialogue = null;
            Hidden = false;
            EndEvent();
        }

        /// <summary>After an event every NPC heads back to the routine step in force now, and its action starts again on arrival.</summary>
        void EndEvent()
        {
            Event = null;
            Schedule.Resume(Content, Npcs, State.Minute);
            scheduledThrough = State.Minute;
        }

        /// <summary>
        /// Moves the clock to the minute and puts every NPC where its routine has it then, action on, like a load at that time.
        /// For the probe and tests; play only ever advances the clock a minute at a time.
        /// </summary>
        public void SetClock(int minute)
        {
            State.Minute = minute;
            seconds = 0;
            Schedule.PlaceAll(Content, Npcs, minute);
            scheduledThrough = minute;
        }

        /// <summary>Adds the player's current map to the visited list the map screen reads.</summary>
        void MarkVisited()
        {
            if (Player.MapId != null && !State.VisitedMaps.Contains(Player.MapId)) State.VisitedMaps.Add(Player.MapId);
        }

        public bool NearBed => Player.MapId == Content.Bed.MapId && World.Distance(Player, Content.Bed) <= BedReach;

        /// <summary>
        /// The one way a day ends, whoever calls it: the bed (Slept) or the clock reaching 02:00 (PassedOut). The next day starts
        /// at 07:00 with the player at the bed and NPCs at their schedule, then the game saves (E2). Refused while an event runs (R4).
        /// </summary>
        public bool EndDay(DayEnd how)
        {
            if (Event != null) return false;
            Quests.Evaluate();
            LastDayReport = new DayReport { Day = State.Day, How = how, Quests = Quests.TakeCompletedToday(), CrownsEarned = State.Wallet - walletAtDayStart };
            walletAtDayStart = State.Wallet;
            State.Day++;
            State.Minute = Clock.DayStart;
            seconds = 0;
            Magic.Cancel();
            Magic.ResetTargets();
            World.Place(Player, Content.Bed, "south");
            MarkVisited();
            Schedule.PlaceAll(Content, Npcs, State.Minute);
            scheduledThrough = State.Minute;
            LastDayEnd = how;
            SaveFile.Write(SavePath, State);
            return true;
        }
    }
}
