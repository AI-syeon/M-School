using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicCampus.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Tilemaps;

namespace MagicCampus.Game
{
    /// <summary>
    /// WindowsPlayer scenario run by --probe: drives the real GameRoot with a virtual keyboard and the root's public panel/menu
    /// calls, checks Core state, saves screenshots, writes result.json and quits (exit 0 pass, 1 fail).
    /// </summary>
    public sealed class Probe : MonoBehaviour
    {
        sealed class Check
        {
            public string Name, Detail;
            public bool Pass;
        }

        readonly List<Check> checks = new List<Check>();
        readonly List<string> errors = new List<string>();
        readonly List<string> steps = new List<string>();
        readonly List<string> branch = new List<string>();
        GameRoot root;
        string mode, dir, ev;
        Keyboard kb;
        int minuteAtKey;

        Core.Game G => root.Game;

        IEnumerator Start()
        {
            root = GetComponent<GameRoot>();
            mode = root.ProbeCase;
            dir = Path.GetFullPath(root.EvidenceDir ?? Path.Combine(UnityEngine.Application.persistentDataPath, "probe-" + mode));
            Directory.CreateDirectory(dir);
            yield return Guard(Run());
            if (errors.Count > 0) yield return Guard(Shot("failure"));
            var pass = errors.Count == 0 && checks.Count > 0 && checks.All(c => c.Pass);
            if (G != null) File.WriteAllText(Path.Combine(dir, "final.json"), SaveFile.ToJson(G.State), new UTF8Encoding(false));
            var result = new { mode, pass, checks, errors, steps, unity = UnityEngine.Application.unityVersion, platform = UnityEngine.Application.platform.ToString() };
            File.WriteAllText(Path.Combine(dir, "result.json"), JsonConvert.SerializeObject(result, Formatting.Indented), new UTF8Encoding(false));
            UnityEngine.Application.Quit(pass ? 0 : 1);
        }

        IEnumerator Run()
        {
            if (G == null) throw new InvalidOperationException("game did not start: " + root.Error);
            kb = InputSystem.AddDevice<Keyboard>("ProbeKeyboard");
            ev = mode == "a10" ? "event.mira_snack" : "event.snack";
            yield return null;
            yield return Case();
        }

        IEnumerator Case()
        {
            switch (mode)
            {
                case "success-notes": return Ordinary("notes", 20);
                case "success-tea": return Ordinary("tea", 20);
                case "moved": return Ordinary("notes", 20);
                case "a10": return Ordinary("props", 30);
                case "intro-false": return IntroFalse();
                case "cancel": return Cancel();
                case "day-save": return DaySave();
                case "ui-long": case "ui-200": return Ui();
                default: throw new InvalidOperationException("unknown probe case " + mode);
            }
        }

        IEnumerator Ordinary(string choice, int reward)
        {
            var wallet0 = G.State.Wallet;
            if (mode != "a10") yield return WalkToSerinAndInteract();
            Step("root.AcceptAndStart(" + ev + ")");
            root.AcceptAndStart(ev);
            Expect("event.started", G.Event != null, root.Status);
            var minute0 = G.State.Minute;
            yield return PlayEvent(choice, false);
            Expect("wallet.plus-" + reward, G.State.Wallet == wallet0 + reward, wallet0 + " -> " + G.State.Wallet);
            Expect("clock.plus-cost", minuteAtKey == minute0 + G.Content.Event(ev).CostMinutes, minute0 + " -> " + minuteAtKey + " in the frame the last line closed");
            Expect("completed", G.State.Completed.Contains(ev), string.Join(",", G.State.Completed));
            Expect("appointment.removed", !G.State.Appointments.ContainsKey(ev), string.Join(",", G.State.Appointments.Keys));
            var expected = Command.Parse(G.Content.Event(ev).Forks[choice]).Where(c => c.Name == "say").Select(c => c.Args[1]).ToList();
            Expect("branch.only-chosen", branch.SequenceEqual(expected), "expected " + string.Join(" | ", expected) + "; shown " + string.Join(" | ", branch));
            Expect("status.completed", root.Status == "함께한 시간을 기억했습니다.", root.Status);
            yield return Frames(30);
            yield return Shot("final-world");
            var cam = Camera.main;
            var ppu = cam.pixelHeight / (2.0 * cam.orthographicSize);
            var unit = root.View.PixelsPerUnit;
            Expect("render.integer-pixel-scale", ppu >= unit && Math.Abs(ppu - Math.Round(ppu)) < 1e-3 && (long)Math.Round(ppu) % unit == 0, "screen pixels per unit " + ppu);
            Expect("render.tilemap", FindObjectsByType<Tilemap>().Length == 1, FindObjectsByType<Tilemap>().Length + " tilemaps");
            var wallet1 = G.State.Wallet;
            Expect("retry.refused", !G.StartEvent(ev) && G.Event == null && G.State.Wallet == wallet1, "wallet " + G.State.Wallet);
        }

