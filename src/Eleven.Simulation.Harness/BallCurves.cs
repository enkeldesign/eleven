// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>What one launch or drop of the ball did, from release until it came to rest.</summary>
    public sealed class BallCurve
    {
        /// <summary><c>kick</c> (grid launch), <c>config-kick</c> (the config's own kick) or <c>drop</c>.</summary>
        public string Kind = "";
        public double SpeedMps;
        public double LoftDegrees;
        public double SpinRadps;
        public double DropMetres;

        /// <summary>Horizontal distance from release to the first bounce; 0 if it never left the ground.</summary>
        public double CarryMetres;

        /// <summary>Highest clearance of the ball's underside above the pitch.</summary>
        public double PeakMetres;

        /// <summary>Time from release to the first bounce.</summary>
        public double FlightSeconds;

        public int Bounces;

        /// <summary>Horizontal distance from the first bounce (or release, if none) to where it stopped.</summary>
        public double RolloutMetres;

        /// <summary>Horizontal distance from release to where it stopped.</summary>
        public double TotalMetres;

        /// <summary>Sideways distance at rest from the launch line (+y, left, for spin about +z).</summary>
        public double LateralMetres;

        /// <summary>Highest clearance after the first bounce.</summary>
        public double ReboundMetres;

        /// <summary>Time to come to rest, or null if it hadn't after <see cref="BallCurves.MaxSeconds"/>.</summary>
        public double? RestSeconds;
    }

    /// <summary>
    /// Characterises the ball over a documented grid of launches and drops, under any config, so
    /// candidate feels can be compared (#24). The ball is launched from the ground along +x with no
    /// players on the pitch; only the ball model runs.
    /// </summary>
    public static class BallCurves
    {
        public static readonly ReadOnlyCollection<double> SpeedsMps = Array.AsReadOnly(new double[] { 8, 12, 16, 20, 25, 30 });
        public static readonly ReadOnlyCollection<double> LoftsDegrees = Array.AsReadOnly(new double[] { 0, 5, 10, 20, 30, 45 });
        public static readonly ReadOnlyCollection<double> SpinsRadps = Array.AsReadOnly(new double[] { 0, 10, 20 });
        public static readonly ReadOnlyCollection<double> DropsMetres = Array.AsReadOnly(new double[] { 0.5, 1, 2, 5, 10 });
        public const double MaxSeconds = 120;

        private const int BallId = 100;

        /// <summary>The whole grid, then the config's own kick, then the drops.</summary>
        public static List<BallCurve> Characterise(SimConfig config)
        {
            var curves = new List<BallCurve>();
            foreach (double speed in SpeedsMps)
            foreach (double loft in LoftsDegrees)
            foreach (double spin in SpinsRadps)
            {
                curves.Add(Kick(config, "kick", speed, loft, spin));
            }

            curves.Add(ConfigKick(config));
            foreach (double drop in DropsMetres) curves.Add(Drop(config, drop));
            return curves;
        }

        /// <summary>The config's kick at its largest sidespin (real kicks draw 0.5–1 × that).</summary>
        public static BallCurve ConfigKick(SimConfig config) =>
            Kick(config, "config-kick", config.KickSpeed, config.KickLoft * 180 / Math.PI, config.KickSideSpin);

        public static BallCurve Kick(SimConfig config, string kind, double speedMps, double loftDegrees, double spinRadps)
        {
            double loft = loftDegrees * Math.PI / 180;
            var ball = new BallState
            {
                Id = BallId,
                Position = new Vec3(0, 0, config.BallRadius),
                Velocity = new Vec3(speedMps * SimMath.Cos(loft), 0, speedMps * SimMath.Sin(loft)),
                Spin = new Vec3(0, 0, spinRadps),
            };

            BallCurve curve = Run(config, ball);
            curve.Kind = kind;
            curve.SpeedMps = speedMps;
            curve.LoftDegrees = loftDegrees;
            curve.SpinRadps = spinRadps;
            return curve;
        }

        public static BallCurve Drop(SimConfig config, double metres)
        {
            var ball = new BallState { Id = BallId, Position = new Vec3(0, 0, config.BallRadius + metres) };
            BallCurve curve = Run(config, ball);
            curve.Kind = "drop";
            curve.DropMetres = metres;
            return curve;
        }

        private static BallCurve Run(SimConfig config, BallState ball)
        {
            var sim = new Simulator(config, new SimState { Ball = ball });
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            var curve = new BallCurve();
            double radius = config.BallRadius;
            Vec2 start = ball.Position.Horizontal;
            Vec2 firstContact = start;
            long firstBounceTick = -1;
            long maxTicks = (long)(MaxSeconds * config.TickRate);

            curve.PeakMetres = ball.Position.Z - radius;
            for (long t = 0; t < maxTicks; t++)
            {
                events.Clear();
                sim.Step(commands, events);
                BallState b = sim.PeekState().Ball;
                double clearance = b.Position.Z - radius;
                if (clearance > curve.PeakMetres) curve.PeakMetres = clearance;
                if (firstBounceTick >= 0 && clearance > curve.ReboundMetres) curve.ReboundMetres = clearance;

                foreach (SimEvent e in events)
                {
                    if (e.Kind != SimEventKind.Bounced) continue;
                    curve.Bounces++;
                    if (firstBounceTick < 0)
                    {
                        firstBounceTick = sim.Tick;
                        firstContact = b.Position.Horizontal;
                        curve.CarryMetres = Distance(start, firstContact);
                        curve.FlightSeconds = sim.Tick * config.TickSeconds;
                    }
                }

                if (b.Velocity.X == 0 && b.Velocity.Y == 0 && b.Velocity.Z == 0 && b.Position.Z == radius)
                {
                    curve.RestSeconds = sim.Tick * config.TickSeconds;
                    break;
                }
            }

            BallState end = sim.PeekState().Ball;
            curve.RolloutMetres = Distance(firstContact, end.Position.Horizontal);
            curve.TotalMetres = Distance(start, end.Position.Horizontal);
            curve.LateralMetres = end.Position.Y - start.Y;
            return curve;
        }

        private static double Distance(Vec2 a, Vec2 b) => SimMath.Sqrt((a - b).LengthSquared);

        public const string CsvHeader =
            "config_hash,kind,speed_mps,loft_deg,spin_radps,drop_m,carry_m,peak_m,flight_s,bounces,rollout_m,total_m,lateral_m,rebound_m,rest_s";

        /// <summary>One row per curve. Every row carries the config hash, so a plot traces back to its settings.</summary>
        public static void WriteCsv(TextWriter writer, SimConfig config, IEnumerable<BallCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine(CsvHeader);
            foreach (BallCurve c in curves)
            {
                writer.WriteLine(string.Join(",",
                    config.Hash.ToString("x16", CultureInfo.InvariantCulture),
                    c.Kind,
                    F(c.SpeedMps), F(c.LoftDegrees), F(c.SpinRadps), F(c.DropMetres),
                    F(c.CarryMetres), F(c.PeakMetres), F(c.FlightSeconds),
                    c.Bounces.ToString(CultureInfo.InvariantCulture),
                    F(c.RolloutMetres), F(c.TotalMetres), F(c.LateralMetres), F(c.ReboundMetres),
                    c.RestSeconds.HasValue ? F(c.RestSeconds.Value) : ""));
            }
        }

        /// <summary>Who made the curves (build, config hash, every tunable value, the overrides) and the key numbers.</summary>
        public static void WriteSummary(TextWriter writer, SimConfig config, IReadOnlyCollection<string> overridden, IReadOnlyList<BallCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine("# eleven ball curves v1");
            writer.WriteLine($"build: {SimulationInfo.BuildId}");
            writer.WriteLine($"config-hash: {config.Hash:x16}");
            writer.WriteLine($"config-format: {SimConfig.FormatVersion}");
            writer.WriteLine("status: technical reference of the current values, not approved feel");
            foreach (TunableParameter p in SimConfig.Tunables)
            {
                string mark = Contains(overridden, p.Name) ? "  (override)" : "";
                writer.WriteLine($"{p.Name}: {F(config.Get(p.Name))} {p.Unit}{mark}");
            }

            writer.WriteLine($"grid: speed {List(SpeedsMps)} m/s x loft {List(LoftsDegrees)} deg x spin {List(SpinsRadps)} rad/s; drops {List(DropsMetres)} m; launched from the ground along +x, until rest or {MaxSeconds:F0} s");
            writer.WriteLine($"key: {KeyNumbers(config, curves)}");
        }

        /// <summary>The numbers worth a glance: the config's own kick, a hard low drive and a 2 m drop.</summary>
        public static string KeyNumbers(SimConfig config, IReadOnlyList<BallCurve> curves)
        {
            BallCurve? configKick = Find(curves, c => c.Kind == "config-kick");
            BallCurve? drive = Find(curves, c => c.Kind == "kick" && c.SpeedMps == 30 && c.LoftDegrees == 10 && c.SpinRadps == 0);
            BallCurve? drop = Find(curves, c => c.Kind == "drop" && c.DropMetres == 2);
            var parts = new List<string>();
            if (configKick != null)
            {
                parts.Add($"config kick {F1(configKick.SpeedMps)} m/s at {F1(configKick.LoftDegrees)} deg, spin {F1(configKick.SpinRadps)}: " +
                    $"carry {F1(configKick.CarryMetres)} m, peak {F1(configKick.PeakMetres)} m, total {F1(configKick.TotalMetres)} m, lateral {F1(configKick.LateralMetres)} m");
            }

            if (drive != null) parts.Add($"30 m/s at 10 deg: carry {F1(drive.CarryMetres)} m, total {F1(drive.TotalMetres)} m");
            if (drop != null) parts.Add($"2 m drop: rebound {F(drop.ReboundMetres)} m, {drop.Bounces} bounces");
            parts.Add($"airDrag {F(config.AirDrag)}");
            return string.Join("; ", parts);
        }

        private static BallCurve? Find(IReadOnlyList<BallCurve> curves, Func<BallCurve, bool> match)
        {
            foreach (BallCurve c in curves)
            {
                if (match(c)) return c;
            }

            return null;
        }

        private static bool Contains(IReadOnlyCollection<string> names, string name)
        {
            foreach (string n in names)
            {
                if (n == name) return true;
            }

            return false;
        }

        private static string List(IEnumerable<double> values)
        {
            var parts = new List<string>();
            foreach (double v in values) parts.Add(v.ToString("0.##", CultureInfo.InvariantCulture));
            return string.Join("/", parts);
        }

        // Adding +0 turns −0 into +0, so a sign-less zero never prints as "-0.000".
        private static string F(double value) => (value + 0.0).ToString("0.000", CultureInfo.InvariantCulture);

        private static string F1(double value) => (value + 0.0).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
