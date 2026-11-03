// Stage5 WindowsPlayer integration probe: drives the production graph through real services, Dialogue commands and a virtual Keyboard.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Content.Parsing;
using MagicCampus.Contracts;
using MagicCampus.Events;
using MagicCampus.Persistence;
using MagicCampus.Presentation.Dialogue;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.World.Movement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Tilemaps;

namespace MagicCampus.Bootstrap.FirstConnection
{
  public sealed class FirstConnectionProbe : MonoBehaviour
  {
    sealed class Chk { public string Name; public bool Pass; public string Detail; }

    const char QT = (char)34, BS = (char)92;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    FirstConnectionGame game;
    FirstConnectionRuntime Rt { get { return game.Runtime; } }
    DialoguePresentation Dlg { get { return game.Dialogue; } }

    readonly List<Chk> checks = new List<Chk>();
    readonly List<string> errors = new List<string>();
    readonly List<string> skipped = new List<string>();
    readonly List<string> branchTexts = new List<string>();
    string mode, evDir, ev, initialDump = "";
    int cost = 20;
    long inputSeq;
    Keyboard kb;
    PauseToken harness;
    object lastWindow;
    IHandle<EventCompletion> evHandle;

    // ------------------------------------------------------------------ entry

    IEnumerator Start()
    {
      game = GetComponent<FirstConnectionGame>();
      mode = Arg("probe") ?? "success-notes";
      evDir = Path.GetFullPath(Arg("evidence-dir") ?? Path.Combine(UnityEngine.Application.persistentDataPath, "probe-" + mode));
      Directory.CreateDirectory(evDir);
      yield return Guard(Main());
      if (errors.Count > 0) yield return Guard(Shot("failure"));
      WriteResult();
      UnityEngine.Application.Quit(Passed() ? 0 : 1);
    }

    IEnumerator Main()
    {
      Require(game != null, "FirstConnectionGame component on the same GameObject");
      Require(game.Runtime != null, "runtime composition exists before probe");
      Harness();
      yield return Until(() => game.Runtime != null && (game.Runtime.Ready || game.Runtime.Phase == RuntimePhase.Faulted), "Runtime.Ready");
      Require(Rt.Ready, "runtime ready: " + Rt.LastError);
      ev = Arg("event") ?? (mode == "a10" ? "event.mira_snack" : "event.snack");
      cost = mode == "a10" ? 30 : 20;
      initialDump = Dump(Rt.Store.Current.Root);
      Write("initial.json", initialDump);
      Check("codec.tree.enumerable", !initialDump.Contains("<unenumerable>"), "initial.json length " + initialDump.Length);
      Check("game.visuals-dialogue", game.Visuals != null && game.Dialogue != null, "promised public properties");
      kb = InputSystem.AddDevice<Keyboard>("ProbeKeyboard");
      Check("input.virtual-keyboard", kb != null, "virtual Keyboard added for early confirm and choice confirm");
      switch (mode)
      {
        case "success-notes": case "success-tea": case "moved": case "a10": yield return Ordinary(); break;
        case "cancel": yield return CancelCase(); break;
        case "map-fail": case "closed": case "blocked": yield return FailureCase(); break;
        case "disk-fail": yield return DiskFail(); break;
        case "intro-false": yield return IntroFalse(); break;
        case "validation": yield return Validation(); break;
        case "kill-move": case "kill-say": case "kill-choice": case "inspect-choice": yield return Checkpoint(); break;
        case "reload-entry": yield return ReloadEntry(); break;
        case "reload-success": yield return ReloadSuccess(); break;
        case "ui-long": case "ui-200": yield return UiCase(); break;
        default: throw new InvalidOperationException("unknown probe mode " + mode);
      }
    }

    // ------------------------------------------------------------------ cases

    IEnumerator Ordinary()
    {
      var hint = Arg("choice") ?? (mode == "success-tea" ? "tea" : mode == "a10" ? "props" : "notes");
      Harness();
      var w0 = Wallet(); var c0 = ClockMin(); var clock0 = Rt.Now.ToString();
      yield return AcceptAndSave();
      yield return DriveScene("", hint, null, false);
      yield return Settle(true);
      Check("event.succeeded", evHandle.Terminal.Status == OperationStatus.Succeeded, evHandle.Terminal.Status + ":" + evHandle.Terminal.Code);
      Check("wallet.plus-cost", w0.HasValue && Wallet() == w0 + cost, w0 + " -> " + Wallet() + " cost " + cost);
      CheckClock(c0, 10, clock0);
      Check("ledger.reward-once", Count("rewardLedger", ev) == 1, "count=" + Count("rewardLedger", ev));
      Check("ledger.completed", Count("completed", ev) >= 1, "count=" + Count("completed", ev));
      Check("appointment.removed", Count("appointments", ev) == 0, "count=" + Count("appointments", ev));
      Check("branch.text.observed", branchTexts.Count > 0, string.Join(" | ", branchTexts.ToArray()));
      var expectedBranch = FindEvent(ev).Forks[hint].Where(c => c.Function.Name == "say").Select(c => c.Args["text"].Text).ToArray();
      Check("branch.only-chosen", branchTexts.SequenceEqual(expectedBranch), "expected " + string.Join(" | ", expectedBranch) + "; actual " + string.Join(" | ", branchTexts));
      Check("snapshot.persisted", Rt.Policy.Latest != null && Rt.Policy.Latest.Saved && !Rt.Unsaved, "latest saved=" + (Rt.Policy.Latest != null && Rt.Policy.Latest.Saved));
      CheckTraceOrder();
      yield return Shot("final-world");
      CheckRender();
      var w1 = Wallet(); var c1 = ClockMin();
      evHandle = Rt.StartEvent(ev); Rec("StartEvent(retry)", "public Runtime command");
      yield return Until(() => evHandle.Terminal != null, "retry terminal");
      Check("retry.failed-no-reward", evHandle.Terminal.Status == OperationStatus.Failed && Wallet() == w1 && ClockMin() == c1, evHandle.Terminal.Status + ":" + evHandle.Terminal.Code);
      Leaks("final");
    }

