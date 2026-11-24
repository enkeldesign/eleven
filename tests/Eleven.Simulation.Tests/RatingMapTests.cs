using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #159: a footballer's ratings drive the simulation. Every mapped value meets the reference
    /// at the middle rating, moves one way from 1 to 20 and stays within its tunable's range; the
    /// world's footballers carry their own values into a match; and a top-division club beats a
    /// bottom-division one more often than not.
    /// </summary>
    public class RatingMapTests
    {
        private static double Reference(string group, string target) => group switch
        {
            "outfield" => PlayerCapabilities.Reference.Get(target),
            "keeping" => target switch
            {
                "reactionTime" => KeeperCapabilities.Reference.ReactionTime,
                "diveSpeed" => KeeperCapabilities.Reference.DiveSpeed,
                "reach" => KeeperCapabilities.Reference.Reach,
                _ => KeeperCapabilities.Reference.Handling,
            },
            _ => target switch
            {
                "decisionInterval" => CpuSkill.Pro.DecisionInterval,
                "reactionDelay" => CpuSkill.Pro.ReactionDelay,
                "perceptionError" => CpuSkill.Pro.PerceptionError,
                _ => CpuSkill.Pro.VisionRange,
            },
        };

        private static TunableParameter Tunable(string group, string target) =>
            (group == "outfield" ? PlayerCapabilities.Parameters : group == "keeping" ? KeeperCapabilities.Parameters : CpuSkill.Parameters).Single(p => p.Name == target);

        public static IEnumerable<object[]> Entries() =>
            RatingMap.Outfield.Select(e => new object[] { "outfield", e.Target })
                .Concat(RatingMap.Keeping.Select(e => new object[] { "keeping", e.Target }))
                .Concat(RatingMap.Cpu.Select(e => new object[] { "cpu", e.Target }));

        private static RatingEntry Entry(string group, string target) =>
            (group == "outfield" ? RatingMap.Outfield : group == "keeping" ? RatingMap.Keeping : RatingMap.Cpu).Single(e => e.Target == target);

        [Theory]
        [MemberData(nameof(Entries))]
        public void Each_value_meets_the_reference_at_the_middle_moves_one_way_and_stays_in_range(string group, string target)
        {
            RatingEntry e = Entry(group, target);
            TunableParameter p = Tunable(group, target);
            Assert.Equal(Reference(group, target), e.At(RatingMap.ReferenceRating), 12);
            Assert.NotEqual(e.At(1), e.At(20));
            int direction = Math.Sign(e.At(20) - e.At(1));
            double previous = e.At(1);
            for (double r = 1.25; r <= 20; r += 0.25)
            {
                double v = e.At(r);
                Assert.True(Math.Sign(v - previous) * direction >= 0, $"{target} turns back at {r}: {previous} then {v}");
                Assert.InRange(v, p.Min, p.Max);
                previous = v;
            }
        }

        private static Player Rated(int value)
        {
            var p = new Player { Id = 1 };
            for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = value;
            p.Positions.Add(Position.CentralMidfield);
            return p;
        }

        [Fact]
        public void A_footballers_values_are_his_ratings_mapped()
        {
            foreach (int rating in new[] { 1, 7, 14, 20 })
            {
                Player p = Rated(rating);
                PlayerCapabilities c = RatingMap.Capabilities(p);
                foreach (RatingEntry e in RatingMap.Outfield) Assert.Equal(e.Value(p), c.Get(e.Target), 12);

                KeeperCapabilities k = RatingMap.Keeper(p);
                Assert.Equal(RatingMap.Keeping.Single(e => e.Target == "reactionTime").Value(p), k.ReactionTime, 12);
                Assert.Equal(RatingMap.Keeping.Single(e => e.Target == "handling").Value(p), k.Handling, 12);

                CpuSkill s = RatingMap.Skill(p);
                Assert.Equal(RatingMap.Cpu.Single(e => e.Target == "decisionInterval").Value(p), s.DecisionInterval, 12);
                Assert.Equal(RatingMap.Cpu.Single(e => e.Target == "visionRange").Value(p), s.VisionRange, 12);
            }
        }

        private static readonly World World = WorldGenerator.Generate(0x157);

        [Fact]
        public void A_career_match_gives_each_footballer_keeper_and_cpu_footballer_his_own_values()
        {
            SimConfig c = SpikeScenario.Config();
            Club home = World.Clubs.First(x => x.Division == 1), away = World.Clubs.First(x => x.Division == 3);
            SimState s = CareerSquads.Match(c, World, home.Id, away.Id, 10, 1);
            var picked = CareerSquads.Selection(World, home.Id, 10);
            Assert.Equal(11, picked.Starters.Count);
            Assert.Equal(Position.Goalkeeper, picked.Starters[picked.Starters.Count - 1].Positions[0]);

            for (int i = 0; i < 10; i++)
            {
                PlayerState ps = s.Players.Single(x => x.Id == Squads.HomeOutfielder(i));
                Assert.Equal(RatingMap.Capabilities(picked.Starters[i]).MaxSpeed, ps.Capabilities.MaxSpeed, 12);
                Assert.Equal(RatingMap.Capabilities(picked.Starters[i]).KickError, ps.Capabilities.KickError, 12);
            }

            KeeperState keeper = s.Keepers.Single(k => k.PlayerId == Squads.HomeKeeper(10));
            Assert.Equal(RatingMap.Keeper(picked.Starters[10]).ReactionTime, keeper.Capabilities.ReactionTime, 12);
            Assert.Contains(s.Cpus, x => x.PlayerId == Squads.HomeOutfielder(3));
            Assert.Equal(RatingMap.Skill(picked.Starters[3]).DecisionInterval, s.SkillOf(Squads.HomeOutfielder(3)).DecisionInterval, 12);

            // Not one value for the whole side.
            Assert.True(s.Players.Where(x => x.TeamId == Squads.HomeTeam).Select(x => x.Capabilities.MaxSpeed).Distinct().Count() > 3);
            Assert.NotEqual(
                s.Players.Where(x => x.TeamId == Squads.HomeTeam).Average(x => x.Capabilities.Tackling),
                s.Players.Where(x => x.TeamId == Squads.AwayTeam).Average(x => x.Capabilities.Tackling));
        }

        [Fact]
        public void A_top_division_club_beats_a_bottom_division_club_more_often_than_not()
        {
            // CPU against CPU at five a side, each pairing at home and away, over fixed seeds.
            SimConfig c = SpikeScenario.Config().With("halfLength", 90);
            var top = World.Clubs.Where(x => x.Division == 1).Take(4).ToList();
            var bottom = World.Clubs.Where(x => x.Division == 3).Take(4).ToList();
            int topWins = 0, bottomWins = 0, topGoals = 0, bottomGoals = 0;
            for (int i = 0; i < 4; i++)
            {
                foreach (bool topHome in new[] { true, false })
                {
                    Club h = topHome ? top[i] : bottom[i], a = topHome ? bottom[i] : top[i];
                    MatchResult r = CareerSquads.Play(c, World, h.Id, a.Id, 5, 0x159000UL + (ulong)(i * 2 + (topHome ? 0 : 1)));
                    int topScore = topHome ? r.HomeGoals : r.AwayGoals, bottomScore = topHome ? r.AwayGoals : r.HomeGoals;
                    topGoals += topScore;
                    bottomGoals += bottomScore;
                    if (topScore > bottomScore) topWins++;
                    else if (bottomScore > topScore) bottomWins++;
                }
            }

            Assert.True(topWins > bottomWins && topGoals > bottomGoals, $"top division {topWins} wins, {topGoals} goals; bottom {bottomWins} wins, {bottomGoals} goals");
        }
    }
}
