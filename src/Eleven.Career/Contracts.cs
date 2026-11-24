#nullable enable
using System;
using System.Collections.Generic;

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
        /// <summary>The generator's contract for a footballer of <paramref name="age"/>, drawn on his own counter.</summary>
        public static int FirstContract(ulong seed, int playerId, int age, ContractRules? rules = null) => 0;

        /// <summary>A renewal's or a free signing's length for a footballer of <paramref name="age"/>.</summary>
        public static int RenewYears(int age, ContractRules? rules = null) => 0;

        /// <summary>Whether <paramref name="club"/>, to play in <paramref name="division"/>, wants <paramref name="p"/>.</summary>
        public static bool Wanted(World world, Club club, Player p, int division, ContractRules? rules = null, DevelopmentRules? development = null) => false;

        /// <summary>The level of the youth that would replace a footballer at <paramref name="club"/> in <paramref name="division"/> (#194).</summary>
        public static double ReplacementLevel(World world, Club club, int division, DevelopmentRules? development = null) => 0;

        /// <summary>The free agents: the world's footballers without a club.</summary>
        public static List<Player> FreeAgents(World world) => world.Players.FindAll(p => p.ClubId == 0);

        /// <summary>
        /// The season's turn into <paramref name="season"/>, before the youth fill: each contract a
        /// season down, those ending renewed or released, then the free agents signed by the clubs
        /// short of a position. <paramref name="division"/> is each club's next division.
        /// </summary>
        public static ContractTurn Turn(World world, int season, IReadOnlyDictionary<int, int> division, ContractRules? rules = null, DevelopmentRules? development = null) => new ContractTurn();
    }
}
