using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
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
        [JsonIgnore] public List<RecipeDef> Recipes = new List<RecipeDef>();

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
    }
}
