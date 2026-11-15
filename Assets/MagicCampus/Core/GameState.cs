using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace MagicCampus.Core
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Appointment { Offered, Accepted }

    /// <summary>How a day ended: the player went to bed, or stayed up until Clock.PassOutAt.</summary>
    public enum DayEnd { Slept, PassedOut }

    /// <summary>Everything the save file holds. NPC positions are not saved: a load places them by schedule.</summary>
    public sealed class GameState
    {
        public int Day;
        public int Minute = Clock.DayStart;
        public Actor Player = new Actor { Id = Actor.PlayerId };
        public int Wallet;
        public Dictionary<string, JToken> Flags = new Dictionary<string, JToken>();
        public List<string> Completed = new List<string>();
        public Dictionary<string, Appointment> Appointments = new Dictionary<string, Appointment>();
        public List<string> KnownSpells = new List<string>();
        public bool QuickCastUnlocked;
        /// <summary>Spell id per quick slot (keys 1 to 4); null is an empty slot.</summary>
        public string[] QuickSlots = new string[Magic.QuickSlotCount];
        /// <summary>Per spell, the distinct situation contexts it has succeeded in. A list used as a set.</summary>
        public Dictionary<string, List<string>> SituationLog = new Dictionary<string, List<string>>();
        /// <summary>Bag slots in order (Inventory.BagSize long); null is an empty slot.</summary>
        public Lot[] Bag = new Lot[Inventory.BagSize];
        /// <summary>Key items kept in the notebook. They use no bag slot and can never be used up or thrown away.</summary>
        public List<string> KeyItems = new List<string>();
        /// <summary>The dorm mailbox: what did not fit in the bag. No limit.</summary>
        public List<Lot> Mailbox = new List<Lot>();
        /// <summary>Maps the player has stood on, in the order first visited. The map screen shows only these.</summary>
        public List<string> VisitedMaps = new List<string>();
        /// <summary>Per quest id, the indices of its steps that are done. A step stays done once its check held. A list used as a set.</summary>
        public Dictionary<string, List<int>> QuestSteps = new Dictionary<string, List<int>>();
        /// <summary>Quests whose reward has been given, in the order they completed. A quest is never given twice.</summary>
        public List<string> CompletedQuests = new List<string>();
        /// <summary>Spells that have succeeded at least once, in the order first cast. A list used as a set.</summary>
        public List<string> SpellsCast = new List<string>();
        /// <summary>Per arrangeable map id, where the player put its furniture. No entry means the layout's own arrangement. Normalized by Furniture.</summary>
        public Dictionary<string, RoomArrangement> Arrangements = new Dictionary<string, RoomArrangement>();

        /// <summary>
        /// Old saves lack the quest fields and a hand-edited file can hold nulls or repeats: fill defaults and make every list a set.
        /// Ids the content no longer defines are kept, like unknown spells, so a bundle without quests does not erase progress.
        /// </summary>
        public void NormalizeQuests()
        {
            CompletedQuests = (CompletedQuests ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            SpellsCast = (SpellsCast ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            var steps = new Dictionary<string, List<int>>();
            foreach (var pair in QuestSteps ?? new Dictionary<string, List<int>>())
                if (!string.IsNullOrEmpty(pair.Key)) steps[pair.Key] = (pair.Value ?? new List<int>()).Where(i => i >= 0).Distinct().ToList();
            QuestSteps = steps;
        }

        /// <summary>
        /// Old saves lack the inventory fields and a hand-edited file can hold empty or broken rows: make the bag BagSize long, drop
        /// empty lots, and move key items found in the bag or mailbox to the notebook. Unknown item ids are kept, like unknown spells.
        /// </summary>
        public void NormalizeInventory(Content content)
        {
            bool Usable(Lot l) => l != null && !string.IsNullOrEmpty(l.ItemId) && l.Count > 0;
            bool Key(Lot l) => content.Item(l.ItemId)?.IsKey ?? false;
            KeyItems = (KeyItems ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            var bag = new Lot[Inventory.BagSize];
            var old = Bag ?? new Lot[0];
            for (var i = 0; i < bag.Length && i < old.Length; i++) bag[i] = Usable(old[i]) ? old[i] : null;
            Mailbox = (Mailbox ?? new List<Lot>()).Where(Usable).ToList();
            foreach (var lot in bag.Concat(Mailbox).Where(l => l != null && Key(l)).ToList())
                if (!KeyItems.Contains(lot.ItemId)) KeyItems.Add(lot.ItemId);
            for (var i = 0; i < bag.Length; i++) if (bag[i] != null && Key(bag[i])) bag[i] = null;
            Mailbox.RemoveAll(Key);
            foreach (var lot in bag.Concat(Mailbox).Where(l => l != null))
                if (lot.Quality != Inventory.Fine || !(content.Item(lot.ItemId)?.HasQuality ?? true)) lot.Quality = Inventory.Standard;
            Bag = bag;
            VisitedMaps = (VisitedMaps ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        }

        /// <summary>
        /// Old saves lack the spell fields and a hand-edited file can hold null or a stale slot: fill defaults, make the slots four
        /// long, and empty slots that are locked or hold a spell that is gone or cannot be quick cast. Learned ids are kept even
        /// when this content does not define them, so a bundle without spells does not erase them on the next save.
        /// </summary>
        public void NormalizeMagic(Content content)
        {
            KnownSpells = (KnownSpells ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            SituationLog = SituationLog ?? new Dictionary<string, List<string>>();
            foreach (var key in SituationLog.Keys.ToList())
                SituationLog[key] = (SituationLog[key] ?? new List<string>()).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            var slots = new string[Magic.QuickSlotCount];
            for (var i = 0; i < slots.Length && QuickSlots != null && i < QuickSlots.Length; i++)
            {
                var spell = QuickSlots[i] == null ? null : content.Spell(QuickSlots[i]);
                slots[i] = QuickCastUnlocked && spell != null && spell.AllowQuickCast ? spell.Id : null;
            }
            QuickSlots = slots;
        }
    }

    public sealed class Actor
    {
        public const string PlayerId = "player";
        public const double PlayerSpeed = 3;
        public const string Idle = "idle";

        public string Id, MapId;
        public double X, Y;
        public string Facing = "south";
        [JsonIgnore] public double Speed = PlayerSpeed;
        [JsonIgnore] public bool Moving;
        /// <summary>Points still to walk through, nearest first. Empty when standing.</summary>
        [JsonIgnore] public List<Spot> Path = new List<Spot>();
        /// <summary>What the actor is doing while it stands: a routine action id, Idle when nothing. Runtime only, never saved. Placing or sending an actor off resets it.</summary>
        [JsonIgnore] public string Action = Idle;
        /// <summary>The routine step the actor is walking to; null when none. Its action starts when the walk ends.</summary>
        [JsonIgnore] public RoutineStep Goal;

        public Actor Clone()
        {
            var copy = (Actor)MemberwiseClone();
            copy.Path = new List<Spot>(Path);
            return copy;
        }

        public void CopyFrom(Actor other)
        {
            MapId = other.MapId; X = other.X; Y = other.Y; Facing = other.Facing; Moving = other.Moving; Action = other.Action; Goal = other.Goal;
            Path = new List<Spot>(other.Path);
        }
    }

    public static class Clock
    {
        public const int DayStart = 7 * 60;
        public const int MinutesPerDay = 24 * 60;
        /// <summary>02:00 of the next morning, written as minutes past the day's midnight.</summary>
        public const int PassOutAt = 26 * 60;

        /// <summary>"HH:MM" as minutes past the day's midnight; hours run to 25 so the small hours of the next morning are 24:xx and 25:xx. -1 when the text is not a time inside the day (before 26:00).</summary>
        public static int ParseTime(string text)
        {
            var m = text == null ? null : System.Text.RegularExpressions.Regex.Match(text, @"^([0-9]{1,2}):([0-5][0-9])$");
            if (m == null || !m.Success) return -1;
            var minute = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
            return minute < PassOutAt ? minute : -1;
        }

        public static string FormatTime(int minute) => (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
    }

    /// <summary>What the dialogue box shows. Choices is null for a line that waits for Advance.</summary>
    public sealed class Dialogue
    {
        public string Speaker, Text;
        public List<string> Choices;
    }

    public sealed class Invite
    {
        public string EventId, Label, NpcId;
        public Appointment Status;
    }
}
