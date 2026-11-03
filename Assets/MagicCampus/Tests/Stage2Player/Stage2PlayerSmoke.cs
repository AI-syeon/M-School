using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MagicCampus.Contracts;
using MagicCampus.Runtime;
using MagicCampus.UnityAdapters.Input;
using MagicCampus.UnityAdapters.Maps;
using MagicCampus.UnityAdapters.Collision;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MagicCampus.Tests.Stage2Player
{
    // Codex verification harness. Runs only with an explicit diagnostic command-line flag.
    public sealed class Stage2PlayerSmoke : MonoBehaviour
    {
        [Serializable] public sealed class Report { public string unityVersion; public string platform; public bool passed; public int assertions; public string error; }
        static readonly List<InputDevice> devices = new List<InputDevice>();
        MapAdapter maps;
        int assertions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--stage2-player-smoke") < 0) return;
            var host = new GameObject("Stage2 player acceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<Stage2PlayerSmoke>();
        }

        IEnumerator Start()
        {
            yield return null;
            var report = new Report { unityVersion = UnityEngine.Application.unityVersion, platform = UnityEngine.Application.platform.ToString() };
            var previous = InputSystem.settings.backgroundBehavior;
            try
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                Verify();
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
            finally
            {
                maps?.Dispose();
                foreach (var device in devices) if (device.added) InputSystem.RemoveDevice(device);
                InputSystem.settings.backgroundBehavior = previous;
            }
            yield return null; // allow actual deferred Unity object destruction
            report.assertions = assertions;
            var output = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "stage2-player.json");
            foreach (var arg in Environment.GetCommandLineArgs()) if (arg.StartsWith("--result=")) output = arg.Substring(9);
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("Stage2 Windows player acceptance: " + report.passed + " assertions=" + assertions);
            UnityEngine.Application.Quit(report.passed ? 0 : 1);
        }

        void Verify()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>(); devices.Add(keyboard);
            var gamepad = InputSystem.AddDevice<Gamepad>(); devices.Add(gamepad);
            var captured = false;
            var input = new InputAdapter(InputAdapterConfig.Default, () => captured);
            long tick = 0;
            var directions = new[] { new[] { Key.W }, new[] { Key.W, Key.D }, new[] { Key.D }, new[] { Key.S, Key.D }, new[] { Key.S }, new[] { Key.S, Key.A }, new[] { Key.A }, new[] { Key.W, Key.A } };
            foreach (var keys in directions)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update();
                var frame = input.Poll(tick++);
                Check(Math.Abs(frame.MoveX * frame.MoveX + frame.MoveY * frame.MoveY - 1) < 1e-6, "8-way normalized direction");
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right }); InputSystem.Update();
            Check(input.Poll(tick++).MoveX > 0.9, "player virtual gamepad drives adapter");
            InputSystem.RemoveDevice(gamepad); InputSystem.Update();
            Check(Math.Abs(input.Poll(tick++).MoveX) < 1e-6, "player device detach resets movement");
            captured = true;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.E)); InputSystem.Update();
            var blocked = input.Poll(tick++);
            Check(blocked.MoveX == 0 && blocked.MoveY == 0 && !Contains(blocked.Pressed, "interact"), "UI capture prevents world input");

            var definition = DataNode.FromList(new[] { Map("map.a", true), Map("map.b", false) });
            var parsed = MapLayout.Parse(definition, "stage2-player-fixture");
            Check(parsed.Succeeded, "real player map definitions accepted");
            var pump = new CompletionPump(); var factory = new HandleFactory(pump);
            maps = new MapAdapter(parsed.Value, factory, new MapAdapterConfig(null, (id, root) => id == "map.b" ? "injected load failure" : null));
            var loaded = maps.Load("map.a", "player-smoke"); maps.Tick(0, 0); pump.AdvanceTick();
            Check(loaded.Status == OperationStatus.Succeeded && maps.ActiveMapId == "map.a", "actual player tilemap load");
            Check(maps.FindColliderMismatches().Count == 0, "player TilemapCollider2D matches geometry");
            var collision = new GridCollisionPort(parsed.Value);
            var hit = collision.Sweep(new Position("map.a", 0.5, 2.5), new Position("map.a", 4.5, 2.5), 0.2);
            Check(hit.Blocked && hit.End.X < 1.81, "player high-delta sweep stops before wall");
            parsed.Value.SetPortalOpen("portal.ab", false);
            Check(!collision.IsPassable(new Position("map.a", 4.5, 1.5), 0.2), "closed door blocks player footprint");
            Check(maps.FindColliderMismatches().Count == 0, "closed door collider agrees");
            var failed = maps.Load("map.b", "player-smoke"); maps.Tick(0, 0);
            Check(failed.Status == OperationStatus.Failed && maps.ActiveMapId == "map.a", "failed map keeps previous player map");
            var cancelled = maps.Load("map.b", "player-smoke"); cancelled.Cancel("twice"); cancelled.Cancel("twice"); maps.Tick(0, 0);
            Check(cancelled.Status == OperationStatus.Cancelled && maps.ActiveMapId == "map.a", "map cancel cleans up and retains valid map");
            Check(factory.LiveCount == 0, "actual player handles terminate without leak");
        }

        static bool Contains(IReadOnlyList<string> values, string value) { foreach (var item in values) if (item == value) return true; return false; }
        void Check(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException(message); }
        static DataNode Map(string id, bool portal)
        {
            var fields = new Dictionary<string, DataNode> {
                ["id"] = DataNode.FromString(id), ["width"] = DataNode.FromInteger(5), ["height"] = DataNode.FromInteger(5),
                ["blocked"] = DataNode.FromList(new[] { DataNode.FromList(new[] { DataNode.FromInteger(2), DataNode.FromInteger(2) }) }),
                ["portals"] = DataNode.FromList(portal ? new[] { DataNode.FromObject(new Dictionary<string, DataNode> {
                    ["id"] = DataNode.FromString("portal.ab"), ["x"] = DataNode.FromNumber(4.5), ["y"] = DataNode.FromNumber(1.5), ["open"] = DataNode.True,
                    ["to"] = DataNode.FromObject(new Dictionary<string, DataNode> { ["mapId"] = DataNode.FromString("map.b"), ["x"] = DataNode.FromNumber(0.5), ["y"] = DataNode.FromNumber(1.5) })
                }) } : new DataNode[0])
            };
            return DataNode.FromObject(fields);
        }
    }
}
