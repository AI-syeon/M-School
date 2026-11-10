using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace MagicCampus.Core
{
    public sealed class Spot
    {
        public string MapId;
        public double X, Y;
    }

    public sealed class VariableDef
    {
        public string Id, Type;
        public JToken Initial;
    }

    public sealed class PortalDef
    {
        public string Id;
        public double X, Y;
        public Spot To;
    }

    public sealed class MapDef
    {
        public string Id;
        public int Width, Height;
        public List<int[]> Blocked = new List<int[]>();
        public List<PortalDef> Portals = new List<PortalDef>();
    }

    public sealed class DestinationDef
    {
        public string Id;
        public Spot Position;
        public double Radius;
        public string Facing;
    }

    public sealed class NpcDef
    {
        public string Id, Name, Home;
        public Spot Position;
        public double Speed;
    }

    public sealed class ScheduleDef
    {
        public string Id, NpcId, Script;
    }

    public sealed class EventDef
    {
        public string Id, Trigger, Staging, RepeatPolicy, When, Script, Effects;
        public int CostMinutes;
        public Dictionary<string, string> Forks = new Dictionary<string, string>();
    }

    public sealed class JoinDef
    {
        public string ActorId;
        public Spot JoinAt;
    }

    public sealed class RecipeDef
    {
        public string EventId;
        public List<JoinDef> Join = new List<JoinDef>();
    }

    /// <summary>A condition read at cast time. Kind is statAtLeast, levelAtLeast or itemCountAtLeast; Label is the player-facing name.</summary>
    public sealed class RequirementDef
    {
        public string Kind, Id, Label;
        public int Min;
    }

    public sealed class SpellDef
    {
        public string Id, Name;
        /// <summary>2 to 5 runes, each 1 to 4. Unique across the bundle; one array may be the start of another.</summary>
        public int[] Runes = new int[0];
        public List<RequirementDef> Requirements = new List<RequirementDef>();
        public bool AllowQuickCast, UnlocksQuickCast;
        /// <summary>Tiles from the player to a target.</summary>
        public double Range = 3;
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReactionKind { Protect, Combo, React }

    public sealed class TagAdd
    {
        public string Tag;
        /// <summary>How long the tag stays, in game seconds. Null keeps it until the targets reset.</summary>
        public double? Seconds;
    }

    /// <summary>
    /// One row of the ordered reaction table. A cast takes the first row whose Spell matches ("*" is every spell) and whose Needs
    /// are all on the target, checking Protect rows, then Combo rows, then React rows, each in file order.
    /// </summary>
    public sealed class ReactionDef
    {
        public string Id, Spell, Observe, Context;
        public ReactionKind Kind;
        public List<string> Needs = new List<string>();
        public List<TagAdd> Add = new List<TagAdd>();
        public List<string> Remove = new List<string>();
    }

    public sealed class MagicTargetDef
    {
        public string Id, Name, MapId;
        public double X, Y;
        public List<string> Tags = new List<string>();
    }

    /// <summary>One call of a content call string: <c>!name arg "quoted arg"</c>.</summary>
    public sealed class Command
    {
        public string Name;
        public List<string> Args = new List<string>();
        public bool Negated;

        public static List<Command> Parse(string text)
        {
            var list = new List<Command>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var tokens = new List<string>();
            var token = new StringBuilder();
            bool quoted = false, inToken = false;
            foreach (var c in text + "/")
            {
                if (c == '"') { quoted = !quoted; inToken = true; continue; }
                if (!quoted && (c == ' ' || c == '/'))
                {
                    if (inToken) tokens.Add(token.ToString());
                    token.Clear();
                    inToken = false;
                    if (c == '/' && tokens.Count > 0)
                    {
                        var negated = tokens[0].StartsWith("!", StringComparison.Ordinal);
                        list.Add(new Command { Name = negated ? tokens[0].Substring(1) : tokens[0], Args = tokens.Skip(1).ToList(), Negated = negated });
                        tokens = new List<string>();
                    }
                    continue;
                }
                token.Append(c);
                inToken = true;
            }
            return list;
        }
    }

    /// <summary>The runner bundle and staging recipes as written. Not validated here; Tests/Core checks every shipped bundle (R3).</summary>
    public sealed class Content
    {
        public List<VariableDef> Variables = new List<VariableDef>();
        public List<MapDef> Maps = new List<MapDef>();
        public List<DestinationDef> Destinations = new List<DestinationDef>();
        public List<NpcDef> Npcs = new List<NpcDef>();
        public List<ScheduleDef> Schedules = new List<ScheduleDef>();
        public List<EventDef> Events = new List<EventDef>();
        public List<SpellDef> Spells = new List<SpellDef>();
        public List<ReactionDef> Reactions = new List<ReactionDef>();
        public List<MagicTargetDef> MagicTargets = new List<MagicTargetDef>();
        [JsonIgnore] public List<RecipeDef> Recipes = new List<RecipeDef>();
        /// <summary>Where the player sleeps and where every new day starts.</summary>
        public Spot Bed;

        public static Content Load(string dir)
        {
            return Parse(File.ReadAllText(Path.Combine(dir, "runner-bundle.json"), Encoding.UTF8),
                File.ReadAllText(Path.Combine(dir, "staging-recipes.json"), Encoding.UTF8));
        }

        public static Content Parse(string bundleJson, string recipesJson)
        {
            var content = JsonConvert.DeserializeObject<Content>(bundleJson);
            content.Recipes = JsonConvert.DeserializeObject<List<RecipeDef>>(recipesJson);
            return content;
        }

        public MapDef Map(string id) => Maps.First(m => m.Id == id);
        public DestinationDef Destination(string id) => Destinations.First(d => d.Id == id);
        public NpcDef Npc(string id) => Npcs.First(n => n.Id == id);
        public EventDef Event(string id) => Events.First(e => e.Id == id);
        public RecipeDef Recipe(string eventId) => Recipes.First(r => r.EventId == eventId);
        public VariableDef Variable(string id) => Variables.First(v => v.Id == id);
        public SpellDef Spell(string id) => Spells.FirstOrDefault(s => s.Id == id);
    }
}
