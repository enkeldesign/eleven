using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Named presets (#46): a bench candidate saved under a short name, recalled whole or refused
    /// with a reason, and a shelf of them that keeps what it can't read.
    /// </summary>
    public class BenchPresetTests
    {
        private static KeyValuePair<string, double> Set(string name, double value) => new KeyValuePair<string, double>(name, value);

        private static BenchCandidate Changed(params KeyValuePair<string, double>[] changes)
        {
            Assert.True(BenchCandidate.Reference.TryWith(changes, out BenchCandidate? changed, out string problem), problem);
            return changed!;
        }

        private static BenchPreset Preset(string name, BenchCandidate? candidate = null, ControlCandidate controls = ControlCandidate.Split, string build = "153+abc1234")
        {
            Assert.True(BenchPreset.TryMake(name, candidate ?? Changed(Set("kickSpeed", 18.5), Set("splitHoldSeconds", 0.2)), controls, build, out BenchPreset? preset, out string problem), problem);
            return preset!;
        }

        [Fact]
        public void The_technical_reference_is_the_untuned_reference_values()
        {
            BenchPreset reference = BenchPreset.TechnicalReference;
            Assert.Equal("TECHNICAL REFERENCE", reference.Name);
            Assert.Equal(0, reference.Candidate.ChangesFrom(BenchCandidate.Reference));
            Assert.Equal(BenchCandidate.Reference.Config.Hash, reference.ConfigHash);
        }

        [Fact]
        public void A_preset_round_trips_every_value_it_owns()
        {
            BenchCandidate candidate = Changed(Set("kickSpeed", 18.5), Set("maxSpeed", 7.5), Set("actionHoldSeconds", 0.2), Set("splitFlickStrength", 0.8), Set("fireHoldStrength", 0.4));
            BenchPreset saved = Preset("Heavy ball 2", candidate, ControlCandidate.OneFire);

            Assert.True(BenchPreset.TryRead(saved.Write(), out BenchPreset? read, out string problem), problem);
            Assert.Equal("Heavy ball 2", read!.Name);
            Assert.Equal(0, read.Candidate.ChangesFrom(candidate));
            Assert.Equal(candidate.Config.Hash, read.ConfigHash);
            Assert.Equal(ControlCandidate.OneFire, read.Controls);
            Assert.Equal("153+abc1234", read.Build);
            Assert.Equal(saved.Write(), read.Write());
        }

        [Fact]
        public void The_text_records_the_format_the_build_the_controls_the_config_and_every_knob()
        {
            BenchPreset saved = Preset("first");
            string[] lines = saved.Write().Split('\n');
            Assert.Equal("eleven-preset/1", lines[0]);
            Assert.Contains("name=first", lines);
            Assert.Contains("build=153+abc1234", lines);
            Assert.Contains("controls=split", lines);
            Assert.Contains($"configFormat={SimConfig.FormatVersion}", lines);
            Assert.Contains($"configHash={saved.ConfigHash:x16}", lines);
            Assert.Contains($"knobs={BenchCandidate.Knobs.Count}", lines);
            int values = Array.IndexOf(lines, "values:");
            Assert.True(values > 0);
            Assert.Equal(BenchCandidate.Knobs.Select(k => k.Name), lines.Skip(values + 1).Where(l => l.Length > 0).Select(l => l.Substring(0, l.IndexOf('='))));
        }

        [Theory]
        [InlineData("", "needs a name")]
        [InlineData("   ", "needs a name")]
        [InlineData("a name that is far too long to show", "at most 24")]
        [InlineData("technical reference", "built-in")]
        [InlineData("slash/name", "letters, digits")]
        [InlineData("two\nlines", "letters, digits")]
        public void A_name_that_cannot_be_used_is_refused_with_the_reason(string name, string reason)
        {
            Assert.False(BenchPreset.TryMake(name, BenchCandidate.Reference, ControlCandidate.ActionSurface, "1", out BenchPreset? preset, out string problem));
            Assert.Null(preset);
            Assert.Contains(reason, problem);
        }

        [Fact]
        public void A_name_is_trimmed_and_case_does_not_tell_two_apart()
        {
            Assert.Equal("Loose touch", Preset("  Loose touch ").Name);
            Assert.True(BenchPreset.SameName("Loose touch", "loose TOUCH "));
            Assert.False(BenchPreset.SameName("Loose touch", "Loose touch 2"));
        }

        // Loading an incompatible preset fails visibly rather than partly applying it.
        [Fact]
        public void A_preset_that_does_not_fit_this_build_is_refused_whole_with_the_reason()
        {
            string good = Preset("first").Write();

            void Refused(string text, string reason)
            {
                Assert.False(BenchPreset.TryRead(text, out BenchPreset? preset, out string problem), $"read: {reason}");
                Assert.Null(preset);
                Assert.Contains(reason, problem);
            }

            Refused("", "isn't an ELEVEN preset");
            Refused("kickSpeed=1\n", "isn't an ELEVEN preset");
            Refused(good.Replace("eleven-preset/1", "eleven-preset/2"), "format 2; this build reads format 1");
            Refused(good.Replace($"configFormat={SimConfig.FormatVersion}", $"configFormat={SimConfig.FormatVersion - 1}"), $"config format {SimConfig.FormatVersion - 1}; this build's is {SimConfig.FormatVersion}");
            Refused(good.Replace("controls=split", "controls=two-button"), "two-button");
            Refused(good.Replace("name=first", "name=technical reference"), "built-in");

            // A knob this build doesn't have, one missing, and one out of its range.
            Refused(good + "windSpeed=0000000000000000\n", "windSpeed");
            string[] lines = good.Split('\n');
            Refused(string.Join("\n", lines.Where(l => !l.StartsWith("gravity=", StringComparison.Ordinal))), "Missing Gravity");
            Refused(string.Join("\n", lines.Select(l => l.StartsWith("kickSpeed=", StringComparison.Ordinal) ? "kickSpeed=" + BenchCandidate.Bits(1e9) : l)), "Kick speed");
            Refused(good.Replace($"configHash={Preset("first").ConfigHash:x16}", $"configHash={Preset("first").ConfigHash ^ 1:x16}"), "recorded config");
            Refused(good.Replace($"configHash={Preset("first").ConfigHash:x16}", "configHash=not-a-hash"), "recorded config");

            // The values of another config under this one's hash: the hash it recorded doesn't match.
            string other = Preset("first", Changed(Set("kickSpeed", 19))).Write();
            string swapped = string.Join("\n", other.Split('\n').Select(l => l.StartsWith("configHash=", StringComparison.Ordinal) ? lines.First(g => g.StartsWith("configHash=", StringComparison.Ordinal)) : l));
            Refused(swapped, "recorded config");

            // Its head cut off before the values.
            Refused(string.Join("\n", lines.TakeWhile(l => l != "values:")), "no values");
        }

        // The example saved preset #46 records: `eleven-sim preset` reads it and writes its curves.
        // When a knob or the config format changes, it must be regenerated, as a real one would be.
        [Fact]
        public void The_example_preset_fixture_reads_in_this_build()
        {
            string path = System.IO.Path.Combine(BallCurvesTests.RepositoryRoot(), "tests", "Eleven.Simulation.Tests", "Fixtures", "example-preset.txt");
            Assert.True(BenchPreset.TryRead(System.IO.File.ReadAllText(path), out BenchPreset? example, out string problem), problem);
            Assert.Equal("Example", example!.Name);
            Assert.Equal(ControlCandidate.OneFire, example.Controls);
            Assert.Equal(3, example.Candidate.ChangesFrom(BenchCandidate.Reference));
            Assert.Equal(18.5, example.Candidate.Get("kickSpeed"));
        }

        [Fact]
        public void A_preset_saved_under_attached_ball_control_reads_back_under_it()
        {
            BenchCandidate attached = Changed(Set("kickSpeed", 18.5)).WithControlModel(ControlModel.Attached);
            BenchPreset saved = Preset("Attached one", attached);
            Assert.Equal(ControlModel.Attached, saved.Model);
            Assert.Contains("\nmodel=attached\n", saved.Write());

            Assert.True(BenchPreset.TryRead(saved.Write(), out BenchPreset? read, out string problem), problem);
            Assert.Equal(ControlModel.Attached, read!.Model);
            Assert.Equal(attached.Config.Hash, read.ConfigHash);
            Assert.Equal(saved.Write(), read.Write());

            // FREE is recorded too, and a model this build doesn't have is refused.
            Assert.Contains("\nmodel=free\n", Preset("Free one").Write());
            Assert.False(BenchPreset.TryRead(saved.Write().Replace("model=attached", "model=glued"), out _, out problem));
            Assert.Contains("glued", problem);
            Assert.False(BenchPreset.TryRead(saved.Write().Replace("model=attached\n", ""), out _, out problem));
            Assert.Contains("model", problem);

            // The model is part of what the hash checks.
            Assert.False(BenchPreset.TryRead(saved.Write().Replace("model=attached", "model=free"), out _, out problem));
            Assert.Contains("can't be loaded", problem);
        }

        [Fact]
        public void The_matching_preset_has_the_same_values_model_and_touch_candidate()
        {
            BenchCandidate values = Changed(Set("maxSpeed", 7.5));
            var shelf = new PresetShelf();
            shelf.Save(Preset("under A", values, ControlCandidate.ActionSurface));
            shelf.Save(Preset("under C", values, ControlCandidate.OneFire));
            shelf.Save(Preset("attached C", values.WithControlModel(ControlModel.Attached), ControlCandidate.OneFire));

            Assert.Equal("under A", shelf.Matching(values, ControlCandidate.ActionSurface)!.Name);
            Assert.Equal("under C", shelf.Matching(values, ControlCandidate.OneFire)!.Name);
            Assert.Equal("attached C", shelf.Matching(values.WithControlModel(ControlModel.Attached), ControlCandidate.OneFire)!.Name);
            Assert.Null(shelf.Matching(values, ControlCandidate.Split));
            Assert.Null(shelf.Matching(values.WithControlModel(ControlModel.Attached), ControlCandidate.ActionSurface));
        }

        [Fact]
        public void The_ball_control_tools_measure_the_footballer_they_are_given()
        {
            SimConfig config = SpikeScenario.Config();
            PlayerCapabilities slow = PlayerCapabilities.Reference.With("maxSpeed", 5);

            DribbleCurve stop = DribbleCurves.Characterise(config).First(c => c.Case == "sprint-stop");
            DribbleCurve slowStop = DribbleCurves.Characterise(config, slow).First(c => c.Case == "sprint-stop");
            Assert.NotEqual(stop.MaxSeparationMetres, slowStop.MaxSeparationMetres);

            Assert.NotEqual(TurnCurves.KeyNumbers(TurnCurves.Characterise(config)), TurnCurves.KeyNumbers(TurnCurves.Characterise(config, slow)));
            Assert.NotEqual(PushCurves.KeyNumbers(PushCurves.Characterise(config)), PushCurves.KeyNumbers(PushCurves.Characterise(config, slow)));

            // The "bench" profile is the given footballer's own ball control.
            Assert.Contains(PushCurves.Characterise(config, slow.With("ballControl", 0.55)), c => c.Profile == "bench" && c.BallControl == 0.55);
        }

        [Fact]
        public void The_shelf_saves_recalls_replaces_and_deletes_by_name()
        {
            var shelf = new PresetShelf();
            Assert.False(shelf.Save(Preset("first")));
            Assert.False(shelf.Save(Preset("second", Changed(Set("maxSpeed", 7.5)))));
            Assert.Equal(new[] { "first", "second" }, shelf.Presets.Select(p => p.Name));

            // The same name, in any case, replaces in place.
            BenchCandidate newer = Changed(Set("kickSpeed", 20));
            Assert.True(shelf.Save(Preset("FIRST", newer)));
            Assert.Equal(new[] { "FIRST", "second" }, shelf.Presets.Select(p => p.Name));
            Assert.Equal(0, shelf.Find("first")!.Candidate.ChangesFrom(newer));

            Assert.Same(shelf.Find("second"), shelf.Matching(Changed(Set("maxSpeed", 7.5)), ControlCandidate.Split));
            Assert.Null(shelf.Matching(BenchCandidate.Reference, ControlCandidate.Split));

            Assert.True(shelf.Delete("First"));
            Assert.False(shelf.Delete("first"));
            Assert.Null(shelf.Find("first"));
            Assert.Single(shelf.Presets);
        }

        [Fact]
        public void The_shelf_round_trips_and_keeps_what_it_cannot_read()
        {
            var shelf = new PresetShelf();
            shelf.Save(Preset("first"));
            shelf.Save(Preset("second", Changed(Set("maxSpeed", 7.5)), ControlCandidate.ActionSurface));
            string stored = shelf.Write();

            PresetShelf read = PresetShelf.Read(stored);
            Assert.Equal(new[] { "first", "second" }, read.Presets.Select(p => p.Name));
            Assert.Empty(read.Problems);
            Assert.Equal(stored, read.Write());

            // One from a later format among them: listed with its reason, and still there when stored again.
            string later = Preset("from later").Write().Replace("eleven-preset/1", "eleven-preset/9");
            PresetShelf mixed = PresetShelf.Read(stored + later + PresetShelf.Separator + "\n");
            Assert.Equal(new[] { "first", "second" }, mixed.Presets.Select(p => p.Name));
            Assert.Contains("format 9", Assert.Single(mixed.Problems));
            Assert.Contains("eleven-preset/9", mixed.Write());
            Assert.Contains("name=from later", mixed.Write());

            // Nothing stored, or rubbish: an empty shelf, never an exception.
            Assert.Empty(PresetShelf.Read(null).Presets);
            Assert.Empty(PresetShelf.Read("rubbish").Presets);
        }
    }
}