    IEnumerator CancelCase()
    {
      Harness();
      var w0 = Wallet();
      yield return AcceptAndSave();
      var pre = ActorsPos(); var rev = Rt.Policy.Latest.Snapshot.Revision;
      yield return DriveScene("", "", null, true);
      yield return Settle(true);
      Check("cancel.terminal", evHandle.Terminal.Status == OperationStatus.Cancelled || evHandle.Terminal.Code == ResultCodes.Cancelled, evHandle.Terminal.Status + ":" + evHandle.Terminal.Code);
      Check("cancel.accepted-kept", Wallet() == w0 && Count("appointments", ev) >= 1 && Count("rewardLedger", ev) <= 0, "wallet " + w0 + " -> " + Wallet());
      Check("cancel.actors-restored", ActorsPos() == pre, pre + " vs " + ActorsPos());
      Check("cancel.boundary-renewed", Rt.Policy.Latest != null && Rt.Policy.Latest.Snapshot.Revision >= rev, "rev " + rev + " -> " + Rt.Policy.Latest.Snapshot.Revision);
      Leaks("cancel");
    }

    IEnumerator FailureCase()
    {
      Harness();
      var w0 = Wallet(); var c0 = ClockMin();
      yield return AcceptAndSave();
      var def = FindEvent(ev); string actor; Position target;
      Require(def != null && FirstMove(def, out actor, out target), "first move destination of " + ev);
      FirstMove(def, out actor, out target);
      var pre = ActorsPos(); var map0 = Rt.Maps.ActiveMapId;
      if (mode == "map-fail") Rt.FailMapId = target.MapId;
      else if (mode == "closed")
      {
        foreach (var m in Rt.Layout.MapIds.ToList())
          foreach (var p in Rt.Layout.PortalsOn(m).ToList())
            if (p.Open && p.Arrival.MapId == target.MapId) Rt.Layout.SetPortalOpen(p.PortalId, false);
      }
      else Rt.Layout.SetBlocked(target.MapId, (int)Math.Floor(target.X), (int)Math.Floor(target.Y), true);
      Log("armed " + mode + " target=" + target.MapId);
      evHandle = Rt.StartEvent(ev); Rec("StartEvent", "public Runtime command");
      yield return Until(() => evHandle.Terminal != null, "terminal after " + mode);
      yield return Settle(true);
      var st = evHandle.Terminal.Status;
      Check(mode + ".terminal", st == OperationStatus.Failed || st == OperationStatus.Cancelled, st + ":" + evHandle.Terminal.Code);
      Check(mode + ".no-effects", Wallet() == w0 && ClockMin() == c0 && Count("rewardLedger", ev) <= 0, "wallet " + w0 + " -> " + Wallet());
      Check(mode + ".entry-restored", Rt.Maps.ActiveMapId == map0 && ActorsPos() == pre, map0 + " / " + Rt.Maps.ActiveMapId);
      Rt.FailMapId = null;
      Leaks(mode);
    }

    IEnumerator DiskFail()
    {
      var hint = Arg("choice") ?? "notes";
      Harness();
      var w0 = Wallet();
      yield return AcceptAndSave();
      yield return DriveScene("", hint, () => { Rt.FailDisk = true; Log("FailDisk armed after entry save and Choose"); }, false);
      yield return Settle(false);
      yield return Until(() => Rt.PendingSaveCount == 0, "failed automatic save terminal callback");
      Check("disk-fail.memory-reward", Wallet() == w0 + cost && Rt.Unsaved, "wallet " + Wallet() + " unsaved=" + Rt.Unsaved);
      Rt.FailDisk = false;
      var rs = Rt.RetrySave(); Rec("RetrySave", "public Runtime command");
      yield return Until(() => rs.Terminal != null, "retry save");
      Check("disk-fail.retry-saved", rs.Terminal.Status == OperationStatus.Succeeded && !Rt.Unsaved, rs.Terminal.Status + ":" + rs.Terminal.Code);
      var ld = Rt.LoadLatest(); Rec("LoadLatest", "public Runtime command");
      yield return Until(() => ld.Terminal != null, "full reload");
      Check("disk-fail.reload-no-extra", ld.Terminal.Status == OperationStatus.Succeeded && Wallet() == w0 + cost && Count("rewardLedger", ev) == 1, "wallet " + Wallet() + " ledger " + Count("rewardLedger", ev));
      Leaks("disk-fail");
    }

