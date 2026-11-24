using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #179: your squad. The rows; a swap, from the AI's pick at first; those out refused; the AI's
    /// pick again; picks placed in the slots by the AI's rule; a pick out by kick-off replaced, and
    /// the hub saying so; and the picks played, watched or simulated.
    /// </summary>
    public class SquadPickTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(ulong seed = 0x179, CareerRole role = CareerRole.PlayerManager)
        {
            World w = CareerSession.NewWorld(seed);
            return CareerSession.Start(w, seed, QuickMatch.Clubs(w)[2].Id, role, Config);
        }

        private static List<int> Group(CareerSession s, SquadGroup g)
        {
            List<SquadRow> rows = CareerHub.SquadRows(s);
            Assert.NotEmpty(rows);
            List<int> ids = rows.Where(r => r.Group == g).Select(r => r.Player).ToList();
            Assert.NotEmpty(ids);
            return ids;
        }

        private static Club Mine(CareerSession s) => s.World.Clubs.Single(c => c.Id == s.Club);

        private static bool Keeper(CareerSession s, int id) => s.World.Players.Single(p => p.Id == id).Positions[0] == Position.Goalkeeper;

        private static int Outfielder(CareerSession s, SquadGroup g, int nth = 0) => Group(s, g).Where(id => !Keeper(s, id)).ElementAt(nth);

        [Fact]
        public void The_squad_shows_the_ais_pick_then_everyone_else_by_level()
        {
            CareerSession s = New();
            List<SquadRow> rows = CareerHub.SquadRows(s);
            Assert.Equal(Mine(s).Squad.Count, rows.Count);
            SquadSelection ai = CareerSquads.Selection(s.World, s.Club, CareerSession.PerSide, null, s.Squad);
            Assert.Equal(ai.Starters.Select(p => p.Id).OrderBy(x => x), Group(s, SquadGroup.Xi).OrderBy(x => x));
            Assert.Equal(ai.Bench.Select(p => p.Id), Group(s, SquadGroup.Bench));
            List<SquadRow> rest = rows.Where(r => r.Group == SquadGroup.NotPicked).ToList();
            Assert.Equal(rest.OrderByDescending(r => r.Level).ThenBy(r => r.Player).Select(r => r.Player), rest.Select(r => r.Player));
            SquadRow first = rows[0];
            Player p = s.World.Players.Single(x => x.Id == first.Player);
            Assert.Equal(CareerSquads.Level(p), first.Level, 9);
            Assert.True(first.Level >= 1 && first.Level <= 20);
            Assert.StartsWith($"{p.FirstName[0]}. {p.LastName}, {p.Age} · ", first.Text);
            Assert.Contains(FormattableString.Invariant($"{first.Level:F1}"), first.Text);
        }

        [Fact]
        public void A_swap_changes_two_places_and_one_out_is_refused()
        {
            CareerSession s = New();
            Assert.Empty(Mine(s).Picks);
            int starter = Outfielder(s, SquadGroup.Xi, 3), sub = Group(s, SquadGroup.Bench)[0], spare = Outfielder(s, SquadGroup.NotPicked);
            Assert.Contains(sub, CareerHub.SwapCandidates(s, starter).Select(r => r.Player));
            Assert.DoesNotContain(Group(s, SquadGroup.Xi)[4], CareerHub.SwapCandidates(s, starter).Select(r => r.Player));
            Assert.True(s.Swap(starter, sub));
            Assert.Contains(sub, Group(s, SquadGroup.Xi));
            Assert.Contains(starter, Group(s, SquadGroup.Bench));
            Assert.Equal(11 + Squads.BenchOf(CareerSession.PerSide), Mine(s).Picks.Count);

            Player hurt = s.World.Players.Single(p => p.Id == spare);
            hurt.InjuredDays = 10;
            Assert.DoesNotContain(spare, CareerHub.SwapCandidates(s, sub).Select(r => r.Player));
            List<int> before = Mine(s).Picks.ToList();
            Assert.False(s.Swap(sub, spare));
            Assert.Equal(before, Mine(s).Picks);
            Assert.False(s.Swap(sub, -5));

            s.AutoPick();
            Assert.Empty(Mine(s).Picks);
            Assert.Contains(starter, Group(s, SquadGroup.Xi));
        }

        [Fact]
        public void A_keepers_place_in_the_xi_is_a_keepers_and_none_sits_on_the_bench()
        {
            CareerSession s = New(0x1795);
            int keeper = Group(s, SquadGroup.Xi).Single(id => Keeper(s, id));
            int outfielder = Outfielder(s, SquadGroup.Xi), sub = Group(s, SquadGroup.Bench)[0];
            int spareKeeper = Group(s, SquadGroup.NotPicked).First(id => Keeper(s, id));
            Assert.All(Group(s, SquadGroup.Bench), id => Assert.False(Keeper(s, id)));
            Assert.DoesNotContain(spareKeeper, CareerHub.SwapCandidates(s, outfielder).Select(r => r.Player));
            Assert.DoesNotContain(spareKeeper, CareerHub.SwapCandidates(s, sub).Select(r => r.Player));
            Assert.Contains(spareKeeper, CareerHub.SwapCandidates(s, keeper).Select(r => r.Player));
            Assert.False(s.Swap(outfielder, spareKeeper));
            Assert.False(s.Swap(sub, spareKeeper));
            Assert.False(s.Swap(keeper, sub));
            Assert.True(s.Swap(keeper, spareKeeper));
            Assert.Equal(spareKeeper, CareerSquads.Selection(s.World, s.Club, CareerSession.PerSide).Starters[CareerSession.PerSide].Id);
        }

        [Fact]
        public void Your_eleven_are_placed_in_the_slots_by_the_ais_rule()
        {
            CareerSession s = New(0x1791);
            int starter = Group(s, SquadGroup.Xi).First(id => s.World.Players.Single(p => p.Id == id).Positions[0] != Position.Goalkeeper);
            int spare = Group(s, SquadGroup.NotPicked).First(id => s.World.Players.Single(p => p.Id == id).Positions[0] != Position.Goalkeeper);
            Assert.True(s.Swap(starter, spare));
            SquadSelection picked = CareerSquads.Selection(s.World, s.Club, CareerSession.PerSide);
            var eleven = new HashSet<int>(Mine(s).Picks.Take(11));
            Assert.Equal(eleven, new HashSet<int>(picked.Starters.Select(p => p.Id)));

            // The same placement as the AI's rule over those eleven alone.
            var others = new HashSet<int>(s.World.Players.Where(p => p.ClubId == s.Club && !eleven.Contains(p.Id)).Select(p => p.Id));
            SquadSelection rule = CareerSquads.Selection(Without(s.World, s.Club), s.Club, CareerSession.PerSide, others);
            Assert.Equal(rule.Starters.Select(p => p.Id), picked.Starters.Select(p => p.Id));
        }

        // The world as it is, the human's picks set aside.
        private static World Without(World w, int club)
        {
            World copy = WorldCodec.Decode(WorldCodec.Encode(w));
            copy.Clubs.Single(c => c.Id == club).Picks.Clear();
            return copy;
        }

        [Fact]
        public void A_pick_out_is_replaced_in_his_line_from_the_rest_and_the_other_picks_keep_their_places()
        {
            // Codex on #187: a picked defender out took a picked midfielder into defence, a forward
            // into midfield and a reserve forward in, though a fit reserve defender was there. The
            // gap is filled from the rest, in its line, and every other pick keeps his place. Each
            // club plays its manager's formation (#180), so its slots' lines are its formation's.
            int checkedSides = 0;
            for (ulong seed = 0x1793; seed < 0x1793 + 8; seed++)
            {
                CareerSession s = New(seed);
                int n = CareerSession.PerSide;
                SquadSelection ai = CareerSquads.Selection(s.World, s.Club, n);
                CareerSquads.Line[] lines = CareerSquads.SlotLines(n, CareerManagers.FormationOf(s.World, s.Club));
                int[] back = Enumerable.Range(0, n).Where(i => lines[i] == CareerSquads.Line.Defence).ToArray();
                bool Defender(Player p) => p.Positions[0] is Position.CentreBack or Position.FullBack;
                if (back.Length < 2 || !back.All(i => Defender(ai.Starters[i]))) continue;
                Club mine = Mine(s);
                mine.Picks.Clear();
                mine.Picks.AddRange(ai.Starters.Select(p => p.Id));
                mine.Picks.AddRange(ai.Bench.Select(p => p.Id));
                var eleven = new HashSet<int>(ai.Starters.Select(p => p.Id));
                if (!s.World.Players.Any(p => p.ClubId == s.Club && !eleven.Contains(p.Id) && Defender(p))) continue;

                Player outDefender = ai.Starters[back[1]];
                SquadSelection picked = CareerSquads.Selection(s.World, s.Club, n, new HashSet<int> { outDefender.Id });
                List<Player> newcomers = picked.Starters.Where(p => !eleven.Contains(p.Id)).ToList();
                Assert.Single(newcomers);
                Assert.True(Defender(newcomers[0]), $"seed {seed:x}: {newcomers[0].Positions[0]} came in for a defender");
                foreach (int i in Enumerable.Range(0, n).Where(i => lines[i] != CareerSquads.Line.Defence)) Assert.Equal(ai.Starters[i].Id, picked.Starters[i].Id);
                Assert.Equal(ai.Starters[n].Id, picked.Starters[n].Id);
                var backLine = new HashSet<int>(back.Select(i => picked.Starters[i].Id));
                Assert.Subset(backLine, new HashSet<int>(back.Select(i => ai.Starters[i].Id).Where(id => id != outDefender.Id)));
                checkedSides++;
            }

            Assert.True(checkedSides >= 3, $"{checkedSides} sides checked");
        }

        [Fact]
        public void A_pick_out_by_kick_off_is_replaced_and_the_hub_says_so()
        {
            CareerSession s = New(0x1792);
            int starter = Outfielder(s, SquadGroup.Xi, 5), sub = Group(s, SquadGroup.Bench)[1];
            Assert.True(s.Swap(starter, sub));
            Assert.Equal("", CareerHub.PickLine(s));
            Player p = s.World.Players.Single(x => x.Id == sub);
            p.LeagueBan = 1;
            Dictionary<int, int> lineup = s.LineupFor(s.Next()!);
            Assert.DoesNotContain(sub, lineup.Values);
            Assert.Equal(2 * (CareerSession.PerSide + 1) + 2 * Squads.BenchOf(CareerSession.PerSide), lineup.Count);
            Assert.Equal($"Picked but out: {p.FirstName[0]}. {p.LastName}; the AI's pick takes his place.", CareerHub.PickLine(s));
        }

        [Fact]
        public void Your_picks_play_your_fixture_whether_played_watched_or_simulated()
        {
            foreach (CareerRole role in new[] { CareerRole.PlayerManager, CareerRole.ManagerOnly })
            {
                CareerSession s = New(0x1793, role);
                int starter = Outfielder(s, SquadGroup.Xi, 2), spare = Outfielder(s, SquadGroup.NotPicked);
                Assert.True(s.Swap(starter, spare));
                var eleven = new HashSet<int>(Mine(s).Picks.Take(11));
                Assert.Contains(spare, eleven);
                Dictionary<int, int> lineup = s.LineupFor(s.Next()!);
                var mine = Enumerable.Range(0, CareerSession.PerSide).Select(Squads.HomeOutfielder).Append(Squads.HomeKeeper(CareerSession.PerSide)).Select(id => lineup[id]);
                Assert.Equal(eleven, new HashSet<int>(mine));
            }

            // Simulated, the quick-sim's side and lines are the fixture's lineup, his picks among it.
            CareerSession q = New(0x1794);
            int a = Outfielder(q, SquadGroup.Xi, 1), b = Outfielder(q, SquadGroup.NotPicked);
            Assert.True(q.Swap(a, b));
            Fixture next = q.Next()!;
            var picks = new HashSet<int>(Mine(q).Picks.Take(11));
            MatchdayKind kind = next.Competition == CompetitionId.Cup ? MatchdayKind.Cup : MatchdayKind.League;
            Dictionary<int, int> map = CareerSquads.Lineup(q.World, next.Home, next.Away, CareerSession.PerSide,
                Availability.OutOf(q.World, next.Home, kind), Availability.OutOf(q.World, next.Away, kind), q.Squad);
            bool home = next.Home == q.Club;
            var side = (home ? CareerQuickSim.HomeIds(CareerSession.PerSide) : CareerQuickSim.AwayIds(CareerSession.PerSide)).Select(id => map[id]);
            Assert.Equal(picks, new HashSet<int>(side));
            q.Simulate();
            MatchResult r = q.Season.Results[next.Id];
            Assert.Equal(11, r.Players.Count(l => l.Team == (home ? r.HomeTeam : r.AwayTeam)));
        }
    }
}
