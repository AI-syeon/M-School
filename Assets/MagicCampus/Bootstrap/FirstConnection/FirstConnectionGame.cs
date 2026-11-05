using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicCampus.Contracts;
using MagicCampus.Persistence;
using MagicCampus.Presentation.Dialogue;
using MagicCampus.State;
using MagicCampus.UnityAdapters.Input;
using MagicCampus.UnityAdapters.Maps;
using UnityEngine;

namespace MagicCampus.Bootstrap.FirstConnection
{
  public sealed class FirstConnectionGame : MonoBehaviour
  {
    enum Panel { None, Invite, Journal }

    [Serializable]
    sealed class EvidenceState
    {
      public string variant, phase, tokens, mapId, time, selectedEvent, status, lastError;
      public double playerX, playerY;
      public long revision;
      public bool unsaved, menuOpen;
      public string[] diagnostics, candidates;
    }

    const float MoveStep = 0.4f;

    string variant = "base", initialSave = "save-offered.json", saveDir, probeCase, evidenceDir;
    bool fresh, userConfigured, running;
    InputAdapter input;
    StreamWriter traceWriter;
    Camera cam;
    PauseToken menuToken;
    Panel panel;
    string inviteNpc, selectedEvent, errorText = string.Empty;
    readonly HashSet<string> inviteMatches = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string, StartCandidate> candidates = new Dictionary<string, StartCandidate>(StringComparer.Ordinal);
    IReadOnlyList<string> diags = new string[0];
    string lastDiag = string.Empty;
    float diagUntil, candidateAt, stateAt, retryAt, hudH, doorRetries;
    bool initialWritten, menuOpen;
    int insetKey = -1;

    // manual movement: 0 none, 1 WASD, 2 door
    int moveKind;
    bool hasDir;
    Vector2 moveDir;
    IHandle<MoveArrival> moveHandle;
    Position? doorTarget;

    Font font;
    int styleSize;
    GUIStyle label, bold, button, box, err;
    Vector2 hudScroll, panelScroll, errScroll, menuScroll;

    public FirstConnectionRuntime Runtime { get; private set; }
    public FirstConnectionVisuals Visuals { get; private set; }
    public DialoguePresentation Dialogue { get; private set; }
    public bool Initialized { get; private set; }
    public string InitializationError { get; private set; }
    public string StatusText { get; private set; } = string.Empty;
    public float TextScale { get; private set; } = 1f;
    public string ProbeCase { get { return probeCase; } }
    public string EvidenceDir { get { return evidenceDir; } }
    public string SelectedEvent { get { return selectedEvent; } }
    public bool MenuOpen { get { return menuOpen; } }
    public bool RuntimeReady { get { return Runtime != null && Runtime.Ready; } }
    public event Action<string> ObservedText;

    bool Captured { get { return menuOpen || panel != Panel.None || (Dialogue != null && Dialogue.UiCaptured); } }

    /// <summary>Optional; call before Start to bypass command line parsing.</summary>
    public void Configure(string variantName, string initialSaveName, string saveDirectory, bool freshStart, string probe, string evidence)
    {
      if (Initialized) throw new InvalidOperationException("already initialized");
      if (!string.IsNullOrEmpty(variantName)) variant = variantName;
      if (!string.IsNullOrEmpty(initialSaveName)) initialSave = initialSaveName;
      saveDir = saveDirectory; fresh = freshStart; probeCase = probe; evidenceDir = evidence;
      userConfigured = true;
    }

    void Start()
    {
      try { Initialize(); }
      catch (Exception e) { Fail("초기화 실패", e); }
      finally { AttachProbe(); }
    }

