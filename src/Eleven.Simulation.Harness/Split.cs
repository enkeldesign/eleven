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
    /// Candidate B's gesture parameters (#28, #76, SPLIT): how KICK reads a press, a hold and a
    /// flick. PASS has none: a press on it is the quick pass. Adapter values, not simulation config:
    /// they decide which owned command a gesture becomes, never how the football behaves, so no
    /// config or state hash depends on them. The references are Candidate A's values, untuned.
    ///
    /// The aftertouch window's length is the simulation's own (<c>aftertouchSeconds</c>).
    /// </summary>
    public sealed class SplitSettings
    {
        /// <summary>The bench group these knobs show in.</summary>
        public const string Group = "Split";

        /// <summary>The adapter's name in captures and command traces.</summary>
        public const string Candidate = "split";

        /// <summary>Bumped whenever the gesture interpretation changes.</summary>
        public const int Version = 1;

        /// <summary>Every parameter, with its unit and valid range: A's, for KICK.</summary>
        public static readonly ReadOnlyCollection<TunableParameter> Parameters = Array.AsReadOnly(new[]
        {
            new TunableParameter(Group, "splitHoldSeconds", "s", 0.05, 0.6, 0.12, 0.25),
            new TunableParameter(Group, "splitFlickDistance", "pt", 4, 80, 12, 32),
            new TunableParameter(Group, "splitFastFlickDistance", "pt", 2, 40),
            new TunableParameter(Group, "splitFastFlickSpeed", "pt/s", 50, 3000),
            new TunableParameter(Group, "splitFlickStrength", "fraction of kick power", 0, 1),
            new TunableParameter(Group, "splitChargeSeconds", "s", 0.05, 2),
            new TunableParameter(Group, "splitChargedStrength", "fraction of kick power", 0, 1),
            new TunableParameter(Group, "splitLoft", "fraction of kickLoft", 0, 1),
            new TunableParameter(Group, "splitAftertouchRange", "pt", 10, 200),
        });

        private static readonly string[] Names =
        {
            "splitHoldSeconds", "splitFlickDistance", "splitFastFlickDistance", "splitFastFlickSpeed", "splitFlickStrength",
            "splitChargeSeconds", "splitChargedStrength", "splitLoft", "splitAftertouchRange",
        };

        private static readonly string[] ActionNames =
        {
            "actionHoldSeconds", "actionFlickDistance", "actionFastFlickDistance", "actionFastFlickSpeed", "actionFlickStrength",
            "actionChargeSeconds", "actionChargedStrength", "actionLoft", "actionAftertouchRange",
        };

        /// <summary>The technical starting values: Candidate A's, so the two compare on the same reading (#76).</summary>
        public static readonly SplitSettings Reference = FromAction(ActionSurfaceSettings.Reference);

        private readonly double[] _values;

        private SplitSettings(double[] values)
        {
            _values = values;
        }

        private static SplitSettings FromAction(ActionSurfaceSettings a)
        {
            var values = new double[Names.Length];
            for (int i = 0; i < Names.Length; i++) values[i] = a.Get(ActionNames[i]);
            return new SplitSettings(values);
        }

        /// <summary>A press on KICK, held this long, charges its flick.</summary>
        public double HoldSeconds => _values[0];

        /// <summary>Travel from the press that makes a flick, however slow.</summary>
        public double FlickDistance => _values[1];

        /// <summary>The shortest travel that makes a flick when it is fast; also where a hold ends and a flick begins.</summary>
        public double FastFlickDistance => _values[2];

        /// <summary>Release speed at which a flick of <see cref="FastFlickDistance"/> or more counts.</summary>
        public double FastFlickSpeed => _values[3];

        /// <summary>Requested strength of an immediate flick.</summary>
        public double FlickStrength => _values[4];

        /// <summary>Hold beyond <see cref="HoldSeconds"/> that reaches <see cref="ChargedStrength"/>.</summary>
        public double ChargeSeconds => _values[5];

        /// <summary>Requested strength of a fully charged flick.</summary>
        public double ChargedStrength => _values[6];

        /// <summary>Loft of a flicked kick.</summary>
        public double Loft => _values[7];

        /// <summary>Drag from the re-touch on KICK that gives full aftertouch: its sensitivity.</summary>
        public double AftertouchRange => _values[8];

        /// <summary>The values KICK reads a flick with.</summary>
        public FlickValues Flick => new FlickValues(HoldSeconds, FlickDistance, FastFlickDistance, FastFlickSpeed, FlickStrength, ChargeSeconds, ChargedStrength, Loft);

        /// <summary>A value, by parameter name.</summary>
        public double Get(string name)
        {
            int i = Array.IndexOf(Names, name);
            if (i < 0) throw new ArgumentException($"'{name}' is not a split parameter.", nameof(name));
            return _values[i];
        }

        /// <summary>These settings with one value changed. Refused, naming the range, outside its valid range.</summary>
        public SplitSettings With(string name, double value)
        {
            int i = Array.IndexOf(Names, name);
            if (i < 0) throw new ArgumentException($"'{name}' is not a split parameter.", nameof(name));
            TunableParameter p = Parameters[i];
            if (double.IsNaN(value) || double.IsInfinity(value) || value < p.Min || value > p.Max)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0} must be within {1:G6}–{2:G6} {3}; got {4:G6}.", name, p.Min, p.Max, p.Unit, value), nameof(value));
            }

            var values = (double[])_values.Clone();
            values[i] = value;
            return new SplitSettings(values);
        }

        /// <summary>The adapter and every value, for a capture's control candidate. Values round-trip ("R").</summary>
        public string Describe()
        {
            var text = new StringBuilder($"{Candidate}/v{Version}");
            foreach (TunableParameter p in Parameters) text.Append(' ').Append(p.Name).Append('=').Append(Get(p.Name).ToString("R", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    /// <summary>
    /// Candidate B, SPLIT (#28, #76), after Super Soccer Champs: the right thumb has two controls.
    /// PASS, a disc at the thumb's rest, asks the simulation for the footballer's quick pass at the
    /// press. KICK, the rest of the right-hand area, reads a flick as a manual kick along it, kept
    /// exactly, a hold → flick as a harder one, and a re-touch while a kick winds up or its window is
    /// open as shaping. A press on KICK released without a flick asks for nothing. The stick,
    /// shaping, cancelling and the screen-to-world mapping are <see cref="GestureAdapter"/>'s.
    /// </summary>
    public sealed class SplitAdapter : GestureAdapter
    {
        public SplitAdapter(int controllerId, SplitSettings settings)
            : base(controllerId)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public SplitSettings Settings { get; private set; }

        /// <summary>Whether a finger is on KICK for a gesture.</summary>
        public override bool ActionHeld => false;

        /// <summary>Whether a finger holds PASS.</summary>
        public bool PassHeld => false;

        /// <summary>New settings; any gesture in progress is cancelled.</summary>
        public void Use(SplitSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Cancel();
        }

        public override double Charge(double now) => 0;

        public override string Describe() => Settings.Describe();

        protected override double AftertouchRange => Settings.AftertouchRange;

        protected override TouchRole? HeldRole(int pointer) => null;

        protected override IEnumerable<int> ControlPointers()
        {
            yield break;
        }

        protected override void CancelControls()
        {
        }

        protected override TouchRole BeginControl(in TouchSample sample) => TouchRole.None;

        protected override TouchRole FeedControl(in TouchSample sample) => TouchRole.None;
    }
}
