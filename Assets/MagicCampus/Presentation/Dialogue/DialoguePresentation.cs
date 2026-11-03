using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using MagicCampus.Presentation.Cutscene;
using UnityEngine;

namespace MagicCampus.Presentation.Dialogue
{
    /// <summary>
    /// Reusable dialogue, choice and cutscene display adapter (task 3-4). Display only: it owns no branching, no save
    /// state and no rewards. Windows and tweens are temporary and owner scoped.
    ///
    /// Time: tweens advance by the unpaused delta passed to Tick, gated by IPauseService.Effective.Scene. A paused
    /// Calendar therefore does not stop scene-owned camera or clips, while a Menu pause (Scene true) does. UI input
    /// polling and cancel stay active under every pause.
    ///
    /// While a window is open one pause token pauses Calendar and Player and is released on close.
    /// Cancelling a Choose window by the player fails the handle with CANCELLED; cancelling through the handle or
    /// CloseOwner or player cancel on Say finalizes it as Cancelled after the window is released.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialoguePresentation : MonoBehaviour, IPresentationPort, IPresentationLifetime, IRuntimeTick, IDialogueUiCommands
    {
        public const string ResourcePath = "MagicCampus/Dialogue";
        static readonly PauseSet WindowPause = new PauseSet(true, false, true, false, false);

        [SerializeField] float textScale = 1f;
        [SerializeField] int baseFontSize = 30;
        [SerializeField] float referenceHeight = 1080f;
        [SerializeField] [Range(0.2f, 0.6f)] float panelHeightRatio = 0.34f;
        [SerializeField] int scrollLinesPerStep = 2;
        [SerializeField] float worldUnitsPerTile = 1f;
        [SerializeField] bool pollHardwareInput = true;
        [SerializeField] string[] fontNames = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic" };
        [SerializeField] string confirmHint = "확인";
        [SerializeField] string scrollHint = "▲▼ 더 보기";
        [SerializeField] string choiceHint = "▲▼ 이동    확인 결정    취소 닫기";

        readonly DialogueUiPoller poller = new DialogueUiPoller();
        readonly DialogueGuiSettings settings = new DialogueGuiSettings();
        readonly Dictionary<string, VisualClipCallback> pendingClips = new Dictionary<string, VisualClipCallback>(StringComparer.Ordinal);
        IHandleFactory handles;
        IPauseService pause;
        IMapPort map;
        IActorVisualResolver actors;
        CutsceneDirector director;
        DialogueGuiRenderer renderer;
        DialogueWindow window;
        Camera presentationCamera;
        Func<Position, Vector3> tileToWorld;
        Action<int> pointerOptionAction;
        Action pointerConfirmAction;
        Action<int> pointerScrollAction;
        int serial;

        /// <summary>Captures the caller's UI focus when the first window opens; the value is passed back to FocusRestore on close.</summary>
        public Func<object> FocusCapture;
        public Action<object> FocusRestore;
        public Func<string, string> SpeakerDisplayName;

        public bool PollHardwareInput { get { return pollHardwareInput; } set { pollHardwareInput = value; } }
        public float TextScale { get { return textScale; } set { textScale = Mathf.Clamp(value, 0.5f, 3f); } }

        public Func<Position, Vector3> TileToWorld
        {
            get { return tileToWorld; }
            set { tileToWorld = value; if (director != null) director.TileToWorld = value; }
        }

        public bool UiCaptured { get { return window != null; } }
        public DialogueWindowKind ActiveWindow { get { return window == null ? DialogueWindowKind.None : window.Kind; } }
        public string CurrentSpeaker { get { return window == null ? string.Empty : window.Speaker; } }
        public string CurrentText { get { return window == null ? string.Empty : window.Text; } }
        public string CurrentPrompt { get { return window == null ? string.Empty : window.Prompt; } }
        public int FocusIndex { get { return window == null ? -1 : window.Focus; } }
        public int OptionCount { get { return window == null ? 0 : window.Options.Length; } }
        public int TextScrollLines { get { return window == null ? 0 : window.ScrollLines; } }
        public string CameraOwner { get { return director == null ? null : director.CameraOwner; } }

        /// <summary>Pause tokens, camera holds and visual holds still outstanding. Zero after everything is closed.</summary>
        public int OutstandingLocks
        {
            get { return (window != null && window.Token != null ? 1 : 0) + (director != null ? director.HeldCount : 0); }
        }

        public string OptionLabel(int index)
        {
            if (window == null || index < 0 || index >= window.Options.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return window.Options[index].Text;
        }

        /// <summary>Wires the ports. Reconfiguring closes whatever is still open first.</summary>
        public void Configure(IHandleFactory handleFactory, IPauseService pauseService, IMapPort mapPort, IActorVisualResolver actorResolver)
        {
            if (handleFactory == null) throw new ArgumentNullException(nameof(handleFactory));
            if (pauseService == null) throw new ArgumentNullException(nameof(pauseService));
            if (handles != null) CloseAll("reconfigured");
            handles = handleFactory;
            pause = pauseService;
            map = mapPort;
            actors = actorResolver;
            director = new CutsceneDirector(handles, map, actors);
            director.Camera = presentationCamera;
            director.WorldUnitsPerTile = worldUnitsPerTile;
            director.TileToWorld = tileToWorld;
            foreach (var kv in pendingClips) director.RegisterClip(kv.Key, kv.Value);
        }

        public void SetCamera(Camera camera)
        {
            presentationCamera = camera;
            if (director != null) director.Camera = camera;
        }

        public void RegisterClip(string clipName, VisualClipCallback callback)
        {
            if (!PresentationHandles.ValidId(clipName)) throw new ArgumentException("invalid clip name", nameof(clipName));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (pendingClips.ContainsKey(clipName)) throw new ArgumentException("clip already registered", nameof(clipName));
            pendingClips.Add(clipName, callback);
            if (director != null) director.RegisterClip(clipName, callback);
        }

        // ------------------------------------------------------------------ IPresentationPort

        public IHandle Say(string speaker, string text, string ownerToken)
        {
            RequireConfigured();
            var id = NextId("say");
            if (!PresentationHandles.ValidId(ownerToken)) return Refuse<bool>(id, ResultCodes.ContentInvalid, "owner token is invalid", "valid owner token", ownerToken ?? "null");
            if (string.IsNullOrWhiteSpace(text)) return Refuse<bool>(id, ResultCodes.ContentInvalid, "dialogue text is empty", "text", "empty");
            if (window != null) return Refuse<bool>(id, ResultCodes.ResourceBusy, "a dialogue window is already open", "no window", window.Kind.ToString());

            var ctl = handles.Create<bool>(id);
            var w = new DialogueWindow { Kind = DialogueWindowKind.Say, Owner = ownerToken, Speaker = SpeakerDisplayName != null ? SpeakerDisplayName(speaker ?? string.Empty) : speaker ?? string.Empty, Text = text, SayController = ctl };
            Open(w);
            ctl.SetCancellationHandler(reason => CancelWindow(w, reason));
            ctl.Start();
            return ctl;
        }

        public IHandle<ChoiceSelection> Choose(string prompt, IReadOnlyList<ChoiceOption> options, string ownerToken)
        {
            RequireConfigured();
            var id = NextId("choose");
            if (!PresentationHandles.ValidId(ownerToken)) return Refuse<ChoiceSelection>(id, ResultCodes.ContentInvalid, "owner token is invalid", "valid owner token", ownerToken ?? "null");
            if (options == null || options.Count == 0) return Refuse<ChoiceSelection>(id, ResultCodes.ContentInvalid, "at least one choice is required", ">= 1 option", "0");
            var copy = new ChoiceOption[options.Count];
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < copy.Length; i++)
            {
                var o = options[i];
                if (o == null) return Refuse<ChoiceSelection>(id, ResultCodes.ContentInvalid, "choice option is null", "option", "null");
                if (string.IsNullOrWhiteSpace(o.Text)) return Refuse<ChoiceSelection>(id, ResultCodes.ContentInvalid, "choice label is empty", "label", "empty");
                if (!keys.Add(o.Key)) return Refuse<ChoiceSelection>(id, ResultCodes.ContentInvalid, "duplicate choice key", "unique key", o.Key);
                copy[i] = o;
            }
            if (window != null) return Refuse<ChoiceSelection>(id, ResultCodes.ResourceBusy, "a dialogue window is already open", "no window", window.Kind.ToString());

            var ctl = handles.Create<ChoiceSelection>(id);
            var w = new DialogueWindow { Kind = DialogueWindowKind.Choose, Owner = ownerToken, Prompt = prompt ?? string.Empty, Options = copy, Focus = 0, ChoiceController = ctl };
            Open(w);
            ctl.SetCancellationHandler(reason => CancelWindow(w, reason));
            ctl.Start();
            return ctl;
        }

        public IHandle MoveCamera(Destination target, double seconds, string ownerToken)
        {
            RequireConfigured();
            return director.MoveCamera(target, seconds, ownerToken);
        }

        public IHandle Animate(string actorId, string clip, double seconds, string ownerToken)
        {
            RequireConfigured();
            return director.Animate(actorId, clip, seconds, ownerToken);
        }

        // ------------------------------------------------------------------ IPresentationLifetime

        public void CloseOwner(string ownerToken)
        {
            if (!PresentationHandles.ValidId(ownerToken)) return;
            var w = window;
            if (w != null && string.Equals(w.Owner, ownerToken, StringComparison.Ordinal)) CancelWindow(w, "owner closed");
            if (director != null) director.CloseOwner(ownerToken);
        }

        /// <summary>Scene teardown: closes every window and tween regardless of owner.</summary>
        public void CloseAll(string reason)
        {
            var w = window;
            if (w != null) CancelWindow(w, reason);
            if (director != null) director.CloseAll(reason);
        }

        // ------------------------------------------------------------------ IRuntimeTick

        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (handles == null) return;
            var dt = (unpausedSeconds > 0.0 && !double.IsNaN(unpausedSeconds) && !double.IsInfinity(unpausedSeconds)) ? (float)unpausedSeconds : 0f;
            var w = window;
            if (w != null && pollHardwareInput) poller.Poll(this, dt, Time.frameCount > w.OpenFrame);
            if (!pause.Effective.Scene) director.Advance(dt);
        }

