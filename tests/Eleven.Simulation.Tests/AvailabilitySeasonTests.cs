using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #176 in the harness: selection leaves out who is out, the keeper's fallback included; a
    /// headless season never picks a footballer out, plays the quick-sim on the XI it picked, and
    /// carries its sendings-off and injuries; the human's fixture's lineup leaves out his own, and
    /// what happened to them in it is carried, home or away; and the hub names who is out.
    /// </summary>
    public class AvailabilitySeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;

        private static IEnumerable<int> Picked(SquadSelection s) => s.Starters.Concat(s.Bench).Select(p => p.Id);

        [Fact]
        public void Selection_leaves_out_who_is_out_and_with_no_fit_keeper_the_best_at_keeping_goes_in_goal()
        {
            World w = WorldGenerator.Generate(0x176);
            int club = w.Clubs[4].Id;
            SquadSelection full = CareerSquads.Selection(w, club, 10);
            var outs = new HashSet<int> { full.Starters[0].Id, full.Starters[5].Id, full.Starters[10].Id };
            SquadSelection without = CareerSquads.Selection(w, club, 10, outs);
            Assert.Equal(11, without.Starters.Count);
            Assert.Empty(Picked(without).Intersect(outs));

            var keepers = new HashSet<int>(w.Players.Where(p => p.ClubId == club && p.Positions[0] == Position.Goalkeeper).Select(p => p.Id));
            Assert.NotEmpty(keepers);
            SquadSelection noKeeper = CareerSquads.Selection(w, club, 10, keepers);
            Assert.Empty(Picked(noKeeper).Intersect(keepers));
            Player inGoal = noKeeper.Starters[10];
            Player bestAtKeeping = w.Players.Where(p => p.ClubId == club && !keepers.Contains(p.Id))
                .OrderByDescending(p => CareerSquads.KeeperLevel(p)).ThenBy(p => p.Id).First();
            Assert.Equal(bestAtKeeping.Id, inGoal.Id);

            // The quick-sim's side is the XI picked.
            Assert.NotEqual(CareerQuickSim.Side(w, club, 10).Tier, CareerQuickSim.Side(w, club, 10, outs).Tier);
        }

        [Fact]
        public void A_match_with_outfielders_out_has_only_the_bench_it_picked()
        {
            // Codex on #185: with two or more outfielders out the selection's bench is short, but
            // the match kept every bench place, the rest at reference values, eligible to come on
            // and missing from the lineup. Now a side's bench is exactly the substitutes picked.
            World w = WorldGenerator.Generate(0x176);
            int home = w.Clubs[4].Id, away = w.Clubs[30].Id;
            SquadSelection full = CareerSquads.Selection(w, home, 10);
            var outs = new HashSet<int>(full.Starters.Take(4).Select(p => p.Id));
            SquadSelection picked = CareerSquads.Selection(w, home, 10, outs);
            Assert.True(picked.Bench.Count < Squads.BenchOf(10), $"{picked.Bench.Count} substitutes");

            SimState s = CareerSquads.Match(Config, w, home, away, 10, 0x185, homeOut: outs);
            Dictionary<int, int> lineup = CareerSquads.Lineup(w, home, away, 10, homeOut: outs);
            Assert.Equal(picked.Bench.Count, s.Bench.Count(b => b.TeamId == Squads.HomeTeam));
            Assert.Equal(Squads.BenchOf(10), s.Bench.Count(b => b.TeamId == Squads.AwayTeam));
            // Every substitute comes from the bench, so each names a footballer of the lineup.
            Assert.All(s.Bench, b => Assert.True(lineup.ContainsKey(b.Id), $"substitute {b.Id} names no footballer"));
        }

        [Fact]
        public void A_headless_season_never_picks_anyone_out_and_carries_what_happened()
        {
            World w = WorldGenerator.Generate(0x1760);
            Season s = SeasonEngine.Start(w, 1, 0x1760, SeasonEngine.FirstRanking(w), Rules);
            int outPicks = 0, absences = 0, bans = 0, injuries = 0;
            while (!SeasonEngine.Finished(s, Rules))
            {
                MatchdayKind kind = SeasonCalendar.Of(Rules)[s.Matchday].Kind;
                List<Fixture> next = SeasonEngine.Next(s, Rules);
                var expected = new Dictionary<int, (Dictionary<int, int> Lineup, string Text)>();
                foreach (Fixture f in next)
                {
                    HashSet<int> h = Availability.OutOf(w, f.Home, kind), a = Availability.OutOf(w, f.Away, kind);
                    absences += h.Count + a.Count;
                    Dictionary<int, int> lineup = CareerSquads.Lineup(w, f.Home, f.Away, 10, h, a);
                    outPicks += lineup.Values.Count(id => h.Contains(id) || a.Contains(id));
                    expected[f.Id] = (lineup, CareerSeasons.Quick(Config, w, s, f, 10, h, a).ToText());
                }

                CareerSeasons.PlayMatchday(Config, w, s, Rules, 10, (Func<Fixture, PlayedFixture?>?)null);
                foreach (Fixture f in next)
                {
                    MatchResult r = s.Results[f.Id];
                    Assert.Equal(expected[f.Id].Text, r.ToText());
                    foreach (ResultPlayer line in r.Players)
                    {
                        Player p = w.Players.Single(x => x.Id == expected[f.Id].Lineup[line.Player]);
                        if (line.SentOff)
                        {
                            Assert.True((kind == MatchdayKind.League ? p.LeagueBan : p.CupBan) > 0, $"{p.Id} sent off and not banned");
                            bans++;
                        }

                        if (line.Injury > 0)
                        {
                            // Its layoff, less the week to the next matchday; a longer one already running may outlast it.
                            int left = Math.Max(0, Availability.Layoff(s.Seed, f.Id, p.Id, line.Injury, SquadRules.Reference) - SquadRules.Reference.DaysPerMatchday);
                            Assert.True(p.InjuredDays >= left, $"{p.Id} injured for {left} more days, but {p.InjuredDays}");
                            injuries++;
                        }
                    }
                }
            }

            Assert.Equal(0, outPicks);
            Assert.True(bans > 0 && absences > 0, $"{bans} sendings-off, {absences} absences: the season never tested it");
        }

        [Fact]
        public void The_humans_lineup_leaves_out_his_own_and_his_played_fixture_is_carried_home_or_away()
        {
            foreach (bool home in new[] { true, false })
            {
                World w = CareerSession.NewWorld(0x1761);
                int club = QuickMatch.Clubs(w)[0].Id;
                CareerSession s = CareerSession.Start(w, 0x1761, club, CareerRole.PlayerManager, Config);
                while (s.Next() is not Fixture f || (f.Home == club) != home) s.Simulate();
                Fixture fixture = s.Next()!;

                Player banned = CareerSquads.Selection(w, club, CareerSession.PerSide).Starters[2];
                banned.LeagueBan = 1;
                Dictionary<int, int> lineup = s.LineupFor(fixture);
                Assert.Equal(2 * (CareerSession.PerSide + 1) + 2 * Squads.BenchOf(CareerSession.PerSide), lineup.Count);
                Assert.DoesNotContain(banned.Id, lineup.Values);
                int opponent = fixture.Home == club ? fixture.Away : fixture.Home;
                MatchdayKind kind = fixture.Competition == CompetitionId.Cup ? MatchdayKind.Cup : MatchdayKind.League;
                Assert.Equal(MatchdayKind.League, kind);
                Assert.Equal(CareerSquads.Lineup(w, club, opponent, CareerSession.PerSide, Availability.OutOf(w, club, kind), Availability.OutOf(w, opponent, kind)), lineup);

                // His footballer hurt in it and another sent off, on the simulation's home side whatever the ground.
                int hurtSim = Squads.HomeOutfielder(0), offSim = Squads.HomeOutfielder(1);
                Player hurt = w.Players.Single(p => p.Id == lineup[hurtSim]), off = w.Players.Single(p => p.Id == lineup[offSim]);
                MatchResult played = new MatchResult(Squads.HomeTeam, Squads.AwayTeam, 1, 0, 60, 120, Array.Empty<ResultGoal>(), 0)
                    .WithPlayers(new[] { new ResultPlayer(hurtSim, Squads.HomeTeam, 120, 0, false, 2), new ResultPlayer(offSim, Squads.HomeTeam, 80, 0, true, 0) });
                int left = Math.Max(0, Availability.Layoff(s.Season.Seed, fixture.Id, hurt.Id, 2, s.Squad) - s.Squad.DaysPerMatchday);
                s.Finish(fixture, played);
                Assert.Equal(left, hurt.InjuredDays);
                Assert.Equal(1, off.LeagueBan);
                Assert.Equal(0, banned.LeagueBan);
            }
        }

        [Fact]
        public void The_hub_names_who_is_out_and_why()
        {
            World w = CareerSession.NewWorld(0x1762);
            int club = QuickMatch.Clubs(w)[1].Id;
            CareerSession s = CareerSession.Start(w, 0x1762, club, CareerRole.ManagerOnly, Config);
            Assert.Equal("", CareerHub.OutLine(s));

            List<Player> squad = w.Players.Where(p => p.ClubId == club).OrderBy(p => p.Id).ToList();
            squad[3].InjuredDays = 12;
            squad[1].LeagueBan = 1;
            squad[5].LeagueBan = 2;
            squad[5].CupBan = 1;
            squad[7].InjuredDays = 1;
            squad[7].CupBan = 1;
            string Name(Player p) => $"{p.FirstName[0]}. {p.LastName}";
            Assert.Equal($"Out: {Name(squad[1])} (banned, 1 league match), {Name(squad[3])} (injured, 12 days), "
                + $"{Name(squad[5])} (banned, 2 league matches and 1 cup match), {Name(squad[7])} (injured, 1 day; banned, 1 cup match)", CareerHub.OutLine(s));
        }
    }
}
