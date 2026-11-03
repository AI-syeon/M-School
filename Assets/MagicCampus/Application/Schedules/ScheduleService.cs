using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;

namespace MagicCampus.Application.Schedules
{
    /// <summary>
    /// Selects exactly one destination for an NPC from already compiled schedule entries (function name = start minute,
    /// destination argument = destination id or the home keyword). Nothing is reparsed from text and no path is stored:
    /// the latest entry whose start is at or before the minute of day wins, and before the first entry the last entry
    /// of the previous day applies.
    /// </summary>
    public sealed class ScheduleService : IScheduleService
    {
        public const string HomeKeyword = "home";
        public const string DestinationArgument = "destination";
        private const string SourceFile = "runner.json";

        private sealed class Entry
        {
            public int Minute;
            public string Destination;
        }

        private sealed class NpcSchedule
        {
            public string ScheduleId;
            public Entry[] Entries = new Entry[0];
            public readonly List<Diagnostic> Errors = new List<Diagnostic>();
        }

        private readonly IDestinationResolver resolver;
        private readonly Dictionary<string, string> homes = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, NpcSchedule> schedules = new Dictionary<string, NpcSchedule>(StringComparer.Ordinal);
        private readonly List<Diagnostic> globalErrors = new List<Diagnostic>();

        /// <param name="catalog">Supplies the runner root (FindDefinition runner/root) listing active NPCs, their home and the schedule rows.</param>
        /// <param name="compiledSchedules">Compiled entry lists keyed by schedule row id, produced by the catalog compiler.</param>
        public ScheduleService(ICatalog catalog, IReadOnlyDictionary<string, IReadOnlyList<CompiledCall>> compiledSchedules, IDestinationResolver resolver)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (compiledSchedules == null) throw new ArgumentNullException(nameof(compiledSchedules));
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            Index(catalog.FindDefinition("runner", "root"), compiledSchedules);
        }

        public Outcome<Destination> CurrentDestination(string npcId, GameTime now)
        {
            if (string.IsNullOrEmpty(npcId))
                return Outcome<Destination>.Fail(ResultCodes.ContentInvalid, 0, Diag(ResultCodes.ContentInvalid, "npc id is required", "/npcs", "", "npc id", npcId ?? "null"));
            if (globalErrors.Count > 0)
                return Outcome<Destination>.Fail(ResultCodes.ContentInvalid, 0, globalErrors.ToArray());
            if (!homes.ContainsKey(npcId))
                return Outcome<Destination>.Fail(ResultCodes.RefNotFound, 0, Diag(ResultCodes.RefNotFound, "npc is not active in the runner content", "/npcs", npcId, "active npc", npcId));
            if (!schedules.TryGetValue(npcId, out var schedule))
                return Outcome<Destination>.Fail(ResultCodes.RefNotFound, 0, Diag(ResultCodes.RefNotFound, "npc has no schedule row", "/schedules", npcId, "schedule row", "none"));
            if (schedule.Errors.Count > 0)
                return Outcome<Destination>.Fail(ResultCodes.ContentInvalid, 0, schedule.Errors.ToArray());

            var minute = now.MinuteOfDay;
            var chosen = schedule.Entries[schedule.Entries.Length - 1];
            Entry latest = null;
            foreach (var entry in schedule.Entries)
            {
                if (entry.Minute <= minute) latest = entry;
                else break;
            }
            if (latest != null) chosen = latest;

            var destinationId = chosen.Destination;
            if (destinationId == HomeKeyword)
            {
                destinationId = homes[npcId];
                if (string.IsNullOrEmpty(destinationId))
                    return Outcome<Destination>.Fail(ResultCodes.RefNotFound, 0, Diag(ResultCodes.RefNotFound, "npc has no home destination", "/npcs", npcId, "home destination id", "missing"));
            }
            return resolver.Resolve(npcId, destinationId);
        }

