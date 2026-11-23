using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #172: the result names its footballers (schema 8). In full matches every line's time, cards
    /// and injuries add up to the side's; a substitute's time and his man's make a whole match;
    /// the lines are in the text and the codec; the quick-sim's results name the XI with the
    /// scoreline unchanged; and a match's lineup maps simulation ids to the world's footballers.
    /// </summary>
    public class ResultPlayersTests
    {
        private static readonly World World = WorldGenerator.Generate(0x157);
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        // A full CPU match between two of the world's clubs, with its substitutions seen.
        private static (MatchResult Result, List<(int On, int Off)> Subs) Play(int home, int away, ulong seed)
        {
            var sim = new Simulator(Config, CareerSquads.Match(Config, World, World.Clubs[home].Id, World.Clubs[away].Id, 10, seed));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            var subs = new List<(int, int)>();
            while (recorder.Result == null && sim.Tick < 8L * Config.HalfTicks + 600L * Config.TickRate)
            {
                commands.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                events.Clear();
                sim.Step(commands, events);
                recorder.Observe(Config, sim.PeekState(), events);
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Substituted) subs.Add((e.Subject, e.Other));
                }
            }

            Assert.NotNull(recorder.Result);
            return (recorder.Result!, subs);
        }

        [Fact]
        public void A_full_matchs_lines_add_up_to_its_sides()
        {
            int checkedSubs = 0;
            foreach ((int home, int away, ulong seed) in new[] { (0, 30, 0x1720UL), (5, 20, 0x1721UL), (12, 35, 0x1722UL) })
            {
                (MatchResult r, List<(int On, int Off)> subs) = Play(home, away, seed);
                Assert.True(r.Players.Count >= 22, $"{r.Players.Count} lines");
                Assert.Equal(r.Players.Count, r.Players.Select(p => p.Player).Distinct().Count());
                foreach ((int team, int cautions, int off, int injuries) in new[] { (r.HomeTeam, r.HomeCautions, r.HomeSendingsOff, r.HomeInjuries), (r.AwayTeam, r.AwayCautions, r.AwaySendingsOff, r.AwayInjuries) })
                {
                    List<ResultPlayer> side = r.Players.Where(p => p.Team == team).ToList();
                    Assert.True(side.Count >= 11);
                    Assert.Equal(cautions, side.Sum(p => p.Cautions));
                    Assert.Equal(off, side.Count(p => p.SentOff));
                    Assert.InRange(side.Count(p => p.Injury > 0), Math.Min(1, injuries), injuries);
                    Assert.All(side, p => Assert.InRange(p.Seconds, 0, r.PlayedSeconds + 1e-9));
                }

                // A footballer with nothing happen to him, and never replaced, plays it all.
                var touched = new HashSet<int>(subs.SelectMany(x => new[] { x.On, x.Off }));
                foreach (ResultPlayer p in r.Players.Where(p => !touched.Contains(p.Player) && !p.SentOff && p.Injury < 2))
                    Assert.Equal(r.PlayedSeconds, p.Seconds, 6);

                // A substitute and the man he replaced make a whole match between them.
                foreach ((int on, int off) in subs)
                {
                    ResultPlayer a = r.Players.Single(p => p.Player == on), b = r.Players.Single(p => p.Player == off);
                    if (b.SentOff || b.Injury == 2 || touched.Count(x => x == on) > 1) continue;
                    Assert.Equal(r.PlayedSeconds, a.Seconds + b.Seconds, 6);
                    checkedSubs++;
                }
            }

            Assert.True(checkedSubs > 0, "no substitution checked");
        }

        [Fact]
        public void The_lines_are_in_the_text_and_the_codec_at_schema_8()
        {
            (MatchResult r, _) = Play(1, 25, 0x1723);
            Assert.Equal(8, MatchResult.SchemaVersion);
            string text = r.ToText();
            Assert.StartsWith("eleven-result/8\n", text);
            Assert.Equal(r.Players.Count, text.Split('\n').Count(l => l.StartsWith("player=", StringComparison.Ordinal)));
            var w = new CanonicalWriter(1 << 14);
            MatchResultCodec.Write(w, r);
            MatchResult back = MatchResultCodec.Read(new CanonicalReader(w.ToArray()));
            Assert.Equal(text, back.ToText());
            Assert.Equal(r.Players.Select(p => (p.Player, p.Team, p.Seconds, p.Cautions, p.SentOff, p.Injury)), back.Players.Select(p => (p.Player, p.Team, p.Seconds, p.Cautions, p.SentOff, p.Injury)));
        }

        [Fact]
        public void A_schema_7_result_from_a_save_before_lines_reads_back_with_none()
        {
            // A save written before #172 holds schema-7 results: schema 8's fields less the lines.
            // Those bytes are schema 8's with the version changed and the empty lines' count dropped.
            MatchResult plain = QuickSim.Play(CareerQuickSimCalibration.Config(), CareerQuickSim.Side(World, World.Clubs[3].Id, 10), CareerQuickSim.Side(World, World.Clubs[20].Id, 10), 0x1724, model: QuickSimModel.ElevenASide);
            Assert.Empty(plain.Players);
            var w8 = new CanonicalWriter(1 << 12);
            MatchResultCodec.Write(w8, plain);
            byte[] eight = w8.ToArray();
            var w7 = new CanonicalWriter(1 << 12);
            w7.WriteInt32(7);
            w7.WriteBytes(eight[4..^4]);

            MatchResult back = MatchResultCodec.Read(new CanonicalReader(w7.ToArray()));
            Assert.Equal(plain.ToText(), back.ToText());
            Assert.Empty(back.Players);

            // Any other schema is still refused.
            var w6 = new CanonicalWriter(1 << 12);
            w6.WriteInt32(6);
            w6.WriteBytes(eight[4..^4]);
            Assert.Throws<FormatException>(() => MatchResultCodec.Read(new CanonicalReader(w6.ToArray())));
        }

        [Fact]
        public void A_neutral_grounds_model_keeps_every_rate_but_home_advantage()
        {
            QuickSimModel m = QuickSimModel.ElevenASide, n = CareerSeasons.WithoutHome(m);
            Assert.Equal(0, n.Home);
            Assert.Equal((m.BaseRate, m.CapabilityWeight, m.Rho, m.FoulsPerSide, m.CautionsPerSide, m.SendingsOffPerSide, m.ShootoutConversion),
                (n.BaseRate, n.CapabilityWeight, n.Rho, n.FoulsPerSide, n.CautionsPerSide, n.SendingsOffPerSide, n.ShootoutConversion));
            Assert.Equal((m.MinorInjuriesPerSide, m.SeriousInjuriesPerSide), (n.MinorInjuriesPerSide, n.SeriousInjuriesPerSide));
            for (int t = 0; t < QuickSimModel.Tiers; t++) Assert.Equal(m.TierRating(t), n.TierRating(t));
        }

        [Fact]
        public void A_matchs_lineup_maps_simulation_ids_to_the_worlds_footballers()
        {
            int home = World.Clubs[2].Id, away = World.Clubs[29].Id;
            Dictionary<int, int> lineup = CareerSquads.Lineup(World, home, away, 10);
            SquadSelection h = CareerSquads.Selection(World, home, 10), a = CareerSquads.Selection(World, away, 10);
            Assert.Equal(h.Starters.Count + h.Bench.Count + a.Starters.Count + a.Bench.Count, lineup.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(h.Starters[i].Id, lineup[Squads.HomeOutfielder(i)]);
                Assert.Equal(a.Starters[i].Id, lineup[Squads.AwayOutfielder(10, i)]);
            }

            Assert.Equal(h.Starters[10].Id, lineup[Squads.HomeKeeper(10)]);
            Assert.Equal(a.Starters[10].Id, lineup[Squads.AwayKeeper(10)]);
            Assert.Equal(h.Bench[0].Id, lineup[Squads.HomeBench(10, 0)]);
            Assert.Equal(a.Bench[0].Id, lineup[Squads.AwayBench(10, 0)]);
        }

        [Fact]
        public void A_quick_sims_result_names_the_xis_with_its_scoreline_unchanged()
        {
            SimConfig c = CareerQuickSimCalibration.Config();
            int lines = 0, cautions = 0, injuries = 0;
            for (ulong seed = 0; seed < 60; seed++)
            {
                int home = World.Clubs[(int)(seed % 36)].Id, away = World.Clubs[(int)((seed * 7 + 5) % 36)].Id;
                if (home == away) continue;
                MatchResult r = CareerQuickSim.Play(c, World, home, away, 10, seed);
                MatchResult plain = QuickSim.Play(c, CareerQuickSim.Side(World, home, 10), CareerQuickSim.Side(World, away, 10), seed, model: QuickSimModel.ElevenASide);
                Assert.Equal((plain.HomeGoals, plain.AwayGoals, plain.HomeCautions, plain.AwayCautions), (r.HomeGoals, r.AwayGoals, r.HomeCautions, r.AwayCautions));
                Assert.Equal(plain.Goals.Select(g => (g.Team, g.Half, g.ClockSeconds)), r.Goals.Select(g => (g.Team, g.Half, g.ClockSeconds)));

                Dictionary<int, int> lineup = CareerSquads.Lineup(World, home, away, 10);
                Assert.Equal(22, r.Players.Count);
                Assert.All(r.Players, p => Assert.True(lineup.ContainsKey(p.Player), $"line for {p.Player}"));
                Assert.Equal(r.HomeCautions, r.Players.Where(p => p.Team == r.HomeTeam).Sum(p => p.Cautions));
                Assert.Equal(r.AwaySendingsOff, r.Players.Count(p => p.Team == r.AwayTeam && p.SentOff));
                // Two injuries can fall to one footballer, whose line keeps the worse.
                Assert.InRange(r.Players.Count(p => p.Team == r.HomeTeam && p.Injury > 0), Math.Min(1, r.HomeInjuries), r.HomeInjuries);
                injuries += r.HomeInjuries + r.AwayInjuries;
                Assert.All(r.Players, p => Assert.Equal(r.PlayedSeconds, p.Seconds, 9));
                lines += r.Players.Count;
                cautions += r.HomeCautions + r.AwayCautions;
            }

            Assert.True(lines > 1000 && cautions > 10, $"{lines} lines, {cautions} cautions");
            Assert.True(injuries > 0, "no injury drawn");
        }

        [Fact]
        public void The_quick_sims_cards_fall_to_distinct_footballers_so_its_lines_total_its_counts()
        {
            // Codex on #184: a side's sendings-off went to footballers drawn with replacement, so two
            // could fall to one and the lines undercount; two cautions could fall to one footballer
            // never sent off. At card-heavy rates, every card goes to a footballer without one, the
            // keeper never; beyond the outfielders none is named.
            SimConfig c = CareerQuickSimCalibration.Config();
            QuickSimModel e = QuickSimModel.ElevenASide;
            double[] tiers = Enumerable.Range(0, QuickSimModel.Tiers).Select(t => e.TierRating(t)).ToArray();
            QuickSimModel Cards(double cautions, double off) => new QuickSimModel(e.BaseRate, e.Home, tiers, e.CapabilityWeight, e.Rho,
                e.FoulsPerSide, cautions, off, e.ShootoutConversion, e.MinorInjuriesPerSide, e.SeriousInjuriesPerSide);
            int[] home = Enumerable.Range(100, 11).ToArray(), away = Enumerable.Range(200, 11).ToArray();
            QuickSide side = CareerQuickSim.Side(World, World.Clubs[4].Id, 10);

            int several = 0;
            for (ulong seed = 0; seed < 200; seed++)
            {
                MatchResult r = QuickSim.Play(c, side, side, seed, model: Cards(3, 2), homePlayers: home, awayPlayers: away);
                foreach ((int team, int cautions, int off) in new[] { (r.HomeTeam, r.HomeCautions, r.HomeSendingsOff), (r.AwayTeam, r.AwayCautions, r.AwaySendingsOff) })
                {
                    List<ResultPlayer> mine = r.Players.Where(p => p.Team == team).ToList();
                    Assert.Equal(Math.Min(off, 10), mine.Count(p => p.SentOff));
                    Assert.Equal(Math.Min(cautions, 10), mine.Sum(p => p.Cautions));
                    Assert.All(mine, p => Assert.InRange(p.Cautions, 0, 1));
                    ResultPlayer keeper = mine.Single(p => p.Player == (team == r.HomeTeam ? home[10] : away[10]));
                    Assert.Equal((0, false), (keeper.Cautions, keeper.SentOff));
                    if (off >= 2) several++;
                }
            }

            Assert.True(several > 50, $"{several} sides with two or more sendings-off");

            // More sendings-off than outfielders: each of them is named once, and no more.
            MatchResult flood = QuickSim.Play(c, side, side, 5, model: Cards(40, 40), homePlayers: home, awayPlayers: away);
            Assert.True(flood.HomeSendingsOff > 10 && flood.HomeCautions > 10, $"{flood.HomeSendingsOff} sendings-off, {flood.HomeCautions} cautions");
            Assert.Equal(10, flood.Players.Count(p => p.Team == flood.HomeTeam && p.SentOff));
            Assert.Equal(10, flood.Players.Count(p => p.Team == flood.HomeTeam && p.Cautions == 1));
        }
    }
}