        IEnumerator IntroFalse()
        {
            var before = SaveFile.ToJson(G.State);
            Step("Game.StartEvent(" + ev + ")");
            Expect("intro-false.refused", !G.StartEvent(ev) && G.Event == null && !G.Hidden, "event " + G.Event?.Def.Id);
            Expect("intro-false.state-unchanged", SaveFile.ToJson(G.State) == before, SaveFile.ToJson(G.State));
            yield return Frames(10);
            Expect("intro-false.no-save", !File.Exists(root.SavePath), root.SavePath);
            yield return Shot("intro-false");
        }

        IEnumerator Cancel()
        {
            var wallet0 = G.State.Wallet;
            var before = Spots();
            Step("root.AcceptAndStart(" + ev + ")");
            root.AcceptAndStart(ev);
            var minute0 = G.State.Minute;
            yield return PlayEvent("notes", true);
            Expect("cancel.event-ended", G.Event == null && G.Dialogue == null && !G.Hidden, "event " + G.Event?.Def.Id);
            Expect("cancel.no-effects", G.State.Wallet == wallet0 && minuteAtKey == minute0 && !G.State.Completed.Contains(ev), "wallet " + G.State.Wallet + " minute " + minute0 + " -> " + minuteAtKey + " in the Esc frame");
            Expect("cancel.accepted-kept", G.State.Appointments.TryGetValue(ev, out var a) && a == Appointment.Accepted, a.ToString());
            Expect("cancel.actors-restored", Spots() == before, before + " vs " + Spots());
            G.CancelEvent();
            Expect("cancel.repeat-is-noop", G.Event == null && Spots() == before, Spots());
            yield return Frames(10);
            yield return Shot("cancel");
        }

        IEnumerator DaySave()
        {
            root.AcceptAndStart(ev);
            yield return PlayEvent("notes", false);
            var wallet = G.State.Wallet;
            Step("root.EndDay()");
            root.EndDay();
            Expect("day-save.file", File.Exists(root.SavePath), root.SavePath);
            var saved = SaveFile.Read(root.SavePath);
            Expect("day-save.contents", saved != null && saved.Day == 1 && saved.Minute == Clock.DayStart && saved.Wallet == wallet && saved.Completed.Contains(ev) && !saved.Appointments.ContainsKey(ev),
                File.Exists(root.SavePath) ? File.ReadAllText(root.SavePath) : "missing");
            var before = G;
            yield return Press(Key.F9);
            Expect("day-save.reloaded-new-game", !ReferenceEquals(G, before) && G.State.Day == 1 && G.State.Wallet == wallet && G.State.Completed.Contains(ev), "wallet " + G.State.Wallet + " day " + G.State.Day);
            Expect("day-save.event-refused", !G.StartEvent(ev) && G.Event == null && G.State.Wallet == wallet, "wallet " + G.State.Wallet);
            yield return Frames(10);
            yield return Shot("day-save");
        }

        IEnumerator Ui()
        {
            if (mode == "ui-200") root.TextScale = 2f;
            var text = string.Concat(Enumerable.Repeat("마법 캠퍼스의 첫 연결 대화를 길게 확인하는 시험 문장입니다. ", 20));
            G.Content.Events.Add(new EventDef { Id = "event.ui", Script = "say 안내 \"" + text + "\"" });
            G.Content.Recipes.Add(new RecipeDef { EventId = "event.ui" });
            Step("Game.StartEvent(event.ui) with a " + text.Length + " character line");
            Expect("ui.started", G.StartEvent("event.ui"), "probe-only event");
            yield return Until(() => G.Dialogue != null, "long line");
            foreach (var size in new[] { new Vector2Int(640, 480), new Vector2Int(960, 640) })
            {
                Screen.SetResolution(size.x, size.y, false);
                yield return Frames(10);
                Expect("ui.resolution " + size.x + "x" + size.y, Screen.width == size.x && Screen.height == size.y, Screen.width + "x" + Screen.height);
                yield return Shot(mode + "-" + size.x + "x" + size.y);
            }
            yield return Press(Key.DownArrow);
            yield return Press(Key.DownArrow);
            yield return Frames(2);
            Expect("ui.paged", root.Box.ScrollLines > 0 && G.Dialogue != null, "scroll lines " + root.Box.ScrollLines);
            yield return Shot(mode + "-paged");
            yield return Press(Key.Enter);
            yield return Until(() => G.Event == null, "line closed");
            Expect("ui.closed", G.Dialogue == null && G.State.Completed.Contains("event.ui"), "dialogue " + (G.Dialogue != null));
        }

