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
    /// <summary>
    /// How one capability profile moves: from rest to top speed, braking from top speed, and
    /// turning at top speed. Every turn and the stop are measured from top speed along +x.
    /// </summary>
    public sealed class MovementCurve
    {
        /// <summary><c>profile</c> (the profile asked for) or <c>grid</c> (the starting-range grid).</summary>
        public string Kind = "";
        public double MaxSpeedMps;
        public double AccelerationMps2;

        /// <summary>From rest, full stick: time until running at top speed.</summary>
        public double FullSpeedSeconds;

        /// <summary>From rest, full stick: distance covered by the time top speed is reached.</summary>
        public double FullSpeedMetres;

        /// <summary>From rest, full stick: time to cover 5 m.</summary>
        public double Split5mSeconds;

        /// <summary>From rest, full stick: time to cover 10 m.</summary>
        public double Split10mSeconds;

        /// <summary>From top speed, stick released: time to a standstill.</summary>
        public double StopSeconds;

        /// <summary>From top speed, stick released: distance covered before the standstill.</summary>
        public double StopMetres;

        /// <summary>From top speed, full stick turned 45°: time until running at top speed on the new line.</summary>
        public double Turn45Seconds;

        /// <summary>
        /// How far the player is, once on the new line, from where a player who turned instantly
        /// would be: what the turn costs in metres.
        /// </summary>
        public double Turn45LagMetres;

        /// <summary>The lowest speed through the 45° turn.</summary>
        public double Turn45MinSpeedMps;

        public double Turn90Seconds;
        public double Turn90LagMetres;
        public double Turn90MinSpeedMps;

        /// <summary>Full stick reversed: through a standstill to top speed the other way.</summary>
        public double Turn180Seconds;

        public double Turn180LagMetres;
    }

    /// <summary>
    /// Headless movement characterisation (#36): one player under the real simulator, across the
    /// research starting ranges of <see cref="PlayerCapabilities.Parameters"/>. A technical
    /// reference of the current model and values, not approved feel (docs/simulation/movement.md).
    /// </summary>
    public static class MovementCurves
    {
        private const int PlayerId = 1;
        private const int ControllerId = 1;

        /// <summary>
        /// Longest a single measurement may run. The slowest change any valid profile makes, a
        /// reversal at 15 m/s with 1 m/s², takes 30 s.
        /// </summary>
        public const double MaxSeconds = 60;

        /// <summary>Velocity within this of the stick's target counts as reached, m/s.</summary>
        private const double Reached = 1e-9;

        /// <summary>The grid's top speeds: four evenly spaced across the starting range, ends included.</summary>
        public static readonly ReadOnlyCollection<double> MaxSpeedsMps = AcrossStartingRange(PlayerCapabilities.Parameters[0]);

        /// <summary>The grid's accelerations: four evenly spaced across the starting range, ends included.</summary>
        public static readonly ReadOnlyCollection<double> AccelerationsMps2 = AcrossStartingRange(PlayerCapabilities.Parameters[1]);

        private static readonly double[] TurnsDegrees = { 45, 90, 180 };

        private static ReadOnlyCollection<double> AcrossStartingRange(TunableParameter p)
        {
            double min = p.StartMin ?? p.Min, max = p.StartMax ?? p.Max;
            var values = new double[4];
            for (int i = 0; i < values.Length; i++) values[i] = min + i * (max - min) / (values.Length - 1);
            return Array.AsReadOnly(values);
        }

        /// <summary>The profile asked for first, then the grid, speed-major.</summary>
        public static List<MovementCurve> Characterise(SimConfig config, PlayerCapabilities profile)
        {
            var curves = new List<MovementCurve> { Measure(config, profile, "profile") };
            foreach (double speed in MaxSpeedsMps)
            {
                foreach (double acceleration in AccelerationsMps2)
                {
                    curves.Add(Measure(config, profile.With("maxSpeed", speed).With("acceleration", acceleration), "grid"));
                }
            }

            return curves;
        }

        public static MovementCurve Measure(SimConfig config, PlayerCapabilities capabilities, string kind)
        {
            double top = capabilities.MaxSpeed;
            var curve = new MovementCurve { Kind = kind, MaxSpeedMps = top, AccelerationMps2 = capabilities.Acceleration };

            Response start = Respond(config, capabilities, Vec2.Zero, new Vec2(1, 0), splits: true);
            curve.FullSpeedSeconds = start.Seconds;
            curve.FullSpeedMetres = start.Position.X;
            curve.Split5mSeconds = start.Split5Seconds;
            curve.Split10mSeconds = start.Split10Seconds;

            Response stop = Respond(config, capabilities, new Vec2(top, 0), Vec2.Zero, splits: false);
            curve.StopSeconds = stop.Seconds;
            curve.StopMetres = stop.LagMetres;

            foreach (double degrees in TurnsDegrees)
            {
                double radians = degrees * Math.PI / 180;
                // Exact unit sticks along the axes, so a reversal is a straight line in velocity.
                Vec2 stick = degrees == 90 ? new Vec2(0, 1)
                    : degrees == 180 ? new Vec2(-1, 0)
                    : new Vec2(SimMath.Cos(radians), SimMath.Sin(radians));
                Response turn = Respond(config, capabilities, new Vec2(top, 0), stick, splits: false);
                switch (degrees)
                {
                    case 45:
                        curve.Turn45Seconds = turn.Seconds;
                        curve.Turn45LagMetres = turn.LagMetres;
                        curve.Turn45MinSpeedMps = turn.MinSpeed;
                        break;
                    case 90:
                        curve.Turn90Seconds = turn.Seconds;
                        curve.Turn90LagMetres = turn.LagMetres;
                        curve.Turn90MinSpeedMps = turn.MinSpeed;
                        break;
                    default:
                        curve.Turn180Seconds = turn.Seconds;
                        curve.Turn180LagMetres = turn.LagMetres;
                        break;
                }
            }

            return curve;
        }

        private readonly struct Response
        {
            public Response(double seconds, Vec2 position, double lagMetres, double minSpeed, double split5Seconds, double split10Seconds)
            {
                Seconds = seconds;
                Position = position;
                LagMetres = lagMetres;
                MinSpeed = minSpeed;
                Split5Seconds = split5Seconds;
                Split10Seconds = split10Seconds;
            }

            /// <summary>Time until the velocity reached the stick's target.</summary>
            public double Seconds { get; }

            /// <summary>Where the player was then, from a start at the origin.</summary>
            public Vec2 Position { get; }

            /// <summary>Distance from where an instant response (target velocity from the first tick) would have been.</summary>
            public double LagMetres { get; }

            public double MinSpeed { get; }
            public double Split5Seconds { get; }
            public double Split10Seconds { get; }
        }

        // One player from the origin with `startVelocity`, holding `stick` every tick, until the
        // velocity reaches the stick's target (and, with splits, 10 m are covered). The ball is far
        // out of the way.
        private static Response Respond(SimConfig config, PlayerCapabilities capabilities, Vec2 startVelocity, Vec2 stick, bool splits)
        {
            var sim = new Simulator(config, new SimState
            {
                Ball = new BallState { Id = 100, Position = new Vec3(0, 1e4, config.BallRadius) },
                Players = new[]
                {
                    new PlayerState { Id = PlayerId, OwnerControllerId = ControllerId, Capabilities = capabilities, Velocity = startVelocity },
                },
            });

            Vec2 target = stick * capabilities.MaxSpeed;
            var commands = new Command[1];
            var events = new List<SimEvent>();
            int maxTicks = (int)(MaxSeconds * config.TickRate);
            double minSpeed = Length(startVelocity), previousDistance = 0;
            double? reachedSeconds = null, split5 = null, split10 = null;
            Vec2 reachedAt = Vec2.Zero;

            for (int tick = 1; tick <= maxTicks; tick++)
            {
                commands[0] = new Command(ControllerId, sim.Tick, stick);
                sim.Step(commands, events);
                PlayerState player = sim.PeekState().Players[0];
                double seconds = tick / (double)config.TickRate;

                if (reachedSeconds == null)
                {
                    minSpeed = Math.Min(minSpeed, Length(player.Velocity));
                    if ((player.Velocity - target).LengthSquared <= Reached * Reached)
                    {
                        reachedSeconds = seconds;
                        reachedAt = player.Position;
                    }
                }

                // Within a tick the player moves at the tick's new velocity, so the crossing
                // interpolates exactly.
                double distance = Length(player.Position);
                if (split5 == null && distance >= 5) split5 = Crossing(5, previousDistance, distance, tick, config.TickRate);
                if (split10 == null && distance >= 10) split10 = Crossing(10, previousDistance, distance, tick, config.TickRate);
                previousDistance = distance;

                if (reachedSeconds.HasValue && (!splits || split10.HasValue))
                {
                    double lag = Length(reachedAt - target * reachedSeconds.Value);
                    return new Response(reachedSeconds.Value, reachedAt, lag, minSpeed, split5 ?? 0, split10 ?? 0);
                }
            }

            throw new InvalidOperationException($"Movement did not settle within {MaxSeconds} s for {capabilities.MaxSpeed} m/s, {capabilities.Acceleration} m/s².");
        }

        private static double Crossing(double metres, double before, double after, int tick, int tickRate) =>
            (tick - 1 + (metres - before) / (after - before)) / tickRate;

        private static double Length(Vec2 v) => SimMath.Sqrt(v.LengthSquared);

        public const string CsvHeader =
            "config_hash,kind,max_speed_mps,acceleration_mps2,full_speed_s,full_speed_m,split_5m_s,split_10m_s,stop_s,stop_m," +
            "turn45_s,turn45_lag_m,turn45_min_speed_mps,turn90_s,turn90_lag_m,turn90_min_speed_mps,turn180_s,turn180_lag_m";

        /// <summary>One row per profile. Every row carries the config hash, so a plot traces back to its settings.</summary>
        public static void WriteCsv(TextWriter writer, SimConfig config, IEnumerable<MovementCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine(CsvHeader);
            foreach (MovementCurve c in curves)
            {
                writer.WriteLine(string.Join(",",
                    config.Hash.ToString("x16", CultureInfo.InvariantCulture),
                    c.Kind,
                    F(c.MaxSpeedMps), F(c.AccelerationMps2),
                    F(c.FullSpeedSeconds), F(c.FullSpeedMetres), F(c.Split5mSeconds), F(c.Split10mSeconds),
                    F(c.StopSeconds), F(c.StopMetres),
                    F(c.Turn45Seconds), F(c.Turn45LagMetres), F(c.Turn45MinSpeedMps),
                    F(c.Turn90Seconds), F(c.Turn90LagMetres), F(c.Turn90MinSpeedMps),
                    F(c.Turn180Seconds), F(c.Turn180LagMetres)));
            }
        }

        /// <summary>Who made the curves (build, config, the profile's capabilities written exactly) and the key numbers.</summary>
        public static void WriteSummary(TextWriter writer, SimConfig config, PlayerCapabilities profile, IReadOnlyList<MovementCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine("# eleven movement curves v1");
            writer.WriteLine($"build: {SimulationInfo.BuildId}");
            writer.WriteLine($"config-hash: {config.Hash:x16}");
            writer.WriteLine($"config-format: {SimConfig.FormatVersion}");
            writer.WriteLine($"tick-rate: {config.TickRate}");
            writer.WriteLine("status: technical reference of the current values, not approved feel");
            foreach (TunableParameter p in PlayerCapabilities.Parameters)
            {
                writer.WriteLine($"{p.Name}: {profile.Get(p.Name).ToString("R", CultureInfo.InvariantCulture)} {p.Unit}");
            }

            writer.WriteLine($"grid: max speed {List(MaxSpeedsMps)} m/s x acceleration {List(AccelerationsMps2)} m/s² (the research starting ranges); " +
                "one player from the origin along +x; stop and turns from top speed, full stick");
            writer.WriteLine($"key: {KeyNumbers(curves)}");
        }

        /// <summary>The profile's numbers worth a glance.</summary>
        public static string KeyNumbers(IReadOnlyList<MovementCurve> curves)
        {
            MovementCurve c = curves[0];
            return $"{F1(c.MaxSpeedMps)} m/s, {F1(c.AccelerationMps2)} m/s²: top speed in {F2(c.FullSpeedSeconds)} s; 10 m in {F2(c.Split10mSeconds)} s; " +
                $"stops in {F2(c.StopSeconds)} s over {F1(c.StopMetres)} m; turns 90° in {F2(c.Turn90Seconds)} s (lag {F1(c.Turn90LagMetres)} m), " +
                $"reverses in {F2(c.Turn180Seconds)} s (lag {F1(c.Turn180LagMetres)} m)";
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

        private static string F2(double value) => (value + 0.0).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
