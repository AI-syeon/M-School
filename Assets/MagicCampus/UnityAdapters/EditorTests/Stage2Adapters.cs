#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MagicCampus.Contracts;
using MagicCampus.UnityAdapters.Collision;
using MagicCampus.UnityAdapters.Input;
using MagicCampus.UnityAdapters.Maps;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using LogicalActions = MagicCampus.Contracts.InputActions;

namespace MagicCampus.UnityAdapters.EditorTests
{
    /// <summary>
    /// Real-engine tests for the Task 2-4 adapters: virtual Keyboard/Gamepad/Mouse devices on the Input System, real Grid/Tilemap/TilemapCollider2D roots,
    /// swept circles against the logical grid. Run with -executeMethod MagicCampus.UnityAdapters.EditorTests.Stage2Adapters.Run.
    /// Writes Tests/Results/stage2-adapters.json (always) and throws when any case fails. Handles and the completion pump are local fakes.
    /// </summary>
    public static class Stage2Adapters
    {
        private sealed class AssertionFailed : Exception
        {
            public AssertionFailed(string message) : base(message) { }
        }

        private sealed class CaseRecord
        {
            public string Name;
            public bool Passed;
            public int Assertions;
            public double Millis;
            public string Message;
        }

        private static readonly List<CaseRecord> cases = new List<CaseRecord>();
        private static int assertions;
        private static long tick;

        public static void Run()
        {
            cases.Clear();
            assertions = 0;
            tick = 0;
            var started = DateTime.UtcNow;
            var settings = InputSystem.settings;
            var previousBehavior = settings.backgroundBehavior;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            try
            {
                Case("input.eight-directions", InputEightDirections);
                Case("input.gamepad-deadzone-and-detach", InputGamepadDeadzoneAndDetach);
                Case("input.ui-capture-no-leak", InputUiCapture);
                Case("input.mouse-buttons", InputMouse);
                Case("maps.parse-and-validate", MapsParse);
                Case("maps.load-builds-real-roots-and-colliders", MapsLoadBuildsRealRoots);
                Case("maps.load-failure-cancel-cleanup", MapsFailureAndCancel);
                Case("collision.doorway-closed-portal-blocked-tile-high-delta", CollisionScenarios);
                Case("collision.inactive-map-and-argument-guards", CollisionInactiveAndGuards);
                Case("collision.random-sweeps-never-tunnel", CollisionRandomSweeps);
            }
            finally
            {
                settings.backgroundBehavior = previousBehavior;
                WriteReport(started);
            }

            var failed = cases.FindAll(c => !c.Passed);
            Debug.Log("[Stage2Adapters] cases=" + cases.Count + " failed=" + failed.Count + " assertions=" + assertions);
            if (failed.Count > 0)
            {
                var names = new List<string>();
                foreach (var f in failed) names.Add(f.Name + " (" + f.Message + ")");
                throw new InvalidOperationException("Stage2Adapters failed: " + string.Join("; ", names));
            }
        }

        // ------------------------------------------------------------ framework

        private static void Case(string name, Action body)
        {
            var record = new CaseRecord { Name = name };
            var before = assertions;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                body();
                record.Passed = true;
            }
            catch (Exception e)
            {
                record.Passed = false;
                record.Message = e.GetType().Name + ": " + e.Message;
            }
            record.Assertions = assertions - before;
            record.Millis = watch.Elapsed.TotalMilliseconds;
            cases.Add(record);
            var line = "[Stage2Adapters] " + (record.Passed ? "PASS " : "FAIL ") + name + " assertions=" + record.Assertions;
            if (record.Passed) Debug.Log(line);
            else Debug.LogError(line + " :: " + record.Message);
        }

        private static void Ensure(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new AssertionFailed(message);
        }

