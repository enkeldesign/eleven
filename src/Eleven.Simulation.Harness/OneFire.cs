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
    /// Candidate C's gesture parameters (#28, #77, ONE FIRE): how the one fire control reads a tap
    /// and a hold. Adapter values, not simulation config: they decide which owned command a gesture
    /// becomes, never how the football behaves, so no config or state hash depends on them. The
    /// references are Candidate A's values where the two share a meaning, untuned.
    ///
    /// The aftertouch window's length is the simulation's own (<c>aftertouchSeconds</c>).
    /// </summary>
    public sealed class OneFireSettings
    {
        /// <summary>The bench group these knobs show in.</summary>
        public const string Group = "Fire";

        /// <summary>The adapter's name in captures and command traces.</summary>
        public const string Candidate = "one-fire";

        /// <summary>Bumped whenever the gesture interpretation changes.</summary>
        public const int Version = 1;

        /// <summary>Every parameter, with its unit and valid range, and #28's starting range where it gives one.</summary>
        public static readonly ReadOnlyCollection<TunableParameter> Parameters = Array.AsReadOnly(new[]
        {
            new TunableParameter(Group, "fireHoldSeconds", "s", 0.05, 0.6, 0.12, 0.25),
            new TunableParameter(Group, "fireHoldStrength", "fraction of kick power", 0, 1),
            new TunableParameter(Group, "fireChargeSeconds", "s", 0.05, 2),
            new TunableParameter(Group, "fireChargedStrength", "fraction of kick power", 0, 1),
            new TunableParameter(Group, "fireLoft", "fraction of kickLoft", 0, 1),
            new TunableParameter(Group, "fireAftertouchRange", "pt", 10, 200),
        });

        /// <summary>
        /// The technical starting values: A's hold threshold, its immediate flick's strength for a
        /// kick released at the threshold, its charge time, charged strength, loft and aftertouch
        /// range, so the three candidates compare on the same numbers (#77).
        /// </summary>
        public static readonly OneFireSettings Reference = new OneFireSettings(new[]
        {
            ActionSurfaceSettings.Reference.HoldSeconds, ActionSurfaceSettings.Reference.FlickStrength, ActionSurfaceSettings.Reference.ChargeSeconds,
            ActionSurfaceSettings.Reference.ChargedStrength, ActionSurfaceSettings.Reference.Loft, ActionSurfaceSettings.Reference.AftertouchRange,
        });

        private readonly double[] _values;

        private OneFireSettings(double[] values)
        {
            _values = values;
        }

        /// <summary>A press released before this is a tap, the quick pass. Held this long, its release is a kick.</summary>
        public double HoldSeconds => _values[0];

        /// <summary>Requested strength of a kick released just as the hold threshold passes.</summary>
        public double HoldStrength => _values[1];

        /// <summary>Hold beyond <see cref="HoldSeconds"/> that reaches <see cref="ChargedStrength"/>.</summary>
        public double ChargeSeconds => _values[2];

        /// <summary>Requested strength of a fully charged kick.</summary>
        public double ChargedStrength => _values[3];

        /// <summary>Loft of a held kick.</summary>
        public double Loft => _values[4];

        /// <summary>Drag from the re-touch that gives full aftertouch: its sensitivity.</summary>
        public double AftertouchRange => _values[5];

        /// <summary>A value, by parameter name.</summary>
        public double Get(string name) => _values[IndexOf(name)];

        /// <summary>These settings with one value changed. Refused, naming the range, outside its valid range.</summary>
        public OneFireSettings With(string name, double value)
        {
            int i = IndexOf(name);
            TunableParameter p = Parameters[i];
            if (double.IsNaN(value) || double.IsInfinity(value) || value < p.Min || value > p.Max)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0} must be within {1:G6}–{2:G6} {3}; got {4:G6}.", name, p.Min, p.Max, p.Unit, value), nameof(value));
            }

            var values = (double[])_values.Clone();
            values[i] = value;
            return new OneFireSettings(values);
        }

        /// <summary>The adapter and every value, for a capture's control candidate. Values round-trip ("R").</summary>
        public string Describe()
        {
            var text = new StringBuilder($"{Candidate}/v{Version}");
            for (int i = 0; i < Parameters.Count; i++) text.Append(' ').Append(Parameters[i].Name).Append('=').Append(_values[i].ToString("R", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static int IndexOf(string name)
        {
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].Name == name) return i;
            }

            throw new ArgumentException($"'{name}' is not a one-fire parameter.", nameof(name));
        }
    }

    /// <summary>
    /// Candidate C, ONE FIRE (#28, #77), after Sensible Soccer and Kick Off: the right thumb has one
    /// fire control, the whole right-hand area. A tap asks the simulation for the footballer's quick
    /// pass, at its release. A hold, released, asks for a manual kick along the footballer's heading,
    /// harder the longer the hold: there is no flick and nothing to aim but the footballer. A
    /// re-touch while a kick winds up or its window is open shapes it. The stick, shaping,
    /// cancelling and the screen-to-world mapping are <see cref="GestureAdapter"/>'s.
    ///
    /// The kick is <see cref="KickIntent.AlongHeading"/>: the simulation reads the heading on the
    /// tick it accepts the command, and holds that line through the wind-up. The adapter never
    /// samples a heading, so no view or later turn can change the line, and a recording replays it.
    /// </summary>
    public sealed class OneFireAdapter : GestureAdapter
    {
        private int _firePointer = NoPointer;
        private double _pressTime;

        public OneFireAdapter(int controllerId, OneFireSettings settings)
            : base(controllerId)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public OneFireSettings Settings { get; private set; }

        /// <summary>Whether a finger is on fire for a gesture.</summary>
        public override bool ActionHeld => _firePointer != NoPointer;

        /// <summary>New settings; any gesture in progress is cancelled.</summary>
        public void Use(OneFireSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Cancel();
        }

        public override double Charge(double now) => _firePointer == NoPointer ? 0 : ChargeFor(now - _pressTime);

        public override string Describe() => Settings.Describe();

        protected override double AftertouchRange => Settings.AftertouchRange;

        protected override TouchRole? HeldRole(int pointer) => pointer == _firePointer ? TouchRole.Fire : (TouchRole?)null;

        protected override IEnumerable<int> ControlPointers()
        {
            if (_firePointer != NoPointer) yield return _firePointer;
        }

        protected override void CancelControls() => _firePointer = NoPointer;

        // Not read yet: the tests come first (#77).
        protected override TouchRole BeginControl(in TouchSample sample)
        {
            _pressTime = sample.Time;
            return TouchRole.None;
        }

        protected override TouchRole FeedControl(in TouchSample sample) => TouchRole.None;

        // 0 at the hold threshold, 1 a charge time later.
        private double ChargeFor(double held)
        {
            double c = (held - Settings.HoldSeconds) / Settings.ChargeSeconds;
            return c <= 0 ? 0 : c >= 1 ? 1 : c;
        }
    }
}
