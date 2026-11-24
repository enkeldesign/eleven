using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #198: a club's money. Wages by level, age and division; broadcasting and wages by the day,
    /// a home match's takings, the season's merit and cup payments, each booked to its ledger line;
    /// the board's wage budget; and the world's bytes keeping it, a format-7 world read by the rules.
    /// </summary>
    public class FinanceTests
    {
        private static readonly FinanceRules Rules = FinanceRules.Reference;

        private static (World World, Season Season) New(ulong seed = 0x198)
        {
            World w = WorldGenerator.Generate(seed);
            Season s = SeasonEngine.Start(w, 1, seed, SeasonEngine.FirstRanking(w), CompetitionRules.Reference);
            return (w, s);
        }

        private static Player At(int level, int age)
        {
            var p = new Player { Age = age };
            p.Positions.Add(Position.CentralMidfield);
            for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = level;
            return p;
        }

        private static int Expected(int level, int age, int division)
        {
            double share = age < 21 ? Rules.YoungWage : age < 24 ? Rules.NearPeakWage : 1;
            return Math.Max(1, (int)Math.Round(Rules.WageBase * Math.Exp(Rules.WageSlope * (level - 10)) * Rules.DivisionWage[division - 1] * share));
        }

        [Fact]
        public void A_wage_is_by_his_level_age_and_his_clubs_division_and_the_generator_pays_everyone()
        {
            Assert.Equal(Expected(10, 26, 2), Finance.Wage(At(10, 26), 2));
            Assert.Equal(4, Finance.Wage(At(10, 26), 2));
            Assert.Equal(Expected(14, 26, 2), Finance.Wage(At(14, 26), 2));
            Assert.Equal(Expected(14, 26, 1), Finance.Wage(At(14, 26), 1));
            Assert.Equal(Expected(14, 26, 3), Finance.Wage(At(14, 26), 3));
            Assert.Equal(Expected(14, 19, 1), Finance.Wage(At(14, 19), 1));
            Assert.Equal(Expected(14, 22, 1), Finance.Wage(At(14, 22), 1));
            Assert.True(Finance.Wage(At(14, 26), 1) > Finance.Wage(At(14, 22), 1) && Finance.Wage(At(14, 22), 1) > Finance.Wage(At(14, 19), 1));
            Assert.Equal(1, Finance.Wage(At(1, 17), 3));

            (World w, _) = New();
            foreach (Club c in w.Clubs)
            {
                Assert.Equal(Finance.StartBalance(c), c.Balance);
                Assert.True(c.Balance > 0);
                Assert.All(w.Players.Where(p => p.ClubId == c.Id), p => Assert.Equal(Finance.Wage(p, c.Division), p.Wage));
            }
        }

        [Fact]
        public void The_days_pay_the_wages_and_bring_the_broadcasting_each_on_its_line()
        {
            (World w, _) = New();
            Dictionary<int, long> before = w.Clubs.ToDictionary(c => c.Id, c => c.Balance);
            Finance.Pass(w, 7);
            foreach (Club c in w.Clubs)
            {
                long wages = (long)Math.Round(Finance.WageBill(w, c) * 7 / 7.0), broadcast = (long)Math.Round(Rules.BroadcastPerDay[c.Division - 1] * 7);
                Assert.Equal((-wages, broadcast), (c.Season[LedgerLine.Wages], c.Season[LedgerLine.Broadcasting]));
                Assert.Equal(before[c.Id] - wages + broadcast, c.Balance);
            }
        }

        [Fact]
        public void A_home_match_brings_its_takings_by_division_and_reputation_and_the_away_side_none()
        {
            (World w, Season s) = New();
            Fixture f = s.Fixtures.First(x => x.Competition != CompetitionId.Cup);
            Club home = w.Clubs.Single(c => c.Id == f.Home), away = w.Clubs.Single(c => c.Id == f.Away);
            Finance.Matchday(w, new[] { f });
            long takings = (long)Math.Round(Rules.MatchdayBase[home.Division - 1] * (1 + Rules.MatchdayPerReputation * (home.Reputation - 10)));
            Assert.Equal(takings, home.Season[LedgerLine.Matchday]);
            Assert.Equal(0, away.Season[LedgerLine.Matchday]);
            Assert.Equal(Finance.StartBalance(home) + takings, home.Balance);
        }

        [Fact]
        public void The_seasons_end_pays_merit_by_place_and_the_cup_by_round_and_its_ledger_becomes_last_seasons()
        {
            (World w, Season s) = New();
            CompetitionRules rules = CompetitionRules.Reference;
            while (!SeasonEngine.Finished(s, rules))
            {
                foreach (Fixture f in SeasonEngine.Next(s, rules))
                {
                    // A league match by the clubs' ids; a cup tie never level, the lower id through.
                    (int home, int away) = f.Competition == CompetitionId.Cup ? (f.Home < f.Away ? (1, 0) : (0, 1)) : (f.Home % 3, f.Away % 2);
                    SeasonEngine.Record(s, rules, f.Id, new MatchResult(1, 2, home, away, 180, 360, Array.Empty<ResultGoal>(), 0));
                }

                SeasonEngine.Advance(s, rules);
            }

            Finance.SeasonEnd(w, s, rules);
            foreach (Club c in w.Clubs)
            {
                List<TableRow> table = SeasonEngine.Table(s, (CompetitionId)c.Division, rules);
                int place = table.FindIndex(r => r.Club == c.Id) + 1;
                long merit = (long)Math.Round(Rules.MeritTop[c.Division - 1] * (13 - place) / 12.0);
                int reached = s.Fixtures.Where(f => f.Competition == CompetitionId.Cup && (f.Home == c.Id || f.Away == c.Id)).Select(f => f.Round).DefaultIfEmpty(0).Max();
                long cup = (long)Math.Round(Rules.CupPerRound * reached + (s.CupWinner == c.Id ? Rules.CupWinner : 0));
                Assert.Equal((merit, cup), (c.LastSeason[LedgerLine.Merit], c.LastSeason[LedgerLine.Cup]));
                Assert.Equal(0, c.Season.Net);
                Assert.Equal(Finance.StartBalance(c) + merit + cup, c.Balance);
            }

            Assert.Contains(w.Clubs, c => c.LastSeason[LedgerLine.Cup] > 0);
        }

        [Fact]
        public void The_boards_wage_budget_is_its_share_of_last_seasons_revenue_a_week()
        {
            (World w, _) = New();
            Club c = w.Clubs[3];
            long first = Finance.WageBudget(w, c);
            Assert.True(first > 0);
            Assert.InRange(first, Finance.WageBill(w, c) / 3, Finance.WageBill(w, c) * 3);
            c.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 3650;
            c.LastSeason.Amounts[(int)LedgerLine.Matchday] = 1000;
            c.LastSeason.Amounts[(int)LedgerLine.Wages] = -2000;
            // Over the season's weeks: the days it pays for, over 7.
            int days = Finance.SeasonDays(CompetitionRules.Reference, SquadRules.Reference);
            Assert.InRange(days, 150, 400);
            Assert.Equal((long)Math.Round(Rules.WageShare * 4650 / (days / 7.0)), Finance.WageBudget(w, c));
        }

        [Fact]
        public void The_worlds_bytes_keep_each_clubs_money_and_each_wage_and_a_format_7_world_reads_them_by_the_rules()
        {
            (World w, _) = New();
            Assert.Equal(8, WorldCodec.FormatVersion);
            byte[] plain = WorldCodec.Encode(w);

            // Format 7 is format 8 less its section: each club's balance and two ledgers, each footballer's wage.
            int section = 4 + w.Clubs.Count * (4 + 2 * Ledger.Lines * 4) + 4 + w.Players.Count * 2;
            var seven = new CanonicalWriter(plain.Length);
            seven.WriteBytes(plain[..4]);
            seven.WriteInt32(7);
            seven.WriteBytes(plain[8..^section]);
            World old = WorldCodec.Decode(seven.ToArray());
            Assert.Equal(w.Clubs.Select(c => c.Balance), old.Clubs.Select(c => c.Balance));
            Assert.Equal(w.Players.Select(p => p.Wage), old.Players.Select(p => p.Wage));
            Assert.Equal(plain, WorldCodec.Encode(old));

            Finance.Pass(w, 10);
            w.Clubs[0].Balance = -350;
            w.Players[4].Wage = 77;
            w.Clubs[2].LastSeason.Amounts[(int)LedgerLine.Merit] = 1200;
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            Assert.Equal(w.Clubs.Select(c => (c.Balance, string.Join(".", c.Season.Amounts), string.Join(".", c.LastSeason.Amounts))), back.Clubs.Select(c => (c.Balance, string.Join(".", c.Season.Amounts), string.Join(".", c.LastSeason.Amounts))));
            Assert.Equal(w.Players.Select(p => p.Wage), back.Players.Select(p => p.Wage));
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));
        }
    }
}