    IEnumerator IntroFalse()
    {
      var before = Dump(Rt.Store.Current.Root); var cand = Rt.Disk.ListCandidates().Count;
      evHandle = Rt.StartEvent(ev); Rec("StartEvent(no accept)", "public Runtime command");
      yield return Until(() => evHandle.Terminal != null, "intro-false terminal");
      var t = evHandle.Terminal;
      var text = string.Join(" ", t.Details.Select(d => d.Code + " " + d.Message).ToArray());
      Check("intro-false.condition-false", t.Status == OperationStatus.Failed && t.Code == ResultCodes.ConditionFalse, t.Status + ":" + t.Code);
      Check("intro-false.diagnostics", text.IndexOf("false", StringComparison.OrdinalIgnoreCase) >= 0, text);
      Check("intro-false.state-unchanged", Dump(Rt.Store.Current.Root) == before && Rt.Disk.ListCandidates().Count == cand, "candidates " + cand);
      Check("intro-false.no-saves", Rt.PendingSaveCount == 0 && Rt.PendingDiskOperations == 0, Rt.TokenReport());
      Leaks("intro-false");
      yield break;
    }

    IEnumerator Validation()
    {
      var root = Path.Combine(UnityEngine.Application.streamingAssetsPath, "FirstConnection");
      Require(!string.IsNullOrEmpty(root), "fixture root (--fixture-root or game.FixtureRoot)");
      var baseDir = Path.Combine(root, "base");
      var compiler = new CatalogCompiler(new CallParser(), Rt.Schema);
      var ok = Compile(compiler, baseDir, baseDir);
      Check("validation.base-compiles", ok.Succeeded, ok.Succeeded ? "ok" : Describe(ok.Result));
      var vroot = Path.Combine(root, "variants", "invalid");
      var bad = Directory.GetDirectories(vroot).OrderBy(d => d, StringComparer.Ordinal).ToList();
      Check("validation.invalid-variants-found", bad.Count > 0, bad.Count + " invalid variant folders under " + vroot);
      foreach (var dir in bad)
      {
        var name = Path.GetFileName(dir);
        var o = Compile(compiler, baseDir, dir);
        var details = o.Result == null ? new Diagnostic[0] : o.Result.Details.ToArray();
        Write("validation-" + name + ".txt", Describe(o.Result) + "\n" + string.Join("\n", details.Select(Props).ToArray()));
        Check("validation.rejected:" + name, !o.Succeeded && details.Length > 0, Describe(o.Result));
        Check("validation.location:" + name, details.Any(d => d.Location != null && d.Location.Pointer.Contains("/events/") && d.Location.Offset >= 0 && d.Location.Call.Length > 0), string.Join(";",details.Select(Props)));
      }
      Leaks("validation");
      yield break;
    }

    IEnumerator Checkpoint()
    {
      Harness();
      var hint = Arg("choice") ?? "notes";
      yield return AcceptAndSave();
      Check("checkpoint.candidates", Rt.Disk.ListCandidates().Count > 0, "before staging");
      var stop = mode == "kill-move" ? "move" : mode == "kill-say" ? "say" : "choose";
      yield return DriveScene(stop, hint, null, false);
      Write("entry.json", Dump(Rt.Policy.Latest.Snapshot.Root));
      yield return Shot("checkpoint");
      if (mode != "inspect-choice")
      {
        Rt.Pause.Acquire("probe:kill-lock", PauseSet.All);
        Log("intentional harness lock PauseSet.All acquired at checkpoint; token0 is NOT claimed here");
      }
      yield return new WaitForEndOfFrame();
      SafeTrace();
      WriteAtomic("checkpoint.json", "{" + J("phase") + ":" + J(stop) + "," + J("mode") + ":" + J(mode) + "," + J("revision") + ":" + Rt.Store.Current.Revision + "," + J("harnessLock") + ":" + (mode != "inspect-choice" ? "true" : "false") + "," + J("pid") + ":" + System.Diagnostics.Process.GetCurrentProcess().Id + "," + J("report") + ":" + J(Rt.TokenReport()) + "}");
      Log("checkpoint written; waiting for external termination");
      while (true) yield return null;
    }

    IEnumerator ReloadEntry()
    {
      Harness();
      Check("reload.accepted", Count("appointments", ev) >= 1, "appointments count " + Count("appointments", ev));
      Check("reload.wallet200", Wallet() == 200, "wallet " + Wallet());
      Check("reload.not-completed", Count("completed", ev) <= 0 && Count("rewardLedger", ev) <= 0, "completed " + Count("completed", ev));
      Check("reload.actors-match-state", Mismatch() == 0, "mismatch " + Mismatch());
      var exp = Arg("expected-entry");
      if (!string.IsNullOrEmpty(exp))
      {
        var parsed = Rt.Codec.Parse(exp, File.ReadAllBytes(exp));
        Require(parsed.Succeeded, "expected-entry parse");
        var same = true;
        foreach (var s in new[] { "player", "npcPositions" })
        {
          DataNode e, c;
          if (!parsed.Value.Root.TryGet(s, out e) || !Rt.Store.Current.Root.TryGet(s, out c) || Dump(e) != Dump(c)) same = false;
        }
        Check("reload.matches-entry", same, exp);
      }
      else Skip("reload.matches-entry", "no --expected-entry");
      Check("reload.ui-closed", Kind() == DialogueWindowKind.None && Rt.PresentationLocks == 0, "kind " + Kind());
      Leaks("reload-entry");
      yield return Shot("reload-entry");
    }

