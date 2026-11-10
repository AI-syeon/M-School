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

        public string Id, MapId;
        public double X, Y;
        public string Facing = "south";
        [JsonIgnore] public double Speed = PlayerSpeed;
        [JsonIgnore] public bool Moving;
        /// <summary>Points still to walk through, nearest first. Empty when standing.</summary>
        [JsonIgnore] public List<Spot> Path = new List<Spot>();

        public Actor Clone()
        {
            var copy = (Actor)MemberwiseClone();
            copy.Path = new List<Spot>(Path);
            return copy;
        }

        public void CopyFrom(Actor other)
        {
            MapId = other.MapId; X = other.X; Y = other.Y; Facing = other.Facing; Moving = other.Moving;
            Path = new List<Spot>(other.Path);
        }
    }

    public static class Clock
    {
        public const int DayStart = 7 * 60;
        public const int MinutesPerDay = 24 * 60;
        /// <summary>02:00 of the next morning, written as minutes past the day's midnight.</summary>
        public const int PassOutAt = 26 * 60;
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
