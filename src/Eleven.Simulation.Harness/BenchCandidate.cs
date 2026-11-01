// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Eleven.Simulation.Harness
{
    /// <summary>One knob on the feel test bench (#45): a tunable from the supported metadata.</summary>
    public sealed class BenchKnob
    {
        internal BenchKnob(TunableParameter parameter, bool onPlayer)
        {
            Parameter = parameter;
            OnPlayer = onPlayer;
            Label = "";
            Step = 1;
        }

        /// <summary>The metadata it shows: unit, valid range and research starting range.</summary>
        public TunableParameter Parameter { get; }

        /// <summary>A <see cref="PlayerCapabilities"/> value of the controlled player, rather than a <see cref="SimConfig"/> value.</summary>
        public bool OnPlayer { get; }

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
    }

    /// <summary>
    /// A complete set of feel test bench values (#45): every <see cref="SimConfig.Tunables"/> value and
    /// every <see cref="PlayerCapabilities.Parameters"/> value of the controlled player. It is built
    /// only through the supported surface (<see cref="SimConfig.With(IEnumerable{KeyValuePair{string, double}})"/>
    /// and <see cref="PlayerCapabilities.With"/>), so a candidate is an ordinary immutable config plus
    /// a player, never a change to a running simulation.
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

        /// <summary>The config tunables, then the player capabilities, in metadata order.</summary>
        public static readonly ReadOnlyCollection<BenchKnob> Knobs = Array.AsReadOnly(Array.Empty<BenchKnob>());

        private BenchCandidate(SimConfig config, PlayerCapabilities capabilities)
        {
            Config = config;
            Capabilities = capabilities;
        }

        /// <summary>The technical reference: the spike's config and <see cref="PlayerCapabilities.Reference"/>.</summary>
        public static BenchCandidate Reference => new BenchCandidate(new SimConfig(), default);

        /// <summary>The values of an existing config and player.</summary>
        public static BenchCandidate From(SimConfig config, PlayerCapabilities capabilities) => new BenchCandidate(config, capabilities);

        /// <summary>The immutable config these values make.</summary>
        public SimConfig Config { get; }

        /// <summary>The controlled player's capabilities these values make.</summary>
        public PlayerCapabilities Capabilities { get; }

        /// <summary>A knob's value, by name.</summary>
        public double Get(string name) => 0;

        /// <summary>The standard practice start under these values: the spike's start with this player.</summary>
        public SimState PracticeStart() => SpikeScenario.InitialState(Config);

        /// <summary>The state hash of <see cref="PracticeStart"/>.</summary>
        public ulong PracticeStartHash() => 0;

        /// <summary>How many knobs differ from <paramref name="other"/>, bit for bit.</summary>
        public int ChangesFrom(BenchCandidate other) => 0;

        /// <summary><see cref="ReferencePreset"/> for the technical reference values, otherwise <see cref="UnsavedPreset"/>.</summary>
        public string PresetName => ReferencePreset;

        /// <summary>These values with some changed. Refused whole, with the knob and its range named, if any change is invalid.</summary>
        public bool TryWith(IEnumerable<KeyValuePair<string, double>> changes, out BenchCandidate? result, out string problem)
        {
            result = this;
            problem = "";
            return true;
        }

        /// <summary>
        /// For the page: one line per knob, tab-separated: group, name, label, unit, min, max, start
        /// min, start max, step, step decimals, value. Numbers are IEEE-754 bits in hex; an absent
        /// starting range is "-".
        /// </summary>
        public string Describe() => "";

        /// <summary>Every value, one <c>name=bits</c> line per knob: what the page sends back to apply.</summary>
        public string Values() => "";

        /// <summary>
        /// Reads <see cref="Values"/>. Every knob must appear exactly once with a valid value, or the
        /// whole candidate is refused with a reason: nothing is ever partly applied.
        /// </summary>
        public static bool TryParse(string values, out BenchCandidate? candidate, out string problem)
        {
            candidate = Reference;
            problem = "";
            return true;
        }

        /// <summary>A double as its IEEE-754 bits, 16 hex digits.</summary>
        public static string Bits(double value) => "";
    }
}
