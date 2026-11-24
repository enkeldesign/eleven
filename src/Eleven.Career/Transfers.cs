#nullable enable
using System;
using System.Collections.Generic;

namespace Eleven.Career
{
    /// <summary>
    /// The transfer market's knobs (#203), all technical references, not tuned feel. FIFA's two
    /// registration periods, a long one between seasons and a short one mid-season; a fee that
    /// rises with ability and division, peaks in the mid-twenties and falls with a contract's
    /// years left (Carmichael and Thomas, 1993; Dobson and Gerrard, 1999; Frick, 2007).
    /// </summary>
    public sealed class TransferRules
    {
        /// <summary>A footballer's value at level 10, at his peak, in the second division, with three years left.</summary>
        public double ValueBase = 320;

        /// <summary>His value's growth a level: exp(ValueSlope × (level − 10)), as #198's wage.</summary>
        public double ValueSlope = 0.25;

        /// <summary>His value by his club's division: 1, 2, 3.</summary>
        public double[] DivisionValue = { 1.6, 1.0, 0.6 };

        /// <summary>By age: under 21 more a level of room to his potential, at most; then from 28, 30 and 32.</summary>
        public double YoungRoom = 0.15, YoungMost = 1.6, From28 = 0.75, From30 = 0.5, From32 = 0.25;

        /// <summary>By the contract's years left: one, two; three or more is 1.</summary>
        public double OneYearLeft = 0.5, TwoYearsLeft = 0.8;

        /// <summary>A club that wants him asks this much more than his value.</summary>
        public double WantedMarkup = 1.25;

        /// <summary>A buyer spends at most this share of its balance on one fee.</summary>
        public double SpendShare = 0.5;

        /// <summary>A buyer's new footballer must be this many levels above the one he replaces.</summary>
        public double UpgradeMargin = 1;

        /// <summary>A club buys at most this many a window, and an over-budget one lists this many.</summary>
        public int BuysPerWindow = 2, Listed = 2;

        /// <summary>A seller keeps at least this many in its squad.</summary>
        public int SquadMinimum = 20;

        /// <summary>The mid-season window opens after the league round this far through the season, 0.5 halfway.</summary>
        public double MidSeason = 0.5;

        public static TransferRules Reference => new TransferRules();
    }

    /// <summary>A transfer window (#203). Numeric values are stable and encoded as one byte.</summary>
    public enum TransferWindow : byte
    {
        PreSeason = 0,
        MidSeason = 1,
    }

    /// <summary>One transfer (#203): who, from and to which club, for what fee, in which window.</summary>
    public sealed class Transfer
    {
        public int PlayerId, From, To, Season;
        public long Fee;
        public TransferWindow Window;
        public string FirstName = "", LastName = "";
    }

    /// <summary>
    /// The transfer market (#203): a footballer's value, and a window's buying and selling by
    /// the AI's clubs, each fee from the buyer's balance to the seller's on the ledger's transfers
    /// line, the footballer moving with a new contract at the buyer's wage.
    /// </summary>
    public static class Transfers
    {
        /// <summary>A footballer's value in k, by his level, age, contract and his club's division.</summary>
        public static long Value(Player p, int division, TransferRules? rules = null) => 0;

        /// <summary>The fee his club asks: his value, more if it wants him (#201).</summary>
        public static long Asking(World world, Club seller, Player p, TransferRules? rules = null) => 0;

        /// <summary>A club's XI by level: its best keeper and its ten best outfielders.</summary>
        public static List<Player> Eleven(World world, Club club) => new List<Player>();

        /// <summary>Whether the matchday just played is the mid-season window's: the league round <see cref="TransferRules.MidSeason"/> through.</summary>
        public static bool MidSeasonAfter(Season season, CompetitionRules competition, TransferRules? rules = null) => false;

        /// <summary>
        /// A window in <paramref name="season"/>: each club but those <paramref name="excluded"/>
        /// (the human's, until M6.12c), by division and reputation, buying for its XI's weakest
        /// place; the over-budget listing their highest-paid outside their XI. Each club's
        /// division is <paramref name="division"/>'s, or its own.
        /// </summary>
        public static List<Transfer> Window(World world, int season, TransferWindow window, IReadOnlyDictionary<int, int>? division = null, ICollection<int>? excluded = null, TransferRules? rules = null) => new List<Transfer>();
    }
}
