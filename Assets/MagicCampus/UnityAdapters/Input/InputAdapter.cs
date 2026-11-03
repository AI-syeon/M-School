using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using UnityEngine.InputSystem;
using LogicalActions = MagicCampus.Contracts.InputActions;

namespace MagicCampus.UnityAdapters.Input
{
    /// <summary>Stick dead zone as a fraction of full deflection, [0, 1).</summary>
    public sealed class InputAdapterConfig
    {
        public static readonly InputAdapterConfig Default = new InputAdapterConfig(0.2);
        public double Deadzone { get; }

        public InputAdapterConfig(double deadzone)
        {
            if (double.IsNaN(deadzone) || deadzone < 0 || deadzone >= 1) throw new ArgumentOutOfRangeException(nameof(deadzone));
            Deadzone = deadzone;
        }
    }

    /// <summary>
    /// Keyboard, mouse and gamepad (Input System 1.19) to logical InputFrame. Devices are sampled on Poll, as a union over every attached device,
    /// so a removed gamepad drops out of the next frame (no stuck movement or buttons). Main thread only. Never writes actor positions.
    /// MoveY is positive for up (tile Y grows upward). Bindings: interact E / pad West; confirm Enter,Space / pad South / left mouse;
    /// cancel Backspace,X / pad East / right mouse; menu Escape / pad Start; journal J,Tab / pad Select; cast Q / pad RightShoulder,RightTrigger;
    /// dodge LeftShift / pad LeftShoulder,LeftTrigger.
    /// UI capture: movement and gameplay actions (interact, journal, cast, dodge) are consumed; confirm, cancel and menu still pass.
    /// A gameplay action held while captured stays consumed until it is released, so it never leaks as a fresh press afterwards.
    /// </summary>
    public sealed class InputAdapter : IInputPort
    {
        private static readonly string[] Gameplay = { LogicalActions.Interact, LogicalActions.Journal, LogicalActions.Cast, LogicalActions.Dodge };

        private sealed class Sample
        {
            public bool Up, Down, Left, Right;
            public double StickX, StickY, StickSq;
        }

        private readonly InputAdapterConfig config;
        private readonly Func<bool> uiCaptured;
        private readonly HashSet<string> previous = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> consumed = new HashSet<string>(StringComparer.Ordinal);
        private InputFrame last;
        private long sequence;

        public InputAdapter(InputAdapterConfig config, Func<bool> uiCaptured)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.uiCaptured = uiCaptured ?? throw new ArgumentNullException(nameof(uiCaptured));
        }

        /// <summary>Polling the same tick twice returns the same frame; a new tick increments InputSequence.</summary>
        public InputFrame Poll(long tick)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (last != null && last.Tick == tick) return last;

            var raw = new HashSet<string>(StringComparer.Ordinal);
            var sample = new Sample();
            foreach (var device in InputSystem.devices)
            {
                if (device is Keyboard keyboard) ReadKeyboard(keyboard, raw, sample);
                else if (device is Mouse mouse) ReadMouse(mouse, raw);
                else if (device is Gamepad gamepad) ReadGamepad(gamepad, raw, sample);
            }

            var captured = uiCaptured();
            foreach (var action in Gameplay)
            {
                if (!raw.Contains(action)) consumed.Remove(action);
                else if (captured) consumed.Add(action);
            }

            var held = new List<string>();
            var pressed = new List<string>();
            foreach (var action in raw)
            {
                if (consumed.Contains(action)) continue;
                held.Add(action);
                if (!previous.Contains(action)) pressed.Add(action);
            }
            previous.Clear();
            foreach (var action in held) previous.Add(action);

            double x = 0, y = 0;
            if (!captured) Move(sample, out x, out y);
            last = new InputFrame(tick, ++sequence, x, y, captured, pressed, held);
            return last;
        }

        private void Move(Sample s, out double x, out double y)
        {
            double dx = (s.Right ? 1 : 0) - (s.Left ? 1 : 0);
            double dy = (s.Up ? 1 : 0) - (s.Down ? 1 : 0);
            if (dx != 0 || dy != 0)
            {
                var len = Math.Sqrt(dx * dx + dy * dy);
                x = dx / len;
                y = dy / len;
                return;
            }
            var mag = Math.Sqrt(s.StickSq);
            if (mag <= config.Deadzone) { x = 0; y = 0; return; }
            var scale = Math.Min(1.0, (mag - config.Deadzone) / (1.0 - config.Deadzone));
            x = s.StickX / mag * scale;
            y = s.StickY / mag * scale;
        }

        private static bool Any(Keyboard kb, params Key[] keys)
        {
            foreach (var k in keys)
                if (kb[k].isPressed) return true;
            return false;
        }

        private static void ReadKeyboard(Keyboard kb, HashSet<string> raw, Sample s)
        {
            if (Any(kb, Key.W, Key.UpArrow)) s.Up = true;
            if (Any(kb, Key.S, Key.DownArrow)) s.Down = true;
            if (Any(kb, Key.A, Key.LeftArrow)) s.Left = true;
            if (Any(kb, Key.D, Key.RightArrow)) s.Right = true;
            if (Any(kb, Key.E)) raw.Add(LogicalActions.Interact);
            if (Any(kb, Key.Enter, Key.NumpadEnter, Key.Space)) raw.Add(LogicalActions.Confirm);
            if (Any(kb, Key.Backspace, Key.X)) raw.Add(LogicalActions.Cancel);
            if (Any(kb, Key.Escape)) raw.Add(LogicalActions.Menu);
            if (Any(kb, Key.J, Key.Tab)) raw.Add(LogicalActions.Journal);
            if (Any(kb, Key.Q)) raw.Add(LogicalActions.Cast);
            if (Any(kb, Key.LeftShift)) raw.Add(LogicalActions.Dodge);
        }

        private static void ReadMouse(Mouse mouse, HashSet<string> raw)
        {
            if (mouse.leftButton.isPressed) raw.Add(LogicalActions.Confirm);
            if (mouse.rightButton.isPressed) raw.Add(LogicalActions.Cancel);
        }

        private static void ReadGamepad(Gamepad pad, HashSet<string> raw, Sample s)
        {
            if (pad.dpad.up.isPressed) s.Up = true;
            if (pad.dpad.down.isPressed) s.Down = true;
            if (pad.dpad.left.isPressed) s.Left = true;
            if (pad.dpad.right.isPressed) s.Right = true;
            var v = pad.leftStick.ReadUnprocessedValue();
            var sq = (double)v.x * v.x + (double)v.y * v.y;
            if (sq > s.StickSq) { s.StickSq = sq; s.StickX = v.x; s.StickY = v.y; }
            if (pad.buttonWest.isPressed) raw.Add(LogicalActions.Interact);
            if (pad.buttonSouth.isPressed) raw.Add(LogicalActions.Confirm);
            if (pad.buttonEast.isPressed) raw.Add(LogicalActions.Cancel);
            if (pad.startButton.isPressed) raw.Add(LogicalActions.Menu);
            if (pad.selectButton.isPressed) raw.Add(LogicalActions.Journal);
            if (pad.rightShoulder.isPressed || pad.rightTrigger.isPressed) raw.Add(LogicalActions.Cast);
            if (pad.leftShoulder.isPressed || pad.leftTrigger.isPressed) raw.Add(LogicalActions.Dodge);
        }
    }
}
