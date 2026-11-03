using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Runtime;
using MagicCampus.World.Movement;

namespace MagicCampus.Tests.Movement
{
    internal static class Expect
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("MovementSpecs failed: " + message);
        }

        public static void Near(double actual, double expected, double eps, string message)
        {
            if (!(Math.Abs(actual - expected) <= eps))
                throw new InvalidOperationException("MovementSpecs failed: " + message + " expected " + expected + " actual " + actual);
        }

        public static void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("MovementSpecs failed: " + message + " (no " + typeof(T).Name + ")");
        }

        public static void Failed(IHandle handle, string code, string message)
        {
            True(handle.Terminal != null, message + ": not terminal");
            True(handle.Terminal.Status == OperationStatus.Failed, message + ": status " + handle.Terminal.Status);
            True(handle.Terminal.Code == code, message + ": code " + handle.Terminal.Code + " expected " + code);
        }

        public static void Succeeded(IHandle handle, string message)
        {
            True(handle.Terminal != null, message + ": not terminal");
            True(handle.Terminal.Status == OperationStatus.Succeeded, message + ": status " + handle.Terminal.Status + " " + handle.Terminal.Code);
        }
    }

    internal static class Dn
    {
        public static KeyValuePair<string, DataNode> P(string k, DataNode v) { return new KeyValuePair<string, DataNode>(k, v); }
        public static DataNode O(params KeyValuePair<string, DataNode>[] m) { return DataNode.FromObject(m); }
        public static DataNode L(params DataNode[] i) { return DataNode.FromList(i); }
        public static DataNode I(long v) { return DataNode.FromInteger(v); }
        public static DataNode N(double v) { return DataNode.FromNumber(v); }
        public static DataNode S(string v) { return DataNode.FromString(v); }
    }

    internal sealed class TestGrid
    {
        public TestGrid(string id, params string[] rows)
        {
            Id = id;
            Height = rows.Length;
            Width = rows[0].Length;
            Wall = new bool[Width, Height];
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    Wall[x, y] = rows[y][x] == '#';
        }

        public readonly string Id;
        public readonly int Width;
        public readonly int Height;
        public readonly bool[,] Wall;
    }

    /// <summary>Grid collision and geometry fake. Ghost cells block sweeps only; blocker cells block both sweeps and passability.</summary>
    internal sealed class TestWorld : ICollisionPort, IMapGeometry
    {
        private readonly Dictionary<string, TestGrid> grids = new Dictionary<string, TestGrid>(StringComparer.Ordinal);
        private readonly HashSet<string> blockers = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> ghosts = new HashSet<string>(StringComparer.Ordinal);

        public void Add(TestGrid grid) { grids[grid.Id] = grid; }
        public void Block(string map, int x, int y) { blockers.Add(Key(map, x, y)); }
        public void Ghost(string map, int x, int y) { ghosts.Add(Key(map, x, y)); }

        public MapGeometry Find(string mapId)
        {
            TestGrid g;
            return mapId != null && grids.TryGetValue(mapId, out g) ? new MapGeometry(g.Id, g.Width, g.Height) : null;
        }

        public bool IsPassable(Position at, double radius) { return Clear(at, radius, false); }

        public SweepResult Sweep(Position from, Position to, double radius)
        {
            if (from.MapId != to.MapId) throw new ArgumentException("sweep across maps");
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            var steps = Math.Max(1, (int)Math.Ceiling(len / 0.05));
            var last = from;
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (double)steps;
                var p = i == steps ? to : new Position(from.MapId, from.X + dx * t, from.Y + dy * t);
                if (!Clear(p, radius, true)) return new SweepResult(last, true, "blocker");
                last = p;
            }
            return new SweepResult(to, false, string.Empty);
        }

        private static string Key(string map, int x, int y) { return map + ":" + x + ":" + y; }

        private bool Clear(Position at, double r, bool withGhosts)
        {
            TestGrid g;
            if (!grids.TryGetValue(at.MapId, out g)) return false;
            if (!(at.X >= 0 && at.X < g.Width && at.Y >= 0 && at.Y < g.Height)) return false;
            var x0 = (int)Math.Floor(at.X - r);
            var x1 = (int)Math.Floor(at.X + r);
            var y0 = (int)Math.Floor(at.Y - r);
            var y1 = (int)Math.Floor(at.Y + r);
            for (var cy = y0; cy <= y1; cy++)
                for (var cx = x0; cx <= x1; cx++)
                {
                    var blocked = cx < 0 || cy < 0 || cx >= g.Width || cy >= g.Height || g.Wall[cx, cy]
                        || blockers.Contains(Key(at.MapId, cx, cy)) || (withGhosts && ghosts.Contains(Key(at.MapId, cx, cy)));
                    if (!blocked) continue;
                    var nx = Math.Max(cx, Math.Min(cx + 1, at.X));
                    var ny = Math.Max(cy, Math.Min(cy + 1, at.Y));
                    var d = Math.Sqrt((at.X - nx) * (at.X - nx) + (at.Y - ny) * (at.Y - ny));
                    if (d < r - 1e-9) return false;
                }
            return true;
        }
    }

    internal sealed class PendingLoad
    {
        public string MapId;
        public IHandleController<string> Handle;
    }

    internal sealed class TestMapPort : IMapPort
    {
        private readonly IHandleFactory handles;
        private readonly SortedDictionary<string, Portal> portals = new SortedDictionary<string, Portal>(StringComparer.Ordinal);
        private int loadSequence;

        public TestMapPort(IHandleFactory handles, string activeMapId)
        {
            this.handles = handles;
            ActiveMapId = activeMapId;
        }

        public string ActiveMapId { get; private set; }
        public long NavRevision { get; private set; }
        public bool AutoComplete = true;
        public bool FailLoads;
        public readonly List<string> LoadRequests = new List<string>();
        public readonly List<PendingLoad> Pending = new List<PendingLoad>();
        public Action<string> OnLoadRequested;

        public void AddPortal(Portal portal) { portals[portal.PortalId] = portal; NavRevision++; }
        public void BumpNav() { NavRevision++; }

        public void SetOpen(string id, bool open)
        {
            var p = portals[id];
            portals[id] = new Portal(p.PortalId, p.From, p.Arrival, open);
            NavRevision++;
        }

        public bool TryGetPortal(string portalId, out Portal portal) { return portals.TryGetValue(portalId, out portal); }

        public IReadOnlyList<Portal> PortalsOn(string mapId)
        {
            var list = new List<Portal>();
            foreach (var p in portals.Values)
                if (p.From.MapId == mapId) list.Add(p);
            return list;
        }

        public IHandle Load(string mapId, string ownerToken)
        {
            LoadRequests.Add(mapId);
            if (OnLoadRequested != null) OnLoadRequested(mapId);
            var handle = handles.Create<string>("load." + (++loadSequence));
            handle.Start();
            var pending = new PendingLoad { MapId = mapId, Handle = handle };
            handle.SetCancellationHandler(reason => handle.Cancelled(reason, 0));
            if (AutoComplete) Settle(pending, !FailLoads); else Pending.Add(pending);
            return handle;
        }

        public bool CompletePending(int index, bool success) { return Settle(Pending[index], success); }

        private bool Settle(PendingLoad p, bool success)
        {
            if (success)
            {
                if (!p.Handle.Succeed(p.MapId, 0)) return false;
                ActiveMapId = p.MapId;
                return true;
            }
            return p.Handle.Fail(ResultCodes.MapLoadFailed, 0, new[] { new Diagnostic("TEST_LOAD_FAILED", "fake load failure", null, string.Empty, string.Empty) });
        }
    }

    internal sealed class TestPause : IPauseService
    {
        private readonly List<PauseToken> tokens = new List<PauseToken>();
        private int sequence;

        public IPausePolicy Effective
        {
            get
            {
                bool c = false, a = false, p = false, w = false, s = false;
                foreach (var t in tokens)
                {
                    c |= t.Policy.Calendar; a |= t.Policy.Ambient; p |= t.Policy.Player; w |= t.Policy.World; s |= t.Policy.Scene;
                }
                return new PauseSet(c, a, p, w, s);
            }
        }

        public PauseToken Acquire(string owner, IPausePolicy requested)
        {
            var token = new PauseToken("pause." + (++sequence), owner, requested);
            tokens.Add(token);
            return token;
        }

        public void Release(PauseToken token) { tokens.Remove(token); }
    }

    internal sealed class TestCatalog : ICatalogProvider, ICatalog
    {
        private readonly Dictionary<string, DataNode> rows = new Dictionary<string, DataNode>(StringComparer.Ordinal);

        public void Set(string kind, string id, DataNode row) { rows[kind + "|" + id] = row; }

        public ICatalog Current { get { return this; } }

        public Outcome<ICatalog> TryReplace(ContentBundle catalog, ContentBundle runner, ContentBundle functions, ContentBundle catalogSchema, ContentBundle runnerSchema)
        {
            return Outcome<ICatalog>.Fail(ResultCodes.ContentInvalid, 0, new Diagnostic("NOT_SUPPORTED", "test catalog is edited with Set", null, string.Empty, string.Empty));
        }

        public string ContentVersion { get { return "test"; } }
        public string CatalogHash { get { return "test-hash"; } }
        public IReadOnlyList<CompiledEvent> Events { get { return Array.Empty<CompiledEvent>(); } }
        public CompiledEvent FindEvent(string id) { return null; }

        public DataNode FindDefinition(string kind, string id)
        {
            DataNode row;
            return rows.TryGetValue(kind + "|" + id, out row) ? row : null;
        }
    }

    internal sealed class Rig
    {
        public readonly CompletionPump Pump = new CompletionPump();
        public readonly HandleFactory Handles;
        public readonly TestWorld World;
        public readonly TestMapPort Maps;
        public readonly TestPause Pause = new TestPause();
        public readonly ActorWorld Actors;
        public readonly MoveService Moves;

        public Rig(string activeMap, IEnumerable<ActorState> actors, Func<string, bool> sceneOwner = null, MoveOptions options = null)
        {
            Handles = new HandleFactory(Pump);
            World = StandardWorld();
            Maps = new TestMapPort(Handles, activeMap);
            Maps.AddPortal(new Portal("hall-east", Pos("hall", 8.5, 2.5), Pos("yard", 2.5, 2.5), true));
            Maps.AddPortal(new Portal("yard-west", Pos("yard", 1.5, 2.5), Pos("hall", 7.5, 2.5), true));
            Actors = new ActorWorld(World, actors);
            Moves = new MoveService(Actors, World, Maps, World, Handles, Pause, sceneOwner, "player", options);
        }

        public static TestWorld StandardWorld()
        {
            var w = new TestWorld();
            w.Add(new TestGrid("hall", "##########", "#........#", "#........#", "#........#", "#........#", "##########"));
            w.Add(new TestGrid("yard", "########", "#......#", "#......#", "#......#", "#......#", "########"));
            w.Add(new TestGrid("wall", "##########", "#....#...#", "#....#...#", "#....#...#", "#........#", "##########"));
            w.Add(new TestGrid("sealed", "##########", "#...#....#", "#...#....#", "#...#....#", "#...#....#", "##########"));
            w.Add(new TestGrid("door", "###########", "#....#....#", "#.........#", "#....#....#", "###########"));
            return w;
        }

        public static Position Pos(string map, double x, double y) { return new Position(map, x, y); }

        public static ActorState Actor(string id, string map, double x, double y, double radius = 0.3, double speed = 2.0)
        {
            return new ActorState(id, Pos(map, x, y), radius, speed);
        }

        public static MoveRequest Req(string actor, string map, double x, double y, double tolerance = 0.1, string owner = "owner.test", double timeout = 60, bool portals = true)
        {
            return new MoveRequest(actor, new Destination(string.Empty, Pos(map, x, y), tolerance), owner, timeout, portals);
        }

        public void Frame(double dt)
        {
            Pump.AdvanceTick();
            Moves.Tick(dt, dt);
        }

        public int RunUntil(IHandle handle, double dt, int max)
        {
            var n = 0;
            while (handle.Terminal == null && n < max) { Frame(dt); n++; }
            return n;
        }

        public void AssertClean()
        {
            Frame(0.0);
            Frame(0.0);
            Expect.True(Moves.ActiveMoves.Count == 0, "no active moves remain");
            Expect.True(Handles.LiveCount == 0, "no live handles remain");
        }
    }
}
