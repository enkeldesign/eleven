using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The feel test bench's candidate (#45): its knobs come from the supported metadata, it changes
    /// values only through the supported surface, it is refused whole when any value is invalid, and
    /// the same values always make the same config and practice start.
    /// </summary>
    public class BenchCandidateTests
    {
        private static KeyValuePair<string, double> Set(string name, double value) => new KeyValuePair<string, double>(name, value);

        private static BenchCandidate Changed(params KeyValuePair<string, double>[] changes)
        {
            Assert.True(BenchCandidate.Reference.TryWith(changes, out BenchCandidate? result, out string problem), problem);
            Assert.NotNull(result);
            return result!;
        }

        [Fact]
        public void The_knobs_are_the_config_tunables_then_the_player_capabilities_then_the_touch_gestures()
        {
            var expected = SimConfig.Tunables.Concat(PlayerCapabilities.Parameters).Concat(ActionSurfaceSettings.Parameters).ToList();
            int players = SimConfig.Tunables.Count;
            int touch = players + PlayerCapabilities.Parameters.Count;
            Assert.Equal(expected.Count, BenchCandidate.Knobs.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Same(expected[i], BenchCandidate.Knobs[i].Parameter);
                Assert.Equal(i >= players && i < touch, BenchCandidate.Knobs[i].OnPlayer);
                Assert.Equal(i >= touch, BenchCandidate.Knobs[i].OnAdapter);
            }
        }

        [Fact]
        public void Every_knob_has_a_group_a_label_and_a_step_inside_its_range()
        {
            var groups = new[] { TunableParameter.BallGroup, TunableParameter.KickGroup, TunableParameter.PassGroup, TunableParameter.PlayerGroup, ActionSurfaceSettings.Group };
            Assert.NotEmpty(BenchCandidate.Knobs);
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                Assert.Contains(k.Group, groups);
                Assert.Equal(k.OnPlayer, k.Group == TunableParameter.PlayerGroup);
                Assert.Equal(k.OnAdapter, k.Group == ActionSurfaceSettings.Group);
                Assert.False(string.IsNullOrWhiteSpace(k.Label), k.Name);
                Assert.True(char.IsUpper(k.Label[0]), k.Label);
                Assert.True(k.Step > 0 && k.Step <= k.Parameter.Max - k.Parameter.Min, $"{k.Name} step {k.Step}");

                // 1, 2 or 5 × 10ⁿ, and shown with exactly its decimal places.
                double mantissa = k.Step * Math.Pow(10, k.StepDecimals);
                Assert.Contains(Math.Round(mantissa, 9), new[] { 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0, 200.0, 500.0 });
                Assert.Equal(k.Step, Math.Round(k.Step, k.StepDecimals));
            }
        }

        [Theory]
        [InlineData("kickSpeed", "Kick speed", 0.5)]          // starting range 15–32 m/s
        [InlineData("magnusCoefficient", "Magnus coefficient", 0.0002)] // 0.004–0.008
        [InlineData("rollingFriction", "Rolling friction", 0.01)]       // 0.05–0.3
        [InlineData("maxSpeed", "Max speed", 0.1)]            // 7–10 m/s
        [InlineData("acceleration", "Acceleration", 0.5)]     // 12–30 m/s²
        [InlineData("aftertouchSeconds", "Aftertouch seconds", 0.005)] // 0.15–0.30 s
        [InlineData("gravity", "Gravity", 2)]                 // no starting range: valid 0.1–50
        [InlineData("passVision", "Pass vision", 0.1)]        // valid 0–π
        [InlineData("actionHoldSeconds", "Action hold seconds", 0.005)] // 0.12–0.25 s
        [InlineData("actionFlickDistance", "Action flick distance", 1)] // 12–32 pt
        public void Labels_come_from_the_names_and_steps_fit_twenty_times_into_the_starting_range(string name, string label, double step)
        {
            BenchKnob knob = BenchCandidate.Knobs.SingleOrDefault(k => k.Name == name)
                ?? throw new Xunit.Sdk.XunitException($"no knob {name}");
            Assert.Equal(label, knob.Label);
            Assert.Equal(step, knob.Step);
        }

        [Fact]
        public void The_reference_candidate_is_the_technical_reference()
        {
            BenchCandidate reference = BenchCandidate.Reference;
            Assert.Equal(SpikeScenario.Config().Hash, reference.Config.Hash);
            Assert.Equal(PlayerCapabilities.Reference, reference.Capabilities);
            var spike = new Simulator(SpikeScenario.Config(), SpikeScenario.InitialState(SpikeScenario.Config()));
            Assert.Equal(spike.StateHash(), reference.PracticeStartHash());
            Assert.Equal(BenchCandidate.ReferencePreset, reference.PresetName);
            Assert.Equal(ActionSurfaceSettings.Reference.Describe(), reference.ActionSurface.Describe());
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                double expected = k.OnAdapter ? ActionSurfaceSettings.Reference.Get(k.Name)
                    : k.OnPlayer ? PlayerCapabilities.Reference.Get(k.Name) : SpikeScenario.Config().Get(k.Name);
                Assert.Equal(expected, reference.Get(k.Name));
            }
        }

        [Fact]
        public void A_touch_knob_changes_the_adapter_and_never_the_config_or_practice_start()
        {
            BenchCandidate touch = Changed(Set("actionHoldSeconds", 0.2), Set("actionAftertouchRange", 60));

            Assert.Equal(0.2, touch.ActionSurface.HoldSeconds);
            Assert.Equal(60, touch.ActionSurface.AftertouchRange);
            Assert.Equal(0.2, touch.Get("actionHoldSeconds"));
            Assert.Equal(BenchCandidate.Reference.Config.Hash, touch.Config.Hash);
            Assert.Equal(BenchCandidate.Reference.PracticeStartHash(), touch.PracticeStartHash());
            Assert.Equal(2, touch.ChangesFrom(BenchCandidate.Reference));
            Assert.Equal(BenchCandidate.UnsavedPreset, touch.PresetName);

            Assert.True(BenchCandidate.TryParse(touch.Values(), out BenchCandidate? parsed, out string problem), problem);
            Assert.Equal(touch.ActionSurface.Describe(), parsed!.ActionSurface.Describe());
            Assert.Equal(0, parsed.ChangesFrom(touch));
        }

        [Fact]
        public void The_same_values_always_give_the_same_config_hash_and_practice_start()
        {
            BenchCandidate a = Changed(Set("kickSpeed", 28), Set("maxSpeed", 8.5), Set("rollingFriction", 0.22));
            BenchCandidate b = Changed(Set("rollingFriction", 0.22), Set("maxSpeed", 8.5), Set("kickSpeed", 28));
            Assert.True(BenchCandidate.TryParse(a.Values(), out BenchCandidate? c, out string problem), problem);

            foreach (BenchCandidate other in new[] { b, c! })
            {
                Assert.Equal(a.Config.Hash, other.Config.Hash);
                Assert.Equal(a.PracticeStartHash(), other.PracticeStartHash());
                Assert.Equal(0, a.ChangesFrom(other));
            }

            Assert.NotEqual(BenchCandidate.Reference.Config.Hash, a.Config.Hash);
            Assert.NotEqual(BenchCandidate.Reference.PracticeStartHash(), a.PracticeStartHash());
            Assert.Equal(3, a.ChangesFrom(BenchCandidate.Reference));
            Assert.Equal(8.5, a.Capabilities.MaxSpeed);
            Assert.Equal(8.5, a.PracticeStart().Players[0].Capabilities.MaxSpeed);
            Assert.Equal(BenchCandidate.UnsavedPreset, a.PresetName);
        }

        [Fact]
        public void Changing_a_value_back_returns_to_the_reference()
        {
            BenchCandidate changed = Changed(Set("kickSpeed", 28));
            Assert.True(changed.TryWith(new[] { Set("kickSpeed", BenchCandidate.Reference.Get("kickSpeed")) }, out BenchCandidate? back, out string problem), problem);
            Assert.Equal(0, back!.ChangesFrom(BenchCandidate.Reference));
            Assert.Equal(BenchCandidate.Reference.Config.Hash, back.Config.Hash);
            Assert.Equal(BenchCandidate.ReferencePreset, back.PresetName);
        }

        [Fact]
        public void Values_cross_the_page_boundary_bit_for_bit()
        {
            double third = 1.0 / 3;
            double almostPi = Math.PI - 1e-15;
            BenchCandidate a = Changed(Set("bounceRestitution", 0.1 + 0.2 + third / 10), Set("passVision", almostPi), Set("magnusCoefficient", 0.006000000000000001));
            Assert.True(BenchCandidate.TryParse(a.Values(), out BenchCandidate? b, out string problem), problem);
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Get(k.Name)), BitConverter.DoubleToInt64Bits(b!.Get(k.Name)));
            }

            Assert.Equal("400921fb54442d18", BenchCandidate.Bits(Math.PI));
            Assert.Equal("8000000000000000", BenchCandidate.Bits(-0.0));
        }

        public static IEnumerable<object[]> OutOfRange()
        {
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                double span = k.Parameter.Max - k.Parameter.Min;
                yield return new object[] { k.Name, k.Parameter.Min - span * 0.01 };
                yield return new object[] { k.Name, k.Parameter.Max + span * 0.01 };
            }

            // Always some rows, so the theory has data even before the knobs exist.
            yield return new object[] { "kickSpeed", double.NaN };
            yield return new object[] { "maxSpeed", double.PositiveInfinity };
            yield return new object[] { "maxSpeed", 0.5 };
            yield return new object[] { "kickSpeed", 61.0 };
        }

        [Theory]
        [MemberData(nameof(OutOfRange))]
        public void Values_outside_the_valid_range_are_refused_whole(string name, double value)
        {
            var changes = new[] { Set("rollingFriction", 0.2), Set(name, value) };
            Assert.False(BenchCandidate.Reference.TryWith(changes, out BenchCandidate? result, out string problem));
            Assert.Null(result);
            BenchKnob knob = BenchCandidate.Knobs.Single(k => k.Name == name);
            Assert.Contains(knob.Label, problem);
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                Assert.Contains(knob.Parameter.Unit, problem);
                Assert.Contains(knob.Parameter.Max.ToString("G6", CultureInfo.InvariantCulture), problem);
            }

            // The same through the page format: one bad line refuses the lot.
            string text = string.Join("\n", BenchCandidate.Reference.Values().Split('\n')
                .Select(line => line.StartsWith(name + "=", StringComparison.Ordinal) ? $"{name}={BenchCandidate.Bits(value)}" : line));
            Assert.False(BenchCandidate.TryParse(text, out BenchCandidate? parsed, out string parseProblem));
            Assert.Null(parsed);
            Assert.Contains(knob.Label, parseProblem);
        }

        [Fact]
        public void Both_ends_of_every_valid_range_can_be_applied()
        {
            Assert.NotEmpty(BenchCandidate.Knobs);
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                foreach (double end in new[] { k.Parameter.Min, k.Parameter.Max })
                {
                    Assert.True(BenchCandidate.Reference.TryWith(new[] { Set(k.Name, end) }, out BenchCandidate? result, out string problem), $"{k.Name}={end}: {problem}");
                    Assert.Equal(end, result!.Get(k.Name));
                }
            }
        }

        [Theory]
        [InlineData("", "Missing")]
        [InlineData("noSuchKnob=4039000000000000", "noSuchKnob")]
        [InlineData("kickSpeed=40390000000000", "Kick speed")]   // too short
        [InlineData("kickSpeed=zz39000000000000", "Kick speed")] // not hex
        [InlineData("kickSpeed", "Kick speed")]                  // no value
        public void Malformed_page_values_are_refused_with_a_reason(string badLine, string named)
        {
            List<string> lines = BenchCandidate.Reference.Values().Split('\n').Where(l => l.Length > 0 && !l.StartsWith("kickSpeed=", StringComparison.Ordinal)).ToList();
            if (badLine.Length > 0) lines.Add(badLine);
            if (badLine.StartsWith("noSuchKnob", StringComparison.Ordinal)) lines.Add($"kickSpeed={BenchCandidate.Bits(25)}");
            Assert.False(BenchCandidate.TryParse(string.Join("\n", lines), out BenchCandidate? parsed, out string problem));
            Assert.Null(parsed);
            Assert.Contains(named, problem);
        }

        [Fact]
        public void A_knob_given_twice_is_refused()
        {
            string text = BenchCandidate.Reference.Values() + $"\nkickSpeed={BenchCandidate.Bits(30)}";
            Assert.False(BenchCandidate.TryParse(text, out BenchCandidate? parsed, out string problem));
            Assert.Null(parsed);
            Assert.Contains("Kick speed", problem);
            Assert.Contains("twice", problem);
        }

        [Fact]
        public void A_candidate_never_changes_the_values_it_came_from()
        {
            BenchCandidate reference = BenchCandidate.Reference;
            ulong before = reference.Config.Hash;
            _ = Changed(Set("kickSpeed", 30), Set("maxSpeed", 9));
            Assert.Equal(before, reference.Config.Hash);
            Assert.Equal(PlayerCapabilities.Reference, reference.Capabilities);
            Assert.Equal(25, reference.Get("acceleration"));
        }

        [Fact]
        public void The_page_description_lists_every_knob_with_its_metadata_and_value()
        {
            BenchCandidate candidate = Changed(Set("kickSpeed", 27.5));
            string[] lines = candidate.Describe().Split('\n').Where(l => l.Length > 0).ToArray();
            Assert.Equal(BenchCandidate.Knobs.Count, lines.Length);
            Assert.NotEmpty(lines);
            for (int i = 0; i < lines.Length; i++)
            {
                BenchKnob k = BenchCandidate.Knobs[i];
                string[] f = lines[i].Split('\t');
                Assert.Equal(11, f.Length);
                Assert.Equal(new[] { k.Group, k.Name, k.Label, k.Parameter.Unit }, f.Take(4));
                Assert.Equal(BenchCandidate.Bits(k.Parameter.Min), f[4]);
                Assert.Equal(BenchCandidate.Bits(k.Parameter.Max), f[5]);
                Assert.Equal(k.Parameter.StartMin.HasValue ? BenchCandidate.Bits(k.Parameter.StartMin.Value) : "-", f[6]);
                Assert.Equal(k.Parameter.StartMax.HasValue ? BenchCandidate.Bits(k.Parameter.StartMax.Value) : "-", f[7]);
                Assert.Equal(BenchCandidate.Bits(k.Step), f[8]);
                Assert.Equal(k.StepDecimals.ToString(CultureInfo.InvariantCulture), f[9]);
                Assert.Equal(BenchCandidate.Bits(candidate.Get(k.Name)), f[10]);
            }
        }

        [Fact]
        public void A_practice_run_under_a_candidate_steps_like_any_other()
        {
            BenchCandidate candidate = Changed(Set("maxSpeed", 9.5), Set("acceleration", 15), Set("kickSpeed", 20));
            var simulator = new Simulator(candidate.Config, candidate.PracticeStart());
            Assert.Equal(candidate.PracticeStartHash(), simulator.StateHash());
            var events = new List<SimEvent>();
            for (long t = 0; t < 120; t++)
            {
                simulator.Step(new[] { new Command(SpikeScenario.ControllerId, t, new Vec2(1, 0)) }, events);
            }

            // Ten-plus metres in two seconds at 9.5 m/s, more than the reference player's 7 m/s allows.
            Assert.True(simulator.PeekState().Players[0].Position.X > 10 + 7 * 2, $"x = {simulator.PeekState().Players[0].Position.X}");
        }
    }
}
