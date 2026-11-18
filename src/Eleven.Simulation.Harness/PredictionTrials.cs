using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>What a prediction trial found: how many predicted ball positions were checked, and how many differed.</summary>
    public sealed class PredictionTrial
    {
        public int PerSide;
        public ulong Seed;

        /// <summary>Predictions made: one a tick the ball was free.</summary>
        public int Predictions;

        /// <summary>Predicted positions compared with the simulation's own ball, and those that differed.</summary>
        public long Checked, Differed;

        /// <summary>The first difference, for the report: tick, steps ahead, predicted and actual.</summary>
        public string FirstDifference = "";
    }

    /// <summary>
    /// M5's gate (#130), INV-13's same-sim prediction: in CPU matches, each tick the ball is free, the
    /// CPU's flight prediction (<see cref="BallFlight.Predict"/>, as its brains read it) is compared
    /// with where the simulation then puts the ball, step by step, until something plays it or play
    /// stops. Any difference fails. It reads the state; it changes nothing.
    /// </summary>
    public static class PredictionTrials
    {
        /// <summary>s. How far ahead each prediction runs: the brains' own horizon is shorter.</summary>
        public const double Horizon = 2;

        public static PredictionTrial One(SimConfig config, int perSide, ulong seed, double seconds)
        {
            var t = new PredictionTrial { PerSide = perSide, Seed = seed };
            var sim = new Simulator(config, Squads.Match(config, seed, perSide, bothCpu: true));
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            var open = new List<(long From, List<Vec3> Path)>();
            int steps = (int)(Horizon * config.TickRate);
            long until = (long)(seconds * config.TickRate);
            while (sim.Tick < until)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();

                // Something played the ball, or play stopped: every open prediction ends here.
                if (Played(events) || !Free(s)) open.Clear();

                for (int i = open.Count - 1; i >= 0; i--)
                {
                    (long from, List<Vec3> path) = open[i];
                    int k = (int)(s.Tick - from) - 1;
                    if (k >= path.Count)
                    {
                        open.RemoveAt(i);
                        continue;
                    }

                    t.Checked++;
                    if (path[k].X == s.Ball.Position.X && path[k].Y == s.Ball.Position.Y && path[k].Z == s.Ball.Position.Z) continue;
                    t.Differed++;
                    if (t.FirstDifference.Length == 0) t.FirstDifference = $"tick {from} + {k + 1}: predicted {path[k]}, the ball at {s.Ball.Position}";
                    open.RemoveAt(i);
                }

                // The ball free now: predict it as the CPU's brains do, from the state they read.
                if (Free(s))
                {
                    var path = new List<Vec3>(steps);
                    BallFlight.Predict(config, s.Ball.Position, s.Ball.Velocity, s.Ball.Spin, steps, path, bothGoals: s.Teams.Length == 2);
                    open.Add((s.Tick, path));
                    t.Predictions++;
                }
            }

            return t;
        }

        // Live, in a half, neither at a footballer's feet nor in a keeper's hands, and no one shaping it.
        private static bool Free(SimState s)
        {
            if (s.Restart.Phase != RestartPhase.Live || s.Ball.AttachedTo != 0 || s.Ball.HeldBy != 0) return false;
            if (s.Match.Phase != MatchPhase.FirstHalf && s.Match.Phase != MatchPhase.SecondHalf
                && s.Match.Phase != MatchPhase.ExtraFirstHalf && s.Match.Phase != MatchPhase.ExtraSecondHalf) return false;
            foreach (PlayerState p in s.Players)
            {
                if (p.AftertouchTicks > 0) return false;
            }

            return true;
        }

        // An event that plays the ball, or stops play.
        private static bool Played(List<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                switch (e.Kind)
                {
                    case SimEventKind.Kicked:
                    case SimEventKind.Touched:
                    case SimEventKind.Attached:
                    case SimEventKind.Headed:
                    case SimEventKind.Volleyed:
                    case SimEventKind.KeeperHeld:
                    case SimEventKind.KeeperParried:
                    case SimEventKind.ChallengeWon:
                    case SimEventKind.SlideWon:
                    case SimEventKind.RestartAwarded:
                    case SimEventKind.Goal:
                        return true;
                }
            }

            return false;
        }
    }
}
