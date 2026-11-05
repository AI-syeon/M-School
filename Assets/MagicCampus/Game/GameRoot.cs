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
        enum Panel { None, Invite, Journal }

        string variant = "base", initial = "start-offered.json", saveDir, contentDir, fixture;
        bool fresh;
        Panel panel;
        string panelTitle;
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
        public string Error { get; private set; }
        public bool MenuOpen { get; private set; }
        public bool PanelOpen => panel != Panel.None;
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
            if (Pressed(Key.Escape))
            {
                if (MenuOpen) MenuOpen = false;
                else if (panel != Panel.None) panel = Panel.None;
                else if (Game.Event != null) CancelEvent();
                else MenuOpen = true;
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
            if (panel != Panel.None)
            {
                if (Pressed(Key.UpArrow) || Pressed(Key.W)) selected = Math.Max(0, selected - 1);
                if (Pressed(Key.DownArrow) || Pressed(Key.S)) selected++;
                if ((Pressed(Key.Enter) || Pressed(Key.Space)) && Rows().Count > 0) AcceptAndStart(Rows()[Math.Min(selected, Rows().Count - 1)]);
                return;
            }
            if (Game.Event != null) return;
            if (Pressed(Key.E)) Interact();
            if (Pressed(Key.J) || Pressed(Key.Tab)) OpenPanel(Panel.Journal, "일지 · 약속", Game.State.Appointments.Keys.ToList());
        }

        static bool Pressed(Key key) => InputSystem.devices.OfType<Keyboard>().Any(k => k[key].wasPressedThisFrame);

        static float Axis(Key a, Key b) => InputSystem.devices.OfType<Keyboard>().Any(k => k[a].isPressed || k[b].isPressed) ? 1f : 0f;

        void Interact()
        {
            var invites = Game.Interact();
            if (invites.Count == 0) SetStatus("근처에 상호작용할 대상이 없습니다.");
            else OpenPanel(Panel.Invite, "초대 · " + Name(invites[0].NpcId), invites.Select(i => i.EventId).ToList());
        }

        void OpenPanel(Panel kind, string title, List<string> events)
        {
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

        /// <summary>The menu's "하루 마치기": the next day starts at 07:00 and the game saves.</summary>
        public void EndDay()
        {
            Game.EndDay();
            MenuOpen = false;
            WatchProgress();
        }

        /// <summary>F9: a new Core.Game from the last save, or from the start fixture when nothing was saved yet.</summary>
        public void Reload()
        {
            var had = File.Exists(SavePath);
            Game = Core.Game.Open(contentDir, fixture, SavePath);
            panel = Panel.None;
            MenuOpen = false;
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
            SetStatus((watchedDay + 1) + "일 아침입니다. 하루를 저장했습니다.");
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
                appointments = Game.State.Appointments, completed = Game.State.Completed
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
            var hudH = Mathf.Min(h * 0.34f, styles.HudSize * 1.5f * 4.6f + 8f);
            if (View != null) View.TopInset = Mathf.CeilToInt(hudH + 8f);
            if (Game != null) DrawHud(new Rect(4, 4, w - 8, hudH));
            if (Error != null) DrawError(new Rect(4, h * 0.7f - 4, w - 8, h * 0.3f));
            if (Game == null) return;
            if (panel != Panel.None) DrawPanel(new Rect(4, hudH + 8f, w - 8, Mathf.Max(60f, h - hudH - 16f)));
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
            GUILayout.Label((s.Day + 1) + "일 " + (s.Minute / 60).ToString("00") + ":" + (s.Minute % 60).ToString("00") + " · 소지금 " + s.Wallet + " 크라운", styles.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("일지(J)", styles.Button, GUILayout.ExpandWidth(false))) OpenPanel(Panel.Journal, "일지 · 약속", s.Appointments.Keys.ToList());
            if (GUILayout.Button("메뉴(Esc)", styles.Button, GUILayout.ExpandWidth(false))) MenuOpen = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(Status, styles.Label);
            GUILayout.Label("WASD 이동 · E 상호작용 · J 일지 · X 취소 · Esc 메뉴 · F9 마지막 저장 불러오기", styles.Label);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

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
            GUI.enabled = Game.Event == null;
            if (GUILayout.Button("하루 마치기 (저장)", styles.Button)) EndDay();
            GUI.enabled = true;
            if (GUILayout.Button("마지막 저장 불러오기 (F9)", styles.Button)) Reload();
            if (GUILayout.Button("글자 크기 1배", styles.Button)) TextScale = 1f;
            if (GUILayout.Button("글자 크기 2배", styles.Button)) TextScale = 2f;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