    void Initialize()
    {
      if (!userConfigured) ParseArgs();
      if (string.IsNullOrEmpty(saveDir)) saveDir = Path.Combine(UnityEngine.Application.persistentDataPath, "Stage5");
      OpenEvidence();
      Dialogue = new GameObject("Dialogue").AddComponent<DialoguePresentation>();
      Dialogue.transform.SetParent(transform, false);
      Dialogue.PollHardwareInput = true;
      Visuals = new GameObject("Visuals").AddComponent<FirstConnectionVisuals>();
      Visuals.transform.SetParent(transform, false);
      var art = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Stage5Art");
      Visuals.LoadArt(art);
      var config = new FirstConnectionRuntimeConfig
      {
        FixtureRoot = Path.Combine(UnityEngine.Application.streamingAssetsPath, "FirstConnection"),
        Variant = variant,
        InitialSave = initialSave,
        SaveDirectory = saveDir,
        DialogueUi = Dialogue,
        VisualResolver = Visuals,
        MapConfig = new MapAdapterConfig(Visuals.TileFor, null),
        Trace = WriteTrace,
        ResumeFromDisk = !fresh,
        PumpInStep = false,
        ValidateEveryCommit = probeCase != null
      };
      Runtime = new FirstConnectionRuntime(config);
      Dialogue.SpeakerDisplayName = id =>
      {
        var row = Runtime.Compiled.FindDefinition("npc", id);
        DataNode name;
        return row != null && row.TryGet("name", out name) && name.Kind == DataKind.String ? name.Text : id;
      };
      cam = Camera.main;
      if (cam == null) cam = FindAnyObjectByType<Camera>();
      if (cam == null)
      {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        cam = go.AddComponent<Camera>();
        cam.orthographic = true;
      }
      Visuals.Configure(Runtime.Actors, Runtime.Maps, Runtime.Compiled, art, cam);
      Runtime.HiddenChanged += Visuals.SetHidden;
      Visuals.SetHidden(Runtime.Hidden);
      Dialogue.SetCamera(cam);
      Dialogue.RegisterClip("wave", (visual, origin, t) =>
      {
        origin.ApplyTo(visual);
        visual.localRotation *= Quaternion.Euler(0, 0, Mathf.Sin(t * Mathf.PI * 4) * 8);
      });
      input = new InputAdapter(InputAdapterConfig.Default, () => Captured);
      Initialized = true;
      running = true;
      SetStatus("시작 중...", false);
    }

    void ParseArgs()
    {
      var a = Environment.GetCommandLineArgs();
      for (var i = 1; i < a.Length; i++)
      {
        var s = a[i];
        string inline = null;
        var eq = s.IndexOf('=');
        if (s.StartsWith("--", StringComparison.Ordinal) && eq > 0) { inline = s.Substring(eq + 1); s = s.Substring(0, eq); }
        switch (s)
        {
          case "--variant": variant = Val(a, ref i, inline) ?? variant; break;
          case "--initial": initialSave = Val(a, ref i, inline) ?? initialSave; break;
          case "--save-dir": saveDir = Val(a, ref i, inline); break;
          case "--evidence-dir": evidenceDir = Val(a, ref i, inline); break;
          case "--fresh": fresh = true; break;
          case "--probe": probeCase = Val(a, ref i, inline) ?? "default"; break;
        }
      }
    }

    static string Val(string[] a, ref int i, string inline)
    {
      if (inline != null) return inline;
      if (i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal)) return a[++i];
      return null;
    }

    void AttachProbe()
    {
      if (probeCase == null) return;
      gameObject.AddComponent<FirstConnectionProbe>();
    }

    // ------------------------------------------------------------------ frame

    void Update()
    {
      if (!running) return;
      try
      {
        var dt = Math.Min((double)Time.unscaledDeltaTime, 0.1);
        Runtime.Pump.AdvanceTick();
        var f = input.Poll(Runtime.Ticks.TickCount);
        HandleButtons(f);
        DriveManual(f);
        Dialogue.PollHardwareInput = !menuOpen;
        Visuals.Paused = Runtime.Pause.Effective.Scene;
        Runtime.Step(dt);
      }
      catch (Exception e) { running = false; Fail("프레임 오류", e); }
      PostFrame();
    }

