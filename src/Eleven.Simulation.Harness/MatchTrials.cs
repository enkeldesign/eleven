// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Eleven.Simulation.Harness
{
    /// <summary>One whole CPU-against-CPU match (#86).</summary>
    public sealed class MatchTrial
    {
        public ulong Seed;
        public bool Finished;
        public int HomeGoals, AwayGoals, FirstHalfGoals, SecondHalfGoals, Restarts;

        /// <summary>s of simulation from the kick-off to full time: the clock, the dead balls and the break.</summary>
        public double LengthSeconds;

        public string Result = "";
        public ulong FinalHash;
    }

    /// <summary>
    /// Whole CPU-against-CPU matches (#86): a dozen seeds under the reference halves, each from
    /// the kick-off to full time, every brain running. Records the score, the goals by half, the
    /// match's length and whether it finished. Technical evidence, not feel.
    /// </summary>
    public static class MatchTrials
    {
        public const int Seeds = 12;

        public static List<MatchTrial> Run(SimConfig config)
        {
            var trials = new List<MatchTrial>();
            for (int i = 1; i <= Seeds; i++) trials.Add(One(config, 0x86000000UL + (ulong)i));
            return trials;
        }

        public static MatchTrial One(SimConfig config, ulong seed)
        {
            var sim = new Simulator(config, OneVOne.Match(config, seed, bothCpu: true));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();

            // Far more than a match needs: a match that hasn't finished by then is stuck.
            int limit = 3 * 2 * config.HalfTicks;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                recorder.Observe(config, sim.PeekState(), events);
            }

            var trial = new MatchTrial { Seed = seed, Finished = recorder.Result != null, LengthSeconds = sim.Tick * config.TickSeconds, FinalHash = sim.StateHash() };
            if (recorder.Result != null)
            {
                MatchResult r = recorder.Result;
                trial.HomeGoals = r.HomeGoals;
                trial.AwayGoals = r.AwayGoals;
                trial.Restarts = r.Restarts;
                foreach (ResultGoal g in r.Goals)
                {
                    if (g.Half == 1) trial.FirstHalfGoals++;
                    else trial.SecondHalfGoals++;
                }

                trial.Result = r.ToText();
            }

            return trial;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<MatchTrial> trials)
        {
            w.WriteLine("config_hash,seed,finished,home,away,first_half_goals,second_half_goals,restarts,length_s,final_hash");
            foreach (MatchTrial t in trials)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1:x},{2},{3},{4},{5},{6},{7},{8:F2},{9:x16}",
                    config.Hash, t.Seed, t.Finished ? "yes" : "no", t.HomeGoals, t.AwayGoals, t.FirstHalfGoals, t.SecondHalfGoals, t.Restarts, t.LengthSeconds, t.FinalHash));
            }
        }

        public static string KeyNumbers(IReadOnlyList<MatchTrial> trials)
        {
            int finished = 0, goals = 0, first = 0, second = 0, home = 0, away = 0, draws = 0;
            double length = 0;
            foreach (MatchTrial t in trials)
            {
                if (t.Finished) finished++;
                goals += t.HomeGoals + t.AwayGoals;
                first += t.FirstHalfGoals;
                second += t.SecondHalfGoals;
                if (t.HomeGoals > t.AwayGoals) home++;
                else if (t.AwayGoals > t.HomeGoals) away++;
                else draws++;
                length += t.LengthSeconds;
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0} of {1} matches finished; {2} goals ({3} first half, {4} second); home {5} wins, away {6}, {7} draws; mean length {8:F0} s; result schema {9}",
                finished, trials.Count, goals, first, second, home, away, draws, length / Math.Max(1, trials.Count), MatchResult.SchemaVersion);
        }
    }
}