    IEnumerator ReloadSuccess()
    {
      Harness();
      var w = Wallet(); var c = ClockMin();
      Check("reload.completed", Count("completed", ev) >= 1 && Count("rewardLedger", ev) == 1, "ledger " + Count("rewardLedger", ev));
      var exp = Arg("expected-wallet");
      if (exp != null) Check("reload.wallet-expected", w.ToString() == exp, w + " vs " + exp); else Skip("reload.wallet-expected", "no --expected-wallet");
      Check("reload.actors-restored", Mismatch() == 0, "mismatch " + Mismatch());
      var expectedFile = Arg("expected-entry");
      Require(!string.IsNullOrEmpty(expectedFile), "success reload requires previous process final.json");
      var expected = Rt.Codec.Parse(expectedFile, File.ReadAllBytes(expectedFile));
      Require(expected.Succeeded, "previous success state parses");
      foreach (var kv in expected.Value.Root.Members)
      {
        if (kv.Key == "generation" || kv.Key == "safePoint") continue;
        DataNode actual;
        Check("reload.state:" + kv.Key, Rt.Store.Current.Root.TryGet(kv.Key, out actual) && Dump(actual) == Dump(kv.Value), "persisted full slice " + kv.Key);
      }
      evHandle = Rt.StartEvent(ev); Rec("StartEvent(replay)", "public Runtime command");
      yield return Until(() => evHandle.Terminal != null, "replay terminal");
      Check("reload.replay-blocked", evHandle.Terminal.Status == OperationStatus.Failed && Wallet() == w && ClockMin() == c, evHandle.Terminal.Status + ":" + evHandle.Terminal.Code);
      Leaks("reload-success");
    }

    IEnumerator UiCase()
    {
      if (mode == "ui-200") Dlg.TextScale = 2.0f;
      var text = string.Concat(Enumerable.Repeat("마법 캠퍼스의 첫 연결 대화를 길게 확인하는 시험 문장입니다. ", 20));
      Check("ui.text-500+", text.Length >= 500, "chars " + text.Length);
      var h = CallSay(text);
      Rec("Dialogue.Say", "reflected public method");
      yield return Until(() => Kind() == DialogueWindowKind.Say, "Say window");
      yield return Frames(2);
      foreach (var size in new[] { new Vector2Int(640, 480), new Vector2Int(960, 640) })
      {
        Screen.SetResolution(size.x, size.y, false);
        yield return Frames(10);
        Check("ui.resolution " + size.x + "x" + size.y, Screen.width == size.x && Screen.height == size.y, Screen.width + "x" + Screen.height);
        yield return Shot(mode + "-" + size.x + "x" + size.y);
      }
      Dlg.CloseAll("probe cleanup");
      yield return Until(() => h == null || h.Terminal != null, "say handle terminal");
      yield return Frames(2);
      Check("ui.cleanup", Kind() == DialogueWindowKind.None && Rt.PresentationLocks == 0, Rt.TokenReport());
      Leaks("ui");
      Skip("ui.human-review", "explicit human review of the captured screenshots is still required");
    }

    // ------------------------------------------------------------------ scene driver

    IEnumerator AcceptAndSave()
    {
      var h = Rt.Accept(ev); Rec("Accept", "public Runtime command");
      yield return Until(() => h.Terminal != null, "accept physically saved");
      Check("accept.saved", h.Terminal.Status == OperationStatus.Succeeded && !Rt.Unsaved && Rt.Disk.ListCandidates().Count > 0, h.Terminal.Status + ":" + h.Terminal.Code + " candidates " + Rt.Disk.ListCandidates().Count);
      Require(h.Terminal.Status == OperationStatus.Succeeded, "accept must succeed");
    }

