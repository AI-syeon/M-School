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
                case "map": return MapCase();
                case "inventory": return InventoryCase();
                case "ui-tour": return UiTour();
                case "furniture": return FurnitureCase();
                case "stacking": return StackingCase();
                case "routine": case "routine-mira": return RoutineCase();
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
            var questsBefore = G.State.CompletedQuests.ToList();
            yield return PlayEvent(choice, false);
            var questPay = G.State.CompletedQuests.Except(questsBefore).Sum(id => G.Content.Quest(id).Reward.Crowns);
            Expect("wallet.plus-" + reward + (questPay > 0 ? "-and-quest-" + questPay : ""), G.State.Wallet == wallet0 + reward + questPay, wallet0 + " -> " + G.State.Wallet);
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
            yield return Line();
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
            yield return Line();
            yield return Press(Key.Enter);
            yield return Until(() => G.Event == null, "line closed");
            Expect("ui.closed", G.Dialogue == null && G.State.Completed.Contains("event.ui"), "dialogue " + (G.Dialogue != null));
        }

        const string Sign = "target.signboard";

        string Tags(string target) => string.Join(",", G.Magic.Tags(target).OrderBy(x => x));

        /// <summary>Holds W from the start position until the player faces the dark signboard, about 1.8 tiles ahead and to the side.</summary>
        IEnumerator FaceSign()
        {
            Step("hold W");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return Until(() => G.Player.Y >= 4.7, "walk toward the signboard", 10);
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

            yield return Press(Key.Enter);
            yield return Frames(2);
            var litSprite = root.View.TargetSprite(Sign);
            Expect("lantern.effect-starts", root.View.TargetEffectPlaying(Sign), "sprite " + litSprite);
            yield return Shot("lit-effect", false);
            var lit = G.Magic.Last;
            Expect("lantern.lit", Tags(Sign) == "lit" && lit != null && lit.Ok && lit.ReactionId == "react.lantern_dark" && lit.TargetId == Sign, "tags " + Tags(Sign) + " message " + lit?.Message);
            Expect("lantern.single-cast", G.Magic.Casts == 1 && !G.Magic.Open && Runes(G) == "", "casts " + G.Magic.Casts);
            Expect("lantern.situation-logged", G.State.SituationLog.TryGetValue("spell.lantern", out var log) && log.SequenceEqual(new[] { "lantern.dark" }), JsonConvert.SerializeObject(G.State.SituationLog));
            Expect("lantern.sprite-swapped", litSprite == "target-signboard-lit", "sprite " + litSprite);
            yield return Seconds(0.3f);
            yield return Shot("lit-bloom", false);
            yield return Seconds(0.7f);
            Expect("lantern.effect-ends-glow-stays", !root.View.TargetEffectPlaying(Sign) && root.View.TargetGlowShown(Sign) && root.View.TargetSprite(Sign) == "target-signboard-lit", "sprite " + root.View.TargetSprite(Sign));
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
            yield return Line();
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

            World.Place(G.Player, new Spot { MapId = "map.courtyard", X = 17.5, Y = 7.5 });
            yield return Frames(2);
            yield return PressAll(Key.Q, Key.Digit1);
            Expect("context.open-before-portal", G.Magic.Open && Runes(G) == "1", "runes [" + Runes(G) + "]");
            Step("hold W");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
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
            yield return WalkTo(new Spot { MapId = "map.courtyard", X = 11.5, Y = 4.5 }, "from the bed out to the signboard");
            yield return FaceSign();
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
            yield return Until(() => G.Player.Y > start + 0.5, "player started walking", 10);
            Expect("render.walking-frame-captured", G.Player.Moving && G.Player.Facing == "north", "moving " + G.Player.Moving + " facing " + G.Player.Facing);
            yield return Shot("walking");
            yield return Until(() => DistanceTo("npc.serin") <= 1.2, "player walked to Serin", 10);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(2);
            Expect("input.walk-north", G.Player.Y > start + 1 && G.Player.Facing == "north", "y " + start + " -> " + G.Player.Y + " facing " + G.Player.Facing);
            yield return Press(Key.E);
            yield return Frames(2);
            Expect("interact.invite-panel", root.PanelOpen && root.PanelEvents.Contains(ev), string.Join(",", root.PanelEvents));
            var offered = G.Interact();
            Expect("interact.invite-is-a-dialogue-choice", root.InviteOpen && offered.Count > 0 && root.Invitation.Choices.Take(offered.Count).SequenceEqual(offered.Select(i => i.Label))
                && root.Invitation.Choices.Count == offered.Count + 1 && root.Invitation.Choices.Last() == "다음에" && root.Invitation.Choices.Contains(Command.Parse(G.Content.Event(ev).Trigger)[0].Args[1]),
                root.Invitation == null ? "no invitation" : string.Join(" | ", root.Invitation.Choices));
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
            yield return Line();
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
                    yield return Line();
                    if (branch.Count == 1) yield return Shot("branch");
                    yield return Press(Key.Enter);
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------ map (M) and bag (I), start-items.json

        IEnumerator HoldUntil(Key key, Func<bool> done, string what)
        {
            Step("hold " + key + " until " + what);
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return Until(done, what, 15);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(2);
        }

        IEnumerator MapCase()
        {
            Expect("map.start-visited", G.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard" }), string.Join(",", G.State.VisitedMaps));
            yield return Press(Key.M);
            Expect("map.m-opens-on-current-area", root.MapOpen && root.MapPanel.Shown == "map.courtyard", "open " + root.MapOpen + " shown " + root.MapPanel.Shown);
            yield return Shot("map-courtyard");

            var before = G.Player.X;
            yield return Hold(Key.D, 30);
            Expect("map.player-holds-still-while-open", Math.Abs(G.Player.X - before) < 1e-9 && root.MapOpen, "x " + before + " -> " + G.Player.X);
            yield return Press(Key.M);
            Expect("map.m-closes", !root.MapOpen && !root.PanelOpen, "panel " + root.PanelOpen);

            Step("place player below the lounge door (17.5, 7.5)");
            World.Place(G.Player, new Spot { MapId = "map.courtyard", X = 17.5, Y = 7.5 });
            yield return HoldUntil(Key.W, () => G.MapId == "map.lounge", "enter the lounge");
            Expect("map.fade-runs", root.View.Transitioning, "fade " + root.View.FadeAlpha);
            yield return Until(() => !root.View.Transitioning, "fade finished", 3);
            Expect("map.lounge-door-cue", root.View.DoorCueCount == 1, "cues " + root.View.DoorCueCount);
            yield return Shot("lounge-arrived");
            yield return HoldUntil(Key.W, () => G.Player.Y >= 1.6, "step into the lounge");
            Expect("map.lounge-recorded", G.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard", "map.lounge" }), string.Join(",", G.State.VisitedMaps));

            yield return Press(Key.M);
            Expect("map.opens-on-lounge", root.MapOpen && root.MapPanel.Shown == "map.lounge", "shown " + root.MapPanel.Shown);
            yield return Shot("map-lounge");
            yield return Press(Key.RightArrow);
            Expect("map.arrow-switches-area", root.MapPanel.Shown == "map.courtyard" && G.MapId == "map.lounge", "shown " + root.MapPanel.Shown);
            yield return Shot("map-courtyard-from-lounge");
            yield return Press(Key.RightArrow);
            Expect("map.arrow-wraps", root.MapPanel.Shown == "map.lounge", "shown " + root.MapPanel.Shown);
            yield return Press(Key.Escape);
            Expect("map.esc-closes-without-menu", !root.MapOpen && !root.MenuOpen, "map " + root.MapOpen + " menu " + root.MenuOpen);

            // The campus beyond the courtyard: walk to the standing spot in each new area, so every door on the way is really crossed.
            var rooms = new[]
            {
                ("map.hall", 6.5, 3.5, 5, "to the hall board"), ("map.library", 3.5, 4.5, 1, "to the library desk"), ("map.classroom", 3.5, 3.5, 1, "to the classroom seat"),
                ("map.cafeteria", 3.5, 3.5, 1, "to the cafeteria table"), ("map.greenhouse", 3.5, 2.5, 1, "to the greenhouse bed"), ("map.dorm", 2.5, 5.5, 1, "to the dorm bed"),
            };
            foreach (var (mapId, x, y, doors, what) in rooms)
            {
                yield return WalkTo(new Spot { MapId = mapId, X = x, Y = y }, what);
                yield return Until(() => !root.View.Transitioning, "fade finished", 3);
                Expect("map.visited-" + mapId.Substring(4), G.MapId == mapId && G.State.VisitedMaps.Contains(mapId) && root.View.DoorCueCount == doors, G.MapId + " cues " + root.View.DoorCueCount);
                yield return Press(Key.M);
                Expect("map.opens-on-" + mapId.Substring(4), root.MapOpen && root.MapPanel.Shown == mapId, "shown " + root.MapPanel.Shown);
                yield return Frames(20);
                yield return Shot("map-" + mapId.Substring(4));
                yield return Press(Key.M);
            }

            var visited = G.State.VisitedMaps.Select(id => (id, art: root.View.ArtOf(id))).ToList();
            Expect("map.minimap-matches-size", visited.All(v => v.art != null && v.art.Minimap.Length == G.Content.Map(v.id).Height
                && v.art.CellColor(G.Content.Map(v.id).Width - 1, 0) != null && v.art.CellColor(G.Content.Map(v.id).Width, 0) == null), string.Join(",", visited.Select(v => v.id + ":" + v.art?.Minimap.Length)));
            int Colours(string id) => Enumerable.Range(0, G.Content.Map(id).Width).SelectMany(cx => Enumerable.Range(0, G.Content.Map(id).Height).Select(cy => root.View.ArtOf(id).CellColor(cx, cy))).Distinct().Count();
            Expect("map.minimap-colours-differ", visited.All(v => Colours(v.id) >= 3), string.Join(",", visited.Select(v => v.id + ":" + Colours(v.id))));
            Expect("map.faces-cut", root.View.Face("player") != null && root.View.Face("npc.serin") != null && root.View.Face("npc.mira") != null
                && root.View.Face("npc.serin").height < 32, "player " + root.View.Face("player")?.width + "x" + root.View.Face("player")?.height);
            yield return Press(Key.M);
            Expect("map.tabs-listed", G.State.VisitedMaps.Count == 8 && root.MapPanel.Shown == "map.dorm", string.Join(",", G.State.VisitedMaps));
            yield return Frames(20);
            yield return Shot("map-eight-tabs");
            yield return Press(Key.Escape);
        }

        IEnumerator InventoryCase()
        {
            var inv = G.Inventory;
            Lot Slot(int i) => G.State.Bag[i];
            string Id(int i) => Slot(i)?.ItemId ?? "-";
            Expect("bag.start", Id(0) == "item.lens" && Id(1) == "item.rune_chalk" && Id(7) == "item.crystal_dust" && inv.FreeSlots == 16 && G.State.KeyItems.SequenceEqual(new[] { "item.student_card" }),
                string.Join(",", Enumerable.Range(0, 8).Select(Id)) + " free " + inv.FreeSlots);

            yield return Press(Key.I);
            Expect("bag.i-opens", root.InventoryOpen && root.BagPanel.Selected == 0, "open " + root.InventoryOpen);
            yield return Shot("bag");

            yield return Press(Key.RightArrow);
            yield return Press(Key.Enter);
            Expect("bag.enter-picks-up", root.BagPanel.Holding == 1, "holding " + root.BagPanel.Holding + " " + root.BagPanel.Message);
            yield return Press(Key.DownArrow);
            yield return Shot("bag-holding");
            yield return Press(Key.Enter);
            Expect("bag.enter-swaps", Id(1) == "item.crystal_dust" && Id(7) == "item.rune_chalk" && Slot(7).Count == 3 && root.BagPanel.Holding == -1,
                "slot1 " + Id(1) + " slot7 " + Id(7));

            Step("G.Inventory.Add frost_mint 3");
            inv.Add("item.frost_mint", 3);
            yield return Frames(2);
            Expect("bag.add-merges", Slot(6).Count == 10 && inv.FreeSlots == 16 && root.Status.Contains("서리박하"), "count " + Slot(6).Count + " status " + root.Status);

            Step("G.Inventory.Add crystal_dust 1600");
            var r = inv.Add("item.crystal_dust", 16 * 99 + 16);
            yield return Frames(2);
            var mail = G.State.Mailbox.FirstOrDefault(l => l.ItemId == "item.crystal_dust");
            Expect("bag.overflow-to-mailbox", inv.FreeSlots == 0 && r.ToBag == 16 * 99 && r.ToMailbox == 16 && mail != null && mail.Count == 36,
                "free " + inv.FreeSlots + " toBag " + r.ToBag + " toMail " + r.ToMailbox + " mailbox " + mail?.Count);
            Expect("bag.overflow-notice", root.Status.Contains("우편 보관함") && root.Status.Contains("16개"), root.Status);
            yield return Shot("bag-full");

            Step("G.Inventory.Add torn_note 1 (key item)");
            var key = inv.Add("item.torn_note", 1);
            yield return Frames(2);
            Expect("bag.key-item-to-notebook", key.ToNotebook && G.State.KeyItems.Contains("item.torn_note") && inv.FreeSlots == 0 && !G.State.Mailbox.Any(l => l.ItemId == "item.torn_note"),
                string.Join(",", G.State.KeyItems));

            yield return Press(Key.UpArrow);
            Expect("bag.cursor-back-on-slot-1", root.BagPanel.Selected == 1, "selected " + root.BagPanel.Selected);
            yield return Press(Key.LeftArrow);
            yield return Press(Key.Delete);
            Expect("bag.delete-discards-one", Slot(0) == null && root.BagPanel.Message.Contains("버렸"), "slot0 " + Id(0) + " " + root.BagPanel.Message);

            Step("BagPanel.TakeMail(0) away from the bed");
            root.BagPanel.TakeMail(G, 0);
            Expect("bag.mailbox-needs-bed", G.State.Mailbox.Count == 1 && G.State.Mailbox[0].Count == 36 && root.BagPanel.Message.Contains("잠자리"), root.BagPanel.Message);
            Step("place player at the bed");
            World.Place(G.Player, G.Content.Bed);
            yield return Frames(2);
            Step("BagPanel.TakeMail(0) at the bed");
            root.BagPanel.TakeMail(G, 0);
            Expect("bag.mailbox-at-bed", G.Content.Bed != null && G.NearBed && G.State.Mailbox.Count == 0 && Id(0) == "item.crystal_dust" && Slot(0).Count == 36,
                "mailbox " + G.State.Mailbox.Count + " slot0 " + Id(0) + " " + root.BagPanel.Message);
            yield return Shot("bag-mailbox-taken");

            yield return Press(Key.I);
            Expect("bag.i-closes", !root.InventoryOpen, "open " + root.InventoryOpen);

            var bag = JsonConvert.SerializeObject(G.State.Bag);
            var keys = string.Join(",", G.State.KeyItems);
            Step("G.EndDay(Slept)");
            Expect("bag.day-ends", G.EndDay(DayEnd.Slept), "event " + G.Event?.Def.Id);
            yield return Frames(2);
            yield return Press(Key.F9);
            Expect("bag.reload-keeps-bag", JsonConvert.SerializeObject(G.State.Bag) == bag && string.Join(",", G.State.KeyItems) == keys && G.State.Mailbox.Count == 0,
                "keys " + string.Join(",", G.State.KeyItems));
        }

        // ------------------------------------------------------------------ furniture: arrange the dorm room (F), start-offered.json

        const string DormId = "map.dorm";

        bool Walkable(int x, int y) => World.Passable(G.Content.Map(DormId), x, y);

        string Piece(string id) => G.Furniture.PlacementOf(DormId, id) is PiecePlacement p ? p.X + "," + p.Y : G.Furniture.IsStored(DormId, id) ? "stored" : "gone";

        IEnumerable<Key> Repeat(Key key, int times)
        {
            for (var i = 0; i < times; i++) yield return key;
        }

        IEnumerator FurnitureCase()
        {
            yield return WalkTo(new Spot { MapId = DormId, X = 5.5, Y = 1.5 }, "from the courtyard into the dorm");
            yield return Until(() => !root.View.Transitioning, "fade finished", 3);
            Expect("furniture.in-dorm", G.MapId == DormId && G.Furniture.IsArrangeable(DormId) && G.Furniture.Placed(DormId).Count == 12 && G.State.Arrangements.Count == 0,
                G.MapId + " placed " + G.Furniture.Placed(DormId).Count);
            Expect("furniture.default-collision", !Walkable(3, 5) && !Walkable(4, 5) && Walkable(8, 2) && Walkable(1, 1) == false, "bookshelf cell open " + Walkable(3, 5));
            yield return Frames(20);
            yield return Shot("dorm-default");

            var minute = G.State.Minute;
            yield return Press(Key.F);
            yield return Frames(3);
            Expect("furniture.f-opens-arrange", root.ArrangeOpen && root.ArrangePanel.Mode == ArrangeScreen.Phase.Browse && root.ArrangePanel.Selected(G).Id == "bookshelf",
                "open " + root.ArrangeOpen + " selected " + root.ArrangePanel.Selected(G)?.Id);
            Expect("furniture.outline-is-floor", root.View.ArrangeOutline?.Equals(new RectInt(3, 5, 2, 1)) == true, "outline " + root.View.ArrangeOutline);
            yield return Seconds(1.2f);
            Expect("furniture.time-paused", G.State.Minute == minute, minute + " -> " + G.State.Minute);
            yield return Shot("arrange-browse");

            yield return Press(Key.Enter);
            yield return Frames(3);
            Expect("furniture.enter-lifts", root.ArrangePanel.Mode == ArrangeScreen.Phase.Move && root.ArrangePanel.GhostX == 3 && root.ArrangePanel.GhostY == 5 && root.ArrangePanel.GhostProblem == PlaceProblem.None,
                "mode " + root.ArrangePanel.Mode + " ghost " + root.ArrangePanel.GhostX + "," + root.ArrangePanel.GhostY);
            yield return Press(Key.LeftArrow);
            yield return Frames(3);
            Expect("furniture.bed-cell-refused", root.ArrangePanel.GhostX == 2 && root.ArrangePanel.GhostProblem == PlaceProblem.Keep, "ghost " + root.ArrangePanel.GhostX + " problem " + root.ArrangePanel.GhostProblem);
            yield return Shot("arrange-invalid");
            var before = SaveFile.ToJson(G.State);
            yield return Press(Key.Enter);
            yield return Frames(3);
            Expect("furniture.invalid-enter-refused", root.ArrangePanel.Mode == ArrangeScreen.Phase.Move && Piece("bookshelf") == "3,5" && SaveFile.ToJson(G.State) == before && root.Status.Contains("놓을 수 없어요"),
                "bookshelf " + Piece("bookshelf") + " status " + root.Status);
            yield return Shot("arrange-refused-toast");
            yield return Press(Key.Escape);
            Expect("furniture.esc-puts-it-back", root.ArrangeOpen && root.ArrangePanel.Mode == ArrangeScreen.Phase.Browse && Piece("bookshelf") == "3,5", "mode " + root.ArrangePanel.Mode + " bookshelf " + Piece("bookshelf"));

            yield return Press(Key.Enter);
            foreach (var key in Repeat(Key.RightArrow, 5).Concat(Repeat(Key.DownArrow, 3))) yield return Press(key);
            yield return Frames(3);
            Expect("furniture.valid-ghost", root.ArrangePanel.GhostX == 8 && root.ArrangePanel.GhostY == 2 && root.ArrangePanel.GhostProblem == PlaceProblem.None,
                "ghost " + root.ArrangePanel.GhostX + "," + root.ArrangePanel.GhostY + " problem " + root.ArrangePanel.GhostProblem);
            Expect("furniture.ghost-outline-is-floor", root.View.ArrangeOutline?.Equals(new RectInt(8, 2, 2, 1)) == true, "outline " + root.View.ArrangeOutline);
            yield return Shot("arrange-valid");
            yield return Press(Key.Enter);
            yield return Frames(5);
            Expect("furniture.placed", Piece("bookshelf") == "8,2" && root.ArrangePanel.Mode == ArrangeScreen.Phase.Browse && root.Status.Contains("놓았어요") && G.State.Arrangements.ContainsKey(DormId),
                "bookshelf " + Piece("bookshelf") + " status " + root.Status);
            Expect("furniture.cells-moved", Walkable(3, 5) && Walkable(4, 5) && !Walkable(8, 2) && !Walkable(9, 2), "old " + Walkable(3, 5) + " new " + Walkable(8, 2));
            yield return Seconds(0.5f);
            yield return Shot("arrange-placed");

            // Store one, take one out: plant-a is the ninth piece, the rug the twelfth.
            foreach (var key in Repeat(Key.DownArrow, 8)) yield return Press(key);
            Expect("furniture.cursor-on-plant", root.ArrangePanel.Selected(G).Id == "plant-a", root.ArrangePanel.Selected(G)?.Id);
            yield return Press(Key.C);
            yield return Frames(5);
            Expect("furniture.c-stores", G.Furniture.IsStored(DormId, "plant-a") && Walkable(1, 1) && G.Furniture.Placed(DormId).Count == 11 && root.Status.Contains("보관했어요"), "plant-a " + Piece("plant-a") + " status " + root.Status);
            foreach (var key in Repeat(Key.DownArrow, 3)) yield return Press(key);
            yield return Press(Key.C);
            Expect("furniture.rug-stored", G.Furniture.IsStored(DormId, "rug") && root.ArrangePanel.Selected(G).Id == "rug", "rug " + Piece("rug"));
            yield return Shot("arrange-stored");
            yield return Press(Key.Enter);
            yield return Frames(3);
            Expect("furniture.enter-takes-out", root.ArrangePanel.Mode == ArrangeScreen.Phase.Move && root.ArrangePanel.GhostProblem == PlaceProblem.None, "mode " + root.ArrangePanel.Mode + " problem " + root.ArrangePanel.GhostProblem);
            yield return Shot("arrange-take-out");
            yield return Press(Key.Enter);
            yield return Frames(3);
            Expect("furniture.rug-out", Piece("rug") != "stored" && Piece("rug") != "gone" && !G.Furniture.IsStored(DormId, "rug") && G.Furniture.IsStored(DormId, "plant-a"), "rug " + Piece("rug"));
            var rug = Piece("rug");

            yield return Press(Key.F);
            Expect("furniture.f-closes", !root.ArrangeOpen && !root.PanelOpen, "open " + root.ArrangeOpen);
            yield return Frames(10);
            yield return Shot("dorm-arranged");

            // Real walking: the new spot stops the player, the old one lets them through.
            Step("place the player below the new bookshelf and hold S");
            World.Place(G.Player, new Spot { MapId = DormId, X = 9.5, Y = 4.5 });
            yield return Frames(2);
            yield return Hold(Key.S, 50);
            Expect("furniture.walks-into-new-spot", G.Player.Y >= 3.19 && G.Player.Y < 3.7 && G.Player.X > 9.4 && G.Player.X < 9.6, "player " + G.Player.X + "," + G.Player.Y);
            var shelf = G.Furniture.Piece(DormId, "bookshelf");
            var standing = ((int)Math.Floor(G.Player.X), (int)Math.Floor(G.Player.Y));
            yield return Frames(2);
            Expect("furniture.behind-piece-hidden", Furniture.Covered(shelf, 8, 2).Contains(standing) && !Furniture.Footprint(shelf, 8, 2).Contains(standing) && root.View.PieceCovers("bookshelf", G.Player.Id),
                "cell " + standing + " covers " + root.View.PieceCovers("bookshelf", G.Player.Id));
            yield return Shot("behind-bookshelf");
            Step("place the player below the old bookshelf spot and hold W");
            World.Place(G.Player, new Spot { MapId = DormId, X = 4.5, Y = 4.5 });
            yield return Frames(2);
            yield return Hold(Key.W, 50);
            Expect("furniture.walks-through-old-spot", G.Player.Y > 5.4, "player " + G.Player.X + "," + G.Player.Y);

            // The map screen paints the furniture where it stands now.
            var art = root.View.ArtOf(DormId);
            var painted = art.Furnished(G.Furniture.Placed(DormId), G.Furniture.Room(DormId));
            Expect("furniture.minimap-follows", painted.ContainsKey((8, 2)) && painted[(8, 2)] != art.CellColor(8, 2) && !painted.ContainsKey((3, 6)) && art.Pieces.Count == 12,
                "cells " + painted.Count + " at 8,2 " + (painted.ContainsKey((8, 2)) ? painted[(8, 2)].ToString() : "none"));
            yield return Press(Key.M);
            yield return Frames(20);
            yield return Shot("map-dorm-arranged");
            yield return Press(Key.M);

            // Sleep in the bed: the day saves the arrangement.
            yield return WalkTo(new Spot { MapId = DormId, X = 2.5, Y = 5.5 }, "to the bed");
            Expect("furniture.bed-reachable", G.NearBed, "player " + G.Player.X + "," + G.Player.Y);
            yield return Press(Key.E);
            yield return Frames(3);
            Expect("furniture.sleep-prompt", root.SleepPromptOpen, "prompt " + root.SleepPromptOpen);
            yield return Press(Key.Enter);
            yield return Frames(5);
            Expect("furniture.slept", G.LastDayEnd == DayEnd.Slept && G.State.Day == 1 && File.Exists(root.SavePath), "day " + G.State.Day);
            yield return Seconds(0.7f);
            yield return Press(Key.Enter);
            yield return Frames(20);
            var saved = SaveFile.Read(root.SavePath);
            var room = saved?.Arrangements.TryGetValue(DormId, out var entry) == true ? entry : null;
            Expect("furniture.save-has-arrangement", room != null && room.Placed.Any(p => p.Piece == "bookshelf" && p.X == 8 && p.Y == 2) && room.Stored.SequenceEqual(new[] { "plant-a" })
                && room.Placed.Any(p => p.Piece == "rug" && p.X + "," + p.Y == rug) && room.Placed.Count == 11, File.Exists(root.SavePath) ? JsonConvert.SerializeObject(room) : "missing");
            yield return Shot("morning-arranged");

            var game = G;
            yield return Press(Key.F9);
            yield return Frames(20);
            Expect("furniture.reloaded", !ReferenceEquals(G, game) && Piece("bookshelf") == "8,2" && Piece("plant-a") == "stored" && Piece("rug") == rug && Piece("desk") == "6,3", "bookshelf " + Piece("bookshelf") + " plant-a " + Piece("plant-a"));
            Expect("furniture.reloaded-collision", !Walkable(8, 2) && Walkable(3, 5) && Walkable(1, 1) && !Walkable(6, 3), "8,2 " + Walkable(8, 2) + " 3,5 " + Walkable(3, 5) + " 1,1 " + Walkable(1, 1));
            yield return Shot("arrange-reloaded");
        }

        // ------------------------------------------------------------------ stacking: a plant on the desk and on the chest lid, start-offered.json

        IEnumerator StackingCase()
        {
            yield return WalkTo(new Spot { MapId = DormId, X = 5.5, Y = 1.5 }, "from the courtyard into the dorm");
            yield return Until(() => !root.View.Transitioning, "fade finished", 3);
            var art = root.View.ArtOf(DormId);
            var desk = G.Furniture.Piece(DormId, "desk");
            var chest = G.Furniture.Piece(DormId, "chest");
            Expect("stacking.data", desk.Surface.Count == 3 && chest.Surface.Count == 1 && G.Furniture.Piece(DormId, "plant-a").Stackable && !desk.Stackable,
                "desk surface " + desk.Surface.Count + " chest surface " + chest.Surface.Count);
            yield return Press(Key.F);
            yield return Frames(3);
            foreach (var key in Repeat(Key.DownArrow, 8)) yield return Press(key);
            Expect("stacking.cursor-on-plant", root.ArrangePanel.Selected(G).Id == "plant-a", root.ArrangePanel.Selected(G)?.Id);
            yield return Press(Key.Enter);
            foreach (var key in Repeat(Key.RightArrow, 5).Concat(Repeat(Key.UpArrow, 3))) yield return Press(key);
            yield return Frames(3);
            Expect("stacking.valid-ghost-on-desk", root.ArrangePanel.GhostX == 6 && root.ArrangePanel.GhostY == 4 && root.ArrangePanel.GhostProblem == PlaceProblem.None,
                "ghost " + root.ArrangePanel.GhostX + "," + root.ArrangePanel.GhostY + " problem " + root.ArrangePanel.GhostProblem);
            Expect("stacking.ghost-is-lifted", root.ArrangePanel.Overlay(G, art)?.Lift == desk.Lift, "lift " + root.ArrangePanel.Overlay(G, art)?.Lift + " desk " + desk.Lift);
            yield return Shot("plant-on-desk-ghost");
            yield return Press(Key.Enter);
            yield return Frames(5);
            var stance = G.Furniture.Stance(DormId, "plant-a");
            Expect("stacking.plant-on-desk", Piece("plant-a") == "6,4" && stance?.Layer == Layer.Top && stance?.Host == "desk" && G.Furniture.IsLoaded(DormId, "desk") && !Walkable(6, 4),
                "plant-a " + Piece("plant-a") + " layer " + stance?.Layer + " host " + stance?.Host);
            yield return Press(Key.F);
            yield return Frames(10);
            yield return Shot("plant-on-desk");

            Step("try to lift and store the loaded desk");
            yield return Press(Key.F);
            yield return Press(Key.DownArrow);
            Expect("stacking.cursor-on-desk", root.ArrangePanel.Selected(G).Id == "desk", root.ArrangePanel.Selected(G)?.Id);
            yield return Press(Key.Enter);
            yield return Frames(3);
            Expect("stacking.loaded-desk-cannot-lift", root.ArrangePanel.Mode == ArrangeScreen.Phase.Browse && root.Status.Contains("올려 둔 물건부터") && Piece("desk") == "6,3", "mode " + root.ArrangePanel.Mode + " status " + root.Status);
            yield return Shot("desk-loaded-refused");
            yield return Press(Key.C);
            yield return Frames(3);
            Expect("stacking.loaded-desk-cannot-store", !G.Furniture.IsStored(DormId, "desk") && Piece("desk") == "6,3" && root.Status.Contains("보관할 수 없어요"), "desk " + Piece("desk") + " status " + root.Status);

            Step("a second plant onto the same tabletop cell, then onto the chest lid");
            foreach (var key in Repeat(Key.DownArrow, 8)) yield return Press(key);
            Expect("stacking.cursor-on-plant-b", root.ArrangePanel.Selected(G).Id == "plant-b", root.ArrangePanel.Selected(G)?.Id);
            yield return Press(Key.Enter);
            foreach (var key in Repeat(Key.LeftArrow, 3).Concat(Repeat(Key.UpArrow, 3))) yield return Press(key);
            yield return Frames(3);
            Expect("stacking.second-plant-same-cell-refused", root.ArrangePanel.GhostX == 6 && root.ArrangePanel.GhostY == 4 && root.ArrangePanel.GhostProblem == PlaceProblem.SurfaceTaken,
                "ghost " + root.ArrangePanel.GhostX + "," + root.ArrangePanel.GhostY + " problem " + root.ArrangePanel.GhostProblem);
            yield return Press(Key.Enter);
            yield return Frames(3);
            Expect("stacking.refused-enter-changes-nothing", root.ArrangePanel.Mode == ArrangeScreen.Phase.Move && Piece("plant-b") == "9,1" && root.Status.Contains("이미 뭔가 올라가"), "plant-b " + Piece("plant-b") + " status " + root.Status);
            yield return Shot("plant-second-refused");
            foreach (var key in Repeat(Key.LeftArrow, 3).Concat(Repeat(Key.DownArrow, 1))) yield return Press(key);
            yield return Frames(3);
            Expect("stacking.chest-lid-ghost", root.ArrangePanel.GhostX == 3 && root.ArrangePanel.GhostY == 3 && root.ArrangePanel.GhostProblem == PlaceProblem.None && root.ArrangePanel.Overlay(G, art)?.Lift == chest.Lift,
                "ghost " + root.ArrangePanel.GhostX + "," + root.ArrangePanel.GhostY + " problem " + root.ArrangePanel.GhostProblem);
            yield return Press(Key.Enter);
            yield return Frames(5);
            Expect("stacking.plant-on-chest", Piece("plant-b") == "3,3" && G.Furniture.Stance(DormId, "plant-b")?.Host == "chest", "plant-b " + Piece("plant-b") + " host " + G.Furniture.Stance(DormId, "plant-b")?.Host);
            yield return Press(Key.F);
            yield return Frames(10);
            yield return Shot("plant-on-chest");
            var painted = art.Furnished(G.Furniture.Placed(DormId), G.Furniture.Room(DormId));
            Expect("stacking.minimap-paints-top-last", painted.ContainsKey((6, 4)) && painted.ContainsKey((3, 3)), "cells " + painted.Count);

            yield return WalkTo(new Spot { MapId = DormId, X = 2.5, Y = 5.5 }, "to the bed");
            yield return Press(Key.E);
            yield return Frames(3);
            yield return Press(Key.Enter);
            yield return Frames(5);
            Expect("stacking.slept", G.LastDayEnd == DayEnd.Slept && File.Exists(root.SavePath), "day " + G.State.Day);
            yield return Seconds(0.7f);
            yield return Press(Key.Enter);
            yield return Frames(20);
            var game = G;
            yield return Press(Key.F9);
            yield return Frames(20);
            Expect("stacking.reloaded", !ReferenceEquals(G, game) && G.Furniture.Stance(DormId, "plant-a")?.Host == "desk" && G.Furniture.Stance(DormId, "plant-b")?.Host == "chest"
                && G.Furniture.IsLoaded(DormId, "desk") && G.Furniture.IsLoaded(DormId, "chest"), "plant-a " + Piece("plant-a") + " plant-b " + Piece("plant-b"));
            yield return Shot("plant-on-desk-reloaded");
        }

        // ------------------------------------------------------------------ ui-tour: one screenshot per screen, start-offered.json

        // ------------------------------------------------------------------ routine

        bool Stands(Actor a, DestinationDef place) => a.MapId == place.Position.MapId && Math.Abs(a.X - place.Position.X) < 1e-6 && Math.Abs(a.Y - place.Position.Y) < 1e-6;

        string Clip(string npcId) => root.View.ClipOf(npcId);

        /// <summary>Puts the player on a free cell beside the NPC, looking at it, so the camera shows the NPC's map.</summary>
        void StandBeside(Actor npc)
        {
            var map = G.Content.Map(npc.MapId);
            int cx = (int)Math.Floor(npc.X), cy = (int)Math.Floor(npc.Y);
            foreach (var (dx, dy, look) in new[] { (1, 0, "west"), (-1, 0, "east"), (0, -1, "north"), (0, 1, "south"), (2, 0, "west"), (-2, 0, "east"), (0, -2, "north") })
                if (World.Passable(map, cx + dx, cy + dy) && World.PortalAt(map, cx + dx, cy + dy) == null)
                {
                    World.Place(G.Player, new Spot { MapId = map.Id, X = cx + dx + 0.5, Y = cy + dy + 0.5 }, look);
                    return;
                }
            throw new InvalidOperationException("no free cell beside " + npc.Id);
        }

        /// <summary>Plays the NPC's clip for 1.2 seconds and returns the frame indexes the view showed.</summary>
        IEnumerator WatchFrames(string npcId, HashSet<int> frames)
        {
            var end = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < end)
            {
                frames.Add(root.View.FrameOf(npcId));
                yield return null;
            }
        }

        /// <summary>Jumps the clock to a minute, which places the NPC in its step with the action on, and shows it next to the player.</summary>
        IEnumerator RoutineShow(string npcId, int minute, string action, string shot)
        {
            var npc = G.Actor(npcId);
            Step("Game.SetClock(" + Clock.FormatTime(minute) + ")");
            G.SetClock(minute);
            var place = Schedule.PlaceOf(G.Content, npcId, Schedule.StepAt(G.Content, npcId, minute));
            StandBeside(npc);
            yield return Frames(3);
            yield return Until(() => !root.View.Transitioning, "fade finished", 3);
            Expect("routine." + shot + "-placed", Stands(npc, place) && npc.Action == action && !npc.Moving, npc.MapId + " " + npc.X + "," + npc.Y + " action " + npc.Action);
            Expect("routine." + shot + "-clip", Clip(npcId) == "south." + action, "clip " + Clip(npcId));
            var frames = new HashSet<int>();
            yield return WatchFrames(npcId, frames);
            Expect("routine." + shot + "-animates", frames.Count >= 3, "frames seen " + string.Join(",", frames.OrderBy(f => f)));
            yield return Shot(shot);
        }

        IEnumerator RoutineCase()
        {
            var mira = mode == "routine-mira";
            var who = mira ? "npc.mira" : "npc.serin";
            var npc = G.Actor(who);
            var home = G.Content.Destination(G.Content.Npc(who).Home);
            yield return Frames(3);
            Expect("routine.day-starts-at-home", Stands(npc, home) && npc.Action == Actor.Idle && G.State.Minute == Clock.DayStart && Clip(who) == "south.idle", npc.MapId + " " + npc.X + "," + npc.Y + " clip " + Clip(who));

            var first = G.Content.Routine(who).First(s => s.Place != RoutineStep.Home);
            var leave = first.Minute;
            Step("Game.SetClock(" + Clock.FormatTime(leave - 1) + ")");
            G.SetClock(leave - 1);
            yield return Frames(2);
            Expect("routine.waits-for-the-step", Stands(npc, home) && !npc.Moving && npc.Goal == null, "moving " + npc.Moving);
            yield return Until(() => npc.Moving, "the NPC leaves at " + first.At, 10);
            Expect("routine.leaves-on-time", G.State.Minute >= leave && G.State.Minute <= leave + 1 && npc.Goal == first && npc.Action == Actor.Idle, "minute " + G.State.Minute + " goal " + npc.Goal?.Place);
            yield return Frames(3);
            Expect("routine.plays-walk", Clip(who).EndsWith(".walk"), "clip " + Clip(who));
            yield return Until(() => npc.Goal == null && !npc.Moving, "the NPC arrives at " + first.Place, 60);
            yield return Frames(3);
            var firstPlace = G.Content.Destination(first.Place);
            Expect("routine.arrives-with-action", Stands(npc, firstPlace) && npc.Action == first.Action && Clip(who) == "south." + first.Action, npc.MapId + " " + npc.X + "," + npc.Y + " action " + npc.Action + " clip " + Clip(who));
            StandBeside(npc);
            yield return Frames(3);
            yield return Until(() => !root.View.Transitioning, "fade finished", 3);
            var walked = new HashSet<int>();
            yield return WatchFrames(who, walked);
            Expect("routine.walked-action-animates", walked.Count >= 3, "frames seen " + string.Join(",", walked.OrderBy(f => f)));
            yield return Shot(first.Action + "-after-walk");

            if (mira)
            {
                yield return RoutineShow(who, 13 * 60 + 15, "eat", "mira-eat");
                yield return RoutineShow(who, 9 * 60 + 45, "write", "mira-write");
                yield return RoutineShow(who, 14 * 60 + 45, "chat", "mira-chat");
                yield break;
            }
            yield return RoutineShow(who, 11 * 60 + 30, "write", "serin-write");
            yield return RoutineShow(who, 12 * 60 + 45, "eat", "serin-eat");

            // The next walk: from the cafeteria at 13:29 to the courtyard notice board at 13:30, across three maps.
            Step("Game.SetClock(13:29)");
            G.SetClock(13 * 60 + 29);
            yield return Until(() => npc.Moving, "Serin leaves the cafeteria at 13:30", 10);
            var notice = G.Content.Routine(who).First(s => s.Place == "dest.notice");
            Expect("routine.second-walk-starts", npc.Goal == notice && npc.Action == Actor.Idle && npc.MapId == "map.cafeteria", npc.MapId + " goal " + npc.Goal?.Place);
            var maps = new HashSet<string> { npc.MapId };
            yield return Until(() => { maps.Add(npc.MapId); return npc.Goal == null && !npc.Moving; }, "Serin arrives at the notice board", 60);
            yield return Frames(3);
            Expect("routine.second-walk-crosses-maps", maps.SetEquals(new[] { "map.cafeteria", "map.hall", "map.courtyard" }), string.Join(",", maps));
            Expect("routine.second-walk-arrives", Stands(npc, G.Content.Destination("dest.notice")) && npc.Action == "read" && Clip(who) == "south.read", npc.MapId + " action " + npc.Action + " clip " + Clip(who));

            yield return RoutineShow(who, 16 * 60 + 45, "chat", "serin-chat");
            yield return RoutineShow(who, 9 * 60 + 20, "read", "serin-read");

            // An event takes her out of her pose; cancelling puts her back in it.
            Step("root.AcceptAndStart(" + ev + ")");
            root.AcceptAndStart(ev);
            yield return Until(() => G.Event != null && !G.Hidden, "event on screen", 10);
            yield return Frames(5);
            Expect("routine.event-ends-the-pose", npc.Action == Actor.Idle && !Clip(who).EndsWith(".read") && npc.MapId == "map.lounge", "action " + npc.Action + " clip " + Clip(who) + " on " + npc.MapId);
            yield return Press(Key.Escape);
            yield return Until(() => G.Event == null, "event cancelled");
            yield return Until(() => !root.View.Transitioning, "fade back to the library", 3);
            yield return Frames(3);
            Expect("routine.cancel-restores-the-pose", Stands(npc, G.Content.Destination("dest.library_desk")) && npc.Action == "read" && Clip(who) == "south.read", npc.MapId + " action " + npc.Action + " clip " + Clip(who));
            var json = SaveFile.ToJson(G.State);
            Expect("routine.nothing-saved", !json.Contains("\"action\"") && !json.Contains("\"goal\""), "state has no routine fields");
        }

        IEnumerator UiTour()
        {
            yield return Seconds(0.5f);
            Expect("tour.hud-plain", !root.PanelOpen && !root.MenuOpen && G.Quests.Active.Count >= 2 && G.State.Day == 0, "active " + G.Quests.Active.Count);
            yield return Shot("01-hud");

            Step("place the player in the middle of the plaza");
            World.Place(G.Player, new Spot { MapId = "map.courtyard", X = 10.5, Y = 3.5 }, "north");
            yield return Seconds(0.5f);
            var looks = WorldArt.Load(Path.Combine(GamePaths.StreamingAssets, "FirstConnection", "world-art.json")).Targets;
            Expect("tour.targets-default-sprites", G.Content.MagicTargets.Count == 6 && G.Content.MagicTargets.All(t => looks.TryGetValue(t.Id, out var l) && root.View.TargetSprite(t.Id) == l.Default),
                string.Join(",", G.Content.MagicTargets.Select(t => t.Id + "=" + root.View.TargetSprite(t.Id))));
            yield return Shot("01b-targets-default");

            Expect("tour.door-cues", root.View.DoorCueCount == G.Content.Map(G.MapId).Portals.Count && root.View.DoorCueCount > 0, "cues " + root.View.DoorCueCount);
            yield return WalkTo(new Spot { MapId = "map.courtyard", X = 11.5, Y = 4.5 }, "to the signboard");
            yield return FaceSign();
            yield return Seconds(0.2f);
            yield return Shot("02-aimed-chip");

            yield return PressAll(Key.Q);
            Expect("tour.rune-input-open", G.Magic.Open && G.Magic.Runes.Count == 0, "open " + G.Magic.Open);
            yield return Shot("03-rune-input");
            yield return PressAll(Key.Digit1);
            yield return PressAll(Key.Digit2);
            Expect("tour.rune-input-knows-lantern", G.Magic.Exact?.Id == "spell.lantern", "runes [" + Runes(G) + "]");
            yield return Shot("04-rune-input-lantern");

            yield return PressAll(Key.Enter);
            yield return Seconds(0.35f);
            Expect("tour.quest-done-toast", G.State.CompletedQuests.Contains("quest.first_light") && Tags(Sign) == "lit"
                && root.Hud.Toasts.Any(t => t.Kind == ToastKind.QuestDone && t.Text.Contains("안내판")), string.Join(" | ", root.Hud.Toasts.Select(t => t.Kind + ":" + t.Text)));
            Expect("tour.effect-and-glow", root.View.TargetSprite(Sign) == "target-signboard-lit" && root.View.TargetEffectPlaying(Sign), "sprite " + root.View.TargetSprite(Sign));
            yield return Shot("05-toast", false);
            yield return Seconds(1.2f);
            Expect("tour.signboard-lit-glow", root.View.TargetSprite(Sign) == "target-signboard-lit" && root.View.TargetGlowShown(Sign) && !root.View.TargetEffectPlaying(Sign), "sprite " + root.View.TargetSprite(Sign));
            yield return Shot("05b-signboard-lit");

            foreach (var (key, tab, name) in new[] { (Key.J, GameRoot.Tab.Journal, "06-tab-journal"), (Key.K, GameRoot.Tab.Spellbook, "07-tab-spellbook"), (Key.I, GameRoot.Tab.Bag, "08-tab-bag"), (Key.M, GameRoot.Tab.Map, "09-tab-map") })
            {
                yield return Press(key);
                yield return Seconds(0.25f);
                Expect("tour.tab-" + tab, root.BookOpen && root.BookTab == tab, "open " + root.BookOpen + " tab " + root.BookTab);
                yield return Shot(name);
            }
            var minute = G.State.Minute;
            yield return Seconds(1.5f);
            Expect("tour.clock-paused-in-window", G.State.Minute == minute, minute + " -> " + G.State.Minute);
            yield return Press(Key.Escape);
            Expect("tour.window-closes", !root.PanelOpen && !root.MenuOpen, "panel " + root.PanelOpen);

            yield return WalkTo(new Spot { MapId = "map.courtyard", X = 8.5, Y = 4.5 }, "to Serin");
            yield return Hold(Key.W, 3);
            yield return Seconds(0.2f);
            yield return Shot("10a-talk-prompt");
            yield return Press(Key.E);
            yield return Seconds(0.25f);
            Expect("tour.invitation-choice", root.InviteOpen && root.Invitation.Choices.Count == G.Interact().Count + 1 && G.Interact().Count == 2 && root.Invitation.Choices.Last() == "다음에" && root.Invitation.Speaker == "npc.serin",
                root.Invitation == null ? "none" : string.Join(" | ", root.Invitation.Choices));
            yield return Shot("10-invitation-choice");

            Step("drop the second offer so one invitation is left");
            yield return Press(Key.Escape);
            G.State.Appointments.Remove("event.snack_later");
            yield return Press(Key.E);
            yield return Seconds(0.25f);
            Expect("tour.invitation-choice-single", root.InviteOpen && root.Invitation.Choices.Count == 2 && root.Invitation.Choices[0] == "간식 모임 시작" && root.Invitation.Choices[1] == "다음에",
                root.Invitation == null ? "none" : string.Join(" | ", root.Invitation.Choices));
            yield return Shot("10b-invitation-choice-single");

            yield return Press(Key.Enter);
            Expect("tour.choice-starts-event", G.Event?.Def.Id == "event.snack" && !root.PanelOpen, "event " + G.Event?.Def.Id);
            yield return Until(() => G.Dialogue != null, "first line");
            yield return Seconds(0.15f);
            yield return Shot("11a-dialogue-typing");
            yield return Line();
            yield return Seconds(0.5f);
            Expect("tour.dialogue-keeps-player-visible", !G.Hidden && root.View.BottomInset > 0f && root.View.GuiPoint(G.Player.X, G.Player.Y - 0.1).y < Screen.height - root.View.BottomInset,
                "inset " + root.View.BottomInset + " feet " + root.View.GuiPoint(G.Player.X, G.Player.Y - 0.1).y + " of " + Screen.height);
            yield return Shot("11-dialogue-line");
            yield return Press(Key.Escape);
            yield return Until(() => G.Event == null, "event cancelled");

            Step("place the player next to the bed");
            World.Place(G.Player, new Spot { MapId = G.Content.Bed.MapId, X = G.Content.Bed.X + 0.5, Y = G.Content.Bed.Y + 0.5 });
            yield return Seconds(0.3f);
            yield return Shot("12a-bed-prompt");
            yield return Press(Key.E);
            yield return Seconds(0.25f);
            Expect("tour.sleep-confirm", root.SleepPromptOpen, "prompt " + root.SleepPromptOpen);
            yield return Shot("12-sleep-confirm");

            yield return Press(Key.Enter);
            yield return Seconds(0.8f);
            Expect("tour.day-report", root.DayReportOpen && G.State.Day == 1 && G.LastDayReport.Quests.Any(q => q.Id == "quest.first_light"), "report " + root.DayReportOpen + " day " + G.State.Day);
            yield return Shot("13-day-report");
            yield return Press(Key.Enter);
            Expect("tour.report-closes", !root.DayReportOpen, "report " + root.DayReportOpen);
            yield return Seconds(0.8f);
            Expect("tour.morning-in-dorm", G.MapId == "map.dorm" && root.View.DoorCueCount == 1, G.MapId + " cues " + root.View.DoorCueCount);
            yield return Shot("14-morning");

            root.OpenMenu();
            yield return Seconds(0.25f);
            yield return Shot("15a-menu");
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            yield return Seconds(0.25f);
            Expect("tour.menu-controls", root.MenuOpen && root.Page == GameRoot.MenuPage.Controls, "page " + root.Page);
            yield return Shot("15-menu-controls");
            yield return Press(Key.Escape);
            yield return Press(Key.Escape);
            Expect("tour.menu-closes", !root.MenuOpen, "menu " + root.MenuOpen);
        }


        /// <summary>
        /// Walks the real keyboard path to a spot, possibly on another map: one held arrow key at a time toward the next cell of
        /// Core's own path, recentring on the cross axis before each turn so the player's box never clips a corner.
        /// </summary>
        IEnumerator WalkTo(Spot goal, string what)
        {
            Step("walk " + what);
            var held = Key.None;
            var deadline = Time.realtimeSinceStartup + 30;
            while (G.Player.MapId != goal.MapId || Math.Abs(G.Player.X - goal.X) > 0.12 || Math.Abs(G.Player.Y - goal.Y) > 0.12)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("walk " + what + " stuck at " + G.Player.MapId + " " + G.Player.X + "," + G.Player.Y);
                var p = G.Player;
                var path = World.FindPath(G.Content, p, goal) ?? throw new InvalidOperationException("no path " + what);
                var next = path.FirstOrDefault(s => s.MapId == p.MapId) ?? goal;
                double dx = next.X - p.X, dy = next.Y - p.Y;
                double offX = Math.Floor(p.X) + 0.5 - p.X, offY = Math.Floor(p.Y) + 0.5 - p.Y;
                var key = Math.Abs(dx) >= Math.Abs(dy)
                    ? Math.Abs(offY) > 0.15 ? (offY > 0 ? Key.W : Key.S) : (dx > 0 ? Key.D : Key.A)
                    : Math.Abs(offX) > 0.15 ? (offX > 0 ? Key.D : Key.A) : (dy > 0 ? Key.W : Key.S);
                if (key != held)
                {
                    InputSystem.QueueStateEvent(kb, new KeyboardState(key));
                    held = key;
                }
                yield return null;
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(2);
        }

        double DistanceTo(string npcId)
        {
            var npc = G.Actor(npcId);
            return World.Distance(G.Player, new Spot { MapId = npc.MapId, X = npc.X, Y = npc.Y });
        }

        string Spots() => string.Join("|", G.Actors.Select(a => a.Id + "=" + a.MapId + ":" + a.X.ToString("F3") + ":" + a.Y.ToString("F3")));

        /// <summary>Waits until the dialogue box has typed out the current line, so Enter advances instead of revealing.</summary>
        IEnumerator Line()
        {
            yield return Until(() => G.Dialogue == null || root.Box.RevealedFor(G.Dialogue), "line typed out", 5);
        }

        IEnumerator Seconds(float s)
        {
            var end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

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

        /// <summary>A screenshot of the settled screen: waits out a map fade first, unless settle is false (a mid-effect frame).</summary>
        IEnumerator Shot(string name, bool settle = true)
        {
            if (settle) yield return Until(() => !root.View.Transitioning, "fade finished before " + name, 3);
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
