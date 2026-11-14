// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Eleven.Simulation.Harness
{
    /// <summary>One CPU-against-CPU possession trial (#83).</summary>
    public sealed class CpuTrial
    {
        public string Tier = "";
        public ulong Seed;

        /// <summary><c>goal-home</c>, <c>goal-away</c>, <c>held</c> (a keeper has it), <c>out</c> (over a goal line, or off the pitch's side) or <c>time</c>.</summary>
        public string Outcome = "";

        public double Seconds;
        public int HomeKicks;
        public int AwayKicks;
        public int HomeTouches;
        public int AwayTouches;

        /// <summary>How often the ball went from one side's feet to the other's.</summary>
        public int Recoveries;

        /// <summary>s. The longest stretch in which no one touched or kicked the ball.</summary>
        public double LongestIdleSeconds;

        /// <summary>Decisions made, by mode: none, seek, approach, carry, shoot, defend, recover.</summary>
        public int[] Modes = new int[7];

        /// <summary>The run's last state hash.</summary>
        public ulong FinalHash;
    }

    /// <summary>
    /// CPU against CPU in the 1v1 world (#83): both outfielders are CPU footballers of the same
    /// tier, both keepers keep, and no one plays. Each trial runs from the 1v1 start until a goal,
    /// a keeper holding the ball, the ball leaving the pitch, or a time limit. The ball starts loose in
    /// the middle third, at a spot the seed picks. The trials show
    /// whether the brain plays football at all: shots, the ball changing feet, and no stretch where
    /// nobody gets to it. Technical evidence, not feel.
    /// </summary>
    public static class CpuTrials
    {
        public const int Seeds = 12;

        /// <summary>s. A trial's time limit.</summary>
        public const double LimitSeconds = 45;

        /// <summary>m. Half the pitch's width, for calling the ball out at the side: Law 1's 68 m.</summary>
        public const double HalfWidth = 34;

        public static readonly string[] Tiers = { "apprentice", "club", "pro", "elite" };

        public static List<CpuTrial> Run(SimConfig config)
        {
            var trials = new List<CpuTrial>();
            foreach (string tier in Tiers)
            {
                for (int i = 1; i <= Seeds; i++) trials.Add(One(config, tier, 0xC0FFEE00UL + (ulong)i));
            }

            return trials;
        }

        public static CpuTrial One(SimConfig config, string tier, ulong seed)
        {
            CpuSkill skill = CpuSkill.Named(tier) ?? throw new ArgumentException($"'{tier}' is not a difficulty tier.", nameof(tier));
            // The ball starts loose somewhere in the middle third, by the seed, so the trials differ
            // in who gets to it and from where.
            SimState start = OneVOne.Start(config, seed, awayCpu: skill, homeCpu: skill);
            double x = (Randomness.CounterRng.Uniform(seed, 0, Randomness.RandomStream.Perception, 0, 100) * 2 - 1) * 12;
            double y = (Randomness.CounterRng.Uniform(seed, 0, Randomness.RandomStream.Perception, 0, 101) * 2 - 1) * 15;
            start.Ball.Position = new Vec3(x, y, config.BallRadius);
            var sim = new Simulator(config, start);
            var trial = new CpuTrial { Tier = tier, Seed = seed };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            int limit = (int)(LimitSeconds * config.TickRate);
            int lastTeam = 0;
            long lastContact = 0;
            long idle = 0;
            for (int step = 0; step < limit && trial.Outcome.Length == 0; step++)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                foreach (Command c in commands)
                {
                    if (c.Plan.HasValue) trial.Modes[(int)c.Plan.Value.Mode]++;
                }

                sim.Step(commands, events);
                foreach (SimEvent e in events)
                {
                    bool home = e.Subject == OneVOne.HomePlayer, away = e.Subject == OneVOne.AwayPlayer;
                    if (e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.Touched)
                    {
                        if (!home && !away) continue;
                        if (e.Kind == SimEventKind.Kicked)
                        {
                            if (home) trial.HomeKicks++;
                            else trial.AwayKicks++;
                        }
                        else if (home) trial.HomeTouches++;
                        else trial.AwayTouches++;

                        int team = home ? OneVOne.HomeTeam : OneVOne.AwayTeam;
                        if (lastTeam != 0 && team != lastTeam) trial.Recoveries++;
                        lastTeam = team;
                        if (e.Tick - lastContact > idle) idle = e.Tick - lastContact;
                        lastContact = e.Tick;
                    }
                    else if (e.Kind == SimEventKind.Goal) trial.Outcome = e.Other == OneVOne.HomeTeam ? "goal-home" : "goal-away";
                    else if (e.Kind == SimEventKind.KeeperHeld && trial.Outcome.Length == 0) trial.Outcome = "held";
                }

                SimState s = sim.PeekState();
                if (trial.Outcome.Length == 0 && (s.Ball.Line == LineState.Out || s.Ball.Position.Y > HalfWidth || s.Ball.Position.Y < -HalfWidth)) trial.Outcome = "out";
            }

            if (trial.Outcome.Length == 0) trial.Outcome = "time";
            if (sim.Tick - lastContact > idle) idle = sim.Tick - lastContact;
            trial.Seconds = sim.Tick * config.TickSeconds;
            trial.LongestIdleSeconds = idle * config.TickSeconds;
            trial.FinalHash = sim.StateHash();
            return trial;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<CpuTrial> trials)
        {
            w.WriteLine("config_hash,tier,seed,outcome,seconds,home_kicks,away_kicks,home_touches,away_touches,recoveries,longest_idle_s,seek,approach,carry,shoot,defend,recover,final_hash");
            foreach (CpuTrial t in trials)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1},{2:x},{3},{4:F2},{5},{6},{7},{8},{9},{10:F2},{11},{12},{13},{14},{15},{16},{17:x16}",
                    config.Hash, t.Tier, t.Seed, t.Outcome, t.Seconds, t.HomeKicks, t.AwayKicks, t.HomeTouches, t.AwayTouches, t.Recoveries, t.LongestIdleSeconds,
                    t.Modes[1], t.Modes[2], t.Modes[3], t.Modes[4], t.Modes[5], t.Modes[6], t.FinalHash));
            }
        }

        /// <summary>For each tier: how the trials ended, kicks, recoveries and the longest idle stretch.</summary>
        public static string KeyNumbers(IReadOnlyList<CpuTrial> trials)
        {
            var parts = new List<string>();
            foreach (string tier in Tiers)
            {
                int n = 0, goals = 0, held = 0, outs = 0, time = 0, kicks = 0, recoveries = 0;
                double idle = 0;
                foreach (CpuTrial t in trials)
                {
                    if (t.Tier != tier) continue;
                    n++;
                    if (t.Outcome.StartsWith("goal", StringComparison.Ordinal)) goals++;
                    else if (t.Outcome == "held") held++;
                    else if (t.Outcome == "out") outs++;
                    else time++;
                    kicks += t.HomeKicks + t.AwayKicks;
                    recoveries += t.Recoveries;
                    if (t.LongestIdleSeconds > idle) idle = t.LongestIdleSeconds;
                }

                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} trials, {2} goals, {3} held, {4} out, {5} timed out, {6} kicks, {7} recoveries, longest idle {8:F1} s",
                    tier, n, goals, held, outs, time, kicks, recoveries, idle));
            }

            return string.Join("; ", parts);
        }
    }
}