    // stopAt: "" runs to the terminal; "move"/"say"/"choose" returns once that phase is observed.
    IEnumerator DriveScene(string stopAt, string hint, Action atChoose, bool cancel)
    {
      var def = FindEvent(ev); string actor = null; Position target = default(Position);
      Require(def != null, "event in Compiled.Events: " + ev);
      var hasMove = FirstMove(def, out actor, out target);
      evHandle = Rt.StartEvent(ev); Rec("StartEvent", "public Runtime command");
      int terminalNotifications = 0;
      evHandle.OnTerminal(r => terminalNotifications++);
      yield return Until(() => Rt.ActiveMoveCount > 0 || Kind() != DialogueWindowKind.None || evHandle.Terminal != null, "scene move or window", 60, true);
      if (Rt.ActiveMoveCount > 0 && Kind() == DialogueWindowKind.None)
      {
        var leaseIds = def.Resources.Concat(new[] { "snapshot.world" }).Select(r => Rt.Leases.HolderOf(r)).ToArray();
        Check("A03.all-resources-held-before-walk", leaseIds.All(id => id != null) && leaseIds.Distinct().Count() == 1, string.Join(",", leaseIds));
        Check("A03.entry-saved-before-walk", Rt.Policy.Latest != null && Rt.Policy.Latest.Saved && Rt.Policy.Latest.Boundary == SafeBoundary.SceneEntry && Rt.Disk.ListCandidates().Count >= 2, "entry snapshot saved with physical generations=" + Rt.Disk.ListCandidates().Count);
        if (stopAt == "move") { yield return Until(() => Rt.Staging.ActiveCount == 0 && Rt.ActiveMoveCount > 0 && Rt.TraceLog.Any(l => l.Contains("phase=call call=" + ev + "#1")), "event move after staging", 60, true); yield break; }
        Rec("Confirm(Enter)", "virtual Keyboard while moving");
        yield return Press(Key.Enter);
        yield return Frames(2);
        Check("A05.early-confirm-ignored", Kind() == DialogueWindowKind.None && evHandle.Terminal == null, "kind " + Kind() + " moves " + Rt.ActiveMoveCount);
      }
      else Skip("A05.early-confirm-ignored", "move finished before it was observed");
      yield return NextWindow(DialogueWindowKind.Say, "first Say");
      if (hasMove)
      {
        var a = Rt.Actors.Find(actor);
        var d = a == null ? 999.0 : Math.Sqrt(Math.Pow(a.Position.X - target.X, 2) + Math.Pow(a.Position.Y - target.Y, 2));
        Check("scene.actor-at-first-destination", a != null && a.Position.MapId == target.MapId && d < 0.35, actor + " dist " + d.ToString("F3", Inv) + " on " + (a == null ? "-" : a.Position.MapId) + " want " + target.MapId);
      }
      if (stopAt == "say") yield break;
      Rec("Dialogue.Confirm", "public command"); Dlg.Confirm();
      yield return NextWindow(DialogueWindowKind.Choose, "Choose");
      if (atChoose != null) atChoose();
      if (stopAt == "choose") yield break;
      if (cancel)
      {
        Rec("Dialogue.Cancel", "public command"); Dlg.Cancel();
        Rec("Runtime.Cancel(duplicate)", "public command"); Rt.Cancel("duplicate cancel in same frame");
        yield return Frames(2);
        Rec("Runtime.Cancel(2)", "public command"); Rt.Cancel("duplicate cancel");
        yield return Until(() => evHandle.Terminal != null, "cancel terminal", 60);
        yield return Frames(2);
        Check("cancel.terminal-once", terminalNotifications == 1, "notifications " + terminalNotifications);
        yield break;
      }
      var opts = Dlg.CurrentOptions;
      var idx = -1;
      for (var i = 0; opts != null && i < opts.Length; i++) if (opts[i].Key.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) { idx = i; break; }
      Check("choice.target-found", idx >= 0, "hint '" + hint + "' in [" + string.Join(",", (opts ?? new ChoiceOption[0]).Select(o => o.Key).ToArray()) + "]");
      if (idx < 0) idx = 0;
      for (var n = 0; opts != null && n < opts.Length && Dlg.FocusIndex != idx; n++) { Rec("Dialogue.Navigate(Down)", "public command"); Dlg.Navigate(NavDirection.Down); yield return null; }
      yield return Shot("choose");
      var win = Win();
      Rec("Confirm(Enter)", "virtual Keyboard on choice");
      yield return Press(Key.Enter);
      yield return Frames(2);
      Require(!ReferenceEquals(Win(), win), "virtual keyboard confirmation must close the choice without fallback");
      var until = Time.realtimeSinceStartup + 60;
      while (evHandle.Terminal == null)
      {
        if (Time.realtimeSinceStartup > until) throw new TimeoutException("branch did not finish | " + Rt.TokenReport());
        var w = Win();
        if (w != null && !ReferenceEquals(w, lastWindow) && Kind() == DialogueWindowKind.Say)
        {
          lastWindow = w;
          yield return Frames(2);
          branchTexts.Add(Dlg.CurrentText);
          if (branchTexts.Count == 1) yield return Shot("branch");
          Rec("Dialogue.Confirm", "public command"); Dlg.Confirm();
        }
        yield return null;
      }
    }

    IEnumerator NextWindow(DialogueWindowKind k, string what)
    {
      yield return Until(() => { var w = Win(); return w != null && !ReferenceEquals(w, lastWindow) && Kind() == k; }, what, 60, true);
      lastWindow = Win();
      yield return Frames(2);
    }

    IEnumerator Settle(bool includeSaves)
    {
      yield return Until(() => Rt.ActiveEventCount == 0 && (!includeSaves || Rt.PendingSaveCount == 0) && Rt.PendingDiskOperations == 0 && Rt.ActiveMoveCount == 0 && Rt.Staging.ActiveCount == 0 && Kind() == DialogueWindowKind.None, "cleanup settle");
    }

    // ------------------------------------------------------------------ bounded real-time helpers

    IEnumerator Until(Func<bool> cond, string what, double seconds = 60, bool watchEvent = false)
    {
      var t0 = Time.realtimeSinceStartup;
      while (!cond())
      {
        if (Time.realtimeSinceStartup - t0 > Math.Min(60.0, seconds)) throw new TimeoutException("timeout waiting for " + what + " | " + Rt.TokenReport());
        if (watchEvent && evHandle != null && evHandle.Terminal != null && !cond()) throw new InvalidOperationException("event ended (" + evHandle.Terminal.Status + ":" + evHandle.Terminal.Code + ") while waiting for " + what);
        yield return null;
      }
    }

    IEnumerator Frames(int n)
    {
      var s = Time.frameCount;
      while (Time.frameCount < s + n) yield return null;
    }

    // QueueStateEvent, one frame down, one frame up; no InputSystem.Update inside the engine update.
    IEnumerator Press(Key key)
    {
      InputSystem.QueueStateEvent(kb, new KeyboardState(key));
      yield return null;
      InputSystem.QueueStateEvent(kb, new KeyboardState());
      yield return null;
    }