    static bool Has(IReadOnlyList<string> list, string action)
    {
      for (var i = 0; i < list.Count; i++) if (list[i] == action) return true;
      return false;
    }

    void HandleButtons(InputFrame f)
    {
      var kb = UnityEngine.InputSystem.Keyboard.current;
      if (kb != null)
      {
        if (kb.f5Key.wasPressedThisFrame) RequestSave();
        if (kb.f9Key.wasPressedThisFrame) RequestReload();
      }
      if (Has(f.Pressed, MagicCampus.Contracts.InputActions.Menu)) SetMenu(!menuOpen);
      if (Has(f.Pressed, MagicCampus.Contracts.InputActions.Cancel))
      {
        if (panel != Panel.None) panel = Panel.None;
        else if (!menuOpen) Runtime.Cancel("cancel");
      }
      if (Has(f.Pressed, MagicCampus.Contracts.InputActions.Journal) && !Captured) OpenPanel(Panel.Journal, null);
      if (Has(f.Pressed, MagicCampus.Contracts.InputActions.Interact) && !Captured && Runtime.Ready && Runtime.ActiveEventCount == 0) TryInteract();
    }

    // ------------------------------------------------------------------ interaction

    static double Dist(Position a, Position b)
    {
      if (a.MapId != b.MapId) return double.PositiveInfinity;
      return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    static bool IsRangeName(string name)
    {
      var n = name.ToLowerInvariant();
      return n.Contains("dist") || n.Contains("range") || n.Contains("radius") || n.Contains("near") || n.Contains("within");
    }

    // Trigger args name an actor id and optionally a distance; both are read from the compiled call, never from an event id.
    bool TriggerMatch(CompiledEvent e, Position at, out string npc, out double dist)
    {
      npc = null; dist = double.MaxValue;
      var max = 2.0;
      foreach (var kv in e.Trigger.Args)
      {
        var n = kv.Value;
        if (n.Kind == DataKind.String && n.Text != FirstConnectionRuntime.PlayerId && Runtime.Actors.Find(n.Text) != null) npc = n.Text;
        else if ((n.Kind == DataKind.Integer || n.Kind == DataKind.Number) && IsRangeName(kv.Key)) max = n.Kind == DataKind.Integer ? n.Integer : n.Number;
      }
      if (npc == null) return false;
      dist = Dist(at, Runtime.Actors.Find(npc).Position);
      return dist <= max;
    }

    void TryInteract()
    {
      var player = Runtime.Actors.Find(FirstConnectionRuntime.PlayerId);
      if (player == null) return;
      string bestNpc = null;
      var best = double.MaxValue;
      foreach (var e in Runtime.Compiled.Events)
      {
        string npc; double d;
        if (TriggerMatch(e, player.Position, out npc, out d) && d < best) { best = d; bestNpc = npc; }
      }
      if (bestNpc != null)
      {
        inviteMatches.Clear();
        foreach (var e in Runtime.Compiled.Events)
        {
          string npc; double d;
          if (TriggerMatch(e, player.Position, out npc, out d) && npc == bestNpc) inviteMatches.Add(e.Id);
        }
        OpenPanel(Panel.Invite, bestNpc);
        return;
      }
      Portal door = null;
      var doorDist = 1.5;
      foreach (var p in Runtime.Maps.PortalsOn(Runtime.Maps.ActiveMapId))
      {
        var d = Dist(player.Position, p.From);
        if (p.Open && d <= doorDist) { door = p; doorDist = d; }
      }
      if (door != null)
      {
        doorTarget = door.Arrival; doorRetries = 0;
        SetStatus("문을 지나가는 중...", false);
        return;
      }
      SetStatus("근처에 상호작용할 대상이 없습니다.", false);
    }

    // ------------------------------------------------------------------ manual movement

    bool MoveActive { get { return moveHandle != null && moveHandle.Terminal == null; } }

    void CancelMove()
    {
      if (MoveActive) Runtime.CancelManualMove("input");
      moveKind = 0; hasDir = false;
    }

    void IssueMove(Position target, int kind)
    {
      var h = Runtime.ManualMove(target, 0.001);
      moveHandle = h; moveKind = kind;
      h.OnTerminal(r =>
      {
        if (r.Status == OperationStatus.Failed) retryAt = Time.unscaledTime + 0.15f;
      });
    }

    void DriveManual(InputFrame f)
    {
      var can = Runtime.Ready && !Captured && Runtime.ActiveEventCount == 0 && Runtime.PendingSaveCount == 0;
      if (!can)
      {
        if (moveKind == 1) CancelMove();
        if (!Runtime.Ready) doorTarget = null;
        if (!Runtime.Ready || Captured) { if (moveKind == 2) CancelMove(); doorTarget = null; }
        return;
      }
      var held = f.MoveX * f.MoveX + f.MoveY * f.MoveY > 1e-6;
      var player = Runtime.Actors.Find(FirstConnectionRuntime.PlayerId);
      if (player == null) return;
      if (!held)
      {
        if (moveKind == 1) CancelMove();
        hasDir = false;
        if (doorTarget.HasValue)
        {
          if (MoveActive && moveKind != 2) { Runtime.CancelManualMove("door"); return; }
          if (!MoveActive)
          {
            var t = doorTarget.Value;
            IssueMove(t, 2);
            if (moveHandle.Terminal != null && ++doorRetries < 30) return;
            doorTarget = null;
          }
        }
        return;
      }
      doorTarget = null;
      var dir = new Vector2((float)f.MoveX, (float)f.MoveY).normalized;
      if (MoveActive && (!hasDir || moveKind != 1 || Vector2.Dot(dir, moveDir) < 0.98f))
      {
        Runtime.CancelManualMove("direction");
        moveDir = dir; hasDir = true;
        return;
      }
      moveDir = dir; hasDir = true;
      if (MoveActive || Time.unscaledTime < retryAt) return;
      var p = player.Position;
      IssueMove(new Position(p.MapId, p.X + dir.x * MoveStep, p.Y + dir.y * MoveStep), 1);
    }

    // ------------------------------------------------------------------ public operations

    public void SetMenu(bool open)
    {
      if (open == menuOpen) return;
      menuOpen = open;
      if (open)
      {
        panel = Panel.None;
        if (Runtime != null) menuToken = Runtime.Pause.Acquire("game:menu", PauseSet.All);
      }
      else ReleaseMenuToken();
    }

    void ReleaseMenuToken()
    {
      if (menuToken != null && Runtime != null) Runtime.Pause.Release(menuToken);
      menuToken = null;
    }

    public void RequestSave()
    {
      if (!running) return;
      SetStatus("저장 중...", false);
      Runtime.RetrySave().OnTerminal(r =>
      {
        if (r.Status == OperationStatus.Succeeded) SetStatus("저장 완료", false);
        else SetStatus("저장 실패(메모리 상태 유지): " + Describe(r), true);
      });
    }

    public void RequestReload()
    {
      if (!running) return;
      panel = Panel.None; CancelMove(); doorTarget = null;
      SetStatus("불러오는 중...", false);
      Runtime.LoadLatest().OnTerminal(r =>
      {
        if (r.Status == OperationStatus.Succeeded) SetStatus("불러오기 완료", false);
        else SetStatus("불러오기 실패: " + Describe(r), true);
      });
    }

    /// <summary>Accepts (waiting for the physical save handle) and then starts; already accepted events start directly.</summary>
    public void AcceptAndStart(string eventId)
    {
      if (!running) { SetStatus("런타임이 실행 중이 아닙니다.", true); return; }
      selectedEvent = eventId;
      RefreshCandidates();
      StartCandidate c;
      if (!candidates.TryGetValue(eventId, out c)) { SetStatus("약속이 없습니다: " + eventId, true); return; }
      if (c.Status != ReservationStatus.Offered) { StartAccepted(eventId); return; }
      SetStatus("약속을 수락하고 저장하는 중...", false);
      Runtime.Accept(eventId).OnTerminal(r =>
      {
        if (r.Status == OperationStatus.Succeeded) StartAccepted(eventId);
        else SetStatus("수락/저장 실패: " + Describe(r), true);
      });
    }

    void AcceptOnly(string eventId)
    {
      SetStatus("약속을 수락하는 중...", false);
      Runtime.Accept(eventId).OnTerminal(r =>
      {
        if (r.Status == OperationStatus.Succeeded) SetStatus("약속을 수락하고 저장했습니다.", false);
        else SetStatus("수락/저장 실패: " + Describe(r), true);
      });
    }

    public void StartAccepted(string eventId)
    {
      if (!running) { SetStatus("런타임이 실행 중이 아닙니다.", true); return; }
      panel = Panel.None;
      SetStatus("약속 장소로 이동합니다.", false);
      Runtime.StartEvent(eventId).OnTerminal(r =>
      {
        if (r.Status == OperationStatus.Succeeded) SetStatus("함께한 시간을 기억했습니다. 저장 완료.", false);
        else SetStatus("이벤트 " + r.Status + ": " + Describe(r), r.Status != OperationStatus.Cancelled);
      });
    }

    void OpenPanel(Panel p, string npc)
    {
      panel = p; inviteNpc = npc;
      if (p == Panel.Journal) inviteMatches.Clear();
      RefreshCandidates();
      if (selectedEvent == null || !candidates.ContainsKey(selectedEvent))
      {
        selectedEvent = null;
        foreach (var e in Runtime.Compiled.Events)
          if (inviteMatches.Contains(e.Id) || (p == Panel.Journal && candidates.ContainsKey(e.Id))) { selectedEvent = e.Id; break; }
      }
    }

    void RefreshCandidates()
    {
      candidates.Clear();
      if (Runtime == null || !Runtime.Ready) return;
      foreach (var c in Runtime.Reservations.Candidates(Runtime.Store.Current))
        if (!candidates.ContainsKey(c.EventId)) candidates[c.EventId] = c;
      foreach (var row in Runtime.Store.Current.State.Appointments.Rows)
        if (row.Status == ReservationStatus.Offered && Runtime.Compiled.FindEvent(row.Id) != null)
          candidates[row.Id] = new StartCandidate(row.Id, row.Id, ReservationStatus.Offered, 0, row.AcceptedAt, 0);
    }

    // ------------------------------------------------------------------ bookkeeping

    void PostFrame()
    {
      if (Runtime == null) return;
      var now = Time.unscaledTime;
      if (now >= candidateAt) { candidateAt = now + 0.25f; RefreshCandidates(); RefreshDiagnostics(now); }
      if (!initialWritten && Runtime.Ready) { initialWritten = true; SetStatus("친구에게 다가가 E키로 말을 걸어 보세요.", false); WriteState("initial-state.json"); }
      if (now >= stateAt) { stateAt = now + 1f; WriteState("last-state.json"); }
    }

    void RefreshDiagnostics(float now)
    {
      diags = Runtime.Diagnostics;
      var last = diags.Count > 0 ? diags[diags.Count - 1] : string.Empty;
      if (last != lastDiag)
      {
        lastDiag = last;
        if (last.Length > 0) { Debug.LogError("[FirstConnection] " + last); diagUntil = now + 15f; }
      }
      var sb = new StringBuilder();
      if (InitializationError != null) sb.AppendLine(InitializationError);
      if (!string.IsNullOrEmpty(Runtime.LastError)) sb.AppendLine(Runtime.LastError);
      if (Runtime.Phase == RuntimePhase.Faulted || now < diagUntil)
        for (var i = Math.Max(0, diags.Count - 6); i < diags.Count; i++) sb.Append("· ").AppendLine(diags[i]);
      errorText = sb.ToString();
    }

    void SetStatus(string text, bool error)
    {
      StatusText = text;
      if (error) Debug.LogError("[FirstConnection] " + text);
      var handler = ObservedText;
      if (handler != null) { try { handler(text); } catch (Exception e) { Debug.LogException(e); } }
    }

    void Fail(string what, Exception e)
    {
      InitializationError = what + ": " + e.Message;
      errorText = InitializationError;
      Debug.LogError("[FirstConnection] " + what);
      Debug.LogException(e);
      SetStatus(InitializationError, false);
    }

    static string Describe(Result r)
    {
      var sb = new StringBuilder(r.Status + ":" + r.Code);
      foreach (var d in r.Details) sb.Append("; ").Append(d.Code).Append(' ').Append(d.Message);
      return sb.ToString();
    }

    // ------------------------------------------------------------------ evidence

    void OpenEvidence()
    {
      if (string.IsNullOrEmpty(evidenceDir)) return;
      try
      {
        Directory.CreateDirectory(evidenceDir);
        var fs = new FileStream(Path.Combine(evidenceDir, "trace.log"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        traceWriter = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
      }
      catch (Exception e) { Debug.LogError("evidence dir unusable: " + e.Message); }
    }

    void WriteTrace(string line)
    {
      if (traceWriter != null) traceWriter.WriteLine(line);
    }

    void WriteState(string fileName)
    {
      if (string.IsNullOrEmpty(evidenceDir) || Runtime == null) return;
      try
      {
        var player = Runtime.Actors.Find(FirstConnectionRuntime.PlayerId);
        var cand = new List<string>();
        foreach (var c in candidates.Values) cand.Add(c.EventId + ":" + c.Status);
        var t = Runtime.Now;
        var s = new EvidenceState
        {
          variant = variant,
          phase = Runtime.Phase.ToString(),
          tokens = Runtime.TokenReport(),
          mapId = Runtime.Maps.ActiveMapId,
          time = "D" + t.Day + " " + t.MinuteOfDay,
          selectedEvent = selectedEvent,
          status = StatusText,
          lastError = Runtime.LastError,
          playerX = player == null ? 0 : player.Position.X,
          playerY = player == null ? 0 : player.Position.Y,
          revision = Runtime.Store.Current.Revision,
          unsaved = Runtime.Unsaved,
          menuOpen = menuOpen,
          diagnostics = new List<string>(Runtime.Diagnostics).ToArray(),
          candidates = cand.ToArray()
        };
        File.WriteAllText(Path.Combine(evidenceDir, fileName), JsonUtility.ToJson(s, true), new UTF8Encoding(false));
      }
      catch (Exception e) { Debug.LogError("evidence write failed: " + e.Message); }
    }

    // ------------------------------------------------------------------ IMGUI

    void EnsureStyles()
    {
      var size = Mathf.Max(11, Mathf.RoundToInt(Screen.height / 40f * TextScale));
      if (label != null && size == styleSize) return;
      styleSize = size;
      if (font == null) font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", size);
      label = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, wordWrap = true };
      bold = new GUIStyle(label) { fontStyle = FontStyle.Bold };
      button = new GUIStyle(GUI.skin.button) { font = font, fontSize = size, wordWrap = true };
      box = new GUIStyle(GUI.skin.box);
      err = new GUIStyle(label);
      err.normal.textColor = new Color(1f, 0.45f, 0.4f);
    }

    static Vector2 Scroll(Vector2 s)
    {
      return GUILayout.BeginScrollView(s, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
    }

    void OnGUI()
    {
      if (!Initialized && InitializationError == null) return;
      EnsureStyles();
      var w = Screen.width;
      var h = Screen.height;
      var line = styleSize * 1.5f;
      hudH = Mathf.Min(h * 0.34f, line * 4.6f + 8f);
      var key = Mathf.RoundToInt(hudH) * 4 + (TextScale > 1.5f ? 1 : 0);
      if (Visuals != null && key != insetKey) { insetKey = key; Visuals.SetUiInsets(Mathf.CeilToInt(hudH + 8f), 0); }
      var bottom = h - 8f;
      if (errorText.Length > 0)
      {
        var eh = Mathf.Min(h * 0.3f, line * 5f);
        DrawErrors(new Rect(4, h - eh - 4, w - 8, eh));
        bottom = h - eh - 8f;
      }
      if (Runtime != null) DrawHud(w);
      if (panel != Panel.None && Runtime != null) DrawPanel(new Rect(4, hudH + 8f, w - 8, Mathf.Max(60f, bottom - hudH - 8f)));
      if (menuOpen) DrawMenu(w, h);
    }

    void DrawHud(float w)
    {
      var r = new Rect(4, 4, w - 8, hudH);
      GUI.Box(r, GUIContent.none, box);
      GUILayout.BeginArea(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height - 4));
      hudScroll = Scroll(hudScroll);
      GUILayout.Label("매직캠퍼스 · 첫 약속", bold);
      var t = Runtime.Now;
      var min = t.MinuteOfDay;
      GUILayout.Label((t.Day + 1) + "일 " + (min / 60).ToString("00") + ":" + (min % 60).ToString("00") + " · 소지금 " + Runtime.Store.Current.State.Progress.Wallet.Value.ToString() + " 크라운", label);
      GUILayout.BeginHorizontal();
      var prev = GUI.contentColor;
      var unsaved = Runtime.Unsaved;
      GUI.contentColor = unsaved ? new Color(1f, 0.7f, 0.2f) : new Color(0.5f, 1f, 0.5f);
      GUILayout.Label(unsaved ? "● 미저장" : "● 저장됨", label, GUILayout.ExpandWidth(false));
      GUI.contentColor = prev;
      if (unsaved && GUILayout.Button("저장 재시도", button, GUILayout.ExpandWidth(false))) RequestSave();
      if (GUILayout.Button("일지(J)", button, GUILayout.ExpandWidth(false))) OpenPanel(Panel.Journal, null);
      if (GUILayout.Button("메뉴(Esc)", button, GUILayout.ExpandWidth(false))) SetMenu(true);
      GUILayout.EndHorizontal();
      GUILayout.Label(StatusText, label);
      GUILayout.Label("WASD 이동 · E 상호작용 · J 일지 · X 취소 · F5 저장 · F9 불러오기", label);
      GUILayout.EndScrollView();
      GUILayout.EndArea();
    }

    void DrawErrors(Rect r)
    {
      GUI.Box(r, GUIContent.none, box);
      GUILayout.BeginArea(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height - 4));
      errScroll = Scroll(errScroll);
      GUILayout.Label("오류 / 진단", err);
      GUILayout.Label(errorText, err);
      if (Runtime != null && Runtime.Phase == RuntimePhase.Faulted && GUILayout.Button("맵 로드 재시도", button)) Runtime.RetryMapLoad();
      GUILayout.EndScrollView();
      GUILayout.EndArea();
    }

    static string StatusKo(ReservationStatus s)
    {
      switch (s)
      {
        case ReservationStatus.Offered: return "제안됨";
        case ReservationStatus.Accepted: return "수락됨";
        default: return "준비됨";
      }
    }

    static string EventLabel(CompiledEvent e)
    {
      DataNode triggerLabel;
      if (e.Trigger.Args.TryGetValue("label", out triggerLabel) && triggerLabel.Kind == DataKind.String) return triggerLabel.Text;
      foreach (var k in new[] { "title", "name", "label" })
      {
        DataNode n;
        if (e.Raw.TryGet(k, out n) && n.Kind == DataKind.String && n.Text.Length > 0) return n.Text;
      }
      return e.Id;
    }

    string Reminder()
    {
      int offered = 0, accepted = 0;
      foreach (var c in candidates.Values) { if (c.Status == ReservationStatus.Offered) offered++; else accepted++; }
      if (offered + accepted == 0) return "알림: 예정된 약속이 없습니다.";
      return "알림: 제안 " + offered + "건, 수락한 약속 " + accepted + "건 — 선택 후 수락/시작하세요.";
    }

    void DrawPanel(Rect r)
    {
      GUI.Box(r, GUIContent.none, box);
      GUILayout.BeginArea(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12));
      GUILayout.Label(panel == Panel.Invite ? "초대 · " + inviteNpc : "일지 · 약속", bold);
      GUILayout.Label(Reminder(), label);
      panelScroll = Scroll(panelScroll);
      foreach (var e in Runtime.Compiled.Events)
      {
        StartCandidate c;
        var has = candidates.TryGetValue(e.Id, out c);
        if (panel == Panel.Journal && !has) continue;
        var text = (inviteMatches.Contains(e.Id) ? "★ " : "") + EventLabel(e) + " [" + (has ? StatusKo(c.Status) : "불가") + "]";
        if (GUILayout.Toggle(selectedEvent == e.Id, text, button) && selectedEvent != e.Id) selectedEvent = e.Id;
      }
      GUILayout.EndScrollView();
      StartCandidate sel;
      var picked = selectedEvent != null && candidates.TryGetValue(selectedEvent, out sel);
      sel = null;
      if (picked) candidates.TryGetValue(selectedEvent, out sel);
      GUILayout.BeginHorizontal();
      GUI.enabled = sel != null && sel.Status == ReservationStatus.Offered;
      if (GUILayout.Button("수락", button)) AcceptOnly(selectedEvent);
      GUI.enabled = sel != null && sel.Status != ReservationStatus.Offered;
      if (GUILayout.Button("시작", button)) StartAccepted(selectedEvent);
      GUI.enabled = sel != null;
      if (GUILayout.Button("수락 후 시작", button)) AcceptAndStart(selectedEvent);
      GUI.enabled = true;
      if (GUILayout.Button("닫기", button)) panel = Panel.None;
      GUILayout.EndHorizontal();
      GUILayout.EndArea();
    }

