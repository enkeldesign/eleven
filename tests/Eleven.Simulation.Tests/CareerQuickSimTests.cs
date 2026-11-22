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
    }
}
