// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Eleven.Simulation.Harness
{
    /// <summary>One knob on the feel test bench (#45): a tunable from the supported metadata.</summary>
    public sealed class BenchKnob
    {
        internal BenchKnob(TunableParameter parameter, bool onPlayer, bool onAdapter = false)
        {
            Parameter = parameter;
            OnPlayer = onPlayer;
            OnAdapter = onAdapter;
            Label = LabelFor(parameter.Name);
            int exponent;
            Step = StepFor(parameter, out exponent);
            StepDecimals = Math.Max(0, -exponent);
        }

        /// <summary>The metadata it shows: unit, valid range and research starting range.</summary>
        public TunableParameter Parameter { get; }

        /// <summary>A <see cref="PlayerCapabilities"/> value of the controlled player, rather than a <see cref="SimConfig"/> value.</summary>
        public bool OnPlayer { get; }

        /// <summary>A touch adapter value (<see cref="ActionSurfaceSettings"/>): how gestures read, never how the football behaves.</summary>
        public bool OnAdapter { get; }

        /// <summary>A <see cref="KeeperCapabilities"/> value of the practice keeper (#69), rather than a <see cref="SimConfig"/> value.</summary>
        public bool OnKeeper { get; }

        public string Name => Parameter.Name;

        public string Group => Parameter.Group;

        /// <summary>The name in words, e.g. "Kick speed" for <c>kickSpeed</c>.</summary>
        public string Label { get; }

        /// <summary>
        /// What one press of − or + changes. Presentation only: the largest 1, 2 or 5 × 10ⁿ that fits
        /// twenty times into the research starting range, or into the valid range where there is
        /// none. Any valid value can still be typed.
        /// </summary>
        public double Step { get; }

        /// <summary>Decimal places of <see cref="Step"/>, for showing values on its grid.</summary>
        public int StepDecimals { get; }

        // "kickWindUpSeconds" → "Kick wind up seconds".
        private static string LabelFor(string name)
        {
            var label = new StringBuilder(name.Length + 4);
            foreach (char c in name)
            {
                if (label.Length == 0) label.Append(char.ToUpperInvariant(c));
                else if (char.IsUpper(c)) label.Append(' ').Append(char.ToLowerInvariant(c));
                else label.Append(c);
            }

            return label.ToString();
        }

        // The largest 1, 2 or 5 × 10^exponent at most a twentieth of the range. Powers of ten are
        // exact up to 10^22, and m / 10^k is correctly rounded, so the step is the double its decimal
        // spelling names (0.0002, not 2 × 0.0001).
        private static double StepFor(TunableParameter p, out int exponent)
        {
            bool start = p.StartMin.HasValue && p.StartMax.HasValue && p.StartMax.Value > p.StartMin.Value;
            double span = start ? p.StartMax!.Value - p.StartMin!.Value : p.Max - p.Min;
            double target = span / 20 * (1 + 1e-9);

            exponent = 0;
            while (exponent > -12 && Scaled(1, exponent) > target) exponent--;
            while (exponent < 12 && Scaled(1, exponent + 1) <= target) exponent++;
            foreach (int m in new[] { 5, 2 })
            {
                if (Scaled(m, exponent) <= target) return Scaled(m, exponent);
            }

            return Scaled(1, exponent);
        }

        private static double Scaled(int m, int exponent)
        {
            double power = 1;
            for (int i = 0; i < Math.Abs(exponent); i++) power *= 10;
            return exponent >= 0 ? m * power : m / power;
        }
    }

    /// <summary>
    /// A complete set of feel test bench values (#45): every <see cref="SimConfig.Tunables"/> value,
    /// every <see cref="PlayerCapabilities.Parameters"/> value of the controlled player, and every
    /// touch gesture value of Candidate A (<see cref="ActionSurfaceSettings.Parameters"/>, #28). It is
    /// built only through the supported surface (<see cref="SimConfig.With(IEnumerable{KeyValuePair{string, double}})"/>,
    /// <see cref="PlayerCapabilities.With"/> and <see cref="ActionSurfaceSettings.With"/>), so a
    /// candidate is an ordinary immutable config plus a player and an adapter's settings, never a
    /// change to a running simulation. The touch values decide which commands gestures become; they
    /// are in no config or state hash.
    ///
    /// The page and the host exchange values as their IEEE-754 bits, so the same values are the same
    /// doubles in every runtime; decimal is for display only.
    /// </summary>
    public sealed class BenchCandidate
    {
        /// <summary>The active preset a capture records for the technical reference values.</summary>
        public const string ReferencePreset = "technical-reference";

        /// <summary>The active preset a capture records for any other values, until presets have names (#46).</summary>
        public const string UnsavedPreset = "unsaved";

        /// <summary>The config tunables, then the player capabilities, then the touch gestures, in metadata order.</summary>
        public static readonly ReadOnlyCollection<BenchKnob> Knobs = Array.AsReadOnly(MakeKnobs());

        private static readonly BenchCandidate s_reference = new BenchCandidate(SpikeScenario.Config(), PlayerCapabilities.Reference, ActionSurfaceSettings.Reference);

        private BenchCandidate(SimConfig config, PlayerCapabilities capabilities, ActionSurfaceSettings actionSurface)
        {
            Config = config;
            Capabilities = capabilities;
            ActionSurface = actionSurface;
        }

        /// <summary>The technical reference: the spike's config, <see cref="PlayerCapabilities.Reference"/> and <see cref="ActionSurfaceSettings.Reference"/>.</summary>
        public static BenchCandidate Reference => s_reference;

        /// <summary>The values of an existing config and player, with the reference touch settings unless given.</summary>
        public static BenchCandidate From(SimConfig config, PlayerCapabilities capabilities, ActionSurfaceSettings? actionSurface = null) =>
            new BenchCandidate(config, capabilities, actionSurface ?? ActionSurfaceSettings.Reference);

        /// <summary>The immutable config these values make.</summary>
        public SimConfig Config { get; }

        /// <summary>These values under another ball control model (#60), the live switch's applied candidate; every knob is kept.</summary>
        public BenchCandidate WithControlModel(ControlModel model) => new BenchCandidate(Config.WithControlModel(model), Capabilities, ActionSurface);

        /// <summary>The controlled player's capabilities these values make.</summary>
        public PlayerCapabilities Capabilities { get; }

        /// <summary>Candidate A's gesture settings these values make (#28).</summary>
        public ActionSurfaceSettings ActionSurface { get; }

        /// <summary>The practice keeper's capabilities these values make (#69).</summary>
        public KeeperCapabilities Keeper => KeeperCapabilities.Reference;

        /// <summary>A knob's value, by name.</summary>
        public double Get(string name)
        {
            BenchKnob knob = Find(name) ?? throw new ArgumentException($"'{name}' is not a bench knob.", nameof(name));
            return knob.OnAdapter ? ActionSurface.Get(name) : knob.OnPlayer ? Capabilities.Get(name) : Config.Get(name);
        }

        /// <summary>The standard practice start under these values: the spike's start with this player.</summary>
        public SimState PracticeStart() => SpikeScenario.InitialState(Config, Capabilities);

        /// <summary>The state hash of <see cref="PracticeStart"/>.</summary>
        public ulong PracticeStartHash() => new Simulator(Config, PracticeStart()).StateHash();

        /// <summary>How many knobs differ from <paramref name="other"/>, bit for bit.</summary>
        public int ChangesFrom(BenchCandidate other)
        {
            int changes = 0;
            foreach (BenchKnob k in Knobs)
            {
                if (BitConverter.DoubleToInt64Bits(Get(k.Name)) != BitConverter.DoubleToInt64Bits(other.Get(k.Name))) changes++;
            }

            return changes;
        }

        /// <summary><see cref="ReferencePreset"/> for the technical reference values, otherwise <see cref="UnsavedPreset"/>.</summary>
        public string PresetName => ChangesFrom(s_reference) == 0 ? ReferencePreset : UnsavedPreset;

        /// <summary>These values with some changed. Refused whole, with the knob and its range named, if any change is invalid.</summary>
        public bool TryWith(IEnumerable<KeyValuePair<string, double>> changes, out BenchCandidate? result, out string problem)
        {
            result = null;
            var config = new List<KeyValuePair<string, double>>();
            PlayerCapabilities capabilities = Capabilities;
            ActionSurfaceSettings actionSurface = ActionSurface;
            foreach (KeyValuePair<string, double> change in changes)
            {
                BenchKnob? knob = Find(change.Key);
                if (knob == null)
                {
                    problem = $"'{change.Key}' is not a bench knob.";
                    return false;
                }

                problem = RangeProblem(knob, change.Value);
                if (problem.Length > 0) return false;
                if (knob.OnAdapter) actionSurface = actionSurface.With(knob.Name, change.Value);
                else if (knob.OnPlayer) capabilities = capabilities.With(knob.Name, change.Value);
                else config.Add(change);
            }

            SimConfig made;
            try
            {
                made = config.Count == 0 ? Config : Config.With(config);
            }
            catch (ArgumentException e)
            {
                problem = e.Message;
                return false;
            }

            result = new BenchCandidate(made, capabilities, actionSurface);
            problem = "";
            return true;
        }

        /// <summary>
        /// For the page: one line per knob, tab-separated: group, name, label, unit, min, max, start
        /// min, start max, step, step decimals, value. Numbers are IEEE-754 bits in hex; an absent
        /// starting range is "-".
        /// </summary>
        public string Describe()
        {
            var text = new StringBuilder();
            foreach (BenchKnob k in Knobs)
            {
                TunableParameter p = k.Parameter;
                text.Append(k.Group).Append('\t').Append(k.Name).Append('\t').Append(k.Label).Append('\t').Append(p.Unit).Append('\t')
                    .Append(Bits(p.Min)).Append('\t').Append(Bits(p.Max)).Append('\t')
                    .Append(p.StartMin.HasValue ? Bits(p.StartMin.Value) : "-").Append('\t')
                    .Append(p.StartMax.HasValue ? Bits(p.StartMax.Value) : "-").Append('\t')
                    .Append(Bits(k.Step)).Append('\t').Append(k.StepDecimals.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Bits(Get(k.Name))).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>Every value, one <c>name=bits</c> line per knob: what the page sends back to apply.</summary>
        public string Values()
        {
            var text = new StringBuilder();
            foreach (BenchKnob k in Knobs) text.Append(k.Name).Append('=').Append(Bits(Get(k.Name))).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// Reads <see cref="Values"/>. Every knob must appear exactly once with a valid value, or the
        /// whole candidate is refused with a reason: nothing is ever partly applied.
        /// </summary>
        public static bool TryParse(string values, out BenchCandidate? candidate, out string problem)
        {
            candidate = null;
            var given = new Dictionary<string, double>();
            foreach (string raw in (values ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int equals = line.IndexOf('=');
                string name = equals < 0 ? line : line.Substring(0, equals);
                BenchKnob? knob = Find(name);
                if (knob == null)
                {
                    problem = $"'{name}' is not a bench knob.";
                    return false;
                }

                if (equals < 0 || !TryFromBits(line.Substring(equals + 1), out double value))
                {
                    problem = $"{knob.Label} has no readable value (16 hex digits of its IEEE-754 bits).";
                    return false;
                }

                if (given.ContainsKey(name))
                {
                    problem = $"{knob.Label} is given twice.";
                    return false;
                }

                given.Add(name, value);
            }

            var changes = new List<KeyValuePair<string, double>>(Knobs.Count);
            foreach (BenchKnob k in Knobs)
            {
                if (!given.TryGetValue(k.Name, out double value))
                {
                    problem = $"Missing {k.Label}: every knob must be given.";
                    return false;
                }

                changes.Add(new KeyValuePair<string, double>(k.Name, value));
            }

            return s_reference.TryWith(changes, out candidate, out problem);
        }

        /// <summary>A double as its IEEE-754 bits, 16 hex digits.</summary>
        public static string Bits(double value) =>
            BitConverter.DoubleToInt64Bits(value).ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>Reads <see cref="Bits"/>: exactly 16 hex digits.</summary>
        public static bool TryFromBits(string text, out double value)
        {
            value = 0;
            if (text == null || text.Length != 16) return false;
            if (!ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong bits)) return false;
            value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
            return true;
        }

        private static BenchKnob? Find(string name)
        {
            foreach (BenchKnob k in Knobs)
            {
                if (k.Name == name) return k;
            }

            return null;
        }

        // Empty if the value may be applied.
        private static string RangeProblem(BenchKnob knob, double value)
        {
            TunableParameter p = knob.Parameter;
            if (double.IsNaN(value) || double.IsInfinity(value)) return $"{knob.Label} must be a finite number.";
            if (value >= p.Min && value <= p.Max) return "";
            return string.Format(CultureInfo.InvariantCulture, "{0} must be within {1:G6}–{2:G6} {3}; got {4:G6}.", knob.Label, p.Min, p.Max, p.Unit, value);
        }

        private static BenchKnob[] MakeKnobs()
        {
            var knobs = new List<BenchKnob>(SimConfig.Tunables.Count + PlayerCapabilities.Parameters.Count + ActionSurfaceSettings.Parameters.Count);
            foreach (TunableParameter p in SimConfig.Tunables) knobs.Add(new BenchKnob(p, onPlayer: false));
            foreach (TunableParameter p in PlayerCapabilities.Parameters) knobs.Add(new BenchKnob(p, onPlayer: true));
            foreach (TunableParameter p in ActionSurfaceSettings.Parameters) knobs.Add(new BenchKnob(p, onPlayer: false, onAdapter: true));
            return knobs.ToArray();
        }
    }
}
