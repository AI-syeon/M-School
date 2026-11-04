using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Transactions.Effects
{
    public enum EffectKind { Set, Complete, CurrencyAdd, EventComplete }

    /// <summary>
    /// One typed event-effects operation inside a StatePlan. set, complete and currency.add are built by EffectPlanner after it validated
    /// the compiled call, event.complete by EventCommitPlanner. EventEffectsParticipant applies the typed fields without parsing;
    /// Payload only mirrors them for observers that read plans generically (the reservation participant reads event.complete).
    /// </summary>
    public sealed class EffectChange : PlannedChange
    {
        public EffectKind Kind { get; }
        /// <summary>Set: variable id. Complete: completion id. EventComplete: event id.</summary>
        public string Id { get; }
        /// <summary>Set: the accepted value.</summary>
        public DataNode Value { get; }
        /// <summary>CurrencyAdd: positive wallet-sized amount.</summary>
        public long Crowns { get; }
        /// <summary>EventComplete: shared completion key.</summary>
        public string CompletionKey { get; }
        /// <summary>EventComplete: repeat policy the plan claims.</summary>
        public RepeatPolicy Policy { get; }

        private EffectChange(string operation, EffectKind kind, DataNode payload, string id, DataNode value, long crowns, string completionKey, RepeatPolicy policy)
            : base(EffectOperations.ParticipantId, operation, payload)
        {
            Kind = kind;
            Id = id;
            Value = value;
            Crowns = crowns;
            CompletionKey = completionKey;
            Policy = policy;
        }

        internal static EffectChange Set(string variableId, DataNode value)
        {
            return new EffectChange(EffectOperations.Set, EffectKind.Set, Obj(Pair("variable", DataNode.FromString(variableId)), Pair("value", value)), variableId, value, 0, null, default(RepeatPolicy));
        }

        internal static EffectChange Complete(string id)
        {
            return new EffectChange(EffectOperations.Complete, EffectKind.Complete, Obj(Pair("id", DataNode.FromString(id))), id, null, 0, null, default(RepeatPolicy));
        }

        internal static EffectChange CurrencyAdd(long crowns)
        {
            return new EffectChange(EffectOperations.CurrencyAdd, EffectKind.CurrencyAdd, Obj(Pair("amountCrowns", DataNode.FromInteger(crowns))), null, null, crowns, null, default(RepeatPolicy));
        }

        /// <summary>The event ledger entry. The participant checks it against the catalog and the plan completion key.</summary>
        public static EffectChange EventComplete(string eventId, string completionKey, RepeatPolicy policy)
        {
            var payload = Obj(Pair("eventId", DataNode.FromString(eventId)), Pair("completionKey", DataNode.FromString(completionKey)),
                Pair("repeatPolicy", DataNode.FromString(policy == RepeatPolicy.Once ? EffectOperations.Once : EffectOperations.Practice)));
            return new EffectChange(EffectOperations.EventComplete, EffectKind.EventComplete, payload, eventId, null, 0, completionKey, policy);
        }

        private static KeyValuePair<string, DataNode> Pair(string key, DataNode value)
        {
            return new KeyValuePair<string, DataNode>(key, value);
        }

        private static DataNode Obj(params KeyValuePair<string, DataNode>[] members)
        {
            return DataNode.FromObject(members);
        }
    }
}