    IEnumerator Shot(string name)
    {
      yield return new WaitForEndOfFrame();
      var p = Path.Combine(evDir, name + ".png");
      ScreenCapture.CaptureScreenshot(p);
      yield return null;
      yield return Until(() => File.Exists(p) && new FileInfo(p).Length > 0, "screenshot " + name, 15);
      yield return null;
    }

    // Flattens nested helpers so MoveNext runs inside try/catch while yields stay outside it.
    IEnumerator Guard(IEnumerator root)
    {
      var stack = new Stack<IEnumerator>();
      stack.Push(root);
      while (stack.Count > 0)
      {
        object cur = null; var more = false;
        try { var top = stack.Peek(); more = top.MoveNext(); if (more) cur = top.Current; }
        catch (Exception e) { errors.Add(e.ToString()); Log("EXCEPTION " + e); yield break; }
        if (!more) { stack.Pop(); continue; }
        var nested = cur as IEnumerator;
        if (nested != null) { stack.Push(nested); continue; }
        yield return cur;
      }
    }

    // ------------------------------------------------------------------ state readers

    object Win() { return Dlg.WindowIdentity; }

    DialogueWindowKind Kind() { return Dlg.ActiveWindow; }

    long? Wallet()
    {
      var n = Rt.Store.Current.Slice("wallet");
      if (n == null) { DataNode r; if (!Rt.Store.Current.Root.TryGet("wallet", out r)) return null; n = r; }
      if (n.Kind == DataKind.Integer) return n.Integer;
      foreach (var kv in Pairs(n)) if (kv.Value != null && kv.Value.Kind == DataKind.Integer) return kv.Value.Integer;
      return null;
    }

    long? ClockMin()
    {
      return Rt.Now.AbsoluteMinute;
    }

    void CheckClock(long? c0, int delta, string text0)
    {
      var c1 = ClockMin();
      if (c0.HasValue && c1.HasValue) Check("clock.plus-" + delta, c1 == c0 + delta, c0 + " -> " + c1);
      else Check("clock.plus-" + delta, false, "GameTime exposes no numeric minutes accessor: " + text0 + " -> " + Rt.Now);
    }

    int Count(string slice, string id)
    {
      var n = Rt.Store.Current.Slice(slice);
      if (n == null) return -1;
      return n.Items.Count(row => row.Kind == DataKind.String ? row.Text == id : row.Kind == DataKind.Object && row.Members.Values.Any(v => v.Kind == DataKind.String && v.Text == id));
    }

    string ActorsPos()
    {
      var ids = new List<string> { FirstConnectionRuntime.PlayerId };
      var def = FindEvent(ev);
      if (def != null)
        foreach (var c in def.Script)
        {
          DataNode a;
          if (c.Function.Role == FunctionRole.Command && c.Args.TryGetValue("actor", out a) && a.Kind == DataKind.String && !ids.Contains(a.Text)) ids.Add(a.Text);
        }
      return string.Join("|", ids.Select(id => { var a = Rt.Actors.Find(id); return id + "=" + (a == null ? "null" : a.Position.MapId + ":" + a.Position.X.ToString("F3", Inv) + ":" + a.Position.Y.ToString("F3", Inv)); }).ToArray());
    }

    int Mismatch()
    {
      var bad = Differs(FirstConnectionRuntime.PlayerId, Rt.Store.Current.Slice("player"));
      var np = Rt.Store.Current.Slice("npcPositions");
      if (np != null && np.Kind == DataKind.List)
        foreach (var e in np.Items) { DataNode id; if (e.Kind == DataKind.Object && e.TryGet("npcId", out id)) bad += Differs(id.Text, e); }
      return bad;
    }

    int Differs(string id, DataNode p)
    {
      var a = Rt.Actors.Find(id); DataNode m, x, y;
      if (a == null && id != FirstConnectionRuntime.PlayerId) return 0;
      if (a == null || p == null || !p.TryGet("mapId", out m) || !p.TryGet("x", out x) || !p.TryGet("y", out y)) return 1;
      return a.Position.MapId == m.Text && Math.Abs(a.Position.X - Num(x)) < 1e-3 && Math.Abs(a.Position.Y - Num(y)) < 1e-3 ? 0 : 1;
    }

    static double Num(DataNode n) { return n.Kind == DataKind.Integer ? n.Integer : n.Number; }

    CompiledEvent FindEvent(string id)
    {
      foreach (var e in Rt.Compiled.Events) if (e.Id == id) return e;
      return null;
    }

    bool FirstMove(CompiledEvent def, out string actor, out Position target)
    {
      actor = null; target = default(Position);
      foreach (var c in def.Script)
      {
        if (c.Function.Role != FunctionRole.Command || c.Function.Name != "move") continue;
        DataNode a, d;
        if (!c.Args.TryGetValue("actor", out a) || !c.Args.TryGetValue("destination", out d)) return false;
        var r = Rt.Destinations.Resolve(a.Text, d.Text);
        if (!r.Succeeded) return false;
        actor = a.Text; target = r.Value.Target;
        return true;
      }
      return false;
    }

    // ------------------------------------------------------------------ checks and evidence