    void DrawMenu(float w, float h)
    {
      var mw = Mathf.Min(w - 16f, styleSize * 18f);
      var mh = Mathf.Min(h - 16f, styleSize * 14f);
      var r = new Rect((w - mw) / 2f, (h - mh) / 2f, mw, mh);
      GUI.Box(r, GUIContent.none, box);
      GUI.Box(r, GUIContent.none, box);
      GUILayout.BeginArea(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16));
      menuScroll = Scroll(menuScroll);
      GUILayout.Label("메뉴 (게임 일시정지)", bold);
      if (GUILayout.Button("계속하기", button)) SetMenu(false);
      if (GUILayout.Button("저장 (F5)", button)) RequestSave();
      if (GUILayout.Button("불러오기 (F9)", button)) { SetMenu(false); RequestReload(); }
      if (GUILayout.Button("글자 크기 1배", button)) TextScale = 1f;
      if (GUILayout.Button("글자 크기 2배", button)) TextScale = 2f;
      GUILayout.EndScrollView();
      GUILayout.EndArea();
    }

    // ------------------------------------------------------------------ cleanup

    void OnApplicationQuit() { WriteState("last-state.json"); }

    void OnDestroy()
    {
      running = false;
      WriteState("last-state.json");
      ReleaseMenuToken();
      if (Runtime != null)
      {
        if (Visuals != null) Runtime.HiddenChanged -= Visuals.SetHidden;
        Runtime.Dispose();
      }
      if (Visuals != null) Visuals.Dispose();
      if (traceWriter != null) { traceWriter.Dispose(); traceWriter = null; }
    }
  }
}