        // ------------------------------------------------------------------ UI commands (same path for hardware, pointer and tests)

        public bool Navigate(NavDirection direction)
        {
            var w = window;
            if (w == null) return false;
            if (w.Kind == DialogueWindowKind.Say)
            {
                if (direction == NavDirection.Up) return ScrollText(-1);
                if (direction == NavDirection.Down) return ScrollText(1);
                return false;
            }
            var n = w.Options.Length;
            if (n < 2) return false;
            var delta = (direction == NavDirection.Up || direction == NavDirection.Left) ? -1 : 1;
            w.Focus = (w.Focus + delta + n) % n;
            return true;
        }

        public bool Confirm()
        {
            var w = window;
            if (w == null) return false;
            if (w.Kind == DialogueWindowKind.Say)
            {
                CloseWindow(w);
                w.SayController.Succeed(true, 0);
                return true;
            }
            var chosen = w.Options[w.Focus];
            var selection = new ChoiceSelection(chosen.Key, w.Focus);
            CloseWindow(w);
            w.ChoiceController.Succeed(selection, 0);
            return true;
        }

        public bool Cancel()
        {
            var w = window;
            if (w == null) return false;
            if (w.Kind == DialogueWindowKind.Say)
            {
                CancelWindow(w, "dialogue cancelled by the player");
                return true;
            }
            CloseWindow(w);
            w.ChoiceController.Fail(ResultCodes.Cancelled, 0, new[] { PresentationHandles.Diag(ResultCodes.Cancelled, "choice cancelled by the player", "selection", "cancelled") });
            return true;
        }