    void Harness()
    {
      if (harness != null) return;
      harness = Rt.Pause.Acquire("probe:harness", new PauseSet(true, true, false, false, false));
      Check("harness.policy", harness != null, "owner probe:harness PauseSet(true,true,false,false,false): ordinary calendar/ambient frozen, Scene channel free; released before leak assertions");
    }

    void Leaks(string tag)
    {
      if (harness != null) { Rt.Pause.Release(harness); harness = null; }
      Check(tag + ".leaks0", Rt.ActiveLeaseCount == 0 && Rt.PauseTokenCount == 0 && Rt.LiveHandleCount == 0 && Rt.PresentationLocks == 0 && Rt.PendingDiskOperations == 0 && Rt.ActiveMoveCount == 0, Rt.TokenReport());
    }

    void CheckTraceOrder()
    {
      var log = Rt.TraceLog.ToList();
      var saved = log.FindIndex(l => l.Contains("accept.saved"));
      var stage = log.FindIndex(l => l.IndexOf("stag", StringComparison.OrdinalIgnoreCase) >= 0 && !l.Contains("accept."));
      Check("trace.entry-save-before-stage", saved >= 0 && stage > saved, "accept.saved line " + saved + " first stage line " + stage);
    }

    void CheckRender()
    {
      var tilemaps = FindObjectsByType<Tilemap>();
      Check("render.tilemaps-present", tilemaps.Length > 0, tilemaps.Length + " Tilemap components");
      var cam = Camera.main;
      var ppu = cam != null && cam.orthographic ? cam.pixelHeight / (2.0 * cam.orthographicSize) : 0.0;
      Check("render.ppu-16-multiple", ppu >= 16 && Math.Abs(ppu - Math.Round(ppu)) < 1e-3 && ((long)Math.Round(ppu)) % 16 == 0, "PixelsPerUnit " + ppu.ToString("F3", Inv));
      Check("render.tilemap-collider-mismatch0", Rt.Maps.FindColliderMismatches().Count == 0, string.Join(";", Rt.Maps.FindColliderMismatches()));
    }

    IHandle CallSay(string text)
    {
      return Dlg.Say("안내", text, "probe:ui");
    }

