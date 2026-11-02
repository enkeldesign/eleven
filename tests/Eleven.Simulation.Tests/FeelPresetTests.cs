using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Named presets (#46): every future-affecting value in a text file, read back exactly or refused
    /// whole, and never approved feel unless committed as a baseline.
    /// </summary>
    public class FeelPresetTests
    {
        private static BenchCandidate Candidate(params (string Name, double Value)[] changes)
        {
            Assert.True(BenchCandidate.Reference.TryWith(changes.Select(c => new KeyValuePair<string, double>(c.Name, c.Value)), out BenchCandidate? c, out string problem), problem);
            return c!;
        }

        private static FeelPreset Saved(string name = "Quick and firm") =>
            TrySaveOrFail(name, Candidate(("kickSpeed", 28), ("maxSpeed", 8.5), ("rollingFriction", 0.22)));

        private static FeelPreset TrySaveOrFail(string name, BenchCandidate candidate)
        {
            Assert.True(FeelPreset.TrySave(name, candidate, "70+52a99fd", "none", out FeelPreset? preset, out string problem), problem);
            return preset!;
        }

        private static string Replace(string text, string prefix, string line) =>
            string.Join("\n", text.Split('\n').Select(l => l.StartsWith(prefix, StringComparison.Ordinal) ? line : l));

        [Fact]
        public void A_saved_preset_reads_back_exactly()
        {
            FeelPreset saved = Saved();
            Assert.True(FeelPreset.TryRead(saved.Encode(), allowApproved: false, out FeelPreset? read, out string problem), problem);
            Assert.Equal("Quick and firm", read!.Name);
            Assert.Equal(FeelPreset.SavedKind, read.Kind);
            Assert.Equal("70+52a99fd", read.HostBuild);
            Assert.Equal("none", read.ControlCandidate);
            Assert.Equal(0, read.Candidate.ChangesFrom(saved.Candidate));
            Assert.Equal(saved.Candidate.Config.Hash, read.Candidate.Config.Hash);
            Assert.Equal(saved.Candidate.PracticeStartHash(), read.Candidate.PracticeStartHash());
            Assert.Equal(saved.Encode(), read.Encode());
        }

        [Fact]
        public void The_file_records_versions_config_hash_control_and_every_value_with_its_decimal()
        {
            FeelPreset saved = Saved();
            string[] lines = saved.Encode().Split('\n');
            Assert.Equal("eleven-preset 1", lines[0]);
            Assert.Contains("name: Quick and firm", lines);
            Assert.Contains("kind: saved", lines);
            Assert.Contains($"tick: {SimulationInfo.TickConvention}", lines);
            Assert.Contains($"state-schema: {SimulationInfo.StateSchemaVersion}", lines);
            Assert.Contains($"config-format: {SimConfig.FormatVersion}", lines);
            Assert.Contains($"config: {saved.Candidate.Config.Hash:x16}", lines);
            Assert.Contains("control: none", lines);
            Assert.Contains("host: 70+52a99fd", lines);
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                Assert.Contains(lines, l => l.StartsWith($"{k.Name}={BenchCandidate.Bits(saved.Candidate.Get(k.Name))}", StringComparison.Ordinal));
            }

            Assert.Contains(lines, l => l.StartsWith("kickSpeed=", StringComparison.Ordinal) && l.EndsWith("# 28 m/s", StringComparison.Ordinal));
        }

        [Fact]
        public void The_technical_reference_is_built_in_and_restores_the_reference_values()
        {
            FeelPreset reference = FeelPreset.TechnicalReference;
            Assert.Equal("TECHNICAL REFERENCE", reference.Name);
            Assert.Equal(FeelPreset.TechnicalReferenceKind, reference.Kind);
            Assert.Equal(0, reference.Candidate.ChangesFrom(BenchCandidate.Reference));
            Assert.Equal(BenchCandidate.Reference.Config.Hash, reference.Candidate.Config.Hash);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a name that is far too long to be a short name")]
        [InlineData("two\nlines")]
        [InlineData("TECHNICAL REFERENCE")]
        public void Bad_names_are_refused(string name)
        {
            Assert.False(FeelPreset.TrySave(name, BenchCandidate.Reference, "b", "none", out FeelPreset? preset, out string problem));
            Assert.Null(preset);
            Assert.False(string.IsNullOrWhiteSpace(problem));
        }

        [Theory]
        [InlineData("state-schema:", "state-schema: 99", "state schema 99")]
        [InlineData("config-format:", "config-format: 99", "config format 99")]
        [InlineData("tick:", "tick: tick-v0", "tick convention")]
        [InlineData("eleven-preset", "eleven-preset 2", "preset format 2")]
        public void Another_build_s_preset_is_refused_by_name(string prefix, string line, string named)
        {
            string text = Replace(Saved().Encode(), prefix, line);
            Assert.False(FeelPreset.TryRead(text, false, out FeelPreset? preset, out string problem));
            Assert.Null(preset);
            Assert.Contains(named, problem);
        }

        [Fact]
        public void Values_that_do_not_make_the_recorded_config_are_refused()
        {
            string text = Replace(Saved().Encode(), "kickSpeed=", $"kickSpeed={BenchCandidate.Bits(29)}");
            Assert.False(FeelPreset.TryRead(text, false, out FeelPreset? preset, out string problem));
            Assert.Null(preset);
            Assert.Contains("config", problem);
        }

        [Fact]
        public void A_missing_or_invalid_value_refuses_the_whole_preset()
        {
            string missing = string.Join("\n", Saved().Encode().Split('\n').Where(l => !l.StartsWith("maxSpeed=", StringComparison.Ordinal)));
            Assert.False(FeelPreset.TryRead(missing, false, out FeelPreset? a, out string why));
            Assert.Null(a);
            Assert.Contains("Max speed", why);

            string invalid = Replace(Saved().Encode(), "maxSpeed=", $"maxSpeed={BenchCandidate.Bits(99)}");
            Assert.False(FeelPreset.TryRead(invalid, false, out FeelPreset? b, out string why2));
            Assert.Null(b);
            Assert.Contains("Max speed", why2);
        }

        [Fact]
        public void Only_a_committed_baseline_can_be_approved_feel()
        {
            string approved = Replace(Saved().Encode(), "kind:", "kind: approved");
            Assert.False(FeelPreset.TryRead(approved, allowApproved: false, out FeelPreset? onDevice, out string problem));
            Assert.Null(onDevice);
            Assert.Contains("approved", problem);

            Assert.True(FeelPreset.TryRead(approved, allowApproved: true, out FeelPreset? baseline, out string why), why);
            Assert.Equal(FeelPreset.ApprovedKind, baseline!.Kind);
        }

        [Fact]
        public void Research_hypotheses_read_and_stay_labelled()
        {
            string hypothesis = Replace(Saved("Kick Off pace").Encode(), "kind:", "kind: research-hypothesis");
            Assert.True(FeelPreset.TryRead(hypothesis, false, out FeelPreset? preset, out string problem), problem);
            Assert.Equal(FeelPreset.ResearchHypothesisKind, preset!.Kind);
        }

        [Fact]
        public void An_agent_s_preset_is_always_a_research_hypothesis()
        {
            Assert.True(FeelPreset.TryHypothesis("Retro quick", Candidate(("kickSpeed", 30)), "eleven-sim", out FeelPreset? made, out string problem), problem);
            Assert.Equal(FeelPreset.ResearchHypothesisKind, made!.Kind);
            Assert.Contains("kind: research-hypothesis", made.Encode().Split('\n'));
            Assert.True(FeelPreset.TryRead(made.Encode(), false, out FeelPreset? read, out string why), why);
            Assert.Equal(FeelPreset.ResearchHypothesisKind, read!.Kind);
        }

        [Fact]
        public void Unknown_kinds_and_garbage_are_refused()
        {
            Assert.False(FeelPreset.TryRead(Replace(Saved().Encode(), "kind:", "kind: favourite"), false, out _, out string kind));
            Assert.Contains("kind", kind);
            Assert.False(FeelPreset.TryRead("not a preset", false, out _, out string garbage));
            Assert.Contains("ELEVEN preset", garbage);
            Assert.False(FeelPreset.TryRead("", false, out _, out _));
        }

        [Fact]
        public void Comments_blank_lines_and_windows_line_endings_are_tolerated()
        {
            string text = Saved().Encode().Replace("\n", "\r\n") + "\r\n\r\n# kept by Erik\r\n";
            Assert.True(FeelPreset.TryRead(text, false, out FeelPreset? preset, out string problem), problem);
            Assert.Equal("Quick and firm", preset!.Name);
        }
    }
}
