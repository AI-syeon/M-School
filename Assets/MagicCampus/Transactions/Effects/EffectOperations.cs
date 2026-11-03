namespace MagicCampus.Transactions.Effects
{
    /// <summary>Fixed internal plan format from ADR 0002. These are commands between registered participants, not authoring functions.</summary>
    public static class EffectOperations
    {
        public const string ParticipantId = "event-effects";
        public const string Set = "set", Complete = "complete", CurrencyAdd = "currency.add", EventComplete = "event.complete";
        public const string ClockParticipant = "clock", ClockAdvance = "advance";
        public const string WorldParticipant = "world", WorldProject = "project";
        public const string ReservationsParticipant = "reservations", ReservationsComplete = "complete";
        public const string Once = "once", Practice = "practice";
    }
}
