// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

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
    /// </summary>
    public sealed class ActionSurfaceAdapter
    {
        private readonly int _controllerId;

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
#pragma warning disable CS0067 // Stub: raised once the adapter reads gestures.
        public event Action<ActionReport>? Delivered;
#pragma warning restore CS0067

        public bool StickActive => false;
        public double StickOriginX => 0;
        public double StickOriginY => 0;
        public double StickKnobX => 0;
        public double StickKnobY => 0;

        /// <summary>Move vector, length at most 1; +x right, +y up the screen.</summary>
        public Vec2 Move => Vec2.Zero;

        /// <summary>Whether a finger is on the ACTION surface for a gesture.</summary>
        public bool ActionHeld => false;

        /// <summary>Whether a re-touch on the ACTION surface is shaping the ball.</summary>
        public bool Shaping => false;

        /// <summary>New settings; any gesture in progress is cancelled.</summary>
        public void Use(ActionSurfaceSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Cancel();
        }

        /// <summary>Where the controls are this frame, in screen pixels, and how many pixels make a point.</summary>
        public void SetLayout(TouchRegion stickZone, TouchRegion actionZone, double stickRadius, double pixelsPerPoint)
        {
        }

        /// <summary>One sample, in arrival order. Returns the role a beginning pointer was given.</summary>
        public TouchRole Feed(in TouchSample sample) => TouchRole.None;

        /// <summary>How far a hold has charged at <paramref name="now"/>: 0 before the hold threshold, 1 at full charge.</summary>
        public double Charge(double now) => 0;

        /// <summary>The owned command for <paramref name="tick"/>. A gesture's kick or pass is delivered once.</summary>
        public Command CommandFor(long tick) => new Command(_controllerId, tick, Vec2.Zero);

        /// <summary>
        /// Forgets every gesture in progress, for a reset, a retry or new settings: nothing pending
        /// is delivered, and fingers already down are ignored until they lift, so none can end as a
        /// phantom kick, pass or aftertouch.
        /// </summary>
        public void Cancel()
        {
        }
    }
}
