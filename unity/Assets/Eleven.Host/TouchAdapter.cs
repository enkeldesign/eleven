using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using GesturePhase = Eleven.Simulation.Harness.TouchPhase;

namespace Eleven.Host
{
    /// <summary>
    /// The touch input adapter (INV-10): reads raw pointers through the Input System and hands them
    /// to Candidate A's gesture reading (<see cref="ActionSurfaceAdapter"/>, #28), which turns them
    /// into owned commands for one controller. It does no assistance; that belongs in the simulation.
    ///
    /// The stick floats: a press anywhere in the stick zone becomes its centre, and dragging away
    /// from it gives the move vector, full stick at <see cref="HudLayout.StickRadius"/>. The right
    /// thumb's ACTION surface reads a tap as the quick pass, a flick as a manual kick along it, a
    /// hold → flick as a harder one, and a re-touch while a kick winds up or its window is open as
    /// aftertouch.
    ///
    /// Touchscreen first; the mouse stands in for a single finger on desktop browsers, and is
    /// ignored once any touch has been seen so a browser's emulated mouse events don't double up.
    ///
    /// Presses, releases and moves are taken from Input System events as they arrive, each with its
    /// event time, not by polling once a frame: a tap that goes down and up between two frames still
    /// counts, and a flick is timed from its own samples, at any frame rate. A press that lands on the
    /// UI belongs to the UI (#48) and never reaches the gestures.
    /// </summary>
    internal sealed class TouchAdapter : IDisposable
    {
        private const int MousePointer = -1;

        private readonly ActionSurfaceAdapter _gestures;
        private readonly InputAction _pressAction;
        private readonly InputAction _moveAction;
        private readonly List<int> _onUi = new List<int>(4);
        private readonly Dictionary<int, double> _pressedAt = new Dictionary<int, double>(4);
        private int _screenHeight;
        private bool _touchSeen;

        public TouchAdapter(int controllerId, ActionSurfaceSettings settings)
        {
            _gestures = new ActionSurfaceAdapter(controllerId, settings);

            // Pass-through reports every value change of every bound control separately, so two
            // fingers pressing in the same frame are two presses, and every move is a sample.
            _pressAction = new InputAction("Pointer presses", InputActionType.PassThrough);
            _pressAction.AddBinding("<Touchscreen>/touch*/press");
            _pressAction.AddBinding("<Mouse>/leftButton");
            _pressAction.performed += OnPress;
            _pressAction.Enable();

            _moveAction = new InputAction("Pointer moves", InputActionType.PassThrough);
            _moveAction.AddBinding("<Touchscreen>/touch*/position");
            _moveAction.AddBinding("<Mouse>/position");
            _moveAction.performed += OnMove;
            _moveAction.Enable();
        }

        /// <summary>Raised once per press as it arrives: where it landed and what it was given.</summary>
        public event Action<string> PressLogged;

        /// <summary>Raised once per lift of a finger the controls took: its role and how long it was down.</summary>
        public event Action<string> ReleaseLogged;

        /// <summary>Raised when a gesture's command is delivered to a tick.</summary>
        public event Action<ActionReport> Delivered
        {
            add => _gestures.Delivered += value;
            remove => _gestures.Delivered -= value;
        }

        /// <summary>Whether a HUD point lands on the UI (#48): those presses belong to the UI, not to play.</summary>
        public Func<Vector2, bool> OverUi { get; set; }

        public ActionSurfaceSettings Settings => _gestures.Settings;

        public bool StickActive => _gestures.StickActive;
        public Vector2 StickOrigin => new Vector2((float)_gestures.StickOriginX, (float)_gestures.StickOriginY);
        public Vector2 StickKnob => new Vector2((float)_gestures.StickKnobX, (float)_gestures.StickKnobY);
        public bool ActionHeld => _gestures.ActionHeld;
        public bool Shaping => _gestures.Shaping;

        /// <summary>How far a hold on ACTION has charged now: 0 to 1.</summary>
        public double Charge => _gestures.Charge(Now);

        /// <summary>Whether a press on ACTION shapes the ball now: set from the state after each frame's steps.</summary>
        public bool ShapingOpen
        {
            set => _gestures.ShapingOpen = value;
        }

