// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A named set of feel test bench values (#46): every future-affecting value, the config hash it
    /// makes, and what made it, as a small text file that can be kept on a device, attached to an
    /// issue or committed.
    ///
    /// Reading checks everything before anything is applied: format and versions, every knob given
    /// once with a valid value, and the values rebuilding the recorded config hash. Any failure refuses
    /// the whole preset with the reason. Nothing on a device can make a preset approved feel: only a
    /// committed baseline can say so, after Erik's explicit promotion on the owning issue.
    /// </summary>
    public sealed class FeelPreset
    {
        public const int FormatVersion = 1;

        /// <summary>The built-in technical reference: the untuned reference values.</summary>
        public const string TechnicalReferenceKind = "technical-reference";

        /// <summary>An agent's starting point from research: never approved feel.</summary>
        public const string ResearchHypothesisKind = "research-hypothesis";

        /// <summary>Saved by Erik on the bench: his, but not approved feel.</summary>
        public const string SavedKind = "saved";

        /// <summary>Promoted by Erik on the owning issue and committed as a baseline.</summary>
        public const string ApprovedKind = "approved";

        /// <summary>The longest preset name.</summary>
        public const int MaxNameLength = 32;

        private FeelPreset(string name, string kind, BenchCandidate candidate)
        {
            Name = name;
            Kind = kind;
            Candidate = candidate;
        }

        public string Name { get; }

        public string Kind { get; }

        public BenchCandidate Candidate { get; }

        public string HostBuild { get; private set; } = "";

        public string SimulationBuild { get; private set; } = "";

        public string ControlCandidate { get; private set; } = "none";

        /// <summary>The built-in TECHNICAL REFERENCE preset.</summary>
        public static FeelPreset TechnicalReference => new FeelPreset("", "", BenchCandidate.Reference);

        /// <summary>The bench's current candidate saved under a short name. Fails on an empty, long or multi-line name.</summary>
        public static bool TrySave(string name, BenchCandidate candidate, string hostBuild, string controlCandidate, out FeelPreset? preset, out string problem)
        {
            preset = null;
            problem = "";
            return false;
        }

        /// <summary>The preset as its text file. The same preset always gives the same text.</summary>
        public string Encode() => "";

        /// <summary>
        /// Reads a preset file. Refused whole, with the reason, on anything this build can't use
        /// exactly. <paramref name="allowApproved"/> is true only for presets committed as baselines.
        /// </summary>
        public static bool TryRead(string text, bool allowApproved, out FeelPreset? preset, out string problem)
        {
            preset = null;
            problem = "";
            return false;
        }
    }
}
