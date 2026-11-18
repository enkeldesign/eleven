using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>What a penalty benchmark found: kicks taken, and how each ended.</summary>
    public sealed class PenaltyTrial
    {
        public int PerSide, Shootouts;

        /// <summary>Kicks taken, and how each ended: in the goal, saved by the keeper (held or parried), or missed.</summary>
        public int Kicks, Scored, Saved, Missed;

        /// <summary>Of those missed: off the frame, and still short of the line when the kick's time ran out.</summary>
        public int Frame, Short;
    }

    /// <summary>
    /// #122's penalty benchmark: the reference CPU taker against the reference keeper. Each trial is
    /// a knockout level at the end of extra time, so its shootout's kicks are penalties through the
    /// restart sequencer (INV-8), taken by the CPU taker's own command (INV-12) against the keeper's
    /// brain. Every kick is counted: scored, saved, or missed.
    /// </summary>
    public static class PenaltyTrials
    {
        public const int Shootouts = 40;
        public const ulong FirstSeed = 0x122000000UL;

        /// <summary>
        /// <paramref name="shootouts"/> shootouts at <paramref name="perSide"/> a side, each from its own
        /// seed: every kick counted. A kick that ends neither in the goal nor touched by the keeper
        /// is missed; off the frame or short of the line, it is noted as such.
        /// </summary>
        public static PenaltyTrial Run(SimConfig config, int perSide, int shootouts = Shootouts, ulong firstSeed = FirstSeed)
        {
            SimConfig c = config.With("knockout", 1);
            var trial = new PenaltyTrial { PerSide = perSide, Shootouts = shootouts };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            for (int i = 0; i < shootouts; i++)
            {
                var sim = new Simulator(c, LevelAtTheEnd(c, firstSeed + (ulong)i, perSide));
                bool keeperTouched = false, frame = false;
                long limit = 600L * c.TickRate;
                for (long t = 0; t < limit && sim.PeekState().Match.Phase != MatchPhase.FullTime; t++)
                {
                    commands.Clear();
                    KeeperControllers.Append(sim, commands);
                    CpuControllers.Append(sim, commands);
                    RestartControllers.Append(sim, commands);
                    events.Clear();
                    sim.Step(commands, events);
                    foreach (SimEvent e in events)
                    {
                        if (e.Kind == SimEventKind.RestartTaken)
                        {
                            keeperTouched = false;
                            frame = false;
                        }
                        else if (e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.KeeperParried) keeperTouched = true;
                        else if (e.Kind == SimEventKind.HitFrame) frame = true;
                        else if (e.Kind == SimEventKind.ShootoutKick)
                        {
                            trial.Kicks++;
                            if (e.Other == 1) trial.Scored++;
                            else if (keeperTouched) trial.Saved++;
                            else
                            {
                                trial.Missed++;
                                if (frame) trial.Frame++;
                                else if (Short(sim.PeekState(), c)) trial.Short++;
                            }
                        }
                    }
                }
            }

            return trial;
        }

        // A missed kick still on the pitch, inside the touchlines and short of either goal line.
        private static bool Short(SimState s, SimConfig c) =>
            (s.Ball.Position.X < 0 ? -s.Ball.Position.X : s.Ball.Position.X) < c.GoalLineX
            && (s.Ball.Position.Y < 0 ? -s.Ball.Position.Y : s.Ball.Position.Y) < c.TouchlineY;

        // A knockout level at the last step of extra time, every footballer a CPU footballer: its
        // next step starts the shootout.
        private static SimState LevelAtTheEnd(SimConfig c, ulong seed, int perSide)
        {
            SimState s = Squads.Match(c, seed, perSide, bothCpu: true);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.AwardedTeam = 0;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            s.Ball.Position = new Vec3(0, 20, c.BallRadius);
            s.Match.Phase = MatchPhase.ExtraSecondHalf;
            s.Match.ClockTicks = c.ExtraTicks - 1;
            s.Teams[0].Goals = 1;
            s.Teams[1].Goals = 1;
            return s;
        }
    }
}