        /// <summary>Holds W until the player stands within reach of Serin, then presses E: the real movement and interact path.</summary>
        IEnumerator WalkToSerinAndInteract()
        {
            var start = G.Player.Y;
            Step("hold W");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return Until(() => DistanceTo("npc.serin") <= 1.2, "player walked to Serin", 10);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(2);
            Expect("input.walk-north", G.Player.Y > start + 1 && G.Player.Facing == "north", "y " + start + " -> " + G.Player.Y + " facing " + G.Player.Facing);
            yield return Press(Key.E);
            yield return Frames(2);
            Expect("interact.invite-panel", root.PanelOpen && root.PanelEvents.Contains(ev), string.Join(",", root.PanelEvents));
            yield return Shot("invite");
        }

        /// <summary>Plays the running event through the keyboard: Enter while the first mover walks, Enter on lines, arrows and Enter on the choice (or Esc to cancel).</summary>
        IEnumerator PlayEvent(string choice, bool cancel)
        {
            var script = Command.Parse(G.Content.Event(ev).Script);
            var move = script.First(c => c.Name == "move");
            var firstLine = script.First(c => c.Name == "say").Args[1];
            yield return Until(() => !G.Hidden && G.Actor(move.Args[0]).Moving, "first move under way");
            yield return Press(Key.Enter);
            yield return Until(() => G.Dialogue != null, "first line");
            Expect("early-enter-ignored", G.Dialogue.Text == firstLine, G.Dialogue.Text);
            var dest = G.Content.Destination(move.Args[1]);
            Expect("mover-at-destination", World.Distance(G.Actor(move.Args[0]), dest.Position) <= dest.Radius, move.Args[0] + " at " + G.Actor(move.Args[0]).X + "," + G.Actor(move.Args[0]).Y);
            yield return Press(Key.Enter);
            yield return Until(() => G.Dialogue?.Choices != null, "choice");
            var c = script.First(x => x.Name == "choice");
            var index = c.Args.Skip(1).Where((_, i) => i % 2 == 0).ToList().IndexOf(choice);
            Expect("choice.found", index >= 0, choice + " in " + string.Join(",", c.Args));
            while (root.Box.Focus != index) yield return Press(Key.DownArrow);
            yield return Frames(2);
            yield return Shot("choose");
            if (cancel)
            {
                yield return Press(Key.Escape);
                yield return Frames(2);
                yield break;
            }
            yield return Press(Key.Enter);
            Dialogue last = null;
            var deadline = Time.realtimeSinceStartup + 30;
            while (G.Event != null)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("branch did not finish");
                if (G.Dialogue != null && !ReferenceEquals(G.Dialogue, last))
                {
                    last = G.Dialogue;
                    branch.Add(last.Text);
                    yield return Frames(2);
                    if (branch.Count == 1) yield return Shot("branch");
                    yield return Press(Key.Enter);
                }
                yield return null;
            }
        }

        double DistanceTo(string npcId)
        {
            var npc = G.Actor(npcId);
            return World.Distance(G.Player, new Spot { MapId = npc.MapId, X = npc.X, Y = npc.Y });
        }

        string Spots() => string.Join("|", G.Actors.Select(a => a.Id + "=" + a.MapId + ":" + a.X.ToString("F3") + ":" + a.Y.ToString("F3")));

        IEnumerator Press(Key key)
        {
            Step("key " + key);
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            minuteAtKey = G.State.Minute;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        IEnumerator Until(Func<bool> condition, string what, double seconds = 60)
        {
            var t0 = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - t0 > seconds) throw new TimeoutException("timeout waiting for " + what);
                yield return null;
            }
        }

        IEnumerator Frames(int n)
        {
            var end = Time.frameCount + n;
            while (Time.frameCount < end) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return Until(() => File.Exists(path) && new FileInfo(path).Length > 0, "screenshot " + name, 15);
        }

        /// <summary>Runs nested coroutines inside try/catch so an exception becomes an error in result.json instead of a hang.</summary>
        IEnumerator Guard(IEnumerator body)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                object current;
                try
                {
                    if (!stack.Peek().MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    current = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    errors.Add(e.ToString());
                    yield break;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
        }

        void Expect(string name, bool pass, string detail) => checks.Add(new Check { Name = name, Pass = pass, Detail = detail });

        void Step(string what) => steps.Add(Time.frameCount + " " + what);
    }
}
