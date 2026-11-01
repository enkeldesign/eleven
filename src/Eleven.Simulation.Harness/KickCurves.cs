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
    /// What one manual kick does: the launch the model gives the intent, and how the ball then
    /// travels, with any aftertouch held from the step after the intent to the end.
    /// </summary>
    public sealed class KickCurve
    {
        /// <summary><c>launch</c> (the strength × loft grid) or <c>aftertouch</c> (inputs across the starting window).</summary>
        public string Kind = "";

        /// <summary>For aftertouch rows, the kick shaped: <c>ground-pass</c> or <c>lofted</c>.</summary>
        public string Case = "";

        public ulong ConfigHash;
        public double Strength;
        public double Loft;

        /// <summary><c>none</c>, <c>left</c>, <c>back</c> or <c>forward</c> of the intended line, at full stick.</summary>
        public string Input = "";

        public double WindowSeconds;

        /// <summary>Strength × the kicker's kick power × the config's hardest kick.</summary>
        public double LaunchSpeedMps;

        /// <summary>Loft × the config's maximum loft.</summary>
        public double LaunchElevationDegrees;

        /// <summary>Measured: the launch's deviation from the intended line, the kicker's execution error.</summary>
        public double ErrorDegrees;

        /// <summary>Measured: how far the horizontal velocity turned from the launch to the window's end, + left.</summary>
        public double TurnDegrees;

        /// <summary>Measured: sidespin about +z at the window's end.</summary>
        public double SpinRadps;

        /// <summary>Measured: vertical speed at the window's end minus that of the same kick with no input.</summary>
        public double LiftMps;

        public double CarryMetres;
        public double PeakMetres;
        public double FlightSeconds;
        public double TotalMetres;

        /// <summary>Where the ball comes to rest, off the launch line, + left.</summary>
        public double LateralMetres;

        /// <summary>Time to come to rest, or null if it hadn't after <see cref="KickCurves.MaxSeconds"/>.</summary>
        public double? RestSeconds;
    }

    /// <summary>
    /// Headless characterisation of the manual kick (#37) under the real simulator: one player at
    /// rest, facing +x, kicks a ball at rest just ahead along +x. A technical reference of the
    /// current model and values, not approved feel (docs/simulation/kick.md).
    /// </summary>
    public static class KickCurves
    {
        /// <summary>Longest a kick is followed; one still moving then has no rest time.</summary>
        public const double MaxSeconds = 120;

        public static readonly ReadOnlyCollection<double> Strengths = Array.AsReadOnly(new[] { 0.25, 0.5, 0.75, 1.0 });
        public static readonly ReadOnlyCollection<double> Lofts = Array.AsReadOnly(new[] { 0.0, 0.25, 0.5, 0.75, 1.0 });

        /// <summary>Aftertouch windows: four evenly spaced across the starting range, ends included.</summary>
        public static readonly ReadOnlyCollection<double> WindowsSeconds = Array.AsReadOnly(new[] { 0.15, 0.20, 0.25, 0.30 });

        /// <summary>The kicks aftertouch shapes: a ground pass, and a lofted kick.</summary>
        public static readonly ReadOnlyCollection<(string Name, double Strength, double Loft)> ShapedKicks =
            Array.AsReadOnly(new[] { ("ground-pass", 0.6, 0.0), ("lofted", 1.0, 0.4) });

        private static readonly (string Name, Vec2 Stick)[] Inputs =
        {
            ("none", Vec2.Zero), ("left", new Vec2(0, 1)), ("back", new Vec2(-1, 0)), ("forward", new Vec2(1, 0)),
        };

        private const int PlayerId = 1;
        private const int ControllerId = 1;

        /// <summary>
        /// The windows aftertouch is measured across: the starting-range samples, plus the config's
        /// own window if it is not one of them, in ascending order.
        /// </summary>
        public static List<double> WindowsFor(SimConfig config)
        {
            var windows = new List<double>(WindowsSeconds);
            if (!windows.Contains(config.AftertouchSeconds)) windows.Add(config.AftertouchSeconds);
            windows.Sort();
            return windows;
        }

        /// <summary>
        /// The launch grid under <paramref name="config"/>, then every input on every shaped kick
        /// across <see cref="WindowsFor"/>.
        /// </summary>
        public static List<KickCurve> Characterise(SimConfig config, PlayerCapabilities profile)
        {
            var curves = new List<KickCurve>();
            foreach (double strength in Strengths)
            foreach (double loft in Lofts)
            {
                curves.Add(Measure(config, profile, "launch", "", strength, loft, Inputs[0], out _));
            }

            foreach ((string name, double strength, double loft) in ShapedKicks)
            foreach (double window in WindowsFor(config))
            {
                SimConfig windowed = config.With("aftertouchSeconds", window);
                double? unshaped = null;
                foreach ((string, Vec2) input in Inputs)
                {
                    KickCurve curve = Measure(windowed, profile, "aftertouch", name, strength, loft, input, out double vertical);
                    unshaped ??= vertical;
                    curve.LiftMps = vertical - unshaped.Value;
                    curves.Add(curve);
                }
            }

            return curves;
        }

        // `vertical` is the ball's vertical speed at the window's end, for comparing with the unshaped kick.
        private static KickCurve Measure(SimConfig config, PlayerCapabilities profile, string kind, string @case, double strength, double loft,
            (string Name, Vec2 Stick) input, out double vertical)
        {
            vertical = 0;
            var start = new Vec2(0.5, 0);
            var sim = new Simulator(config, new SimState
            {
                Ball = new BallState { Id = 100, Position = new Vec3(start.X, start.Y, config.BallRadius) },
                Players = new[] { new PlayerState { Id = PlayerId, OwnerControllerId = ControllerId, Capabilities = profile } },
            });

            var curve = new KickCurve
            {
                Kind = kind,
                Case = @case,
                ConfigHash = config.Hash,
                Strength = strength,
                Loft = loft,
                Input = input.Name,
                WindowSeconds = config.AftertouchSeconds,
                LaunchSpeedMps = strength * profile.KickPower * config.KickSpeed,
                LaunchElevationDegrees = loft * config.KickLoft * 180 / Math.PI,
            };

            double rate = config.TickRate, radius = config.BallRadius;
            var commands = new Command[1];
            var events = new List<SimEvent>();
            long maxTicks = (long)(MaxSeconds * rate);
            // The contact step launches the ball and integrates it once, so a ball that bounces or
            // stops on step t has flown t - contact + 1 steps.
            long contact = -1, windowEnd = -1, bounced = -1;
            double launchAngle = 0;
            Vec2 line = Vec2.Zero;

            for (long t = 0; t < maxTicks; t++)
            {
                KickIntent? kick = t == 0 ? new KickIntent(new Vec2(1, 0), strength, loft) : (KickIntent?)null;
                commands[0] = new Command(ControllerId, t, Vec2.Zero, kick, t == 0 ? Vec2.Zero : input.Stick);
                events.Clear();
                sim.Step(commands, events);
                BallState b = sim.PeekState().Ball;

                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Kicked)
                    {
                        contact = t;
                        windowEnd = t + config.AftertouchTicks;
                        launchAngle = SimMath.Atan2(b.Velocity.Y, b.Velocity.X);
                        line = new Vec2(SimMath.Cos(launchAngle), SimMath.Sin(launchAngle));
                        curve.ErrorDegrees = launchAngle * 180 / Math.PI;
                    }
                    else if (e.Kind == SimEventKind.KickMissed)
                    {
                        throw new InvalidOperationException("The characterisation's kick missed.");
                    }
                    else if (e.Kind == SimEventKind.Bounced && bounced < 0 && contact >= 0)
                    {
                        bounced = t;
                        curve.CarryMetres = Length(b.Position.Horizontal - start);
                        curve.FlightSeconds = (t - contact + 1) / rate;
                    }
                }

                if (contact < 0) continue;
                curve.PeakMetres = Math.Max(curve.PeakMetres, b.Position.Z - radius);
                if (t == windowEnd)
                {
                    curve.TurnDegrees = Wrap(SimMath.Atan2(b.Velocity.Y, b.Velocity.X) - launchAngle) * 180 / Math.PI;
                    curve.SpinRadps = b.Spin.Z;
                    vertical = b.Velocity.Z;
                }

                bool resting = b.Velocity.X == 0 && b.Velocity.Y == 0 && b.Velocity.Z == 0 && b.Position.Z == radius;
                if (t >= windowEnd && resting)
                {
                    curve.RestSeconds = (t - contact + 1) / rate;
                    break;
                }
            }

            if (contact < 0) throw new InvalidOperationException("The characterisation's kick never connected.");

            // At rest, or where the ball was when time ran out, as the ball curves do.
            Vec2 travelled = sim.PeekState().Ball.Position.Horizontal - start;
            curve.TotalMetres = Length(travelled);
            curve.LateralMetres = line.X * travelled.Y - line.Y * travelled.X;
            return curve;
        }

        private static double Length(Vec2 v) => SimMath.Sqrt(v.LengthSquared);

        private static double Wrap(double angle) =>
            angle > Math.PI ? angle - 2 * Math.PI : angle < -Math.PI ? angle + 2 * Math.PI : angle;

        public const string CsvHeader =
            "config_hash,kind,case,strength,loft,input,window_s,launch_speed_mps,launch_elevation_deg,error_deg," +
            "turn_deg,spin_radps,lift_mps,carry_m,peak_m,flight_s,total_m,lateral_m,rest_s";

        /// <summary>One row per kick. Every row carries its config's hash: aftertouch rows vary the window, so theirs differ.</summary>
        public static void WriteCsv(TextWriter writer, IEnumerable<KickCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine(CsvHeader);
            foreach (KickCurve c in curves)
            {
                writer.WriteLine(string.Join(",",
                    c.ConfigHash.ToString("x16", CultureInfo.InvariantCulture), c.Kind, c.Case,
                    F(c.Strength), F(c.Loft), c.Input, F(c.WindowSeconds),
                    F(c.LaunchSpeedMps), F(c.LaunchElevationDegrees), F(c.ErrorDegrees),
                    F(c.TurnDegrees), F(c.SpinRadps), F(c.LiftMps),
                    F(c.CarryMetres), F(c.PeakMetres), F(c.FlightSeconds), F(c.TotalMetres), F(c.LateralMetres), c.RestSeconds.HasValue ? F(c.RestSeconds.Value) : ""));
            }
        }

        /// <summary>
        /// Who made the curves (build, config, every tunable and the profile, written exactly) and the
        /// key numbers. The tool takes ball values as well as kick values, so every tunable is
        /// recorded, and those that differ from the defaults are marked.
        /// </summary>
        public static void WriteSummary(TextWriter writer, SimConfig config, PlayerCapabilities profile, IReadOnlyList<KickCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine("# eleven kick curves v1");
            writer.WriteLine($"build: {SimulationInfo.BuildId}");
            writer.WriteLine($"config-hash: {config.Hash:x16}");
            writer.WriteLine($"config-format: {SimConfig.FormatVersion}");
            writer.WriteLine($"tick-rate: {config.TickRate}");
            writer.WriteLine("status: technical reference of the current values, not approved feel");
            var defaults = new SimConfig();
            foreach (TunableParameter p in SimConfig.Tunables)
            {
                string mark = config.Get(p.Name) != defaults.Get(p.Name) ? "  (override)" : "";
                writer.WriteLine($"{p.Name}: {R(config.Get(p.Name))} {p.Unit}{mark}");
            }

            foreach (TunableParameter p in PlayerCapabilities.Parameters)
            {
                writer.WriteLine($"{p.Name}: {R(profile.Get(p.Name))} {p.Unit}");
            }

            writer.WriteLine($"launch grid: strength {List(Strengths)} x loft {List(Lofts)}; one player at rest kicks a ball at rest along +x");
            writer.WriteLine($"aftertouch: none/left/back/forward at full stick, held from the step after the intent, on a ground pass " +
                $"(strength {R(ShapedKicks[0].Strength)}, loft {R(ShapedKicks[0].Loft)}) and a lofted kick (strength {R(ShapedKicks[1].Strength)}, loft {R(ShapedKicks[1].Loft)}), " +
                $"windows {List(WindowsFor(config))} s (the research starting range, and the config's own {R(config.AftertouchSeconds)} s)");
            writer.WriteLine($"key: {KeyNumbers(config, curves)}");
        }

        /// <summary>The numbers worth a glance.</summary>
        public static string KeyNumbers(SimConfig config, IReadOnlyList<KickCurve> curves)
        {
            KickCurve full = Find(curves, "launch", "", 1.0, 0.5, "none", null);
            KickCurve pass = Find(curves, "aftertouch", "ground-pass", 0.6, 0.0, "none", config.AftertouchSeconds);
            KickCurve bent = Find(curves, "aftertouch", "ground-pass", 0.6, 0.0, "left", config.AftertouchSeconds);
            KickCurve lofted = Find(curves, "aftertouch", "lofted", 1.0, 0.4, "none", config.AftertouchSeconds);
            KickCurve lifted = Find(curves, "aftertouch", "lofted", 1.0, 0.4, "back", config.AftertouchSeconds);
            KickCurve curled = Find(curves, "aftertouch", "lofted", 1.0, 0.4, "left", config.AftertouchSeconds);
            return $"full strength, half loft: {F1(full.LaunchSpeedMps)} m/s at {F1(full.LaunchElevationDegrees)}°, carry {F1(full.CarryMetres)} m, peak {F1(full.PeakMetres)} m; " +
                $"ground pass {F1(pass.LaunchSpeedMps)} m/s rolls {F1(pass.TotalMetres)} m; " +
                $"{R(config.AftertouchSeconds)} s window: left turns it {F1(bent.TurnDegrees)}° and rests {F1(bent.LateralMetres)} m left; " +
                $"lofted kick peak {F1(lofted.PeakMetres)} m, back lifts it to {F1(lifted.PeakMetres)} m, left curls it {F1(curled.LateralMetres)} m";
        }

        private static KickCurve Find(IReadOnlyList<KickCurve> curves, string kind, string @case, double strength, double loft, string input, double? window)
        {
            foreach (KickCurve c in curves)
            {
                if (c.Kind == kind && c.Case == @case && c.Strength == strength && c.Loft == loft && c.Input == input
                    && (window == null || c.WindowSeconds == window.Value))
                {
                    return c;
                }
            }

            throw new ArgumentException($"No {kind} {@case} {strength}/{loft} {input} curve.");
        }

        private static string List(IEnumerable<double> values)
        {
            var parts = new List<string>();
            foreach (double v in values) parts.Add(v.ToString("0.##", CultureInfo.InvariantCulture));
            return string.Join("/", parts);
        }

        private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // Adding +0 turns −0 into +0, so a sign-less zero never prints as "-0.000".
        private static string F(double value) => (value + 0.0).ToString("0.000", CultureInfo.InvariantCulture);

        private static string F1(double value) => (value + 0.0).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
