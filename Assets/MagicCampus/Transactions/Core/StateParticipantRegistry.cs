using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.Transactions.Core
{
    /// <summary>
    /// Registry with a frozen path-prefix ownership allowlist. Registration refuses duplicate ids, malformed pointers and any
    /// ancestor/descendant/equal ownership overlap between participants. After Seal() nothing can be registered.
    /// Find returns a wrapper whose id and owned slices were copied at registration time.
    /// </summary>
    public sealed class StateParticipantRegistry : IStateParticipantRegistry
    {
        private sealed class FrozenParticipant : IStateParticipant
        {
            private readonly IStateParticipant inner;
            public string ParticipantId { get; }
            public IReadOnlyList<string> OwnedSlices { get; }

            public FrozenParticipant(IStateParticipant inner, string id, string[] owned)
            {
                this.inner = inner;
                ParticipantId = id;
                OwnedSlices = Array.AsReadOnly(owned);
            }

            public Outcome<StateReplacement> Prepare(StateSnapshot basis, IStatePlan plan) => inner.Prepare(basis, plan);
        }

        private readonly Dictionary<string, FrozenParticipant> byId = new Dictionary<string, FrozenParticipant>(StringComparer.Ordinal);
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

            var copy = new List<string>();
            var pointers = new List<JsonPointer>();
            foreach (var s in slices)
            {
                if (!JsonPointer.TryParse(s, out var pointer, out var error))
                    return Refuse(ResultCodes.ContentInvalid, "OWNED_SLICE_INVALID", error, id + " " + (s ?? ""));
                foreach (var claim in claims)
                    if (claim.Value.Overlaps(pointer))
                        return Refuse(ResultCodes.SliceOverlap, "OWNERSHIP_OVERLAP", id + " overlaps " + claim.Key + " at " + claim.Value.Text, s);
                copy.Add(s);
                pointers.Add(pointer);
            }

            foreach (var p in pointers) claims.Add(new KeyValuePair<string, JsonPointer>(id, p));
            byId.Add(id, new FrozenParticipant(participant, id, copy.ToArray()));
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }

        public IStateParticipant Find(string participantId)
        {
            if (participantId == null) return null;
            return byId.TryGetValue(participantId, out var p) ? p : null;
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
