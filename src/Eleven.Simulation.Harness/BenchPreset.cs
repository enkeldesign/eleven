// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A named set of test bench values (#46): a <see cref="BenchCandidate"/> saved under a short
    /// name, with what is needed to tell whether a later build can still read it. Saving one says
    /// nothing about feel: no preset is approved feel, and promoting one to a feel baseline is
    /// Erik's explicit act, recorded in the owning issue, never something this type does.
    ///
    /// The text form is whole or nothing. A preset of another format, of another config format, with
    /// a knob missing, unknown or out of range, or whose values don't make the config hash it
    /// recorded, is refused with a reason and never partly applied.
    /// </summary>
    public sealed class BenchPreset
    {
        /// <summary>The text form's version, bumped when its lines change meaning.</summary>
        public const int Format = 1;

        /// <summary>The first line of every preset.</summary>
        public const string Header = "eleven-preset/";

        /// <summary>The built-in preset's name: the current untuned reference values. Not a name a saved preset may take.</summary>
        public const string TechnicalReferenceName = "TECHNICAL REFERENCE";

        public const int MaxNameLength = 24;

        private BenchPreset(string name, BenchCandidate candidate, ControlCandidate controls, string build)
        {
            Name = name;
            Candidate = candidate;
            Controls = controls;
            Build = build;
        }

        /// <summary>The short name it was saved under.</summary>
        public string Name { get; }

        /// <summary>Every value it owns: the config's tunables, both players' capabilities and every touch candidate's settings.</summary>
        public BenchCandidate Candidate { get; }

        /// <summary>The touch candidate that was active when it was saved.</summary>
        public ControlCandidate Controls { get; }

        /// <summary>The build that saved it.</summary>
        public string Build { get; }

        /// <summary>The hash of the config its values make.</summary>
        public ulong ConfigHash => Candidate.Config.Hash;

        /// <summary>The built-in preset: <see cref="BenchCandidate.Reference"/> under Candidate A.</summary>
        public static BenchPreset TechnicalReference { get; } =
            new BenchPreset(TechnicalReferenceName, BenchCandidate.Reference, ControlCandidate.ActionSurface, "");

        /// <summary>
        /// A preset of these values under this name. Refused, with the reason, for a name that is
        /// empty, too long, the built-in preset's, or has characters other than letters, digits,
        /// spaces, hyphens and underscores.
        /// </summary>
        public static bool TryMake(string name, BenchCandidate candidate, ControlCandidate controls, string build, out BenchPreset? preset, out string problem)
        {
            preset = null;
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            name = (name ?? "").Trim();
            problem = NameProblem(name);
            if (problem.Length > 0) return false;
            preset = new BenchPreset(name, candidate, controls, Clean(build));
            return true;
        }

        /// <summary>Why a name can't be used, or empty if it can.</summary>
        public static string NameProblem(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "A preset needs a name.";
            if (name.Length > MaxNameLength) return $"A preset's name is at most {MaxNameLength} characters.";
            if (string.Equals(name, TechnicalReferenceName, StringComparison.OrdinalIgnoreCase)) return $"{TechnicalReferenceName} is the built-in preset's name.";
            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')) return "A preset's name has letters, digits, spaces, hyphens and underscores only.";
            }

            return "";
        }

        /// <summary>Whether two names are the same preset's: case doesn't tell them apart.</summary>
        public static bool SameName(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The text form: the format, the name, the build, the active touch candidate, the config's
        /// format and hash, the number of knobs, then every value as its bits.
        /// </summary>
        public string Write()
        {
            var text = new StringBuilder();
            text.Append(Header).Append(Format.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("name=").Append(Name).Append('\n');
            text.Append("build=").Append(Build).Append('\n');
            text.Append("controls=").Append(TouchCandidates.NameOf(Controls)).Append('\n');
            text.Append("configFormat=").Append(SimConfig.FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("configHash=").Append(ConfigHash.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
            text.Append("knobs=").Append(BenchCandidate.Knobs.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("values:\n").Append(Candidate.Values());
            return text.ToString();
        }

        /// <summary>
        /// Reads <see cref="Write"/>. The whole preset or nothing: on any mismatch the reason says
        /// what doesn't fit this build, and no preset is returned.
        /// </summary>
        public static bool TryRead(string text, out BenchPreset? preset, out string problem)
        {
            preset = null;
            string[] lines = (text ?? "").Replace("\r", "").Split('\n');
            if (lines.Length == 0 || !lines[0].StartsWith(Header, StringComparison.Ordinal))
            {
                problem = "This isn't an ELEVEN preset.";
                return false;
            }

            if (!int.TryParse(lines[0].Substring(Header.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int format) || format != Format)
            {
                problem = $"This preset is format {lines[0].Substring(Header.Length)}; this build reads format {Format}.";
                return false;
            }

            var head = new Dictionary<string, string>();
            int at = 1;
            for (; at < lines.Length && lines[at] != "values:"; at++)
            {
                int equals = lines[at].IndexOf('=');
                if (equals <= 0 || head.ContainsKey(lines[at].Substring(0, equals)))
                {
                    problem = $"This preset's line {at + 1} can't be read.";
                    return false;
                }

                head[lines[at].Substring(0, equals)] = lines[at].Substring(equals + 1);
            }

            foreach (string key in new[] { "name", "build", "controls", "configFormat", "configHash", "knobs" })
            {
                if (!head.ContainsKey(key) || at >= lines.Length)
                {
                    problem = at >= lines.Length ? "This preset has no values." : $"This preset has no {key}.";
                    return false;
                }
            }

            string name = head["name"].Trim();
            problem = NameProblem(name);
            if (problem.Length > 0) return false;

            if (!int.TryParse(head["configFormat"], NumberStyles.None, CultureInfo.InvariantCulture, out int configFormat) || configFormat != SimConfig.FormatVersion)
            {
                problem = $"\"{name}\" was saved under config format {head["configFormat"]}; this build's is {SimConfig.FormatVersion}. It can't be loaded.";
                return false;
            }

            ControlCandidate? controls = TouchCandidates.Named(head["controls"]);
            if (!controls.HasValue)
            {
                problem = $"\"{name}\" was saved under touch controls \"{head["controls"]}\", which this build doesn't have.";
                return false;
            }

            string values = string.Join("\n", lines, at + 1, lines.Length - at - 1);
            if (!BenchCandidate.TryParse(values, out BenchCandidate? candidate, out string valueProblem))
            {
                problem = $"\"{name}\" doesn't fit this build's knobs: {valueProblem}";
                return false;
            }

            if (!ulong.TryParse(head["configHash"], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong hash) || hash != candidate!.Config.Hash)
            {
                problem = $"\"{name}\" recorded config {head["configHash"]}, but its values make {candidate!.Config.Hash:x16} in this build. It can't be loaded.";
                return false;
            }

            preset = new BenchPreset(name, candidate, controls.Value, Clean(head["build"]));
            problem = "";
            return true;
        }

        private static string Clean(string? build) => (build ?? "").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    /// <summary>
    /// The presets one device keeps (#46), by name. Saving under a name already there replaces it.
    /// Stored as one text; a preset this build can't read stays on the shelf as it was, listed with
    /// the reason, so it fails visibly and a later or earlier build can still read it.
    /// </summary>
    public sealed class PresetShelf
    {
        /// <summary>The line between presets in the stored text.</summary>
        public const string Separator = "=====";

        private readonly List<BenchPreset> _presets = new List<BenchPreset>();
        private readonly List<KeyValuePair<string, string>> _unread = new List<KeyValuePair<string, string>>();

        /// <summary>The presets this build reads, in the order they were first saved.</summary>
        public IReadOnlyList<BenchPreset> Presets => _presets;

        /// <summary>For each stored preset this build can't read: why. Their texts are kept.</summary>
        public IEnumerable<string> Problems
        {
            get
            {
                foreach (KeyValuePair<string, string> u in _unread) yield return u.Value;
            }
        }

        /// <summary>The preset of this name, or null.</summary>
        public BenchPreset? Find(string name)
        {
            foreach (BenchPreset p in _presets)
            {
                if (BenchPreset.SameName(p.Name, name)) return p;
            }

            return null;
        }

        /// <summary>The saved preset whose values and touch settings are exactly these, or null.</summary>
        public BenchPreset? Matching(BenchCandidate candidate)
        {
            foreach (BenchPreset p in _presets)
            {
                if (p.Candidate.ChangesFrom(candidate) == 0) return p;
            }

            return null;
        }

        /// <summary>Saves a preset, in place of any of the same name. Returns whether it replaced one.</summary>
        public bool Save(BenchPreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            for (int i = 0; i < _presets.Count; i++)
            {
                if (!BenchPreset.SameName(_presets[i].Name, preset.Name)) continue;
                _presets[i] = preset;
                return true;
            }

            _presets.Add(preset);
            return false;
        }

        /// <summary>Deletes the preset of this name. Returns whether there was one.</summary>
        public bool Delete(string name)
        {
            for (int i = 0; i < _presets.Count; i++)
            {
                if (!BenchPreset.SameName(_presets[i].Name, name)) continue;
                _presets.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>The stored text: every preset, readable or not.</summary>
        public string Write()
        {
            var text = new StringBuilder();
            foreach (BenchPreset p in _presets) text.Append(p.Write()).Append(Separator).Append('\n');
            foreach (KeyValuePair<string, string> u in _unread) text.Append(u.Key).Append(Separator).Append('\n');
            return text.ToString();
        }

        /// <summary>Reads <see cref="Write"/>. Never throws: what can't be read is kept and listed in <see cref="Problems"/>.</summary>
        public static PresetShelf Read(string? text)
        {
            var shelf = new PresetShelf();
            var one = new StringBuilder();
            foreach (string line in (text ?? "").Replace("\r", "").Split('\n'))
            {
                if (line != Separator)
                {
                    one.Append(line).Append('\n');
                    continue;
                }

                string preset = one.ToString();
                one.Clear();
                if (preset.Trim().Length == 0) continue;
                if (BenchPreset.TryRead(preset, out BenchPreset? read, out string problem) && shelf.Find(read!.Name) == null) shelf._presets.Add(read);
                else shelf._unread.Add(new KeyValuePair<string, string>(preset, problem.Length > 0 ? problem : "A second preset of the same name."));
            }

            return shelf;
        }
    }
}
