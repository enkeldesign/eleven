// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One contact (#94): a full kick along +x at a ball at one height, coming at one speed, by one heading.</summary>
    public sealed class AirCurve
    {
        /// <summary>m. The ball's centre at contact.</summary>
        public double HeightMetres;

        /// <summary>m/s. The ball's speed toward the footballer at contact.</summary>
        public double IncomingMps;

        public double Heading;

        /// <summary><c>kick</c>, <c>volley</c>, <c>header</c> or <c>miss</c>.</summary>
        public string Contact = "";

        /// <summary>m/s. The ball's speed as it leaves; NaN for a miss.</summary>
        public double SpeedMps = double.NaN;

        /// <summary>rad. The largest and the mean angle off the line over the seeds; NaN for a miss.</summary>
        public double MaxErrorRadians = double.NaN;

        public double MeanErrorRadians = double.NaN;
    }

    /// <summary>
    /// The air-contact characterisation (#94): a footballer a step from the end of a full kick's
    /// wind-up, the ball 0.4 m in front at each height and coming at each speed, for three heading
    /// profiles. It records which contact the height makes, how fast the ball leaves and how far off
    /// the line it goes over <see cref="Seeds"/> seeds. The kicker's own error is the reference's.
    /// </summary>
    public static class AirCurves
    {
        public const int Seeds = 32;

        public static readonly double[] Heights = { 0.11, 0.3, 0.5, 0.7, 0.9, 1.1, 1.3, 1.5, 1.7, 1.9, 2.1, 2.3, 2.5, 2.7 };

        public static readonly double[] Incoming = { 0, 5, 10, 15, 20 };

        public static readonly double[] Headings = { 0, 0.6, 1 };

        private const int PlayerId = 1, ControllerId = 1;

        public static List<AirCurve> Characterise(SimConfig config, PlayerCapabilities profile)
        {
            var curves = new List<AirCurve>();
            foreach (double heading in Headings)
            {
                foreach (double height in Heights)
                {
                    foreach (double incoming in Incoming) curves.Add(Measure(config, profile, height, incoming, heading));
                }
            }

            return curves;
        }

        private static AirCurve Measure(SimConfig config, PlayerCapabilities profile, double height, double incoming, double heading)
        {
            var curve = new AirCurve { HeightMetres = height, IncomingMps = incoming, Heading = heading };
            double sum = 0, most = 0, speed = 0;
            int connected = 0;
            var events = new List<SimEvent>();
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                PlayerCapabilities caps = profile;
                caps.Heading = heading;
                var sim = new Simulator(config, new SimState
                {
                    Seed = seed,
                    Ball = new BallState { Id = 100, Position = new Vec3(0.4, 0, height), Velocity = new Vec3(-incoming, 0, 0) },
                    Players = new[]
                    {
                        new PlayerState { Id = PlayerId, OwnerControllerId = ControllerId, Capabilities = caps, WindUpTicks = 1, WindUpStrength = 1 },
                    },
                });
                events.Clear();
                sim.Step(Array.Empty<Command>(), events);
                string kind = "miss";
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Kicked && kind == "miss") kind = "kick";
                    if (e.Kind == SimEventKind.Volleyed) kind = "volley";
                    if (e.Kind == SimEventKind.Headed) kind = "header";
                }

                curve.Contact = kind;
                if (kind == "miss") return curve;
                Vec3 v = sim.PeekState().Ball.Velocity;
                double off = Math.Abs(SimMath.Atan2(v.Y, v.X));
                sum += off;
                most = Math.Max(most, off);
                speed = SimMath.Sqrt(v.LengthSquared);
                connected++;
            }

            curve.SpeedMps = speed;
            curve.MaxErrorRadians = most;
            curve.MeanErrorRadians = sum / connected;
            return curve;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<AirCurve> curves)
        {
            w.WriteLine("config_hash,heading,height_m,incoming_mps,contact,speed_mps,max_error_rad,mean_error_rad");
            foreach (AirCurve c in curves)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1:F1},{2:F2},{3:F0},{4},{5},{6},{7}",
                    config.Hash, c.Heading, c.HeightMetres, c.IncomingMps, c.Contact, Number(c.SpeedMps, "F2"), Number(c.MaxErrorRadians, "F4"), Number(c.MeanErrorRadians, "F4")));
            }
        }

        private static string Number(double value, string format) => double.IsNaN(value) ? "" : value.ToString(format, CultureInfo.InvariantCulture);

        public static string KeyNumbers(IReadOnlyList<AirCurve> curves)
        {
            int Count(string kind) => curves.Count(c => c.Contact == kind);
            AirCurve? header = curves.FirstOrDefault(c => c.Contact == "header" && c.Heading == 0.6 && c.IncomingMps == 0);
            AirCurve? kick = curves.FirstOrDefault(c => c.Contact == "kick" && c.IncomingMps == 0);
            AirCurve? fastVolley = curves.LastOrDefault(c => c.Contact == "volley" && c.Heading == 0.6);
            return string.Format(CultureInfo.InvariantCulture, "air contact: {0} kicks, {1} volleys, {2} headers, {3} misses; header {4:F1} m/s against a kick's {5:F1}; volley at {6:F0} m/s off by up to {7:F3} rad",
                Count("kick"), Count("volley"), Count("header"), Count("miss"), header?.SpeedMps ?? double.NaN, kick?.SpeedMps ?? double.NaN,
                fastVolley?.IncomingMps ?? double.NaN, fastVolley?.MaxErrorRadians ?? double.NaN);
        }
    }

    /// <summary>How air contact turns up in CPU matches (#94), at one side size.</summary>
    public sealed class AirTrial
    {
        public int PerSide;
        public int Matches;
        public int Kicks;
        public int Volleys;
        public int Headers;
        public int Goals;

        /// <summary>Goals by the last contact before them: a kick, a volley, a header; any touch after it makes the goal the touch's.</summary>
        public int GoalsFromKicks;
        public int GoalsFromVolleys;
        public int GoalsFromHeaders;
    }

    /// <summary>
    /// Air contact in play (#94): CPU-against-CPU matches at the reference halves, at one and two a
    /// side, counting every connecting kick by its kind and the goals each kind scored.
    /// </summary>
    public static class AirTrials
    {
        public const int Seeds = 12;

        public static List<AirTrial> Run(SimConfig config, int seeds = Seeds)
        {
            var trials = new List<AirTrial>();
            foreach (int perSide in new[] { 1, 2 }) trials.Add(One(config, perSide, seeds));
            return trials;
        }

        public static AirTrial One(SimConfig config, int perSide, int seeds)
        {
            var trial = new AirTrial { PerSide = perSide, Matches = seeds };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            for (int i = 1; i <= seeds; i++)
            {
                var sim = new Simulator(config, Squads.Match(config, 0x94000000UL + (ulong)i, perSide, bothCpu: true));
                int limit = 3 * 2 * config.HalfTicks;
                string last = "";
                while (sim.Tick < limit && sim.PeekState().Match.Phase != MatchPhase.FullTime)
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
                            case SimEventKind.Kicked:
                                trial.Kicks++;
                                last = "kick";
                                break;
                            case SimEventKind.Volleyed:
                                trial.Kicks--;
                                trial.Volleys++;
                                last = "volley";
                                break;
                            case SimEventKind.Headed:
                                trial.Kicks--;
                                trial.Headers++;
                                last = "header";
                                break;
                            case SimEventKind.Touched:
                            case SimEventKind.KeeperHeld:
                            case SimEventKind.ChallengeWon:
                                last = "";
                                break;
                            case SimEventKind.Goal:
                                trial.Goals++;
                                if (last == "kick") trial.GoalsFromKicks++;
                                else if (last == "volley") trial.GoalsFromVolleys++;
                                else if (last == "header") trial.GoalsFromHeaders++;
                                break;
                        }
                    }
                }
            }

            return trial;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<AirTrial> trials)
        {
            w.WriteLine("config_hash,per_side,matches,kicks,volleys,headers,goals,goals_from_kicks,goals_from_volleys,goals_from_headers");
            foreach (AirTrial t in trials)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                    config.Hash, t.PerSide, t.Matches, t.Kicks, t.Volleys, t.Headers, t.Goals, t.GoalsFromKicks, t.GoalsFromVolleys, t.GoalsFromHeaders));
            }
        }

        public static string KeyNumbers(IReadOnlyList<AirTrial> trials) =>
            string.Join("; ", trials.Select(t => string.Format(CultureInfo.InvariantCulture,
                "{0}v{0}: {1} kicks, {2} volleys, {3} headers; {4} goals, {5} from volleys, {6} from headers", t.PerSide, t.Kicks, t.Volleys, t.Headers, t.Goals, t.GoalsFromVolleys, t.GoalsFromHeaders)));
    }
}
