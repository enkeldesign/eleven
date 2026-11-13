// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Where a candidate's controls are this frame, in screen pixels with the origin at the top left,
    /// and how many pixels make a point. Each candidate reads what it uses: the stick zone always;
    /// Candidate A the action zone as its ACTION surface; Candidate B the PASS disc, and the action
    /// zone outside it as KICK (#76).
    /// </summary>
    public readonly struct TouchLayout
    {
        public readonly TouchRegion StickZone;
        public readonly TouchRegion ActionZone;
        public readonly double PassX;
        public readonly double PassY;
        public readonly double PassRadius;
        public readonly double StickRadius;
        public readonly double PixelsPerPoint;

        public TouchLayout(TouchRegion stickZone, TouchRegion actionZone, double stickRadius, double pixelsPerPoint,
            double passX = 0, double passY = 0, double passRadius = 0)
        {
            StickZone = stickZone;
            ActionZone = actionZone;
            StickRadius = stickRadius > 0 ? stickRadius : 1;
            PixelsPerPoint = pixelsPerPoint > 0 ? pixelsPerPoint : 1;
            PassX = passX;
            PassY = passY;
            PassRadius = passRadius;
        }

        /// <summary>Whether a point is on the PASS disc.</summary>
        public bool OnPass(double x, double y)
        {
            double dx = x - PassX, dy = y - PassY;
            return PassRadius > 0 && dx * dx + dy * dy <= PassRadius * PassRadius;
        }
    }

    /// <summary>
    /// What every touch candidate shares (#28): the floating stick, the re-touch that shapes a kick,
    /// a gesture's command waiting for its tick, cancelling, and the mapping from screen to world
    /// (#72). Engine-free, so .NET tests read the same samples the same way every runtime does. It
    /// turns raw pointer samples into owned commands for one controller (INV-10), and never sees
    /// teammates or picks a receiver.
    ///
    /// A pointer's role is fixed where it begins, and each control belongs to its first finger; a
    /// second finger on it is ignored. Nothing depends on the order fingers land in, only on where
    /// each began. A candidate decides what a pointer that begins outside the stick zone is.
    /// </summary>
    public abstract class GestureAdapter
    {
        protected const int NoPointer = int.MinValue;

        private readonly int _controllerId;
        private readonly List<int> _ignored = new List<int>(4);

        private int _stickPointer = NoPointer;
        private Vec2 _move;

        private int _shapingPointer = NoPointer;
        private double _shapingOriginX;
        private double _shapingOriginY;
        private Vec2 _aftertouch;

        private bool _pending;
        private KickIntent? _pendingKick;
        private ActionReport _pendingReport;

        protected GestureAdapter(int controllerId)
        {
            _controllerId = controllerId;
        }

        /// <summary>Where the controls are, as last set.</summary>
        protected TouchLayout Layout { get; private set; } = new TouchLayout(default, default, 1, 1);

        /// <summary>
        /// How the pitch is shown (#72). Gestures are read in screen space, and the stick, a flick's
        /// line and aftertouch become world directions through it before a command is made, so the
        /// commands recorded are in world coordinates whatever the view (INV-10).
        /// </summary>
        public PitchOrientation Orientation { get; private set; } = PitchOrientation.Practice;

        /// <summary>
        /// Whether the controlled player's kick is winding up or its aftertouch window is open, read
        /// from the state each frame: a press where a kick is made then shapes the ball instead.
        /// </summary>
        public bool ShapingOpen { get; set; }

        /// <summary>Raised when <see cref="CommandFor"/> delivers a gesture's command.</summary>
        public event Action<ActionReport>? Delivered;

        public bool StickActive => _stickPointer != NoPointer;
        public double StickOriginX { get; private set; }
        public double StickOriginY { get; private set; }
        public double StickKnobX { get; private set; }
        public double StickKnobY { get; private set; }

        /// <summary>The stick's move vector on screen, length at most 1: +x right, +y up the screen.</summary>
        public Vec2 Move => _move;

        /// <summary>Whether a re-touch is shaping the ball.</summary>
        public bool Shaping => _shapingPointer != NoPointer;

        /// <summary>Whether a finger is on the control that makes a kick, for a gesture.</summary>
        public abstract bool ActionHeld { get; }

        /// <summary>How far a hold has charged at <paramref name="now"/>: 0 before the hold threshold, 1 at full charge.</summary>
        public abstract double Charge(double now);

        /// <summary>The candidate and every value, for a capture: enough to explain how raw touch became its owned commands.</summary>
        public abstract string Describe();

        /// <summary>A new view, such as a half-time flip: every gesture in progress is cancelled, as on a reset.</summary>
        public void Orient(PitchOrientation orientation)
        {
            Orientation = orientation;
            Cancel();
        }

        /// <summary>Where the controls are this frame.</summary>
        public void SetLayout(in TouchLayout layout) => Layout = layout;

        /// <summary>One sample, in arrival order. Returns the role a beginning pointer was given, or the role it has.</summary>
        public TouchRole Feed(in TouchSample sample)
        {
            if (sample.Phase == TouchPhase.Began) return Begin(sample);

            if (_ignored.Contains(sample.Pointer))
            {
                if (sample.Phase != TouchPhase.Moved) _ignored.Remove(sample.Pointer);
                return TouchRole.None;
            }

            bool ends = sample.Phase != TouchPhase.Moved;
            if (sample.Pointer == _stickPointer)
            {
                if (ends) ReleaseStick();
                else MoveStick(sample);
                return TouchRole.Stick;
            }

            if (sample.Pointer == _shapingPointer)
            {
                if (ends)
                {
                    // Lifting a re-touch is never a tap.
                    _shapingPointer = NoPointer;
                    _aftertouch = Vec2.Zero;
                }
                else _aftertouch = AftertouchAt(sample);

                return TouchRole.Shaping;
            }

            return FeedControl(sample);
        }

        /// <summary>The owned command for <paramref name="tick"/>. A gesture's kick or pass is delivered once.</summary>
        public Command CommandFor(long tick)
        {
            // Read on screen, sent in the world: what the screen shows up is up-field (#72).
            Vec2 move = Orientation.ToWorld(_move);
            Vec2 aftertouch = _shapingPointer != NoPointer ? Orientation.ToWorld(_aftertouch) : Vec2.Zero;
            if (!_pending) return new Command(_controllerId, tick, move, null, aftertouch);

            var command = new Command(_controllerId, tick, move, _pendingKick, aftertouch, quickPass: !_pendingKick.HasValue);
            ActionReport report = _pendingReport;
            _pending = false;
            _pendingKick = null;
            Delivered?.Invoke(report);
            return command;
        }

        /// <summary>
        /// Forgets every gesture in progress, for a reset, a retry, new settings or a switch of
        /// candidate: nothing pending is delivered, and fingers already down are ignored until they
        /// lift, so none can end as a phantom kick, pass or aftertouch.
        /// </summary>
        public void Cancel()
        {
            Ignore(_stickPointer);
            Ignore(_shapingPointer);
            foreach (int pointer in ControlPointers()) Ignore(pointer);
            _stickPointer = NoPointer;
            _shapingPointer = NoPointer;
            _move = Vec2.Zero;
            _aftertouch = Vec2.Zero;
            _pending = false;
            _pendingKick = null;
            CancelControls();
        }

        /// <summary>Every finger this adapter holds or ignores: what another candidate must ignore when it takes over.</summary>
        public IEnumerable<int> HeldPointers()
        {
            if (_stickPointer != NoPointer) yield return _stickPointer;
            if (_shapingPointer != NoPointer) yield return _shapingPointer;
            foreach (int pointer in ControlPointers()) yield return pointer;
            foreach (int pointer in _ignored) yield return pointer;
        }

        /// <summary>Fingers already down when this adapter takes over: ignored until they lift.</summary>
        public void IgnoreUntilLifted(IEnumerable<int> pointers)
        {
            foreach (int pointer in pointers) Ignore(pointer);
        }

        /// <summary>
        /// A finger lifted while another candidate was active: no longer ignored here, so a pointer
        /// id the platform reuses (the desktop mouse's) starts a fresh gesture when this candidate
        /// is active again (Codex on #79).
        /// </summary>
        public void Lifted(int pointer) => _ignored.Remove(pointer);

        /// <summary>A pointer that begins outside the stick zone and isn't already held: the candidate gives it a role.</summary>
        protected abstract TouchRole BeginControl(in TouchSample sample);

        /// <summary>The role of a finger one of the candidate's controls already holds, reported pressed again; null if none holds it.</summary>
        protected abstract TouchRole? HeldRole(int pointer);

        /// <summary>A later sample of a pointer the stick and shaping don't hold.</summary>
        protected abstract TouchRole FeedControl(in TouchSample sample);

        /// <summary>The pointers the candidate's own controls hold.</summary>
        protected abstract IEnumerable<int> ControlPointers();

        /// <summary>Forget the candidate's own controls' gestures; the base has already ignored their pointers.</summary>
        protected abstract void CancelControls();

        /// <summary>Whether a press here would shape: a kick is winding up, its window is open, or a gesture waits for its tick.</summary>
        protected bool ShapingWanted => ShapingOpen || _pending;

        /// <summary>Whether a re-touch already holds shaping.</summary>
        protected bool ShapingHeld => _shapingPointer != NoPointer;

        /// <summary>Whether a gesture's command waits for its tick.</summary>
        protected bool Pending => _pending;

        /// <summary>Start shaping from this press.</summary>
        protected TouchRole StartShaping(in TouchSample sample)
        {
            _shapingPointer = sample.Pointer;
            _shapingOriginX = sample.X;
            _shapingOriginY = sample.Y;
            _aftertouch = Vec2.Zero;
            return TouchRole.Shaping;
        }

        /// <summary>
        /// A gesture's command, for the next tick. At most one waits: a second, in the same tick, is
        /// dropped, so nothing already made is overwritten.
        /// </summary>
        protected void Pend(KickIntent? kick, in ActionReport report)
        {
            if (_pending) return;
            _pending = true;
            _pendingKick = kick;
            _pendingReport = report;
        }

        private TouchRole Begin(in TouchSample sample)
        {
            // A finger already down that is reported pressed again (Unity does whenever its phase
            // changes) keeps its role and its press, and one held across a cancel stays ignored
            // until it lifts. Platforms give every touch a fresh id, so a repeated id is the same
            // finger.
            if (sample.Pointer == _stickPointer) return TouchRole.Stick;
            TouchRole? held = HeldRole(sample.Pointer);
            if (held.HasValue) return held.Value;
            if (sample.Pointer == _shapingPointer) return TouchRole.Shaping;
            if (_ignored.Contains(sample.Pointer)) return TouchRole.None;

            if (Layout.StickZone.Contains(sample.X, sample.Y))
            {
                if (_stickPointer != NoPointer) return TouchRole.None;
                _stickPointer = sample.Pointer;
                StickOriginX = StickKnobX = sample.X;
                StickOriginY = StickKnobY = sample.Y;
                _move = Vec2.Zero;
                return TouchRole.Stick;
            }

            return BeginControl(sample);
        }

        private void MoveStick(in TouchSample sample)
        {
            double radius = Layout.StickRadius;
            double dx = sample.X - StickOriginX;
            double dy = sample.Y - StickOriginY;
            double length = SimMath.Sqrt(dx * dx + dy * dy);
            if (length > radius)
            {
                dx *= radius / length;
                dy *= radius / length;
            }

            StickKnobX = StickOriginX + dx;
            StickKnobY = StickOriginY + dy;
            // Screen y grows downwards; the pitch's y grows up the screen.
            _move = new Vec2(dx / radius, (StickOriginY - StickKnobY) / radius);
        }

        private void ReleaseStick()
        {
            _stickPointer = NoPointer;
            _move = Vec2.Zero;
        }

        // Full aftertouch a sensitivity's drag from the re-touch, at most full.
        private Vec2 AftertouchAt(in TouchSample sample)
        {
            double range = AftertouchRange * Layout.PixelsPerPoint;
            double x = (sample.X - _shapingOriginX) / range;
            double y = (_shapingOriginY - sample.Y) / range;
            double length = SimMath.Sqrt(x * x + y * y);
            return length > 1 ? new Vec2(x / length, y / length) : new Vec2(x, y);
        }

        /// <summary>Points of drag from the re-touch that give full aftertouch.</summary>
        protected abstract double AftertouchRange { get; }

        private void Ignore(int pointer)
        {
            if (pointer != NoPointer && !_ignored.Contains(pointer)) _ignored.Add(pointer);
        }
    }

    /// <summary>
    /// The values a flick is read with: A's ACTION surface and B's KICK read a press, a hold and a
    /// flick the same way, each with its own knobs.
    /// </summary>
    public readonly struct FlickValues
    {
        public readonly double HoldSeconds;
        public readonly double FlickDistance;
        public readonly double FastFlickDistance;
        public readonly double FastFlickSpeed;
        public readonly double FlickStrength;
        public readonly double ChargeSeconds;
        public readonly double ChargedStrength;
        public readonly double Loft;

        public FlickValues(double holdSeconds, double flickDistance, double fastFlickDistance, double fastFlickSpeed,
            double flickStrength, double chargeSeconds, double chargedStrength, double loft)
        {
            HoldSeconds = holdSeconds;
            FlickDistance = flickDistance;
            FastFlickDistance = fastFlickDistance;
            FastFlickSpeed = fastFlickSpeed;
            FlickStrength = flickStrength;
            ChargeSeconds = chargeSeconds;
            ChargedStrength = chargedStrength;
            Loft = loft;
        }
    }

    /// <summary>What a released press was.</summary>
    public enum FlickRelease
    {
        /// <summary>Released before the hold threshold, without a flick.</summary>
        Tap,

        /// <summary>Held past the hold threshold and released without a flick.</summary>
        Hold,

        /// <summary>A flick, immediate.</summary>
        Flick,

        /// <summary>A flick after a hold, charged.</summary>
        HoldFlick,
    }

    /// <summary>
    /// One press read as a tap, a hold or a flick (#28): travel from the press makes a flick, or a
    /// shorter fast one; the time from the press to where the travel first reached a flick's is the
    /// hold, which charges the kick past the hold threshold.
    /// </summary>
    public sealed class FlickReader
    {
        /// <summary>
        /// Release speed is measured over at least this long before the finger lifts, so one jittery
        /// last sample can't make a slow drag fast. A technical constant, not a feel value.
        /// </summary>
        public const double SpeedWindowSeconds = 0.03;

        private readonly List<TouchSample> _recent = new List<TouchSample>(16);
        private TouchSample _press;
        private double? _flickStart;

        public TouchSample Press => _press;

        public void Begin(in TouchSample sample)
        {
            _press = sample;
            _flickStart = null;
            _recent.Clear();
            _recent.Add(sample);
        }

        public void Reset()
        {
            _flickStart = null;
            _recent.Clear();
        }

        /// <summary>Points from the press, and when the travel first reached a flick's: where a hold ends.</summary>
        public void Track(in TouchSample sample, in FlickValues values, double pixelsPerPoint)
        {
            if (!_flickStart.HasValue && Travel(sample, pixelsPerPoint) >= values.FastFlickDistance) _flickStart = sample.Time;

            // Keep the newest sample at least the speed window old, and everything after it.
            _recent.Add(sample);
            while (_recent.Count >= 2 && _recent[1].Time <= sample.Time - SpeedWindowSeconds) _recent.RemoveAt(0);
        }

        /// <summary>
        /// What the press was at its release, and for a flick its strength and its line on screen
        /// (+x right, +y up the screen).
        /// </summary>
        public FlickRelease Release(in TouchSample sample, in FlickValues values, double pixelsPerPoint, out double strength, out Vec2 screenLine)
        {
            double distance = Travel(sample, pixelsPerPoint);
            bool flick = distance >= values.FlickDistance || (distance >= values.FastFlickDistance && ReleaseSpeed(sample, pixelsPerPoint) >= values.FastFlickSpeed);
            screenLine = new Vec2(sample.X - _press.X, _press.Y - sample.Y);
            if (!flick)
            {
                strength = 0;
                return sample.Time - _press.Time < values.HoldSeconds ? FlickRelease.Tap : FlickRelease.Hold;
            }

            double hold = (_flickStart ?? sample.Time) - _press.Time;
            bool charged = hold >= values.HoldSeconds;
            strength = charged ? Lerp(values.FlickStrength, values.ChargedStrength, ChargeFor(hold, values)) : values.FlickStrength;
            return charged ? FlickRelease.HoldFlick : FlickRelease.Flick;
        }

        /// <summary>How far a hold has charged at <paramref name="now"/>.</summary>
        public double Charge(double now, in FlickValues values) => ChargeFor((_flickStart ?? now) - _press.Time, values);

        // 0 at the hold threshold, 1 a charge time later.
        private static double ChargeFor(double hold, in FlickValues values)
        {
            double c = (hold - values.HoldSeconds) / values.ChargeSeconds;
            return c <= 0 ? 0 : c >= 1 ? 1 : c;
        }

        private static double Lerp(double from, double to, double t) => t >= 1 ? to : from + (to - from) * t;

        private double Travel(in TouchSample sample, double pixelsPerPoint)
        {
            double dx = sample.X - _press.X;
            double dy = sample.Y - _press.Y;
            return SimMath.Sqrt(dx * dx + dy * dy) / pixelsPerPoint;
        }

        // Points per second over the last stretch: from the newest sample at least the speed window
        // before the release, or from the press if the gesture is shorter than that.
        private double ReleaseSpeed(in TouchSample release, double pixelsPerPoint)
        {
            TouchSample from = _press;
            foreach (TouchSample s in _recent)
            {
                if (s.Time <= release.Time - SpeedWindowSeconds) from = s;
            }

            double dx = release.X - from.X;
            double dy = release.Y - from.Y;
            double distance = SimMath.Sqrt(dx * dx + dy * dy) / pixelsPerPoint;
            double seconds = release.Time - from.Time;
            if (seconds <= 0) return distance > 0 ? double.PositiveInfinity : 0;
            return distance / seconds;
        }
    }
}
