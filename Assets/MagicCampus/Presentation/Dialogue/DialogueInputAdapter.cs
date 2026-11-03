using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCampus.Presentation.Dialogue
{
    public static class DialogueInputAdapter
    {
        /// <summary>
        /// World view of a frame while UI owns input: movement is zeroed and only the UI actions confirm, cancel and
        /// menu survive, so interact, cast, dodge and journal cannot leak into the world.
        /// </summary>
        public static InputFrame SuppressWorld(InputFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            return new InputFrame(frame.Tick, frame.InputSequence, 0.0, 0.0, true, KeepUi(frame.Pressed), KeepUi(frame.Held));
        }

        static List<string> KeepUi(IReadOnlyList<string> src)
        {
            var list = new List<string>();
            for (var i = 0; i < src.Count; i++)
            {
                var a = src[i];
                if (a == InputActions.Confirm || a == InputActions.Cancel || a == InputActions.Menu) list.Add(a);
            }
            return list;
        }
    }

    /// <summary>IInputPort decorator: while the UI is captured the world only sees the suppressed frame.</summary>
    public sealed class UiCapturingInputPort : IInputPort
    {
        readonly IInputPort inner;
        readonly Func<bool> uiCaptured;

        public UiCapturingInputPort(IInputPort inner, Func<bool> uiCaptured)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.uiCaptured = uiCaptured ?? throw new ArgumentNullException(nameof(uiCaptured));
        }

        public InputFrame Poll(long tick)
        {
            var frame = inner.Poll(tick);
            if (frame == null) return null;
            return (uiCaptured() || frame.UiCaptured) ? DialogueInputAdapter.SuppressWorld(frame) : frame;
        }
    }

    /// <summary>
    /// Reads the UI bindings straight from the Input System devices: arrows/WASD, d-pad and left stick for focus,
    /// Enter/Space/south button to confirm, Escape/Backspace/east button to cancel. Held directions repeat.
    /// </summary>
    public sealed class DialogueUiPoller
    {
        public float StickThreshold = 0.6f;
        public float RepeatDelay = 0.4f;
        public float RepeatInterval = 0.12f;

        bool active;
        NavDirection previous;
        float heldFor;
        float nextRepeat;

        public void Reset()
        {
            active = false;
            heldFor = 0f;
            nextRepeat = 0f;
        }

        public void Poll(IDialogueUiCommands sink, float deltaSeconds, bool allowConfirm)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            var kb = Keyboard.current;
            var pad = Gamepad.current;

            if (allowConfirm && ConfirmPressed(kb, pad)) sink.Confirm();
            if (CancelPressed(kb, pad)) sink.Cancel();

            NavDirection dir;
            if (!ReadDirection(kb, pad, out dir))
            {
                Reset();
                return;
            }
            if (!active || dir != previous)
            {
                active = true;
                previous = dir;
                heldFor = 0f;
                nextRepeat = Mathf.Max(0.05f, RepeatDelay);
                sink.Navigate(dir);
                return;
            }
            heldFor += Mathf.Max(0f, deltaSeconds);
            var interval = Mathf.Max(0.02f, RepeatInterval);
            var guard = 0;
            while (heldFor >= nextRepeat && guard++ < 8)
            {
                nextRepeat += interval;
                sink.Navigate(dir);
            }
        }

        static bool ConfirmPressed(Keyboard kb, Gamepad pad)
        {
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) return true;
            return pad != null && pad.buttonSouth.wasPressedThisFrame;
        }

        static bool CancelPressed(Keyboard kb, Gamepad pad)
        {
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) return true;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }

        bool ReadDirection(Keyboard kb, Gamepad pad, out NavDirection dir)
        {
            dir = NavDirection.Up;
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) { dir = NavDirection.Up; return true; }
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) { dir = NavDirection.Down; return true; }
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) { dir = NavDirection.Left; return true; }
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) { dir = NavDirection.Right; return true; }
            }
            if (pad != null)
            {
                if (pad.dpad.up.isPressed) { dir = NavDirection.Up; return true; }
                if (pad.dpad.down.isPressed) { dir = NavDirection.Down; return true; }
                if (pad.dpad.left.isPressed) { dir = NavDirection.Left; return true; }
                if (pad.dpad.right.isPressed) { dir = NavDirection.Right; return true; }
                var s = pad.leftStick.ReadValue();
                var ax = Mathf.Abs(s.x);
                var ay = Mathf.Abs(s.y);
                if (ay >= StickThreshold && ay >= ax) { dir = s.y > 0f ? NavDirection.Up : NavDirection.Down; return true; }
                if (ax >= StickThreshold) { dir = s.x > 0f ? NavDirection.Right : NavDirection.Left; return true; }
            }
            return false;
        }
    }
}
