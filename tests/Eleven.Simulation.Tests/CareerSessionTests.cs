using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #167: a career session. A season simulated runs to its end with the human's 22 league
    /// matches; his fixture, played, is recorded on its own ground, home or away; a result seen
    /// from the other ground is exact and its own inverse; the role decides whether he plays; and
    /// a career seed gives the same career.
    /// </summary>
    public class CareerSessionTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(CareerRole role = CareerRole.PlayerManager, ulong seed = 0x167, int pick = 0)
        {
            World w = CareerSession.NewWorld(seed);
            Assert.NotEmpty(w.Clubs);
            int club = QuickMatch.Clubs(w)[pick].Id;
            return CareerSession.Start(w, seed, club, role, Config);
        }

        [Fact]
        public void A_new_career_has_its_own_world_its_club_and_its_first_season()
        {
            CareerSession c = New();
            Assert.Equal(WorldCodec.Encode(WorldGenerator.Generate(0x167)), WorldCodec.Encode(CareerSession.NewWorld(0x167)));
            Assert.Equal(1, c.Season.Number);
            Assert.Equal(396, c.Season.Fixtures.Count);
            Assert.Equal(1, c.Division);
            Assert.Contains(c.Club, c.Season.Divisions[0]);
            Assert.False(c.SeasonOver);
            Assert.Equal(12, c.Table().Count);
        }

        [Fact]
        public void A_season_simulated_reaches_its_end_with_the_humans_22_league_matches()
        {
            CareerSession c = New();
            int matchdays = 0;
            while (!c.SeasonOver)
            {
                c.Simulate();
                Assert.True(++matchdays <= 28);
            }

            Assert.Equal(28, matchdays);
            TableRow mine = c.Table().Single(r => r.Club == c.Club);
            Assert.Equal(22, mine.Played);
            Assert.InRange(c.Position, 1, 12);
            Assert.Equal(c.Table().FindIndex(r => r.Club == c.Club) + 1, c.Position);
            List<(Fixture Fixture, MatchResult Result)> last = c.LastResults(3);
            Assert.Equal(3, last.Count);
            Assert.All(last, x => Assert.True(x.Fixture.Home == c.Club || x.Fixture.Away == c.Club));

            c.NextSeason();
            Assert.Equal(2, c.Season.Number);
            Assert.False(c.SeasonOver);
        }

        [Fact]
        public void A_top_club_has_a_bye_in_the_preliminary_round()
        {
            CareerSession c = New();
            // Matchdays 1 to 3 are league rounds, the 4th the cup's preliminary round.
            for (int i = 0; i < 3; i++)
            {
                Assert.NotNull(c.Next());
                c.Simulate();
            }

            Assert.Null(c.Next());
            Assert.False(c.SeasonOver);
            c.Simulate();
            Assert.NotNull(c.Next());
        }

        private static MatchResult Won(int by) => new MatchResult(1, 2, by, 0, 60, 120, Array.Empty<ResultGoal>(), 0, homeFouls: 5, awayFouls: 1);

        [Fact]
        public void A_played_fixture_is_recorded_on_its_own_ground_home_or_away()
        {
            CareerSession c = New();
            bool home = false, away = false;
            for (int day = 0; day < 22 && !(home && away); day++)
            {
                Fixture? f = c.Next();
                if (f == null)
                {
                    c.Simulate();
                    continue;
                }

                bool atHome = f.Home == c.Club;
                c.Finish(f, Won(3));
                MatchResult r = c.Season.Results[f.Id];
                // The human's club won 3-0, wherever it played.
                Assert.Equal(atHome ? 3 : 0, r.HomeGoals);
                Assert.Equal(atHome ? 0 : 3, r.AwayGoals);
                Assert.Equal(atHome ? 5 : 1, r.HomeFouls);
                home |= atHome;
                away |= !atHome;
                // The rest of the matchday is played too.
                Assert.All(c.Season.Fixtures.Where(x => x.Competition != CompetitionId.Cup && x.Round == f.Round), x => Assert.True(c.Season.Results.ContainsKey(x.Id)));
            }

            Assert.True(home && away);
        }

        [Fact]
        public void A_result_seen_from_the_other_ground_is_exact_and_its_own_inverse()
        {
            World w = WorldGenerator.Generate(0x157);
            MatchResult r = CareerSquads.Play(Config.With("knockout", 1), w, w.Clubs[0].Id, w.Clubs[30].Id, 10, 0x167);
            MatchResult m = r.Mirrored();
            Assert.NotSame(r, m);
            Assert.Equal((r.AwayTeam, r.HomeTeam, r.AwayGoals, r.HomeGoals), (m.HomeTeam, m.AwayTeam, m.HomeGoals, m.AwayGoals));
            Assert.Equal((r.AwayFouls, r.HomeFouls, r.AwayCautions, r.HomeCautions, r.AwaySendingsOff, r.HomeSendingsOff), (m.HomeFouls, m.AwayFouls, m.HomeCautions, m.AwayCautions, m.HomeSendingsOff, m.AwaySendingsOff));
            Assert.Equal((r.AwayPenalties, r.HomePenalties, r.AwayInjuries, r.HomeInjuries, r.AwaySubstitutions, r.HomeSubstitutions), (m.HomePenalties, m.AwayPenalties, m.HomeInjuries, m.AwayInjuries, m.HomeSubstitutions, m.AwaySubstitutions));
            Assert.Equal((r.AwayShootoutKicks, r.HomeShootoutKicks, r.AwayShootoutGoals, r.HomeShootoutGoals), (m.HomeShootoutKicks, m.AwayShootoutKicks, m.HomeShootoutGoals, m.AwayShootoutGoals));
            Assert.Equal((r.HalfSeconds, r.PlayedSeconds, r.Restarts, r.ExtraTime), (m.HalfSeconds, m.PlayedSeconds, m.Restarts, m.ExtraTime));
            Assert.Equal(r.AwayMetrics.Shots, m.HomeMetrics.Shots);
            Assert.Equal(r.HomeMetrics.Possession, m.AwayMetrics.Possession);
            Assert.Equal(r.Goals.Select(g => (g.Team, g.Half, g.ClockSeconds)), m.Goals.Select(g => (g.Team, g.Half, g.ClockSeconds)));
            Assert.Equal(r.Winner, m.Winner);
            Assert.True(r.HomeMetrics.Passes + r.AwayMetrics.Passes > 0, "a played match has passes");
            Assert.Equal(r.ToText(), m.Mirrored().ToText());
        }

        [Fact]
        public void A_player_manager_plays_his_clubs_footballer_and_a_manager_only_watches()
        {
            foreach (CareerRole role in new[] { CareerRole.PlayerManager, CareerRole.ManagerOnly })
            {
                CareerSession c = New(role);
                Fixture? f = c.Next();
                Assert.NotNull(f);
                SimState s = c.MatchFor(f!, 0x167);
                // A human footballer: his controller's, and no CPU state plays him.
                int human = s.Players.Count(p => p.OwnerControllerId == OneVOne.HomeController && !s.Cpus.Any(x => x.PlayerId == p.Id));
                Assert.Equal(role == CareerRole.PlayerManager ? 1 : 0, human);

                // His club is the simulation's home side, on its own footballers' values.
                SquadSelection xi = CareerSquads.Selection(c.World, c.Club, CareerSession.PerSide);
                PlayerState first = s.Players.Single(p => p.Id == Squads.HomeOutfielder(1));
                Assert.Equal(RatingMap.Capabilities(xi.Starters[1]).MaxSpeed, first.Capabilities.MaxSpeed, 12);
                Assert.Equal(2 * TeamTactics.PerSide, s.Tactics.Length);
            }
        }

        [Fact]
        public void A_manager_only_match_played_by_the_simulation_is_recorded_from_his_side()
        {
            CareerSession c = New(CareerRole.ManagerOnly);
            Fixture? f = c.Next();
            Assert.NotNull(f);
            SimConfig mc = c.MatchConfig(f!);
            var sim = new Simulator(mc, c.MatchFor(f!, 0x1670));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            while (recorder.Result == null && sim.Tick < 8L * mc.HalfTicks)
            {
                commands.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                events.Clear();
                sim.Step(commands, events);
                recorder.Observe(mc, sim.PeekState(), events);
            }

            Assert.NotNull(recorder.Result);
            c.Finish(f!, recorder.Result!);
            MatchResult r = c.Season.Results[f!.Id];
            int mine = f.Home == c.Club ? r.HomeGoals : r.AwayGoals, theirs = f.Home == c.Club ? r.AwayGoals : r.HomeGoals;
            Assert.Equal((recorder.Result!.HomeGoals, recorder.Result.AwayGoals), (mine, theirs));
            Assert.Equal(1, c.Season.Matchday);
        }

        [Fact]
        public void A_cup_tie_is_a_knockout_and_a_career_seed_gives_the_same_career()
        {
            CareerSession a = New(), b = New();
            for (int i = 0; i < 6; i++)
            {
                a.Simulate();
                b.Simulate();
            }

            Assert.NotEmpty(a.Season.Results);
            Assert.Equal(a.Season.Results.OrderBy(x => x.Key).Select(x => x.Value.ToText()), b.Season.Results.OrderBy(x => x.Key).Select(x => x.Value.ToText()));
            Fixture tie = a.Season.Fixtures.First(f => f.Competition == CompetitionId.Cup);
            Assert.Equal(1, a.MatchConfig(tie).Knockout);
            Assert.Equal(0, a.MatchConfig(a.Season.Fixtures[0]).Knockout);
        }
    }
}
