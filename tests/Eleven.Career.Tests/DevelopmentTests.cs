using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #194: development and ageing. The young grow toward their potential by age, their manager's
    /// competences and their starts, never past it and none from the peak, every fraction kept;
    /// at the season's turn every footballer ages, the older decline by kind, keepers later, the
    /// oldest retire and youth fill each squad again with fresh ids; the same career gives the
    /// same footballers; and the world's bytes keep it, a format-5 world read with none.
    /// </summary>
    public class DevelopmentTests
    {
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;
        private static readonly DevelopmentRules Dev = DevelopmentRules.Reference;

        private static (World World, Season Season) New(ulong seed = 0x194)
        {
            World w = WorldGenerator.Generate(seed);
            Season s = SeasonEngine.Start(w, 1, seed, SeasonEngine.FirstRanking(w), Rules);
            return (w, s);
        }

        private static Manager Coach(int competence)
        {
            var m = new Manager();
            for (int i = 0; i < m.Competences.Length; i++) m.Competences[i] = competence;
            return m;
        }

        // An outfielder of the given age with room below his potential.
        private static Player Young(int age, int level = 8, int potential = 18)
        {
            var p = new Player { Id = 1, Age = age, Potential = potential };
            p.Positions.Add(Position.CentralMidfield);
            for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = level;
            return p;
        }

        [Fact]
        public void Growth_is_by_age_his_managers_competences_and_his_starts()
        {
            Manager middle = Coach(10), best = Coach(20), worst = Coach(1);
            double young = Development.SeasonGrowth(Young(17), middle, 0);
            Assert.Equal(Dev.GrowthYoung * (1 + Dev.CompetenceSpread * (10 - 10.5) / 9.5), young, 9);

            // Fastest to 19, falling to none at the peak.
            Assert.Equal(young, Development.SeasonGrowth(Young(16), middle, 0), 9);
            Assert.Equal(young, Development.SeasonGrowth(Young(19), middle, 0), 9);
            Assert.Equal(young * 3 / 5, Development.SeasonGrowth(Young(21), middle, 0), 9);
            Assert.Equal(0, Development.SeasonGrowth(Young(24), middle, 0));
            Assert.Equal(0, Development.SeasonGrowth(Young(31), middle, 0));

            // ±30 % by the manager, +50 % for one who starts every match.
            Assert.Equal(Dev.GrowthYoung * 1.3, Development.SeasonGrowth(Young(17), best, 0), 9);
            Assert.Equal(Dev.GrowthYoung * 0.7, Development.SeasonGrowth(Young(17), worst, 0), 9);
            Assert.Equal(young * 1.5, Development.SeasonGrowth(Young(17), middle, 1), 9);
            Assert.Equal(young * 1.25, Development.SeasonGrowth(Young(17), middle, 0.5), 9);
        }

        [Fact]
        public void No_growth_past_his_potential_and_none_from_the_peak()
        {
            (World w, Season s) = New();
            Player at = w.Players.First(p => p.Age < 20 && p.Positions[0] != Position.Goalkeeper);
            Player peaked = w.Players.First(p => p.Age >= Dev.PeakFrom && p.Age < Dev.DeclineFrom);
            at.Potential = (int)Math.Ceiling(Development.Level(at));
            int[] before = at.Attributes.ToArray(), peakedBefore = peaked.Attributes.ToArray();
            for (int day = 0; day < 40; day++) Development.Matchday(w, s, Array.Empty<int>(), 30);
            Assert.True(Development.Level(at) <= at.Potential + 1e-9, $"{Development.Level(at)} past {at.Potential}");
            Assert.Equal(peakedBefore, peaked.Attributes);
            Assert.True(at.Attributes.Sum() > before.Sum(), "he grew to his potential first");
        }

        [Fact]
        public void A_young_footballers_growth_is_kept_to_the_fraction_and_spent_by_his_positions_profile()
        {
            (World w, Season s) = New();
            Player p = w.Players.First(x => x.Age <= 18 && x.Positions[0] == Position.Striker);
            p.Potential = 20;
            Manager m = w.Managers.Single(x => x.ClubId == p.ClubId);
            double perSeason = Development.SeasonGrowth(p, m, 0) * (PlayerAttributes.Count - 3);
            int[] before = p.Attributes.ToArray();
            const int days = 30;
            for (int day = 0; day < days; day++) Development.Matchday(w, s, Array.Empty<int>(), days);

            // One season's growth exactly: the points spent and the fraction kept.
            int spent = p.Attributes.Sum() - before.Sum();
            Assert.Equal(perSeason, spent + p.Progress, 9);
            Assert.InRange(p.Progress, 0, 1);
            Assert.Equal(spent, p.SeasonPoints);

            // A striker's points go most to his shooting; none to keeping.
            int shot = new[] { "shootingPower", "finishing", "curve" }.Sum(a => p.Get(a) - before[PlayerAttributes.Index(a)]);
            int defend = new[] { "tackling", "marking", "positioning" }.Sum(a => p.Get(a) - before[PlayerAttributes.Index(a)]);
            int keep = new[] { "keeperReflexes", "keeperHandling", "keeperPositioning" }.Sum(a => p.Get(a) - before[PlayerAttributes.Index(a)]);
            Assert.True(shot > defend, $"shooting +{shot}, defending +{defend}");
            Assert.Equal(0, keep);
        }

        [Fact]
        public void A_matchday_counts_each_starters_start()
        {
            (World w, Season s) = New();
            Player a = w.Players[3], b = w.Players[4];
            Development.Matchday(w, s, new[] { a.Id }, 30);
            Development.Matchday(w, s, new[] { a.Id, b.Id }, 30);
            Assert.Equal((2, 1), (a.Starts, b.Starts));
        }

        [Fact]
        public void At_the_seasons_turn_every_footballer_ages_and_the_older_decline_by_kind_keepers_later()
        {
            (World w, _) = New();
            var rules = new DevelopmentRules { PhysicalDecline = 4, RetireFrom = 99, KeeperRetireFrom = 99 };
            Player Set(Player p, int age)
            {
                p.Age = age;
                for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = 12;
                return p;
            }

            Player First(Position position, int skip = 0) => w.Players.Where(x => x.Positions[0] == position).Skip(skip).First();
            Player fresh = Set(First(Position.Striker), 24), thirty = Set(First(Position.CentreBack), 30), thirtyTwo = Set(First(Position.FullBack), 32), thirtyFour = Set(First(Position.CentralMidfield), 34);
            Player keeper = Set(First(Position.Goalkeeper), 33), olderKeeper = Set(First(Position.Goalkeeper, 1), 37);
            int[] ages = w.Players.Select(p => p.Age).ToArray();
            Development.Rollover(w, 2, rules);

            Assert.Equal(ages.Select(a => a + 1), w.Players.Select(p => p.Age));
            int Lost(Player p, string a) => 12 - p.Get(a);
            Assert.All(PlayerAttributes.Ids, a => Assert.Equal(0, Lost(fresh, a)));
            Assert.Equal((4, 0, 0), (Lost(thirty, "topSpeed"), Lost(thirty, "finishing"), Lost(thirty, "decisions")));
            Assert.Equal((4, 2, 0), (Lost(thirtyTwo, "stamina"), Lost(thirtyTwo, "shortPassing"), Lost(thirtyTwo, "anticipation")));
            Assert.Equal((4, 2, 1), (Lost(thirtyFour, "acceleration"), Lost(thirtyFour, "tackling"), Lost(thirtyFour, "vision")));

            // A keeper three years later: at 33 as an outfielder at 30, at 37 as one at 34.
            Assert.Equal((4, 0), (Lost(keeper, "keeperReflexes"), Lost(keeper, "keeperHandling")));
            Assert.Equal((4, 2), (Lost(olderKeeper, "keeperReflexes"), Lost(olderKeeper, "keeperHandling")));
        }

        [Fact]
        public void The_oldest_retire_and_youth_fill_each_squad_again_with_fresh_ids()
        {
            (World w, _) = New();
            int next = w.NextPlayerId;
            Assert.Equal(w.Players.Max(p => p.Id) + 1, next);
            Club club = w.Clubs[2];
            Player keeper = w.Players.First(p => p.ClubId == club.Id && p.Positions[0] == Position.Goalkeeper);
            Player striker = w.Players.First(p => p.ClubId == club.Id && p.Positions[0] == Position.Striker);
            keeper.Age = 45;
            striker.Age = 45;
            club.Picks.Add(striker.Id);
            HashSet<int> before = w.Players.Select(p => p.Id).ToHashSet();
            Development.Rollover(w, 2);

            Assert.DoesNotContain(w.Players, p => p.Id == keeper.Id || p.Id == striker.Id);
            Assert.DoesNotContain(striker.Id, club.Squad);
            Assert.DoesNotContain(striker.Id, club.Picks);
            Assert.Contains(w.LastRetired, r => r.Id == striker.Id && r.ClubId == club.Id && r.Age == 46);
            Assert.All(w.Clubs, c => Assert.Equal(WorldGenerator.SquadSize, w.Players.Count(p => p.ClubId == c.Id)));
            Assert.All(w.Clubs, c => Assert.Equal(c.Squad.OrderBy(x => x), w.Players.Where(p => p.ClubId == c.Id).Select(p => p.Id).OrderBy(x => x)));
            Assert.Equal(WorldGenerator.KeepersPerSquad, w.Players.Count(p => p.ClubId == club.Id && p.Positions[0] == Position.Goalkeeper));

            // The youth: new ids from the next, never a retired one's, aged 16–18, joined this season.
            List<Player> youth = w.Players.Where(p => !before.Contains(p.Id)).ToList();
            Assert.True(youth.Count >= 2);
            Assert.All(youth, p => Assert.True(p.Id >= next, $"{p.Id} below {next}"));
            Assert.All(youth, p => Assert.InRange(p.Age, Dev.YouthMin, Dev.YouthMax));
            Assert.All(youth, p => Assert.Equal(2, p.Joined));
            Assert.Equal(w.Players.Count, w.Players.Select(p => p.Id).Distinct().Count());
            Assert.Equal(w.Players.Max(p => p.Id) + 1, w.NextPlayerId);
        }

        [Fact]
        public void A_retired_pick_in_the_xi_sends_his_clubs_picks_back_to_the_ais_and_one_on_the_bench_just_leaves_it()
        {
            // Codex on #195: a club's picks are its XI's 11, then its bench, so dropping a retired
            // XI pick would move the first bench footballer into the XI, an outfielder in goal for a keeper.
            (World w, _) = New();
            foreach (Player p in w.Players)
            {
                if (p.Age >= 30) p.Age = 25;
            }

            Club a = w.Clubs[1], b = w.Clubs[2];
            List<int> Picked(Club c) => w.Players.Where(p => p.ClubId == c.Id).OrderBy(p => p.Positions[0] == Position.Goalkeeper ? 0 : 1).ThenBy(p => p.Id).Take(18).Select(p => p.Id).ToList();
            a.Picks.AddRange(Picked(a));
            b.Picks.AddRange(Picked(b));
            w.Players.Single(p => p.Id == a.Picks[0]).Age = 45;
            int benched = b.Picks[15];
            w.Players.Single(p => p.Id == benched).Age = 45;
            List<int> before = b.Picks.ToList();
            Development.Rollover(w, 2);

            Assert.Empty(a.Picks);
            Assert.Equal(before.Where(id => id != benched), b.Picks);
            Assert.Equal(before.Take(11), b.Picks.Take(11));
        }

        [Fact]
        public void Retirement_is_more_likely_each_year_from_the_age_it_starts()
        {
            (World w, _) = New();
            foreach (Player p in w.Players) p.Age = p.Positions[0] == Position.Goalkeeper ? 1 : Dev.RetireFrom - 1;
            int outfield = w.Players.Count(p => p.Positions[0] != Position.Goalkeeper);
            Development.Rollover(w, 2);
            double first = (double)w.LastRetired.Count / outfield;
            Assert.InRange(first, Dev.RetireChance - 0.06, Dev.RetireChance + 0.06);
            Assert.All(w.LastRetired, r => Assert.Equal(Dev.RetireFrom, r.Age));
        }

        [Fact]
        public void The_same_career_twice_gives_the_same_footballers()
        {
            byte[] Run()
            {
                (World w, Season s) = New(0x1941);
                for (int season = 1; season <= 2; season++)
                {
                    for (int day = 0; day < 30; day++) Development.Matchday(w, s, w.Players.Where((p, i) => (i + day) % 3 == 0).Select(p => p.Id).ToArray(), 30);
                    Development.Rollover(w, season + 1);
                }

                return WorldCodec.Encode(w);
            }

            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void The_worlds_bytes_keep_development_and_a_format_5_world_reads_with_none()
        {
            (World w, Season s) = New();
            Assert.Equal(9, WorldCodec.FormatVersion);
            byte[] plain = WorldCodec.Encode(w);

            // Format 5 is format 7 with no development or training, less those sections: the next id and three empty counts (#196).
            var five = new CanonicalWriter(plain.Length);
            five.WriteBytes(plain[..4]);
            five.WriteInt32(5);
            five.WriteBytes(plain[8..^(16 + Formats.From8(w))]);
            World old = WorldCodec.Decode(five.ToArray());
            Assert.All(old.Players, p => Assert.Equal((0.0, 0, 0, 0, 0), (p.Progress, p.Starts, p.SeasonPoints, p.LastSeasonPoints, p.Joined)));
            Assert.Equal(w.Players.Max(p => p.Id) + 1, old.NextPlayerId);
            Assert.Equal(plain, WorldCodec.Encode(old));

            for (int day = 0; day < 10; day++) Development.Matchday(w, s, w.Players.Take(40).Select(p => p.Id).ToArray(), 30);
            w.Players[5].Age = 45;
            Development.Rollover(w, 2);
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            string Of(Player p) => $"{p.Id},{p.Age},{string.Join(".", p.Attributes)},{p.Progress:R},{p.Starts},{p.SeasonPoints},{p.LastSeasonPoints},{p.Joined}";
            Assert.Equal(w.Players.Select(Of), back.Players.Select(Of));
            Assert.Equal(w.NextPlayerId, back.NextPlayerId);
            Assert.Equal(w.LastRetired.Select(r => (r.Id, r.ClubId, r.Age, r.FirstName, r.LastName, r.Position)), back.LastRetired.Select(r => (r.Id, r.ClubId, r.Age, r.FirstName, r.LastName, r.Position)));
            Assert.NotEmpty(back.LastRetired);
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));
        }
    }
}
