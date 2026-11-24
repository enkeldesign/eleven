using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #203: the transfer market. A footballer's value and his club's asking price; a window's
    /// buying for a club's weakest place from its division or below, and an over-budget club's
    /// listing; each fee on both ledgers; the balance and the budget holding; the human's club out
    /// of it; money and footballers conserved; the mid-season window's matchday; and the world's
    /// bytes keeping it, a format-10 world read with none.
    /// </summary>
    public class TransferTests
    {
        private static readonly TransferRules Rules = TransferRules.Reference;
        private static readonly CompetitionRules Competition = CompetitionRules.Reference;

        private static (World World, Season Season) New(ulong seed = 0x203)
        {
            World w = WorldGenerator.Generate(seed);
            Season s = SeasonEngine.Start(w, 1, seed, SeasonEngine.FirstRanking(w), Competition);
            return (w, s);
        }

        private static Player At(int level, int age, int contract, int potential = 0)
        {
            var p = new Player { Age = age, ContractYears = contract, Potential = Math.Max(potential, level) };
            p.Positions.Add(Position.CentralMidfield);
            for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = level;
            return p;
        }

        private static Player Set(Player p, int level, int age = 25, int contract = 3)
        {
            for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = level;
            p.Potential = Math.Max(p.Potential, level);
            p.Age = age;
            p.ContractYears = contract;
            return p;
        }

        private static List<Player> SquadOf(World w, Club c) => w.Players.Where(p => p.ClubId == c.Id).ToList();

        // A buyer: rich, its centre backs its weakest place, and room for one more.
        private static (Club Buyer, Player Gone) Buyer(World w, Club buyer)
        {
            foreach (Player p in SquadOf(w, buyer)) Set(p, p.Positions[0] == Position.CentreBack ? 9 : 14);
            buyer.Balance = 100_000;
            buyer.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 1_000_000;
            Player gone = SquadOf(w, buyer).First(p => p.Positions[0] == Position.Striker);
            w.Players.Remove(gone);
            buyer.Squad.Remove(gone.Id);
            return (buyer, gone);
        }

        private static void Poor(World w, params Club[] but)
        {
            foreach (Club c in w.Clubs.Where(c => !but.Contains(c))) c.Balance = 0;
        }

        // Every other centre back no upgrade on the buyer's, at 9: only those the test sets are.
        private static void NoOtherBacks(World w, Club buyer)
        {
            foreach (Player p in w.Players.Where(p => p.ClubId != buyer.Id && p.Positions[0] == Position.CentreBack)) Set(p, 9);
        }

        [Fact]
        public void A_value_rises_with_level_and_division_peaks_in_the_mid_twenties_and_falls_with_the_contracts_years_left()
        {
            long V(Player p, int division = 2) => Transfers.Value(p, division);
            Assert.Equal((long)Math.Round(Rules.ValueBase), V(At(10, 25, 3)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Math.Exp(Rules.ValueSlope * 4)), V(At(14, 25, 3)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.DivisionValue[0]), V(At(10, 25, 3), 1));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.DivisionValue[2]), V(At(10, 25, 3), 3));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.From28), V(At(10, 29, 3)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.From30), V(At(10, 31, 3)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.From32), V(At(10, 33, 3)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Math.Min(1 + Rules.YoungRoom * 4, Rules.YoungMost)), V(At(10, 19, 3, 14)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.YoungMost), V(At(10, 19, 3, 20)));
            Assert.Equal((long)Math.Round(Rules.ValueBase), V(At(10, 19, 3, 10)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.OneYearLeft), V(At(10, 25, 1)));
            Assert.Equal((long)Math.Round(Rules.ValueBase * Rules.TwoYearsLeft), V(At(10, 25, 2)));
            Assert.Equal(V(At(10, 25, 3)), V(At(10, 25, 5)));

            // Asked: his value, more if his club wants him (#201).
            (World w, _) = New();
            Club club = w.Clubs[0];
            Player good = Set(SquadOf(w, club)[5], 16), poor = Set(SquadOf(w, club)[6], 4, 29);
            Assert.True(Contracts.Wanted(w, club, good, club.Division) && !Contracts.Wanted(w, club, poor, club.Division));
            Assert.Equal((long)Math.Round(Transfers.Value(good, club.Division) * Rules.WantedMarkup), Transfers.Asking(w, club, good));
            Assert.Equal(Transfers.Value(poor, club.Division), Transfers.Asking(w, club, poor));
        }

        [Fact]
        public void A_clubs_eleven_is_its_best_keeper_and_its_best_at_each_place_of_its_shape()
        {
            (World w, _) = New();
            Club club = w.Clubs[4];
            List<Player> xi = Transfers.Eleven(w, club);
            Assert.Equal(11, xi.Count);
            Assert.Single(xi, p => p.Positions[0] == Position.Goalkeeper);
            Player bestKeeper = SquadOf(w, club).Where(p => p.Positions[0] == Position.Goalkeeper).OrderByDescending(Development.Level).ThenBy(p => p.Id).First();
            Assert.Contains(bestKeeper, xi);

            // A centre back made the best in the club is in it; one made the worst isn't.
            List<Player> backs = SquadOf(w, club).Where(p => p.Positions[0] == Position.CentreBack).ToList();
            Set(backs[0], 20);
            Set(backs[1], 1);
            xi = Transfers.Eleven(w, club);
            Assert.Contains(backs[0], xi);
            Assert.DoesNotContain(backs[1], xi);
        }

        [Fact]
        public void A_window_buys_for_a_clubs_weakest_place_from_its_division_or_below_and_the_fee_moves_on_both_ledgers()
        {
            (World w, _) = New();
            (Club buyer, _) = Buyer(w, w.Clubs[2]);
            Club seller = w.Clubs.First(c => c.Division == 2);
            Poor(w, buyer);
            NoOtherBacks(w, buyer);
            Player star = Set(SquadOf(w, seller).First(p => p.Positions[0] == Position.CentreBack), 20);
            long fee = Transfers.Asking(w, seller, star);
            long buyerBefore = buyer.Balance, sellerBefore = seller.Balance;

            List<Transfer> made = Transfers.Window(w, 2, TransferWindow.PreSeason);
            Transfer t = Assert.Single(made);
            Assert.Equal((star.Id, seller.Id, buyer.Id, fee, 2, TransferWindow.PreSeason), (t.PlayerId, t.From, t.To, t.Fee, t.Season, t.Window));
            Assert.Equal((buyer.Id, Finance.Wage(star, buyer.Division), Contracts.RenewYears(star.Age)), (star.ClubId, star.Wage, star.ContractYears));
            Assert.Equal((buyerBefore - fee, sellerBefore + fee), (buyer.Balance, seller.Balance));
            Assert.Equal((-fee, fee), (buyer.Season[LedgerLine.Transfers], seller.Season[LedgerLine.Transfers]));
            Assert.Contains(star.Id, buyer.Squad);
            Assert.DoesNotContain(star.Id, seller.Squad);
            Assert.Equal(made, w.LastTransfers);

            // Transfers aren't revenue.
            Assert.Equal(0, seller.Season.Revenue);
        }

        [Fact]
        public void A_club_doesnt_buy_from_a_higher_division_unless_it_lists_him_over_its_budget()
        {
            (World w, _) = New();
            (Club buyer, _) = Buyer(w, w.Clubs.First(c => c.Division == 2));
            Club seller = w.Clubs[0];
            Poor(w, buyer);
            NoOtherBacks(w, buyer);

            // The top division's club: its other centre backs better, so this one is outside its XI.
            foreach (Player p in SquadOf(w, seller).Where(p => p.Positions[0] == Position.CentreBack)) Set(p, 15);
            Player back = Set(SquadOf(w, seller).First(p => p.Positions[0] == Position.CentreBack), 13);
            back.Wage = 500;
            Assert.DoesNotContain(back, Transfers.Eleven(w, seller));

            // Within its budget, the top division's club sells nothing down.
            seller.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 1_000_000;
            Assert.Empty(Transfers.Window(w, 2, TransferWindow.PreSeason));

            // Over it, it lists its highest-paid outside its XI, at his value.
            seller.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 1;
            long value = Transfers.Value(back, seller.Division);
            Transfer t = Assert.Single(Transfers.Window(w, 2, TransferWindow.PreSeason));
            Assert.Equal((back.Id, seller.Id, buyer.Id, value), (t.PlayerId, t.From, t.To, t.Fee));
        }

        [Fact]
        public void A_buyer_without_the_balance_or_the_budget_or_the_room_buys_none_and_the_humans_club_is_out_of_it()
        {
            (World w, _) = New();
            (Club buyer, Player gone) = Buyer(w, w.Clubs[2]);
            Club seller = w.Clubs.First(c => c.Division == 2);
            Poor(w, buyer);
            NoOtherBacks(w, buyer);
            Player star = Set(SquadOf(w, seller).First(p => p.Positions[0] == Position.CentreBack), 20);
            long fee = Transfers.Asking(w, seller, star);

            buyer.Balance = 2 * fee - 1;
            Assert.Empty(Transfers.Window(w, 2, TransferWindow.PreSeason));
            buyer.Balance = 100_000;
            buyer.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 1;
            Assert.Empty(Transfers.Window(w, 2, TransferWindow.PreSeason));
            buyer.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 1_000_000;
            Assert.Empty(Transfers.Window(w, 2, TransferWindow.PreSeason, null, new[] { buyer.Id }));
            Assert.Empty(Transfers.Window(w, 2, TransferWindow.PreSeason, null, new[] { seller.Id }));

            // A full squad has room only by letting an unwanted footballer of the place go in his contract's last season.
            w.Players.Add(gone);
            buyer.Squad.Add(gone.Id);
            foreach (Player p in SquadOf(w, buyer)) p.ContractYears = 2;
            Assert.Empty(Transfers.Window(w, 2, TransferWindow.PreSeason));
            Assert.Equal(buyer.Id, gone.ClubId);
            Assert.Equal(seller.Id, star.ClubId);
        }

        [Fact]
        public void A_window_conserves_the_money_and_the_footballers_and_keeps_every_squad_within_its_bounds()
        {
            (World w, _) = New();
            foreach (Club c in w.Clubs)
            {
                c.Balance *= 4;
                // Room: two outfielders out of contract gone (the squad lists its keepers first).
                foreach (Player p in SquadOf(w, c).Where(p => p.ContractYears == 1 && p.Positions[0] != Position.Goalkeeper).Take(2).ToList())
                {
                    w.Players.Remove(p);
                    c.Squad.Remove(p.Id);
                }
            }

            long money = w.Clubs.Sum(c => c.Balance);
            HashSet<int> footballers = w.Players.Select(p => p.Id).ToHashSet();
            List<Transfer> made = Transfers.Window(w, 2, TransferWindow.PreSeason);
            Assert.NotEmpty(made);
            Assert.Equal(money, w.Clubs.Sum(c => c.Balance));
            Assert.Equal(0, w.Clubs.Sum(c => c.Season[LedgerLine.Transfers]));
            Assert.Equal(footballers, w.Players.Select(p => p.Id).ToHashSet());
            foreach (Club c in w.Clubs)
            {
                Assert.InRange(SquadOf(w, c).Count, Rules.SquadMinimum, WorldGenerator.SquadSize);
                Assert.Equal(c.Squad.OrderBy(x => x), SquadOf(w, c).Select(p => p.Id).OrderBy(x => x));
                Assert.True(SquadOf(w, c).Count(p => p.Positions[0] == Position.Goalkeeper) >= 2, c.ShortName);
            }

            Assert.All(made, t => Assert.True(w.Clubs.Single(c => c.Id == t.To).Division <= w.Clubs.Single(c => c.Id == t.From).Division || t.Fee == Transfers.Value(w.Players.Single(p => p.Id == t.PlayerId), w.Clubs.Single(c => c.Id == t.From).Division)));
        }

        [Fact]
        public void The_mid_season_window_follows_the_league_round_halfway_through_once()
        {
            (World w, Season s) = New();
            var after = new List<int>();
            List<Matchday> calendar = SeasonCalendar.Of(Competition);
            while (!SeasonEngine.Finished(s, Competition))
            {
                foreach (Fixture f in SeasonEngine.Next(s, Competition))
                {
                    (int home, int away) = f.Competition == CompetitionId.Cup ? (f.Home < f.Away ? (1, 0) : (0, 1)) : (1, 1);
                    SeasonEngine.Record(s, Competition, f.Id, new MatchResult(1, 2, home, away, 180, 360, Array.Empty<ResultGoal>(), 0));
                }

                SeasonEngine.Advance(s, Competition);
                if (Transfers.MidSeasonAfter(s, Competition)) after.Add(s.Matchday - 1);
            }

            int day = Assert.Single(after);
            Assert.Equal((MatchdayKind.League, (int)Math.Round(Competition.LeagueRounds * Rules.MidSeason)), (calendar[day].Kind, calendar[day].Round));
        }

        [Fact]
        public void The_worlds_bytes_keep_the_transfers_line_and_the_windows_transfers_and_a_format_10_world_reads_with_none()
        {
            (World w, _) = New();
            Assert.Equal(11, WorldCodec.FormatVersion);
            byte[] plain = WorldCodec.Encode(w);

            // Format 10 is format 11 less its section: each club's two transfers lines, then the window's transfers.
            var ten = new CanonicalWriter(plain.Length);
            ten.WriteBytes(plain[..4]);
            ten.WriteInt32(10);
            ten.WriteBytes(plain[8..^Formats.Transfers(w)]);
            World old = WorldCodec.Decode(ten.ToArray());
            Assert.All(old.Clubs, c => Assert.Equal((0L, 0L), (c.Season[LedgerLine.Transfers], c.LastSeason[LedgerLine.Transfers])));
            Assert.Empty(old.LastTransfers);
            Assert.Equal(plain, WorldCodec.Encode(old));

            w.Clubs[3].Season.Amounts[(int)LedgerLine.Transfers] = -870;
            w.Clubs[9].LastSeason.Amounts[(int)LedgerLine.Transfers] = 1_240;
            Player p = w.Players[50];
            w.LastTransfers.Add(new Transfer { PlayerId = p.Id, From = 7, To = 3, Season = 2, Fee = 870, Window = TransferWindow.MidSeason, FirstName = p.FirstName, LastName = p.LastName });
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            Assert.Equal(w.Clubs.Select(c => (c.Season[LedgerLine.Transfers], c.LastSeason[LedgerLine.Transfers])), back.Clubs.Select(c => (c.Season[LedgerLine.Transfers], c.LastSeason[LedgerLine.Transfers])));
            Assert.Equal(w.LastTransfers.Select(t => (t.PlayerId, t.From, t.To, t.Season, t.Fee, t.Window, t.FirstName, t.LastName)), back.LastTransfers.Select(t => (t.PlayerId, t.From, t.To, t.Season, t.Fee, t.Window, t.FirstName, t.LastName)));
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));
        }
    }
}