        private static void Same<T>(T expected, T actual, string message)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionFailed(message + " (expected " + expected + ", actual " + actual + ")");
        }

        private static void Near(double expected, double actual, double tolerance, string message)
        {
            assertions++;
            if (!(Math.Abs(expected - actual) <= tolerance))
                throw new AssertionFailed(message + " (expected " + expected.ToString("R", CultureInfo.InvariantCulture) + ", actual " + actual.ToString("R", CultureInfo.InvariantCulture) + ")");
        }

        private static void Throws<TEx>(Action action, string message) where TEx : Exception
        {
            assertions++;
            try { action(); }
            catch (TEx) { return; }
            catch (Exception e) { throw new AssertionFailed(message + ": wrong exception " + e.GetType().Name); }
            throw new AssertionFailed(message + ": no exception");
        }

        private static string Q(string s)
        {
            var bs = (char)92;
            var sb = new StringBuilder();
            sb.Append((char)34);
            foreach (var c in s ?? string.Empty)
            {
                if (c == (char)34 || c == bs) sb.Append(bs).Append(c);
                else if (c < 32) sb.Append(bs).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append((char)34);
            return sb.ToString();
        }

        private static void WriteReport(DateTime started)
        {
            var passed = 0;
            foreach (var c in cases) if (c.Passed) passed++;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"unityVersion\": ").Append(Q(UnityEngine.Application.unityVersion)).Append(",\n");
            sb.Append("  \"startedUtc\": ").Append(Q(started.ToString("o", CultureInfo.InvariantCulture))).Append(",\n");
            sb.Append("  \"cases\": ").Append(cases.Count).Append(",\n");
            sb.Append("  \"passed\": ").Append(passed).Append(",\n");
            sb.Append("  \"failed\": ").Append(cases.Count - passed).Append(",\n");
            sb.Append("  \"assertions\": ").Append(assertions).Append(",\n");
            sb.Append("  \"outcomes\": [\n");
            for (var i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                sb.Append("    {\"name\": ").Append(Q(c.Name));
                sb.Append(", \"passed\": ").Append(c.Passed ? "true" : "false");
                sb.Append(", \"assertions\": ").Append(c.Assertions);
                sb.Append(", \"millis\": ").Append(c.Millis.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(", \"message\": ").Append(Q(c.Message ?? string.Empty)).Append("}");
                sb.Append(i + 1 < cases.Count ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");

            var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            var dir = Path.Combine(projectRoot, "Tests", "Results");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stage2-adapters.json");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Stage2Adapters] report written to " + path);
        }

        // ------------------------------------------------------------ local fakes (no Stage 2 Runtime reference)

        private sealed class FakePump : ICompletionPump
        {
            private readonly List<Action> queue = new List<Action>();
            public void Post(Action action) { queue.Add(action); }
            public void Post(CompletionKey key, Action action) { queue.Add(action); }
            public void AdvanceTick()
            {
                var snapshot = queue.ToArray();
                queue.Clear();
                foreach (var a in snapshot) a();
            }
        }

        private sealed class FakeHandle<T> : IHandleController<T>
        {
            private readonly FakePump pump;
            private readonly List<Action<Result>> callbacks = new List<Action<Result>>();
            private Action<string> cancelHandler;

            public FakeHandle(string id, FakePump pump) { OperationId = id; this.pump = pump; Status = OperationStatus.Pending; }

            public string OperationId { get; }
            public OperationStatus Status { get; private set; }
            public Result Terminal { get; private set; }
            public T Value { get; private set; }

            public void Start() { if (Status == OperationStatus.Pending) Status = OperationStatus.Running; }

            public bool Succeed(T value, long revision)
            {
                if (Terminal != null) return false;
                Value = value;
                return Finish(OperationStatus.Succeeded, ResultCodes.Ok, revision, null);
            }

            public bool Fail(string code, long revision, IReadOnlyList<Diagnostic> details) => Finish(OperationStatus.Failed, code, revision, details);

            public bool Cancelled(string reason, long revision) =>
                Finish(OperationStatus.Cancelled, ResultCodes.Cancelled, revision, new[] { new Diagnostic(ResultCodes.Cancelled, reason ?? string.Empty, null, string.Empty, string.Empty) });

            public void SetCancellationHandler(Action<string> handler) { cancelHandler = handler; }

            public void Cancel(string reason)
            {
                if (Terminal != null) return;
                if (cancelHandler != null) cancelHandler(reason);
            }

            public void OnTerminal(Action<Result> callback)
            {
                if (Terminal != null)
                {
                    var result = Terminal;
                    pump.Post(() => callback(result));
                }
                else callbacks.Add(callback);
            }

            private bool Finish(OperationStatus status, string code, long revision, IReadOnlyList<Diagnostic> details)
            {
                if (Terminal != null) return false;
                Terminal = new Result(status, code, revision, details);
                Status = status;
                foreach (var cb in callbacks)
                {
                    var captured = cb;
                    var result = Terminal;
                    pump.Post(() => captured(result));
                }
                callbacks.Clear();
                return true;
            }
        }

        private sealed class FakeFactory : IHandleFactory
        {
            private readonly FakePump pump;
            public FakeFactory(FakePump pump) { this.pump = pump; }
            public IHandleController<T> Create<T>(string operationId) => new FakeHandle<T>(operationId, pump);
        }

        // ------------------------------------------------------------ input tests

        private static InputFrame Step(InputAdapter adapter)
        {
            InputSystem.Update();
            return adapter.Poll(tick++);
        }

        private static void Hold(Keyboard kb, params Key[] keys) { InputSystem.QueueStateEvent(kb, new KeyboardState(keys)); }

        private static void Stick(Gamepad pad, float x, float y, params GamepadButton[] buttons)
        {
            var state = new GamepadState { leftStick = new Vector2(x, y) };
            foreach (var b in buttons) state = state.WithButton(b);
            InputSystem.QueueStateEvent(pad, state);
        }

        private static bool Has(IReadOnlyList<string> list, string value)
        {
            foreach (var s in list) if (s == value) return true;
            return false;
        }

        private static InputAdapter NewAdapter(Func<bool> captured) => new InputAdapter(InputAdapterConfig.Default, captured);

        private static void Remove(InputDevice device)
        {
            if (device != null && device.added) InputSystem.RemoveDevice(device);
        }

        private sealed class Dir
        {
            public readonly string Name;
            public readonly double X, Y;
            public readonly Key[] Keys;
            public Dir(string name, double x, double y, params Key[] keys) { Name = name; X = x; Y = y; Keys = keys; }
        }

        private static void InputEightDirections()
        {
            var kb = InputSystem.AddDevice<Keyboard>("Stage2Keyboard");
            try
            {
                var adapter = NewAdapter(() => false);
                const double d = 0.70710678118654757;
                var dirs = new[]
                {
                    new Dir("N", 0, 1, Key.W), new Dir("NE", d, d, Key.W, Key.D), new Dir("E", 1, 0, Key.D), new Dir("SE", d, -d, Key.S, Key.D),
                    new Dir("S", 0, -1, Key.S), new Dir("SW", -d, -d, Key.S, Key.A), new Dir("W", -1, 0, Key.A), new Dir("NW", -d, d, Key.W, Key.A)
                };
                long lastSequence = -1;
                InputFrame f;
                foreach (var dir in dirs)
                {
                    Hold(kb, dir.Keys);
                    f = Step(adapter);
                    Near(dir.X, f.MoveX, 1e-9, dir.Name + " x");
                    Near(dir.Y, f.MoveY, 1e-9, dir.Name + " y");
                    Near(1.0, Math.Sqrt(f.MoveX * f.MoveX + f.MoveY * f.MoveY), 1e-9, dir.Name + " has unit length (diagonals normalized)");
                    Ensure(f.InputSequence > lastSequence, dir.Name + " input sequence increases");
                    lastSequence = f.InputSequence;
                    Ensure(!f.UiCaptured, dir.Name + " not UI captured");
                }

                Hold(kb, Key.UpArrow, Key.RightArrow);
                f = Step(adapter);
                Near(d, f.MoveX, 1e-9, "arrow diagonal x");
                Near(d, f.MoveY, 1e-9, "arrow diagonal y");

                Hold(kb, Key.W, Key.S);
                f = Step(adapter);
                Near(0, f.MoveY, 0, "opposite keys cancel");
                Hold(kb, Key.A, Key.D);
                f = Step(adapter);
                Near(0, f.MoveX, 0, "opposite keys cancel horizontally");

                Hold(kb);
                f = Step(adapter);
                Same(0, f.Held.Count, "released keys leave nothing held");
                var same = adapter.Poll(tick - 1);
                Ensure(ReferenceEquals(f, same), "polling the same tick returns the same frame");

                Hold(kb, Key.E);
                f = Step(adapter);
                Ensure(Has(f.Pressed, LogicalActions.Interact), "interact pressed on first tick");
                f = Step(adapter);
                Ensure(!Has(f.Pressed, LogicalActions.Interact) && Has(f.Held, LogicalActions.Interact), "interact held, not re-pressed");
                Hold(kb);
                f = Step(adapter);
                Ensure(!Has(f.Held, LogicalActions.Interact), "interact released");
                Hold(kb, Key.E);
                f = Step(adapter);
                Ensure(Has(f.Pressed, LogicalActions.Interact), "interact pressed again after release");
            }
            finally { Remove(kb); }
        }

        private static void InputGamepadDeadzoneAndDetach()
        {
            var pad = InputSystem.AddDevice<Gamepad>("Stage2Gamepad");
            Gamepad pad2 = null;
            try
            {
                var adapter = NewAdapter(() => false);
                Stick(pad, 0.1f, 0.05f);
                var f = Step(adapter);
                Near(0, f.MoveX, 1e-9, "inside dead zone x");
                Near(0, f.MoveY, 1e-9, "inside dead zone y");

                Stick(pad, 0.2f, 0f);
                f = Step(adapter);
                Near(0, f.MoveX, 1e-6, "exactly at dead zone edge");

                Stick(pad, 0.6f, 0f);
                f = Step(adapter);
                Near(0.5, f.MoveX, 1e-5, "dead zone rescales deflection");

                Stick(pad, 1f, 1f);
                f = Step(adapter);
                Near(0.70710678, f.MoveX, 1e-5, "square gate diagonal x");
                Near(0.70710678, f.MoveY, 1e-5, "square gate diagonal y");
                Ensure(f.MoveX * f.MoveX + f.MoveY * f.MoveY <= 1.000001, "stick never exceeds unit length");

                Stick(pad, -1f, 0f);
                f = Step(adapter);
                Near(-1, f.MoveX, 1e-6, "full left");

                Stick(pad, 0f, 1f, GamepadButton.South, GamepadButton.West, GamepadButton.RightShoulder);
                f = Step(adapter);
                Near(1, f.MoveY, 1e-6, "stick up before detach");
                Ensure(Has(f.Pressed, LogicalActions.Confirm) && Has(f.Pressed, LogicalActions.Interact) && Has(f.Pressed, LogicalActions.Cast), "pad buttons map to logical actions");

                InputSystem.RemoveDevice(pad);
                f = Step(adapter);
                Near(0, f.MoveX, 0, "movement reset after detach x");
                Near(0, f.MoveY, 0, "movement reset after detach y");
                Same(0, f.Held.Count, "no stuck held actions after detach");
                Same(0, f.Pressed.Count, "no phantom presses after detach");

                pad2 = InputSystem.AddDevice<Gamepad>("Stage2Gamepad2");
                f = Step(adapter);
                Same(0, f.Held.Count, "re-attached pad starts neutral");
                Near(0, f.MoveY, 0, "re-attached pad has no movement");
            }
            finally
            {
                Remove(pad);
                Remove(pad2);
            }
        }

        private static void InputUiCapture()
        {
            var kb = InputSystem.AddDevice<Keyboard>("Stage2KeyboardUi");
            try
            {
                var captured = true;
                var adapter = NewAdapter(() => captured);

                Hold(kb, Key.Q, Key.E, Key.W, Key.Enter, Key.Escape, Key.X);
                var f = Step(adapter);
                Ensure(f.UiCaptured, "frame reports UI capture");
                Ensure(!Has(f.Pressed, LogicalActions.Cast) && !Has(f.Held, LogicalActions.Cast), "cast consumed while captured");
                Ensure(!Has(f.Pressed, LogicalActions.Interact) && !Has(f.Held, LogicalActions.Interact), "interact consumed while captured");
                Near(0, f.MoveY, 0, "movement consumed while captured");
                Ensure(Has(f.Pressed, LogicalActions.Confirm), "confirm passes through UI capture");
                Ensure(Has(f.Pressed, LogicalActions.Cancel), "cancel passes through UI capture");
                Ensure(Has(f.Pressed, LogicalActions.Menu), "menu passes through UI capture");

                captured = false;
                f = Step(adapter);
                Ensure(!f.UiCaptured, "capture ended");
                Ensure(!Has(f.Pressed, LogicalActions.Cast), "no cast press leaks when capture ends");
                Ensure(!Has(f.Held, LogicalActions.Cast), "cast stays consumed until released");
                Ensure(!Has(f.Pressed, LogicalActions.Interact), "no interact press leaks when capture ends");
                Near(1, f.MoveY, 1e-9, "movement resumes after capture");

                Hold(kb);
                Step(adapter);
                Hold(kb, Key.Q);
                f = Step(adapter);
                Ensure(Has(f.Pressed, LogicalActions.Cast), "fresh cast press works after release");
            }
            finally { Remove(kb); }
        }

        private static void InputMouse()
        {
            var mouse = InputSystem.AddDevice<Mouse>("Stage2Mouse");
            try
            {
                var adapter = NewAdapter(() => false);
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                var f = Step(adapter);
                Ensure(Has(f.Pressed, LogicalActions.Confirm), "left mouse is confirm");
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right));
                f = Step(adapter);
                Ensure(Has(f.Pressed, LogicalActions.Cancel) && !Has(f.Held, LogicalActions.Confirm), "right mouse is cancel");
                InputSystem.QueueStateEvent(mouse, new MouseState());
                f = Step(adapter);
                Same(0, f.Held.Count, "mouse released");
            }
            finally { Remove(mouse); }
        }

        // ------------------------------------------------------------ map data helpers

        private static DataNode I(long v) => DataNode.FromInteger(v);
        private static DataNode S(string v) => DataNode.FromString(v);
        private static DataNode Num(double v) => v == Math.Floor(v) ? I((long)v) : DataNode.FromNumber(v);
        private static KeyValuePair<string, DataNode> P(string k, DataNode v) => new KeyValuePair<string, DataNode>(k, v);
        private static DataNode O(params KeyValuePair<string, DataNode>[] members) => DataNode.FromObject(members);
        private static DataNode L(IEnumerable<DataNode> items) => DataNode.FromList(items);
        private static DataNode Cell(int x, int y) => L(new[] { I(x), I(y) });

        private static DataNode PortalDef(string id, double x, double y, string toMap, double tx, double ty, bool open) =>
            O(P("id", S(id)), P("x", Num(x)), P("y", Num(y)), P("to", O(P("mapId", S(toMap)), P("x", Num(tx)), P("y", Num(ty)))), P("open", DataNode.FromBool(open)));

        private static DataNode MapDef(string id, int w, int h, IEnumerable<DataNode> blocked, IEnumerable<DataNode> portals) =>
            O(P("id", S(id)), P("width", I(w)), P("height", I(h)), P("blocked", L(blocked)), P("portals", L(portals)));

        // hall 10x6: wall row at y=3 with a one-tile doorway at x=4 (portal door-hall), extra pillar at (7,1). yard 5x5: pillar (1,1), gate at (2,0).
        private static DataNode Maps()
        {
            var hallBlocked = new List<DataNode>();
            for (var x = 0; x < 10; x++) if (x != 4) hallBlocked.Add(Cell(x, 3));
            hallBlocked.Add(Cell(7, 1));
            return L(new[]
            {
                MapDef("hall", 10, 6, hallBlocked, new[] { PortalDef("door-hall", 4, 3, "yard", 2, 2, true) }),
                MapDef("yard", 5, 5, new[] { Cell(1, 1) }, new[] { PortalDef("gate-yard", 2, 0, "hall", 4, 1, true) })
            });
        }

        private static int GridCount()
        {
            var n = 0;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                n += root.GetComponentsInChildren<Grid>(true).Length;
            return n;
        }

        private static int CountBlocked(MapLayout layout, string mapId)
        {
            var g = layout.Find(mapId);
            var n = 0;
            for (var y = 0; y < g.Height; y++)
                for (var x = 0; x < g.Width; x++)
                    if (layout.IsBlocked(mapId, x, y)) n++;
            return n;
        }

        private sealed class World : IDisposable
        {
            public readonly FakePump Pump = new FakePump();
            public readonly MapLayout Layout;
            public readonly MapAdapter Adapter;
            public readonly GridCollisionPort Port;
            public string VerifierFailure;
            public bool VerifierThrows;

            public World()
            {
                var parsed = MapLayout.Parse(Maps(), "stage2-maps");
                Ensure(parsed.Succeeded, "world maps parse");
                Layout = parsed.Value;
                Adapter = new MapAdapter(Layout, new FakeFactory(Pump), new MapAdapterConfig(null, Verify));
                Port = new GridCollisionPort(Layout);
            }

            private string Verify(string mapId, GameObject root)
            {
                if (VerifierThrows) throw new InvalidOperationException("simulated verifier crash");
                return VerifierFailure;
            }

            public IHandle LoadNow(string mapId)
            {
                var handle = Adapter.Load(mapId, "tester");
                Adapter.Tick(0.016, 0.016);
                return handle;
            }

            public void Dispose() { Adapter.Dispose(); }
        }

        // ------------------------------------------------------------ map tests

        private static void ExpectParseFailure(DataNode input, string code, string pointer, string what)
        {
            var r = MapLayout.Parse(input, "bad.json");
            Ensure(!r.Succeeded, what + ": parse fails");
            Same(ResultCodes.ContentInvalid, r.Result.Code, what + ": overall code");
            Ensure(r.Value == null, what + ": no partial layout");
            var found = false;
            foreach (var d in r.Result.Details)
                if (d.Code == code && d.Location != null && (pointer == null || d.Location.Pointer == pointer)) found = true;
            Ensure(found, what + ": diagnostic " + code + " at " + pointer);
        }

        private static void MapsParse()
        {
            var ok = MapLayout.Parse(Maps(), "maps.json");
            Ensure(ok.Succeeded, "valid maps parse");
            var layout = ok.Value;
            var hall = layout.Find("hall");
            Same(10, hall.Width, "hall width");
            Same(6, hall.Height, "hall height");
            Ensure(layout.Find("nope") == null, "unknown map has no geometry");
            Portal portal;
            Ensure(layout.TryGetPortal("door-hall", out portal), "portal lookup");
            Same("yard", portal.Arrival.MapId, "portal arrival map");
            Ensure(portal.Open, "portal starts open");
            Same(1, layout.PortalsOn("hall").Count, "portals on hall");
            Same(0, layout.PortalsOn("nope").Count, "portals on unknown map");
            Ensure(layout.IsBlocked("hall", 0, 3) && !layout.IsBlocked("hall", 4, 3), "wall row with open doorway");
            Ensure(layout.IsBlocked("hall", -1, 0) && layout.IsBlocked("nope", 0, 0), "outside and unknown are blocked");

            var frac = MapLayout.Parse(L(new[] { MapDef("m", 3, 3, new DataNode[0], new[] { PortalDef("p", 1.5, 1.5, "m", 0.5, 0.5, false) }) }), "frac.json");
            Ensure(frac.Succeeded, "fractional portal coordinates parse");
            Ensure(frac.Value.TryGetPortal("p", out portal), "fractional portal lookup");
            Near(1.5, portal.From.X, 0, "portal keeps original coordinate");
            Ensure(frac.Value.IsBlocked("m", 1, 1), "closed portal blocks its tile");

            ExpectParseFailure(L(new[] { MapDef("a", 3, 3, new[] { Cell(3, 0) }, new DataNode[0]) }), ResultCodes.ContentInvalid, "/0/blocked/0", "blocked out of range");
            ExpectParseFailure(L(new[] { MapDef("a", 3, 3, new DataNode[0], new DataNode[0]), MapDef("a", 3, 3, new DataNode[0], new DataNode[0]) }), ResultCodes.ContentInvalid, "/1/id", "duplicate map id");
            ExpectParseFailure(L(new[] { MapDef("a", 3, 3, new DataNode[0], new[] { PortalDef("p", 1, 1, "ghost", 0, 0, true) }) }), ResultCodes.RefNotFound, "/0/portals/0/to/mapId", "portal to unknown map");
            ExpectParseFailure(L(new[] { MapDef("a", 3, 3, new DataNode[0], new[] { PortalDef("p", 5, 5, "a", 0, 0, true) }) }), ResultCodes.ContentInvalid, "/0/portals/0", "portal outside map");
            ExpectParseFailure(L(new[]
            {
                MapDef("a", 3, 3, new DataNode[0], new[] { PortalDef("p", 1, 1, "b", 0, 0, true) }),
                MapDef("b", 3, 3, new DataNode[0], new[] { PortalDef("p", 1, 1, "a", 0, 0, true) })
            }), ResultCodes.ContentInvalid, "/1/portals/0/id", "duplicate portal id");
            ExpectParseFailure(O(), ResultCodes.ContentInvalid, "", "not a list");
        }

        private static void MapsLoadBuildsRealRoots()
        {
            var baseline = GridCount();
            using (var w = new World())
            {
                Same(string.Empty, w.Adapter.ActiveMapId, "no map before first load");
                var handle = w.Adapter.Load("hall", "tester");
                Same(OperationStatus.Running, handle.Status, "load pending");
                Ensure(w.Adapter.PendingRoot != null && !w.Adapter.PendingRoot.activeSelf, "temporary root exists and is inactive");
                Same(baseline + 1, GridCount(), "temporary Grid exists");
                var delivered = false;
                handle.OnTerminal(r => delivered = true);

                w.Adapter.Tick(0.016, 0.016);
                Same(OperationStatus.Succeeded, handle.Status, "load committed");
                Ensure(!delivered, "callback is not inline");
                w.Pump.AdvanceTick();
                Ensure(delivered, "callback delivered on the next tick");
                Same("hall", w.Adapter.ActiveMapId, "active map id");
                Ensure(w.Adapter.PendingRoot == null, "no pending root after commit");
                Ensure(w.Adapter.ActiveRoot != null && w.Adapter.ActiveRoot.activeInHierarchy, "active root is live");
                Same(baseline + 1, GridCount(), "only the active Grid remains");
                var placedCells = 0;
                foreach (var cell in w.Adapter.CollisionTilemap.cellBounds.allPositionsWithin)
                    if (w.Adapter.CollisionTilemap.HasTile(cell)) placedCells++;
                Same(CountBlocked(w.Layout, "hall"), placedCells, "collision cells equal logical blocked cells");
                var mismatches = w.Adapter.FindColliderMismatches();
                Same(0, mismatches.Count, "rendered colliders agree with logical grid: " + string.Join("; ", mismatches));

                var rev = w.Adapter.NavRevision;
                Ensure(w.Layout.SetPortalOpen("door-hall", false).Status == OperationStatus.Succeeded, "close door");
                Ensure(w.Adapter.NavRevision > rev, "door change bumps NavRevision");
                mismatches = w.Adapter.FindColliderMismatches();
                Same(0, mismatches.Count, "colliders follow the closed door: " + string.Join("; ", mismatches));
                Ensure(w.Adapter.CollisionTilemap.GetTile(new Vector3Int(4, 3, 0)) != null, "closed door has a collider tile");

                rev = w.Adapter.NavRevision;
                Ensure(w.Layout.SetBlocked("hall", 4, 1, true).Status == OperationStatus.Succeeded, "block a tile");
                Ensure(w.Adapter.NavRevision > rev, "blocker change bumps NavRevision");
                mismatches = w.Adapter.FindColliderMismatches();
                Same(0, mismatches.Count, "colliders follow the new blocker: " + string.Join("; ", mismatches));

                rev = w.Adapter.NavRevision;
                w.Layout.SetBlocked("hall", 4, 1, true);
                w.Layout.SetPortalOpen("door-hall", false);
                Same(rev, w.Adapter.NavRevision, "no-op updates keep NavRevision");

                w.Layout.SetBlocked("hall", 4, 1, false);
                w.Layout.SetPortalOpen("door-hall", true);
                mismatches = w.Adapter.FindColliderMismatches();
                Same(0, mismatches.Count, "colliders follow the reopened door: " + string.Join("; ", mismatches));

                var unknownPortal = w.Layout.SetPortalOpen("ghost", true);
                Same(ResultCodes.RefNotFound, unknownPortal.Code, "unknown portal refused");
                var outside = w.Layout.SetBlocked("hall", 99, 0, true);
                Same(ResultCodes.OutOfScope, outside.Code, "tile outside the map refused");

                Same(4, MapCoordinates.ToCell(new Position("hall", 4.9, 3.1)).x, "ToCell x");
                Same(3, MapCoordinates.ToCell(new Position("hall", 4.9, 3.1)).y, "ToCell y");
            }
            Same(baseline, GridCount(), "dispose removes every root");
        }

        private static void MapsFailureAndCancel()
        {
            var baseline = GridCount();
            using (var w = new World())
            {
                var first = w.LoadNow("hall");
                Same(OperationStatus.Succeeded, first.Status, "hall loads");
                var activeRoot = w.Adapter.ActiveRoot;
                var withActive = GridCount();
                Same(baseline + 1, withActive, "one Grid for the active map");

                var missing = w.Adapter.Load("missing", "tester");
                Same(OperationStatus.Failed, missing.Status, "missing map fails immediately");
                Same(ResultCodes.MapLoadFailed, missing.Terminal.Code, "missing map code");
                Same(withActive, GridCount(), "missing map leaves no temporary root");
                Same("hall", w.Adapter.ActiveMapId, "missing map keeps previous map");

                w.VerifierFailure = "simulated failure";
                var failing = w.Adapter.Load("yard", "tester");
                Same(withActive + 1, GridCount(), "temporary root exists before the commit tick");
                w.Adapter.Tick(0.016, 0.016);
                Same(OperationStatus.Failed, failing.Status, "verifier failure fails the load");
                Same(ResultCodes.MapLoadFailed, failing.Terminal.Code, "verifier failure code");
                Ensure(failing.Terminal.Details.Count > 0 && failing.Terminal.Details[0].Actual.Contains("simulated failure"), "failure diagnostic carries the reason");
                Same(withActive, GridCount(), "failed load destroyed its temporary root");
                Ensure(ReferenceEquals(activeRoot, w.Adapter.ActiveRoot) && activeRoot != null, "previous root preserved after failure");
                Same("hall", w.Adapter.ActiveMapId, "previous map preserved after failure");
                Ensure(w.Adapter.PendingRoot == null, "nothing pending after failure");

                w.VerifierFailure = null;
                w.VerifierThrows = true;
                var crashing = w.Adapter.Load("yard", "tester");
                w.Adapter.Tick(0.016, 0.016);
                Same(OperationStatus.Failed, crashing.Status, "verifier exception fails the load");
                Same(withActive, GridCount(), "exception path destroyed its temporary root");
                Same("hall", w.Adapter.ActiveMapId, "previous map preserved after exception");
                w.VerifierThrows = false;

                var cancelled = w.Adapter.Load("yard", "tester");
                Same(withActive + 1, GridCount(), "temporary root exists while pending");
                cancelled.Cancel("user");
                Same(OperationStatus.Cancelled, cancelled.Status, "cancel reaches Cancelled");
                Same(ResultCodes.Cancelled, cancelled.Terminal.Code, "cancel code");
                Same(withActive, GridCount(), "cancel destroyed the temporary root");
                Same("hall", w.Adapter.ActiveMapId, "previous map preserved after cancel");
                w.Adapter.Tick(0.016, 0.016);
                Same(OperationStatus.Cancelled, cancelled.Status, "cancelled load is not resurrected by a tick");
                Same("hall", w.Adapter.ActiveMapId, "tick after cancel keeps the map");

                var older = w.Adapter.Load("yard", "first");
                var newer = w.Adapter.Load("hall", "second");
                Same(OperationStatus.Cancelled, older.Status, "newer load supersedes the pending one");
                Same(withActive + 1, GridCount(), "only the newest temporary root exists");
                w.Adapter.Tick(0.016, 0.016);
                Same(OperationStatus.Succeeded, newer.Status, "newest load commits");
                Same(withActive, GridCount(), "reload replaced the old root");
                if (!UnityEngine.Application.isPlaying) Ensure(activeRoot == null, "old root destroyed after replacement");

                var yard = w.LoadNow("yard");
                Same(OperationStatus.Succeeded, yard.Status, "yard loads");
                Same("yard", w.Adapter.ActiveMapId, "active map switched");
                Same(withActive, GridCount(), "still exactly one Grid");
                var mismatches = w.Adapter.FindColliderMismatches();
                Same(0, mismatches.Count, "yard colliders agree: " + string.Join("; ", mismatches));
                yard.Cancel("late");
                Same(OperationStatus.Succeeded, yard.Status, "cancel after success has no effect");
            }
            Same(baseline, GridCount(), "dispose removes every root");
        }

        // ------------------------------------------------------------ collision tests

        private static void CollisionScenarios()
        {
            var baseline = GridCount();
            using (var w = new World())
            {
                Same(OperationStatus.Succeeded, w.LoadNow("hall").Status, "hall loads for collider agreement");
                var port = w.Port;
                var through = new Position("hall", 4.5, 4.5);

                var r = port.Sweep(new Position("hall", 4.5, 1.5), through, 0.3);
                Ensure(!r.Blocked && r.End.Equals(through), "narrow open doorway passes a radius 0.3 actor");
                r = port.Sweep(new Position("hall", 4.5, 1.5), through, 0.5);
                Ensure(!r.Blocked && r.End.Equals(through), "a diameter-1 actor fits the 1-tile doorway exactly");

                r = port.Sweep(new Position("hall", 4.5, 1.5), through, 0.6);
                Ensure(r.Blocked, "a radius 0.6 actor does not fit");
                Near(3 - Math.Sqrt(0.11), r.End.Y, 1e-4, "wide actor stops at the doorway corners");
                Ensure(r.BlockerId.StartsWith("tile:"), "wide actor reports a tile blocker");

                r = port.Sweep(new Position("hall", 3.5, 1.5), new Position("hall", 3.5, 4.5), 0.3);
                Ensure(r.Blocked, "misaligned actor hits the wall");
                Near(2.7, r.End.Y, 1e-5, "stops before the wall face");
                Near(3.5, r.End.X, 0, "no sideways drift");
                Same("tile:3,3", r.BlockerId, "wall blocker id");
                Ensure(port.IsPassable(r.End, 0.3), "stop position is passable");

                var rev = w.Adapter.NavRevision;
                w.Layout.SetPortalOpen("door-hall", false);
                Ensure(w.Adapter.NavRevision > rev, "closing the door changes NavRevision");
                r = port.Sweep(new Position("hall", 4.5, 1.5), through, 0.3);
                Ensure(r.Blocked, "closed portal blocks the doorway");
                Same("portal:door-hall", r.BlockerId, "closed portal blocker id");
                Near(2.7, r.End.Y, 1e-5, "stops before the closed door");
                Ensure(!port.IsPassable(new Position("hall", 4.5, 3.5), 0.3), "closed door tile not passable");
                Same(0, w.Adapter.FindColliderMismatches().Count, "colliders agree with the closed door");
                w.Layout.SetPortalOpen("door-hall", true);
                r = port.Sweep(new Position("hall", 4.5, 1.5), through, 0.3);
                Ensure(!r.Blocked, "reopened door passes again");

                w.Layout.SetBlocked("hall", 4, 1, true);
                r = port.Sweep(new Position("hall", 4.5, 0.5), new Position("hall", 4.5, 2.5), 0.3);
                Ensure(r.Blocked, "dynamic blocked tile stops the sweep");
                Near(0.7, r.End.Y, 1e-5, "stops before the dynamic blocker");
                Same("tile:4,1", r.BlockerId, "dynamic blocker id");
                Same(0, w.Adapter.FindColliderMismatches().Count, "colliders agree with the dynamic blocker");
                w.Layout.SetBlocked("hall", 4, 1, false);

                r = port.Sweep(new Position("hall", 0.5, 1.5), new Position("hall", 500.5, 1.5), 0.3);
                Ensure(r.Blocked, "high delta stops at the pillar");
                Near(6.7, r.End.X, 1e-5, "high delta stop position");
                Same("tile:7,1", r.BlockerId, "high delta blocker");
                r = port.Sweep(new Position("hall", 0.5, 1.5), new Position("hall", 1e12, 1.5), 0.3);
                Near(6.7, r.End.X, 1e-5, "extreme delta still stops at the pillar");
                r = port.Sweep(new Position("hall", 4.5, 0.5), new Position("hall", 4.5, 4000), 0.3);
                Ensure(r.Blocked, "huge delta through the doorway ends at the map bounds");
                Near(5.7, r.End.Y, 1e-5, "upper bound stop");
                Same(GridCollisionPort.BoundsBlocker, r.BlockerId, "bounds blocker id");
                r = port.Sweep(new Position("hall", 4.5, 0.5), new Position("hall", 4.5, -1e7), 0.3);
                Near(0.3, r.End.Y, 1e-5, "lower bound stop");

                Ensure(port.IsPassable(new Position("hall", 4.5, 3.5), 0.3), "open doorway passable");
                Ensure(!port.IsPassable(new Position("hall", 3.5, 3.5), 0.3), "wall tile not passable");
                Ensure(!port.IsPassable(new Position("hall", 0.1, 0.1), 0.3), "footprint outside bounds not passable");
                Ensure(port.IsPassable(new Position("hall", 0.3, 0.3), 0.3), "touching the bounds is allowed");

                var start = new Position("hall", 4.5, 1.5);
                var zero = port.Sweep(start, start, 0.3);
                Ensure(!zero.Blocked && zero.End.Equals(start), "zero-length free move");
                var inside = new Position("hall", 0.5, 3.5);
                zero = port.Sweep(inside, inside, 0.3);
                Ensure(zero.Blocked && zero.End.Equals(inside), "zero-length move from an overlapping start is blocked");

                var geometry = w.Layout.Find("hall");
                var disagreements = new List<string>();
                for (var y = 0; y < geometry.Height; y++)
                    for (var x = 0; x < geometry.Width; x++)
                    {
                        var centre = new Position("hall", x + 0.5, y + 0.5);
                        if (port.IsPassable(centre, 0.25) == w.Layout.IsBlocked("hall", x, y)) disagreements.Add(x + "," + y);
                    }
                Same(0, disagreements.Count, "port and logical grid agree per cell: " + string.Join(" ", disagreements));
                Same(0, w.Adapter.FindColliderMismatches().Count, "rendered colliders agree at the end");
            }
            Same(baseline, GridCount(), "dispose removes every root");
        }

        private static void CollisionInactiveAndGuards()
        {
            var baseline = GridCount();
            using (var w = new World())
            {
                Same(OperationStatus.Succeeded, w.LoadNow("hall").Status, "hall active");
                var port = w.Port;

                Ensure(port.IsPassable(new Position("yard", 2.5, 2.5), 0.3), "inactive map free cell passable");
                Ensure(!port.IsPassable(new Position("yard", 1.5, 1.5), 0.3), "inactive map pillar blocked");
                var r = port.Sweep(new Position("yard", 0.5, 1.5), new Position("yard", 4.5, 1.5), 0.3);
                Ensure(r.Blocked, "sweep on inactive map stops at the pillar");
                Near(0.7, r.End.X, 1e-5, "inactive map stop position");
                Same("tile:1,1", r.BlockerId, "inactive map blocker");

                r = port.Sweep(new Position("yard", 2.5, 2.5), new Position("yard", 2.5, -1), 0.3);
                Near(0.3, r.End.Y, 1e-5, "open gate ends at the bounds");
                Same(GridCollisionPort.BoundsBlocker, r.BlockerId, "open gate bounds blocker");
                w.Layout.SetPortalOpen("gate-yard", false);
                r = port.Sweep(new Position("yard", 2.5, 2.5), new Position("yard", 2.5, -1), 0.3);
                Near(1.3, r.End.Y, 1e-5, "closed gate stops the sweep");
                Same("portal:gate-yard", r.BlockerId, "closed gate blocker");
                Same("hall", w.Adapter.ActiveMapId, "queries never change the active map");

                var unknown = new Position("ghost", 1, 1);
                Ensure(!port.IsPassable(unknown, 0.1), "unknown map is not passable");
                r = port.Sweep(unknown, new Position("ghost", 2, 2), 0.1);
                Ensure(r.Blocked && r.End.Equals(unknown), "unknown map sweep is blocked at the start");

                Throws<ArgumentException>(() => port.Sweep(new Position("hall", 1, 1), new Position("yard", 1, 1), 0.3), "cross-map sweep");
                Throws<ArgumentOutOfRangeException>(() => port.Sweep(new Position("hall", 1, 1), new Position("hall", 2, 1), -0.1), "negative radius");
                Throws<ArgumentOutOfRangeException>(() => port.IsPassable(new Position("hall", 1, 1), double.NaN), "NaN radius");
                Throws<ArgumentException>(() => port.IsPassable(default(Position), 0.3), "default position");
            }
            Same(baseline, GridCount(), "dispose removes every root");
        }

        private static double Sq(double v) => v * v;

        private static void CollisionRandomSweeps()
        {
            var parsed = MapLayout.Parse(Maps(), "random");
            Ensure(parsed.Succeeded, "maps parse");
            var port = new GridCollisionPort(parsed.Value);
            uint seed = 12345;
            Func<double> rnd = () =>
            {
                unchecked { seed = seed * 1664525u + 1013904223u; }
                return (seed >> 8) / (double)(1 << 24);
            };

            foreach (var radius in new[] { 0.3, 0.45 })
            {
                var tested = 0;
                for (var i = 0; i < 400; i++)
                {
                    var start = new Position("hall", rnd() * 10, rnd() * 6);
                    if (!port.IsPassable(start, radius)) continue;
                    var goal = new Position("hall", rnd() * 14 - 2, rnd() * 10 - 2);
                    var r = port.Sweep(start, goal, radius);
                    tested++;
                    Ensure(port.IsPassable(r.End, radius), "sweep end is always a passable position");

                    var len = Math.Sqrt(Sq(goal.X - start.X) + Sq(goal.Y - start.Y));
                    var steps = Math.Max(1, (int)Math.Ceiling(len / 0.01));
                    var firstBad = -1.0;
                    for (var s = 0; s <= steps; s++)
                    {
                        var t = s / (double)steps;
                        var sample = new Position("hall", start.X + (goal.X - start.X) * t, start.Y + (goal.Y - start.Y) * t);
                        if (port.IsPassable(sample, radius)) continue;
                        firstBad = t * len;
                        break;
                    }

                    var travelled = Math.Sqrt(Sq(r.End.X - start.X) + Sq(r.End.Y - start.Y));
                    if (!r.Blocked)
                    {
                        Ensure(firstBad < 0, "a free sweep never crosses an obstruction");
                        Ensure(r.End.Equals(goal), "a free sweep ends at the target");
                    }
                    else if (firstBad >= 0)
                    {
                        Ensure(travelled <= firstBad + 1e-6, "sweep stops no later than the first sampled penetration (no tunneling)");
                    }
                }
                Ensure(tested > 100, "enough random samples exercised for radius " + radius);
            }
        }
    }
}
#endif
