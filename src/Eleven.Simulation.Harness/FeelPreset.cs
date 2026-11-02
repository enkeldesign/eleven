// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
    ///
    /// The file:
    /// <code>
    /// eleven-preset 1
    /// name: Quick and firm
    /// kind: saved
    /// host: 70+52a99fd
    /// simulation: 1.0.0+…
    /// tick: tick-v1
    /// state-schema: 5
    /// config-format: 6
    /// config: 2f4ea4f9446cecc1
    /// control: none
    /// kickSpeed=403c000000000000  # 28 m/s
    /// …                           (every knob, as IEEE-754 bits; the comment is for people)
    /// </code>
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

        public const string TechnicalReferenceName = "TECHNICAL REFERENCE";

        private const string Header = "eleven-preset";

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

        public string SimulationBuild { get; private set; } = SimulationInfo.BuildId;

        public string ControlCandidate { get; private set; } = "none";

        /// <summary>The built-in TECHNICAL REFERENCE preset.</summary>
        public static FeelPreset TechnicalReference => new FeelPreset(TechnicalReferenceName, TechnicalReferenceKind, BenchCandidate.Reference);

        /// <summary>The bench's current candidate saved under a short name. Fails on an empty, long, multi-line or reserved name.</summary>
        public static bool TrySave(string name, BenchCandidate candidate, string hostBuild, string controlCandidate, out FeelPreset? preset, out string problem)
        {
            preset = null;
            problem = NameProblem(name);
            if (problem.Length > 0) return false;
            preset = new FeelPreset(name.Trim(), SavedKind, candidate)
            {
                HostBuild = OneLine(hostBuild),
                ControlCandidate = string.IsNullOrWhiteSpace(controlCandidate) ? "none" : OneLine(controlCandidate),
            };
            return true;
        }

        /// <summary>
        /// An agent's preset: always labelled a research hypothesis, never approved feel (#27).
        /// </summary>
        public static bool TryHypothesis(string name, BenchCandidate candidate, string hostBuild, out FeelPreset? preset, out string problem)
        {
            preset = null;
            problem = NameProblem(name);
            if (problem.Length > 0) return false;
            preset = new FeelPreset(name.Trim(), ResearchHypothesisKind, candidate) { HostBuild = OneLine(hostBuild) };
            return true;
        }

        /// <summary>The preset as its text file. The same preset always gives the same text.</summary>
        public string Encode()
        {
            var text = new StringBuilder();
            text.Append(Header).Append(' ').Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("name: ").Append(Name).Append('\n');
            text.Append("kind: ").Append(Kind).Append('\n');
            text.Append("host: ").Append(HostBuild).Append('\n');
            text.Append("simulation: ").Append(SimulationBuild).Append('\n');
            text.Append("tick: ").Append(SimulationInfo.TickConvention).Append('\n');
            text.Append("state-schema: ").Append(SimulationInfo.StateSchemaVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("config-format: ").Append(SimConfig.FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("config: ").Append(Candidate.Config.Hash.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
            text.Append("control: ").Append(ControlCandidate).Append('\n');
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                double value = Candidate.Get(k.Name);
                text.Append(k.Name).Append('=').Append(BenchCandidate.Bits(value))
                    .Append("  # ").Append(value.ToString("G6", CultureInfo.InvariantCulture)).Append(' ').Append(k.Parameter.Unit).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// Reads a preset file. Refused whole, with the reason, on anything this build can't use
        /// exactly. <paramref name="allowApproved"/> is true only for presets committed as baselines.
        /// </summary>
        public static bool TryRead(string text, bool allowApproved, out FeelPreset? preset, out string problem)
        {
            preset = null;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var values = new StringBuilder();
            bool headerSeen = false;
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (!headerSeen)
                {
                    if (line.Length == 0) continue;
                    if (!line.StartsWith(Header + " ", StringComparison.Ordinal))
                    {
                        problem = "Not an ELEVEN preset: it must start with \"eleven-preset\".";
                        return false;
                    }

                    string format = line.Substring(Header.Length + 1).Trim();
                    if (format != FormatVersion.ToString(CultureInfo.InvariantCulture))
                    {
                        problem = $"Made by preset format {format}; this build reads preset format {FormatVersion}. Not loaded.";
                        return false;
                    }

                    headerSeen = true;
                    continue;
                }

                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                int equals = line.IndexOf('=');
                if (colon > 0 && (equals < 0 || colon < equals)) fields[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                else values.Append(line).Append('\n');
            }

            if (!headerSeen)
            {
                problem = "Not an ELEVEN preset: it is empty.";
                return false;
            }

            string Field(string key) => fields.TryGetValue(key, out string? v) ? v : "";

            var mismatches = new List<string>();
            if (Field("tick") != SimulationInfo.TickConvention) mismatches.Add($"tick convention \"{Field("tick")}\", this build uses \"{SimulationInfo.TickConvention}\"");
            if (Field("state-schema") != SimulationInfo.StateSchemaVersion.ToString(CultureInfo.InvariantCulture)) mismatches.Add($"state schema {Field("state-schema")}, this build reads {SimulationInfo.StateSchemaVersion}");
            if (Field("config-format") != SimConfig.FormatVersion.ToString(CultureInfo.InvariantCulture)) mismatches.Add($"config format {Field("config-format")}, this build reads {SimConfig.FormatVersion}");
            if (mismatches.Count > 0)
            {
                problem = $"Made by another build (host {Field("host")}): {string.Join("; ", mismatches)}. Not loaded.";
                return false;
            }

            string kind = Field("kind");
            if (kind != SavedKind && kind != ResearchHypothesisKind && kind != ApprovedKind && kind != TechnicalReferenceKind)
            {
                problem = $"Unknown preset kind \"{kind}\". Not loaded.";
                return false;
            }

            if (kind == ApprovedKind && !allowApproved)
            {
                problem = "This preset says it is approved feel, which only a committed baseline can be, after Erik promotes it on its issue. Not loaded.";
                return false;
            }

            string name = Field("name");
            problem = kind == TechnicalReferenceKind ? "" : NameProblem(name);
            if (problem.Length > 0) return false;

            if (!BenchCandidate.TryParse(values.ToString(), out BenchCandidate? candidate, out problem)) return false;
            string recorded = Field("config");
            string made = candidate!.Config.Hash.ToString("x16", CultureInfo.InvariantCulture);
            if (recorded != made)
            {
                problem = $"The values make config {made}, the preset says {recorded}. Not loaded.";
                return false;
            }

            preset = new FeelPreset(name, kind, candidate)
            {
                HostBuild = Field("host"),
                SimulationBuild = Field("simulation"),
                ControlCandidate = Field("control").Length == 0 ? "none" : Field("control"),
            };
            problem = "";
            return true;
        }

        private static string NameProblem(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "A preset needs a name.";
            if (name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0 || name.IndexOf('#') >= 0) return "A preset name is one line, without '#'.";
            if (name.Trim().Length > MaxNameLength) return $"A preset name is at most {MaxNameLength} characters.";
            if (string.Equals(name.Trim(), TechnicalReferenceName, StringComparison.OrdinalIgnoreCase)) return "TECHNICAL REFERENCE is the built-in preset's name.";
            return "";
        }

        private static string OneLine(string s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('#', ' ').Trim();
    }
}
