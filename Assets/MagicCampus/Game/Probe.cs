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
                case "pass-out": return PassOut();
                case "ui-long": case "ui-200": return Ui();
                case "spell-lantern": return SpellLantern();
                case "spell-context": return SpellContext();
                case "spell-quick": return SpellQuick();
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
            Step("place the player next to the bed");
            World.Place(G.Player, new Spot { MapId = G.Content.Bed.MapId, X = G.Content.Bed.X + 0.5, Y = G.Content.Bed.Y + 0.5 });
            yield return Press(Key.E);
            yield return Frames(2);
            Expect("sleep.prompt", root.SleepPromptOpen && G.State.Day == 0, "prompt " + root.SleepPromptOpen);
            yield return Shot("sleep-prompt");
            yield return Press(Key.Enter);
            Expect("sleep.woke-at-bed", G.LastDayEnd == DayEnd.Slept && !root.SleepPromptOpen && World.Distance(G.Player, G.Content.Bed) < 1e-6,
                G.Player.MapId + " " + G.Player.X + "," + G.Player.Y);
            yield return Frames(10);
            yield return Shot("morning");
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

        IEnumerator PassOut()
        {
            Step("set the clock to 01:58 of the night");
            G.State.Minute = Clock.PassOutAt - 2;
            yield return Frames(2);
            Expect("pass-out.hud-hour", G.State.Day == 0 && G.State.Minute >= Clock.MinutesPerDay, "minute " + G.State.Minute);
            yield return Shot("night");
            yield return Until(() => G.State.Day == 1, "02:00", 10);
            yield return Frames(10);
            var saved = SaveFile.Read(root.SavePath);
            Expect("pass-out.day-ended", G.LastDayEnd == DayEnd.PassedOut && G.State.Minute == Clock.DayStart && World.Distance(G.Player, G.Content.Bed) < 1e-6,
                "minute " + G.State.Minute + " at " + G.Player.X + "," + G.Player.Y);
            Expect("pass-out.saved", saved != null && saved.Day == 1, root.SavePath);
            Expect("pass-out.status", root.Status.Contains("쓰러져"), root.Status);
            yield return Shot("pass-out");
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

        const string Sign = "target.signboard";

        string Tags(string target) => string.Join(",", G.Magic.Tags(target).OrderBy(x => x));

        /// <summary>Holds W from the start position until the player faces the dark signboard from the side, about 1.3 tiles away.</summary>
        IEnumerator FaceSign()
        {
            Step("hold W");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return Until(() => G.Player.Y >= 1.7, "walk toward the signboard", 10);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(3);
            Expect("sign.faced", G.Player.Facing == "north" && G.Magic.Aim(3)?.Id == Sign, "facing " + G.Player.Facing + " at " + G.Player.X + "," + G.Player.Y + " aim " + G.Magic.Aim(3)?.Id);
        }

        /// <summary>Several keys pressed in the same frame, then released.</summary>
        IEnumerator PressTogether(params Key[] keys)
        {
            Step("keys together " + string.Join("+", keys));
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        /// <summary>One key held down for n frames: the game must take it as a single press.</summary>
        IEnumerator Hold(Key key, int frames)
        {
            Step("hold " + key + " for " + frames + " frames");
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return Frames(frames);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        IEnumerator PressAll(params Key[] keys)
        {
            foreach (var key in keys) yield return Press(key);
            yield return Frames(2);
        }

        static string Runes(Core.Game g) => string.Join(",", g.Magic.Runes);

        IEnumerator SpellLantern()
        {
            Expect("lantern.start", G.State.KnownSpells.Contains("spell.lantern") && Tags(Sign) == "dark" && !G.Magic.Open && G.Magic.Casts == 0, "known " + string.Join(",", G.State.KnownSpells));
            yield return FaceSign();
            yield return Shot("before");

            yield return PressAll(Key.Q);
            Expect("lantern.q-opens", G.Magic.Open && G.Magic.Runes.Count == 0 && G.Magic.Casts == 0, "open " + G.Magic.Open);
            yield return Shot("incantation-open");

            yield return PressTogether(Key.Digit1, Key.Digit2);
            yield return Frames(2);
            Expect("lantern.two-runes-at-once-ignored", Runes(G) == "" && G.Magic.Open && root.MagicMessage.Contains("차례로 눌러"), "runes [" + Runes(G) + "] message " + root.MagicMessage);
            yield return PressTogether(Key.Digit1, Key.Enter);
            yield return Frames(2);
            Expect("lantern.rune-and-enter-at-once-ignored", Runes(G) == "" && G.Magic.Open && G.Magic.Casts == 0 && Tags(Sign) == "dark", "runes [" + Runes(G) + "] casts " + G.Magic.Casts);
            yield return Shot("simultaneous-ignored");

            yield return PressAll(Key.Digit1, Key.Digit1);
            Expect("lantern.wrong-order-clears", Runes(G) == "" && G.Magic.Open && G.Magic.Casts == 0 && root.MagicMessage.Contains("비웠"), "runes [" + Runes(G) + "] message " + root.MagicMessage);
            yield return Shot("wrong-order");
            yield return PressAll(Key.Enter);
            Expect("lantern.empty-confirm-stays-open", G.Magic.Open && G.Magic.Casts == 0 && Tags(Sign) == "dark", "casts " + G.Magic.Casts);

            yield return Hold(Key.Digit1, 20);
            Expect("lantern.hold-is-one-rune", Runes(G) == "1", "runes [" + Runes(G) + "]");
            yield return PressAll(Key.Enter);
            Expect("lantern.prefix-only-stays-open", G.Magic.Open && G.Magic.Casts == 0 && Tags(Sign) == "dark", "casts " + G.Magic.Casts + " message " + root.MagicMessage);
            yield return PressAll(Key.Digit2);
            Expect("lantern.confirmable", Runes(G) == "1,2" && G.Magic.Exact?.Id == "spell.lantern" && Tags(Sign) == "dark", "runes [" + Runes(G) + "]");
            yield return Shot("runes-1-2");

            yield return PressAll(Key.Enter);
            var lit = G.Magic.Last;
            Expect("lantern.lit", Tags(Sign) == "lit" && lit != null && lit.Ok && lit.ReactionId == "react.lantern_dark" && lit.TargetId == Sign, "tags " + Tags(Sign) + " message " + lit?.Message);
            Expect("lantern.single-cast", G.Magic.Casts == 1 && !G.Magic.Open && Runes(G) == "", "casts " + G.Magic.Casts);
            Expect("lantern.situation-logged", G.State.SituationLog.TryGetValue("spell.lantern", out var log) && log.SequenceEqual(new[] { "lantern.dark" }), JsonConvert.SerializeObject(G.State.SituationLog));
            yield return Shot("lit");

            yield return Hold(Key.Enter, 20);
            yield return PressAll(Key.Digit1, Key.Digit2);
            Expect("lantern.nothing-without-incantation", G.Magic.Casts == 1 && !G.Magic.Open, "casts " + G.Magic.Casts);

            foreach (var key in new[] { Key.Escape, Key.X, Key.Backspace, Key.Q })
            {
                yield return PressAll(Key.Q);
                yield return PressAll(Key.Digit1);
                Expect("lantern.cancel-" + key, G.Magic.Open && Runes(G) == "1", "runes [" + Runes(G) + "]");
                yield return PressAll(key);
                Expect("lantern.cancel-" + key + "-closes", !G.Magic.Open && Runes(G) == "" && !root.MenuOpen && !root.PanelOpen, "open " + G.Magic.Open + " menu " + root.MenuOpen);
            }
            Expect("lantern.cancels-cast-nothing", G.Magic.Casts == 1 && Tags(Sign) == "lit", "casts " + G.Magic.Casts);
        }

        IEnumerator SpellContext()
        {
            G.State.QuickCastUnlocked = true;
            G.Magic.SetSlot(0, "spell.lantern");
            yield return FaceSign();

            yield return PressAll(Key.Q, Key.Digit1);
            Expect("context.open-before-event", G.Magic.Open && Runes(G) == "1", "runes [" + Runes(G) + "]");
            Step("root.AcceptAndStart(" + ev + ")");
            root.AcceptAndStart(ev);
            yield return Frames(2);
            Expect("context.event-start-clears", G.Event != null && !G.Magic.Open && Runes(G) == "", "event " + G.Event?.Def.Id + " open " + G.Magic.Open);

            yield return Until(() => G.Dialogue != null, "first line");
            yield return PressAll(Key.Q, Key.Digit1);
            Expect("context.dialogue-line-ignores-spell-keys", !G.Magic.Open && G.Magic.Casts == 0 && Tags(Sign) == "dark" && G.Dialogue != null, "casts " + G.Magic.Casts);
            yield return Press(Key.Enter);
            yield return Until(() => G.Dialogue?.Choices != null, "choice");
            yield return Frames(2);
            yield return PressAll(Key.Digit1);
            Expect("context.dialogue-digit-chooses", G.Dialogue == null || G.Dialogue.Choices == null, "choices " + (G.Dialogue?.Choices != null));
            Expect("context.dialogue-digit-does-not-cast", G.Magic.Casts == 0 && Tags(Sign) == "dark", "casts " + G.Magic.Casts);
            yield return Press(Key.Escape);
            yield return Frames(3);
            Expect("context.event-cancelled", G.Event == null && !G.Magic.Open, "event " + G.Event?.Def.Id);

            yield return PressAll(Key.Q, Key.Digit1);
            root.OpenMenu();
            yield return Frames(2);
            Expect("context.menu-clears", root.MenuOpen && !G.Magic.Open && Runes(G) == "", "menu " + root.MenuOpen + " open " + G.Magic.Open);
            yield return PressAll(Key.Digit1);
            Expect("context.menu-ignores-digits", G.Magic.Casts == 0 && Tags(Sign) == "dark", "casts " + G.Magic.Casts);
            yield return PressAll(Key.Escape);
            Expect("context.menu-closes", !root.MenuOpen && !G.Magic.Open, "menu " + root.MenuOpen);

            yield return PressAll(Key.Q, Key.Digit1);
            yield return PressAll(Key.K);
            Expect("context.panel-clears", root.SpellbookOpen && !G.Magic.Open && Runes(G) == "", "panel " + root.SpellbookOpen + " open " + G.Magic.Open);
            yield return PressAll(Key.Q);
            Expect("context.panel-ignores-q", !G.Magic.Open && G.Magic.Casts == 0, "open " + G.Magic.Open);
            yield return PressAll(Key.K);
            Expect("context.panel-closes", !root.SpellbookOpen, "panel " + root.SpellbookOpen);

            yield return PressAll(Key.Q, Key.Digit1);
            root.OnApplicationFocus(false);
            Expect("context.focus-loss-clears", !G.Magic.Open && Runes(G) == "", "open " + G.Magic.Open);

            World.Place(G.Player, new Spot { MapId = "map.courtyard", X = 6.5, Y = 2.5 });
            yield return Frames(2);
            yield return PressAll(Key.Q, Key.Digit1);
            Expect("context.open-before-portal", G.Magic.Open && Runes(G) == "1", "runes [" + Runes(G) + "]");
            Step("hold D");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            yield return Until(() => G.MapId == "map.lounge", "portal crossing", 10);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(3);
            Expect("context.portal-clears", G.MapId == "map.lounge" && !G.Magic.Open && Runes(G) == "", "map " + G.MapId + " open " + G.Magic.Open);

            Expect("context.nothing-cast-so-far", G.Magic.Casts == 0 && Tags(Sign) == "dark", "casts " + G.Magic.Casts);
            yield return PressAll(Key.Q, Key.Digit1);
            var before = G;
            yield return Press(Key.F9);
            yield return Frames(2);
            Expect("context.f9-clears", !ReferenceEquals(G, before) && !G.Magic.Open && Runes(G) == "", "same game " + ReferenceEquals(G, before));

            yield return PressAll(Key.Q, Key.Digit1);
            var day = G.State.Day;
            root.Sleep();
            yield return Frames(2);
            Expect("context.end-day-clears", G.State.Day == day + 1 && !G.Magic.Open && Runes(G) == "", "day " + G.State.Day + " open " + G.Magic.Open);
            yield return Shot("context-final");
        }

        IEnumerator SpellQuick()
        {
            yield return FaceSign();
            Expect("quick.start-locked", !G.State.QuickCastUnlocked && G.State.QuickSlots.All(x => x == null) && G.State.KnownSpells.Contains("spell.quickcast"), SaveFile.ToJson(G.State));
            yield return PressAll(Key.Digit1);
            Expect("quick.inert-before-unlock", G.Magic.Casts == 0 && Tags(Sign) == "dark" && root.MagicMessage == "", "casts " + G.Magic.Casts + " message " + root.MagicMessage);
            Expect("quick.assign-refused-before-unlock", !root.AssignQuickSlot(0, "spell.lantern") && G.State.QuickSlots[0] == null, "slot " + G.State.QuickSlots[0]);

            yield return PressAll(Key.Q, Key.Digit3, Key.Digit2, Key.Digit1);
            Expect("quick.unlock-spell-confirmable", G.Magic.Open && G.Magic.Exact?.Id == "spell.quickcast", "runes [" + Runes(G) + "]");
            yield return PressAll(Key.Enter);
            Expect("quick.unlocked-on-self", G.State.QuickCastUnlocked && G.Magic.Last.Ok && G.Magic.Last.UnlockedQuickCast && G.State.QuickSlots.All(x => x == null) && Tags(Sign) == "dark", G.Magic.Last?.Message);
            yield return Shot("unlocked");

            yield return PressAll(Key.Digit1);
            Expect("quick.empty-slot-reports", G.Magic.Casts == 1 && root.MagicMessage.Contains("비어"), "message " + root.MagicMessage);

            yield return PressAll(Key.K);
            Expect("quick.spellbook-opens", root.SpellbookOpen, "panel " + root.SpellbookOpen);
            yield return Shot("spellbook");
            Step("root.AssignQuickSlot(0, spell.lantern)");
            Expect("quick.assign-lantern-slot-1", root.AssignQuickSlot(0, "spell.lantern") && G.State.QuickSlots[0] == "spell.lantern", "slot " + G.State.QuickSlots[0]);
            Expect("quick.assign-unlock-spell-refused", !root.AssignQuickSlot(1, "spell.quickcast") && G.State.QuickSlots[1] == null, "slot " + G.State.QuickSlots[1]);
            yield return Frames(3);
            yield return Shot("spellbook-assigned");
            yield return PressAll(Key.K);
            Expect("quick.spellbook-closes", !root.SpellbookOpen, "panel " + root.SpellbookOpen);

            yield return PressAll(Key.Digit1);
            var cast = G.Magic.Last;
            Expect("quick.slot-casts-lantern", G.Magic.Casts == 2 && cast.SpellId == "spell.lantern" && cast.Ok && cast.ReactionId == "react.lantern_dark" && Tags(Sign) == "lit", "tags " + Tags(Sign) + " message " + cast.Message);
            yield return Shot("quick-cast");

            G.ResetMagicTargets();
            yield return PressAll(Key.Q, Key.Digit3, Key.Digit2, Key.Digit1, Key.Enter);
            Expect("quick.unlock-again-keeps-slots", G.Magic.Last.Ok && !G.Magic.Last.UnlockedQuickCast && G.State.QuickCastUnlocked && G.State.QuickSlots[0] == "spell.lantern", "slot " + G.State.QuickSlots[0]);

            Step("root.Sleep()");
            root.Sleep();
            yield return Frames(5);
            var saved = SaveFile.Read(root.SavePath);
            Expect("quick.saved-at-day-end", saved != null && saved.QuickCastUnlocked && saved.QuickSlots[0] == "spell.lantern" && saved.SituationLog.ContainsKey("spell.lantern"), File.Exists(root.SavePath) ? File.ReadAllText(root.SavePath) : "missing");
            var fresh = new Core.Game(G.Content, saved, root.SavePath);
            Expect("quick.new-game-from-save", fresh.State.QuickCastUnlocked && fresh.State.QuickSlots.SequenceEqual(new[] { "spell.lantern", null, null, null }) && fresh.Magic.Tags(Sign).SetEquals(new[] { "dark" }), SaveFile.ToJson(fresh.State));

            var before = G;
            yield return Press(Key.F9);
            yield return Frames(2);
            Expect("quick.f9-keeps-unlock-and-slots", !ReferenceEquals(G, before) && G.State.QuickCastUnlocked && G.State.QuickSlots[0] == "spell.lantern" && Tags(Sign) == "dark", "slots " + string.Join(",", G.State.QuickSlots));
            yield return PressAll(Key.Digit1);
            Expect("quick.loaded-slot-reports-no-target", G.Magic.Casts == 1 && G.Magic.Last.Failure == CastFailure.NoTarget && Tags(Sign) == "dark", "message " + G.Magic.Last?.Message);
            Step("hold W");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return Until(() => G.Player.Y >= 1.2, "walk north of the bed", 10);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(3);
            yield return PressAll(Key.Digit1);
            Expect("quick.loaded-slot-casts", G.Magic.Last.Ok && Tags(Sign) == "lit", "tags " + Tags(Sign) + " message " + G.Magic.Last?.Message);
            yield return Shot("quick-after-load");
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
