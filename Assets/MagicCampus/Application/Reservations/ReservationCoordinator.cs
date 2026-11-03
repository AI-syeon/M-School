using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Application.Reservations
{
    public sealed class ReservationCoordinatorOptions
    {
        public static readonly ReservationCoordinatorOptions Default = new ReservationCoordinatorOptions(3, 2, 30, "reservation:", "reservation.");

        public int MaxAutomaticAttempts { get; }
        public double BackoffBaseSeconds { get; }
        public double BackoffMaxSeconds { get; }
        public string OwnerTokenPrefix { get; }
        public string TransactionPrefix { get; }

        public ReservationCoordinatorOptions(int maxAutomaticAttempts, double backoffBaseSeconds, double backoffMaxSeconds, string ownerTokenPrefix, string transactionPrefix)
        {
            if (maxAutomaticAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAutomaticAttempts));
            if (double.IsNaN(backoffBaseSeconds) || double.IsInfinity(backoffBaseSeconds) || backoffBaseSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(backoffBaseSeconds));
            if (double.IsNaN(backoffMaxSeconds) || double.IsInfinity(backoffMaxSeconds) || backoffMaxSeconds < backoffBaseSeconds) throw new ArgumentOutOfRangeException(nameof(backoffMaxSeconds));
            if (string.IsNullOrEmpty(ownerTokenPrefix)) throw new ArgumentException("required", nameof(ownerTokenPrefix));
            if (string.IsNullOrEmpty(transactionPrefix)) throw new ArgumentException("required", nameof(transactionPrefix));
            MaxAutomaticAttempts = maxAutomaticAttempts;
            BackoffBaseSeconds = backoffBaseSeconds;
            BackoffMaxSeconds = backoffMaxSeconds;
            OwnerTokenPrefix = ownerTokenPrefix;
            TransactionPrefix = transactionPrefix;
        }
    }

    /// <summary>
    /// Starts Accepted/Ready reservations through the event runner in candidate order, one at a time. It never accepts.
    /// A failed start leaves the reservation untouched; automatic retries are bounded (attempts and exponential backoff),
    /// after which only an explicit Retry with a fresh input sequence starts it again.
    /// </summary>
    public sealed class ReservationCoordinator : IRuntimeTick
    {
        private sealed class Failure
        {
            public int Count;
            public bool Exhausted;
            public double NextAt;
        }

        private sealed class InFlight
        {
            public string EventId;
            public IHandle<EventCompletion> Handle;
            public long StartedTick;
            public bool Explicit;
        }

        private readonly IStateReader state;
        private readonly IReservationService reservations;
        private readonly IEventRunner runner;
        private readonly Func<bool> startGate;
        private readonly ReservationCoordinatorOptions options;
        private readonly Dictionary<string, Failure> failures = new Dictionary<string, Failure>(StringComparer.Ordinal);

        private InFlight inFlight;
        private double now;
        private long ticks;
        private long attempts;
        private long lastExplicitSequence = -1;

        /// <param name="startGate">Optional; automatic starts only happen while it returns true (for example no foreground scene).</param>
        public ReservationCoordinator(IStateReader state, IReservationService reservations, IEventRunner runner, Func<bool> startGate, ReservationCoordinatorOptions options = null)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.startGate = startGate;
            this.options = options ?? ReservationCoordinatorOptions.Default;
        }

        public Result LastResult { get; private set; }
        public string InFlightEventId => inFlight == null ? null : inFlight.EventId;

        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (double.IsNaN(unpausedSeconds) || double.IsInfinity(unpausedSeconds) || unpausedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(unpausedSeconds));
            ticks++;
            now += unpausedSeconds;

            if (inFlight != null)
            {
                // A runner handle is observed on a later tick than the one that started it.
                if (ticks > inFlight.StartedTick && inFlight.Handle.Status.IsTerminal()) Observe();
                return;
            }

            if (startGate != null && !startGate()) return;

            var candidates = reservations.Candidates(state.Current);
            Prune(candidates);
            foreach (var candidate in candidates)
            {
                if (failures.TryGetValue(candidate.EventId, out var failure) && (failure.Exhausted || now < failure.NextAt)) continue;
                Start(candidate.EventId, attempts + 1, false);
                return;
            }
        }

        /// <summary>Explicit player/interaction retry. Each call must carry a new, increasing input sequence.</summary>
        public Result Retry(string eventId, long inputSequence)
        {
            var revision = state.Current.Revision;
            if (inputSequence < 0) throw new ArgumentOutOfRangeException(nameof(inputSequence));
            if (inputSequence <= lastExplicitSequence)
                return Refuse(ResultCodes.ConditionFalse, revision, "duplicate input sequence ignored", eventId, "sequence > " + lastExplicitSequence, inputSequence.ToString());
            lastExplicitSequence = inputSequence;

            if (inFlight != null)
                return Refuse(ResultCodes.ResourceBusy, revision, "another reservation start is in flight", inFlight.EventId, "idle", inFlight.EventId);

            var found = false;
            foreach (var candidate in reservations.Candidates(state.Current))
                if (string.Equals(candidate.EventId, eventId, StringComparison.Ordinal)) { found = true; break; }
            if (!found)
                return Refuse(ResultCodes.ConditionFalse, revision, "event is not an accepted reservation", eventId, "Accepted or Ready candidate", eventId ?? "null");

            failures.Remove(eventId);
            Start(eventId, inputSequence, true);
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, revision, null);
        }

        public void CancelInFlight(string reason)
        {
            if (inFlight != null) inFlight.Handle.Cancel(reason ?? "cancelled");
        }

        private void Start(string eventId, long inputSequence, bool isExplicit)
        {
            attempts++;
            var transaction = new TransactionId(options.TransactionPrefix + eventId + ".a" + attempts);
            // Automatic attempts and user input sequences are independent producers. Keep their replay scopes distinct.
            var owner = options.OwnerTokenPrefix + (isExplicit ? "explicit:" : "automatic:") + eventId;
            var request = new EventStartRequest(eventId, owner, inputSequence, transaction);
            var handle = runner.Start(request);
            if (handle == null) throw new InvalidOperationException("event runner returned no handle");
            inFlight = new InFlight { EventId = eventId, Handle = handle, StartedTick = ticks, Explicit = isExplicit };
        }

        private void Observe()
        {
            var done = inFlight;
            inFlight = null;
            var result = done.Handle.Terminal;
            LastResult = result;
            if (result.Status == OperationStatus.Succeeded)
            {
                failures.Remove(done.EventId);
                return;
            }

            if (!failures.TryGetValue(done.EventId, out var failure))
            {
                failure = new Failure();
                failures[done.EventId] = failure;
            }
            failure.Count++;
            if (done.Explicit)
            {
                failure.Exhausted = true;
                return;
            }
            if (failure.Count >= options.MaxAutomaticAttempts) failure.Exhausted = true;
            var delay = options.BackoffBaseSeconds * Math.Pow(2, failure.Count - 1);
            failure.NextAt = now + Math.Min(delay, options.BackoffMaxSeconds);
        }

        private void Prune(IReadOnlyList<StartCandidate> candidates)
        {
            if (failures.Count == 0) return;
            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in candidates) live.Add(c.EventId);
            var stale = new List<string>();
            foreach (var key in failures.Keys) if (!live.Contains(key)) stale.Add(key);
            foreach (var key in stale) failures.Remove(key);
        }

        private static Result Refuse(string code, long revision, string message, string entity, string expected, string actual)
        {
            return new Result(OperationStatus.Failed, code, revision, new[] { ReservationRules.Diag(code, message, AppointmentCodec.Pointer, entity, expected, actual) });
        }
    }
}
