#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>
    /// Contracts' knobs (#201), all technical references, not tuned feel. Contracts run one to
    /// five years, shorter for the older; a club extends a footballer it wants and lets an
    /// unwanted one go free at his contract's end (the Bosman ruling, 1995); a renewal is paid
    /// what his level and age command where he plays, within the board's wage budget (#198).
    /// </summary>
    public sealed class ContractRules
    {
        /// <summary>The generator's longest contract by age band: under 24, to 29, 30 and over; the shortest is a season, two under 24.</summary>
        public int YoungLongest = 5, PrimeLongest = 4, OldLongest = 2;

        /// <summary>The age bands' bounds: young under the first, old from the second.</summary>
        public int PrimeFrom = 24, OldFrom = 30;

        /// <summary>A youth's first contract (#194), in seasons.</summary>
        public int YouthYears = 3;

        /// <summary>A renewal's or a free signing's length by age: under 24, under 28, under 31, older.</summary>
        public int[] RenewYears = { 4, 3, 2, 1 };

        public int[] RenewAges = { 24, 28, 31 };

        /// <summary>A club keeps a footballer this many levels above the youth that would replace him.</summary>
        public double KeepMargin = 1;

        /// <summary>A prospect, always kept: this young or younger, with this much room to his potential.</summary>
        public int ProspectAge = 21, ProspectRoom = 2;

        /// <summary>From this age a footballer is kept only at his new division's level or above.</summary>
        public int VeteranFrom = 32;

        /// <summary>
        /// A relegation clause: a relegated club's wages fall this share of the way to the new
        /// division's rate (#198's rule), 1 all the way. Contracts in English football commonly cut
        /// wages by a quarter to a half on relegation; the rule's 1.6 to 1.0 to 0.6 is 37–40 %.
        /// </summary>
        public double RelegationCut = 1;

        public static ContractRules Reference => new ContractRules();
    }

    /// <summary>A footballer's move at the season's turn (#201): released (to 0), or signed from the free agents (from 0).</summary>
    public sealed class ContractMove
    {
        public int PlayerId, From, To;
        public string FirstName = "", LastName = "";
    }

    /// <summary>What a season's turn did with its contracts (#201), for the measurement.</summary>
    public sealed class ContractTurn
    {
        public List<Player> Renewed = new List<Player>(), Released = new List<Player>(), Signed = new List<Player>();
    }

    /// <summary>
    /// Contracts (#201): each footballer's seasons left, run down at each turn; those ending
    /// renewed if his club wants him within its wage budget, or released into the free agents;
    /// and the free agents signed by clubs short of a position, the rest retiring after a season.
    /// </summary>
    public static class Contracts
    {
        // The world stream's draws beyond the generator's four (WorldGenerator): a footballer's first contract.
        private const long ContractDraw = 5;

        /// <summary>The generator's contract for a footballer of <paramref name="age"/>, drawn on his own counter.</summary>
        public static int FirstContract(ulong seed, int playerId, int age, ContractRules? rules = null)
        {
            rules ??= ContractRules.Reference;
            (int low, int high) = age < rules.PrimeFrom ? (2, rules.YoungLongest) : age < rules.OldFrom ? (1, rules.PrimeLongest) : (1, rules.OldLongest);
            double u = CounterRng.Uniform(seed, ContractDraw, RandomStream.World, playerId, 0);
            return low + Math.Min(high - low, (int)(u * (high - low + 1)));
        }

        /// <summary>A renewal's or a free signing's length for a footballer of <paramref name="age"/>.</summary>
        public static int RenewYears(int age, ContractRules? rules = null)
        {
            rules ??= ContractRules.Reference;
            for (int i = 0; i < rules.RenewAges.Length; i++)
            {
                if (age < rules.RenewAges[i]) return rules.RenewYears[i];
            }

            return rules.RenewYears[rules.RenewYears.Length - 1];
        }

        /// <summary>Whether <paramref name="club"/>, to play in <paramref name="division"/>, wants <paramref name="p"/>.</summary>
        public static bool Wanted(World world, Club club, Player p, int division, ContractRules? rules = null, DevelopmentRules? development = null)
        {
            rules ??= ContractRules.Reference;
            double level = Development.Level(p);
            if (p.Age <= rules.ProspectAge && p.Potential - level >= rules.ProspectRoom) return true;
            if (p.Age >= rules.VeteranFrom) return level >= DivisionLevel(division);
            return level >= ReplacementLevel(world, club, division, development) + rules.KeepMargin;
        }

        /// <summary>The level of the youth that would replace a footballer at <paramref name="club"/> in <paramref name="division"/> (#194).</summary>
        public static double ReplacementLevel(World world, Club club, int division, DevelopmentRules? development = null) =>
            Development.YouthLevel(world, club, division, development);

        /// <summary>The free agents: the world's footballers without a club.</summary>
        public static List<Player> FreeAgents(World world) => world.Players.FindAll(p => p.ClubId == 0);

        /// <summary>
        /// The season's turn into <paramref name="season"/>, before the youth fill: each contract a
        /// season down, those ending renewed or released, then the free agents signed by the clubs
        /// short of a position. <paramref name="division"/> is each club's next division.
        /// </summary>
        public static ContractTurn Turn(World world, int season, IReadOnlyDictionary<int, int> division, ContractRules? rules = null, DevelopmentRules? development = null)
        {
            rules ??= ContractRules.Reference;
            world.LastMoves.Clear();
            var turn = new ContractTurn();
            var releasedBy = new Dictionary<int, int>();
            int Next(Club c) => division.TryGetValue(c.Id, out int d) ? d : c.Division;

            // Each club's ending contracts, its best first: renewed if wanted and its budget holds, else released.
            foreach (Club c in world.Clubs)
            {
                int d = Next(c);

                // Relegated: every contract's relegation clause first.
                if (d > c.Division && rules.RelegationCut > 0)
                {
                    foreach (Player p in world.Players)
                    {
                        if (p.ClubId != c.Id) continue;
                        int rate = Finance.Wage(p, d);
                        if (p.Wage > rate) p.Wage -= (int)Math.Round(rules.RelegationCut * (p.Wage - rate));
                    }
                }

                long budget = Finance.WageBudget(world, c, division: d), bill = Finance.WageBill(world, c);
                var ending = new List<Player>();
                foreach (Player p in world.Players)
                {
                    if (p.ClubId != c.Id) continue;
                    p.ContractYears--;
                    if (p.ContractYears <= 0) ending.Add(p);
                }

                ending.Sort((a, b) => Development.Level(a) != Development.Level(b) ? Development.Level(b).CompareTo(Development.Level(a)) : a.Id.CompareTo(b.Id));
                var gone = new HashSet<int>();
                foreach (Player p in ending)
                {
                    int wage = Finance.Wage(p, d);
                    bool fits = wage <= p.Wage || bill - p.Wage + wage <= budget;
                    if (fits && Wanted(world, c, p, d, rules, development))
                    {
                        bill += wage - p.Wage;
                        p.Wage = wage;
                        p.ContractYears = RenewYears(p.Age, rules);
                        turn.Renewed.Add(p);
                        continue;
                    }

                    bill -= p.Wage;
                    p.ClubId = 0;
                    p.FreeSince = season;
                    p.ContractYears = 0;
                    gone.Add(p.Id);
                    releasedBy[p.Id] = c.Id;
                    turn.Released.Add(p);
                    world.LastMoves.Add(new ContractMove { PlayerId = p.Id, From = c.Id, To = 0, FirstName = p.FirstName, LastName = p.LastName });
                }

                Development.Leave(c, gone);
            }

            // The free agents signed: the clubs by their next division, then reputation, each short
            // squad taking the best it wants and can afford in the position it lacks most.
            var order = new List<Club>(world.Clubs);
            order.Sort((a, b) => Next(a) != Next(b) ? Next(a).CompareTo(Next(b)) : a.Reputation != b.Reputation ? b.Reputation.CompareTo(a.Reputation) : a.Id.CompareTo(b.Id));
            foreach (Club c in order)
            {
                int d = Next(c);
                long budget = Finance.WageBudget(world, c, division: d), bill = Finance.WageBill(world, c);
                while (world.Players.FindAll(p => p.ClubId == c.Id).Count < WorldGenerator.SquadSize)
                {
                    // The places it lacks, the most lacking first: the next if one has nobody (Codex on #204).
                    Player? best = null;
                    foreach (Position need in Development.Short(world, c))
                    {
                        foreach (Player f in world.Players)
                        {
                            if (f.ClubId != 0 || f.Positions.Count == 0 || f.Positions[0] != need) continue;
                            if (releasedBy.TryGetValue(f.Id, out int from) && from == c.Id) continue;
                            if (bill + Finance.Wage(f, d) > budget || !Wanted(world, c, f, d, rules, development)) continue;
                            if (best == null || Development.Level(f) > Development.Level(best) || (Development.Level(f) == Development.Level(best) && f.Id < best.Id)) best = f;
                        }

                        if (best != null) break;
                    }

                    if (best == null) break;
                    best.ClubId = c.Id;
                    best.Wage = Finance.Wage(best, d);
                    best.ContractYears = RenewYears(best.Age, rules);
                    best.FreeSince = 0;
                    bill += best.Wage;
                    c.Squad.Add(best.Id);
                    turn.Signed.Add(best);
                    world.LastMoves.Add(new ContractMove { PlayerId = best.Id, From = 0, To = c.Id, FirstName = best.FirstName, LastName = best.LastName });
                }
            }

            return turn;
        }

        private static double DivisionLevel(int division) => WorldGenerator.DivisionLevel[Math.Clamp(division, 1, WorldGenerator.DivisionLevel.Length) - 1];
    }
}
