// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Eleven.Simulation.Harness
{
    /// <summary>What a lattice of shots did at one end (#82).</summary>
    public sealed class SideCount
    {
        public string End = "";
        public int Shots;
        public int Goals;
        public int GoalsForHome;
        public int GoalsForAway;
        public int FrameHits;
        public int Held;
        public int Parried;

        /// <summary>The run's last state hash of every shot, folded: equal runs give equal digests.</summary>
        public ulong Digest;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} shots, {2} goals (home {3}, away {4}), {5} frame hits, {6} held, {7} parried, digest {8:x16}",
            End, Shots, Goals, GoalsForHome, GoalsForAway, FrameHits, Held, Parried, Digest);
    }

    /// <summary>
    /// The two ends compared (#82): the same lattice of shots at the +x goal and, with the whole
    /// 1v1 world turned half a turn, at the −x goal, each keeper's brain running. Home attacks the
    /// end shot at both times, so the same keeper, away's, faces every shot with the same draws,
    /// and every goal is home's. The counts must be the same at both ends.
    /// </summary>
    public static class SideParity
    {
        /// <summary>m. Every shot is struck from this far out, on the goal's centre line.</summary>
        public const double Distance = 11;

        public const int Steps = 180;

        public static List<SideCount> Measure(SimConfig config)
        {
            var near = new SideCount { End = "+x" };
            var far = new SideCount { End = "-x" };
            for (int iy = -6; iy <= 6; iy++)
            for (int iz = 0; iz <= 3; iz++)
            foreach (double speed in new[] { 14.0, 20.0, 26.0 })
            {
                var aim = new Vec3(config.GoalLineX, iy * 0.75, 0.2 + iz * 0.85);
                Shoot(config, aim, speed, 1, near);
                Shoot(config, aim, speed, -1, far);
            }

            return new List<SideCount> { near, far };
        }

        private static void Shoot(SimConfig config, Vec3 aim, double speed, int end, SideCount count)
        {
            // Home attacks the end shot at: the shot is at +x, or the same world and shot turned at −x.
            SimState state = OneVOne.Start(config, seed: 0xE1182, homeAttackSign: end);
            var from = new Vec3(config.GoalLineX - Distance, 0, config.BallRadius);
            Vec3 line = aim - from;
            Vec3 velocity = line * (speed / Eleven.Simulation.Maths.SimMath.Sqrt(line.LengthSquared)) + new Vec3(0, 0, 1.5);
            state.Ball.Position = new Vec3(end * from.X, end * from.Y, from.Z);
            state.Ball.Velocity = new Vec3(end * velocity.X, end * velocity.Y, velocity.Z);
            state.Players[0].Position = new Vec2(0, end * 25);
            state.Players[2].Position = new Vec2(0, end * -25);

            var sim = new Simulator(config, state);
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            for (int i = 0; i < Steps; i++)
            {
                commands.Clear();
                KeeperControllers.Append(sim, commands);
                sim.Step(commands, events);
            }

            count.Shots++;
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.Goal)
                {
                    count.Goals++;
                    if (e.Other == OneVOne.HomeTeam) count.GoalsForHome++;
                    if (e.Other == OneVOne.AwayTeam) count.GoalsForAway++;
                }
                else if (e.Kind == SimEventKind.HitFrame) count.FrameHits++;
                else if (e.Kind == SimEventKind.KeeperHeld) count.Held++;
                else if (e.Kind == SimEventKind.KeeperParried) count.Parried++;
            }

            count.Digest = unchecked(count.Digest * 1099511628211UL ^ sim.StateHash());
        }
    }
}
