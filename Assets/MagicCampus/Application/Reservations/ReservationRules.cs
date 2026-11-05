using MagicCampus.Contracts;

namespace MagicCampus.Application.Reservations
{
    internal static class ReservationRules
    {
        internal const string Pointer = "/appointments";

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
    }
}
