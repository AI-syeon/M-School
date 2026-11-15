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

namespace MagicCampus.Game
{
    /// <summary>
    /// The Unity root: opens Core.Game from the start fixture or the save, turns keys into Core calls, runs Game.Update once per
    /// frame (not while a window, the menu or the day report is up) and draws the screens in GameRoot.Ui.cs. Every rule lives in Core.
    /// Command line: --variant, --initial, --save-dir, --evidence-dir, --fresh, --probe.
    /// </summary>
    public sealed partial class GameRoot : MonoBehaviour
    {
        /// <summary>What holds the screen besides the world. Book is the tabbed window (J, K, I, M).</summary>
        enum Panel { None, Invite, Sleep, Book, Arrange }

        public enum Tab { Journal, Spellbook, Bag, Map }

        public enum MenuPage { Main, Controls }

        static readonly (Tab Tab, Key Key, string Name)[] Tabs =
            { (Tab.Journal, Key.J, "의뢰·약속"), (Tab.Spellbook, Key.K, "주문"), (Tab.Bag, Key.I, "가방"), (Tab.Map, Key.M, "지도") };

        string variant = "base", initial = "start-offered.json", saveDir, contentDir, fixture;
        bool fresh;
        Panel panel;
        Tab tab;
        float panelAt;
        string magicMessage = string.Empty, barHint = string.Empty;
        float barHintAt;
        List<string> panelEvents = new List<string>();
        int selected, menuFocus, sleepFocus, castsSeen;
        Dialogue invite;
        List<Action> inviteActions = new List<Action>();
        EventRun watchedEvent;
        int watchedDay;
        float stateAt;
        bool initialWritten;
        DayReport report;
        float reportAt, reportClosedAt = -10f;
        readonly UiSkin skin = new UiSkin();

        public Core.Game Game { get; private set; }
        public WorldView View { get; private set; }
        public DialogueBox Box { get; } = new DialogueBox();
        public MapScreen MapPanel { get; } = new MapScreen();
        public InventoryScreen BagPanel { get; } = new InventoryScreen();
        public ArrangeScreen ArrangePanel { get; } = new ArrangeScreen();
        public Hud Hud { get; } = new Hud();
        public string SavePath { get; private set; }
        public string ProbeCase { get; private set; }
        public string EvidenceDir { get; private set; }
        /// <summary>The latest thing the game told the player (also shown as a toast); probes read it.</summary>
        public string Status { get; private set; } = string.Empty;
        /// <summary>The latest spell result or incantation hint; empty until the first one.</summary>
        public string MagicMessage => magicMessage;
        public string Error { get; private set; }
        public bool MenuOpen { get; private set; }
        public MenuPage Page { get; private set; }
        public bool PanelOpen => panel != Panel.None;
        public bool InviteOpen => panel == Panel.Invite;
        public bool SleepPromptOpen => panel == Panel.Sleep;
        public bool BookOpen => panel == Panel.Book;
        /// <summary>The furniture arrange mode (F in a room that allows it) is open; the world is paused.</summary>
        public bool ArrangeOpen => panel == Panel.Arrange;
        public Tab BookTab => tab;
        public bool JournalOpen => BookOpen && tab == Tab.Journal;
        public bool SpellbookOpen => BookOpen && tab == Tab.Spellbook;
        public bool MapOpen => BookOpen && tab == Tab.Map;
        public bool InventoryOpen => BookOpen && tab == Tab.Bag;
        /// <summary>The end-of-day card is up and waits for a key.</summary>
        public bool DayReportOpen => report != null;
        public Dialogue Invitation => panel == Panel.Invite ? invite : null;
        public IReadOnlyList<string> PanelEvents => panelEvents;
        public float TextScale { get => skin.TextScale; set => skin.TextScale = value; }

        /// <summary>WebGL: the StreamingAssets files are still being fetched; OnGUI shows the loading card instead of the game.</summary>
        bool loading;
        int loadDone, loadTotal;

        IEnumerator Start()
        {
            if (GamePaths.NeedsDownload)
            {
                loading = true;
                string failure = null;
                yield return GamePaths.Download((done, total) => { loadDone = done; loadTotal = total; }, why => failure = why);
                loading = false;
                if (failure != null) Fail("파일 내려받기 실패", new IOException(failure));
            }
            if (Error == null) StartGame();
            if (ProbeCase != null) gameObject.AddComponent<Probe>();
        }

