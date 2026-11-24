using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #176: who is out. An injury's layoff by UEFA's bands; a ban for a sending-off and for
    /// cautions, SvFF's way, served only in its own competition; the second yellow's two cautions
    /// left uncounted; the days passing; the season's turn; and the world's bytes keeping it all,
    /// a world from before them read with nobody out.
    /// </summary>
    public class AvailabilityTests
    {
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;
        private static readonly SquadRules Squad = SquadRules.Reference;

        private static (World World, Season Season) New(ulong seed = 0x176)
        {
            World w = WorldGenerator.Generate(seed);
            Season s = SeasonEngine.Start(w, 1, seed, SeasonEngine.FirstRanking(w), Rules);
            return (w, s);
        }

        // A fixture's result naming one footballer of its home club, simulation id 1.
        private static (MatchResult Result, Dictionary<int, int> Lineup) One(Fixture f, Player p, int cautions = 0, bool sentOff = false, int injury = 0) =>
            (new MatchResult(1, 2, 0, 0, 180, 360, Array.Empty<ResultGoal>(), 0).WithPlayers(new[] { new ResultPlayer(1, 1, 360, cautions, sentOff, injury) }),
             new Dictionary<int, int> { [1] = p.Id });

        private static Player Of(World w, int club) => w.Players.First(p => p.ClubId == club && p.Positions[0] != Position.Goalkeeper);

        private static Fixture Cup(int home, int away) => new Fixture { Id = 900_001, Season = 1, Competition = CompetitionId.Cup, Round = 1, Home = home, Away = away };

        [Fact]
        public void An_injurys_layoff_falls_in_uefas_bands_by_its_level()
        {
            Assert.Equal(0, Availability.Layoff(7, 1, 1, 0, Squad));
            foreach ((int level, double[] shares) in new[] { (1, Squad.MinorShares), (2, Squad.SeriousShares) })
            {
                var bands = new int[4];
                const int n = 4000;
                for (int i = 0; i < n; i++)
                {
                    int days = Availability.Layoff(0x176, 1000 + i, 5000 + i, level, Squad);
                    int band = Array.FindIndex(Squad.BandDays, b => days >= b[0] && days <= b[1]);
                    Assert.True(band >= 0, $"{days} days is in no band");
                    bands[band]++;
                    Assert.Equal(days, Availability.Layoff(0x176, 1000 + i, 5000 + i, level, Squad));
                }

                for (int b = 0; b < 4; b++) Assert.InRange(bands[b] / (double)n, shares[b] - 0.03, shares[b] + 0.03);
            }
        }

        [Fact]
        public void An_injury_keeps_him_out_for_its_days_and_a_second_never_shortens_it()
        {
            (World w, Season s) = New();
            Fixture f = SeasonEngine.Next(s, Rules)[0];
            Player p = Of(w, f.Home);
            (MatchResult r, Dictionary<int, int> lineup) = One(f, p, injury: 2);
            Availability.Apply(w, s, f, r, lineup, Squad);
            int days = Availability.Layoff(s.Seed, f.Id, p.Id, 2, Squad);
            Assert.True(days >= 4);
            Assert.Equal(days, p.InjuredDays);
            Assert.True(Availability.Out(p, MatchdayKind.League));
            Assert.True(Availability.Out(p, MatchdayKind.Cup));
            Assert.Contains(p.Id, Availability.OutOf(w, f.Home, MatchdayKind.League));

            p.InjuredDays = 100;
            Availability.Apply(w, s, f, r, lineup, Squad);
            Assert.Equal(100, p.InjuredDays);
        }

        [Fact]
        public void A_sending_off_bans_him_from_his_clubs_next_match_in_that_competition()
        {
            (World w, Season s) = New();
            Fixture league = SeasonEngine.Next(s, Rules)[0];
            Player p = Of(w, league.Home);
            (MatchResult r, Dictionary<int, int> lineup) = One(league, p, sentOff: true);
            Availability.Apply(w, s, league, r, lineup, Squad);
            Assert.Equal((1, 0), (p.LeagueBan, p.CupBan));
            Assert.True(Availability.Out(p, MatchdayKind.League));
            Assert.False(Availability.Out(p, MatchdayKind.Cup));

            Fixture cup = Cup(league.Home, league.Away);
            (r, lineup) = One(cup, p, sentOff: true);
            Availability.Apply(w, s, cup, r, lineup, Squad);
            Assert.Equal((1, 1), (p.LeagueBan, p.CupBan));
        }

        [Fact]
        public void The_third_caution_bans_him_and_then_every_second_one()
        {
            (World w, Season s) = New();
            Fixture f = SeasonEngine.Next(s, Rules)[0];
            Player p = Of(w, f.Home);
            var bans = new List<int>();
            for (int i = 1; i <= 9; i++)
            {
                (MatchResult r, Dictionary<int, int> lineup) = One(f, p, cautions: 1);
                Availability.Apply(w, s, f, r, lineup, Squad);
                bans.Add(p.LeagueBan);
            }

            // Banned at the 3rd, 5th, 7th and 9th, never served here.
            Assert.Equal(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4 }, bans);
            Assert.Equal(9, p.LeagueCautions);
            Assert.Equal(0, p.CupCautions);
        }

        [Fact]
        public void A_second_yellows_two_cautions_arent_counted_but_a_yellow_before_a_red_is()
        {
            (World w, Season s) = New();
            Fixture f = SeasonEngine.Next(s, Rules)[0];
            Player p = Of(w, f.Home);
            (MatchResult r, Dictionary<int, int> lineup) = One(f, p, cautions: 2, sentOff: true);
            Availability.Apply(w, s, f, r, lineup, Squad);
            Assert.Equal((0, 1), (p.LeagueCautions, p.LeagueBan));

            Player q = w.Players.Where(x => x.ClubId == f.Home && x.Id != p.Id && x.Positions[0] != Position.Goalkeeper).First();
            (r, lineup) = One(f, q, cautions: 1, sentOff: true);
            Availability.Apply(w, s, f, r, lineup, Squad);
            Assert.Equal((1, 1), (q.LeagueCautions, q.LeagueBan));
        }

        [Fact]
        public void A_ban_is_served_only_in_its_competitions_matches_of_his_club()
        {
            (World w, Season s) = New();
            List<Fixture> day = SeasonEngine.Next(s, Rules);
            Fixture f = day[0];
            Player banned = Of(w, f.Home), cupOnly = w.Players.First(x => x.ClubId == f.Away && x.Positions[0] != Position.Goalkeeper);
            Player hurt = w.Players.First(x => x.ClubId == day[1].Home && x.Positions[0] != Position.Goalkeeper);
            banned.LeagueBan = 2;
            cupOnly.CupBan = 1;
            hurt.InjuredDays = 10;

            Availability.Serve(w, day, MatchdayKind.League, Squad);
            Assert.Equal(1, banned.LeagueBan);
            Assert.Equal(1, cupOnly.CupBan);
            Assert.Equal(10, hurt.InjuredDays);

            // A club with no match that matchday serves nothing.
            Availability.Serve(w, day.Where(x => x.Home != f.Home && x.Away != f.Home).ToList(), MatchdayKind.League, Squad);
            Assert.Equal(1, banned.LeagueBan);

            Availability.Serve(w, new List<Fixture> { Cup(f.Away, f.Home) }, MatchdayKind.Cup, Squad);
            Assert.Equal((1, 0), (banned.LeagueBan, cupOnly.CupBan));
        }

        [Fact]
        public void The_days_pass_for_everyone_injured_whether_his_club_plays_or_not()
        {
            (World w, _) = New();
            w.Players[0].InjuredDays = 10;
            w.Players[500].InjuredDays = 3;
            Availability.Pass(w, Rules.LeagueGapDays, Squad);
            Assert.Equal((10 - Rules.LeagueGapDays, 0), (w.Players[0].InjuredDays, w.Players[500].InjuredDays));
        }

        [Fact]
        public void A_layoff_within_the_week_misses_no_matchday_and_a_longer_one_does()
        {
            // A matchday's injuries count from its own day: the days to the next pass before it.
            (World w, Season s) = New();
            List<Fixture> day = SeasonEngine.Next(s, Rules);
            Fixture f = day[0];
            Player knock = Of(w, f.Home), strain = w.Players.First(x => x.ClubId == f.Away && x.Positions[0] != Position.Goalkeeper);
            Availability.Serve(w, day, MatchdayKind.League, Squad);
            int gap = Availability.GapAfter(Rules, 0);
            knock.InjuredDays = gap;
            strain.InjuredDays = gap + 1;
            Availability.Pass(w, gap, Squad);
            Assert.False(Availability.Out(knock, MatchdayKind.League));
            Assert.True(Availability.Out(strain, MatchdayKind.League));
        }

        [Fact]
        public void At_the_seasons_turn_cautions_start_again_bans_carry_and_the_off_season_heals()
        {
            (World w, Season s) = New();
            Player p = w.Players[0], q = w.Players[1];
            p.LeagueCautions = 2;
            p.CupCautions = 1;
            p.LeagueBan = 1;
            q.InjuredDays = Squad.OffSeasonDays + 5;
            Availability.NewSeason(w, Squad);
            Assert.Equal((0, 0, 1), (p.LeagueCautions, p.CupCautions, p.LeagueBan));
            Assert.Equal(5, q.InjuredDays);
        }

        [Fact]
        public void Rollover_turns_the_squads_season_too()
        {
            (World w, Season s) = New();
            while (!SeasonEngine.Finished(s, Rules))
            {
                foreach (Fixture f in SeasonEngine.Next(s, Rules))
                {
                    MatchResult r = f.Competition == CompetitionId.Cup
                        ? new MatchResult(1, 2, 1, 0, 180, 360, Array.Empty<ResultGoal>(), 0)
                        : new MatchResult(1, 2, 0, 0, 180, 360, Array.Empty<ResultGoal>(), 0);
                    SeasonEngine.Record(s, Rules, f.Id, r);
                }

                SeasonEngine.Advance(s, Rules);
            }

            w.Players[0].LeagueCautions = 2;
            w.Players[1].InjuredDays = Squad.OffSeasonDays + 1;
            SeasonEngine.Rollover(w, s, 0x176, Rules, Squad);
            Assert.Equal(0, w.Players[0].LeagueCautions);
            Assert.Equal(1, w.Players[1].InjuredDays);
        }

        [Fact]
        public void The_worlds_bytes_keep_a_managers_philosophy_and_a_format_3_world_reads_it_unset()
        {
            World w = WorldGenerator.Generate(0x180);
            Assert.Equal(4, WorldCodec.FormatVersion);
            Assert.All(w.Managers, m => Assert.True(m.Pressing >= 1 && m.Depth >= 1));
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            Assert.Equal(w.Managers.Select(m => (m.Pressing, m.Depth)), back.Managers.Select(m => (m.Pressing, m.Depth)));

            // Format 3 is format 4 with no philosophy, less that section's empty count: read unset.
            foreach (Manager m in w.Managers) m.Pressing = m.Depth = 0;
            byte[] plain = WorldCodec.Encode(w);
            var three = new Eleven.Simulation.Encoding.CanonicalWriter(plain.Length);
            three.WriteBytes(plain[..4]);
            three.WriteInt32(3);
            three.WriteBytes(plain[8..^4]);
            World old = WorldCodec.Decode(three.ToArray());
            Assert.All(old.Managers, m => Assert.Equal((0, 0), (m.Pressing, m.Depth)));
            Assert.Equal(plain, WorldCodec.Encode(old));

            w.Managers[0].Pressing = 21;
            Assert.Throws<FormatException>(() => WorldCodec.Decode(WorldCodec.Encode(w)));
        }

        [Fact]
        public void The_worlds_bytes_keep_a_clubs_picks_and_a_format_2_world_reads_with_none()
        {
            World w = WorldGenerator.Generate(0x179);
            foreach (Manager m in w.Managers) m.Pressing = m.Depth = 0;
            byte[] plain = WorldCodec.Encode(w);

            // Format 2 is today's with no club's picks and no philosophy, less those sections' empty counts.
            var two = new Eleven.Simulation.Encoding.CanonicalWriter(plain.Length);
            two.WriteBytes(plain[..4]);
            two.WriteInt32(2);
            two.WriteBytes(plain[8..^8]);
            Assert.Equal(plain, WorldCodec.Encode(WorldCodec.Decode(two.ToArray())));

            Club c = w.Clubs[5];
            c.Picks.AddRange(c.Squad.Take(20));
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            Assert.Equal(c.Picks, back.Clubs[5].Picks);
            Assert.All(back.Clubs.Where(x => x.Id != c.Id), x => Assert.Empty(x.Picks));
            c.Picks[0] = -1;
            Assert.Throws<FormatException>(() => WorldCodec.Decode(WorldCodec.Encode(w)));
        }

        [Fact]
        public void The_worlds_bytes_keep_the_bans_and_a_world_from_before_them_reads_with_none()
        {
            World w = WorldGenerator.Generate(0x176);
            foreach (Manager m in w.Managers) m.Pressing = m.Depth = 0;
            byte[] plain = WorldCodec.Encode(w);
            // Format 1 is today's with nobody banned or cautioned, no picks and no philosophy, less those sections' empty counts.
            var one = new CanonicalWriter(plain.Length);
            one.WriteBytes(plain[..4]);
            one.WriteInt32(1);
            one.WriteBytes(plain[8..^12]);
            World old = WorldCodec.Decode(one.ToArray());
            Assert.Equal(plain, WorldCodec.Encode(old));

            w.Players[3].LeagueBan = 2;
            w.Players[3].CupCautions = 1;
            w.Players[7].CupBan = 1;
            w.Players[7].LeagueCautions = 4;
            w.Players[9].InjuredDays = 12;
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            Assert.Equal((2, 0, 0, 1), (back.Players[3].LeagueBan, back.Players[3].CupBan, back.Players[3].LeagueCautions, back.Players[3].CupCautions));
            Assert.Equal((0, 1, 4, 0), (back.Players[7].LeagueBan, back.Players[7].CupBan, back.Players[7].LeagueCautions, back.Players[7].CupCautions));
            Assert.Equal(12, back.Players[9].InjuredDays);
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));
        }
    }
}