        /// <summary>Mouse click on an option: focuses it and confirms through the normal path.</summary>
        public bool PointerSelect(int index)
        {
            var w = window;
            if (w == null || w.Kind != DialogueWindowKind.Choose || index < 0 || index >= w.Options.Length) return false;
            w.Focus = index;
            return Confirm();
        }

        public bool ScrollText(int steps)
        {
            var w = window;
            if (w == null || w.Kind != DialogueWindowKind.Say || steps == 0) return false;
            var step = Mathf.Max(1, scrollLinesPerStep);
            w.ScrollLines = Mathf.Clamp(w.ScrollLines + steps * step, 0, w.Text.Length);
            return true;
        }

        // ------------------------------------------------------------------ internals

        void Open(DialogueWindow w)
        {
            w.OpenFrame = Time.frameCount;
            w.Token = pause.Acquire(w.Owner, WindowPause);
            if (FocusCapture != null)
            {
                try
                {
                    w.SavedFocus = FocusCapture();
                    w.HasSavedFocus = true;
                }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            window = w;
            poller.Reset();
        }

        void CloseWindow(DialogueWindow w)
        {
            if (w.Closed) return;
            w.Closed = true;
            if (ReferenceEquals(window, w)) window = null;
            poller.Reset();
            if (w.Token != null)
            {
                pause.Release(w.Token);
                w.Token = null;
            }
            if (w.HasSavedFocus && FocusRestore != null)
            {
                try { FocusRestore(w.SavedFocus); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            GUIUtility.keyboardControl = 0;
        }

        void CancelWindow(DialogueWindow w, string reason)
        {
            CloseWindow(w);
            if (w.SayController != null) w.SayController.Cancelled(reason ?? string.Empty, 0);
            if (w.ChoiceController != null) w.ChoiceController.Cancelled(reason ?? string.Empty, 0);
        }

        void RequireConfigured()
        {
            if (handles == null) throw new InvalidOperationException("DialoguePresentation.Configure must be called before use");
        }

        string NextId(string kind)
        {
            serial++;
            return "dialogue." + kind + "." + serial.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        IHandleController<T> Refuse<T>(string id, string code, string message, string expected, string actual)
        {
            return PresentationHandles.Failed<T>(handles, id, code, message, expected, actual);
        }

        void OnGUI()
        {
            var w = window;
            if (w == null) return;
            if (renderer == null) renderer = new DialogueGuiRenderer();
            if (pointerOptionAction == null)
            {
                pointerOptionAction = i => PointerSelect(i);
                pointerConfirmAction = () => Confirm();
                pointerScrollAction = d => ScrollText(d);
            }
            settings.TextScale = textScale;
            settings.BaseFontSize = baseFontSize;
            settings.ReferenceHeight = referenceHeight;
            settings.PanelHeightRatio = panelHeightRatio;
            settings.FontNames = fontNames;
            settings.ConfirmHint = confirmHint;
            settings.ScrollHint = scrollHint;
            settings.ChoiceHint = choiceHint;
            GUI.depth = -1000;
            renderer.Draw(w, settings, pointerOptionAction, pointerConfirmAction, pointerScrollAction);
        }

        void OnDisable()
        {
            CloseAll("presentation disabled");
        }

        void OnDestroy()
        {
            CloseAll("presentation destroyed");
            if (renderer != null)
            {
                renderer.Dispose();
                renderer = null;
            }
        }
    }
}
