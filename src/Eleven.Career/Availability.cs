#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Career
{
    /// <summary>
    /// What keeps a footballer out (#176), as knobs: an injury's layoff by UEFA's elite-club bands,
    /// the calendar's days, and SvFF's bans for cards. Untuned technical references.
    /// </summary>
    public sealed class SquadRules
    {
        /// <summary>UEFA's bands of days out: slight, minor, moderate, severe; each its first and last day.</summary>
        public int[][] BandDays = { new[] { 1, 3 }, new[] { 4, 7 }, new[] { 8, 28 }, new[] { 29, 90 } };

        /// <summary>A minor injury's chance of each band (he played on).</summary>
        public double[] MinorShares = { 0.5, 0.35, 0.15, 0 };

        /// <summary>A serious injury's chance of each band (he left the pitch).</summary>
        public double[] SeriousShares = { 0, 0.2, 0.55, 0.25 };

        /// <summary>Days from one matchday to the next.</summary>
        public int DaysPerMatchday = 7;

        /// <summary>Days between one season's last matchday and the next's first.</summary>
        public int OffSeasonDays = 60;

        /// <summary>A sending-off's ban, in his club's matches of that competition.</summary>
        public int SendingOffBan = 1;

        /// <summary>The caution that brings the first one-match ban, and then each how many more bring another.</summary>
        public int CautionsForBan = 3, CautionsPerFurtherBan = 2;

        public static SquadRules Reference { get; } = new SquadRules();
    }

    /// <summary>
    /// Who is out, and why (#176). After each fixture its footballers' injuries and cards are
    /// applied to them; each matchday the bans of those who sat it out are served, and the days
    /// pass for the injured. A footballer out is never picked.
    /// </summary>
    public static class Availability
    {
        /// <summary>Whether he can't play his club's next match of this kind.</summary>
        public static bool Out(Player p, MatchdayKind kind) => false;

        /// <summary>The club's footballers out of its next match of this kind.</summary>
        public static HashSet<int> OutOf(World world, int club, MatchdayKind kind) => new HashSet<int>();

        /// <summary>An injury's days out: its band drawn by its level, then its day within the band, on the squad stream.</summary>
        public static int Layoff(ulong seed, int fixtureId, int player, int injury, SquadRules rules) => 0;

        /// <summary>
        /// A matchday played: each club that played it serves a match of the ban of each of its
        /// footballers banned in that competition, and the days pass for the injured. Call it before
        /// the matchday's own cards and injuries are applied.
        /// </summary>
        public static void Serve(World world, IReadOnlyList<Fixture> played, MatchdayKind kind, SquadRules rules)
        {
        }

        /// <summary>
        /// A fixture's result applied to its footballers, through its lineup (simulation id to the
        /// world's player id): each injury's layoff; each caution toward a ban, the two behind a
        /// second yellow excepted; each sending-off's ban.
        /// </summary>
        public static void Apply(World world, Season season, Fixture fixture, MatchResult result, IReadOnlyDictionary<int, int> lineup, SquadRules rules)
        {
        }

        /// <summary>The season's turn: cautions start again, bans still to serve carry over, and the off-season's days pass.</summary>
        public static void NewSeason(World world, SquadRules rules)
        {
        }
    }
}