        /// <summary>The Input System's clock, the one its events are stamped on.</summary>
        public static double Now => InputState.currentTime;

        /// <summary>Where the controls are. Call whenever the layout changes.</summary>
        public void Place(in HudLayout layout, float pixelsPerPoint)
        {
            _screenHeight = layout.ScreenHeight;
            _gestures.SetLayout(Region(layout.StickZone), Region(layout.ActionZone), layout.StickRadius, pixelsPerPoint);
        }

        /// <summary>New gesture settings (APPLY / RETRY); any gesture in progress is cancelled.</summary>
        public void Use(ActionSurfaceSettings settings) => _gestures.Use(settings);

        /// <summary>The owned command for <paramref name="tick"/>. A gesture's kick or pass is delivered once.</summary>
        public Command CommandFor(long tick) => _gestures.CommandFor(tick);

        /// <summary>Cancels every gesture in progress, for a reset, a retry or a sheet opening: no phantom command follows.</summary>
        public void Clear() => _gestures.Cancel();

        public void Dispose()
        {
            _pressAction.performed -= OnPress;
            _pressAction.Dispose();
            _moveAction.performed -= OnMove;
            _moveAction.Dispose();
        }

        private void OnPress(InputAction.CallbackContext context)
        {
            bool down = context.ReadValue<float>() >= 0.5f;
            int id;
            Vector2 position;
            GesturePhase end = GesturePhase.Ended;
            if (context.control.parent is TouchControl touch)
            {
                _touchSeen = true;
                id = touch.touchId.ReadValue();
                position = touch.position.ReadValue();
                if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled) end = GesturePhase.Canceled;
            }
            else if (context.control.device is Mouse mouse && !_touchSeen)
            {
                id = MousePointer;
                position = mouse.position.ReadValue();
            }
            else return;

            Vector2 at = ToHud(position);
            if (!down)
            {
                if (_onUi.Remove(id)) return;
                TouchRole role = _gestures.Feed(new TouchSample(id, end, at.x, at.y, context.time));
                if (role != TouchRole.None && _pressedAt.TryGetValue(id, out double pressed))
                {
                    ReleaseLogged?.Invoke(FormattableString.Invariant($"pointer={id} at={at.x:F0},{at.y:F0} role={Target(role)} phase={end} held={(context.time - pressed) * 1000:F0}ms"));
                }

                _pressedAt.Remove(id);
                return;
            }

            string target;
            if (OverUi != null && OverUi(at))
            {
                if (!_onUi.Contains(id)) _onUi.Add(id);
                target = "ui";
            }
            else
            {
                _onUi.Remove(id);
                target = Target(_gestures.Feed(new TouchSample(id, GesturePhase.Began, at.x, at.y, context.time)));
                if (!_pressedAt.ContainsKey(id)) _pressedAt[id] = context.time;
            }

            PressLogged?.Invoke(FormattableString.Invariant($"pointer={id} at={at.x:F0},{at.y:F0} target={target}"));
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            int id;
            if (context.control.parent is TouchControl touch)
            {
                if (!touch.press.isPressed) return;
                id = touch.touchId.ReadValue();
            }
            else if (context.control.device is Mouse mouse && !_touchSeen)
            {
                if (!mouse.leftButton.isPressed) return;
                id = MousePointer;
            }
            else return;

            if (_onUi.Contains(id)) return;
            Vector2 at = ToHud(context.ReadValue<Vector2>());
            _gestures.Feed(new TouchSample(id, GesturePhase.Moved, at.x, at.y, context.time));
        }

        private static string Target(TouchRole role) => role switch
        {
            TouchRole.Stick => "stick",
            TouchRole.Action => "action",
            TouchRole.Shaping => "shaping",
            _ => "none",
        };

        private static TouchRegion Region(Rect r) => new TouchRegion(r.x, r.y, r.width, r.height);

        // Input System positions have their origin at the bottom left; the HUD's is the top left.
        private Vector2 ToHud(Vector2 position) => new Vector2(position.x, _screenHeight - position.y);
    }
}
