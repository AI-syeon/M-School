using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.Transactions.Core
{
    /// <summary>
    /// Registry with a path-prefix ownership allowlist. Registration refuses duplicate ids, malformed pointers and any
    /// ancestor/descendant/equal ownership overlap between participants. After Seal() nothing can be registered.
    /// Owned slices are parsed and copied at registration time, so later changes to the participant's list have no effect.
    /// </summary>
    public sealed class StateParticipantRegistry
    {
        public sealed class Registration
        {
            public IStateParticipant Participant { get; }
            public IReadOnlyList<JsonPointer> Owned { get; }

            public Registration(IStateParticipant participant, JsonPointer[] owned)
            {
                Participant = participant;
                Owned = Array.AsReadOnly(owned);
            }
        }

        private readonly Dictionary<string, Registration> byId = new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, JsonPointer>> claims = new List<KeyValuePair<string, JsonPointer>>();
        private bool sealedFlag;

        public bool IsSealed => sealedFlag;

        public void Seal() { sealedFlag = true; }

        public Result Register(IStateParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (sealedFlag) return Refuse(ResultCodes.ContentInvalid, "REGISTRY_SEALED", "registry is sealed", "");

            var id = participant.ParticipantId;
            if (!ValidId(id)) return Refuse(ResultCodes.ContentInvalid, "PARTICIPANT_ID_INVALID", "participant id must be non-empty without whitespace or control characters", id ?? "");
            if (byId.ContainsKey(id)) return Refuse(ResultCodes.HandleDuplicate, "PARTICIPANT_DUPLICATE", "participant id already registered", id);

            var slices = participant.OwnedSlices;
            if (slices == null || slices.Count == 0) return Refuse(ResultCodes.ContentInvalid, "OWNED_SLICES_EMPTY", "participant must own at least one slice", id);

            var pointers = new List<JsonPointer>();
            foreach (var s in slices)
            {
                if (!JsonPointer.TryParse(s, out var pointer, out var error))
                    return Refuse(ResultCodes.ContentInvalid, "OWNED_SLICE_INVALID", error, id + " " + (s ?? ""));
                foreach (var claim in claims)
                    if (claim.Value.Overlaps(pointer))
                        return Refuse(ResultCodes.SliceOverlap, "OWNERSHIP_OVERLAP", id + " overlaps " + claim.Key + " at " + claim.Value.Text, s);
                pointers.Add(pointer);
            }

            foreach (var p in pointers) claims.Add(new KeyValuePair<string, JsonPointer>(id, p));
            byId.Add(id, new Registration(participant, pointers.ToArray()));
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }

        /// <summary>Null when unregistered; the store then refuses the whole plan with ResultCodes.Unregistered.</summary>
        public Registration Find(string participantId)
        {
            if (participantId == null) return null;
            return byId.TryGetValue(participantId, out var r) ? r : null;
        }

        private static Result Refuse(string code, string diagCode, string message, string actual)
            => new Result(OperationStatus.Failed, code, 0, new[] { new Diagnostic(diagCode, message, null, "", actual) });

        private static bool ValidId(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            foreach (var c in v)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            return true;
        }
    }
}
