// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Eleven.Simulation.Harness
{
    /// <summary>One shot at a keeper (#69): what he did, when, and what came of it.</summary>
    public sealed class KeeperShot
    {
        /// <summary><c>reference</c> or <c>slow</c>.</summary>
        public string Profile = "";

        /// <summary>m from the goal line.</summary>
        public double DistanceMetres;

        /// <summary><c>centre</c>, <c>low-corner</c>, <c>high-corner</c> or <c>lofted</c>.</summary>
        public string Target = "";

        /// <summary>m/s, the kick's launch speed.</summary>
        public double SpeedMps;

        /// <summary><c>held</c>, <c>parried</c> (and kept out), <c>parried-in</c>, <c>goal</c> or <c>miss</c>.</summary>
        public string Outcome = "";

        /// <summary>s from the kick until he could dive: his reaction.</summary>
        public double ReactionSeconds;

        /// <summary>s from the kick to his dive; NaN without one.</summary>
        public double DiveSeconds = double.NaN;

        /// <summary>s from the kick to his contact with the ball; NaN without one.</summary>
        public double ContactSeconds = double.NaN;

        /// <summary>m, the furthest he moved across from where he stood at the kick.</summary>
        public double LateralMetres;

        /// <summary>His phase when he met the ball, or <c>none</c>.</summary>
        public string PhaseAtContact = "none";
    }

    /// <summary>
    /// The keeper's characterisation (#69): shots from the penalty spot and from 16 m, at the
    /// centre, the low and high corners and lofted, at 12, 15 and 18 m/s, against the reference and
    /// a slow keeper played by his brain. A technical reference of the current keeper, not approved
    /// feel (docs/simulation/keeper.md).
    /// </summary>
    public static class KeeperShots
    {
        private const int Shooter = 1, ShooterController = 1, Keeper = 2, KeeperController = 2;
        private const ulong Seed = 0x6EE9E2069UL;

        private static readonly (string Name, double Y, double Height)[] Targets =
        {
            ("centre", 0.4, -1), ("low-corner", 3.2, -1), ("high-corner", 3.0, 2.0), ("lofted", 0.4, -2),
        };

        public static List<KeeperShot> Characterise(SimConfig config)
        {
            var shots = new List<KeeperShot>();
            foreach ((string profile, KeeperCapabilities keeper) in new[] { ("reference", KeeperCapabilities.Reference), ("slow", KeeperCapabilities.Slow) })
            {
                foreach (double distance in new[] { 11.0, 16.0 })
                {
                    foreach ((string name, double y, double height) in Targets)
                    {
                        foreach (double speed in new[] { 12.0, 15.0, 18.0 })
                        {
                            shots.Add(Shoot(config, profile, keeper, distance, name, y, height, speed));
                        }
                    }
                }
            }

            return shots;
        }

        /// <summary>The practice keeper (#69): on the spot his brain stands him on, facing the play.</summary>
        public static KeeperState KeeperState(KeeperCapabilities capabilities) => new KeeperState { PlayerId = Keeper, Capabilities = capabilities };

        private static SimState Start(SimConfig config, KeeperCapabilities keeper, double distance) => new SimState
        {
            Seed = Seed,
            Ball = new BallState { Id = 100, Position = new Vec3(config.GoalLineX - distance, 0, config.BallRadius) },
            Players = new[]
            {
                new PlayerState
                {
                    Id = Shooter, OwnerControllerId = ShooterController, TeamId = 1,
                    Capabilities = PlayerCapabilities.Reference.With("kickError", 0),
                    Position = new Vec2(config.GoalLineX - distance - 0.6, 0),
                },
                new PlayerState
                {
                    Id = Keeper, OwnerControllerId = KeeperController, TeamId = 2, Capabilities = PlayerCapabilities.Reference,
                    Position = new Vec2(config.GoalLineX - 1.5, 0), Heading = Math.PI,
                },
            },
            Keepers = new[] { KeeperState(keeper) },
        };

        private static KeeperShot Shoot(SimConfig config, string profile, KeeperCapabilities keeper, double distance, string target, double y, double height, double speed)
        {
            var shot = new KeeperShot { Profile = profile, DistanceMetres = distance, Target = target, SpeedMps = speed };
            SimState start = Start(config, keeper, distance);
            double strength = speed / (PlayerCapabilities.Reference.KickPower * config.KickSpeed);
            double loft = height == -1 ? 0 : height == -2 ? 0.55 : LoftFor(config, start.Ball.Position, new Vec2(config.GoalLineX, y), speed, height);
            var intent = new KickIntent(new Vec2(config.GoalLineX, y) - start.Ball.Position.Horizontal, strength > 1 ? 1 : strength, loft);

            var sim = new Simulator(config, start);
            var events = new List<SimEvent>();
            var commands = new List<Command>();
            long kicked = -1;
            Vec2 stood = Vec2.Zero;
            bool parried = false, goal = false, crossed = false;
            for (int t = 0; t < 4 * config.TickRate; t++)
            {
                commands.Clear();
                commands.Add(new Command(ShooterController, sim.Tick, Vec2.Zero, t == 0 ? intent : (KickIntent?)null));
                KeeperControllers.Append(sim, commands);
                events.Clear();
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                Vec2 body = s.Players[1].Position;

                foreach (SimEvent e in events)
                {
                    double since = (e.Tick - kicked) * config.TickSeconds;
                    switch (e.Kind)
                    {
                        case SimEventKind.Kicked:
                            kicked = e.Tick;
                            stood = body;
                            shot.ReactionSeconds = Simulator.ReactionTicksFor(keeper, config) * config.TickSeconds;
                            break;
                        case SimEventKind.KeeperDived when double.IsNaN(shot.DiveSeconds):
                            shot.DiveSeconds = since;
                            break;
                        case SimEventKind.KeeperHeld:
                        case SimEventKind.KeeperParried:
                            if (double.IsNaN(shot.ContactSeconds))
                            {
                                shot.ContactSeconds = since;
                                shot.PhaseAtContact = s.Keepers[0].Phase.ToString().ToLowerInvariant();
                            }

                            if (e.Kind == SimEventKind.KeeperHeld) shot.Outcome = "held";
                            else parried = true;
                            break;
                        case SimEventKind.Goal:
                            goal = true;
                            break;
                        case SimEventKind.GoalLineCrossed:
                            crossed = true;
                            break;
                    }
                }

                if (kicked >= 0)
                {
                    double across = Math.Abs(body.Y - stood.Y);
                    if (across > shot.LateralMetres) shot.LateralMetres = across;
                }
            }

            if (shot.Outcome == "") shot.Outcome = goal ? (parried ? "parried-in" : "goal") : parried ? "parried" : crossed ? "miss" : "miss";
            return shot;
        }

        // The lowest loft whose flight, by the simulation's own ball, crosses the goal line at
        // `height`. Scanned up from a flat shot, which stays under the bar until it gets there: a
        // steeper one can meet the bar, so height at the line is not monotonic in loft.
        private static double LoftFor(SimConfig config, Vec3 from, Vec2 to, double speed, double height)
        {
            const double Step = 0.005;
            double lo = 0;
            while (lo < 1 && HeightAtLine(config, from, to, speed, lo + Step) < height) lo += Step;
            double hi = lo + Step;
            for (int i = 0; i < 30; i++)
            {
                double mid = (lo + hi) / 2;
                if (HeightAtLine(config, from, to, speed, mid) < height) lo = mid;
                else hi = mid;
            }

            return (lo + hi) / 2;
        }

        private static double HeightAtLine(SimConfig config, Vec3 from, Vec2 to, double speed, double loft)
        {
            double elevation = loft * config.KickLoft;
            Vec2 line = to - from.Horizontal;
            double length = Maths.SimMath.Sqrt(line.LengthSquared);
            double across = speed * Maths.SimMath.Cos(elevation);
            var velocity = new Vec3(line.X / length * across, line.Y / length * across, speed * Maths.SimMath.Sin(elevation));
            var path = new List<Vec3>();
            BallFlight.Predict(config, from, velocity, Vec3.Zero, 3 * config.TickRate, path);
            Vec3 before = from;
            foreach (Vec3 p in path)
            {
                if (p.X >= config.GoalLineX)
                {
                    double f = (config.GoalLineX - before.X) / (p.X - before.X);
                    return before.Z + (p.Z - before.Z) * f;
                }

                before = p;
            }

            return 0;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<KeeperShot> shots)
        {
            w.Write("config_hash,profile,distance_m,target,speed_mps,outcome,reaction_s,dive_s,contact_s,lateral_m,phase_at_contact\n");
            string hash = config.Hash.ToString("x16", CultureInfo.InvariantCulture);
            foreach (KeeperShot s in shots)
            {
                w.Write(string.Join(",", hash, s.Profile, F(s.DistanceMetres), s.Target, F(s.SpeedMps), s.Outcome, F(s.ReactionSeconds),
                    F(s.DiveSeconds), F(s.ContactSeconds), F(s.LateralMetres), s.PhaseAtContact));
                w.Write('\n');
            }
        }

        /// <summary>The figures a reader wants first, one line: saves by profile and target.</summary>
        public static string KeyNumbers(IReadOnlyList<KeeperShot> shots)
        {
            var parts = new List<string>();
            foreach (string profile in new[] { "reference", "slow" })
            {
                var byTarget = new List<string>();
                foreach ((string name, _, _) in Targets)
                {
                    int saved = 0, all = 0;
                    foreach (KeeperShot s in shots)
                    {
                        if (s.Profile != profile || s.Target != name) continue;
                        all++;
                        if (s.Outcome == "held" || s.Outcome == "parried") saved++;
                    }

                    byTarget.Add($"{name} {saved}/{all}");
                }

                parts.Add($"{profile} saves {string.Join(", ", byTarget)}");
            }

            return string.Join("; ", parts);
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
