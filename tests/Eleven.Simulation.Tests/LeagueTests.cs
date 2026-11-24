using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>M3's gate (#103): the 5v5 league's sides, matches and table.</summary>
    public class LeagueTests
    {
        private static readonly SimConfig Short = SpikeScenario.Config().With("halfLength", 60);

        [Fact]
        public void The_league_has_six_sides_across_the_four_tiers_and_plays_each_pairing_home_and_away()
        {
            IReadOnlyList<LeagueSide> sides = League.Sides();
            Assert.Equal(6, sides.Count);
            Assert.Equal(new[] { "apprentice", "club", "pro", "elite" }, sides.Select(s => s.Tier).Distinct());
            Assert.Equal(6, sides.Select(s => s.Name).Distinct().Count());
        }

        [Fact]
        public void A_sides_capabilities_and_tier_reach_all_its_outfielders()
        {
            LeagueSide strong = League.Sides().First(s => s.Name == "Stonegate");
            SimState s = Squads.Match(Short, 1, League.PerSide, bothCpu: true, awayCpu: strong.Skill, awaySide: strong.Capabilities);
            int[] away = Enumerable.Range(0, League.PerSide).Select(i => Squads.AwayOutfielder(League.PerSide, i)).ToArray();
            Assert.All(s.Players.Where(p => away.Contains(p.Id)), p => Assert.Equal(strong.Capabilities.Strength, p.Capabilities.Strength));
            Assert.All(s.Cpus.Where(c => away.Contains(c.PlayerId)), c => Assert.Equal(strong.Skill.DecisionInterval, s.SkillOf(c.PlayerId).DecisionInterval));
            Assert.All(s.Players.Where(p => !away.Contains(p.Id) && p.TeamId == Squads.AwayTeam), p => Assert.Equal(PlayerCapabilities.Reference.Strength, p.Capabilities.Strength));
        }

        [Fact]
        public void A_league_match_finishes_unstuck_and_counts_what_the_referee_gave()
        {
            IReadOnlyList<LeagueSide> sides = League.Sides();
            LeagueMatch m = League.Play(Short, sides, 5, 0, 0x103000005UL);
            Assert.True(m.Finished);
            Assert.False(m.Stuck, $"still {m.LongestStillSeconds} s, untouched {m.LongestUntouchedSeconds} s");
            Assert.True(m.ThrowIns + m.Corners + m.GoalKicks > 0, "no restart in a whole match");
            Assert.Equal(m.FinalHash, League.Play(Short, sides, 5, 0, 0x103000005UL).FinalHash);
        }

        [Fact]
        public void The_table_gives_three_points_a_win_and_one_a_draw()
        {
            IReadOnlyList<LeagueSide> sides = League.Sides().Take(3).ToList();
            var matches = new List<LeagueMatch>
            {
                new LeagueMatch { Home = 0, Away = 1, Finished = true, HomeGoals = 2, AwayGoals = 1 },
                new LeagueMatch { Home = 1, Away = 2, Finished = true, HomeGoals = 0, AwayGoals = 0 },
                new LeagueMatch { Home = 2, Away = 0, Finished = false, HomeGoals = 5, AwayGoals = 0 },
            };
            List<LeagueRow> table = League.Table(sides, matches);
            Assert.Equal(new[] { "Harbour", "Stonegate", "Millbrook" }, table.Select(r => r.Side.Name));
            Assert.Equal(new[] { 3, 1, 1 }, table.Select(r => r.Points));
            Assert.Equal(new[] { 1, 1, 2 }, table.Select(r => r.Played));
        }
    }
}
