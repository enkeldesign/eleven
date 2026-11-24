using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #201: contracts. The generator's lengths by age and a youth's first; each turn running
    /// them down, renewing those a club wants within its wage budget and releasing the rest; the
    /// free agents signed by clubs short of a position, or retiring after a season unsigned;
    /// every footballer accounted for; and the world's bytes keeping them, a format-9 world read
    /// by the rules.
    /// </summary>
    public class ContractTests
    {
        private static readonly ContractRules Rules = ContractRules.Reference;
        private static readonly CompetitionRules Competition = CompetitionRules.Reference;

        private static (World World, Season Season) New(ulong seed = 0x201)
        {
            World w = WorldGenerator.Generate(seed);
            Season s = SeasonEngine.Start(w, 1, seed, SeasonEngine.FirstRanking(w), Competition);
            return (w, s);
        }

        private static Dictionary<int, int> Divisions(World w) => w.Clubs.ToDictionary(c => c.Id, c => c.Division);

        private static Player Set(Player p, int age, int level, int contract)
        {
            p.Age = age;
            for (int i = 0; i < p.Attributes.Length; i++) p.Attributes[i] = level;
            p.Potential = Math.Max(p.Potential, level);
            p.ContractYears = contract;
            return p;
        }

        // Every club's budget ample: last season's revenue large.
        private static void Rich(World w)
        {
            foreach (Club c in w.Clubs) c.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 1_000_000;
        }

        [Fact]
        public void The_generators_contracts_run_one_to_five_seasons_shorter_for_the_older_and_a_youths_is_his_first()
        {
            (World w, _) = New();
            foreach (Player p in w.Players)
            {
                Assert.Equal(Contracts.FirstContract(w.Seed, p.Id, p.Age), p.ContractYears);
                (int low, int high) = p.Age < Rules.PrimeFrom ? (2, Rules.YoungLongest) : p.Age < Rules.OldFrom ? (1, Rules.PrimeLongest) : (1, Rules.OldLongest);
                Assert.InRange(p.ContractYears, low, high);
                Assert.Equal(0, p.FreeSince);
            }

            Assert.Equal(Enumerable.Range(1, 5), w.Players.Select(p => p.ContractYears).Distinct().OrderBy(x => x));
            Assert.Equal(new[] { 4, 3, 2, 1 }, new[] { 20, 25, 30, 33 }.Select(a => Contracts.RenewYears(a)));

            // A youth signs his first contract.
            foreach (Player p in w.Players.Where(p => p.ClubId == w.Clubs[0].Id).Take(3).ToList()) p.Age = 50;
            Development.Rollover(w, 2);
            List<Player> youth = w.Players.Where(p => p.Joined == 2).ToList();
            Assert.NotEmpty(youth);
            Assert.All(youth, y => Assert.Equal(Rules.YouthYears, y.ContractYears));
        }

        [Fact]
        public void At_the_turn_each_contract_runs_down_and_an_ending_one_is_renewed_if_wanted_or_released()
        {
            (World w, _) = New();
            Rich(w);
            Club club = w.Clubs[0];
            List<Player> squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
            Player good = Set(squad[5], 25, 15, 1), poor = Set(squad[6], 29, 5, 1), other = Set(squad[7], 27, 13, 3);
            good.Wage = 1;
            ContractTurn turn = Contracts.Turn(w, 2, Divisions(w));

            Assert.Equal((club.Id, Contracts.RenewYears(25), Finance.Wage(good, 1)), (good.ClubId, good.ContractYears, good.Wage));
            Assert.Contains(good, turn.Renewed);
            Assert.Equal((0, 2), (poor.ClubId, poor.FreeSince));
            Assert.DoesNotContain(poor.Id, club.Squad);
            Assert.Contains(poor, Contracts.FreeAgents(w));
            Assert.Contains(poor, turn.Released);
            Assert.Contains(w.LastMoves, m => m.PlayerId == poor.Id && m.From == club.Id && m.To == 0 && m.LastName == poor.LastName);
            Assert.Equal(2, other.ContractYears);
            Assert.All(w.Players.Where(p => p.ClubId != 0), p => Assert.InRange(p.ContractYears, 1, 5));
        }

        [Fact]
        public void A_club_wants_its_prospects_and_those_above_their_replacement_and_its_veterans_only_at_its_level()
        {
            (World w, _) = New();
            Club club = w.Clubs[0];
            Manager m = w.Managers.Single(x => x.Id == club.ManagerId);
            double development = (m.Competences[ManagerCompetences.Ids.IndexOf("development")] - 10.5) / 9.5;
            double replacement = WorldGenerator.DivisionLevel[0] - DevelopmentRules.Reference.YouthBelow + development;
            Assert.Equal(replacement, Contracts.ReplacementLevel(w, club, 1), 12);
            Assert.True(Contracts.ReplacementLevel(w, club, 3) < replacement);

            Player p = w.Players.First(x => x.ClubId == club.Id && x.Positions[0] != Position.Goalkeeper);
            Assert.True(Contracts.Wanted(w, club, Set(p, 26, (int)Math.Ceiling(replacement + Rules.KeepMargin + 0.01), 1), 1));
            Assert.False(Contracts.Wanted(w, club, Set(p, 26, (int)Math.Floor(replacement + Rules.KeepMargin - 0.01), 1), 1));
            Assert.True(Contracts.Wanted(w, club, Set(p, 26, (int)Math.Floor(replacement + Rules.KeepMargin - 0.01), 1), 3), "the same footballer, for a lower division");

            // A prospect: young, with room to his potential.
            Set(p, 19, 6, 1);
            p.Potential = 6 + Rules.ProspectRoom;
            Assert.True(Contracts.Wanted(w, club, p, 1));
            p.Potential = 6 + Rules.ProspectRoom - 1;
            Assert.False(Contracts.Wanted(w, club, p, 1));

            // A veteran: only at his division's level.
            Assert.False(Contracts.Wanted(w, club, Set(p, Rules.VeteranFrom, 12, 1), 1));
            Assert.True(Contracts.Wanted(w, club, Set(p, Rules.VeteranFrom, 13, 1), 1));
        }

        [Fact]
        public void The_wage_budget_holds_a_renewal_over_it_releases_him_and_one_for_less_is_kept()
        {
            (World w, _) = New();
            Rich(w);
            Club club = w.Clubs[1];
            club.LastSeason.Amounts[(int)LedgerLine.Broadcasting] = 1;
            Assert.True(Finance.WageBill(w, club) > Finance.WageBudget(w, club));
            List<Player> squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
            Player raise = Set(squad[8], 25, 15, 1), cut = Set(squad[9], 25, 15, 1);
            raise.Wage = 1;
            cut.Wage = 999;
            Contracts.Turn(w, 2, Divisions(w));

            Assert.NotEqual(club.Id, raise.ClubId);
            Assert.Contains(w.LastMoves, m => m.PlayerId == raise.Id && m.From == club.Id && m.To == 0);
            Assert.Equal((club.Id, Finance.Wage(cut, 1)), (cut.ClubId, cut.Wage));
        }

        [Fact]
        public void Free_agents_are_signed_by_clubs_short_of_a_position_within_budget_and_retire_after_a_season_unsigned()
        {
            (World w, _) = New();
            Rich(w);

            // No contract ends at this turn: only these three are free.
            foreach (Player p in w.Players) p.ContractYears = Math.Max(p.ContractYears, 2);
            Club club = w.Clubs.First(c => c.Division == 2);
            List<Player> backs = w.Players.Where(p => p.ClubId == club.Id && p.Positions[0] == Position.CentreBack).Take(2).ToList();
            Player good = Set(backs[0], 26, 14, 0), poor = Set(backs[1], 26, 4, 0);
            Player old = Set(w.Players.First(p => p.ClubId == w.Clubs[30].Id && p.Positions[0] == Position.Striker), 27, 4, 0);
            foreach ((Player p, int since) in new[] { (good, 2), (poor, 2), (old, 1) })
            {
                w.Clubs.Single(c => c.Id == p.ClubId).Squad.Remove(p.Id);
                p.ClubId = 0;
                p.FreeSince = since;
            }

            ContractTurn turn = Contracts.Turn(w, 2, Divisions(w));
            Assert.Equal((club.Id, Finance.Wage(good, 2), Contracts.RenewYears(26), 0), (good.ClubId, good.Wage, good.ContractYears, good.FreeSince));
            Assert.Contains(good.Id, club.Squad);
            Assert.Contains(good, turn.Signed);
            Assert.Contains(w.LastMoves, m => m.PlayerId == good.Id && m.From == 0 && m.To == club.Id);
            Assert.Equal(0, poor.ClubId);

            // At the turn's development, the one unsigned since an earlier season retires; this season's stays free.
            Development.Rollover(w, 2);
            Assert.DoesNotContain(w.Players, p => p.Id == old.Id);
            Assert.Contains(w.LastRetired, r => r.Id == old.Id && r.ClubId == 0);
            Assert.Contains(w.Players, p => p.Id == poor.Id && p.ClubId == 0);
            Assert.All(w.Clubs, c => Assert.Equal(WorldGenerator.SquadSize, w.Players.Count(p => p.ClubId == c.Id)));
        }

        [Fact]
        public void Every_footballer_is_at_one_club_a_free_agent_or_retired_and_every_squad_stays_whole()
        {
            (World w, Season s) = New();
            while (!SeasonEngine.Finished(s, Competition))
            {
                foreach (Fixture f in SeasonEngine.Next(s, Competition))
                {
                    (int home, int away) = f.Competition == CompetitionId.Cup ? (f.Home < f.Away ? (1, 0) : (0, 1)) : (f.Home % 3, f.Away % 2);
                    SeasonEngine.Record(s, Competition, f.Id, new MatchResult(1, 2, home, away, 180, 360, Array.Empty<ResultGoal>(), 0));
                }

                SeasonEngine.Advance(s, Competition);
            }

            HashSet<int> before = w.Players.Select(p => p.Id).ToHashSet();
            SeasonEngine.Rollover(w, s, 0x201, Competition);
            HashSet<int> after = w.Players.Select(p => p.Id).ToHashSet(), retired = w.LastRetired.Select(r => r.Id).ToHashSet();
            HashSet<int> youth = w.Players.Where(p => p.Joined == 2).Select(p => p.Id).ToHashSet();
            Assert.Equal(before.OrderBy(x => x), after.Except(youth).Concat(retired).OrderBy(x => x));
            Assert.Empty(after.Intersect(retired));

            HashSet<int> clubs = w.Clubs.Select(c => c.Id).ToHashSet();
            Assert.All(w.Players, p => Assert.True(p.ClubId == 0 || clubs.Contains(p.ClubId)));
            foreach (Club c in w.Clubs)
            {
                Assert.Equal(WorldGenerator.SquadSize, w.Players.Count(p => p.ClubId == c.Id));
                Assert.Equal(c.Squad.OrderBy(x => x), w.Players.Where(p => p.ClubId == c.Id).Select(p => p.Id).OrderBy(x => x));
            }

            Assert.All(Contracts.FreeAgents(w), p => Assert.Equal(2, p.FreeSince));
            Assert.Contains(w.LastMoves, m => m.To == 0);
        }

        [Fact]
        public void The_worlds_bytes_keep_contracts_free_agents_and_the_turns_moves_and_a_format_9_world_reads_them_by_the_rules()
        {
            (World w, _) = New();
            Assert.Equal(10, WorldCodec.FormatVersion);
            byte[] plain = WorldCodec.Encode(w);

            // Format 9 is format 10 less its section: each footballer's contract and free season, and the turn's moves.
            var nine = new CanonicalWriter(plain.Length);
            nine.WriteBytes(plain[..4]);
            nine.WriteInt32(9);
            nine.WriteBytes(plain[8..^Formats.Contracts(w)]);
            World old = WorldCodec.Decode(nine.ToArray());
            Assert.Equal(w.Players.Select(p => (p.ContractYears, p.FreeSince)), old.Players.Select(p => (p.ContractYears, p.FreeSince)));
            Assert.Empty(old.LastMoves);
            Assert.Equal(plain, WorldCodec.Encode(old));

            Player free = w.Players[40];
            w.Clubs.Single(c => c.Id == free.ClubId).Squad.Remove(free.Id);
            w.LastMoves.Add(new ContractMove { PlayerId = free.Id, From = free.ClubId, To = 0, FirstName = free.FirstName, LastName = free.LastName });
            free.ClubId = 0;
            free.FreeSince = 3;
            w.Players[41].ContractYears = 4;
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            Assert.Equal(w.Players.Select(p => (p.Id, p.ClubId, p.ContractYears, p.FreeSince)), back.Players.Select(p => (p.Id, p.ClubId, p.ContractYears, p.FreeSince)));
            Assert.Equal(w.LastMoves.Select(m => (m.PlayerId, m.From, m.To, m.FirstName, m.LastName)), back.LastMoves.Select(m => (m.PlayerId, m.From, m.To, m.FirstName, m.LastName)));
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));
        }
    }
}