        void StartGame()
        {
            try
            {
                ParseArgs();
                var fixtures = Path.Combine(GamePaths.StreamingAssets, "FirstConnection");
                contentDir = Path.Combine(fixtures, variant);
                fixture = File.Exists(Path.Combine(contentDir, initial)) ? Path.Combine(contentDir, initial) : Path.Combine(fixtures, "base", initial);
                SavePath = Path.Combine(saveDir ?? Path.Combine(UnityEngine.Application.persistentDataPath, "Stage5"), "save.json");
                Game = fresh ? new Core.Game(Content.Load(contentDir), SaveFile.Read(fixture), SavePath) : Core.Game.Open(contentDir, fixture, SavePath);
                watchedDay = Game.State.Day;
                View = new WorldView(Path.Combine(GamePaths.StreamingAssets, "Stage5Art"), Camera.main);
                skin.ItemIcon = id => skin.Icon(Game, id);
                SetStatus("친구에게 다가가 E키로 말을 걸어 보세요.", null);
            }
            catch (Exception e) { Fail("초기화 실패", e); }
        }

        void ParseArgs()
        {
            string[] a;
            try { a = Environment.GetCommandLineArgs(); }
            catch (Exception) { return; }
            string Next(ref int i) => i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal) ? a[++i] : null;
            for (var i = 1; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--variant": variant = Next(ref i) ?? variant; break;
                    case "--initial": initial = Next(ref i) ?? initial; break;
                    case "--save-dir": saveDir = Next(ref i); break;
                    case "--evidence-dir": EvidenceDir = Next(ref i); break;
                    case "--fresh": fresh = true; break;
                    case "--probe": ProbeCase = Next(ref i) ?? "success-notes"; break;
                }
            }
        }

        void Update()
        {
            if (Game == null || Error != null) return;
            try
            {
                HandleKeys();
                var held = MenuOpen || panel != Panel.None || report != null || View.Freezing;
                var free = !held && Game.Event == null;
                Game.SetPlayerInput(free ? Axis(Key.D, Key.RightArrow) - Axis(Key.A, Key.LeftArrow) : 0, free ? Axis(Key.W, Key.UpArrow) - Axis(Key.S, Key.DownArrow) : 0);
                if (!held) Game.Update(Math.Min(Time.unscaledDeltaTime, 0.1));
                if (panel != Panel.None && panel != Panel.Sleep && panel != Panel.Arrange || MenuOpen || report != null || Game.Dialogue != null) Hud.HoldToasts(Time.unscaledDeltaTime);
                WatchProgress();
                View.Arrange = panel == Panel.Arrange ? ArrangePanel.Overlay(Game, View.ArtOf(Game.MapId)) : null;
                View.FocusOverride = panel == Panel.Arrange ? ArrangePanel.Focus(Game) : null;
                View.Draw(Game, MenuOpen || report != null ? 0f : Time.unscaledDeltaTime);
                if (!initialWritten) initialWritten = WriteState("initial-state.json");
                if (GamePaths.IsWeb) FlushSavesWhenChanged();
                if (Time.unscaledTime >= stateAt)
                {
                    stateAt = Time.unscaledTime + 1f;
                    WriteState("last-state.json");
                }
            }
            catch (Exception e) { Fail("프레임 오류", e); }
        }

        float flushAt;
        DateTime saveWrittenAt;

        /// <summary>WebGL: when the save file changed, push persistentDataPath to IndexedDB (once a second at most).</summary>
        void FlushSavesWhenChanged()
        {
            if (Time.unscaledTime < flushAt) return;
            flushAt = Time.unscaledTime + 1f;
            var at = File.Exists(SavePath) ? File.GetLastWriteTimeUtc(SavePath) : DateTime.MinValue;
            if (at == saveWrittenAt) return;
            saveWrittenAt = at;
            GamePaths.FlushSaves();
        }

        // ------------------------------------------------------------------ input

        void HandleKeys()
        {
            if (Pressed(Key.F9))
            {
                Reload();
                return;
            }
            if (report != null)
            {
                if (Time.unscaledTime - reportAt > 0.4f && AnyKey()) CloseReport();
                return;
            }
            if (panel == Panel.Arrange && !MenuOpen)
            {
                ArrangeKeys();
                return;
            }
            if (MagicContext && Game.Magic.Open && (Pressed(Key.Escape) || Pressed(Key.X) || Pressed(Key.Backspace) || Pressed(Key.Q)))
            {
                Game.Magic.Cancel();
                SetMagic("영창을 취소했습니다.");
                return;
            }
            if (Pressed(Key.Escape))
            {
                if (MenuOpen && Page != MenuPage.Main) Page = MenuPage.Main;
                else if (MenuOpen) MenuOpen = false;
                else if (panel != Panel.None) ClosePanel();
                else if (Game.Event != null) CancelEvent();
                else OpenMenu();
                return;
            }
            if (MenuOpen)
            {
                MenuKeys();
                return;
            }
            if (Pressed(Key.X) || Pressed(Key.Backspace))
            {
                if (panel != Panel.None) ClosePanel();
                else CancelEvent();
                return;
            }
            var d = panel == Panel.Invite ? invite : Game.Dialogue;
            if (d != null)
            {
                DialogueKeys(d);
                return;
            }
            if (panel == Panel.Sleep)
            {
                if (Pressed(Key.LeftArrow) || Pressed(Key.A) || Pressed(Key.RightArrow) || Pressed(Key.D)) sleepFocus = 1 - sleepFocus;
                if (Pressed(Key.Y) || (Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space) || Pressed(Key.E)) && sleepFocus == 0) Sleep();
                else if (Pressed(Key.N) || (Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space)) && sleepFocus == 1) ClosePanel();
                return;
            }
            if (panel == Panel.Book)
            {
                BookKeys();
                return;
            }
            if (Game.Event != null) return;
            var panelKey = Pressed(Key.E) || Pressed(Key.Tab) || Tabs.Any(t => Pressed(t.Key));
            if (Pressed(Key.E)) Interact();
            else if (Pressed(Key.Tab)) OpenBook(Tab.Journal);
            else foreach (var t in Tabs) if (Pressed(t.Key)) OpenBook(t.Tab);
            if (Pressed(Key.F) && !panelKey && !Game.Magic.Open) OpenArrange();
            else if (!panelKey) HandleMagicKeys();
        }

        // ------------------------------------------------------------------ furniture arrangement

        float repeatLeft, repeatRight, repeatUp, repeatDown;

        /// <summary>F: opens the arrange mode in a room that allows it; anywhere else it says so.</summary>
        public void OpenArrange()
        {
            if (!Game.Furniture.IsArrangeable(Game.MapId))
            {
                SetStatus("이 방에서는 가구를 옮길 수 없어요.", ToastKind.Info);
                return;
            }
            if (Game.Hidden || View.Transitioning) return;
            Game.Magic.Cancel();
            ArrangePanel.Open(Game);
            panel = Panel.Arrange;
            panelAt = Time.unscaledTime;
        }

        void CloseArrange()
        {
            panel = Panel.None;
            View.Arrange = null;
            View.FocusOverride = null;
        }

        /// <summary>A direction key: true on the press and then again at a steady rate while it is held.</summary>
        static bool Repeat(Key a, Key b, ref float next)
        {
            var keyboards = InputSystem.devices.OfType<Keyboard>().ToList();
            if (keyboards.Any(k => k[a].wasPressedThisFrame || k[b].wasPressedThisFrame))
            {
                next = Time.unscaledTime + 0.32f;
                return true;
            }
            if (!keyboards.Any(k => k[a].isPressed || k[b].isPressed) || Time.unscaledTime < next) return false;
            next = Time.unscaledTime + 0.09f;
            return true;
        }

        void ArrangeKeys()
        {
            var a = ArrangePanel;
            if (Pressed(Key.Escape) || Pressed(Key.X) || Pressed(Key.Backspace))
            {
                if (a.Mode == ArrangeScreen.Phase.Move) a.Back();
                else CloseArrange();
                return;
            }
            if (Pressed(Key.F))
            {
                CloseArrange();
                return;
            }
            var enter = Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space) || Pressed(Key.E);
            var store = Pressed(Key.C) || Pressed(Key.Delete);
            var rebuild = false;
            if (a.Mode == ArrangeScreen.Phase.Browse)
            {
                if (Repeat(Key.UpArrow, Key.W, ref repeatUp) || Repeat(Key.LeftArrow, Key.A, ref repeatLeft)) a.MoveCursor(Game, -1);
                if (Repeat(Key.DownArrow, Key.S, ref repeatDown) || Repeat(Key.RightArrow, Key.D, ref repeatRight)) a.MoveCursor(Game, 1);
                if (enter) a.Pick(Game);
            }
            else
            {
                if (Repeat(Key.LeftArrow, Key.A, ref repeatLeft)) a.MoveGhost(Game, -1, 0);
                if (Repeat(Key.RightArrow, Key.D, ref repeatRight)) a.MoveGhost(Game, 1, 0);
                if (Repeat(Key.UpArrow, Key.W, ref repeatUp)) a.MoveGhost(Game, 0, 1);
                if (Repeat(Key.DownArrow, Key.S, ref repeatDown)) a.MoveGhost(Game, 0, -1);
                if (enter) rebuild = a.Confirm(Game);
            }
            if (store) rebuild = a.Store(Game) || rebuild;
            if (rebuild) View.Rebuild(Game);
            var note = a.TakeNote();
            if (note.HasValue) SetStatus(note.Value.Text, note.Value.Kind);
        }

        void DialogueKeys(Dialogue d)
        {
            Box.Sync(d);
            var sideways = d.Choices != null;
            if (Pressed(Key.UpArrow) || Pressed(Key.W) || sideways && (Pressed(Key.LeftArrow) || Pressed(Key.A))) Box.Navigate(d, -1);
            if (Pressed(Key.DownArrow) || Pressed(Key.S) || sideways && (Pressed(Key.RightArrow) || Pressed(Key.D))) Box.Navigate(d, 1);
            for (var i = 0; sideways && i < Math.Min(9, d.Choices.Count); i++)
                if (Pressed(Key.Digit1 + i))
                {
                    Pick(d, i);
                    return;
                }
            if (!(Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space) || Pressed(Key.E))) return;
            if (d.Choices != null) Pick(d, Box.Focus);
            else if (!Box.Revealed) Box.RevealAll();
            else Game.Advance();
        }

        void Pick(Dialogue d, int index)
        {
            if (ReferenceEquals(d, invite)) inviteActions[index]();
            else if (Game.Dialogue == d) Game.Choose(index);
        }

        void BookKeys()
        {
            if (Pressed(Key.Tab))
            {
                SwitchTab((Tab)(((int)tab + 1) % Tabs.Length));
                return;
            }
            foreach (var t in Tabs)
                if (Pressed(t.Key))
                {
                    if (t.Tab == tab) ClosePanel();
                    else SwitchTab(t.Tab);
                    return;
                }
            bool Left() => Pressed(Key.LeftArrow) || Pressed(Key.A);
            bool Right() => Pressed(Key.RightArrow) || Pressed(Key.D);
            bool Up() => Pressed(Key.UpArrow) || Pressed(Key.W);
            bool Down() => Pressed(Key.DownArrow) || Pressed(Key.S);
            var enter = Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space);
            switch (tab)
            {
                case Tab.Map:
                    if (Left() || Up()) MapPanel.Cycle(Game, -1);
                    if (Right() || Down()) MapPanel.Cycle(Game, 1);
                    break;
                case Tab.Bag:
                    if (Left()) BagPanel.MoveCursor(-1, 0);
                    if (Right()) BagPanel.MoveCursor(1, 0);
                    if (Up()) BagPanel.MoveCursor(0, -1);
                    if (Down()) BagPanel.MoveCursor(0, 1);
                    if (enter) BagPanel.PickOrPlace(Game);
                    if (Pressed(Key.Delete)) BagPanel.DiscardOne(Game);
                    break;
                case Tab.Spellbook:
                    var spells = LearnedSpells();
                    if (Left()) selected--;
                    if (Right()) selected++;
                    if (Up()) selected -= 2;
                    if (Down()) selected += 2;
                    selected = Mathf.Clamp(selected, 0, Math.Max(0, spells.Count - 1));
                    for (var i = 0; i < Magic.QuickSlotCount; i++)
                        if (Pressed(Key.Digit1 + i) && spells.Count > 0) ToggleSlot(i, spells[selected].Id);
                    break;
                case Tab.Journal:
                    var rows = Rows();
                    if (Up()) selected--;
                    if (Down()) selected++;
                    selected = Mathf.Clamp(selected, 0, Math.Max(0, rows.Count - 1));
                    if (enter && rows.Count > 0) AcceptAndStart(rows[selected]);
                    break;
            }
        }

        void MenuKeys()
        {
            if (Page == MenuPage.Controls)
            {
                if (Pressed(Key.Enter) || Pressed(Key.Space)) Page = MenuPage.Main;
                return;
            }
            if (Pressed(Key.UpArrow) || Pressed(Key.W)) menuFocus = (menuFocus + MenuItems.Length - 1) % MenuItems.Length;
            if (Pressed(Key.DownArrow) || Pressed(Key.S)) menuFocus = (menuFocus + 1) % MenuItems.Length;
            if (Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space)) MenuAction(menuFocus);
        }

        static readonly string[] MenuItems = { "계속하기", "조작법", "글자 크기", "마지막 저장 불러오기" };

        void MenuAction(int i)
        {
            menuFocus = i;
            switch (i)
            {
                case 0: MenuOpen = false; break;
                case 1: Page = MenuPage.Controls; break;
                case 2: TextScale = TextScale > 1f ? 1f : 2f; break;
                case 3: Reload(); break;
            }
        }

        /// <summary>The one input context for spells: nothing else (menu, panel, dialogue, event) has taken the frame.</summary>
        bool MagicContext => !MenuOpen && panel == Panel.None && Game.Event == null && report == null;

        /// <summary>
        /// Free-control spell keys. Q opens the incantation; while it is open 1-4 add a rune and Enter confirms, one key per frame
        /// (the key order inside a frame is lost, so two keys are refused). Closed, one of 1-4 casts its quick slot.
        /// </summary>
        void HandleMagicKeys()
        {
            var magic = Game.Magic;
            int runeKeys = 0, rune = 0;
            for (var i = 0; i < Magic.QuickSlotCount; i++)
                if (Pressed(Key.Digit1 + i))
                {
                    runeKeys++;
                    rune = i + 1;
                }
            var enter = Pressed(Key.Enter) || Pressed(Key.NumpadEnter);
            if (!magic.Open)
            {
                if (Pressed(Key.Q))
                {
                    magic.Begin();
                    SetMagic("영창을 열었습니다. 룬 1-4를 차례로 누르고 Enter로 확정하세요.");
                }
                else if (runeKeys == 1) CastQuickSlot(rune - 1);
                return;
            }
            if (runeKeys + (enter ? 1 : 0) > 1)
            {
                SetMagic("차례로 눌러 주세요. 한 번에 키 하나만 입력됩니다.", true);
                return;
            }
            if (runeKeys == 1)
            {
                if (magic.AddRune(rune) == IncantationStep.Cleared) SetMagic("그 순서로 시작하는 주문이 없어 입력을 비웠습니다. 다시 입력하세요.", true);
            }
            else if (enter)
            {
                if (magic.Confirm() == IncantationStep.Cast) SetMagic(magic.Last.Message);
                else SetMagic(magic.Runes.Count == 0 ? "룬을 먼저 입력하세요." : "아직 완성되지 않았습니다. 룬을 더 입력하세요.", true);
            }
        }

        void CastQuickSlot(int slot)
        {
            switch (Game.Magic.CastSlot(slot))
            {
                case SlotOutcome.Empty:
                    SetMagic("슬롯 " + (slot + 1) + "번이 비어 있습니다. 주문 수첩(K)에서 정하세요.");
                    Hud.Push(magicMessage, ToastKind.Bad);
                    break;
                case SlotOutcome.Cast:
                    Hud.FlashSlot(slot);
                    SetMagic(Game.Magic.Last.Message);
                    break;
            }
        }

        /// <summary>The spellbook's slot editor: puts a learned spell in a slot (null empties it). Refused while locked or during an event.</summary>
        public bool AssignQuickSlot(int slot, string spellId)
        {
            var ok = Game.Magic.SetSlot(slot, spellId);
            var spell = spellId == null ? null : Game.Content.Spell(spellId);
            if (ok) SetMagic(spell == null ? "슬롯 " + (slot + 1) + "번을 비웠습니다." : "슬롯 " + (slot + 1) + "번: " + spell.Name + ". 하루를 마칠 때 저장됩니다.");
            else if (!Game.State.QuickCastUnlocked) SetMagic("빠른 시전이 아직 열리지 않았습니다.");
            else if (Game.Event != null) SetMagic("이벤트 중에는 슬롯을 바꿀 수 없습니다.");
            else if (spell != null && !spell.AllowQuickCast) SetMagic("빠른 시전 슬롯에 넣을 수 없는 주문입니다. (" + spell.Name + ")");
            else SetMagic("슬롯을 바꿀 수 없습니다.");
            Hud.Push(magicMessage, ok ? ToastKind.Good : ToastKind.Bad);
            return ok;
        }

        /// <summary>A slot key or click in the spellbook: puts the spell there, or empties the slot when it already holds it.</summary>
        void ToggleSlot(int slot, string spellId) => AssignQuickSlot(slot, Game.State.QuickSlots[slot] == spellId ? null : spellId);

        List<SpellDef> LearnedSpells() => Game.State.KnownSpells.Select(Game.Content.Spell).Where(s => s != null).ToList();

        // ------------------------------------------------------------------ panels

        public void OpenBook(Tab which)
        {
            if (panel != Panel.Book) panelAt = Time.unscaledTime;
            Game.Magic.Cancel();
            panel = Panel.Book;
            SwitchTab(which);
        }

        void SwitchTab(Tab which)
        {
            tab = which;
            selected = 0;
            panelEvents = which == Tab.Journal ? Game.State.Appointments.Keys.ToList() : new List<string>();
            if (which == Tab.Map) MapPanel.Open(Game);
            if (which == Tab.Bag) BagPanel.Open();
        }

        public void OpenJournal() => OpenBook(Tab.Journal);
        public void OpenSpellbook() => OpenBook(Tab.Spellbook);
        /// <summary>M: the full map, starting on the area the player is in.</summary>
        public void OpenMap() => OpenBook(Tab.Map);
        /// <summary>I: the bag, key items and mailbox.</summary>
        public void OpenInventory() => OpenBook(Tab.Bag);

        /// <summary>Every way the menu opens comes through here so the incantation is dropped.</summary>
        public void OpenMenu()
        {
            Game.Magic.Cancel();
            MenuOpen = true;
            Page = MenuPage.Main;
            menuFocus = 0;
            panelAt = Time.unscaledTime;
        }

        /// <summary>Shows the menu's controls page (the probe's tour opens it this way after the menu).</summary>
        public void ShowControls()
        {
            if (!MenuOpen) OpenMenu();
            Page = MenuPage.Controls;
        }

        void ClosePanel()
        {
            panel = Panel.None;
            invite = null;
        }

        public void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && Game != null) Game.Magic.Cancel();
        }

        void SetMagic(string text, bool barHintToo = false)
        {
            magicMessage = text;
            if (!barHintToo) return;
            barHint = text;
            barHintAt = Time.unscaledTime;
        }

        static bool Pressed(Key key) => InputSystem.devices.OfType<Keyboard>().Any(k => k[key].wasPressedThisFrame);

        static bool AnyKey() => InputSystem.devices.OfType<Keyboard>().Any(k => k.anyKey.wasPressedThisFrame) || (Mouse.current?.leftButton.wasPressedThisFrame ?? false);

        static float Axis(Key a, Key b) => InputSystem.devices.OfType<Keyboard>().Any(k => k[a].isPressed || k[b].isPressed) ? 1f : 0f;

        void Interact()
        {
            if (Game.NearBed)
            {
                Game.Magic.Cancel();
                panel = Panel.Sleep;
                sleepFocus = 0;
                panelAt = Time.unscaledTime;
                return;
            }
            var invites = Game.Interact();
            if (invites.Count == 0) SetStatus("근처에 말을 걸 사람이 없어요.", ToastKind.Info);
            else OpenInvite(invites);
        }

        /// <summary>E near an NPC: one row per invitation they offer (it accepts and starts it) and "다음에". Accepting without starting is in the journal.</summary>
        void OpenInvite(List<Invite> invites)
        {
            Game.Magic.Cancel();
            var choices = new List<string>();
            inviteActions = new List<Action>();
            foreach (var inv in invites)
            {
                var id = inv.EventId;
                choices.Add(inv.Label);
                inviteActions.Add(() => AcceptAndStart(id));
            }
            choices.Add("다음에");
            inviteActions.Add(ClosePanel);
            var first = invites[0];
            var line = invites.Count == 1 ? (first.Status == Appointment.Offered ? "「" + first.Label + "」, 같이 할래?" : "약속한 「" + first.Label + "」, 지금 할까?")
                : "같이 하고 싶은 게 몇 가지 있어. 뭐부터 할까?";
            invite = new Dialogue { Speaker = first.NpcId, Text = line, Choices = choices };
            panel = Panel.Invite;
            panelAt = Time.unscaledTime;
            panelEvents = invites.Select(i => i.EventId).ToList();
            selected = 0;
        }

        List<string> Rows() => Game.State.Appointments.Keys.ToList();

        /// <summary>An invitation row: accepts an offered invitation, then starts its event.</summary>
        public void AcceptAndStart(string eventId)
        {
            Game.Accept(eventId);
            StartEvent(eventId);
        }

        void StartEvent(string eventId)
        {
            ClosePanel();
            if (Game.StartEvent(eventId)) SetStatus("약속 장소로 이동합니다.", null);
            else SetStatus("지금은 시작할 수 없는 약속입니다.", ToastKind.Bad);
        }

        void CancelEvent()
        {
            if (Game.Event == null) return;
            Game.CancelEvent();
            SetStatus("이벤트를 취소했습니다. 약속은 그대로 남아 있어요.", ToastKind.Info);
        }

        /// <summary>The bed prompt's "잔다": ends the day through Core's EndDay(Slept).</summary>
        public void Sleep()
        {
            ClosePanel();
            Game.EndDay(DayEnd.Slept);
            WatchProgress();
        }

        /// <summary>F9: a new Core.Game from the last save, or from the start fixture when nothing was saved yet.</summary>
        public void Reload()
        {
            var had = File.Exists(SavePath);
            Game = Core.Game.Open(contentDir, fixture, SavePath);
            View.Rebuild(Game);
            ClosePanel();
            MenuOpen = false;
            report = null;
            magicMessage = barHint = string.Empty;
            watchedEvent = null;
            watchedDay = Game.State.Day;
            castsSeen = Game.Magic.Casts;
            Hud.Clear();
            SetStatus(had ? "마지막 저장을 불러왔습니다." : "저장이 없어 처음 상태로 돌아갔습니다.", ToastKind.Info);
        }

        void CloseReport()
        {
            report = null;
            reportClosedAt = Time.unscaledTime;
        }

        // ------------------------------------------------------------------ progress watching

        void WatchProgress()
        {
            WatchCasts();
            WatchNotices();
            if (Game.Event != null) watchedEvent = Game.Event;
            else if (watchedEvent != null)
            {
                if (Game.State.Completed.Contains(watchedEvent.Def.Id)) SetStatus("함께한 시간을 기억했습니다.", ToastKind.Good);
                watchedEvent = null;
            }
            if (Game.State.Day == watchedDay) return;
            watchedDay = Game.State.Day;
            ClosePanel();
            MenuOpen = false;
            SetStatus((Game.LastDayEnd == DayEnd.PassedOut ? "새벽 2시에 쓰러져 잠들었습니다. " : "푹 잤습니다. ") + (watchedDay + 1) + "일 아침입니다. 하루를 저장했습니다.", null);
            report = Game.LastDayReport;
            reportAt = Time.unscaledTime;
            Hud.Clear();
        }

        /// <summary>A spell result floats over its target (green done, coral refused); a refusal also explains itself in a toast.</summary>
        void WatchCasts()
        {
            if (Game.Magic.Casts == castsSeen) return;
            castsSeen = Game.Magic.Casts;
            var r = Game.Magic.Last;
            if (r == null) return;
            var target = Game.Content.MagicTargets.FirstOrDefault(t => t.Id == r.TargetId && t.MapId == Game.MapId);
            var p = Game.Player;
            double x = target?.X ?? p.X, y = target?.Y ?? p.Y + 0.9;
            if (r.Ok)
            {
                Hud.Float(r.UnlockedQuickCast ? "빠른 시전!" : r.Observe ?? Game.Content.Spell(r.SpellId)?.Name, Game.MapId, x, y, UiSkin.Good);
                if (r.UnlockedQuickCast) Hud.Push(r.Message, ToastKind.Good);
                return;
            }
            var word = r.Failure == CastFailure.NoTarget ? "대상 없음" : r.Failure == CastFailure.Requirement ? "조건 부족" : r.Failure == CastFailure.Protected ? "막혔다"
                : r.Failure == CastFailure.NotLearned ? "모르는 주문" : "시전 불가";
            Hud.Float(word, Game.MapId, x, y, UiSkin.Bad);
            Hud.Push(r.Message, ToastKind.Bad);
        }

        /// <summary>
        /// Core's notices become toasts. Items received outside a quest reward (events, the probe) get one line of their own;
        /// those a reward notice already named are not repeated. The mailbox gets its own sentence (domain-02 §1.4).
        /// </summary>
        void WatchNotices()
        {
            var notices = Game.DrainNotices();
            foreach (var n in notices)
                Hud.Push(n.Text, n.Kind == NoticeKind.QuestDone ? ToastKind.QuestDone : n.Kind == NoticeKind.QuestStep ? ToastKind.QuestStep : ToastKind.Item, n.Kind == NoticeKind.ItemReceived ? n.ItemId : null);
            var got = Game.Inventory.Received;
            if (got.Count == 0) return;
            var announced = notices.Where(n => n.Kind == NoticeKind.ItemReceived).Select(n => n.ItemId).ToList();
            var rest = new List<AddResult>();
            foreach (var r in got)
                if (!announced.Remove(r.ItemId)) rest.Add(r);
            got.Clear();
            if (rest.Count == 0) return;
            var names = rest.Select(r => InventoryScreen.ItemName(Game, r.ItemId) + (r.ToNotebook ? "(수첩)" : " ×" + (r.ToBag + r.ToMailbox))).ToList();
            var mailed = rest.Sum(r => r.ToMailbox);
            SetStatus("받았습니다: " + string.Join(", ", names) + (mailed > 0 ? " · 가방이 가득 차 " + mailed + "개를 기숙사 우편 보관함으로 보냈어요." : ""), ToastKind.Item, rest[0].ItemId);
        }

        string Name(string id) => Game.Content.Npcs.FirstOrDefault(n => n.Id == id)?.Name ?? id;

        string Label(string eventId) => Command.Parse(Game.Content.Event(eventId).Trigger)[0].Args[1];

        /// <summary>Records the line probes read and, unless kind is null, shows it as a toast.</summary>
        void SetStatus(string text, ToastKind? kind, string itemId = null)
        {
            Status = text;
            if (kind.HasValue) Hud.Push(text, kind.Value, itemId);
        }

        void Fail(string what, Exception e)
        {
            Error = what + ": " + e.Message;
            Debug.LogException(e);
        }

        bool WriteState(string file)
        {
            if (string.IsNullOrEmpty(EvidenceDir) || Game == null) return false;
            Directory.CreateDirectory(EvidenceDir);
            var p = Game.Player;
            var state = new
            {
                mapId = p.MapId, playerX = p.X, playerY = p.Y, day = Game.State.Day, minute = Game.State.Minute, wallet = Game.State.Wallet,
                eventId = Game.Event?.Def.Id, status = Status, error = Error, menuOpen = MenuOpen, panel = panel == Panel.Book ? "Book:" + tab : panel.ToString(),
                appointments = Game.State.Appointments, completed = Game.State.Completed,
                inventory = new { bag = Game.State.Bag, keyItems = Game.State.KeyItems, mailbox = Game.State.Mailbox, selected = BagPanel.Selected, holding = BagPanel.Holding, message = BagPanel.Message },
                map = new { visited = Game.State.VisitedMaps, shown = MapPanel.Shown },
                furniture = new { open = panel == Panel.Arrange, mode = ArrangePanel.Mode.ToString(), cursor = ArrangePanel.Cursor, arrangements = Game.State.Arrangements },
                quests = new { active = Game.Quests.Active.Select(q => q.Def.Id), completed = Game.State.CompletedQuests },
                magic = new
                {
                    open = Game.Magic.Open, runes = Game.Magic.Runes, casts = Game.Magic.Casts, message = magicMessage,
                    knownSpells = Game.State.KnownSpells, quickCastUnlocked = Game.State.QuickCastUnlocked, quickSlots = Game.State.QuickSlots,
                    situationLog = Game.State.SituationLog,
                    targets = Game.Content.MagicTargets.ToDictionary(t => t.Id, t => Game.Magic.Tags(t.Id).OrderBy(x => x).ToList())
                }
            };
            File.WriteAllText(Path.Combine(EvidenceDir, file), JsonConvert.SerializeObject(state, Formatting.Indented), new UTF8Encoding(false));
            return true;
        }

        void OnApplicationQuit() => WriteState("last-state.json");
    }
}