    string EvPath(string name)
{
    Directory.CreateDirectory(evDir);
    return Path.Combine(evDir, name);
}

ContentBundle Bundle(string path)
{
    var o = Rt.Codec.Parse(path, File.ReadAllBytes(path));
    if (!o.Succeeded) throw new InvalidOperationException("Parse failed: " + path + Environment.NewLine + Describe(o.Result));
    return o.Value;
}

Outcome<ICatalog> Compile(CatalogCompiler c, string baseDir, string variantDir) =>
    c.Compile(
        Bundle(Path.Combine(baseDir, "full-catalog.json")),
        Bundle(Path.Combine(variantDir, "runner-bundle.json")),
        Bundle(Path.Combine(baseDir, "schemas", "functions.json")),
        Bundle(Path.Combine(baseDir, "schemas", "catalog.schema.json")),
        Bundle(Path.Combine(baseDir, "schemas", "runner.schema.json")));

string Describe(Result r)
{
    if (object.ReferenceEquals(r, null)) return "Result: null";
    var sb = new StringBuilder("Status=" + r.Status + " Code=" + r.Code);
    if (r.Details != null)
        foreach (Diagnostic d in r.Details) sb.AppendLine().Append("  - ").Append(Props(d));
    return sb.ToString();
}

string Props(Diagnostic d)
{
    if (object.ReferenceEquals(d, null)) return "null";
    var l = d.Location;
    return "code=" + d.Code + " message=" + d.Message + " file=" + l?.File + " pointer=" + l?.Pointer
        + " call=" + l?.Call + " offset=" + l?.Offset + " expected=" + d.Expected + " actual=" + d.Actual;
}

static string Arg(string name)
{
    var a = Environment.GetCommandLineArgs();
    var flag = "--" + name;
    for (int i = 1; i < a.Length; i++)
    {
        if (a[i] == flag) return i + 1 < a.Length ? a[i + 1] : null;
        if (a[i].StartsWith(flag + "=", StringComparison.Ordinal)) return a[i].Substring(flag.Length + 1);
    }
    return null;
}

void Log(string s)
{
    try { File.AppendAllText(EvPath("probe.log"), s + Environment.NewLine, new UTF8Encoding(false)); }
    catch (Exception) { }
}

void Rec(string action, string via)
{
    try
    {
        var line = "{" + Q("tick") + ":" + Convert.ToString(Rt.Ticks.TickCount, Inv)
            + "," + Q("seq") + ":" + (++inputSeq).ToString(Inv)
            + "," + Q("revision") + ":" + Convert.ToString(Rt.Store.Current.Revision, Inv)
            + "," + Q("action") + ":" + Q(action)
            + "," + Q("via") + ":" + Q(via) + "}";
        File.AppendAllText(EvPath("input.jsonl"), line + Environment.NewLine, new UTF8Encoding(false));
    }
    catch (Exception e) { Log("Rec failed: " + e.Message); }
}

void Write(string name, string text)
{
    File.WriteAllText(EvPath(name), text ?? "", new UTF8Encoding(false));
}

void WriteAtomic(string name, string text)
{
    var target = EvPath(name);
    var tmp = target + ".tmp";
    File.WriteAllText(tmp, text ?? "", new UTF8Encoding(false));
    if (File.Exists(target)) File.Replace(tmp, target, null);
    else File.Move(tmp, target);
}

void Check(string name, bool pass, string detail)
{
    checks.Add(new Chk { Name = name, Pass = pass, Detail = detail });
    if (!pass) Log("CHECK FAIL " + name + ": " + detail);
}

void Require(bool pass, string detail)
{
    if (!pass) throw new InvalidOperationException(detail);
}

void Skip(string name, string detail)
{
    skipped.Add(name + ": " + detail);
}

bool Passed() => errors.Count == 0 && checks.All(c => c.Pass);

static object Member(object obj, string name)
{
    if (obj == null) return null;
    const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    var t = obj.GetType();
    var p = t.GetProperty(name, f);
    if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(obj);
    var fi = t.GetField(name, f);
    return fi?.GetValue(obj);
}

// Escapes only; no surrounding quotes. Use Q for a quoted JSON string.
static string EscapeJson(string s)
{
    if (s == null) return "";
    var sb = new StringBuilder(s.Length + 8);
    var bs = (char)92;
    foreach (var ch in s)
    {
        if (ch == (char)34 || ch == bs) sb.Append(bs).Append(ch);
        else if (ch == (char)10) sb.Append(bs).Append('n');
        else if (ch == (char)13) sb.Append(bs).Append('r');
        else if (ch == (char)9) sb.Append(bs).Append('t');
        else if (ch < 32) sb.Append(bs).Append('u').Append(((int)ch).ToString("x4", Inv));
        else sb.Append(ch);
    }
    return sb.ToString();
}

static string Q(string s) => s == null ? "null" : (char)34 + EscapeJson(s) + (char)34;

static IEnumerable<KeyValuePair<string, DataNode>> Pairs(DataNode n) => n.Members;

static string Dump(DataNode n)
{
    var sb = new StringBuilder();
    DumpTo(sb, n);
    return sb.ToString();
}

static void DumpTo(StringBuilder sb, DataNode n)
{
    if (object.ReferenceEquals(n, null)) { sb.Append("null"); return; }
    switch (n.Kind)
    {
        case DataKind.Null: sb.Append("null"); break;
        case DataKind.Bool: sb.Append(n.Bool ? "true" : "false"); break;
        case DataKind.Integer: sb.Append(n.Integer.ToString(Inv)); break;
        case DataKind.Number:
            var d = n.Number;
            sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", Inv));
            break;
        case DataKind.String: sb.Append(Q(n.Text)); break;
        case DataKind.List:
            sb.Append('[');
            for (int i = 0; i < n.Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                DumpTo(sb, n.Items[i]);
            }
            sb.Append(']');
            break;
        case DataKind.Object:
            sb.Append('{');
            bool first = true;
            foreach (var kv in Pairs(n).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Q(kv.Key)).Append(':');
                DumpTo(sb, kv.Value);
            }
            sb.Append('}');
            break;
        default:
            throw new InvalidOperationException("Unknown DataNode kind: " + n.Kind);
    }
}

void SafeTrace()
{
    try
    {
        var t = Rt?.TraceLog;
        Write("trace-probe.log", t == null ? "" : string.Join(Environment.NewLine, t));
    }
    catch (Exception e) { Log("SafeTrace failed: " + e.Message); }
}

void WriteResult()
{
    try { if (Rt != null) WriteAtomic("final.json", Dump(Rt.Store.Current.Root)); }
    catch (Exception e) { errors.Add("final.json: " + e.Message); }

    object hash = null, tokens = null;
    try { hash = Rt?.Compiled?.CatalogHash; } catch (Exception x) { errors.Add("catalogHash: " + x.Message); }
    try { tokens = Rt?.TokenReport(); } catch (Exception x) { errors.Add("tokens: " + x.Message); }
    string Oq(object o) => o == null ? "null" : Q(Convert.ToString(o, Inv));

    var sb = new StringBuilder();
    sb.Append('{').Append(Q("mode")).Append(':').Append(Q(mode));
    sb.Append(',').Append(Q("pass")).Append(':').Append(Passed() ? "true" : "false");
    sb.Append(',').Append(Q("checks")).Append(":[").Append(string.Join(",", checks.Select(c =>
        "{" + Q("name") + ":" + Q(c.Name) + "," + Q("pass") + ":" + (c.Pass ? "true" : "false") + "," + Q("detail") + ":" + Q(c.Detail) + "}"))).Append(']');
    sb.Append(',').Append(Q("errors")).Append(":[").Append(string.Join(",", errors.Select(s => Q(s)))).Append(']');
    sb.Append(',').Append(Q("skipped")).Append(":[").Append(string.Join(",", skipped.Select(s => Q(s)))).Append(']');
    sb.Append(',').Append(Q("unity")).Append(':').Append(Q(UnityEngine.Application.unityVersion));
    sb.Append(',').Append(Q("platform")).Append(':').Append(Q(UnityEngine.Application.platform.ToString()));
    sb.Append(',').Append(Q("catalogHash")).Append(':').Append(Oq(hash));
    sb.Append(',').Append(Q("saveVersion")).Append(":2");
    sb.Append(',').Append(Q("tokens")).Append(':').Append(Oq(tokens));
    sb.Append('}');
    try { WriteAtomic("result.json", sb.ToString()); }
    catch (Exception e) { Log("result.json failed: " + e.Message); }
}

static string J(string s) => Q(s);

  }
}
