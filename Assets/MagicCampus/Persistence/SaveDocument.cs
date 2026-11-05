using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.State;

namespace MagicCampus.Persistence
{
    /// <summary>
    /// The one conversion between the save v2 tree and GameState, and the only code that knows save field names.
    /// Read and Write are exact inverses: Serialize(Write(Read(t))) == Serialize(t) for every tree Read accepts.
    /// </summary>
    public static class SaveDocument
    {
        private static readonly HashSet<string> Typed = new HashSet<string>(StringComparer.Ordinal)
        {
            "clock", "dayJobs", "returnPending", "player", "npcPositions", "flags", "completed", "rewardLedger", "wallet",
            "appointments", "evidence", "learnedSpellIds", "discoveredCaseIds", "caseResolutionOrder"
        };

        private static readonly HashSet<string> TypedPlayer = new HashSet<string>(StringComparer.Ordinal) { "mapId", "x", "y" };

        /// <summary>A double holds every integer up to 2^53 exactly; larger integers would not survive Read then Write.</summary>
        private const long ExactIntegerLimit = 1L << 53;

        /// <summary>The load boundary: root shape (45 fields, integer wallet), then the validator once (save schema and references), then Read.</summary>
        public static Outcome<GameState> Admit(DataNode root, IStateInvariant validator)
        {
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            var shape = SaveFields.CheckRoot(root);
            if (shape.Count > 0) return Outcome<GameState>.Fail(ResultCodes.ContentInvalid, 0, new List<Diagnostic>(shape).ToArray());
            var problems = validator.Validate(new StateSnapshot(0, root));
            if (problems != null && problems.Count > 0) return Outcome<GameState>.Fail(ResultCodes.ContentInvalid, 0, new List<Diagnostic>(problems).ToArray());
            return Read(root);
        }

        /// <summary>Typed parse. Refuses, with CONTENT_INVALID, any typed field whose shape Write could not reproduce.</summary>
        public static Outcome<GameState> Read(DataNode root)
        {
            try
            {
                return Outcome<GameState>.Ok(ReadState(root), 0);
            }
            catch (Malformed e)
            {
                return Outcome<GameState>.Fail(ResultCodes.ContentInvalid, 0,
                    new Diagnostic("SAVE_FIELD_INVALID", e.Message, new SourceLocation("save", e.Pointer, "", "", 0), "save v2 typed field", "malformed"));
            }
        }

        public static DataNode Write(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var m = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            foreach (var kv in state.Preserved.TopLevel.Members) m[kv.Key] = kv.Value;

            var clock = state.Clock;
            m["clock"] = Obj(("absoluteMinute", DataNode.FromInteger(clock.AbsoluteMinute)), ("fraction", Num(clock.Fraction)));
            m["dayJobs"] = Strings(clock.DayJobs);
            m["returnPending"] = DataNode.FromBool(clock.ReturnPending);

            var player = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            foreach (var kv in state.Preserved.PlayerRest.Members) player[kv.Key] = kv.Value;
            var at = state.World.Player;
            player["mapId"] = DataNode.FromString(at.MapId);
            player["x"] = Num(at.X);
            player["y"] = Num(at.Y);
            m["player"] = DataNode.FromObject(player);
            var npcs = new List<DataNode>();
            foreach (var n in state.World.Npcs)
                npcs.Add(Obj(("npcId", DataNode.FromString(n.NpcId)), ("mapId", DataNode.FromString(n.At.MapId)), ("x", Num(n.At.X)), ("y", Num(n.At.Y))));
            m["npcPositions"] = DataNode.FromList(npcs);

            var progress = state.Progress;
            var flags = new List<DataNode>();
            foreach (var f in progress.Flags.Rows)
                flags.Add(Obj(("id", DataNode.FromString(f.Key)), ("value", f.Value.IsBool ? DataNode.FromBool(f.Value.Bool) : DataNode.FromInteger(f.Value.Int))));
            m["flags"] = DataNode.FromList(flags);
            m["completed"] = Strings(progress.Completed);
            m["rewardLedger"] = Strings(progress.RewardLedger);
            m["wallet"] = DataNode.FromInteger(progress.Wallet.Value);

            var rows = new List<DataNode>();
            foreach (var a in state.Appointments.Rows)
                rows.Add(Obj(("id", DataNode.FromString(a.Id)), ("status", DataNode.FromString(a.Status.ToString())), ("acceptedAt", DataNode.FromInteger(a.AcceptedAt))));
            m["appointments"] = DataNode.FromList(rows);

            var known = state.Known;
            m["evidence"] = Strings(known.Evidence);
            m["learnedSpellIds"] = Strings(known.LearnedSpellIds);
            m["discoveredCaseIds"] = Strings(known.DiscoveredCaseIds);
            m["caseResolutionOrder"] = Strings(known.CaseResolutionOrder);
            return DataNode.FromObject(m);
        }

