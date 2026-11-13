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
    /// to the active candidate's gesture reading (#28), Candidate A's ACTION SURFACE
    /// (<see cref="ActionSurfaceAdapter"/>) or B's SPLIT (<see cref="SplitAdapter"/>, #76), which
    /// turns them into owned commands for one controller. The candidates switch live
    /// (<see cref="TouchCandidates"/>). It does no assistance; that belongs in the simulation.
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

        private readonly TouchCandidates _gestures;
        private readonly InputAction _pressAction;
        private readonly InputAction _moveAction;
        // Who owns each finger that is down, fixed at its press until it lifts: the UI's, or the
        // controls' (with when it was pressed).
        private readonly List<int> _onUi = new List<int>(4);
        private readonly Dictionary<int, double> _pressedAt = new Dictionary<int, double>(4);
        private int _screenHeight;
        private bool _touchSeen;

        public TouchAdapter(int controllerId, ActionSurfaceSettings actionSurface, SplitSettings split)
        {
            _gestures = new TouchCandidates(controllerId, actionSurface, split);

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

        /// <summary>The active candidate (#76).</summary>
        public ControlCandidate Active => _gestures.Active;

        /// <summary>The active candidate's name: <c>action-surface</c> or <c>split</c>.</summary>
        public string Name => _gestures.Name;

        /// <summary>The active candidate and every value, for a capture.</summary>
        public string Describe() => _gestures.Describe();

        public bool StickActive => _gestures.Current.StickActive;
        public Vector2 StickOrigin => new Vector2((float)_gestures.Current.StickOriginX, (float)_gestures.Current.StickOriginY);
        public Vector2 StickKnob => new Vector2((float)_gestures.Current.StickKnobX, (float)_gestures.Current.StickKnobY);

        /// <summary>Whether a finger is on the control that makes a kick: A's ACTION, or B's KICK.</summary>
        public bool ActionHeld => _gestures.Current.ActionHeld;

        /// <summary>Whether a finger holds B's PASS.</summary>
        public bool PassHeld => _gestures.Active == ControlCandidate.Split && _gestures.Split.PassHeld;

        public bool Shaping => _gestures.Current.Shaping;

        /// <summary>How far a hold has charged now: 0 to 1.</summary>
        public double Charge => _gestures.Current.Charge(Now);

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
            _gestures.SetLayout(new TouchLayout(Region(layout.StickZone), Region(layout.ActionZone), layout.StickRadius, pixelsPerPoint,
                layout.PassCentre.x, layout.PassCentre.y, layout.PassRadius));
        }

        /// <summary>New gesture settings for both candidates (APPLY / RETRY); any gesture in progress is cancelled.</summary>
        public void Use(ActionSurfaceSettings actionSurface, SplitSettings split)
        {
            _gestures.ActionSurface.Use(actionSurface);
            _gestures.Split.Use(split);
        }

        /// <summary>
        /// The live switch (#76): another candidate at once. Gestures in progress are cancelled, and
        /// fingers already down are ignored until they lift.
        /// </summary>
        public void Switch(ControlCandidate to) => _gestures.Switch(to);

        /// <summary>
        /// How the pitch is shown (#72): gestures read on screen go up-field the way the screen shows
        /// it. A new view cancels any gesture in progress, so a flip moves nothing and kicks nothing.
        /// </summary>
        public void Orient(PitchOrientation orientation) => _gestures.Orient(orientation);

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

            // Unity reports a held finger pressed again whenever its phase changes. The repeat keeps
            // the owner the press gave it: a stick finger that has moved over a UI action stays the
            // stick's, and a finger that began on the UI never becomes a gesture (Codex on #57).
            if (_onUi.Contains(id) || _pressedAt.ContainsKey(id)) return;

            string target;
            if (OverUi != null && OverUi(at))
            {
                _onUi.Add(id);
                target = "ui";
            }
            else
            {
                target = Target(_gestures.Feed(new TouchSample(id, GesturePhase.Began, at.x, at.y, context.time)));
                _pressedAt[id] = context.time;
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
            TouchRole.Pass => "pass",
            TouchRole.Kick => "kick",
            _ => "none",
        };

        private static TouchRegion Region(Rect r) => new TouchRegion(r.x, r.y, r.width, r.height);

        // Input System positions have their origin at the bottom left; the HUD's is the top left.
        private Vector2 ToHud(Vector2 position) => new Vector2(position.x, _screenHeight - position.y);
    }
}
