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

        /// <summary>The values the ACTION surface reads a flick with.</summary>
        public FlickValues Flick => new FlickValues(HoldSeconds, FlickDistance, FastFlickDistance, FastFlickSpeed, FlickStrength, ChargeSeconds, ChargedStrength, Loft);

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

        /// <summary>A re-touch that shapes the ball while a kick is winding up or its aftertouch window is open.</summary>
        Shaping,

        /// <summary>Candidate B's PASS (#76).</summary>
        Pass,

        /// <summary>A gesture on Candidate B's KICK (#76): a flick or hold → flick.</summary>
        Kick,
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

        /// <summary>The assisted quick pass, from Candidate B's PASS (#76).</summary>
        Pass,
    }

    /// <summary>A gesture's command as it was delivered, for the HUD and the trace. Not part of any command.</summary>
    public readonly struct ActionReport
    {
        public readonly ActionGesture Gesture;

        /// <summary>When the gesture was made, on the samples' clock: the lift, or B's PASS press.</summary>
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
    /// Candidate A, ACTION SURFACE (#28): one right-thumb surface. A tap asks the simulation for the
    /// footballer's quick pass, a flick for a manual kick along the gesture, kept exactly, a hold →
    /// flick for a harder one, and a re-touch while a kick winds up or its window is open shapes it.
    /// The stick, shaping, cancelling and the screen-to-world mapping are <see cref="GestureAdapter"/>'s.
    ///
    /// docs/simulation/test-bench.md spells out the gesture reading; <see cref="ActionSurfaceSettings"/> holds
    /// its values.
    /// </summary>
    public sealed class ActionSurfaceAdapter : GestureAdapter
    {
        /// <summary>See <see cref="FlickReader.SpeedWindowSeconds"/>.</summary>
        public const double SpeedWindowSeconds = FlickReader.SpeedWindowSeconds;

        private readonly FlickReader _flick = new FlickReader();
        private int _actionPointer = NoPointer;

        public ActionSurfaceAdapter(int controllerId, ActionSurfaceSettings settings)
            : base(controllerId)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public ActionSurfaceSettings Settings { get; private set; }

        /// <summary>Whether a finger is on the ACTION surface for a gesture.</summary>
        public override bool ActionHeld => _actionPointer != NoPointer;

        /// <summary>New settings; any gesture in progress is cancelled.</summary>
        public void Use(ActionSurfaceSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Cancel();
        }

        /// <summary>Where the controls are this frame, in screen pixels, and how many pixels make a point.</summary>
        public void SetLayout(TouchRegion stickZone, TouchRegion actionZone, double stickRadius, double pixelsPerPoint) =>
            SetLayout(new TouchLayout(stickZone, actionZone, stickRadius, pixelsPerPoint));

        public override double Charge(double now) => _actionPointer == NoPointer ? 0 : _flick.Charge(now, Settings.Flick);

        public override string Describe() => Settings.Describe();

        protected override double AftertouchRange => Settings.AftertouchRange;

        protected override TouchRole? HeldRole(int pointer) => pointer == _actionPointer ? TouchRole.Action : (TouchRole?)null;

        protected override IEnumerable<int> ControlPointers()
        {
            if (_actionPointer != NoPointer) yield return _actionPointer;
        }

        protected override void CancelControls()
        {
            _actionPointer = NoPointer;
            _flick.Reset();
        }

        protected override TouchRole BeginControl(in TouchSample sample)
        {
            if (!Layout.ActionZone.Contains(sample.X, sample.Y) || _actionPointer != NoPointer || ShapingHeld) return TouchRole.None;
            if (ShapingWanted) return StartShaping(sample);

            _actionPointer = sample.Pointer;
            _flick.Begin(sample);
            return TouchRole.Action;
        }

        protected override TouchRole FeedControl(in TouchSample sample)
        {
            if (sample.Pointer != _actionPointer) return TouchRole.None;
            if (sample.Phase == TouchPhase.Moved) _flick.Track(sample, Settings.Flick, Layout.PixelsPerPoint);
            else if (sample.Phase == TouchPhase.Ended) Release(sample);
            else _actionPointer = NoPointer; // the system took the touch: no command
            return TouchRole.Action;
        }

        private void Release(in TouchSample sample)
        {
            _actionPointer = NoPointer;
            FlickRelease release = _flick.Release(sample, Settings.Flick, Layout.PixelsPerPoint, out double strength, out Vec2 line);
            switch (release)
            {
                case FlickRelease.Tap:
                    // A tap goes at once, at release.
                    Pend(null, new ActionReport(ActionGesture.Tap, sample.Time, 0, Vec2.Zero));
                    break;
                case FlickRelease.Hold:
                    // A press held past the threshold without a flick is let go.
                    break;
                default:
                    // The gesture's line, kept exactly, in the world direction the screen shows it (#72).
                    Vec2 direction = Orientation.ToWorld(line);
                    Pend(new KickIntent(direction, strength, Settings.Loft),
                        new ActionReport(release == FlickRelease.HoldFlick ? ActionGesture.HoldFlick : ActionGesture.Flick, sample.Time, strength, direction));
                    break;
            }
        }
    }
}
