using System;
using MagicCampus.Contracts;

namespace MagicCampus.Transactions
{
    /// <summary>Validated opaque id: non-empty, no whitespace/control characters.</summary>
    public readonly struct TransactionId : IEquatable<TransactionId>
    {
        public string Value { get; }
        public TransactionId(string value) { Value = Ids.Require(value, nameof(value)); }
        public bool Equals(TransactionId o) => Value == o.Value;
        public override bool Equals(object obj) => obj is TransactionId t && Equals(t);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Validated key identifying one completion (same rules as TransactionId).</summary>
    public readonly struct CompletionKey : IEquatable<CompletionKey>
    {
        public string Value { get; }
        public CompletionKey(string value) { Value = Ids.Require(value, nameof(value)); }
        public bool Equals(CompletionKey o) => Value == o.Value;
        public override bool Equals(object obj) => obj is CompletionKey k && Equals(k);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value ?? string.Empty;
    }
}
