using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MagicCampus.Core
{
    /// <summary>"420 home / 720 dest.notice / 1080 home": at each minute the NPC walks to that destination.</summary>
    public static class Schedule
    {
        public static List<(int Minute, string DestinationId)> Entries(Content content, string npcId)
        {
            var npc = content.Npc(npcId);
            var script = content.Schedules.First(s => s.NpcId == npcId).Script;
            return Command.Parse(script)
                .Select(c => (int.Parse(c.Name, CultureInfo.InvariantCulture), c.Args[0] == "home" ? npc.Home : c.Args[0]))
                .OrderBy(e => e.Item1)
                .ToList();
        }

        /// <summary>The latest entry at or before the minute; before the first entry, the last entry of the previous day.</summary>
        public static string DestinationAt(Content content, string npcId, int minute)
        {
            var entries = Entries(content, npcId);
            var current = entries.LastOrDefault(e => e.Minute <= minute);
            return current.DestinationId ?? entries[entries.Count - 1].DestinationId;
        }

        public static void PlaceAll(Content content, IEnumerable<Actor> npcs, int minute)
        {
            foreach (var npc in npcs)
            {
                var destination = content.Destination(DestinationAt(content, npc.Id, minute));
                World.Place(npc, destination.Position, destination.Facing);
            }
        }

        /// <summary>Starts the walk of every entry whose minute lies in (after, upTo].</summary>
        public static void Run(Content content, IEnumerable<Actor> npcs, int after, int upTo)
        {
            foreach (var npc in npcs)
                foreach (var entry in Entries(content, npc.Id).Where(e => e.Minute > after && e.Minute <= upTo))
                    World.WalkTo(content, npc, content.Destination(entry.DestinationId));
        }
    }
}
