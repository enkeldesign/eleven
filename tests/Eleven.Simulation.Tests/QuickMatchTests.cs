using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #160: Home and Quick Match. The plain address opens on Home and every other address as
    /// before; the picker lists the world's clubs by division; a quick match puts the human on his
    /// club's kick-off taker with every footballer on both sides on his own values, both sides
    /// with their stored plans; and the same choices and seed give the same match.
    /// </summary>
    public class QuickMatchTests
    {
        [Theory]
        [InlineData("https://enkeldesign.github.io/eleven/", true)]
        [InlineData("https://enkeldesign.github.io/eleven/index.html", true)]
        [InlineData("https://enkeldesign.github.io/eleven/pr/160/", true)]
        [InlineData("https://enkeldesign.github.io/eleven/?", true)]
        [InlineData("https://enkeldesign.github.io/eleven/#top", true)]
        [InlineData("https://enkeldesign.github.io/eleven/?home=1", true)]
        [InlineData("http://127.0.0.1:8080/index.html?trace=1&home=1", true)]
        [InlineData("https://enkeldesign.github.io/eleven/?home=1#top", true)]
        [InlineData("https://enkeldesign.github.io/eleven/?trace=1", false)]
        [InlineData("https://enkeldesign.github.io/eleven/?mode=match11", false)]
        [InlineData("https://enkeldesign.github.io/eleven/?home=0", false)]
        [InlineData("https://enkeldesign.github.io/eleven/?sheet=1", false)]
        [InlineData("https://enkeldesign.github.io/eleven/?xhome=1", false)]
        [InlineData("https://enkeldesign.github.io/eleven/?home=10", false)]
        [InlineData("", true)]
        public void The_plain_address_and_home_1_open_on_Home_and_every_other_address_as_before(string url, bool home)
        {
            Assert.Equal(home, QuickMatch.ShowsHome(url));
        }

        [Fact]
        public void The_picker_lists_every_club_by_division_then_reputation()
        {
            World w = QuickMatch.World;
            List<Club> clubs = QuickMatch.Clubs(w);
            Assert.Equal(w.Clubs.Count, clubs.Count);
            Assert.Equal(w.Clubs.Select(c => c.Id).OrderBy(i => i), clubs.Select(c => c.Id).OrderBy(i => i));
            for (int i = 1; i < clubs.Count; i++)
            {
                Club a = clubs[i - 1], b = clubs[i];
                bool ordered = a.Division < b.Division
                    || (a.Division == b.Division && (a.Reputation > b.Reputation || (a.Reputation == b.Reputation && a.Id < b.Id)));
                Assert.True(ordered, $"{a.Name} (division {a.Division}, reputation {a.Reputation}) before {b.Name} (division {b.Division}, reputation {b.Reputation})");
            }
        }

        [Fact]
        public void The_quick_match_world_is_the_same_world_on_every_visit()
        {
            Assert.Equal(WorldCodec.Encode(WorldGenerator.Generate(QuickMatch.WorldSeed)), WorldCodec.Encode(QuickMatch.World));
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        public void A_quick_match_puts_the_human_on_his_club_with_every_footballer_on_his_own_values(int perSide)
        {
            World w = QuickMatch.World;
            List<Club> clubs = QuickMatch.Clubs(w);
            Assert.NotEmpty(clubs);
            Club home = clubs[0], away = clubs[clubs.Count - 1];
            SimConfig c = SpikeScenario.Config();
            SimState s = QuickMatch.Match(c, w, home.Id, away.Id, perSide, 0x160);

            // The human: his club's kick-off taker, and no one else.
            PlayerState[] his = s.Players.Where(p => p.OwnerControllerId == OneVOne.HomeController).ToArray();
            Assert.Single(his);
            Assert.Equal(Squads.HomeTeam, his[0].TeamId);
            Assert.Equal(Squads.HomeOutfielder(0), his[0].Id);

            // Every footballer on his own values: the human's, his team-mates', the keepers' and the CPU's.
            SquadSelection mine = CareerSquads.Selection(w, home.Id, perSide), theirs = CareerSquads.Selection(w, away.Id, perSide);
            Assert.Equal(RatingMap.Capabilities(mine.Starters[0]).MaxSpeed, his[0].Capabilities.MaxSpeed, 12);
            Assert.Equal(RatingMap.Capabilities(mine.Starters[0]).KickError, his[0].Capabilities.KickError, 12);
            PlayerState mate = s.Players.Single(p => p.Id == Squads.HomeOutfielder(perSide - 1));
            Assert.Equal(RatingMap.Capabilities(mine.Starters[perSide - 1]).BallControl, mate.Capabilities.BallControl, 12);
            // The human's team-mates play on their XI's mean CPU skill until a footballer's own follows
            // him through a switch (#166, Codex on #165); the opponent's footballers on their own.
            CpuState mateCpu = s.Cpus.Single(x => x.PlayerId == Squads.HomeOutfielder(perSide - 1));
            Assert.Equal(mine.Starters.Take(perSide).Average(p => RatingMap.Skill(p).DecisionInterval), mateCpu.Skill.DecisionInterval, 12);
            CpuState theirCpu = s.Cpus.Single(x => x.PlayerId == Squads.AwayOutfielder(perSide, 2));
            Assert.Equal(RatingMap.Skill(theirs.Starters[2]).DecisionInterval, theirCpu.Skill.DecisionInterval, 12);
            PlayerState opponent = s.Players.Single(p => p.Id == Squads.AwayOutfielder(perSide, 1));
            Assert.Equal(RatingMap.Capabilities(theirs.Starters[1]).Tackling, opponent.Capabilities.Tackling, 12);
            KeeperState keeper = s.Keepers.Single(k => k.PlayerId == Squads.AwayKeeper(perSide));
            Assert.Equal(RatingMap.Keeper(theirs.Starters[perSide]).ReactionTime, keeper.Capabilities.ReactionTime, 12);
            Assert.True(s.Players.Where(p => p.TeamId == Squads.HomeTeam).Select(p => p.Capabilities.MaxSpeed).Distinct().Count() > 2);

            // Both sides keep the three stored plans (#126), for the dashboard.
            Assert.Equal(2 * TeamTactics.PerSide, s.Tactics.Length);
        }

        [Fact]
        public void The_same_clubs_size_and_seed_give_the_same_match_and_other_clubs_another()
        {
            World w = QuickMatch.World;
            List<Club> clubs = QuickMatch.Clubs(w);
            Assert.True(clubs.Count > 3);
            SimConfig c = SpikeScenario.Config().With("halfLength", 60);
            ulong Hash(int home, int away, ulong seed)
            {
                var sim = new Simulator(c, QuickMatch.Match(c, w, clubs[home].Id, clubs[away].Id, 5, seed));
                var commands = new List<Command>();
                var events = new List<SimEvent>();
                for (int t = 0; t < 600; t++)
                {
                    commands.Clear();
                    KeeperControllers.Append(sim, commands);
                    CpuControllers.Append(sim, commands);
                    RestartControllers.Append(sim, commands);
                    events.Clear();
                    sim.Step(commands, events);
                }

                return sim.StateHash();
            }

            Assert.Equal(Hash(0, 20, 7), Hash(0, 20, 7));
            Assert.NotEqual(Hash(0, 20, 7), Hash(1, 20, 7));
        }

        [Fact]
        public void The_score_line_names_the_clubs_by_their_short_names()
        {
            List<Club> clubs = QuickMatch.Clubs(QuickMatch.World);
            Assert.True(clubs.Count > 1);
            Assert.Equal($"{clubs[0].ShortName} 2 – 1 {clubs[1].ShortName}", QuickMatch.Score(clubs[0], 2, 1, clubs[1]));
        }

        [Fact]
        public void Every_size_and_half_offered_is_one_the_match_takes()
        {
            Assert.Equal(QuickMatch.Sizes.Length, QuickMatch.SizeNames.Length);
            Assert.Equal(QuickMatch.Halves.Length, QuickMatch.HalfNames.Length);
            TunableParameter half = SimConfig.Tunables.Single(p => p.Name == "halfLength");
            Assert.All(QuickMatch.Halves, h => Assert.InRange(h, half.Min, half.Max));
            Assert.All(QuickMatch.Sizes, n => Assert.Contains(n, new[] { 1, 2, 5, 10 }));
        }

        [Fact]
        public void A_switch_never_hands_one_footballers_cpu_skill_to_another()
        {
            World w = QuickMatch.World;
            List<Club> clubs = QuickMatch.Clubs(w);
            Assert.NotEmpty(clubs);
            SimConfig c = SpikeScenario.Config();
            var sim = new Simulator(c, QuickMatch.Match(c, w, clubs[0].Id, clubs[5].Id, 5, 0x166));
            SquadSelection mine = CareerSquads.Selection(w, clubs[0].Id, 5);
            double mean = mine.Starters.Take(5).Average(p => RatingMap.Skill(p).DecisionInterval);
            Dictionary<int, double> away = sim.PeekState().Cpus.Where(x => sim.PeekState().Players.Single(p => p.Id == x.PlayerId).TeamId == Squads.AwayTeam)
                .ToDictionary(x => x.PlayerId, x => x.Skill.DecisionInterval);
            Assert.True(away.Values.Distinct().Count() > 1, "the opponent plays on its footballers' own skills");

            // Switch to each team-mate in turn, and back: every CPU footballer of the human's side
            // keeps the side's skill, the bench too, and the opponent's are untouched.
            var events = new List<SimEvent>();
            int switches = 0;
            foreach (int mate in Enumerable.Range(1, 4).Select(Squads.HomeOutfielder).Concat(new[] { Squads.HomeOutfielder(0) }))
            {
                events.Clear();
                sim.Step(new[] { new Command(OneVOne.HomeController, sim.Tick, Vec2.Zero, switchTo: mate) }, events);
                switches += events.Count(e => e.Kind == SimEventKind.Switched);
                SimState s = sim.PeekState();
                foreach (CpuState cpu in s.Cpus)
                {
                    if (s.Players.Single(p => p.Id == cpu.PlayerId).TeamId == Squads.HomeTeam) Assert.Equal(mean, cpu.Skill.DecisionInterval, 12);
                    else Assert.Equal(away[cpu.PlayerId], cpu.Skill.DecisionInterval, 12);
                }

                Assert.All(s.Bench.Where(b => b.TeamId == Squads.HomeTeam), b => Assert.Equal(mean, b.Skill.DecisionInterval, 12));
            }

            Assert.True(switches >= 4, $"{switches} switches");
        }
    }
}
