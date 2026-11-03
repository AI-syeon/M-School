using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    /// <summary>Registered day-boundary consumers, always enumerated in ordinal consumer-id order.</summary>
    public sealed class DayBoundaryConsumerRegistry
    {
        private readonly SortedDictionary<string, IDayBoundaryConsumer> byId =
            new SortedDictionary<string, IDayBoundaryConsumer>(StringComparer.Ordinal);

        public int Count => byId.Count;

        public IReadOnlyList<IDayBoundaryConsumer> Consumers
        {
            get
            {
                var copy = new IDayBoundaryConsumer[byId.Count];
                byId.Values.CopyTo(copy, 0);
                return Array.AsReadOnly(copy);
            }
        }

        public IDayBoundaryConsumer Find(string consumerId)
        {
            if (consumerId == null) return null;
            return byId.TryGetValue(consumerId, out var consumer) ? consumer : null;
        }

        public Result Register(IDayBoundaryConsumer consumer)
        {
            if (consumer == null) return Refuse("CONSUMER_NULL", "consumer is required", string.Empty);
            var id = consumer.ConsumerId;
            if (!IsValidId(id)) return Refuse("CONSUMER_ID_INVALID", "consumer id must be non-empty without whitespace or control characters", id ?? "<null>");
            if (byId.ContainsKey(id)) return Refuse("CONSUMER_DUPLICATE", "consumer id is already registered", id);
            byId.Add(id, consumer);
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, 0, null);
        }

        private static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var c in id)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            return true;
        }

        private static Result Refuse(string code, string message, string actual)
        {
            return new Result(OperationStatus.Failed, ResultCodes.ContentInvalid, 0,
                new[] { TimeDiagnostics.Make(code, string.Empty, message, string.Empty, actual) });
        }
    }
}
