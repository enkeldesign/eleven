// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Eleven.Simulation.Harness
{
    /// <summary>A fictional league side (#103): a CPU tier and its outfielders' capabilities.</summary>
    public sealed class LeagueSide
    {
        public LeagueSide(string name, string tier, CpuSkill skill, PlayerCapabilities capabilities)
        {
            Name = name;
            Tier = tier;
            Skill = skill;
            Capabilities = capabilities;
        }

        public string Name { get; }
        public string Tier { get; }
        public CpuSkill Skill { get; }
        public PlayerCapabilities Capabilities { get; }
    }

    /// <summary>One league match (#103): what the referee gave and how the footballers moved.</summary>
    public sealed class LeagueMatch
    {
        public int Home, Away;
        public ulong Seed;
        public bool Finished;
        public int HomeGoals, AwayGoals;
        public int Fouls, Penalties, FreeKicks, Corners, ThrowIns, GoalKicks, Advantages;

        /// <summary>s. The longest any outfielder stood within <see cref="League.StillRadius"/> of one place in play.</summary>
        public double LongestStillSeconds;

        /// <summary>s. The longest stretch in play in which a side's outfielders were all within 2 m of each other.</summary>
        public double LongestBunchedSeconds;

        /// <summary>s. The longest stretch in play without a touch of the ball.</summary>
        public double LongestUntouchedSeconds;

        public double LengthSeconds;
        public ulong FinalHash;

        /// <summary>Whether the match got stuck: unfinished, or an outfielder still, or the ball untouched, beyond the league's bounds.</summary>
        public bool Stuck => !Finished || LongestStillSeconds > League.StuckSeconds || LongestUntouchedSeconds > League.StuckSeconds;
    }

    /// <summary>A side's line in the table.</summary>
    public sealed class LeagueRow
    {
        public LeagueSide Side = null!;
        public int Played, Won, Drawn, Lost, For, Against, Points, Fouls;
    }

    /// <summary>
    /// M3's gate (#103): a double round robin of six fictional sides at five a side, every
    /// footballer a CPU footballer at his side's tier, the reference halves. It records each
    /// match's score, fouls and set pieces, and whether anyone got stuck: a test of results and
    /// spread, not of standings. Technical evidence, not feel.
    /// </summary>
    public static class League
    {
        public const int PerSide = 5;

        /// <summary>m. An outfielder who stays within this of one place is still.</summary>
        public const double StillRadius = 1.0;

        /// <summary>s. Still, or the ball untouched, for longer than this in play is stuck.</summary>
        public const double StuckSeconds = 30;

        /// <summary>The six sides, weakest first. Their names are invented; their values are technical references.</summary>
        public static IReadOnlyList<LeagueSide> Sides()
        {
            PlayerCapabilities r = PlayerCapabilities.Reference;
            PlayerCapabilities strong = r.With("strength", 0.8).With("tackling", 0.8).With("stamina", 0.8);
            PlayerCapabilities quick = r.With("maxSpeed", 7.4).With("ballControl", 0.8);
            return new[]
            {
                new LeagueSide("Harbour", "apprentice", CpuSkill.Apprentice, r),
                new LeagueSide("Millbrook", "club", CpuSkill.Club, r),
                new LeagueSide("Stonegate", "club", CpuSkill.Club, strong),
                new LeagueSide("Riverside", "pro", CpuSkill.Pro, r),
                new LeagueSide("Northfield", "pro", CpuSkill.Pro, quick),
                new LeagueSide("Kestrel", "elite", CpuSkill.Elite, r),
            };
        }

        /// <summary>Every side at home to every other, in a fixed order, each match on its own seed.</summary>
        public static List<LeagueMatch> Run(SimConfig config, IReadOnlyList<LeagueSide> sides)
        {
            var matches = new List<LeagueMatch>();
            for (int h = 0; h < sides.Count; h++)
            {
                for (int a = 0; a < sides.Count; a++)
                {
                    if (h != a) matches.Add(Play(config, sides, h, a, 0x103000000UL + (ulong)(h * sides.Count + a)));
                }
            }

            return matches;
        }

        public static LeagueMatch Play(SimConfig config, IReadOnlyList<LeagueSide> sides, int home, int away, ulong seed)
        {
            LeagueSide hs = sides[home], aw = sides[away];
            var sim = new Simulator(config, Squads.Match(config, seed, PerSide, bothCpu: true, awayCpu: aw.Skill, homeCpu: hs.Skill,
                homeSide: hs.Capabilities, awaySide: aw.Capabilities));
            var m = new LeagueMatch { Home = home, Away = away, Seed = seed };
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            int limit = 3 * 2 * config.HalfTicks;
            SimState first = sim.PeekState();
            var anchors = new Vec2[first.Players.Length];
            var stillFrom = new long[first.Players.Length];
            for (int i = 0; i < anchors.Length; i++) stillFrom[i] = -1;
            long longestStill = 0, bunchedFrom = -1, bunched = 0, touchedAt = -1, untouched = 0;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                recorder.Observe(config, s, events);
                foreach (SimEvent e in events)
                {
                    switch (e.Kind)
                    {
                        case SimEventKind.Foul: m.Fouls++; break;
                        case SimEventKind.AdvantagePlayed: m.Advantages++; break;
                        case SimEventKind.RestartAwarded:
                            switch ((RestartKind)e.Other)
                            {
                                case RestartKind.Penalty: m.Penalties++; break;
                                case RestartKind.FreeKick: m.FreeKicks++; break;
                                case RestartKind.Corner: m.Corners++; break;
                                case RestartKind.ThrowIn: m.ThrowIns++; break;
                                case RestartKind.GoalKick: m.GoalKicks++; break;
                            }

                            break;
                    }
                }

                bool live = s.Restart.Phase == RestartPhase.Live && (s.Match.Phase == MatchPhase.FirstHalf || s.Match.Phase == MatchPhase.SecondHalf);
                if (!live)
                {
                    // The clock of stillness, bunching and touches runs only in play.
                    for (int i = 0; i < stillFrom.Length; i++) stillFrom[i] = -1;
                    bunchedFrom = -1;
                    touchedAt = -1;
                    continue;
                }

                for (int i = 0; i < s.Players.Length; i++)
                {
                    if (Array.FindIndex(s.Keepers, k => k.PlayerId == s.Players[i].Id) >= 0) continue;
                    if (stillFrom[i] < 0 || (s.Players[i].Position - anchors[i]).LengthSquared > StillRadius * StillRadius)
                    {
                        anchors[i] = s.Players[i].Position;
                        stillFrom[i] = s.Tick;
                    }

                    longestStill = Math.Max(longestStill, s.Tick - stillFrom[i]);
                }

                if (touchedAt < 0 || events.Any(e => e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.ChallengeWon))
                {
                    touchedAt = s.Tick;
                }

                untouched = Math.Max(untouched, s.Tick - touchedAt);
                bool bunchedNow = SquadTrials.Spacing(s, Squads.HomeTeam) < 2 || SquadTrials.Spacing(s, Squads.AwayTeam) < 2;
                if (bunchedNow && bunchedFrom < 0) bunchedFrom = s.Tick;
                if (!bunchedNow) bunchedFrom = -1;
                if (bunchedFrom >= 0) bunched = Math.Max(bunched, s.Tick - bunchedFrom);
            }

            m.Finished = recorder.Result != null;
            if (recorder.Result != null)
            {
                m.HomeGoals = recorder.Result.HomeGoals;
                m.AwayGoals = recorder.Result.AwayGoals;
            }

            m.LongestStillSeconds = longestStill * config.TickSeconds;
            m.LongestBunchedSeconds = bunched * config.TickSeconds;
            m.LongestUntouchedSeconds = untouched * config.TickSeconds;
            m.LengthSeconds = sim.Tick * config.TickSeconds;
            m.FinalHash = sim.StateHash();
            return m;
        }

        /// <summary>The table: three points a win, one a draw; by points, then goal difference, then goals.</summary>
        public static List<LeagueRow> Table(IReadOnlyList<LeagueSide> sides, IReadOnlyList<LeagueMatch> matches)
        {
            List<LeagueRow> rows = sides.Select(s => new LeagueRow { Side = s }).ToList();
            foreach (LeagueMatch m in matches)
            {
                if (!m.Finished) continue;
                Score(rows[m.Home], m.HomeGoals, m.AwayGoals);
                Score(rows[m.Away], m.AwayGoals, m.HomeGoals);
            }

            return rows.OrderByDescending(r => r.Points).ThenByDescending(r => r.For - r.Against).ThenByDescending(r => r.For).ThenBy(r => r.Side.Name, StringComparer.Ordinal).ToList();
        }

        private static void Score(LeagueRow row, int scored, int conceded)
        {
            row.Played++;
            row.For += scored;
            row.Against += conceded;
            if (scored > conceded)
            {
                row.Won++;
                row.Points += 3;
            }
            else if (scored == conceded)
            {
                row.Drawn++;
                row.Points++;
            }
            else row.Lost++;
        }

        /// <summary>Points a match, by tier: the spread the league is for.</summary>
        public static IReadOnlyList<(string Tier, double PointsPerMatch)> ByTier(IReadOnlyList<LeagueRow> table)
        {
            string[] order = { "apprentice", "club", "pro", "elite" };
            return order.Select(t =>
            {
                LeagueRow[] rows = table.Where(r => r.Side.Tier == t).ToArray();
                int played = rows.Sum(r => r.Played);
                return (t, played > 0 ? (double)rows.Sum(r => r.Points) / played : 0);
            }).ToArray();
        }

        public static void WriteMatches(TextWriter w, SimConfig config, IReadOnlyList<LeagueSide> sides, IReadOnlyList<LeagueMatch> matches)
        {
            w.WriteLine("config_hash,seed,home,away,finished,home_goals,away_goals,fouls,penalties,free_kicks,corners,throw_ins,goal_kicks,advantages,longest_still_s,longest_bunched_s,longest_untouched_s,stuck,length_s,final_hash");
            foreach (LeagueMatch m in matches)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1:x},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14:F2},{15:F2},{16:F2},{17},{18:F2},{19:x16}",
                    config.Hash, m.Seed, sides[m.Home].Name, sides[m.Away].Name, m.Finished ? "yes" : "no", m.HomeGoals, m.AwayGoals, m.Fouls, m.Penalties, m.FreeKicks, m.Corners,
                    m.ThrowIns, m.GoalKicks, m.Advantages, m.LongestStillSeconds, m.LongestBunchedSeconds, m.LongestUntouchedSeconds, m.Stuck ? "yes" : "no", m.LengthSeconds, m.FinalHash));
            }
        }

        public static void WriteTable(TextWriter w, IReadOnlyList<LeagueRow> table)
        {
            w.WriteLine("position,side,tier,played,won,drawn,lost,for,against,points");
            for (int i = 0; i < table.Count; i++)
            {
                LeagueRow r = table[i];
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                    i + 1, r.Side.Name, r.Side.Tier, r.Played, r.Won, r.Drawn, r.Lost, r.For, r.Against, r.Points));
            }
        }

        public static string KeyNumbers(IReadOnlyList<LeagueMatch> matches, IReadOnlyList<LeagueRow> table)
        {
            int n = Math.Max(1, matches.Count);
            string tiers = string.Join(", ", ByTier(table).Select(t => string.Format(CultureInfo.InvariantCulture, "{0} {1:F2}", t.Tier, t.PointsPerMatch)));
            return string.Format(CultureInfo.InvariantCulture,
                "5v5 league: {0} of {1} finished, {2} stuck; {3:F2} goals, {4:F2} fouls, {5:F2} free kicks, {6:F2} penalties, {7:F2} corners a match; longest still {8:F1} s, bunched {9:F1} s, untouched {10:F1} s; points a match by tier: {11}; top {12}",
                matches.Count(m => m.Finished), matches.Count, matches.Count(m => m.Stuck),
                matches.Sum(m => m.HomeGoals + m.AwayGoals) / (double)n, matches.Sum(m => m.Fouls) / (double)n, matches.Sum(m => m.FreeKicks) / (double)n,
                matches.Sum(m => m.Penalties) / (double)n, matches.Sum(m => m.Corners) / (double)n,
                matches.Max(m => m.LongestStillSeconds), matches.Max(m => m.LongestBunchedSeconds), matches.Max(m => m.LongestUntouchedSeconds), tiers, table[0].Side.Name);
        }
    }
}
