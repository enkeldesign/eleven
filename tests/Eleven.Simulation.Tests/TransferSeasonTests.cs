using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #203: the transfer market in a career. A season and its turn trade in both windows with
    /// the money and footballers conserved and the human's club out of it; the same career twice
    /// gives the same transfers; the hub lists the window's; and the measurement trades in every
    /// window, money flowing down and footballers up.
    /// </summary>
    public class TransferSeasonTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static CareerSession New(ulong seed, int club = 0)
        {
            World w = CareerSession.NewWorld(seed);
            return CareerSession.Start(w, seed, w.Clubs[club].Id, CareerRole.ManagerOnly, Config);
        }

        [Fact]
        public void A_season_and_its_turn_trade_with_money_and_footballers_conserved_and_the_humans_club_out_of_it()
        {
            CareerSession s = New(0x203, 2);
            HashSet<int> mine = s.World.Players.Where(p => p.ClubId == s.Club).Select(p => p.Id).ToHashSet();
            var windows = new List<Transfer>();
            while (!s.SeasonOver)
            {
                s.Simulate();
                if (s.World.LastTransfers.Count > 0 && s.World.LastTransfers[0].Window == TransferWindow.MidSeason && !windows.Any(t => t.Window == TransferWindow.MidSeason)) windows.AddRange(s.World.LastTransfers);
            }

            s.NextSeason();
            windows.AddRange(s.World.LastTransfers.Where(t => t.Window == TransferWindow.PreSeason));
            Assert.Contains(windows, t => t.Window == TransferWindow.PreSeason);
            Assert.All(windows, t => Assert.True(t.From != s.Club && t.To != s.Club));
            Assert.Equal(0, s.World.Clubs.Sum(c => c.LastSeason[LedgerLine.Transfers] + c.Season[LedgerLine.Transfers]));
            Assert.All(s.World.Clubs, c => Assert.Equal(WorldGenerator.SquadSize, s.World.Players.Count(p => p.ClubId == c.Id)));
            Assert.All(mine.Where(id => s.World.Players.Any(p => p.Id == id) && !s.World.LastMoves.Any(m => m.PlayerId == id)), id => Assert.Equal(s.Club, s.World.Players.Single(p => p.Id == id).ClubId));
        }

        [Fact]
        public void The_same_career_twice_gives_the_same_transfers()
        {
            string Run()
            {
                CareerSession s = New(0x2031);
                while (!s.SeasonOver) s.Simulate();
                s.NextSeason();
                return string.Join(";", s.World.LastTransfers.Select(t => $"{t.PlayerId}.{t.From}.{t.To}.{t.Fee}")) + "|" + string.Join(";", s.World.Clubs.Select(c => c.Balance));
            }

            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void The_hub_lists_the_windows_transfers_the_dearest_first()
        {
            CareerSession s = New(0x2032);
            Assert.Equal("", CareerHub.TransferLine(s));
            Club a = s.World.Clubs[1], b = s.World.Clubs[20], c = s.World.Clubs[30];
            s.World.LastTransfers.Add(new Transfer { PlayerId = 1, From = b.Id, To = a.Id, Fee = 380, FirstName = "Ann", LastName = "Ek" });
            s.World.LastTransfers.Add(new Transfer { PlayerId = 2, From = c.Id, To = b.Id, Fee = 1_240, FirstName = "Bo", LastName = "Al" });
            Assert.Equal($"Transfers: B. Al, {c.ShortName} to {b.ShortName}, 1,240k; A. Ek, {b.ShortName} to {a.ShortName}, 380k.", CareerHub.TransferLine(s));
        }

        [Fact]
        public void The_measurement_trades_in_every_window_money_down_and_footballers_up()
        {
            CareerTransfers.Measurement m = CareerTransfers.Measure(0x203, 3);
            Assert.Equal(6, m.Windows.Count);
            Assert.Equal(0, m.FeesNet);
            Assert.All(m.Windows.Where(v => v.Window == TransferWindow.PreSeason), v => Assert.True(v.Count.Cast<int>().Sum() > 0, $"season {v.Season}'s pre-season window"));
            Assert.Contains(m.Windows, v => v.Window == TransferWindow.MidSeason && v.Count.Cast<int>().Sum() > 0);
            foreach (CareerTransfers.WindowView v in m.Windows.Where(v => v.Count.Cast<int>().Sum() > 0)) Assert.InRange(v.FeeOverValue, 1.0, Math.Max(1.0, TransferRules.Reference.WantedMarkup));

            // Footballers up: more bought from a lower division than sold to one.
            int up = 0, down = 0;
            foreach (CareerTransfers.WindowView v in m.Windows)
            {
                for (int from = 0; from < 3; from++)
                {
                    for (int to = 0; to < 3; to++)
                    {
                        if (to < from) up += v.Count[from, to];
                        else if (to > from) down += v.Count[from, to];
                    }
                }
            }

            Assert.True(up > down, $"{up} up, {down} down");
            Assert.InRange(m.TopBoughtFromBelow, 0.01, 0.5);
            Assert.Contains("Transfers", CareerTransfers.Report(0x203, 1));
        }
    }
}