        private void Index(DataNode root, IReadOnlyDictionary<string, IReadOnlyList<CompiledCall>> compiled)
        {
            if (root == null || root.Kind != DataKind.Object)
            {
                globalErrors.Add(Diag(ResultCodes.ContentInvalid, "runner root is unavailable", "", "", "object", root == null ? "missing" : root.Kind.ToString()));
                return;
            }

            if (root.TryGet("npcs", out var npcs) && npcs.Kind == DataKind.List)
            {
                foreach (var npc in npcs.Items)
                {
                    if (npc.Kind != DataKind.Object || !npc.TryGet("id", out var id) || id.Kind != DataKind.String) continue;
                    homes[id.Text] = npc.TryGet("home", out var home) && home.Kind == DataKind.String ? home.Text : null;
                }
            }

            if (!root.TryGet("schedules", out var rows) || rows.Kind != DataKind.List) return;
            for (var i = 0; i < rows.Items.Count; i++)
            {
                var row = rows.Items[i];
                var at = "/schedules/" + i;
                if (row.Kind != DataKind.Object
                    || !row.TryGet("id", out var scheduleId) || scheduleId.Kind != DataKind.String
                    || !row.TryGet("npcId", out var npcId) || npcId.Kind != DataKind.String)
                {
                    globalErrors.Add(Diag(ResultCodes.ContentInvalid, "schedule row needs string id and npcId", at, "", "id, npcId", "malformed"));
                    continue;
                }

                if (schedules.TryGetValue(npcId.Text, out var existing))
                {
                    existing.Errors.Add(Diag(ResultCodes.ContentInvalid, "npc has more than one schedule row", at, scheduleId.Text, "one schedule", scheduleId.Text));
                    continue;
                }

                var schedule = new NpcSchedule { ScheduleId = scheduleId.Text };
                schedules.Add(npcId.Text, schedule);
                if (!compiled.TryGetValue(scheduleId.Text, out var calls) || calls == null)
                    schedule.Errors.Add(Diag(ResultCodes.RefNotFound, "no compiled call list for the schedule", at, scheduleId.Text, "compiled schedule", "missing"));
                else
                    Build(schedule, calls);
            }
        }

        private static void Build(NpcSchedule schedule, IReadOnlyList<CompiledCall> calls)
        {
            var entries = new List<Entry>();
            var seen = new HashSet<int>();
            if (calls.Count == 0)
                schedule.Errors.Add(Diag(ResultCodes.ContentInvalid, "schedule has no entries", "/schedules", schedule.ScheduleId, "at least one entry", "0"));

            foreach (var call in calls)
            {
                var name = call.InvocationName;
                if (call.Function.Role != FunctionRole.ScheduleEntry)
                {
                    schedule.Errors.Add(new Diagnostic(ResultCodes.ContentInvalid, "not a schedule entry call", call.Source, "ScheduleEntry", call.Function.Role.ToString()));
                    continue;
                }
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var minute) || minute >= GameTime.MinutesPerDay)
                {
                    schedule.Errors.Add(new Diagnostic(ResultCodes.ContentInvalid, "entry start must be a minute of day", call.Source, "0..1439", name));
                    continue;
                }
                if (!TryDestination(call, out var destination))
                {
                    schedule.Errors.Add(new Diagnostic(ResultCodes.ContentInvalid, "entry needs one destination argument", call.Source, DestinationArgument, "missing"));
                    continue;
                }
                if (!seen.Add(minute))
                {
                    schedule.Errors.Add(new Diagnostic(ResultCodes.ContentInvalid, "two entries start at the same minute", call.Source, "unique minute", name));
                    continue;
                }
                entries.Add(new Entry { Minute = minute, Destination = destination });
            }

            entries.Sort((a, b) => a.Minute.CompareTo(b.Minute));
            schedule.Entries = entries.ToArray();
        }

        private static bool TryDestination(CompiledCall call, out string destination)
        {
            destination = null;
            if (call.Args.TryGetValue(DestinationArgument, out var named))
            {
                if (named.Kind != DataKind.String || named.Text.Length == 0) return false;
                destination = named.Text;
                return true;
            }
            if (call.Args.Count != 1) return false;
            foreach (var pair in call.Args)
            {
                if (pair.Value.Kind != DataKind.String || pair.Value.Text.Length == 0) return false;
                destination = pair.Value.Text;
            }
            return destination != null;
        }

        private static Diagnostic Diag(string code, string message, string pointer, string entity, string expected, string actual)
        {
            return new Diagnostic(code, message, new SourceLocation(SourceFile, pointer, entity, "", 0), expected, actual);
        }
    }
}
