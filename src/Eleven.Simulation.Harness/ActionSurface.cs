// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Candidate A's gesture parameters (#28, ACTION SURFACE): how the right thumb's surface reads a
    /// touch. They are adapter values, not simulation config. They decide which owned command a
    /// gesture becomes, never how the football behaves, so no config or state hash depends on them.
    /// The values are research-informed technical starting points, not approved feel.
    ///
    /// The aftertouch window's length is the simulation's own (<c>aftertouchSeconds</c>); there is
    /// no second duration here.
    /// </summary>
    public sealed class ActionSurfaceSettings
    {
        /// <summary>The bench group these knobs show in.</summary>
        public const string Group = "Touch";

        /// <summary>The adapter's name in captures and command traces.</summary>
        public const string Candidate = "action-surface";

        /// <summary>Bumped whenever the gesture interpretation changes.</summary>
        public const int Version = 1;

        /// <summary>
        /// Every parameter, with its unit and valid range, and the starting range #28 records where
        /// it gives one. Distances are in points (CSS pixels on the web).
        /// </summary>
        public static readonly ReadOnlyCollection<TunableParameter> Parameters = Array.AsReadOnly(new[]
        {
            new TunableParameter(Group, "actionHoldSeconds", "s", 0.05, 0.6, 0.12, 0.25),
            new TunableParameter(Group, "actionFlickDistance", "pt", 4, 80, 12, 32),
            new TunableParameter(Group, "actionFastFlickDistance", "pt", 2, 40),
            new TunableParameter(Group, "actionFastFlickSpeed", "pt/s", 50, 3000),
            new TunableParameter(Group, "actionFlickStrength", "fraction of kick power", 0, 1),
            new TunableParameter(Group, "actionChargeSeconds", "s", 0.05, 2),
            new TunableParameter(Group, "actionChargedStrength", "fraction of kick power", 0, 1),
            new TunableParameter(Group, "actionLoft", "fraction of kickLoft", 0, 1),
            new TunableParameter(Group, "actionAftertouchRange", "pt", 10, 200),
        });

        /// <summary>
        /// The technical starting values. The hold threshold and flick distance sit inside #28's
        /// research ranges; the rest are technical references with no research range yet. The loft
        /// is the spike's kick elevation (0.45 rad of the default 1.05).
        /// </summary>
        public static readonly ActionSurfaceSettings Reference = new ActionSurfaceSettings(
            holdSeconds: 0.18, flickDistance: 20, fastFlickDistance: 8, fastFlickSpeed: 400, flickStrength: 0.6,
            chargeSeconds: 0.5, chargedStrength: 1.0, loft: 0.45 / 1.05, aftertouchRange: 40);

        private ActionSurfaceSettings(double holdSeconds, double flickDistance, double fastFlickDistance, double fastFlickSpeed,
            double flickStrength, double chargeSeconds, double chargedStrength, double loft, double aftertouchRange)
        {
            HoldSeconds = holdSeconds;
            FlickDistance = flickDistance;
            FastFlickDistance = fastFlickDistance;
            FastFlickSpeed = fastFlickSpeed;
            FlickStrength = flickStrength;
            ChargeSeconds = chargeSeconds;
            ChargedStrength = chargedStrength;
            Loft = loft;
            AftertouchRange = aftertouchRange;
        }

        /// <summary>A press released before this, without a flick, is a tap. Held this long, a flick becomes a charged one.</summary>
        public double HoldSeconds { get; }

        /// <summary>Travel from the press that makes a flick, however slow.</summary>
        public double FlickDistance { get; }

        /// <summary>The shortest travel that makes a flick when it is fast; also where a hold ends and a flick begins.</summary>
        public double FastFlickDistance { get; }

        /// <summary>Release speed at which a flick of <see cref="FastFlickDistance"/> or more counts.</summary>
        public double FastFlickSpeed { get; }

        /// <summary>Requested strength of an immediate flick.</summary>
        public double FlickStrength { get; }

        /// <summary>Hold beyond <see cref="HoldSeconds"/> that reaches <see cref="ChargedStrength"/>.</summary>
        public double ChargeSeconds { get; }

        /// <summary>Requested strength of a fully charged flick.</summary>
        public double ChargedStrength { get; }

        /// <summary>Loft of a flicked kick.</summary>
        public double Loft { get; }

        /// <summary>Drag from the re-touch that gives full aftertouch: its sensitivity.</summary>
        public double AftertouchRange { get; }

        /// <summary>A value, by parameter name.</summary>
        public double Get(string name) => name switch
        {
            "actionHoldSeconds" => HoldSeconds,
            "actionFlickDistance" => FlickDistance,
            "actionFastFlickDistance" => FastFlickDistance,
            "actionFastFlickSpeed" => FastFlickSpeed,
            "actionFlickStrength" => FlickStrength,
            "actionChargeSeconds" => ChargeSeconds,
            "actionChargedStrength" => ChargedStrength,
            "actionLoft" => Loft,
            "actionAftertouchRange" => AftertouchRange,
            _ => throw new ArgumentException($"'{name}' is not an action surface parameter.", nameof(name)),
        };

        /// <summary>These settings with one value changed. Refused, naming the range, outside its valid range.</summary>
        public ActionSurfaceSettings With(string name, double value)
        {
            TunableParameter? p = null;
            foreach (TunableParameter candidate in Parameters)
            {
                if (candidate.Name == name) p = candidate;
            }

            if (p == null) throw new ArgumentException($"'{name}' is not an action surface parameter.", nameof(name));
            if (double.IsNaN(value) || double.IsInfinity(value) || value < p.Min || value > p.Max)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0} must be within {1:G6}–{2:G6} {3}; got {4:G6}.", name, p.Min, p.Max, p.Unit, value), nameof(value));
            }

            double V(string n) => n == name ? value : Get(n);
            return new ActionSurfaceSettings(V("actionHoldSeconds"), V("actionFlickDistance"), V("actionFastFlickDistance"), V("actionFastFlickSpeed"),
                V("actionFlickStrength"), V("actionChargeSeconds"), V("actionChargedStrength"), V("actionLoft"), V("actionAftertouchRange"));
        }

        /// <summary>
        /// The adapter and every value, for a capture's control candidate: enough to explain how raw
        /// touch became its owned commands. Values round-trip ("R").
        /// </summary>
        public string Describe()
        {
            var text = new StringBuilder($"{Candidate}/v{Version}");
            foreach (TunableParameter p in Parameters) text.Append(' ').Append(p.Name).Append('=').Append(Get(p.Name).ToString("R", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    /// <summary>Where a touch sample is in its pointer's life.</summary>
    public enum TouchPhase
    {
        Began,
        Moved,
        Ended,
        Canceled,
    }

    /// <summary>One raw pointer sample: screen pixels with the origin at the top left, and seconds on the host's steady clock.</summary>
    public readonly struct TouchSample
    {
        public readonly int Pointer;
        public readonly TouchPhase Phase;
        public readonly double X;
        public readonly double Y;
        public readonly double Time;

        public TouchSample(int pointer, TouchPhase phase, double x, double y, double time)
        {
            Pointer = pointer;
            Phase = phase;
            X = x;
            Y = y;
            Time = time;
        }
    }

    /// <summary>A screen rectangle in pixels, origin at the top left.</summary>
    public readonly struct TouchRegion
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Width;
        public readonly double Height;

        public TouchRegion(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    }

    /// <summary>What a pointer was given when it began.</summary>
    public enum TouchRole
    {
        /// <summary>Not a control's: outside both, or on a control another finger already holds.</summary>
        None,

        /// <summary>The movement stick.</summary>
        Stick,

        /// <summary>A gesture on the ACTION surface: tap, flick or hold → flick.</summary>
        Action,

        /// <summary>A re-touch on the ACTION surface while a kick is winding up or its aftertouch window is open.</summary>
        Shaping,
    }

    /// <summary>What a gesture on the ACTION surface became.</summary>
    public enum ActionGesture
    {
        None,

        /// <summary>The assisted quick pass.</summary>
        Tap,

        /// <summary>A manual kick at the immediate flick strength.</summary>
        Flick,

        /// <summary>A manual kick charged by the hold.</summary>
        HoldFlick,
    }

    /// <summary>A gesture's command as it was delivered, for the HUD and the trace. Not part of any command.</summary>
    public readonly struct ActionReport
    {
        public readonly ActionGesture Gesture;

        /// <summary>When the finger lifted, on the samples' clock.</summary>
        public readonly double ReleaseTime;

        public readonly double Strength;
        public readonly Vec2 Direction;

        public ActionReport(ActionGesture gesture, double releaseTime, double strength, Vec2 direction)
        {
            Gesture = gesture;
            ReleaseTime = releaseTime;
            Strength = strength;
            Direction = direction;
        }
    }

    /// <summary>
    /// Candidate A, ACTION SURFACE (#28): the touch adapter's gesture reading, engine-free so .NET
    /// tests it and every runtime reads the same samples the same way. It turns raw pointer samples
    /// into owned commands for one controller (INV-10). It never sees teammates or picks a
    /// receiver: a tap asks the simulation for the footballer's quick pass, and a flick asks for a
    /// manual kick along the gesture, kept exactly.
    ///
    /// A pointer's role is fixed where it begins: in the stick zone it is the stick, on the ACTION
    /// surface a gesture, or shaping while a kick is winding up, its aftertouch window is open or a
    /// gesture waits for its tick. Each control belongs to its first finger; a second finger on it
    /// is ignored. Nothing depends on the order fingers land in, only on where each began.
    ///
    /// docs/simulation/test-bench.md spells out the gesture reading; <see cref="ActionSurfaceSettings"/> holds
    /// its values.
    /// </summary>
    public sealed class ActionSurfaceAdapter
    {
        /// <summary>
        /// Release speed is measured over at least this long before the finger lifts, so one jittery
        /// last sample can't make a slow drag fast. A technical constant, not a feel value.
        /// </summary>
        public const double SpeedWindowSeconds = 0.03;

        private const int NoPointer = int.MinValue;

        private readonly int _controllerId;
        private readonly List<int> _ignored = new List<int>(4);
        private readonly List<TouchSample> _recent = new List<TouchSample>(16);

        private TouchRegion _stickZone;
        private TouchRegion _actionZone;
        private double _stickRadius = 1;
        private double _pixelsPerPoint = 1;

        private int _stickPointer = NoPointer;
        private Vec2 _move;

        private int _actionPointer = NoPointer;
        private TouchSample _press;
        private double? _flickStart;

        private int _shapingPointer = NoPointer;
        private double _shapingOriginX;
        private double _shapingOriginY;
        private Vec2 _aftertouch;

        private bool _pending;
        private KickIntent? _pendingKick;
        private ActionReport _pendingReport;

        public ActionSurfaceAdapter(int controllerId, ActionSurfaceSettings settings)
        {
            _controllerId = controllerId;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public ActionSurfaceSettings Settings { get; private set; }

        /// <summary>
        /// Whether the controlled player's kick is winding up or its aftertouch window is open, read
        /// from the state each frame: a press on the ACTION surface then shapes the ball instead of
        /// starting a gesture.
        /// </summary>
        public bool ShapingOpen { get; set; }

        /// <summary>Raised when <see cref="CommandFor"/> delivers a gesture's command.</summary>
        public event Action<ActionReport>? Delivered;

        public bool StickActive => _stickPointer != NoPointer;
        public double StickOriginX { get; private set; }
        public double StickOriginY { get; private set; }
        public double StickKnobX { get; private set; }
        public double StickKnobY { get; private set; }

        /// <summary>Move vector, length at most 1; +x right, +y up the screen.</summary>
        public Vec2 Move => _move;

        /// <summary>Whether a finger is on the ACTION surface for a gesture.</summary>
        public bool ActionHeld => _actionPointer != NoPointer;

        /// <summary>Whether a re-touch on the ACTION surface is shaping the ball.</summary>
        public bool Shaping => _shapingPointer != NoPointer;

        /// <summary>New settings; any gesture in progress is cancelled.</summary>
        public void Use(ActionSurfaceSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Cancel();
        }

        /// <summary>Where the controls are this frame, in screen pixels, and how many pixels make a point.</summary>
        public void SetLayout(TouchRegion stickZone, TouchRegion actionZone, double stickRadius, double pixelsPerPoint)
        {
            _stickZone = stickZone;
            _actionZone = actionZone;
            _stickRadius = stickRadius > 0 ? stickRadius : 1;
            _pixelsPerPoint = pixelsPerPoint > 0 ? pixelsPerPoint : 1;
        }

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

            if (sample.Pointer == _actionPointer)
            {
                if (sample.Phase == TouchPhase.Moved) Track(sample);
                else if (sample.Phase == TouchPhase.Ended) Release(sample);
                else _actionPointer = NoPointer; // the system took the touch: no command
                return TouchRole.Action;
            }

            return TouchRole.None;
        }

        /// <summary>How far a hold has charged at <paramref name="now"/>: 0 before the hold threshold, 1 at full charge.</summary>
        public double Charge(double now) => _actionPointer == NoPointer ? 0 : ChargeFor((_flickStart ?? now) - _press.Time);

        /// <summary>The owned command for <paramref name="tick"/>. A gesture's kick or pass is delivered once.</summary>
        public Command CommandFor(long tick)
        {
            Vec2 aftertouch = _shapingPointer != NoPointer ? _aftertouch : Vec2.Zero;
            if (!_pending) return new Command(_controllerId, tick, _move, null, aftertouch);

            var command = new Command(_controllerId, tick, _move, _pendingKick, aftertouch, quickPass: !_pendingKick.HasValue);
            ActionReport report = _pendingReport;
            _pending = false;
            _pendingKick = null;
            Delivered?.Invoke(report);
            return command;
        }

        /// <summary>
        /// Forgets every gesture in progress, for a reset, a retry or new settings: nothing pending
        /// is delivered, and fingers already down are ignored until they lift, so none can end as a
        /// phantom kick, pass or aftertouch.
        /// </summary>
        public void Cancel()
        {
            Ignore(_stickPointer);
            Ignore(_actionPointer);
            Ignore(_shapingPointer);
            _stickPointer = NoPointer;
            _actionPointer = NoPointer;
            _shapingPointer = NoPointer;
            _move = Vec2.Zero;
            _aftertouch = Vec2.Zero;
            _flickStart = null;
            _recent.Clear();
            _pending = false;
            _pendingKick = null;
        }

        private TouchRole Begin(in TouchSample sample)
        {
            // An id the platform reuses after a lift we never saw is a new finger.
            _ignored.Remove(sample.Pointer);

            if (_stickZone.Contains(sample.X, sample.Y))
            {
                if (_stickPointer != NoPointer) return TouchRole.None;
                _stickPointer = sample.Pointer;
                StickOriginX = StickKnobX = sample.X;
                StickOriginY = StickKnobY = sample.Y;
                _move = Vec2.Zero;
                return TouchRole.Stick;
            }

            if (!_actionZone.Contains(sample.X, sample.Y) || _actionPointer != NoPointer || _shapingPointer != NoPointer) return TouchRole.None;

            if (ShapingOpen || _pending)
            {
                _shapingPointer = sample.Pointer;
                _shapingOriginX = sample.X;
                _shapingOriginY = sample.Y;
                _aftertouch = Vec2.Zero;
                return TouchRole.Shaping;
            }

            _actionPointer = sample.Pointer;
            _press = sample;
            _flickStart = null;
            _recent.Clear();
            _recent.Add(sample);
            return TouchRole.Action;
        }

        private void MoveStick(in TouchSample sample)
        {
            double dx = sample.X - StickOriginX;
            double dy = sample.Y - StickOriginY;
            double length = SimMath.Sqrt(dx * dx + dy * dy);
            if (length > _stickRadius)
            {
                dx *= _stickRadius / length;
                dy *= _stickRadius / length;
            }

            StickKnobX = StickOriginX + dx;
            StickKnobY = StickOriginY + dy;
            // Screen y grows downwards; the pitch's y grows up the screen.
            _move = new Vec2(dx / _stickRadius, (StickOriginY - StickKnobY) / _stickRadius);
        }

        private void ReleaseStick()
        {
            _stickPointer = NoPointer;
            _move = Vec2.Zero;
        }

        // Points from the press, and when the travel first reached a flick's: where a hold ends.
        private void Track(in TouchSample sample)
        {
            if (!_flickStart.HasValue && Travel(sample) >= Settings.FastFlickDistance) _flickStart = sample.Time;

            // Keep the newest sample at least the speed window old, and everything after it.
            _recent.Add(sample);
            while (_recent.Count >= 2 && _recent[1].Time <= sample.Time - SpeedWindowSeconds) _recent.RemoveAt(0);
        }

        private void Release(in TouchSample sample)
        {
            _actionPointer = NoPointer;
            ActionSurfaceSettings s = Settings;
            double distance = Travel(sample);
            bool flick = distance >= s.FlickDistance || (distance >= s.FastFlickDistance && ReleaseSpeed(sample) >= s.FastFlickSpeed);

            if (!flick)
            {
                // A tap goes at once, at release. A press held past the threshold without a flick is let go.
                if (sample.Time - _press.Time < s.HoldSeconds) Pend(null, new ActionReport(ActionGesture.Tap, sample.Time, 0, Vec2.Zero));
                return;
            }

            double hold = (_flickStart ?? sample.Time) - _press.Time;
            bool charged = hold >= s.HoldSeconds;
            double strength = charged ? Lerp(s.FlickStrength, s.ChargedStrength, ChargeFor(hold)) : s.FlickStrength;
            // The gesture's line, kept exactly: screen right is pitch +x, screen up pitch +y.
            var direction = new Vec2(sample.X - _press.X, _press.Y - sample.Y);
            Pend(new KickIntent(direction, strength, s.Loft), new ActionReport(charged ? ActionGesture.HoldFlick : ActionGesture.Flick, sample.Time, strength, direction));
        }

        private void Pend(KickIntent? kick, in ActionReport report)
        {
            _pending = true;
            _pendingKick = kick;
            _pendingReport = report;
        }

        // 0 at the hold threshold, 1 a charge time later.
        private double ChargeFor(double hold)
        {
            double c = (hold - Settings.HoldSeconds) / Settings.ChargeSeconds;
            return c <= 0 ? 0 : c >= 1 ? 1 : c;
        }

        private static double Lerp(double from, double to, double t) => t >= 1 ? to : from + (to - from) * t;

        private double Travel(in TouchSample sample)
        {
            double dx = sample.X - _press.X;
            double dy = sample.Y - _press.Y;
            return SimMath.Sqrt(dx * dx + dy * dy) / _pixelsPerPoint;
        }

        // Points per second over the last stretch: from the newest sample at least the speed window
        // before the release, or from the press if the gesture is shorter than that.
        private double ReleaseSpeed(in TouchSample release)
        {
            TouchSample from = _press;
            foreach (TouchSample s in _recent)
            {
                if (s.Time <= release.Time - SpeedWindowSeconds) from = s;
            }

            double dx = release.X - from.X;
            double dy = release.Y - from.Y;
            double distance = SimMath.Sqrt(dx * dx + dy * dy) / _pixelsPerPoint;
            double seconds = release.Time - from.Time;
            if (seconds <= 0) return distance > 0 ? double.PositiveInfinity : 0;
            return distance / seconds;
        }

        // Full aftertouch a sensitivity's drag from the re-touch, at most full.
        private Vec2 AftertouchAt(in TouchSample sample)
        {
            double range = Settings.AftertouchRange * _pixelsPerPoint;
            double x = (sample.X - _shapingOriginX) / range;
            double y = (_shapingOriginY - sample.Y) / range;
            double length = SimMath.Sqrt(x * x + y * y);
            return length > 1 ? new Vec2(x / length, y / length) : new Vec2(x, y);
        }

        private void Ignore(int pointer)
        {
            if (pointer != NoPointer && !_ignored.Contains(pointer)) _ignored.Add(pointer);
        }
    }
}
