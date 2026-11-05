using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Transactions.Conditions;

namespace MagicCampus.State
{
    /// <summary>
    /// The whole game state between two commits. Immutable: a change builds a new GameState that shares every untouched part.
    /// Only SaveDocument.Read and the With copies construct one, so every instance satisfies the invariants the part types encode.
    /// </summary>
    public sealed class GameState
    {
        public readonly ClockState Clock;
        public readonly WorldState World;
        public readonly Progress Progress;
        public readonly AppointmentBook Appointments;
        public readonly Knowledge Known;
        internal readonly PreservedFields Preserved;

        internal GameState(ClockState clock, WorldState world, Progress progress, AppointmentBook appointments, Knowledge known, PreservedFields preserved)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            World = world ?? throw new ArgumentNullException(nameof(world));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Appointments = appointments ?? throw new ArgumentNullException(nameof(appointments));
            Known = known ?? throw new ArgumentNullException(nameof(known));
            Preserved = preserved ?? throw new ArgumentNullException(nameof(preserved));
        }

        public GameState With(ClockState clock) => new GameState(clock, World, Progress, Appointments, Known, Preserved);
        public GameState With(WorldState world) => new GameState(Clock, world, Progress, Appointments, Known, Preserved);
        public GameState With(Progress progress) => new GameState(Clock, World, progress, Appointments, Known, Preserved);
        public GameState With(AppointmentBook appointments) => new GameState(Clock, World, Progress, appointments, Known, Preserved);
    }

    /// <summary>
    /// A saved number plus the JSON kind it is written with ("2" and "2.0" are different save bytes). The kind survives only while the
    /// value is untouched; a changed value is written as a real number.
    /// </summary>
    public readonly struct SaveNumber : IEquatable<SaveNumber>
    {
        public readonly double Value;
        public readonly bool Integral;

        private SaveNumber(double value, bool integral) { Value = value; Integral = integral; }

        internal static SaveNumber Read(double value, bool integral) => new SaveNumber(value, integral);
        public static SaveNumber Real(double value) => new SaveNumber(value, false);
        public SaveNumber MovedTo(double value) => value == Value ? this : Real(value);

        public bool Equals(SaveNumber o) => Value == o.Value && Integral == o.Integral;
        public override bool Equals(object obj) => obj is SaveNumber n && Equals(n);
        public override int GetHashCode() => Value.GetHashCode() * 2 + (Integral ? 1 : 0);
    }

    public sealed class ClockState
    {
        public readonly long AbsoluteMinute;
        public readonly SaveNumber Fraction;
        public readonly KeySet DayJobs;
        public readonly bool ReturnPending;

        internal ClockState(long absoluteMinute, SaveNumber fraction, KeySet dayJobs, bool returnPending)
        {
            _ = new GameTime(absoluteMinute, fraction.Value);
            AbsoluteMinute = absoluteMinute;
            Fraction = fraction;
            DayJobs = dayJobs ?? throw new ArgumentNullException(nameof(dayJobs));
            ReturnPending = returnPending;
        }

        public GameTime Time => new GameTime(AbsoluteMinute, Fraction.Value);

        /// <summary>The one place time moves. Every crossed midnight is recorded once in DayJobs and sets ReturnPending.</summary>
        public ClockState Advance(ClockAdvance step)
        {
            var jobs = DayJobs;
            foreach (var b in step.Boundaries) jobs = jobs.Add(b.Key.Value);
            return new ClockState(step.To.AbsoluteMinute, SaveNumber.Real(step.To.Fraction), jobs, ReturnPending || step.Boundaries.Count > 0);
        }
    }

    public readonly struct Place
    {
        public readonly string MapId;
        public readonly SaveNumber X;
        public readonly SaveNumber Y;

        internal Place(string mapId, SaveNumber x, SaveNumber y)
        {
            MapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
            X = x;
            Y = y;
        }

        public Position ToPosition() => new Position(MapId, X.Value, Y.Value);

        internal Place MovedTo(Position to) => new Place(to.MapId, X.MovedTo(to.X), Y.MovedTo(to.Y));
    }

    public sealed class NpcPlace
    {
        public readonly string NpcId;
        public readonly Place At;

        internal NpcPlace(string npcId, Place at)
        {
            NpcId = npcId ?? throw new ArgumentNullException(nameof(npcId));
            At = at;
        }
    }

    /// <summary>Live actor positions captured at one instant. The capture validates maps and bounds; Project trusts it.</summary>
    public sealed class WorldPlaces
    {
        public readonly Position? Player;
        public readonly IReadOnlyList<KeyValuePair<string, Position>> Npcs;

        public WorldPlaces(Position? player, IEnumerable<KeyValuePair<string, Position>> npcs)
        {
            Player = player;
            Npcs = Array.AsReadOnly(new List<KeyValuePair<string, Position>>(npcs ?? throw new ArgumentNullException(nameof(npcs))).ToArray());
        }
    }

    /// <summary>Safe positions only. The live authority is the actor world; this changes only through Project.</summary>
    public sealed class WorldState
    {
        public readonly Place Player;
        public readonly IReadOnlyList<NpcPlace> Npcs;

        internal WorldState(Place player, IReadOnlyList<NpcPlace> npcs)
        {
            Player = player;
            Npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        }

        /// <summary>Writes the player, merges NPC rows by id in save order, appends unknown NPCs, keeps rows of absent NPCs.</summary>
        public WorldState Project(WorldPlaces live)
        {
            var player = live.Player.HasValue ? Player.MovedTo(live.Player.Value) : Player;
            var rows = new List<NpcPlace>(Npcs);
            foreach (var npc in live.Npcs)
            {
                var i = rows.FindIndex(r => r.NpcId == npc.Key);
                if (i >= 0) rows[i] = new NpcPlace(npc.Key, rows[i].At.MovedTo(npc.Value));
                else rows.Add(new NpcPlace(npc.Key, new Place(npc.Value.MapId, SaveNumber.Real(npc.Value.X), SaveNumber.Real(npc.Value.Y))));
            }
            return new WorldState(player, rows.AsReadOnly());
        }
    }

    /// <summary>Flags, completions, the reward ledger and the wallet. Every "has this been earned" rule lives here, once.</summary>
    public sealed class Progress
    {
        public readonly FlagTable Flags;
        public readonly KeySet Completed;
        public readonly KeySet RewardLedger;
        public readonly Crowns Wallet;

        internal Progress(FlagTable flags, KeySet completed, KeySet rewardLedger, Crowns wallet)
        {
            Flags = flags ?? throw new ArgumentNullException(nameof(flags));
            Completed = completed ?? throw new ArgumentNullException(nameof(completed));
            RewardLedger = rewardLedger ?? throw new ArgumentNullException(nameof(rewardLedger));
            Wallet = wallet;
        }

        /// <summary>The once rule: a key in either persisted ledger is consumed.</summary>
        public bool IsConsumed(CompletionKey key) => Completed.Contains(key.Value) || RewardLedger.Contains(key.Value);

        /// <summary>Records a once-event completion in both ledgers.</summary>
        public Outcome<Progress> Claim(CompletionKey key)
        {
            if (IsConsumed(key))
                return Refuse(new Diagnostic("COMPLETION_ALREADY_RECORDED", "completion key is already persisted", null, "unconsumed completion key", key.Value), ResultCodes.ConditionFalse);
            return Outcome<Progress>.Ok(new Progress(Flags, Completed.Add(key.Value), RewardLedger.Add(key.Value), Wallet), 0);
        }

        /// <summary>complete(id): a plain completion id, written to Completed only.</summary>
        public Outcome<Progress> Complete(string id)
        {
            if (Completed.Contains(id)) return Refuse(Diag.Make(ResultCodes.ConditionFalse, "already completed", "not yet completed", id), ResultCodes.ConditionFalse);
            return Outcome<Progress>.Ok(new Progress(Flags, Completed.Add(id), RewardLedger, Wallet), 0);
        }

        public Outcome<Progress> Earn(long crowns)
        {
            var sum = Wallet.Added(crowns);
            if (!sum.HasValue)
                return Refuse(Diag.Make(ResultCodes.InvariantViolation, "wallet would overflow", "crowns <= " + Diag.Num(Crowns.Limit), Diag.Num(Wallet.Value) + " + " + Diag.Num(crowns)), ResultCodes.InvariantViolation);
            return Outcome<Progress>.Ok(new Progress(Flags, Completed, RewardLedger, sum.Value), 0);
        }

        public Progress Set(string variableId, FlagValue value) => new Progress(Flags.With(variableId, value), Completed, RewardLedger, Wallet);

        private static Outcome<Progress> Refuse(Diagnostic d, string code) => Outcome<Progress>.Fail(code, 0, d);
    }

    /// <summary>Wallet amount, 0..2147483647 by construction (save schema wallet maximum).</summary>
    public readonly struct Crowns
    {
        public const long Limit = int.MaxValue;
        public readonly long Value;

        private Crowns(long value) { Value = value; }

        public static Crowns? Of(long value) => value < 0 || value > Limit ? (Crowns?)null : new Crowns(value);

        /// <summary>Null when amount is not positive or the sum passes Limit. Returns the sum instead of writing an out parameter,
        /// because an out parameter aliased to the receiver zeroed the wallet in an earlier prototype.</summary>
        public Crowns? Added(long amount) => amount <= 0 || amount > Limit - Value ? (Crowns?)null : new Crowns(Value + amount);
    }

    /// <summary>Ordered unique strings in save order.</summary>
    public sealed class KeySet
    {
        private readonly string[] items;
        private readonly HashSet<string> index;

        private KeySet(string[] items, HashSet<string> index) { this.items = items; this.index = index; }

        /// <summary>Null when a value is null or repeated.</summary>
        public static KeySet TryCreate(IEnumerable<string> values)
        {
            var list = new List<string>(values);
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in list) if (v == null || !set.Add(v)) return null;
            return new KeySet(list.ToArray(), set);
        }

        public IReadOnlyList<string> Items => items;
        public int Count => items.Length;
        public bool Contains(string key) => index.Contains(key);

        /// <summary>Appends when absent; returns this when present.</summary>
        public KeySet Add(string key)
        {
            if (index.Contains(key)) return this;
            var next = new string[items.Length + 1];
            Array.Copy(items, next, items.Length);
            next[items.Length] = key;
            return new KeySet(next, new HashSet<string>(index, StringComparer.Ordinal) { key });
        }
    }

    /// <summary>A declared variable value: bool or integer.</summary>
    public readonly struct FlagValue : IEquatable<FlagValue>
    {
        public readonly bool IsBool;
        public readonly bool Bool;
        public readonly long Int;

        private FlagValue(bool isBool, bool b, long i) { IsBool = isBool; Bool = b; Int = i; }

        public static FlagValue Of(bool b) => new FlagValue(true, b, 0);
        public static FlagValue Of(long i) => new FlagValue(false, false, i);

        public bool Equals(FlagValue o) => IsBool == o.IsBool && Bool == o.Bool && Int == o.Int;
        public override bool Equals(object obj) => obj is FlagValue v && Equals(v);
        public override int GetHashCode() => IsBool ? (Bool ? 1 : 0) : Int.GetHashCode() * 3 + 2;
    }

    /// <summary>Flags as an ordered map. New ids append, existing ids are replaced in place.</summary>
    public sealed class FlagTable
    {
        private readonly KeyValuePair<string, FlagValue>[] rows;

        private FlagTable(KeyValuePair<string, FlagValue>[] rows) { this.rows = rows; }

        /// <summary>Null when an id is empty or repeated.</summary>
        public static FlagTable TryCreate(IEnumerable<KeyValuePair<string, FlagValue>> rows)
        {
            var list = new List<KeyValuePair<string, FlagValue>>(rows);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in list) if (string.IsNullOrEmpty(r.Key) || !seen.Add(r.Key)) return null;
            return new FlagTable(list.ToArray());
        }

        public IReadOnlyList<KeyValuePair<string, FlagValue>> Rows => rows;

        public bool TryGet(string id, out FlagValue value)
        {
            foreach (var r in rows)
                if (r.Key == id) { value = r.Value; return true; }
            value = default;
            return false;
        }

        public FlagTable With(string id, FlagValue value)
        {
            var next = new List<KeyValuePair<string, FlagValue>>(rows);
            var i = next.FindIndex(r => r.Key == id);
            var row = new KeyValuePair<string, FlagValue>(id, value);
            if (i < 0) next.Add(row); else next[i] = row;
            return new FlagTable(next.ToArray());
        }
    }

    public sealed class Appointment
    {
        public readonly string Id;
        public readonly ReservationStatus Status;
        public readonly long AcceptedAt;

        internal Appointment(string id, ReservationStatus status, long acceptedAt)
        {
            if (acceptedAt < 0) throw new ArgumentOutOfRangeException(nameof(acceptedAt));
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Status = status;
            AcceptedAt = acceptedAt;
        }
    }

    /// <summary>Appointments in save order, ids unique.</summary>
    public sealed class AppointmentBook
    {
        public readonly IReadOnlyList<Appointment> Rows;

        private AppointmentBook(IReadOnlyList<Appointment> rows) { Rows = rows; }

        /// <summary>Null when an id is repeated.</summary>
        internal static AppointmentBook TryCreate(IEnumerable<Appointment> rows)
        {
            var list = new List<Appointment>(rows);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in list) if (!seen.Add(r.Id)) return null;
            return new AppointmentBook(list.AsReadOnly());
        }

        public Appointment Find(string id)
        {
            foreach (var r in Rows) if (r.Id == id) return r;
            return null;
        }

        /// <summary>An Offered row becomes Accepted in place, an unknown id is appended, an Accepted or Ready row keeps its time.</summary>
        public AppointmentBook Accept(string id, long minute)
        {
            var rows = new List<Appointment>(Rows);
            var i = rows.FindIndex(r => r.Id == id);
            if (i >= 0 && rows[i].Status != ReservationStatus.Offered) return this;
            var accepted = new Appointment(id, ReservationStatus.Accepted, minute);
            if (i < 0) rows.Add(accepted); else rows[i] = accepted;
            return new AppointmentBook(rows.AsReadOnly());
        }

        /// <summary>Drops the row with this id, keeping the order of the rest; returns this when there is none.</summary>
        public AppointmentBook Remove(string id)
        {
            var rows = new List<Appointment>(Rows);
            return rows.RemoveAll(r => r.Id == id) == 0 ? this : new AppointmentBook(rows.AsReadOnly());
        }
    }

    /// <summary>Read-only today: conditions read it, nothing writes it.</summary>
    public sealed class Knowledge
    {
        public readonly KeySet Evidence;
        public readonly KeySet LearnedSpellIds;
        public readonly KeySet DiscoveredCaseIds;
        public readonly KeySet CaseResolutionOrder;

        internal Knowledge(KeySet evidence, KeySet learnedSpellIds, KeySet discoveredCaseIds, KeySet caseResolutionOrder)
        {
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            LearnedSpellIds = learnedSpellIds ?? throw new ArgumentNullException(nameof(learnedSpellIds));
            DiscoveredCaseIds = discoveredCaseIds ?? throw new ArgumentNullException(nameof(discoveredCaseIds));
            CaseResolutionOrder = caseResolutionOrder ?? throw new ArgumentNullException(nameof(caseResolutionOrder));
        }
    }

    /// <summary>
    /// The save fields no game rule reads or writes yet, kept exactly as loaded. Only SaveDocument opens it. To give one of these
    /// fields a rule, move it into a typed part in SaveDocument.Read and Write.
    /// </summary>
    public sealed class PreservedFields
    {
        internal readonly DataNode TopLevel;
        internal readonly DataNode PlayerRest;

        internal PreservedFields(DataNode topLevel, DataNode playerRest)
        {
            TopLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));
            PlayerRest = playerRest ?? throw new ArgumentNullException(nameof(playerRest));
        }
    }
}
