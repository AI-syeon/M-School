using MagicCampus.Contracts;

namespace MagicCampus.Application.Reservations
{
    public static class CandidatePriority
    {
        public const int Resolution = 400, Relationship = 300, Event = 200, General = 100, Schedule = 10;
    }

    public enum ReservationStatus { Offered, Accepted, Ready }

    public sealed class StartCandidate
    {
        public string EventId { get; }
        public string ReservationId { get; }   // empty when not reservation based
        public ReservationStatus Status { get; }
        public int Priority { get; }
        public long ReservedAt { get; }
        public long Sequence { get; }

        public StartCandidate(string eventId, string reservationId, ReservationStatus status, int priority, long reservedAt, long sequence)
        {
            EventId = Ids.Require(eventId, nameof(eventId)); ReservationId = Ids.Opt(reservationId); Status = status; Priority = priority;
            ReservedAt = PortGuard.Min(reservedAt, 0, nameof(reservedAt)); Sequence = PortGuard.Min(sequence, 0, nameof(sequence));
        }
    }
}
