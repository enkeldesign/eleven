// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Eleven.Simulation.Harness
{
    /// <summary>One eleven-a-side match of M4's gate (#112): its result, what the laws gave, and whether anyone got stuck.</summary>
    public sealed class GateMatch
    {
        /// <summary>The hash of the config the match was played under (INV-11): a cup tie's has knockout on.</summary>
        public ulong ConfigHash;

        /// <summary>"league", "semi-final" or "final".</summary>
        public string Stage = "";

        public int Home, Away;
        public ulong Seed;
        public bool Finished;
        public int HomeGoals, AwayGoals;

        /// <summary>Whether extra time was played, and each side's kicks scored in a shootout (#111).</summary>
        public bool ExtraTime;

        public int HomeShootout, AwayShootout;

        /// <summary>The winner's place among the sides, or -1 for a draw or an unfinished match.</summary>
        public int Winner = -1;

        public int Fouls, Offsides, Cautions, SendingsOff, Injuries, Substitutions;
        public int Penalties, FreeKicks, IndirectFreeKicks, Corners, ThrowIns, GoalKicks;

        /// <summary>s. The longest any outfielder on the pitch stood within <see cref="League.StillRadius"/> of one place in play.</summary>
        public double LongestStillSeconds;

        /// <summary>s. The longest stretch in play without a touch of the ball.</summary>
        public double LongestUntouchedSeconds;

        public double LengthSeconds;
        public ulong FinalHash;

        /// <summary>The match's result in its text form (INV-14), or empty when unfinished.</summary>
        public string ResultText = "";

        /// <summary>How many replays of the recorded commands were run, and how many gave the same events and ended on the same state.</summary>
        public int Replays, ReplaysEqual;

        /// <summary>Whether the match got stuck: unfinished, or an outfielder still, or the ball untouched, beyond the league's bounds.</summary>
        public bool Stuck => !Finished || LongestStillSeconds > League.StuckSeconds || LongestUntouchedSeconds > League.StuckSeconds;
    }

    /// <summary>
    /// M4's gate (#112): eleven a side with every law of M4. The six fictional sides of #103's
    /// league play a single round robin under league rules, then the top four a knockout cup:
    /// two semi-finals and a final, with extra time and shootouts (#111). Every footballer a CPU
    /// footballer at his side's tier, each side's manager substituting (#110). Each cup tie is
    /// replayed from its recorded commands. Technical evidence, not feel.
    /// </summary>
    public static class Gate11
    {
        public const int PerSide = 10;

        /// <summary>How often a cup tie's recording is replayed.</summary>
        public const int CupReplays = 2;

        public static List<GateMatch> Run(SimConfig config, IReadOnlyList<LeagueSide> sides)
        {
            var matches = new List<GateMatch>();
            for (int h = 0; h < sides.Count; h++)
            {
                // A single round robin: each pair once, the lower-placed side of the list at home.
                for (int a = h + 1; a < sides.Count; a++) matches.Add(Play(config, sides, h, a, 0x112000000UL + (ulong)(h * sides.Count + a), "league"));
            }

            // The cup: the league's top four, first against fourth and second against third.
            List<int> order = Standing(sides, matches);
            if (order.Count >= 4)
            {
                SimConfig knockout = config.With("knockout", 1);
                GateMatch one = Play(knockout, sides, order[0], order[3], 0x112100001UL, "semi-final", CupReplays);
                GateMatch two = Play(knockout, sides, order[1], order[2], 0x112100002UL, "semi-final", CupReplays);
                matches.Add(one);
                matches.Add(two);
                if (one.Winner >= 0 && two.Winner >= 0) matches.Add(Play(knockout, sides, one.Winner, two.Winner, 0x112100003UL, "final", CupReplays));
            }

            return matches;
        }

        /// <summary>The sides by the league's points, then goal difference, then goals, then name.</summary>
        public static List<int> Standing(IReadOnlyList<LeagueSide> sides, IReadOnlyList<GateMatch> matches)
        {
            var points = new int[sides.Count];
            var difference = new int[sides.Count];
            var scored = new int[sides.Count];
            foreach (GateMatch m in matches)
            {
                if (!m.Finished || m.Stage != "league") continue;
                points[m.Home] += m.HomeGoals > m.AwayGoals ? 3 : m.HomeGoals == m.AwayGoals ? 1 : 0;
                points[m.Away] += m.AwayGoals > m.HomeGoals ? 3 : m.HomeGoals == m.AwayGoals ? 1 : 0;
                difference[m.Home] += m.HomeGoals - m.AwayGoals;
                difference[m.Away] += m.AwayGoals - m.HomeGoals;
                scored[m.Home] += m.HomeGoals;
                scored[m.Away] += m.AwayGoals;
            }

            return Enumerable.Range(0, sides.Count).OrderByDescending(i => points[i]).ThenByDescending(i => difference[i]).ThenByDescending(i => scored[i])
                .ThenBy(i => sides[i].Name, StringComparer.Ordinal).ToList();
        }

        public static GateMatch Play(SimConfig config, IReadOnlyList<LeagueSide> sides, int home, int away, ulong seed, string stage, int replays = 0)
        {
            LeagueSide hs = sides[home], aw = sides[away];
            var sim = new Simulator(config, Squads.Match(config, seed, PerSide, bothCpu: true, awayCpu: aw.Skill, homeCpu: hs.Skill,
                homeSide: hs.Capabilities, awaySide: aw.Capabilities));
            var m = new GateMatch { Stage = stage, Home = home, Away = away, Seed = seed };
            CommandRecording? recording = replays > 0 ? CommandRecording.StartFrom(sim) : null;
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();

            // Long enough for two halves, extra time and a long shootout, with every stoppage.
            long limit = 12L * config.HalfTicks;
            var anchors = new Dictionary<int, Vec2>();
            var stillFrom = new Dictionary<int, long>();
            long longestStill = 0, touchedAt = -1, untouched = 0;
            ulong digest = DigestStart;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                recording?.Record(commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                recorder.Observe(config, s, events);
                foreach (SimEvent e in events)
                {
                    digest = Digest(digest, e);
                    switch (e.Kind)
                    {
                        case SimEventKind.Foul: m.Fouls++; break;
                        case SimEventKind.Offside: m.Offsides++; break;
                        case SimEventKind.Cautioned: m.Cautions++; break;
                        case SimEventKind.SentOff: m.SendingsOff++; break;
                        case SimEventKind.Injured: m.Injuries++; break;
                        case SimEventKind.Substituted: m.Substitutions++; break;
                        case SimEventKind.RestartAwarded:
                            // A shootout's kicks (#111) are penalties of the shootout, not of play.
                            if (s.Match.Phase == MatchPhase.Shootout || s.Match.Phase == MatchPhase.FullTime) break;
                            switch ((RestartKind)e.Other)
                            {
                                case RestartKind.Penalty: m.Penalties++; break;
                                case RestartKind.FreeKick: m.FreeKicks++; break;
                                case RestartKind.IndirectFreeKick: m.IndirectFreeKicks++; break;
                                case RestartKind.Corner: m.Corners++; break;
                                case RestartKind.ThrowIn: m.ThrowIns++; break;
                                case RestartKind.GoalKick: m.GoalKicks++; break;
                            }

                            break;
                    }
                }

                MatchPhase phase = s.Match.Phase;
                bool live = s.Restart.Phase == RestartPhase.Live
                    && (phase == MatchPhase.FirstHalf || phase == MatchPhase.SecondHalf || phase == MatchPhase.ExtraFirstHalf || phase == MatchPhase.ExtraSecondHalf);
                if (!live)
                {
                    // The clocks of stillness and touches run only in play.
                    stillFrom.Clear();
                    touchedAt = -1;
                    continue;
                }

                // By footballer, not by place in the state: a substitution moves places (#110).
                foreach (PlayerState p in s.Players)
                {
                    if (p.Off || Array.FindIndex(s.Keepers, k => k.PlayerId == p.Id) >= 0) continue;
                    if (!stillFrom.TryGetValue(p.Id, out long from) || (p.Position - anchors[p.Id]).LengthSquared > League.StillRadius * League.StillRadius)
                    {
                        anchors[p.Id] = p.Position;
                        stillFrom[p.Id] = from = s.Tick;
                    }

                    longestStill = Math.Max(longestStill, s.Tick - from);
                }

                if (touchedAt < 0 || events.Any(e => e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.ChallengeWon))
                {
                    touchedAt = s.Tick;
                }

                untouched = Math.Max(untouched, s.Tick - touchedAt);
            }

            MatchResult? result = recorder.Result;
            m.Finished = result != null;
            if (result != null)
            {
                m.HomeGoals = result.HomeGoals;
                m.AwayGoals = result.AwayGoals;
                m.ExtraTime = result.ExtraTime == 1;
                m.HomeShootout = result.HomeShootoutGoals;
                m.AwayShootout = result.AwayShootoutGoals;
                m.Winner = result.Winner == Squads.HomeTeam ? home : result.Winner == Squads.AwayTeam ? away : -1;
                m.ResultText = result.ToText();
            }

            m.LongestStillSeconds = longestStill * config.TickSeconds;
            m.LongestUntouchedSeconds = untouched * config.TickSeconds;
            m.LengthSeconds = sim.Tick * config.TickSeconds;
            m.FinalHash = sim.StateHash();

            // Each replay: the recorded commands from the start, no brain run, to the same state
            // through the same events, in order. The result is read from those, so it is the same.
            for (int i = 0; recording != null && i < replays; i++)
            {
                m.Replays++;
                var replayEvents = new List<SimEvent>();
                Simulator again = recording.Replay(config, replayEvents);
                ulong second = DigestStart;
                foreach (SimEvent e in replayEvents) second = Digest(second, e);
                if (again.StateHash() == m.FinalHash && second == digest) m.ReplaysEqual++;
            }

            return m;
        }

        private const ulong DigestStart = 14695981039346656037UL;

        // FNV-1a over an event's tick, kind, subject, rejection and other: an order-sensitive digest of a match's events.
        private static ulong Digest(ulong digest, SimEvent e)
        {
            foreach (long part in new[] { e.Tick, (long)e.Kind, e.Subject, (long)e.Rejection, e.Other })
            {
                digest = unchecked((digest ^ (ulong)part) * 1099511628211UL);
            }

            return digest;
        }

        public static void WriteMatches(TextWriter w, SimConfig config, IReadOnlyList<LeagueSide> sides, IReadOnlyList<GateMatch> matches)
        {
            w.WriteLine("config_hash,stage,seed,home,away,finished,home_goals,away_goals,extra_time,shootout,winner,fouls,offsides,cautions,sendings_off,injuries,substitutions,penalties,free_kicks,indirect_free_kicks,corners,throw_ins,goal_kicks,longest_still_s,longest_untouched_s,stuck,replays_equal,length_s,final_hash");
            foreach (GateMatch m in matches)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:x16},{1},{2:x},{3},{4},{5},{6},{7},{8},{9}-{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24:F2},{25:F2},{26},{27}/{28},{29:F2},{30:x16}",
                    config.Hash, m.Stage, m.Seed, sides[m.Home].Name, sides[m.Away].Name, m.Finished ? "yes" : "no", m.HomeGoals, m.AwayGoals, m.ExtraTime ? "yes" : "no",
                    m.HomeShootout, m.AwayShootout, m.Winner >= 0 ? sides[m.Winner].Name : "none", m.Fouls, m.Offsides, m.Cautions, m.SendingsOff, m.Injuries, m.Substitutions,
                    m.Penalties, m.FreeKicks, m.IndirectFreeKicks, m.Corners, m.ThrowIns, m.GoalKicks, m.LongestStillSeconds, m.LongestUntouchedSeconds, m.Stuck ? "yes" : "no",
                    m.ReplaysEqual, m.Replays, m.LengthSeconds, m.FinalHash));
            }
        }

        /// <summary>Whether the gate holds: nothing stuck, every replay equal, and a cup played to a winner.</summary>
        public static bool Passes(IReadOnlyList<GateMatch> matches) =>
            matches.Count > 0 && matches.All(m => !m.Stuck && m.ReplaysEqual == m.Replays)
            && matches.Count(m => m.Stage == "final") == 1 && matches.Where(m => m.Stage != "league").All(m => m.Winner >= 0);

        public static string KeyNumbers(IReadOnlyList<LeagueSide> sides, IReadOnlyList<GateMatch> matches)
        {
            GateMatch[] league = matches.Where(m => m.Stage == "league").ToArray();
            GateMatch[] cup = matches.Where(m => m.Stage != "league").ToArray();
            GateMatch? final = matches.FirstOrDefault(m => m.Stage == "final");
            double n = Math.Max(1, matches.Count);
            return string.Format(CultureInfo.InvariantCulture,
                "11v11 gate: {0} of {1} finished, {2} stuck; a match: {3:F2} goals, {4:F2} fouls, {5:F2} offsides, {6:F2} cautions, {7:F2} sendings-off, {8:F2} injuries, {9:F2} substitutions, {10:F2} corners; " +
                "longest still {11:F1} s, untouched {12:F1} s; league {13} matches, {14} drawn; cup {15} ties, {16} to extra time, {17} to a shootout; replays {18} of {19} equal; winner {20}",
                matches.Count(m => m.Finished), matches.Count, matches.Count(m => m.Stuck),
                matches.Sum(m => m.HomeGoals + m.AwayGoals) / n, matches.Sum(m => m.Fouls) / n, matches.Sum(m => m.Offsides) / n, matches.Sum(m => m.Cautions) / n,
                matches.Sum(m => m.SendingsOff) / n, matches.Sum(m => m.Injuries) / n, matches.Sum(m => m.Substitutions) / n, matches.Sum(m => m.Corners) / n,
                matches.Max(m => m.LongestStillSeconds), matches.Max(m => m.LongestUntouchedSeconds),
                league.Length, league.Count(m => m.Finished && m.HomeGoals == m.AwayGoals), cup.Length, cup.Count(m => m.ExtraTime), cup.Count(m => m.HomeShootout + m.AwayShootout > 0),
                matches.Sum(m => m.ReplaysEqual), matches.Sum(m => m.Replays), final != null && final.Winner >= 0 ? sides[final.Winner].Name : "none");
        }
    }
}
