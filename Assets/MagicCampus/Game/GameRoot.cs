using System;
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
    /// frame and draws the HUD, the invite/journal panel, the menu and the dialogue box. Every rule lives in Core.
    /// Command line: --variant, --initial, --save-dir, --evidence-dir, --fresh, --probe.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        enum Panel { None, Invite, Journal, Sleep, Spellbook }

        static readonly Dictionary<string, string> TagNames = new Dictionary<string, string>
        {
            ["dark"] = "어두움", ["lit"] = "밝음", ["wet"] = "젖음", ["frozen"] = "얼음", ["record"] = "기록", ["broken"] = "부서짐",
            ["seed_dry"] = "마른 씨앗", ["plant"] = "식물", ["resonant"] = "공명", ["liftable"] = "들 수 있음", ["floating"] = "떠 있음",
            ["germinating"] = "발아 준비", ["sound_source"] = "소리 담김", ["wind_path"] = "바람길", ["warded"] = "안전막", ["shaped"] = "다듬어짐",
            ["mended"] = "수선됨", ["restored"] = "복구됨", ["pinned"] = "고정됨", ["husk_open"] = "껍질 열림", ["soot"] = "그을음",
            ["scene_shown"] = "장면 투사", ["sound_aligned"] = "소리 정렬", ["person"] = "사람", ["private"] = "사유물", ["noMagic"] = "마법 불가"
        };

        string variant = "base", initial = "start-offered.json", saveDir, contentDir, fixture;
        bool fresh;
        Panel panel;
        string panelTitle;
        string magicMessage = string.Empty;
        Vector2 bookScroll;
        List<string> panelEvents = new List<string>();
        int selected;
        EventRun watchedEvent;
        int watchedDay;
        float stateAt;
        bool initialWritten;
        readonly Styles styles = new Styles();
        Vector2 hudScroll, panelScroll, menuScroll;

        public Core.Game Game { get; private set; }
        public WorldView View { get; private set; }
        public DialogueBox Box { get; } = new DialogueBox();
        public string SavePath { get; private set; }
        public string ProbeCase { get; private set; }
        public string EvidenceDir { get; private set; }
        public string Status { get; private set; } = string.Empty;
        /// <summary>The latest spell result or incantation hint shown in the HUD; empty until the first one.</summary>
        public string MagicMessage => magicMessage;
        public string Error { get; private set; }
        public bool MenuOpen { get; private set; }
        public bool PanelOpen => panel != Panel.None;
        public bool SleepPromptOpen => panel == Panel.Sleep;
        public bool SpellbookOpen => panel == Panel.Spellbook;
        public IReadOnlyList<string> PanelEvents => panelEvents;
        public float TextScale { get => styles.TextScale; set => styles.TextScale = value; }

        void Start()
        {
            try
            {
                ParseArgs();
                var fixtures = Path.Combine(UnityEngine.Application.streamingAssetsPath, "FirstConnection");
                contentDir = Path.Combine(fixtures, variant);
                fixture = File.Exists(Path.Combine(contentDir, initial)) ? Path.Combine(contentDir, initial) : Path.Combine(fixtures, "base", initial);
                SavePath = Path.Combine(saveDir ?? Path.Combine(UnityEngine.Application.persistentDataPath, "Stage5"), "save.json");
                Game = fresh ? new Core.Game(Content.Load(contentDir), SaveFile.Read(fixture), SavePath) : Core.Game.Open(contentDir, fixture, SavePath);
                watchedDay = Game.State.Day;
                View = new WorldView(Path.Combine(UnityEngine.Application.streamingAssetsPath, "Stage5Art"), Camera.main);
                SetStatus("친구에게 다가가 E키로 말을 걸어 보세요.");
            }
            catch (Exception e) { Fail("초기화 실패", e); }
            if (ProbeCase != null) gameObject.AddComponent<Probe>();
        }

        void ParseArgs()
        {
            var a = Environment.GetCommandLineArgs();
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
                if (!MenuOpen)
                {
                    var free = panel == Panel.None && Game.Event == null;
                    Game.SetPlayerInput(free ? Axis(Key.D, Key.RightArrow) - Axis(Key.A, Key.LeftArrow) : 0, free ? Axis(Key.W, Key.UpArrow) - Axis(Key.S, Key.DownArrow) : 0);
                    Game.Update(Math.Min(Time.unscaledDeltaTime, 0.1));
                    WatchProgress();
                }
                View.Draw(Game, MenuOpen ? 0f : Time.unscaledDeltaTime);
                if (!initialWritten) initialWritten = WriteState("initial-state.json");
                if (Time.unscaledTime >= stateAt)
                {
                    stateAt = Time.unscaledTime + 1f;
                    WriteState("last-state.json");
                }
            }
            catch (Exception e) { Fail("프레임 오류", e); }
        }

        void HandleKeys()
        {
            if (Pressed(Key.F9))
            {
                Reload();
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
                if (MenuOpen) MenuOpen = false;
                else if (panel != Panel.None) panel = Panel.None;
                else if (Game.Event != null) CancelEvent();
                else OpenMenu();
                return;
            }
            if (MenuOpen) return;
            if (Pressed(Key.X) || Pressed(Key.Backspace))
            {
                if (panel != Panel.None) panel = Panel.None;
                else CancelEvent();
                return;
            }
            var d = Game.Dialogue;
            if (d != null)
            {
                var sideways = d.Choices != null;
                if (Pressed(Key.UpArrow) || Pressed(Key.W) || sideways && (Pressed(Key.LeftArrow) || Pressed(Key.A))) Box.Navigate(d, -1);
                if (Pressed(Key.DownArrow) || Pressed(Key.S) || sideways && (Pressed(Key.RightArrow) || Pressed(Key.D))) Box.Navigate(d, 1);
                for (var i = 0; sideways && i < Math.Min(9, d.Choices.Count); i++)
                    if (Pressed(Key.Digit1 + i)) Game.Choose(i);
                if (Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space))
                {
                    if (d.Choices == null) Game.Advance();
                    else if (Game.Dialogue == d) Game.Choose(Box.Focus);
                }
                return;
            }
            if (panel == Panel.Sleep)
            {
                if (Pressed(Key.Enter) || Pressed(Key.Space) || Pressed(Key.Y)) Sleep();
                if (Pressed(Key.N)) panel = Panel.None;
                return;
            }
            if (panel != Panel.None)
            {
                if (panel == Panel.Spellbook && Pressed(Key.K))
                {
                    panel = Panel.None;
                    return;
                }
                if (Pressed(Key.UpArrow) || Pressed(Key.W)) selected = Math.Max(0, selected - 1);
                if (Pressed(Key.DownArrow) || Pressed(Key.S)) selected++;
                if (panel == Panel.Spellbook)
                    for (var i = 0; i < Magic.QuickSlotCount; i++)
                        if (Pressed(Key.Digit1 + i) && LearnedSpells().Count > 0) AssignQuickSlot(i, LearnedSpells()[Math.Min(selected, LearnedSpells().Count - 1)].Id);
                if ((Pressed(Key.Enter) || Pressed(Key.Space)) && Rows().Count > 0) AcceptAndStart(Rows()[Math.Min(selected, Rows().Count - 1)]);
                return;
            }
            if (Game.Event != null) return;
            var panelKey = Pressed(Key.E) || Pressed(Key.J) || Pressed(Key.Tab) || Pressed(Key.K);
            if (Pressed(Key.E)) Interact();
            if (Pressed(Key.J) || Pressed(Key.Tab)) OpenPanel(Panel.Journal, "일지 · 약속", Game.State.Appointments.Keys.ToList());
            if (Pressed(Key.K)) OpenSpellbook();
            if (!panelKey) HandleMagicKeys();
        }

        /// <summary>The one input context for spells: nothing else (menu, panel, dialogue, event) has taken the frame.</summary>
        bool MagicContext => !MenuOpen && panel == Panel.None && Game.Event == null;

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
                SetMagic("차례로 눌러 주세요. 한 번에 키 하나만 입력됩니다.");
                return;
            }
            if (runeKeys == 1)
            {
                if (magic.AddRune(rune) == IncantationStep.Cleared) SetMagic("그 순서로 시작하는 주문이 없어 입력을 비웠습니다. 다시 입력하세요.");
            }
            else if (enter)
            {
                if (magic.Confirm() == IncantationStep.Cast) SetMagic(magic.Last.Message);
                else SetMagic(magic.Runes.Count == 0 ? "룬을 먼저 입력하세요." : "아직 완성되지 않았습니다. 룬을 더 입력하세요.");
            }
        }

        void CastQuickSlot(int slot)
        {
            switch (Game.Magic.CastSlot(slot))
            {
                case SlotOutcome.Empty: SetMagic("슬롯 " + (slot + 1) + "번이 비어 있습니다. 주문 수첩(K)에서 정하세요."); break;
                case SlotOutcome.Cast: SetMagic(Game.Magic.Last.Message); break;
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
            return ok;
        }

        List<SpellDef> LearnedSpells() => Game.State.KnownSpells.Select(Game.Content.Spell).Where(s => s != null).ToList();

        void OpenSpellbook() => OpenPanel(Panel.Spellbook, "주문 수첩", new List<string>());

        /// <summary>Every way the menu opens (key or HUD button) comes through here so the incantation is dropped.</summary>
        public void OpenMenu()
        {
            Game.Magic.Cancel();
            MenuOpen = true;
        }

        public void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && Game != null) Game.Magic.Cancel();
        }

        void SetMagic(string text) => magicMessage = text;

        static bool Pressed(Key key) => InputSystem.devices.OfType<Keyboard>().Any(k => k[key].wasPressedThisFrame);

        static float Axis(Key a, Key b) => InputSystem.devices.OfType<Keyboard>().Any(k => k[a].isPressed || k[b].isPressed) ? 1f : 0f;

        void Interact()
        {
            if (Game.NearBed)
            {
                OpenPanel(Panel.Sleep, "잠자리", new List<string>());
                return;
            }
            var invites = Game.Interact();
            if (invites.Count == 0) SetStatus("근처에 상호작용할 대상이 없습니다.");
            else OpenPanel(Panel.Invite, "초대 · " + Name(invites[0].NpcId), invites.Select(i => i.EventId).ToList());
        }

        void OpenPanel(Panel kind, string title, List<string> events)
        {
            Game.Magic.Cancel();
            panel = kind;
            panelTitle = title;
            panelEvents = events;
            selected = 0;
        }

        List<string> Rows() => panelEvents.Where(Game.State.Appointments.ContainsKey).ToList();

        /// <summary>The panel's "수락 후 시작": accepts an offered invitation, then starts its event.</summary>
        public void AcceptAndStart(string eventId)
        {
            Game.Accept(eventId);
            StartEvent(eventId);
        }

        void StartEvent(string eventId)
        {
            panel = Panel.None;
            SetStatus(Game.StartEvent(eventId) ? "약속 장소로 이동합니다." : "지금은 시작할 수 없는 약속입니다.");
        }

        void CancelEvent()
        {
            if (Game.Event == null) return;
            Game.CancelEvent();
            SetStatus("이벤트를 취소했습니다. 약속은 그대로 남아 있습니다.");
        }

        /// <summary>The bed prompt's "잔다": ends the day through Core's EndDay(Slept).</summary>
        public void Sleep()
        {
            panel = Panel.None;
            Game.EndDay(DayEnd.Slept);
            WatchProgress();
        }

        /// <summary>F9: a new Core.Game from the last save, or from the start fixture when nothing was saved yet.</summary>
        public void Reload()
        {
            var had = File.Exists(SavePath);
            Game = Core.Game.Open(contentDir, fixture, SavePath);
            panel = Panel.None;
            MenuOpen = false;
            magicMessage = string.Empty;
            watchedEvent = null;
            watchedDay = Game.State.Day;
            SetStatus(had ? "마지막 저장을 불러왔습니다." : "저장이 없어 처음 상태로 돌아갔습니다.");
        }

        void WatchProgress()
        {
            if (Game.Event != null) watchedEvent = Game.Event;
            else if (watchedEvent != null)
            {
                if (Game.State.Completed.Contains(watchedEvent.Def.Id)) SetStatus("함께한 시간을 기억했습니다.");
                watchedEvent = null;
            }
            if (Game.State.Day == watchedDay) return;
            watchedDay = Game.State.Day;
            panel = Panel.None;
            SetStatus((Game.LastDayEnd == DayEnd.PassedOut ? "새벽 2시에 쓰러져 잠들었습니다. " : "푹 잤습니다. ")
                + (watchedDay + 1) + "일 아침입니다. 하루를 저장했습니다.");
        }

        string Name(string id) => Game.Content.Npcs.FirstOrDefault(n => n.Id == id)?.Name ?? id;

        string Label(string eventId) => Command.Parse(Game.Content.Event(eventId).Trigger)[0].Args[1];

        void SetStatus(string text) => Status = text;

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
                eventId = Game.Event?.Def.Id, status = Status, error = Error, menuOpen = MenuOpen, panel = panel.ToString(),
                appointments = Game.State.Appointments, completed = Game.State.Completed,
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

        void OnGUI()
        {
            styles.Ensure();
            var w = Screen.width;
            var h = Screen.height;
            var hudH = Mathf.Min(h * 0.34f, styles.HudSize * 1.5f * 8.6f + 8f);
            if (View != null) View.TopInset = Mathf.CeilToInt(hudH + 8f);
            if (Game != null) DrawHud(new Rect(4, 4, w - 8, hudH));
            if (Error != null) DrawError(new Rect(4, h * 0.7f - 4, w - 8, h * 0.3f));
            if (Game == null) return;
            DrawTargetLabels();
            if (panel == Panel.Sleep) DrawSleep(w, h);
            else if (panel == Panel.Spellbook) DrawSpellbook(new Rect(4, hudH + 8f, w - 8, Mathf.Max(60f, h - hudH - 16f)));
            else if (panel != Panel.None) DrawPanel(new Rect(4, hudH + 8f, w - 8, Mathf.Max(60f, h - hudH - 16f)));
            var d = Game.Dialogue;
            if (d != null)
            {
                var clicked = Box.Draw(d, d.Speaker == null ? null : Name(d.Speaker), styles);
                if (clicked == DialogueBox.Advance) Game.Advance();
                else if (clicked >= 0) Game.Choose(clicked);
            }
            if (MenuOpen) DrawMenu(w, h);
        }

        Vector2 Scroll(Vector2 s) => GUILayout.BeginScrollView(s, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);

        void DrawHud(Rect r)
        {
            GUI.Box(r, GUIContent.none, styles.Box);
            GUILayout.BeginArea(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height - 4));
            hudScroll = Scroll(hudScroll);
            GUILayout.Label("매직캠퍼스 · 첫 약속", styles.Bold);
            var s = Game.State;
            GUILayout.Label((s.Day + 1) + "일 " + (s.Minute / 60 % 24).ToString("00") + ":" + (s.Minute % 60).ToString("00") + " · 소지금 " + s.Wallet + " 크라운", styles.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("일지(J)", styles.Button, GUILayout.ExpandWidth(false))) OpenPanel(Panel.Journal, "일지 · 약속", s.Appointments.Keys.ToList());
            if (GUILayout.Button("주문 수첩(K)", styles.Button, GUILayout.ExpandWidth(false))) OpenSpellbook();
            if (GUILayout.Button("메뉴(Esc)", styles.Button, GUILayout.ExpandWidth(false))) OpenMenu();
            GUILayout.EndHorizontal();
            GUILayout.Label(Status, styles.Label);
            var magicLine = MagicLine();
            if (magicLine.Length > 0) GUILayout.Label(magicLine, styles.Bold);
            if (magicMessage.Length > 0) GUILayout.Label("주문: " + magicMessage, styles.Label);
            GUILayout.Label("WASD 이동 · E 상호작용(침대에서 잠자기) · Q 영창(1-4 룬 → Enter 확정) · 1-4 빠른 시전(해금 후) · K 주문 수첩 · J 일지 · X 취소 · Esc 메뉴 · F9 마지막 저장 불러오기", styles.Label);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>The incantation line while it is open, otherwise the quick slots once they are unlocked.</summary>
        string MagicLine()
        {
            var magic = Game.Magic;
            if (magic.Open)
            {
                var typed = magic.Runes.Count == 0 ? "(비어 있음)" : string.Join(" → ", magic.Runes);
                var exact = magic.Exact;
                var ready = exact == null ? (magic.Runes.Count == 0 ? "룬 1-4를 차례로 누르세요" : "룬을 더 입력하세요 (확정 불가)")
                    : Game.State.KnownSpells.Contains(exact.Id) ? "Enter로 확정 가능 · " + exact.Name : "Enter로 확정 가능";
                return "영창 입력: " + typed + " · " + ready + " · 취소: Q / Esc / X / Backspace";
            }
            if (!Game.State.QuickCastUnlocked) return string.Empty;
            return "빠른 슬롯: " + string.Join(" · ", Game.State.QuickSlots.Select((id, i) => (i + 1) + " " + (id == null ? "비어 있음" : Game.Content.Spell(id)?.Name ?? "?")));
        }

        /// <summary>Names of the targets on this map under their placeholder squares. The aimed one gets an arrow and lists its tags.</summary>
        void DrawTargetLabels()
        {
            if (Game.Hidden || View == null || panel != Panel.None || MenuOpen || Game.Dialogue != null) return;
            var aimed = Game.Magic.Aim(3);
            foreach (var t in Game.Content.MagicTargets)
            {
                if (t.MapId != Game.MapId) continue;
                var at = View.GuiPoint(t.X, t.Y - 0.45);
                var tags = aimed == t ? string.Join(" ", Game.Magic.Tags(t.Id).Select(x => TagNames.TryGetValue(x, out var n) ? n : x)) : "";
                var text = (aimed == t ? "> " : "") + t.Name + (tags.Length > 0 ? "\n" + tags : "");
                var size = styles.Label.CalcSize(new GUIContent(text));
                var r = new Rect(at.x - size.x / 2 - 3, at.y, size.x + 6, size.y + 2);
                GUI.Box(r, GUIContent.none, styles.Box);
                GUI.Label(new Rect(r.x + 3, r.y + 1, size.x + 2, size.y), text, styles.Label);
            }
        }

        void DrawSpellbook(Rect r)
        {
            var spells = LearnedSpells();
            selected = Mathf.Clamp(selected, 0, Math.Max(0, spells.Count - 1));
            var state = Game.State;
            var editable = state.QuickCastUnlocked && Game.Event == null;
            GUI.Box(r, GUIContent.none, styles.Box);
            GUI.Box(r, GUIContent.none, styles.Box);
            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12));
            GUILayout.Label("주문 수첩 · 배운 주문 " + spells.Count + "개", styles.Bold);
            GUILayout.Label(!state.QuickCastUnlocked ? "빠른 시전은 아직 열리지 않았습니다. 전용 주문을 자기 자신에게 성공시키면 슬롯 4개가 열립니다."
                : Game.Event != null ? "이벤트 중에는 슬롯을 바꿀 수 없습니다." : "슬롯을 바꾸려면 주문을 고르고 아래 버튼을 누르세요. (주문을 고른 뒤 키보드 1-4도 됩니다)", styles.Label);
            bookScroll = Scroll(bookScroll);
            for (var i = 0; i < spells.Count; i++)
            {
                var spell = spells[i];
                var inSlots = Enumerable.Range(0, Magic.QuickSlotCount).Where(k => state.QuickSlots[k] == spell.Id).Select(k => (k + 1).ToString()).ToList();
                var lines = new List<string> { spell.Name + " · 룬 " + string.Join(" → ", spell.Runes) + (inSlots.Count > 0 ? " · 슬롯 " + string.Join(",", inSlots) : "")
                    + (spell.UnlocksQuickCast ? " · 빠른 시전 해금" : spell.AllowQuickCast ? "" : " · 영창 전용") };
                var needs = Game.Magic.Requirements(spell);
                lines.Add(needs.Count == 0 ? "사용 조건: 없음" : "사용 조건: " + string.Join(" / ", needs.Select(n => (n.Met ? "[충족] " : "[부족] ") + n.Text)));
                if (GUILayout.Toggle(selected == i, string.Join("\n", lines), styles.Button)) selected = i;
            }
            GUILayout.EndScrollView();
            var pick = spells.Count == 0 ? null : spells[selected];
            var pickable = editable && pick != null && pick.AllowQuickCast;
            GUILayout.BeginHorizontal();
            GUILayout.Label("선택한 주문을 넣기", styles.Label, GUILayout.ExpandWidth(false));
            GUI.enabled = pickable;
            for (var i = 0; i < Magic.QuickSlotCount; i++)
                if (GUILayout.Button("슬롯 " + (i + 1) + ": " + SlotName(i), styles.Button)) AssignQuickSlot(i, pick.Id);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("비우기", styles.Label, GUILayout.ExpandWidth(false));
            for (var i = 0; i < Magic.QuickSlotCount; i++)
            {
                GUI.enabled = editable && state.QuickSlots[i] != null;
                if (GUILayout.Button((i + 1).ToString(), styles.Button)) AssignQuickSlot(i, null);
            }
            GUI.enabled = true;
            if (GUILayout.Button("닫기", styles.Button)) panel = Panel.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        string SlotName(int slot) => Game.State.QuickSlots[slot] == null ? "비어 있음" : Game.Content.Spell(Game.State.QuickSlots[slot])?.Name ?? "?";

        void DrawError(Rect r)
        {
            GUI.Box(r, GUIContent.none, styles.Box);
            GUILayout.BeginArea(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height - 4));
            GUILayout.Label("오류", styles.Error);
            GUILayout.Label(Error, styles.Error);
            GUILayout.EndArea();
        }

        void DrawPanel(Rect r)
        {
            var rows = Rows();
            selected = Mathf.Clamp(selected, 0, Math.Max(0, rows.Count - 1));
            var status = rows.Select(id => Game.State.Appointments[id]).ToList();
            GUI.Box(r, GUIContent.none, styles.Box);
            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12));
            GUILayout.Label(panelTitle, styles.Bold);
            var offered = status.Count(x => x == Appointment.Offered);
            GUILayout.Label(rows.Count == 0 ? "알림: 예정된 약속이 없습니다." : "알림: 제안 " + offered + "건, 수락한 약속 " + (rows.Count - offered) + "건 · 선택 후 수락/시작하세요.", styles.Label);
            panelScroll = Scroll(panelScroll);
            for (var i = 0; i < rows.Count; i++)
                if (GUILayout.Toggle(selected == i, Label(rows[i]) + " [" + (status[i] == Appointment.Offered ? "제안됨" : "수락됨") + "]", styles.Button)) selected = i;
            GUILayout.EndScrollView();
            var pick = rows.Count == 0 ? null : rows[selected];
            var offeredPick = pick != null && status[selected] == Appointment.Offered;
            GUILayout.BeginHorizontal();
            GUI.enabled = offeredPick;
            if (GUILayout.Button("수락", styles.Button))
            {
                Game.Accept(pick);
                SetStatus("약속을 수락했습니다. 하루를 마칠 때 저장됩니다.");
            }
            GUI.enabled = pick != null && !offeredPick;
            if (GUILayout.Button("시작", styles.Button)) StartEvent(pick);
            GUI.enabled = pick != null;
            if (GUILayout.Button("수락 후 시작", styles.Button)) AcceptAndStart(pick);
            GUI.enabled = true;
            if (GUILayout.Button("닫기", styles.Button)) panel = Panel.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawSleep(float w, float h)
        {
            var mw = Mathf.Min(w - 16f, styles.HudSize * 18f);
            var mh = Mathf.Min(h - 16f, styles.HudSize * 8f);
            var r = new Rect((w - mw) / 2f, (h - mh) / 2f, mw, mh);
            GUI.Box(r, GUIContent.none, styles.Box);
            GUI.Box(r, GUIContent.none, styles.Box);
            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16));
            GUILayout.Label("잠자리에 들까요?", styles.Bold);
            GUILayout.Label("하루를 마치고 저장합니다. 다음 날 07:00에 여기서 일어납니다.", styles.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("잔다 (Enter)", styles.Button)) Sleep();
            if (GUILayout.Button("아니요 (Esc)", styles.Button)) panel = Panel.None;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawMenu(float w, float h)
        {
            var mw = Mathf.Min(w - 16f, styles.HudSize * 18f);
            var mh = Mathf.Min(h - 16f, styles.HudSize * 14f);
            var r = new Rect((w - mw) / 2f, (h - mh) / 2f, mw, mh);
            GUI.Box(r, GUIContent.none, styles.Box);
            GUI.Box(r, GUIContent.none, styles.Box);
            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16));
            menuScroll = Scroll(menuScroll);
            GUILayout.Label("메뉴 (게임 일시정지)", styles.Bold);
            if (GUILayout.Button("계속하기", styles.Button)) MenuOpen = false;
            if (GUILayout.Button("마지막 저장 불러오기 (F9)", styles.Button)) Reload();
            if (GUILayout.Button("글자 크기 1배", styles.Button)) TextScale = 1f;
            if (GUILayout.Button("글자 크기 2배", styles.Button)) TextScale = 2f;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
