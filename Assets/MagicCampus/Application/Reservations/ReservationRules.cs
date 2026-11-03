using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Application.Reservations
{
    /// <summary>Reads the shared completion ledger of a snapshot. The save layout belongs to the effects/state layer; this port keeps reservations independent of it.</summary>
    public interface ICompletionLedger
    {
        bool IsCompleted(StateSnapshot snapshot, CompletionKey key);
    }

    internal static class ReservationRules
    {
        /// <summary>Shared completion key of an event; events without an explicit key complete under their own id.</summary>
        internal static CompletionKey EffectiveKey(CompiledEvent ev)
        {
            return ev.Completion ?? new CompletionKey(ev.Id);
        }

        internal static bool IsCompleted(CompiledEvent ev, StateSnapshot snapshot, ICompletionLedger ledger)
        {
            if (ev.Repeat == RepeatPolicy.Practice) return false;
            return ledger.IsCompleted(snapshot, EffectiveKey(ev));
        }

        internal static int Priority(CompiledEvent ev)
        {
            if (ev.Raw.TryGet("category", out var category) && category.Kind == DataKind.String)
            {
                switch (category.Text)
                {
                    case "resolution": return CandidatePriority.Resolution;
                    case "relationship": return CandidatePriority.Relationship;
                    case "festival": return CandidatePriority.Event;
                    case "event": return CandidatePriority.Event;
                    case "schedule": return CandidatePriority.Schedule;
                }
            }
            return CandidatePriority.General;
        }

        internal static Diagnostic Diag(string code, string message, string pointer, string entityId, string expected, string actual)
        {
            return new Diagnostic(code, message, new SourceLocation("save", pointer, entityId, "", 0), expected, actual);
        }

        internal static bool TryString(DataNode obj, string key, out string value)
        {
            value = null;
            if (obj == null || obj.Kind != DataKind.Object || !obj.TryGet(key, out var n) || n.Kind != DataKind.String) return false;
            value = n.Text;
            return true;
        }

        internal static bool TryInteger(DataNode obj, string key, out long value)
        {
            value = 0;
            if (obj == null || obj.Kind != DataKind.Object || !obj.TryGet(key, out var n) || n.Kind != DataKind.Integer) return false;
            value = n.Integer;
            return true;
        }
    }

    internal sealed class AppointmentRow
    {
        internal AppointmentRow(string id, ReservationStatus status, long acceptedAt, int index)
        {
            Id = id;
            Status = status;
            AcceptedAt = acceptedAt;
            Index = index;
        }

        public string Id { get; }
        public ReservationStatus Status { get; }
        public long AcceptedAt { get; }
        public int Index { get; }

        internal AppointmentRow With(ReservationStatus status, long acceptedAt)
        {
            return new AppointmentRow(Id, status, acceptedAt, Index);
        }

        internal DataNode ToNode()
        {
            return DataNode.FromObject(new[]
            {
                new KeyValuePair<string, DataNode>("id", DataNode.FromString(Id)),
                new KeyValuePair<string, DataNode>("status", DataNode.FromString(AppointmentCodec.StatusName(Status))),
                new KeyValuePair<string, DataNode>("acceptedAt", DataNode.FromInteger(AcceptedAt))
            });
        }
    }

    /// <summary>Strict reader/writer for the closed /appointments schema: {id, status, acceptedAt}.</summary>
    internal static class AppointmentCodec
    {
        internal const string SliceName = "appointments";
        internal const string Pointer = "/appointments";

        internal static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var segments = 0;
            var start = true;
            foreach (var c in id)
            {
                if (start)
                {
                    if (c < 'a' || c > 'z') return false;
                    start = false;
                    segments++;
                    continue;
                }
                if (c == '.') { start = true; continue; }
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return !start && segments >= 2;
        }

        internal static string StatusName(ReservationStatus status)
        {
            switch (status)
            {
                case ReservationStatus.Offered: return "Offered";
                case ReservationStatus.Accepted: return "Accepted";
                default: return "Ready";
            }
        }

        internal static bool TryStatus(string text, out ReservationStatus status)
        {
            status = ReservationStatus.Offered;
            switch (text)
            {
                case "Offered": status = ReservationStatus.Offered; return true;
                case "Accepted": status = ReservationStatus.Accepted; return true;
                case "Ready": status = ReservationStatus.Ready; return true;
                default: return false;
            }
        }

        /// <summary>Returns every valid row; each invalid row or slice problem adds an explicit diagnostic.</summary>
        internal static List<AppointmentRow> Read(StateSnapshot snapshot, List<Diagnostic> diagnostics)
        {
            var rows = new List<AppointmentRow>();
            var slice = snapshot.Slice(SliceName);
            if (slice == null)
            {
                diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "appointments slice is missing", Pointer, "", "array", "absent"));
                return rows;
            }
            if (slice.Kind != DataKind.List)
            {
                diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "appointments must be an array", Pointer, "", "array", slice.Kind.ToString()));
                return rows;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < slice.Items.Count; i++)
            {
                var item = slice.Items[i];
                var at = Pointer + "/" + i;
                if (item.Kind != DataKind.Object)
                {
                    diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "appointment must be an object", at, "", "object", item.Kind.ToString()));
                    continue;
                }

                var ok = true;
                foreach (var member in item.Members)
                {
                    if (member.Key == "id" || member.Key == "status" || member.Key == "acceptedAt") continue;
                    diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "unknown appointment field", at + "/" + member.Key, "", "id, status, acceptedAt", member.Key));
                    ok = false;
                }

                var id = "";
                if (!ReservationRules.TryString(item, "id", out id) || !IsValidId(id))
                {
                    diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "appointment id is missing or malformed", at + "/id", id ?? "", "dotted lowercase id", id ?? "missing"));
                    id = "";
                    ok = false;
                }

                var status = ReservationStatus.Offered;
                if (!ReservationRules.TryString(item, "status", out var statusText) || !TryStatus(statusText, out status))
                {
                    diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "appointment status is invalid", at + "/status", id, "Offered|Accepted|Ready", statusText ?? "missing"));
                    ok = false;
                }

                if (!ReservationRules.TryInteger(item, "acceptedAt", out var acceptedAt) || acceptedAt < 0)
                {
                    diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "acceptedAt must be a non-negative integer minute", at + "/acceptedAt", id, "integer >= 0", "invalid"));
                    ok = false;
                }

                if (ok && !seen.Add(id))
                {
                    diagnostics.Add(ReservationRules.Diag(ResultCodes.ContentInvalid, "duplicate appointment id", at + "/id", id, "unique id", id));
                    ok = false;
                }

                if (ok) rows.Add(new AppointmentRow(id, status, acceptedAt, i));
            }
            return rows;
        }

        internal static DataNode Write(IEnumerable<AppointmentRow> rows)
        {
            var nodes = new List<DataNode>();
            foreach (var row in rows) nodes.Add(row.ToNode());
            return DataNode.FromList(nodes);
        }
    }
}
