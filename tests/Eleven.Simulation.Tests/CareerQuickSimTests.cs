using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #163: the quick-sim plays the world's clubs and cup ties. A side's tier lies anywhere
    /// between the CPU tiers, exactly today's at a whole tier; a club's side is read from its XI;
    /// and a knockout's quick-sim result always has a winner, by extra time and then a shootout,
    /// drawn from new counters so that no league result moves.
    /// </summary>
    public class CareerQuickSimTests
    {
        private static readonly World World = WorldGenerator.Generate(0x157);

        private static QuickSide Reference(double tier) => new QuickSide(tier, PlayerCapabilities.Reference);

        [Fact]
        public void A_tier_between_two_mixes_their_ratings_and_a_whole_tier_is_its_own()
        {
            QuickSimModel m = QuickSimModel.Reference;
            double[] whole = Enumerable.Range(0, QuickSimModel.Tiers).Select(t => m.TierRating(t)).ToArray();
            Assert.Equal(new[] { 0, 0.230584, 0.243057, 0.706305 }, whole);
            Assert.Equal((whole[1] + whole[2]) / 2, m.TierRating(1.5), 12);
            Assert.Equal(whole[2] + 0.25 * (whole[3] - whole[2]), m.TierRating(2.25), 12);
            Assert.Equal(whole[0], m.TierRating(-1));
            Assert.Equal(whole[3], m.TierRating(7));
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(5.75, 1)]
        [InlineData(10.5, 2)]
        [InlineData(20, 3)]
        [InlineData(3.375, 0.5)]
        [InlineData(15.25, 2.5)]
        public void A_cpu_rating_sits_on_the_tiers_own_points(double rating, double tier)
        {
            Assert.Equal(tier, CareerQuickSim.TierPosition(rating), 12);
        }

        [Fact]
        public void A_clubs_side_is_its_xis_mean_cpu_rating_and_its_outfielders_mean_capabilities()
        {
            Club top = World.Clubs.Where(c => c.Division == 1).OrderByDescending(c => c.Reputation).First();
            Club bottom = World.Clubs.Where(c => c.Division == 3).OrderBy(c => c.Reputation).First();
            SquadSelection xi = CareerSquads.Selection(World, top.Id, 10);
            List<Player> outfield = xi.Starters.Take(10).ToList();
            double cpu = outfield.Average(p => RatingMap.Cpu.Average(e => e.Rating(p)));

            QuickSide side = CareerQuickSim.Side(World, top.Id, 10);
            Assert.Equal(CareerQuickSim.TierPosition(cpu), side.Tier, 12);
            Assert.Equal(outfield.Average(p => RatingMap.Capabilities(p).MaxSpeed), side.Capabilities.MaxSpeed, 12);
            Assert.Equal(outfield.Average(p => RatingMap.Capabilities(p).KickError), side.Capabilities.KickError, 12);
            Assert.True(QuickSim.Rating(side, QuickSimModel.Reference) > QuickSim.Rating(CareerQuickSim.Side(World, bottom.Id, 10), QuickSimModel.Reference),
                "the top division's best club rates above the bottom division's weakest");
        }

        private static readonly SimConfig League = SpikeScenario.Config().With("halfLength", 180);
        private static readonly SimConfig Knockout = League.With("knockout", 1);

        [Fact]
        public void A_league_quick_sim_can_end_level_and_a_knockout_one_never_does()
        {
            int level = 0, extra = 0, shootouts = 0;
            for (ulong seed = 0; seed < 400; seed++)
            {
                if (QuickSim.Play(League, Reference(2), Reference(2), seed).Winner == 0) level++;
                MatchResult k = QuickSim.Play(Knockout, Reference(2), Reference(2), seed);
                Assert.NotEqual(0, k.Winner);
                extra += k.ExtraTime;
                if (k.HomeShootoutKicks + k.AwayShootoutKicks > 0) shootouts++;
            }

            Assert.True(level > 40, $"{level} of 400 league matches level");
            Assert.Equal(level, extra);
            Assert.True(shootouts > 0 && shootouts < extra, $"{shootouts} shootouts after {extra} extra times");
        }

        [Fact]
        public void A_knockouts_extra_time_and_shootout_are_as_the_full_simulation_records_them()
        {
            double half = Knockout.HalfLength, extraHalf = half * Knockout.ExtraTimeShare;
            int each = (int)Knockout.ShootoutKicks;
            int checkedExtra = 0;
            for (ulong seed = 0; seed < 400; seed++)
            {
                MatchResult k = QuickSim.Play(Knockout, Reference(1), Reference(2), seed);
                int normalHome = k.Goals.Count(g => g.Half <= 2 && g.Team == k.HomeTeam), normalAway = k.Goals.Count(g => g.Half <= 2 && g.Team == k.AwayTeam);
                Assert.Equal(k.HomeGoals, k.Goals.Count(g => g.Team == k.HomeTeam));
                Assert.Equal(k.AwayGoals, k.Goals.Count(g => g.Team == k.AwayTeam));
                if (normalHome != normalAway)
                {
                    Assert.Equal(0, k.ExtraTime);
                    Assert.Equal(2 * half, k.PlayedSeconds, 9);
                    Assert.DoesNotContain(k.Goals, g => g.Half > 2);
                    continue;
                }

                checkedExtra++;
                Assert.Equal(1, k.ExtraTime);
                Assert.Equal(2 * half + 2 * extraHalf, k.PlayedSeconds, 9);
                Assert.All(k.Goals.Where(g => g.Half > 2), g => Assert.InRange(g.ClockSeconds, 0, extraHalf));
                bool shootout = k.HomeShootoutKicks + k.AwayShootoutKicks > 0;
                Assert.Equal(k.HomeGoals == k.AwayGoals, shootout);
                if (!shootout) continue;

                // Taken in turn, never more goals than kicks, and over only once it is decided.
                Assert.InRange(k.HomeShootoutKicks - k.AwayShootoutKicks, 0, 1);
                Assert.InRange(k.HomeShootoutGoals, 0, k.HomeShootoutKicks);
                Assert.InRange(k.AwayShootoutGoals, 0, k.AwayShootoutKicks);
                Assert.NotEqual(k.HomeShootoutGoals, k.AwayShootoutGoals);
                if (k.HomeShootoutKicks > each || k.AwayShootoutKicks > each) Assert.Equal(k.HomeShootoutKicks, k.AwayShootoutKicks);
                else if (k.HomeShootoutKicks == each && k.AwayShootoutKicks == each) { }
                else Assert.True(k.HomeShootoutGoals > k.AwayShootoutGoals + (each - k.AwayShootoutKicks) || k.AwayShootoutGoals > k.HomeShootoutGoals + (each - k.HomeShootoutKicks),
                    $"seed {seed}: over at {k.HomeShootoutGoals}/{k.HomeShootoutKicks} against {k.AwayShootoutGoals}/{k.AwayShootoutKicks}, not yet decided");
            }

            Assert.True(checkedExtra > 30, $"{checkedExtra} extra times");
        }

        [Fact]
        public void A_knockout_plays_the_same_ninety_minutes_as_a_league_match_from_the_seed()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                MatchResult l = QuickSim.Play(League, Reference(1), Reference(3), seed), k = QuickSim.Play(Knockout, Reference(1), Reference(3), seed);
                Assert.Equal(l.Goals.Select(g => (g.Team, g.Half, g.ClockSeconds)), k.Goals.Where(g => g.Half <= 2).Select(g => (g.Team, g.Half, g.ClockSeconds)));
                Assert.Equal((l.HomeFouls, l.AwayFouls, l.HomeCautions, l.AwayCautions), (k.HomeFouls, k.AwayFouls, k.HomeCautions, k.AwayCautions));
                Assert.Equal(k.ToText(), QuickSim.Play(Knockout, Reference(1), Reference(3), seed).ToText());
            }
        }

        [Fact]
        public void Shootout_kicks_go_in_at_the_models_conversion()
        {
            int kicks = 0, goals = 0;
            for (ulong seed = 0; seed < 3000; seed++)
            {
                MatchResult k = QuickSim.Play(Knockout, Reference(2), Reference(2), seed);
                kicks += k.HomeShootoutKicks + k.AwayShootoutKicks;
                goals += k.HomeShootoutGoals + k.AwayShootoutGoals;
            }

            Assert.True(kicks > 500, $"{kicks} kicks");
            double p = QuickSimModel.Reference.ShootoutConversion, rate = (double)goals / kicks;
            // A shootout stops once decided, which leans its last kicks a little; 4 standard errors.
            Assert.InRange(rate, p - 4 * Math.Sqrt(p * (1 - p) / kicks), p + 4 * Math.Sqrt(p * (1 - p) / kicks));
        }

        // ---- Eleven a side (#163): the calibration sample and its fit ----

        [Fact]
        public void The_samples_fixtures_spread_over_every_club_at_home_and_away()
        {
            World w = CareerQuickSimCalibration.World();
            List<(int Home, int Away, ulong Seed)> f = CareerQuickSimCalibration.Fixtures(w);
            Assert.Equal(CareerQuickSimCalibration.Matches, f.Count);
            Assert.Equal(f.Count, f.Select(x => (x.Home, x.Away)).Distinct().Count());
            Assert.All(f, x => Assert.NotEqual(x.Home, x.Away));
            Assert.Equal(Enumerable.Range(0, f.Count).Select(i => CareerQuickSimCalibration.FirstSeed + (ulong)i), f.Select(x => x.Seed));
            foreach (Club c in w.Clubs)
            {
                Assert.InRange(f.Count(x => x.Home == c.Id), 10, 20);
                Assert.InRange(f.Count(x => x.Away == c.Id), 10, 20);
            }

            // Every gap between divisions, both ways round.
            int Gap((int Home, int Away, ulong Seed) x) => w.Clubs.Single(c => c.Id == x.Away).Division - w.Clubs.Single(c => c.Id == x.Home).Division;
            foreach (int gap in new[] { -2, -1, 0, 1, 2 }) Assert.True(f.Count(x => Gap(x) == gap) >= 30, $"gap {gap}");
        }

        [Fact]
        public void A_sample_round_trips_through_its_csv()
        {
            var sample = new List<CareerCalibrationMatch>
            {
                new CareerCalibrationMatch { Index = 0, Home = 3, Away = 17, Seed = 0x163000, HomeGoals = 2, AwayGoals = 1, HomeFouls = 3, AwayFouls = 4, HomeCautions = 1, AwayCautions = 0, HomeSendingsOff = 0, AwaySendingsOff = 1, ResultHash = 0xfeedfacecafebeefUL },
                new CareerCalibrationMatch { Index = 1, Home = 17, Away = 3, Seed = 0x163001, ResultHash = 1 },
            };
            var text = new System.IO.StringWriter();
            CareerQuickSimCalibration.Write(text, sample);
            string[] lines = text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            List<CareerCalibrationMatch> back = CareerQuickSimCalibration.Read(lines);
            Assert.Equal(sample.Count, back.Count);
            for (int i = 0; i < sample.Count; i++)
            {
                Assert.Equal((sample[i].Index, sample[i].Home, sample[i].Away, sample[i].Seed, sample[i].HomeGoals, sample[i].AwayGoals, sample[i].ResultHash),
                    (back[i].Index, back[i].Home, back[i].Away, back[i].Seed, back[i].HomeGoals, back[i].AwayGoals, back[i].ResultHash));
                Assert.Equal((sample[i].HomeFouls, sample[i].AwayFouls, sample[i].HomeCautions, sample[i].AwayCautions, sample[i].HomeSendingsOff, sample[i].AwaySendingsOff),
                    (back[i].HomeFouls, back[i].AwayFouls, back[i].HomeCautions, back[i].AwayCautions, back[i].HomeSendingsOff, back[i].AwaySendingsOff));
            }
        }

        [Fact]
        public void The_fit_recovers_a_model_it_draws_from()
        {
            // A sample drawn from a known eleven-a-side model, the reference's ratings scaled by 0.6,
            // at the sample's fixtures, four times over: the fit finds it within its errors.
            World w = CareerQuickSimCalibration.World();
            QuickSimModel r = QuickSimModel.Reference;
            const double scale = 0.6;
            var truth = new QuickSimModel(r.BaseRate - 0.7, 0.05, Enumerable.Range(0, QuickSimModel.Tiers).Select(t => scale * r.TierRating(t)).ToArray(), scale * r.CapabilityWeight,
                r.Rho, 2.5, 0.6, 0.05);
            SimConfig c = CareerQuickSimCalibration.Config();
            var sample = new List<CareerCalibrationMatch>();
            List<(int Home, int Away, ulong Seed)> fixtures = CareerQuickSimCalibration.Fixtures(w);
            Assert.NotEmpty(fixtures);
            for (int round = 0; round < 4; round++)
            {
                foreach ((int home, int away, ulong seed) in fixtures)
                {
                    MatchResult m = QuickSim.Play(c, CareerQuickSim.Side(w, home, 10), CareerQuickSim.Side(w, away, 10), seed * 4 + (ulong)round, model: truth);
                    sample.Add(new CareerCalibrationMatch { Home = home, Away = away, Seed = seed, HomeGoals = m.HomeGoals, AwayGoals = m.AwayGoals, HomeFouls = m.HomeFouls, AwayFouls = m.AwayFouls, HomeCautions = m.HomeCautions, AwayCautions = m.AwayCautions, HomeSendingsOff = m.HomeSendingsOff, AwaySendingsOff = m.AwaySendingsOff });
                }
            }

            CareerFitResult fit = CareerQuickSimCalibration.Fit(w, sample);
            Assert.InRange(fit.Model.BaseRate, truth.BaseRate - 3 * fit.BaseRateError, truth.BaseRate + 3 * fit.BaseRateError);
            Assert.InRange(fit.Model.Home, truth.Home - 3 * fit.HomeError, truth.Home + 3 * fit.HomeError);
            Assert.InRange(fit.Scale, scale - 3 * fit.ScaleError, scale + 3 * fit.ScaleError);
            Assert.True(fit.BaseRateError > 0 && fit.ScaleError > 0 && fit.ScaleError < 0.2, $"errors: mu {fit.BaseRateError}, scale {fit.ScaleError}");
            Assert.Equal(fit.Scale * r.TierRating(3), fit.Model.TierRating(3), 9);
            Assert.Equal(fit.Scale * r.CapabilityWeight, fit.Model.CapabilityWeight, 9);
            Assert.InRange(fit.Model.FoulsPerSide, 2.3, 2.7);
        }

        private static string CareerReference => System.IO.Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "quicksim-career-reference.csv");

        [Fact]
        public void The_eleven_a_side_model_is_the_fit_of_its_reference_sample()
        {
            Assert.True(System.IO.File.Exists(CareerReference), "no eleven-a-side reference sample");
            List<CareerCalibrationMatch> sample = CareerQuickSimCalibration.Read(System.IO.File.ReadAllLines(CareerReference));
            Assert.Equal(CareerQuickSimCalibration.Matches, sample.Count);
            QuickSimModel fit = CareerQuickSimCalibration.Fit(CareerQuickSimCalibration.World(), sample).Model, e = QuickSimModel.ElevenASide;
            Assert.Equal(fit.BaseRate, e.BaseRate, 6);
            Assert.Equal(fit.Home, e.Home, 6);
            for (int t = 0; t < QuickSimModel.Tiers; t++) Assert.Equal(fit.TierRating(t), e.TierRating(t), 6);
            Assert.Equal(fit.CapabilityWeight, e.CapabilityWeight, 6);
            Assert.Equal(fit.Rho, e.Rho, 6);
            Assert.Equal(fit.FoulsPerSide, e.FoulsPerSide, 6);
            Assert.Equal(fit.CautionsPerSide, e.CautionsPerSide, 6);
            Assert.Equal(fit.SendingsOffPerSide, e.SendingsOffPerSide, 6);
            Assert.Same(e, QuickSimModel.For(10));
            Assert.Same(QuickSimModel.Reference, QuickSimModel.For(5));
        }

        [Fact]
        public void The_eleven_a_side_model_scores_as_its_sample_does()
        {
            Assert.True(System.IO.File.Exists(CareerReference), "no eleven-a-side reference sample");
            List<CareerCalibrationMatch> sample = CareerQuickSimCalibration.Read(System.IO.File.ReadAllLines(CareerReference));
            World w = CareerQuickSimCalibration.World();
            SimConfig c = CareerQuickSimCalibration.Config();
            var sides = new Dictionary<int, QuickSide>();
            QuickSide Side(int club) => sides.TryGetValue(club, out QuickSide s) ? s : sides[club] = CareerQuickSim.Side(w, club, 10);
            double qh = 0, qa = 0;
            int[] full = new int[3], quick = new int[3];
            const int Draws = 20;
            foreach (CareerCalibrationMatch m in sample)
            {
                full[Math.Sign(m.HomeGoals - m.AwayGoals) + 1]++;
                for (int d = 0; d < Draws; d++)
                {
                    MatchResult q = QuickSim.Play(c, Side(m.Home), Side(m.Away), m.Seed * 1000 + (ulong)d, model: QuickSimModel.ElevenASide);
                    qh += q.HomeGoals;
                    qa += q.AwayGoals;
                    quick[Math.Sign(q.HomeGoals - q.AwayGoals) + 1]++;
                }
            }

            int n = sample.Count;
            double MeanError(Func<CareerCalibrationMatch, double> f)
            {
                double mean = sample.Average(f);
                return Math.Sqrt(sample.Sum(x => (f(x) - mean) * (f(x) - mean)) / (n - 1) / n);
            }

            double fh = sample.Average(m => (double)m.HomeGoals), fa = sample.Average(m => (double)m.AwayGoals);
            Assert.InRange(qh / (n * Draws), fh - 2 * MeanError(m => m.HomeGoals), fh + 2 * MeanError(m => m.HomeGoals));
            Assert.InRange(qa / (n * Draws), fa - 2 * MeanError(m => m.AwayGoals), fa + 2 * MeanError(m => m.AwayGoals));

            // Win, draw and loss: a two-sample χ² at 1 % (9.21, 2 df).
            double chi = 0, nq = n * Draws;
            for (int k = 0; k < 3; k++)
            {
                double pooled = (full[k] + quick[k]) / (n + nq);
                chi += Math.Pow(full[k] - n * pooled, 2) / (n * pooled) + Math.Pow(quick[k] - nq * pooled, 2) / (nq * pooled);
            }

            Assert.True(chi < 9.21, $"win/draw/loss χ² {chi:F2}: full {string.Join("/", full)}, quick {string.Join("/", quick)}");
        }
    }
}
