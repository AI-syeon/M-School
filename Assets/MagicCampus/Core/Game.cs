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
        public readonly string SavePath;
        public EventRun Event { get; private set; }
        public Dialogue Dialogue { get; internal set; }
        /// <summary>True while reserved staging moves actors behind a hidden screen.</summary>
        public bool Hidden { get; internal set; }
        /// <summary>How the last day ended in this session; null until a day ends.</summary>
        public DayEnd? LastDayEnd { get; private set; }

        double inputX, inputY, seconds;
        int scheduledThrough;

        public Game(Content content, GameState state, string savePath)
        {
            Content = content;
            State = state;
            SavePath = savePath;
            Npcs = content.Npcs.Select(n => new Actor { Id = n.Id, Speed = n.Speed }).ToList();
            Schedule.PlaceAll(content, Npcs, state.Minute);
            scheduledThrough = state.Minute;
        }

        /// <summary>Continues from the save when one exists, otherwise from the start fixture.</summary>
        public static Game Open(string contentDir, string startFixture, string savePath)
        {
            return new Game(Content.Load(contentDir), SaveFile.Read(savePath) ?? SaveFile.Read(startFixture), savePath);
        }

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
            if (Event != null)
            {
                if (Event.Update(this, dt)) EndEvent();
                return;
            }
            World.MovePlayer(Content, Player, inputX, inputY, dt);
            foreach (var npc in Npcs) World.Walk(Content, npc, dt);
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

        /// <summary>After an event every NPC heads back to where its schedule says it should be now.</summary>
        void EndEvent()
        {
            Event = null;
            foreach (var npc in Npcs) World.WalkTo(Content, npc, Content.Destination(Schedule.DestinationAt(Content, npc.Id, State.Minute)));
        }

        public bool NearBed => Player.MapId == Content.Bed.MapId && World.Distance(Player, Content.Bed) <= BedReach;

        /// <summary>
        /// The one way a day ends, whoever calls it: the bed (Slept) or the clock reaching 02:00 (PassedOut). The next day starts
        /// at 07:00 with the player at the bed and NPCs at their schedule, then the game saves (E2). Refused while an event runs (R4).
        /// </summary>
        public bool EndDay(DayEnd how)
        {
            if (Event != null) return false;
            State.Day++;
            State.Minute = Clock.DayStart;
            seconds = 0;
            World.Place(Player, Content.Bed, "south");
            Schedule.PlaceAll(Content, Npcs, State.Minute);
            scheduledThrough = State.Minute;
            LastDayEnd = how;
            SaveFile.Write(SavePath, State);
            return true;
        }
    }
}
