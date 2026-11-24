using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;
using Line = Eleven.Simulation.Harness.CareerSquads.Line;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #180: a manager's philosophy. His three plans by his pressing, depth and temperament, in
    /// range and in his formation; a world's managers spread across them; a career match storing
    /// both managers' plans; the selection's slots by each formation's lines; and an XI picked for
    /// his formation.
    /// </summary>
    public class ManagerPlansTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static Manager With(int pressing, int depth, int temperament, Formation f = Formation.FourThreeThree) =>
            new Manager { Id = 1, Pressing = pressing, Depth = depth, Temperament = temperament, PreferredFormation = f };

        private static void InRange(TeamTactic t)
        {
            Assert.InRange(t.StepUp, 0, TeamTactic.StepUpMax);
            Assert.InRange(t.Line, -TeamTactic.LineMax, TeamTactic.LineMax);
            Assert.InRange(t.Width, TeamTactic.WidthMin, TeamTactic.WidthMax);
            Assert.InRange(t.Press, TeamTactic.PressMin, TeamTactic.PressMax);
        }

        private static double[] Fields(TeamTactic t) => new[] { t.StepUp, t.Line, t.Width, t.Press };

        [Fact]
        public void A_manager_without_a_philosophy_plays_the_reference_and_every_plan_is_his_formation()
        {
            TeamTactic[] plans = CareerManagers.Plans(With(0, 0, 10, Formation.ThreeFiveTwo), Squads.AwayTeam);
            Assert.Equal(3, plans.Length);
            TeamTactic reference = TeamTactic.Reference(Squads.AwayTeam, Formation.ThreeFiveTwo);
            Assert.Equal(Fields(reference), Fields(plans[0]));
            Assert.All(plans, p => Assert.Equal((Squads.AwayTeam, Formation.ThreeFiveTwo), (p.TeamId, p.Formation)));
        }

        [Fact]
        public void His_depth_sets_his_line_and_his_pressing_his_press_and_step_up()
        {
            TeamTactic deep = CareerManagers.Plans(With(10, 1, 10), 0)[0], high = CareerManagers.Plans(With(10, 20, 10), 0)[0];
            Assert.Equal(-6, deep.Line, 9);
            Assert.Equal(6, high.Line, 9);
            for (int d = 1; d < 20; d++) Assert.True(CareerManagers.Plans(With(10, d + 1, 10), 0)[0].Line > CareerManagers.Plans(With(10, d, 10), 0)[0].Line);

            TeamTactic sitting = CareerManagers.Plans(With(1, 10, 10), 0)[0], pressing = CareerManagers.Plans(With(20, 10, 10), 0)[0];
            Assert.Equal(TacticTrials.LowBlock.Press, sitting.Press, 9);
            Assert.Equal(TeamTactic.PressMax, pressing.Press, 9);
            Assert.Equal(TacticTrials.HighPress.StepUp, pressing.StepUp, 9);
            Assert.True(sitting.Press < CareerManagers.Plans(With(8, 10, 10), 0)[0].Press);
        }

        [Fact]
        public void He_chases_toward_the_high_press_and_protects_toward_the_low_block_by_his_temperament()
        {
            foreach (int pressing in new[] { 1, 6, 11, 16, 20 })
            foreach (int depth in new[] { 1, 10, 20 })
            {
                TeamTactic[] bold = CareerManagers.Plans(With(pressing, depth, 20), 0), cautious = CareerManagers.Plans(With(pressing, depth, 1), 0);
                foreach (TeamTactic[] plans in new[] { bold, cautious })
                {
                    Assert.All(plans, InRange);
                    double[] b = Fields(plans[0]), c = Fields(plans[1]), p = Fields(plans[2]), hi = Fields(TacticTrials.HighPress), lo = Fields(TacticTrials.LowBlock);
                    for (int i = 0; i < 4; i++)
                    {
                        Assert.True(Math.Abs(c[i] - hi[i]) <= Math.Abs(b[i] - hi[i]) + 1e-9, $"chase field {i}");
                        Assert.True(Math.Abs(p[i] - lo[i]) <= Math.Abs(b[i] - lo[i]) + 1e-9, $"protect field {i}");
                    }
                }

                // The bold chase further, the cautious sit deeper.
                Assert.True(Math.Abs(Fields(bold[1])[1] - Fields(TacticTrials.HighPress)[1]) <= Math.Abs(Fields(cautious[1])[1] - Fields(TacticTrials.HighPress)[1]) + 1e-9);
                Assert.True(Math.Abs(Fields(cautious[2])[1] - Fields(TacticTrials.LowBlock)[1]) <= Math.Abs(Fields(bold[2])[1] - Fields(TacticTrials.LowBlock)[1]) + 1e-9);
            }

            TeamTactic[] mid = CareerManagers.Plans(With(10, 10, 10), 0);
            Assert.True(mid[1].Line > mid[0].Line && mid[2].Line < mid[0].Line);
        }

        [Fact]
        public void A_worlds_managers_spread_across_philosophies_and_formations()
        {
            World w = WorldGenerator.Generate(0x180);
            Assert.All(w.Managers, m => Assert.InRange(m.Pressing, 1, 20));
            Assert.All(w.Managers, m => Assert.InRange(m.Depth, 1, 20));
            Assert.True(w.Managers.Select(m => m.Pressing).Distinct().Count() >= 8);
            Assert.True(w.Managers.Select(m => m.Depth).Distinct().Count() >= 8);
            Assert.True(w.Clubs.Select(c => CareerManagers.FormationOf(w, c.Id)).Distinct().Count() >= 3);
            Assert.All(w.Clubs, c => Assert.Equal(w.Managers.Single(m => m.Id == c.ManagerId).PreferredFormation, CareerManagers.FormationOf(w, c.Id)));
        }

        [Fact]
        public void A_career_match_stores_both_managers_plans_and_formations()
        {
            World w = WorldGenerator.Generate(0x180);
            Assert.Contains(w.Clubs, c => CareerManagers.FormationOf(w, c.Id) != Formation.FourFourTwo);
            Club home = w.Clubs.First(c => CareerManagers.FormationOf(w, c.Id) != Formation.FourFourTwo);
            Club away = w.Clubs.First(c => c.Id != home.Id && CareerManagers.FormationOf(w, c.Id) != CareerManagers.FormationOf(w, home.Id));
            SimState s = CareerSquads.Match(Config, w, home.Id, away.Id, 10, 1);
            TeamTactic[] mine = CareerManagers.Plans(w, home.Id, Squads.HomeTeam), theirs = CareerManagers.Plans(w, away.Id, Squads.AwayTeam);
            Assert.Equal(mine.Concat(theirs).Select(Fields).Select(f => string.Join(",", f)), s.Tactics.Select(Fields).Select(f => string.Join(",", f)));
            Assert.Equal(CareerManagers.FormationOf(w, home.Id), s.Teams[0].Formation);
            Assert.Equal(CareerManagers.FormationOf(w, away.Id), s.Teams[1].Formation);

            CareerSession session = CareerSession.Start(w, 0x180, home.Id, CareerRole.ManagerOnly, Config);
            SimState played = session.MatchFor(session.Next()!, 2);
            Assert.Equal(CareerManagers.FormationOf(w, home.Id), played.Teams[0].Formation);
            Assert.Equal(string.Join(",", Fields(mine[1])), string.Join(",", Fields(played.Tactics[1])));
        }

        [Fact]
        public void The_slots_take_each_formations_lines_from_the_back()
        {
            // Index 0 is the kick-off taker, in the last slot; slots run from the back.
            Line D = Line.Defence, M = Line.Midfield, A = Line.Attack;
            Assert.Equal(new[] { A, D, D, D, D, M, M, M, M, A }, CareerSquads.SlotLines(10, Formation.FourFourTwo));
            Assert.Equal(new[] { A, D, D, D, D, M, M, M, A, A }, CareerSquads.SlotLines(10, Formation.FourThreeThree));
            Assert.Equal(new[] { A, D, D, D, M, M, M, M, M, A }, CareerSquads.SlotLines(10, Formation.ThreeFiveTwo));
            Assert.Equal(new[] { A, D, D, D, D, D, M, M, M, A }, CareerSquads.SlotLines(10, Formation.FiveThreeTwo));
            Assert.Equal(new[] { A, D, D, D, D, M, M, M, M, M }, CareerSquads.SlotLines(10, Formation.FourTwoThreeOne));
            Assert.Equal(new[] { A, D, D, D, M, M, M, M, A, A }, CareerSquads.SlotLines(10, Formation.ThreeFourThree));
        }

        [Fact]
        public void An_xi_is_picked_for_his_formation()
        {
            World w = WorldGenerator.Generate(0x180);
            Club club = w.Clubs.First(c => w.Players.Count(p => p.ClubId == c.Id && p.Positions[0] == Position.Striker) >= 3
                && w.Players.Count(p => p.ClubId == c.Id && p.Positions[0] == Position.CentreBack || p.ClubId == c.Id && p.Positions[0] == Position.FullBack) >= 5);
            Manager m = w.Managers.Single(x => x.Id == club.ManagerId);
            int Strikers() => CareerSquads.Selection(w, club.Id, 10).Starters.Take(10).Count(p => p.Positions[0] == Position.Striker);
            int Defenders() => CareerSquads.Selection(w, club.Id, 10).Starters.Take(10).Count(p => p.Positions[0] is Position.CentreBack or Position.FullBack);
            m.PreferredFormation = Formation.FourFourTwo;
            Assert.Equal(2, Strikers());
            m.PreferredFormation = Formation.FourThreeThree;
            Assert.Equal(3, Strikers());
            m.PreferredFormation = Formation.FiveThreeTwo;
            Assert.Equal(5, Defenders());
            Assert.Equal(2, Strikers());
        }
    }
}