        private static GameState ReadState(DataNode root)
        {
            if (root == null || root.Kind != DataKind.Object) throw new Malformed("", "save root must be an object");

            var clockNode = Exactly(Field(root, "clock"), "/clock", "absoluteMinute", "fraction");
            var minute = clockNode.Members["absoluteMinute"];
            if (minute.Kind != DataKind.Integer || minute.Integer < 0) throw new Malformed("/clock/absoluteMinute", "must be a non-negative integer");
            var fraction = ReadNumber(clockNode.Members["fraction"], "/clock/fraction");
            if (fraction.Value < 0 || fraction.Value >= 1) throw new Malformed("/clock/fraction", "must be in [0,1)");
            var pending = Field(root, "returnPending");
            if (pending.Kind != DataKind.Bool) throw new Malformed("/returnPending", "must be a bool");
            var clock = new ClockState(minute.Integer, fraction, Keys(root, "dayJobs"), pending.Bool);

            var playerNode = Field(root, "player");
            if (playerNode.Kind != DataKind.Object) throw new Malformed("/player", "must be an object");
            var playerRest = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            foreach (var kv in playerNode.Members) if (!TypedPlayer.Contains(kv.Key)) playerRest[kv.Key] = kv.Value;
            var npcs = new List<NpcPlace>();
            var npcList = List(root, "npcPositions");
            for (var i = 0; i < npcList.Count; i++)
            {
                var ptr = "/npcPositions/" + i;
                var row = Exactly(npcList[i], ptr, "npcId", "mapId", "x", "y");
                npcs.Add(new NpcPlace(Text(row.Members["npcId"], ptr + "/npcId"), ReadPlace(row, ptr)));
            }
            var world = new WorldState(ReadPlace(playerNode, "/player"), npcs.AsReadOnly());

            var flagRows = new List<KeyValuePair<string, FlagValue>>();
            var flagList = List(root, "flags");
            for (var i = 0; i < flagList.Count; i++)
            {
                var ptr = "/flags/" + i;
                var row = Exactly(flagList[i], ptr, "id", "value");
                var value = row.Members["value"];
                FlagValue typed;
                if (value.Kind == DataKind.Bool) typed = FlagValue.Of(value.Bool);
                else if (value.Kind == DataKind.Integer) typed = FlagValue.Of(value.Integer);
                else throw new Malformed(ptr + "/value", "must be a bool or an integer");
                flagRows.Add(new KeyValuePair<string, FlagValue>(Text(row.Members["id"], ptr + "/id"), typed));
            }
            var flags = FlagTable.TryCreate(flagRows) ?? throw new Malformed("/flags", "holds an empty or duplicate id");
            var walletNode = Field(root, "wallet");
            var wallet = walletNode.Kind == DataKind.Integer ? Crowns.Of(walletNode.Integer) : null;
            if (!wallet.HasValue) throw new Malformed("/wallet", "must be an integer in 0.." + Crowns.Limit);
            var progress = new Progress(flags, Keys(root, "completed"), Keys(root, "rewardLedger"), wallet.Value);

            var appointments = new List<Appointment>();
            var appointmentList = List(root, "appointments");
            for (var i = 0; i < appointmentList.Count; i++)
            {
                var ptr = "/appointments/" + i;
                var row = Exactly(appointmentList[i], ptr, "id", "status", "acceptedAt");
                var acceptedAt = row.Members["acceptedAt"];
                if (acceptedAt.Kind != DataKind.Integer || acceptedAt.Integer < 0) throw new Malformed(ptr + "/acceptedAt", "must be a non-negative integer");
                appointments.Add(new Appointment(Text(row.Members["id"], ptr + "/id"), ReadStatus(row.Members["status"], ptr + "/status"), acceptedAt.Integer));
            }
            var book = AppointmentBook.TryCreate(appointments) ?? throw new Malformed("/appointments", "holds a duplicate id");

            var known = new Knowledge(Keys(root, "evidence"), Keys(root, "learnedSpellIds"), Keys(root, "discoveredCaseIds"), Keys(root, "caseResolutionOrder"));

            var rest = new Dictionary<string, DataNode>(StringComparer.Ordinal);
            foreach (var kv in root.Members) if (!Typed.Contains(kv.Key)) rest[kv.Key] = kv.Value;
            return new GameState(clock, world, progress, book, known, new PreservedFields(DataNode.FromObject(rest), DataNode.FromObject(playerRest)));
        }

