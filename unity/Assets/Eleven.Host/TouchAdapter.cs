using System.Collections.Generic;
using Eleven.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Eleven.Host
{
    /// <summary>
    /// The touch input adapter (INV-10): reads raw pointers through the Input System and turns
    /// them into owned commands for one controller. It does no assistance; that belongs in the
    /// simulation.
    ///
    /// The stick floats: a press anywhere in the stick zone becomes its centre, and dragging
    /// away from it gives the move vector, full stick at <see cref="HudLayout.StickRadius"/>.
    /// The kick button and the HUD buttons act when a press begins on them.
    ///
    /// Touchscreen first; the mouse stands in for a single finger on desktop browsers, and is
    /// ignored once any touch has been seen so a browser's emulated mouse events don't double up.
    /// </summary>
    internal sealed class TouchAdapter
    {
        private const int MousePointer = -1;
        private const int NoPointer = int.MinValue;

        private readonly int _controllerId;
        private readonly List<Pointer> _pointers = new List<Pointer>(11);
        private int _stickPointer = NoPointer;
        private bool _kickRequested;
        private bool _touchSeen;

        public TouchAdapter(int controllerId)
        {
            _controllerId = controllerId;
        }

        /// <summary>Move vector, length at most 1; +x right, +y up the screen.</summary>
        public Vector2 Stick { get; private set; }

        public bool StickActive => _stickPointer != NoPointer;
        public Vector2 StickOrigin { get; private set; }
        public Vector2 StickKnob { get; private set; }
        public bool KickHeld { get; private set; }

        public bool RunTapped { get; private set; }
        public bool MathsTapped { get; private set; }
        public bool ResetTapped { get; private set; }

        /// <summary>Reads this frame's pointers. Call once per frame before generating commands.</summary>
        public void Sample(in HudLayout layout)
        {
            CollectPointers(layout.ScreenHeight);

            RunTapped = MathsTapped = ResetTapped = false;
            KickHeld = false;
            foreach (Pointer p in _pointers)
            {
                if (layout.InKick(p.Position)) KickHeld = true;
                if (!p.Began) continue;

                if (layout.InKick(p.Position)) _kickRequested = true;
                else if (layout.RunButton.Contains(p.Position)) RunTapped = true;
                else if (layout.MathsButton.Contains(p.Position)) MathsTapped = true;
                else if (layout.ResetButton.Contains(p.Position)) ResetTapped = true;
                else if (_stickPointer == NoPointer && layout.StickZone.Contains(p.Position))
                {
                    _stickPointer = p.Id;
                    StickOrigin = p.Position;
                }
            }

            UpdateStick(layout.StickRadius);
        }

        /// <summary>
        /// The owned command for <paramref name="tick"/>. A kick press is delivered once, on the
        /// first tick generated after it, however many ticks a frame runs.
        /// </summary>
        public Command CommandFor(long tick)
        {
            var command = new Command(_controllerId, tick, new Vec2(Stick.x, Stick.y), _kickRequested);
            _kickRequested = false;
            return command;
        }

        /// <summary>Forgets held input, for example after a reset.</summary>
        public void Clear()
        {
            _stickPointer = NoPointer;
            _kickRequested = false;
            Stick = Vector2.zero;
        }

        private void UpdateStick(float radius)
        {
            if (_stickPointer == NoPointer) return;

            foreach (Pointer p in _pointers)
            {
                if (p.Id != _stickPointer) continue;

                Vector2 offset = p.Position - StickOrigin;
                Vector2 clamped = Vector2.ClampMagnitude(offset, radius);
                StickKnob = StickOrigin + clamped;
                // Screen y grows downwards here; the pitch's y grows up the screen.
                Stick = new Vector2(clamped.x, -clamped.y) / radius;
                return;
            }

            // The stick's finger lifted.
            _stickPointer = NoPointer;
            Stick = Vector2.zero;
        }

        private void CollectPointers(int screenHeight)
        {
            _pointers.Clear();

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (TouchControl touch in touchscreen.touches)
                {
                    if (!touch.press.isPressed) continue;
                    _touchSeen = true;
                    _pointers.Add(new Pointer(touch.touchId.ReadValue(), ToHud(touch.position.ReadValue(), screenHeight), touch.press.wasPressedThisFrame));
                }
            }

            Mouse mouse = Mouse.current;
            if (!_touchSeen && mouse != null && mouse.leftButton.isPressed)
            {
                _pointers.Add(new Pointer(MousePointer, ToHud(mouse.position.ReadValue(), screenHeight), mouse.leftButton.wasPressedThisFrame));
            }
        }

        // Input System positions have their origin at the bottom left; the HUD's is the top left.
        private static Vector2 ToHud(Vector2 position, int screenHeight) => new Vector2(position.x, screenHeight - position.y);

        private readonly struct Pointer
        {
            public readonly int Id;
            public readonly Vector2 Position;
            public readonly bool Began;

            public Pointer(int id, Vector2 position, bool began)
            {
                Id = id;
                Position = position;
                Began = began;
            }
        }
    }
}
