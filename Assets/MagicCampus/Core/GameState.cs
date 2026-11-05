using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace MagicCampus.Core
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Appointment { Offered, Accepted }

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
