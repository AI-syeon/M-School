using System;
using MagicCampus.Contracts;
using MagicCampus.Runtime;

namespace MagicCampus.Events
{
    public static class EventPhases
    {
        public const string Start = "start", Refused = "refused", Replay = "replay";
        public const string EntryProject = "entry.project", EntryCapture = "entry.capture", EntryNotify = "entry.notify", EntrySave = "entry.save", Stage = "stage";
        public const string Call = "call", ForkEnter = "fork.enter", ForkExit = "fork.exit";
        public const string Commit = "commit", Abort = "abort", Cleanup = "cleanup", Restore = "restore", Done = "done";
    }

    /// <summary>Observation record for tests and diagnostics; not a product UI.</summary>
    public sealed class EventTraceEntry
    {
        public long Tick { get; }
        public long Sequence { get; }
        public long Revision { get; }
        public string EventId { get; }
        public string Call { get; }
        public string Phase { get; }

        public EventTraceEntry(long tick, long sequence, long revision, string eventId, string call, string phase)
        {
            Tick = tick;
            Sequence = sequence;
            Revision = revision;
            EventId = eventId ?? string.Empty;
            Call = call ?? string.Empty;
            Phase = phase ?? string.Empty;
        }
    }

    /// <summary>Dependency and option bag for EventRunner. Every port and RepeatBlocked are required; options have defaults. EventRunner copies it at construction.</summary>
    public sealed class EventRunnerDependencies
    {
        public ICatalogProvider Catalog { get; set; }
        public IConditionEvaluator Conditions { get; set; }
        public ILeaseService Leases { get; set; }
        public IPauseService Pauses { get; set; }
        public IMoveService Moves { get; set; }
        public IDestinationResolver Destinations { get; set; }
        public IPresentationPort Presentation { get; set; }
        public IPresentationLifetime PresentationLifetime { get; set; }
        public TickClock Clock { get; set; }
        public IStateStore Store { get; set; }
        /// <summary>Commits the success of one event under the given transaction: its rules, time cost, the live actor positions and its reservation row, captured and committed in this one call.</summary>
        public Func<CompiledEvent, TransactionId, Outcome<StateSnapshot>> CommitEvent { get; set; }
        /// <summary>Commits the live actor positions under the given transaction; capture and commit happen in this one call.</summary>
        public Func<TransactionId, Outcome<StateSnapshot>> ProjectWorld { get; set; }
        public ISceneRecovery Recovery { get; set; }
        public ISafeBoundaryPolicy Boundaries { get; set; }
        public ISaveService Saves { get; set; }
        public IEventStagingPort Staging { get; set; }
        public IMapPort Maps { get; set; }
        public IHandleFactory Handles { get; set; }

        /// <summary>Watchdog in unpaused seconds, accumulated from the Tick delta.</summary>
        public double EventTimeoutSeconds { get; set; } = 3600;
        public double RecoveryTimeoutSeconds { get; set; } = 10;
        public int MaxScriptDepth { get; set; } = 16;
        public long MaxInstructionsPerRun { get; set; } = 100000;
        public Action<EventTraceEntry> Trace { get; set; }

        /// <summary>Authoritative once-ledger check, run before leasing. True when the once event is already consumed.</summary>
        public Func<CompiledEvent, StateSnapshot, bool> RepeatBlocked { get; set; }

        internal EventRunnerDependencies Validated()
        {
            Need(Catalog, nameof(Catalog));
            Need(Conditions, nameof(Conditions));
            Need(Leases, nameof(Leases));
            Need(Pauses, nameof(Pauses));
            Need(Moves, nameof(Moves));
            Need(Destinations, nameof(Destinations));
            Need(Presentation, nameof(Presentation));
            Need(PresentationLifetime, nameof(PresentationLifetime));
            Need(Clock, nameof(Clock));
            Need(Store, nameof(Store));
            Need(CommitEvent, nameof(CommitEvent));
            Need(ProjectWorld, nameof(ProjectWorld));
            Need(Recovery, nameof(Recovery));
            Need(Boundaries, nameof(Boundaries));
            Need(Saves, nameof(Saves));
            Need(Staging, nameof(Staging));
            Need(Maps, nameof(Maps));
            Need(Handles, nameof(Handles));
            Need(RepeatBlocked, nameof(RepeatBlocked));
            if (double.IsNaN(EventTimeoutSeconds) || double.IsInfinity(EventTimeoutSeconds) || EventTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(EventTimeoutSeconds));
            if (MaxScriptDepth < 1) throw new ArgumentOutOfRangeException(nameof(MaxScriptDepth));
            if (double.IsNaN(RecoveryTimeoutSeconds) || double.IsInfinity(RecoveryTimeoutSeconds) || RecoveryTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(RecoveryTimeoutSeconds));
            if (MaxInstructionsPerRun < 1) throw new ArgumentOutOfRangeException(nameof(MaxInstructionsPerRun));
            return (EventRunnerDependencies)MemberwiseClone();
        }

        private static void Need(object value, string name)
        {
            if (value == null) throw new ArgumentNullException(name);
        }
    }
}
