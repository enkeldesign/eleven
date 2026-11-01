using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#38: the headless quick-pass measurements and their recorded technical reference.</summary>
    public class PassCurvesTests
    {
        private static string ReferencePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "pass-reference.csv");

        private static readonly SimConfig Config = new SimConfig();

        private static readonly List<PassCurve> Default = PassCurves.Characterise(Config, PlayerCapabilities.Reference);

        private static PassOutcome Of(string drill, string profile) => Default.Single(c => c.Drill == drill && c.Profile == profile).Outcome;

        [Fact]
        public void The_default_curves_match_the_recorded_technical_reference()
        {
            var csv = new StringWriter();
            PassCurves.WriteCsv(csv, Config, Default);

            Assert.Equal(File.ReadAllText(ReferencePath), csv.ToString());
        }

        // Perception decides the target: only a passer who sees the near wide receiver picks them.
        [Fact]
        public void Contrasting_profiles_choose_different_receivers_in_the_standard_drill()
        {
            Assert.Equal("ahead-advancing", Of("standard", "profile").Target);
            Assert.Equal("ahead-advancing", Of("standard", "literal-narrow").Target);
            Assert.Equal("wide", Of("standard", "reader-wide").Target);
            Assert.All(Default, c => Assert.NotEqual("behind-closer", c.Outcome.Target));
        }

        // Anticipation decides the lead: the more of the run a passer reads, the further ahead the
        // pass goes and the closer it comes to meeting the runner.
        [Fact]
        public void Reading_more_of_a_run_leads_it_further_and_meets_it_closer()
        {
            PassOutcome literal = Of("crossing-run", "literal-narrow"), reference = Of("crossing-run", "profile"), reader = Of("crossing-run", "reader-wide");
            Assert.True(literal.LeadMetres < reference.LeadMetres && reference.LeadMetres < reader.LeadMetres);
            Assert.True(literal.ClosestApproachMetres > reference.ClosestApproachMetres && reference.ClosestApproachMetres > reader.ClosestApproachMetres);
            Assert.True(reader.ClosestApproachMetres < 0.5, $"a full reader misses by {reader.ClosestApproachMetres} m");
            Assert.Equal(0, literal.LeadMetres, 9);
        }

        [Fact]
        public void A_pass_to_a_standing_receiver_arrives_at_the_configured_speed()
        {
            PassOutcome wide = Of("standard", "reader-wide");
            Assert.True(wide.ClosestApproachMetres < 0.5);
            Assert.InRange(wide.ArrivalSpeedMps, Config.PassArrivalSpeed - 0.2, Config.PassArrivalSpeed + 0.2);
        }

        [Fact]
        public void Curves_trace_back_to_the_build_config_and_profile_that_made_them()
        {
            SimConfig config = Config.With("passArrivalSpeed", 4.5).With("rollingFriction", 0.2);
            PlayerCapabilities profile = PlayerCapabilities.Reference.With("passVision", 0.875).With("passAnticipation", 0.625);
            List<PassCurve> curves = PassCurves.Characterise(config, profile);

            var csv = new StringWriter();
            PassCurves.WriteCsv(csv, config, curves);
            string[] rows = csv.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            Assert.Equal(curves.Count, rows.Length);
            Assert.All(rows, row => Assert.StartsWith($"{config.Hash:x16},", row));

            var summary = new StringWriter();
            PassCurves.WriteSummary(summary, config, profile, curves);
            string text = summary.ToString();
            Assert.Contains($"build: {SimulationInfo.BuildId}", text);
            Assert.Contains($"config-hash: {config.Hash:x16}", text);
            Assert.Contains("not approved feel", text);

            // Every tunable and capability is written exactly, so the summary rebuilds both.
            var recorded = new List<KeyValuePair<string, double>>();
            PlayerCapabilities capabilities = default;
            foreach (string line in text.Split('\n'))
            {
                int colon = line.IndexOf(": ", StringComparison.Ordinal);
                if (colon <= 0) continue;
                string name = line.Substring(0, colon);
                string value = line.Substring(colon + 2).Split(' ')[0];
                if (SimConfig.Tunables.Any(p => p.Name == name)) recorded.Add(new KeyValuePair<string, double>(name, double.Parse(value, CultureInfo.InvariantCulture)));
                if (PlayerCapabilities.Parameters.Any(p => p.Name == name)) capabilities = capabilities.With(name, double.Parse(value, CultureInfo.InvariantCulture));
            }

            Assert.Equal(config.Hash, new SimConfig().With(recorded).Hash);
            Assert.Equal(profile, capabilities);
        }
    }
}
