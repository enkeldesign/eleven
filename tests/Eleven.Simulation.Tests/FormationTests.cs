using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.AI;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#125: formations as data, in a side's state, read by its CPU through its observation.</summary>
    public class FormationTests
    {
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        public static IEnumerable<object[]> Every => Formations.All.Select(f => new object[] { f });

        [Theory]
        [MemberData(nameof(Every))]
        public void Each_formation_has_the_lines_its_name_says_from_the_back(Formation f)
        {
            FormationLine[] lines = Formations.Lines(f);
            Assert.Equal(Formations.Name(f), string.Join("-", lines.Select(l => l.Count)));
            Assert.Equal(Formations.Outfielders, lines.Sum(l => l.Count));
            for (int i = 1; i < lines.Length; i++) Assert.True(lines[i].Depth > lines[i - 1].Depth, $"{Formations.Name(f)}: line {i} isn't ahead of line {i - 1}");
            Assert.Equal(f, Formations.Parse(Formations.Name(f)));
        }

        [Theory]
        [MemberData(nameof(Every))]
        public void Each_formation_lines_up_on_the_pitch_in_its_own_half_with_no_two_on_one_spot(Formation f)
        {
            SimState s = Squads.Match(C, 1, 10, homeFormation: f, awayFormation: f);
            Assert.All(s.Teams, t => Assert.Equal(f, t.Formation));
            foreach (TeamState team in s.Teams)
            {
                PlayerState[] side = s.Players.Where(p => p.TeamId == team.Id).ToArray();
                Assert.Equal(11, side.Length);
                foreach (PlayerState p in side)
                {
                    Assert.InRange(Math.Abs(p.Position.Y), 0, C.TouchlineY - 1);
                    Assert.True(team.AttackSign * p.Position.X < 0, $"{Formations.Name(f)}: {p.Id} at {p.Position.X:F1} is not in his own half");
                }

                for (int i = 0; i < side.Length; i++)
                {
                    for (int j = i + 1; j < side.Length; j++)
                    {
                        double apart = Math.Sqrt((side[i].Position - side[j].Position).LengthSquared);
                        Assert.True(apart >= 4, $"{Formations.Name(f)}: {side[i].Id} and {side[j].Id} line up {apart:F1} m apart");
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Every))]
        public void Every_home_lies_inside_the_pitch_and_no_two_places_share_one(Formation f)
        {
            var homes = Enumerable.Range(1, 10).Select(slot => Formations.Home(f, 10, slot)).ToArray();
            Assert.All(homes, h =>
            {
                Assert.InRange(Math.Abs(h.X), 0, C.GoalLineX - 6);
                Assert.InRange(Math.Abs(h.Y), 0, C.TouchlineY - 6);
            });
            Assert.Equal(10, homes.Distinct().Count());
        }

        [Fact]
        public void At_full_strength_the_four_four_two_is_the_shape_before_formations()
        {
            (double X, double Y)[] before =
            {
                (-24, -22), (-24, -22 / 3.0), (-24, 22 / 3.0), (-24, 22),
                (-10, -22), (-10, -22 / 3.0), (-10, 22 / 3.0), (-10, 22),
                (4, -10), (4, 10),
            };
            for (int slot = 1; slot <= 10; slot++)
            {
                Vec2 home = OutfieldBrain.ShapeHome(Formation.FourFourTwo, 10, slot);
                Assert.Equal(before[slot - 1].X, home.X, 12);
                Assert.Equal(before[slot - 1].Y, home.Y, 12);
            }
        }

        [Theory]
        [InlineData(Formation.FourFourTwo, 9, "4-4-1")]
        [InlineData(Formation.FourFourTwo, 8, "4-3-1")]
        [InlineData(Formation.FourThreeThree, 9, "4-3-2")]
        [InlineData(Formation.FourThreeThree, 8, "4-3-1")]
        [InlineData(Formation.FourTwoThreeOne, 9, "4-2-2-1")]
        [InlineData(Formation.ThreeFiveTwo, 9, "3-5-1")]
        [InlineData(Formation.FiveThreeTwo, 9, "5-3-1")]
        [InlineData(Formation.ThreeFourThree, 9, "3-4-2")]
        public void A_side_short_of_men_closes_up_from_the_front(Formation f, int n, string lines)
        {
            Assert.Equal(lines, string.Join("-", Formations.Lines(f, n).Select(l => l.Count)));
        }

        [Theory]
        [MemberData(nameof(Every))]
        public void Below_eight_a_side_every_formation_plays_the_small_sides_shape(Formation f)
        {
            for (int n = 3; n < 8; n++)
            {
                for (int slot = 1; slot <= n; slot++) Assert.Equal(OutfieldBrain.ShapeHome(Formation.FourFourTwo, n, slot), OutfieldBrain.ShapeHome(f, n, slot));
            }
        }

        [Fact]
        public void A_cpu_side_plays_to_its_formation()
        {
            // The same match, home in a 5-3-2 and then in a 3-4-3. Where the ball goes moves the whole
            // side, so the measure is its shape: how far its middle stands above its last man.
            // A 5-3-2's homes put it 9 m above, a 3-4-3's 14 m.
            double deep = AboveLastMan(Formation.FiveThreeTwo), high = AboveLastMan(Formation.ThreeFourThree);
            Assert.True(deep < high - 2, $"5-3-2's middle {deep:F1} m above its last man, 3-4-3's {high:F1} m");
        }

        // How far the home side's outfielders' mean x stands above its deepest one's, in its own
        // frame, once a second in live play, over a half.
        private static double AboveLastMan(Formation home)
        {
            var sim = new Simulator(C, Squads.Match(C, 0x125000001UL, 10, bothCpu: true, homeFormation: home));
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            double sum = 0;
            int samples = 0;
            while (sim.Tick < C.HalfTicks)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                if (s.Restart.Phase != RestartPhase.Live || s.Tick % C.TickRate != 0) continue;
                TeamState team = s.Teams.Single(t => t.Id == Squads.HomeTeam);
                double[] x = s.Players.Where(p => p.TeamId == team.Id && p.Slot > 0).Select(p => team.AttackSign * p.Position.X).ToArray();
                sum += x.Average() - x.Min();
                samples++;
            }

            return sum / samples;
        }

        [Fact]
        public void A_snapshot_keeps_each_sides_formation()
        {
            SimState s = Squads.Match(C, 2, 10, homeFormation: Formation.FourTwoThreeOne, awayFormation: Formation.FiveThreeTwo);
            var sim = new Simulator(C, s);
            var restored = new Simulator(C, Snapshot.Read(Snapshot.Capture(sim)).State);
            Assert.Equal(new[] { Formation.FourTwoThreeOne, Formation.FiveThreeTwo }, restored.PeekState().Teams.Select(t => t.Formation));
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }

        [Fact]
        public void The_formation_library_cannot_be_changed()
        {
            // No static mutable state in the simulation (AGENTS.md).
            var all = (IList<Formation>)Formations.All;
            Formation first = all[0];
            try
            {
                Assert.Throws<NotSupportedException>(() => all[0] = Formation.ThreeFourThree);
            }
            finally
            {
                if (all[0] != first) all[0] = first; // leave it as found for the other tests
            }
        }

        [Fact]
        public void The_contrasts_possession_follows_whoever_last_played_the_ball()
        {
            SimState s = Squads.Match(C, 1, 10);
            var possession = new Possession();
            int home = Squads.HomeOutfielder(3), away = Squads.AwayOutfielder(10, 3);
            s.Ball.LastPlayedBy = home;
            possession.Observe(s);
            Assert.Equal(Squads.HomeTeam, possession.Team);

            // Away plays it first time (a kick, header or volley, with no touch): away has it, and won it.
            s.Ball.LastPlayedBy = away;
            Assert.Equal(Squads.AwayTeam, possession.Observe(s));
            Assert.Equal(Squads.AwayTeam, possession.Team);
        }

        [Fact]
        public void A_state_with_an_unknown_formation_is_refused()
        {
            SimState s = Squads.Match(C, 3, 10);
            s.Teams[1].Formation = (Formation)200;
            Assert.Throws<ArgumentException>(() => new Simulator(C, s));
        }
    }
}