        private static Place ReadPlace(DataNode node, string ptr)
        {
            DataNode map, x, y;
            if (!node.TryGet("mapId", out map)) throw new Malformed(ptr + "/mapId", "is missing");
            if (!node.TryGet("x", out x)) throw new Malformed(ptr + "/x", "is missing");
            if (!node.TryGet("y", out y)) throw new Malformed(ptr + "/y", "is missing");
            return new Place(Text(map, ptr + "/mapId"), ReadNumber(x, ptr + "/x"), ReadNumber(y, ptr + "/y"));
        }

        private static SaveNumber ReadNumber(DataNode node, string ptr)
        {
            if (node.Kind == DataKind.Number) return SaveNumber.Read(node.Number, false);
            if (node.Kind == DataKind.Integer && Math.Abs(node.Integer) <= ExactIntegerLimit) return SaveNumber.Read(node.Integer, true);
            throw new Malformed(ptr, "must be a number (integers within 2^53)");
        }

        private static ReservationStatus ReadStatus(DataNode node, string ptr)
        {
            var text = Text(node, ptr);
            foreach (ReservationStatus s in Enum.GetValues(typeof(ReservationStatus)))
                if (s.ToString() == text) return s;
            throw new Malformed(ptr, "must be Offered, Accepted or Ready");
        }

        private static DataNode Field(DataNode root, string key)
        {
            DataNode n;
            if (!root.TryGet(key, out n)) throw new Malformed("/" + key, "is missing");
            return n;
        }

        private static IReadOnlyList<DataNode> List(DataNode root, string key)
        {
            var n = Field(root, key);
            if (n.Kind != DataKind.List) throw new Malformed("/" + key, "must be a list");
            return n.Items;
        }

        private static KeySet Keys(DataNode root, string key)
        {
            var items = List(root, key);
            var values = new List<string>(items.Count);
            for (var i = 0; i < items.Count; i++) values.Add(Text(items[i], "/" + key + "/" + i));
            return KeySet.TryCreate(values) ?? throw new Malformed("/" + key, "holds a duplicate");
        }

        private static string Text(DataNode node, string ptr)
        {
            if (node.Kind != DataKind.String) throw new Malformed(ptr, "must be a string");
            return node.Text;
        }

        private static DataNode Exactly(DataNode node, string ptr, params string[] keys)
        {
            if (node.Kind != DataKind.Object || node.Members.Count != keys.Length) throw new Malformed(ptr, "must be an object with exactly " + string.Join(", ", keys));
            foreach (var k in keys) if (!node.Members.ContainsKey(k)) throw new Malformed(ptr + "/" + k, "is missing");
            return node;
        }

        private static DataNode Num(SaveNumber n) => n.Integral ? DataNode.FromInteger((long)n.Value) : DataNode.FromNumber(n.Value);

        private static DataNode Strings(KeySet keys)
        {
            var items = new List<DataNode>(keys.Count);
            foreach (var k in keys.Items) items.Add(DataNode.FromString(k));
            return DataNode.FromList(items);
        }

        private static DataNode Obj(params (string Key, DataNode Value)[] members)
        {
            var list = new List<KeyValuePair<string, DataNode>>(members.Length);
            foreach (var m in members) list.Add(new KeyValuePair<string, DataNode>(m.Key, m.Value));
            return DataNode.FromObject(list);
        }

        private sealed class Malformed : Exception
        {
            public readonly string Pointer;
            public Malformed(string pointer, string message) : base(message) { Pointer = pointer; }
        }
    }
}
