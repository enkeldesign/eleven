// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Eleven.Simulation.Harness
{
    /// <summary>One CPU-against-CPU match with the referee on (#85).</summary>
    public sealed class RestartTrial
    {
        public ulong Seed;
        public int Goals;

        /// <summary>Restarts awarded, by <see cref="RestartKind"/>: none, kick-off, throw-in, goal kick, corner.</summary>
        public int[] Awarded = new int[5];

        public int Taken;

        /// <summary>s. The mean and the longest time from an award to the restart being taken.</summary>
        public double MeanDeadSeconds, LongestDeadSeconds;

        /// <summary>s. The longest stretch with the ball live and no one playing it.</summary>
        public double LongestIdleSeconds;

        public ulong FinalHash;
    }

    /// <summary>
    /// The restart sequencer over whole CPU-against-CPU matches (#85): a dozen seeds, each from the
    /// kick-off for <see cref="MatchSeconds"/> of play, every brain running. Counts restarts by
    /// kind, how long the ball was dead, and whether any match stalled. Technical evidence.
    /// </summary>
    public static class RestartTrials
    {
        public const int Seeds = 12;
        public const double MatchSeconds = 180;

        public static List<RestartTrial> Run(SimConfig config)
        {
            var trials = new List<RestartTrial>();
            for (int i = 1; i <= Seeds; i++) trials.Add(One(config, 0x85000000UL + (ulong)i));
            return trials;
        }

        public static RestartTrial One(SimConfig config, ulong seed)
        {
            var sim = new Simulator(config, OneVOne.KickOff(config, seed, bothCpu: true));
            var trial = new RestartTrial { Seed = seed };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            long deadFrom = -1, deadTotal = 0, deadLongest = 0, lastPlayed = 0, idle = 0;
            int ticks = (int)(MatchSeconds * config.TickRate);
            for (int t = 0; t < ticks; t++)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                foreach (SimEvent e in events)
                {
                    switch (e.Kind)
                    {
                        case SimEventKind.Goal: trial.Goals++; break;
                        case SimEventKind.RestartAwarded:
                            trial.Awarded[e.Other]++;
                            deadFrom = e.Tick;
                            break;
                        case SimEventKind.RestartTaken:
                            trial.Taken++;
                            if (deadFrom >= 0)
                            {
                                deadTotal += e.Tick - deadFrom;
                                deadLongest = Math.Max(deadLongest, e.Tick - deadFrom);
                                deadFrom = -1;
                            }

                            break;
                        case SimEventKind.Touched:
                        case SimEventKind.Kicked:
                        case SimEventKind.KeeperHeld:
                        case SimEventKind.KeeperParried:
                        case SimEventKind.ChallengeWon:
                            idle = Math.Max(idle, e.Tick - lastPlayed);
                            lastPlayed = e.Tick;
                            break;
                    }
                }

                // A dead ball isn't idle play.
                if (sim.PeekState().Restart.Phase != RestartPhase.Live) lastPlayed = sim.Tick;
            }

            idle = Math.Max(idle, sim.Tick - lastPlayed);
            int restarts = trial.Taken > 0 ? trial.Taken - 1 : 0;
            trial.MeanDeadSeconds = restarts > 0 ? deadTotal * config.TickSeconds / restarts : 0;
            trial.LongestDeadSeconds = deadLongest * config.TickSeconds;
            trial.LongestIdleSeconds = idle * config.TickSeconds;
            trial.FinalHash = sim.StateHash();
            return trial;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<RestartTrial> trials)
        {
            w.WriteLine("config_hash,seed,goals,kick_offs,throw_ins,goal_kicks,corners,taken,mean_dead_s,longest_dead_s,longest_idle_s,final_hash");
            foreach (RestartTrial t in trials)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1:x},{2},{3},{4},{5},{6},{7},{8:F2},{9:F2},{10:F2},{11:x16}",
                    config.Hash, t.Seed, t.Goals, t.Awarded[1], t.Awarded[2], t.Awarded[3], t.Awarded[4], t.Taken, t.MeanDeadSeconds, t.LongestDeadSeconds, t.LongestIdleSeconds, t.FinalHash));
            }
        }

        public static string KeyNumbers(IReadOnlyList<RestartTrial> trials)
        {
            int goals = 0, kickOffs = 0, throws = 0, goalKicks = 0, corners = 0;
            double dead = 0, longest = 0, idle = 0;
            foreach (RestartTrial t in trials)
            {
                goals += t.Goals;
                kickOffs += t.Awarded[1];
                throws += t.Awarded[2];
                goalKicks += t.Awarded[3];
                corners += t.Awarded[4];
                dead += t.MeanDeadSeconds;
                longest = Math.Max(longest, t.LongestDeadSeconds);
                idle = Math.Max(idle, t.LongestIdleSeconds);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0} matches of {1:F0} s: {2} goals, restarts {3} kick-offs, {4} throw-ins, {5} goal kicks, {6} corners; dead ball mean {7:F2} s, longest {8:F2} s; longest idle {9:F1} s",
                trials.Count, MatchSeconds, goals, kickOffs, throws, goalKicks, corners, dead / Math.Max(1, trials.Count), longest, idle);
        }
    }
}
