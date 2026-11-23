#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

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
        public static bool Out(Player p, MatchdayKind kind) => p.InjuredDays > 0 || (kind == MatchdayKind.League ? p.LeagueBan : p.CupBan) > 0;

        /// <summary>The club's footballers out of its next match of this kind.</summary>
        public static HashSet<int> OutOf(World world, int club, MatchdayKind kind)
        {
            var outs = new HashSet<int>();
            foreach (Player p in world.Players)
            {
                if (p.ClubId == club && Out(p, kind)) outs.Add(p.Id);
            }

            return outs;
        }

        /// <summary>An injury's days out: its band drawn by its level, then its day within the band, on the squad stream.</summary>
        public static int Layoff(ulong seed, int fixtureId, int player, int injury, SquadRules rules)
        {
            if (injury <= 0) return 0;
            double[] shares = injury >= 2 ? rules.SeriousShares : rules.MinorShares;
            double u = CounterRng.Uniform(seed, fixtureId, RandomStream.Squad, player, 0), sum = 0;
            int band = shares.Length - 1;
            for (int b = 0; b < shares.Length; b++)
            {
                sum += shares[b];
                if (u < sum)
                {
                    band = b;
                    break;
                }
            }

            // Past every share (they sum to 1 but for rounding): the last band with a share.
            while (band > 0 && shares[band] <= 0) band--;
            int first = rules.BandDays[band][0], last = rules.BandDays[band][1];
            int day = first + (int)(CounterRng.Uniform(seed, fixtureId, RandomStream.Squad, player, 1) * (last - first + 1));
            return Math.Min(last, day);
        }

        /// <summary>
        /// A matchday played: each club that played it serves a match of the ban of each of its
        /// footballers banned in that competition, and the days pass for the injured. Call it before
        /// the matchday's own cards and injuries are applied.
        /// </summary>
        public static void Serve(World world, IReadOnlyList<Fixture> played, MatchdayKind kind, SquadRules rules)
        {
            var clubs = new HashSet<int>();
            foreach (Fixture f in played)
            {
                clubs.Add(f.Home);
                clubs.Add(f.Away);
            }

            foreach (Player p in world.Players)
            {
                if (clubs.Contains(p.ClubId))
                {
                    if (kind == MatchdayKind.League && p.LeagueBan > 0) p.LeagueBan--;
                    else if (kind == MatchdayKind.Cup && p.CupBan > 0) p.CupBan--;
                }

                if (p.InjuredDays > 0) p.InjuredDays = Math.Max(0, p.InjuredDays - rules.DaysPerMatchday);
            }
        }

        /// <summary>
        /// A fixture's result applied to its footballers, through its lineup (simulation id to the
        /// world's player id): each injury's layoff; each caution toward a ban, the two behind a
        /// second yellow excepted; each sending-off's ban.
        /// </summary>
        public static void Apply(World world, Season season, Fixture fixture, MatchResult result, IReadOnlyDictionary<int, int> lineup, SquadRules rules)
        {
            var byId = new Dictionary<int, Player>(world.Players.Count);
            foreach (Player p in world.Players) byId[p.Id] = p;
            bool cup = fixture.Competition == CompetitionId.Cup;
            foreach (ResultPlayer line in result.Players)
            {
                if (!lineup.TryGetValue(line.Player, out int id) || !byId.TryGetValue(id, out Player? p)) continue;
                if (line.Injury > 0) p.InjuredDays = Math.Max(p.InjuredDays, Layoff(season.Seed, fixture.Id, p.Id, line.Injury, rules));
                int cautions = line.SentOff && line.Cautions >= 2 ? line.Cautions - 2 : line.Cautions;
                for (int c = 0; c < cautions; c++)
                {
                    int total = cup ? ++p.CupCautions : ++p.LeagueCautions;
                    bool ban = total == rules.CautionsForBan
                        || (total > rules.CautionsForBan && rules.CautionsPerFurtherBan > 0 && (total - rules.CautionsForBan) % rules.CautionsPerFurtherBan == 0);
                    if (ban && cup) p.CupBan++;
                    else if (ban) p.LeagueBan++;
                }

                if (line.SentOff)
                {
                    if (cup) p.CupBan += rules.SendingOffBan;
                    else p.LeagueBan += rules.SendingOffBan;
                }
            }
        }

        /// <summary>The season's turn: cautions start again, bans still to serve carry over, and the off-season's days pass.</summary>
        public static void NewSeason(World world, SquadRules rules)
        {
            foreach (Player p in world.Players)
            {
                p.LeagueCautions = 0;
                p.CupCautions = 0;
                p.InjuredDays = Math.Max(0, p.InjuredDays - rules.OffSeasonDays);
            }
        }
    }
}
