using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #191: a manager's memory of opponents. Both managers remember each meeting from its result,
    /// each from his side; only head-to-heads, the latest few; and what he remembers of an opponent
    /// shifts his plans and profile for the next meeting, which a career match then plays.
    /// </summary>
    public class ManagerMemoryTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static Manager ManagerOf(World w, int club) => w.Managers.Single(m => m.Id == w.Clubs.Single(c => c.Id == club).ManagerId);

        // A result's goals in its last quarter, the home side's and the away side's.
        private static (int Home, int Away) Late(MatchResult r)
        {
            bool late(ResultGoal g) => g.Half > 2 || (g.Half == 2 && g.ClockSeconds >= r.HalfSeconds / 2);
            return (r.Goals.Count(g => late(g) && g.Team == r.HomeTeam), r.Goals.Count(g => late(g) && g.Team == r.AwayTeam));
        }

        [Fact]
        public void A_meeting_is_remembered_by_both_managers_from_its_result_each_from_his_side()
        {
            World w = CareerSession.NewWorld(0x191);
            CareerSession s = CareerSession.Start(w, 0x191, w.Clubs[4].Id, CareerRole.PlayerManager, Config);

            // A quick-simmed matchday: every fixture remembered, nothing of a match not played in full.
            List<Fixture> first = SeasonEngine.Next(s.Season, s.Rules);
            Fixture mine = s.Next()!;
            MatchResult played = CareerSquads.Play(Config, w, s.Club, mine.Home == s.Club ? mine.Away : mine.Home, CareerSession.PerSide, 0x191);
            s.Finish(mine, played);
            foreach (Fixture f in first)
            {
                MatchResult r = s.Season.Results[f.Id];
                Meeting home = ManagerMemory.With(ManagerOf(w, f.Home), f.Away).Single(), away = ManagerMemory.With(ManagerOf(w, f.Away), f.Home).Single();
                (int lateHome, int lateAway) = Late(r);
                Assert.Equal((1, 1), (home.Season, home.Matchday));
                Assert.Equal((r.HomeGoals, r.AwayGoals, lateHome, lateAway), (home.GoalsFor, home.GoalsAgainst, home.LateFor, home.LateAgainst));
                Assert.Equal((r.AwayGoals, r.HomeGoals, lateAway, lateHome), (away.GoalsFor, away.GoalsAgainst, away.LateFor, away.LateAgainst));
                Assert.Equal(CareerManagers.FormationOf(w, f.Away), home.OpponentFormation);
                Assert.Equal(CareerManagers.FormationOf(w, f.Home), away.OpponentFormation);

                // The human's fixture was played in full, so its metrics are remembered; the rest weren't.
                bool full = f.Id == mine.Id;
                Assert.Equal((full, full), (home.Full, away.Full));
                if (full)
                {
                    Assert.Equal((r.HomeMetrics.Ppda, r.AwayMetrics.Ppda, r.HomeMetrics.Possession), (home.Ppda, home.OpponentPpda, home.Possession));
                    Assert.Equal((r.AwayMetrics.Ppda, r.HomeMetrics.Ppda, r.AwayMetrics.Possession), (away.Ppda, away.OpponentPpda, away.Possession));
                }
            }
        }

        [Fact]
        public void A_manager_remembers_only_his_clubs_meetings_the_latest_few_of_each()
        {
            World w = CareerSession.NewWorld(0x1911);
            CareerSession s = CareerSession.Start(w, 0x1911, w.Clubs[0].Id, CareerRole.ManagerOnly, Config);
            var met = new Dictionary<(int, int), List<(int Season, int Matchday)>>();
            for (int season = 1; season <= 2; season++)
            {
                while (!s.SeasonOver)
                {
                    List<Fixture> day = SeasonEngine.Next(s.Season, s.Rules);
                    int matchday = s.Season.Matchday + 1;
                    s.Simulate();
                    foreach (Fixture f in day)
                    {
                        foreach ((int club, int other) in new[] { (f.Home, f.Away), (f.Away, f.Home) })
                        {
                            if (!met.TryGetValue((club, other), out List<(int, int)>? list)) met[(club, other)] = list = new List<(int, int)>();
                            list.Add((s.Season.Number, matchday));
                        }
                    }
                }

                if (season == 1) s.NextSeason();
            }

            int depth = MemoryRules.Reference.Depth, kept = 0;
            foreach (Club c in w.Clubs)
            {
                Manager m = ManagerOf(w, c.Id);
                Assert.All(m.Memory, x => Assert.True(met.ContainsKey((c.Id, x.Opponent)), $"{c.Id} remembers {x.Opponent}, never met"));
                foreach (IGrouping<int, Meeting> g in m.Memory.GroupBy(x => x.Opponent))
                {
                    List<(int, int)> all = met[(c.Id, g.Key)];
                    Assert.Equal(all.Skip(System.Math.Max(0, all.Count - depth)), g.Select(x => (x.Season, x.Matchday)));
                    kept += g.Count();
                }
            }

            Assert.Contains(met.Values, v => v.Count > depth);
            Assert.True(kept > 0);
        }

        // A manager with these meetings with club 99.
        private static Manager With(params Meeting[] meetings)
        {
            var m = new Manager { Temperament = 10, Pressing = 10, Depth = 10 };
            for (int i = 0; i < m.Competences.Length; i++) m.Competences[i] = 10;
            foreach (Meeting x in meetings)
            {
                x.Opponent = 99;
                m.Memory.Add(x);
            }

            return m;
        }

        [Fact]
        public void What_he_remembers_shifts_his_line_his_step_up_and_when_he_protects_each_capped()
        {
            MemoryRules rules = MemoryRules.Reference;
            Assert.Equal((0.0, 0.0, 0.0), ManagerMemory.Shift(With(), 99));

            // Beaten 0–2 over his memory: his line 1 m deeper, protecting 0.06 of the match earlier.
            (double line, double step, double protect) = ManagerMemory.Shift(With(new Meeting { GoalsFor = 0, GoalsAgainst = 2 }), 99);
            Assert.Equal(-2 * rules.LineShiftPerGoal, line, 9);
            Assert.Equal(2 * rules.ProtectShiftPerGoal, protect, 9);
            Assert.Equal(0, step);

            // Beating them, the mirror image; a heavy record, capped.
            (line, _, protect) = ManagerMemory.Shift(With(new Meeting { GoalsFor = 3, GoalsAgainst = 1 }), 99);
            Assert.Equal((2 * rules.LineShiftPerGoal, -2 * rules.ProtectShiftPerGoal), (line, protect));
            (line, _, protect) = ManagerMemory.Shift(With(new Meeting { GoalsAgainst = 5 }, new Meeting { GoalsAgainst = 6 }), 99);
            Assert.Equal((-rules.LineShiftMax, rules.ProtectShiftMax), (line, protect));

            // Caught late: earlier still. Out-pressed in full matches: his step-up eases.
            (_, _, protect) = ManagerMemory.Shift(With(new Meeting { GoalsFor = 1, GoalsAgainst = 1, LateAgainst = 1 }), 99);
            Assert.Equal(rules.LateShift, protect, 9);
            (_, step, _) = ManagerMemory.Shift(With(new Meeting { GoalsFor = 1, GoalsAgainst = 1, Full = true, Ppda = 12, OpponentPpda = 8 }), 99);
            Assert.Equal(-rules.StepUpEase, step);
            (_, step, _) = ManagerMemory.Shift(With(new Meeting { GoalsFor = 1, GoalsAgainst = 1, Full = false, Ppda = 12, OpponentPpda = 8 }), 99);
            Assert.Equal(0, step);

            // His plans and profile for them: shifted, in range; with no memory, exactly his own.
            SimConfig c = Config;
            Manager none = With(), beaten = With(new Meeting { GoalsAgainst = 2 });
            Assert.Equal(CareerManagers.Plans(none, 1).Select(t => (t.Line, t.StepUp, t.Press)), CareerManagers.Plans(none, 1, 99).Select(t => (t.Line, t.StepUp, t.Press)));
            Assert.Equal(CareerManagers.Profile(none, c).ProtectFrom, CareerManagers.Profile(none, c, 99).ProtectFrom);
            TeamTactic[] own = CareerManagers.Plans(beaten, 1), shifted = CareerManagers.Plans(beaten, 1, 99);
            for (int i = 0; i < 3; i++) Assert.Equal(own[i].Line - 2 * rules.LineShiftPerGoal, shifted[i].Line, 9);
            Assert.Equal(CareerManagers.Profile(beaten, c).ProtectFrom - 2 * rules.ProtectShiftPerGoal, CareerManagers.Profile(beaten, c, 99).ProtectFrom, 9);
        }

        [Fact]
        public void A_career_match_plays_the_shifted_plans_of_a_manager_who_remembers_his_opponent()
        {
            World w = WorldGenerator.Generate(0x191);
            int home = w.Clubs[2].Id, away = w.Clubs[7].Id;
            Manager theirs = ManagerOf(w, away);
            SimState before = CareerSquads.Match(Config, w, home, away, 10, 0x191);
            theirs.Memory.Add(new Meeting { Opponent = home, Season = 1, Matchday = 1, GoalsFor = 0, GoalsAgainst = 3 });
            SimState after = CareerSquads.Match(Config, w, home, away, 10, 0x191);
            TeamTactic[] Away(SimState st) => st.Tactics.Where(t => t.TeamId == Squads.AwayTeam).ToArray();
            Assert.Equal(CareerManagers.Plans(theirs, Squads.AwayTeam, home).Select(t => t.Line), Away(after).Select(t => t.Line));
            Assert.NotEqual(Away(before).Select(t => t.Line), Away(after).Select(t => t.Line));
            Assert.Equal(CareerManagers.Profile(theirs, Config, home).ProtectFrom, CareerManagers.Profiles(w, home, away, Config)[Squads.AwayTeam].ProtectFrom);
        }

        [Fact]
        public void The_hub_names_the_last_meeting_with_the_next_opponent()
        {
            World w = CareerSession.NewWorld(0x1912);
            CareerSession s = CareerSession.Start(w, 0x1912, w.Clubs[3].Id, CareerRole.PlayerManager, Config);
            Fixture f = s.Next()!;
            int opponent = f.Home == s.Club ? f.Away : f.Home;
            Assert.DoesNotContain("Last met", CareerHub.NextLine(s));
            Manager mine = ManagerOf(w, s.Club);
            mine.Memory.Add(new Meeting { Opponent = opponent, Season = 1, Matchday = 2, GoalsFor = 2, GoalsAgainst = 2 });
            mine.Memory.Add(new Meeting { Opponent = opponent, Season = 1, Matchday = 7, GoalsFor = 1, GoalsAgainst = 2 });
            Assert.EndsWith(". Last met: lost 1–2 (season 1, matchday 7)", CareerHub.NextLine(s));
            mine.Memory.Add(new Meeting { Opponent = opponent, Season = 2, Matchday = 1, GoalsFor = 3, GoalsAgainst = 0 });
            Assert.Equal("Last met: won 3–0 (season 2, matchday 1)", CareerHub.LastMet(s, opponent));
        }
    }
}
