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
        /// <summary>The player-facing name shown on the map screen; null falls back to "이름 없는 구역".</summary>
        public string Name;
        public int Width, Height;
        public List<int[]> Blocked = new List<int[]>();
        public List<PortalDef> Portals = new List<PortalDef>();
        /// <summary>Cells the current arrangement of the player's furniture blocks, on top of Blocked. Set by Furniture, never read from content.</summary>
        [JsonIgnore] public HashSet<(int X, int Y)> Furnished = new HashSet<(int X, int Y)>();
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
        public List<EventDef> Events = new List<EventDef>();
        public List<SpellDef> Spells = new List<SpellDef>();
        public List<ReactionDef> Reactions = new List<ReactionDef>();
        public List<MagicTargetDef> MagicTargets = new List<MagicTargetDef>();
        public List<ItemDef> Items = new List<ItemDef>();
        /// <summary>Rooms whose furniture the player may move, and which pieces.</summary>
        public List<ArrangeableDef> Arrangeable = new List<ArrangeableDef>();
        [JsonIgnore] public List<RecipeDef> Recipes = new List<RecipeDef>();
        /// <summary>Daily requests, read from quests.json. A bundle without that file has none.</summary>
        [JsonIgnore] public List<QuestDef> Quests = new List<QuestDef>();
        /// <summary>Each NPC's day, read from routines.json as written (per NPC id, steps in file order). A bundle without that file has none; its NPCs stay at home. Use Routine for the steps that run.</summary>
        [JsonIgnore] public Dictionary<string, List<RoutineStep>> Routines = new Dictionary<string, List<RoutineStep>>();
        /// <summary>Player-facing tag names, read from ui-text.json ("tags"). A bundle without that file shows tag ids.</summary>
        [JsonIgnore] public Dictionary<string, string> TagNames = new Dictionary<string, string>();
        /// <summary>Where the player sleeps and where every new day starts.</summary>
        public Spot Bed;

        public static Content Load(string dir)
        {
            string Optional(string file) => File.Exists(Path.Combine(dir, file)) ? File.ReadAllText(Path.Combine(dir, file), Encoding.UTF8) : null;
            return Parse(File.ReadAllText(Path.Combine(dir, "runner-bundle.json"), Encoding.UTF8),
                File.ReadAllText(Path.Combine(dir, "staging-recipes.json"), Encoding.UTF8),
                Optional("quests.json"), Optional("ui-text.json"), Optional("routines.json"));
        }

        public static Content Parse(string bundleJson, string recipesJson, string questsJson = null, string uiTextJson = null, string routinesJson = null)
        {
            var content = JsonConvert.DeserializeObject<Content>(bundleJson);
            content.Recipes = JsonConvert.DeserializeObject<List<RecipeDef>>(recipesJson);
            content.Quests = string.IsNullOrWhiteSpace(questsJson) ? new List<QuestDef>() : JsonConvert.DeserializeObject<List<QuestDef>>(questsJson) ?? new List<QuestDef>();
            var tags = string.IsNullOrWhiteSpace(uiTextJson) ? null : JObject.Parse(uiTextJson)["tags"];
            content.TagNames = tags?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
            content.Routines = string.IsNullOrWhiteSpace(routinesJson) ? new Dictionary<string, List<RoutineStep>>() : JsonConvert.DeserializeObject<Dictionary<string, List<RoutineStep>>>(routinesJson) ?? new Dictionary<string, List<RoutineStep>>();
            foreach (var step in content.Routines.Values.Where(steps => steps != null).SelectMany(steps => steps).Where(step => step != null))
            {
                step.Minute = Clock.ParseTime(step.At);
                if (string.IsNullOrEmpty(step.Action)) step.Action = Actor.Idle;
            }
            Furniture.ApplyDefaults(content);
            return content;
        }

        public MapDef Map(string id) => Maps.First(m => m.Id == id);
        public DestinationDef Destination(string id) => Destinations.First(d => d.Id == id);
        public NpcDef Npc(string id) => Npcs.First(n => n.Id == id);
        public EventDef Event(string id) => Events.First(e => e.Id == id);
        public RecipeDef Recipe(string eventId) => Recipes.First(r => r.EventId == eventId);
        public VariableDef Variable(string id) => Variables.First(v => v.Id == id);
        public SpellDef Spell(string id) => Spells.FirstOrDefault(s => s.Id == id);
        public ItemDef Item(string id) => Items.FirstOrDefault(i => i.Id == id);
        /// <summary>
        /// The steps of the NPC's day that can run, earliest first (steps at the same minute keep file order). A step with an unreadable
        /// time or a place that is not "home" or a destination is dropped; Tests/Core reports it for every shipped bundle.
        /// </summary>
        public List<RoutineStep> Routine(string npcId)
        {
            return !Routines.TryGetValue(npcId, out var steps) || steps == null ? new List<RoutineStep>()
                : steps.Where(s => s != null && s.Minute >= 0 && (s.Place == RoutineStep.Home || Destinations.Any(d => d.Id == s.Place))).OrderBy(s => s.Minute).ToList();
        }

        public QuestDef Quest(string id) => Quests.FirstOrDefault(q => q.Id == id);
        public string TagName(string id) => TagNames.TryGetValue(id, out var name) ? name : id;
        public string MapName(string id) => Maps.FirstOrDefault(m => m.Id == id)?.Name ?? "이름 없는 구역";
    }
}
