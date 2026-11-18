using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #129: the quick-sim for matches nobody plays. A Dixon–Coles model of each side's goals from
    /// its tier and capabilities, its reference fitted to the full simulation, compared with it over
    /// fixed seeds. Deterministic, isolated from any played match, and its result is the contract's.
    /// </summary>
    public class QuickSimTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private static QuickSide Side(int tier, PlayerCapabilities? caps = null) => new QuickSide(tier, caps ?? PlayerCapabilities.Reference);

        [Fact]
        public void The_same_sides_and_seed_give_the_same_result_and_seeds_vary_it()
        {
            string a = QuickSim.Play(C, Side(1), Side(2), 7).ToText(), b = QuickSim.Play(C, Side(1), Side(2), 7).ToText();
            Assert.Equal(a, b);
            var texts = new HashSet<string>();
            for (ulong seed = 0; seed < 40; seed++) texts.Add(QuickSim.Play(C, Side(1), Side(2), seed).ToText());
            Assert.True(texts.Count > 10, $"{texts.Count} different results over 40 seeds");
        }

        [Fact]
        public void Its_result_is_the_contracts_with_each_goal_timed_in_a_half()
        {
            for (ulong seed = 0; seed < 60; seed++)
            {
                MatchResult r = QuickSim.Play(C, Side(3), Side(0), seed, homeTeam: 5, awayTeam: 9);
                Assert.Equal((5, 9), (r.HomeTeam, r.AwayTeam));
                Assert.StartsWith("eleven-result/7\n", r.ToText());
                Assert.Equal(r.HomeGoals + r.AwayGoals, r.Goals.Count);
                Assert.Equal(r.HomeGoals, r.Goals.Count(g => g.Team == 5));
                Assert.Equal(2 * C.HalfLength, r.PlayedSeconds, 9);
                double last = -1;
                foreach (ResultGoal g in r.Goals)
                {
                    Assert.InRange(g.Half, 1, 2);
                    Assert.InRange(g.ClockSeconds, 0, C.HalfLength);
                    double at = (g.Half - 1) * C.HalfLength + g.ClockSeconds;
                    Assert.True(at >= last, "goals in the order they were scored");
                    last = at;
                    Assert.Equal(0, g.Scorer); // no footballer plays
                }

                Assert.InRange(r.HomeCautions, 0, 20);
                Assert.InRange(r.HomeSendingsOff, 0, 5);
            }
        }

        [Fact]
        public void The_scorelines_chances_sum_to_one_and_take_the_low_score_correction()
        {
            QuickSimModel m = QuickSimModel.Reference;
            (double lh, double la) = (1.4, 1.1);
            double sum = 0;
            for (int x = 0; x <= QuickSim.Limit(lh); x++)
            {
                for (int y = 0; y <= QuickSim.Limit(la); y++) sum += QuickSim.Probability(x, y, lh, la, m.Rho);
            }

            Assert.Equal(1.0, sum, 9);

            // Dixon–Coles: 1–1 against 2–2 differs from independent Poisson by τ(1,1) = 1 − ρ alone.
            double p11 = QuickSim.Probability(1, 1, lh, la, 0.1), p22 = QuickSim.Probability(2, 2, lh, la, 0.1);
            double q11 = QuickSim.Probability(1, 1, lh, la, 0), q22 = QuickSim.Probability(2, 2, lh, la, 0);
            Assert.Equal(1 - 0.1, (p11 / p22) / (q11 / q22), 9);
        }

        [Theory]
        [InlineData(60)]
        [InlineData(180)]
        [InlineData(360)]
        [InlineData(600)]
        public void Over_every_supported_half_the_scorelines_are_a_distribution_with_the_rates_means(double half)
        {
            // Codex's findings on #149: at long halves the rates grow, τ(0,0) must stay non-negative,
            // and the grid must hold the goals the rates give rather than cut them at a fixed count.
            QuickSimModel m = QuickSimModel.Reference;
            for (int h = 0; h < QuickSimModel.Tiers; h++)
            {
                for (int a = 0; a < QuickSimModel.Tiers; a++)
                {
                    (double lh, double la) = QuickSim.Rates(Side(h), Side(a), half, m);
                    double sum = 0, meanHome = 0, meanAway = 0;
                    for (int x = 0; x <= 60; x++)
                    {
                        for (int y = 0; y <= 60; y++)
                        {
                            double p = QuickSim.Probability(x, y, lh, la, m.Rho);
                            Assert.True(p >= 0, $"half {half}, tiers {h}–{a}: P({x},{y}) = {p}");
                            sum += p;
                            meanHome += x * p;
                            meanAway += y * p;
                        }
                    }

                    Assert.Equal(1, sum, 9);

                    // Dixon–Coles moves only the low scores: the means stay within 2 % of the rates.
                    Assert.True(Math.Abs(meanHome - lh) <= 0.02 * lh + 0.02, $"half {half}, tiers {h}–{a}: home mean {meanHome:F3} against λ {lh:F3}");
                    Assert.True(Math.Abs(meanAway - la) <= 0.02 * la + 0.02, $"half {half}, tiers {h}–{a}: away mean {meanAway:F3} against λ {la:F3}");
                }
            }
        }

        [Theory]
        [InlineData(60)]
        [InlineData(180)]
        [InlineData(360)]
        [InlineData(600)]
        public void Sides_at_the_capability_extremes_still_give_a_distribution_and_a_result(double half)
        {
            // Codex's finding on #150: every capability at its best against every one at its worst is
            // valid side data, a capability index gap of 1. With the refitted weight the rate ran to
            // about 60,000 goals, past the portable Exp's range and the grid's limit.
            PlayerCapabilities best = PlayerCapabilities.Reference, worst = PlayerCapabilities.Reference;
            foreach (TunableParameter p in PlayerCapabilities.Parameters)
            {
                bool lessIsBetter = p.Name == "kickError";
                best = best.With(p.Name, lessIsBetter ? p.Min : p.Max);
                worst = worst.With(p.Name, lessIsBetter ? p.Max : p.Min);
            }

            Assert.Equal(1, QuickSim.CapabilityIndex(best) - QuickSim.CapabilityIndex(worst), 9);
            QuickSimModel m = QuickSimModel.Reference;
            foreach ((QuickSide home, QuickSide away) in new[] { (Side(3, best), Side(0, worst)), (Side(0, worst), Side(3, best)), (Side(1, best), Side(1, worst)) })
            {
                (double lh, double la) = QuickSim.Rates(home, away, half, m);
                foreach (double rate in new[] { lh, la })
                {
                    Assert.True(rate > 0 && rate <= QuickSim.MaxRate, $"half {half}: rate {rate} outside (0, {QuickSim.MaxRate}]");
                }

                double sum = 0;
                for (int x = 0; x <= 120; x++)
                {
                    for (int y = 0; y <= 120; y++)
                    {
                        double p = QuickSim.Probability(x, y, lh, la, m.Rho);
                        Assert.True(p >= 0 && !double.IsNaN(p), $"half {half}: P({x},{y}) = {p}");
                        sum += p;
                    }
                }

                Assert.Equal(1, sum, 9);

                MatchResult r = QuickSim.Play(C.With("halfLength", half), home, away, 7);
                Assert.Equal(r.HomeGoals + r.AwayGoals, r.Goals.Count);
            }
        }

        [Fact]
        public void A_better_side_scores_more_and_home_has_its_advantage()
        {
            QuickSimModel m = QuickSimModel.Reference;
            Assert.True(QuickSim.Rating(Side(3), m) > QuickSim.Rating(Side(0), m));
            PlayerCapabilities strong = PlayerCapabilities.Reference.With("strength", 0.8).With("tackling", 0.8).With("stamina", 0.8);
            Assert.True(QuickSim.CapabilityIndex(strong) > 0);
            Assert.Equal(0, QuickSim.CapabilityIndex(PlayerCapabilities.Reference), 12);
            Assert.True(QuickSim.CapabilityIndex(PlayerCapabilities.Reference.With("kickError", 0.2)) < 0, "more kick error is worse");

            (double h, double a) = QuickSim.Rates(Side(3), Side(0), QuickSim.ReferenceHalf, m);
            Assert.True(h > a);
            (double lh, double la) = QuickSim.Rates(Side(1), Side(1), QuickSim.ReferenceHalf, m);
            Assert.Equal(System.Math.Exp(m.Home), lh / la, 9); // between level sides, home's advantage alone: its sign is the fit's
            (double sh, _) = QuickSim.Rates(Side(1), Side(1), QuickSim.ReferenceHalf / 2, m);
            Assert.Equal(lh / 2, sh, 12); // rates scale with the half's length
        }

        [Fact]
        public void A_quick_simmed_match_shares_no_state_with_a_played_one()
        {
            string before = QuickSim.Play(C, Side(2), Side(1), 0x129000001UL).ToText();

            // A full match on the same seed, played and stepped between two quick-sims of it.
            var sim = new Simulator(C, Squads.Match(C, 0x129000001UL, 10, bothCpu: true));
            ulong start = sim.StateHash();
            var commands = new List<Command>();
            for (int i = 0; i < 600; i++)
            {
                commands.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, new List<SimEvent>());
                if (i == 300) Assert.Equal(before, QuickSim.Play(C, Side(2), Side(1), 0x129000001UL).ToText());
            }

            Assert.Equal(before, QuickSim.Play(C, Side(2), Side(1), 0x129000001UL).ToText());

            // And the played match is the one it would be without the quick-sims.
            var alone = new Simulator(C, Squads.Match(C, 0x129000001UL, 10, bothCpu: true));
            Assert.Equal(start, alone.StateHash());
            for (int i = 0; i < 600; i++)
            {
                commands.Clear();
                KeeperControllers.Append(alone, commands);
                CpuControllers.Append(alone, commands);
                RestartControllers.Append(alone, commands);
                alone.Step(commands, new List<SimEvent>());
            }

            Assert.Equal(alone.StateHash(), sim.StateHash());
        }

        // ---- Calibration against the full simulation (docs/simulation/quicksim-calibration-reference.csv) ----

        internal static List<CalibrationMatch> Sample() =>
            QuickSimCalibration.Read(File.ReadAllLines(Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "quicksim-calibration-reference.csv")), League.Sides());

        // The quick-sim over the sample's own fixtures, `draws` times each on derived seeds.
        private static List<MatchResult> QuickOver(List<CalibrationMatch> sample, int draws)
        {
            IReadOnlyList<LeagueSide> sides = League.Sides();
            var results = new List<MatchResult>();
            foreach (CalibrationMatch p in sample)
            {
                for (int d = 0; d < draws; d++)
                {
                    results.Add(QuickSim.Play(C, QuickSides.Of(sides[p.Home]), QuickSides.Of(sides[p.Away]), p.Seed * 1000 + (ulong)d));
                }
            }

            return results;
        }

        // χ² of two samples' counts over the same bins (Press et al.'s two-binned-data form).
        private static double ChiSquared(double[] a, double[] b)
        {
            double na = a.Sum(), nb = b.Sum(), chi = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] + b[i] == 0) continue;
                double t = Math.Sqrt(nb / na) * a[i] - Math.Sqrt(na / nb) * b[i];
                chi += t * t / (a[i] + b[i]);
            }

            return chi;
        }

        // The 1 % critical values of χ² by degrees of freedom.
        private static readonly double[] Critical = { 0, 6.63, 9.21, 11.34, 13.28, 15.09, 16.81 };

        [Fact]
        public void The_reference_model_is_the_fit_to_the_full_simulations_sample()
        {
            QuickSimModel fit = QuickSimFit.Fit(Sample(), League.Sides());
            QuickSimModel r = QuickSimModel.Reference;
            Assert.Equal(fit.BaseRate, r.BaseRate, 3);
            Assert.Equal(fit.Home, r.Home, 3);
            for (int t = 0; t < QuickSimModel.Tiers; t++) Assert.Equal(fit.TierRating(t), r.TierRating(t), 3);
            Assert.Equal(fit.CapabilityWeight, r.CapabilityWeight, 2);
            Assert.Equal(fit.Rho, r.Rho, 3);
            Assert.Equal(fit.FoulsPerSide, r.FoulsPerSide, 3);
            Assert.Equal(fit.CautionsPerSide, r.CautionsPerSide, 3);
            Assert.Equal(fit.SendingsOffPerSide, r.SendingsOffPerSide, 3);
        }

        [Fact]
        public void Its_distributions_match_the_full_simulation_over_the_same_fixtures()
        {
            List<CalibrationMatch> sample = Sample();
            Assert.True(sample.Count >= 60, $"{sample.Count} full matches in the sample");
            const int Draws = 50;
            List<MatchResult> quick = QuickOver(sample, Draws);

            // Goals a side: the quick-sim's mean within two standard errors of the sample's.
            void Mean(string what, Func<CalibrationMatch, double> full, Func<MatchResult, double> sim)
            {
                double[] f = sample.Select(full).ToArray();
                double mean = f.Average(), se = Math.Sqrt(f.Select(x => (x - mean) * (x - mean)).Sum() / (f.Length - 1) / f.Length);
                double q = quick.Average(sim);
                Assert.True(Math.Abs(q - mean) <= 2 * se + 1e-9, $"{what}: quick-sim {q:F3} against the full simulation's {mean:F3} ± {se:F3}");
            }

            Mean("home goals", p => p.HomeGoals, r => r.HomeGoals);
            Mean("away goals", p => p.AwayGoals, r => r.AwayGoals);
            Mean("cautions", p => p.Cautions, r => r.HomeCautions + r.AwayCautions);
            Mean("fouls", p => p.Fouls, r => r.HomeFouls + r.AwayFouls);

            // Win, draw, loss; and the match's goals in bins 0, 1, 2, 3, 4, 5, 6+: χ² at 1 %.
            double[] Outcome<T>(IEnumerable<T> rows, Func<T, int> home, Func<T, int> away)
            {
                var o = new double[3];
                foreach (T r in rows) o[Math.Sign(home(r) - away(r)) + 1]++;
                return o;
            }

            double[] Totals<T>(IEnumerable<T> rows, Func<T, int> total)
            {
                var o = new double[7];
                foreach (T r in rows) o[Math.Min(6, total(r))]++;
                return o;
            }

            double wdl = ChiSquared(Outcome(sample, p => p.HomeGoals, p => p.AwayGoals), Outcome(quick, r => r.HomeGoals, r => r.AwayGoals));
            Assert.True(wdl <= Critical[2], $"win/draw/loss χ² {wdl:F2} against {Critical[2]}");
            double totals = ChiSquared(Totals(sample, p => p.HomeGoals + p.AwayGoals), Totals(quick, r => r.HomeGoals + r.AwayGoals));
            Assert.True(totals <= Critical[6], $"goals-a-match χ² {totals:F2} against {Critical[6]}");

            // And each side's goal difference a match: the quick-sim's within three standard errors of
            // the full simulation's, which plays each side 20 times.
            IReadOnlyList<LeagueSide> sides = League.Sides();
            int k = 0;
            for (int side = 0; side < sides.Count; side++)
            {
                var full = new List<double>();
                foreach (CalibrationMatch p in sample)
                {
                    if (p.Home == side) full.Add(p.HomeGoals - p.AwayGoals);
                    else if (p.Away == side) full.Add(p.AwayGoals - p.HomeGoals);
                }

                double mean = full.Average(), se = Math.Sqrt(full.Select(x => (x - mean) * (x - mean)).Sum() / (full.Count - 1) / full.Count);
                double sum = 0;
                int n = 0;
                k = 0;
                foreach (CalibrationMatch p in sample)
                {
                    for (int d = 0; d < Draws; d++, k++)
                    {
                        if (p.Home == side) sum += quick[k].HomeGoals - quick[k].AwayGoals;
                        else if (p.Away == side) sum += quick[k].AwayGoals - quick[k].HomeGoals;
                        else continue;
                        n++;
                    }
                }

                double q = sum / n;
                Assert.True(Math.Abs(q - mean) <= 3 * se, $"{sides[side].Name}'s goal difference: quick-sim {q:F2} against the full simulation's {mean:F2} ± {se:F2}");
            }
        }
    }
}
