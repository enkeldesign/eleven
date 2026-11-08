// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// One change of direction with the ball (#66): a model, the turn off or on, a ball-control
    /// profile, a speed and an angle, started at each step of a touch interval.
    /// </summary>
    public sealed class TurnCurve
    {
        /// <summary><c>free</c> or <c>attached</c>.</summary>
        public string Model = "";

        /// <summary><c>off</c> (turnStrength 0: every touch ordinary) or <c>on</c> (the config's turn).</summary>
        public string Turn = "";

        /// <summary><c>good</c>, <c>bench</c> (the bench player's ball control) or <c>poor</c>.</summary>
        public string Profile = "";

        public double BallControl;

        /// <summary><c>half</c> (half stick) or <c>sprint</c> (full stick).</summary>
        public string Speed = "";

        /// <summary>The stick's turn, 90, 135 or 180.</summary>
        public double Degrees;

        /// <summary>m/s. The player's speed as the stick turns.</summary>
        public double EntryMps;

        /// <summary>The runs, one started at each step of a touch interval, and those in which the player kept control throughout.</summary>
        public int Starts;

        public int Kept;

        /// <summary>
        /// s, over the runs kept: from the step the player first moves the stick's way until the ball
        /// is 0.3 m ahead of them along it. NaN when none was kept.
        /// </summary>
        public double ForwardMeanSeconds = double.NaN;

        public double ForwardMaxSeconds = double.NaN;

        /// <summary>m, over the runs kept: the furthest the ball is behind the player over that time.</summary>
        public double TrailMaxMetres = double.NaN;

        /// <summary>s, over the runs kept: the longest the ball is behind the player over that time.</summary>
        public double BehindMaxSeconds = double.NaN;

        /// <summary>
        /// s, over the runs kept: from the same step until the ball is kick-ready, 0.3 m ahead, within
        /// the kick reach and low enough, whatever the model holds it by.
        /// </summary>
        public double KickReadyMaxSeconds = double.NaN;

        /// <summary>m/s, mean over the runs kept: the player's speed along the new direction when the ball is first ahead.</summary>
        public double ExitMps = double.NaN;

        /// <summary>m, over the runs kept: the closest to the player's centre the ball passes them, across the new direction; NaN if it never passes.</summary>
        public double PassBesideMinMetres = double.NaN;

        /// <summary>m/s, over every run: the largest change of the ball's horizontal velocity in one step from the turn on.</summary>
        public double MaxStepChangeMps;
    }

    /// <summary>
    /// The turn characterisation (#66): 90°, 135° and 180° changes of direction with the ball at half
    /// stick and full, under FREE and ATTACHED, with the turn off and on, at three ball-control
    /// profiles. Every case is started at each step of a touch interval, since under FREE where a
    /// turn falls in the touch cycle decides it. Technical references for the bench, not feel;
    /// docs/simulation/turn.md says what each measures.
    /// </summary>
    public static class TurnCurves
    {
        public const double GoodControl = 0.9;
        public const double PoorControl = 0.3;
        private const int Controller = 1;
        private const ulong Seed = 0x0E1E7E2059UL;
        private const double Ahead = 0.3;
        private const double TurnSeconds = 2;
        private const double WindowSeconds = 3;

        /// <summary>Every case under FREE, then ATTACHED; each with the turn off, then on; with <paramref name="config"/>'s values.</summary>
        public static List<TurnCurve> Characterise(SimConfig config)
        {
            var curves = new List<TurnCurve>();
            double bench = PlayerCapabilities.Reference.BallControl;
            foreach (ControlModel model in new[] { ControlModel.Free, ControlModel.Attached })
            foreach ((string turn, SimConfig c) in new[] { ("off", config.WithControlModel(model).With("turnStrength", 0)), ("on", config.WithControlModel(model)) })
            foreach ((string profile, double control) in new[] { ("good", GoodControl), ("bench", bench), ("poor", PoorControl) })
            foreach ((string speed, double stick) in new[] { ("half", 0.5), ("sprint", 1.0) })
            foreach (double degrees in new[] { 90.0, 135.0, 180.0 })
            {
                curves.Add(Case(c, turn, profile, control, speed, stick, degrees));
            }

            return curves;
        }

        /// <summary>The config a row was measured under.</summary>
        public static SimConfig ConfigOf(SimConfig config, TurnCurve curve)
        {
            SimConfig c = config.WithControlModel(curve.Model == "attached" ? ControlModel.Attached : ControlModel.Free);
            return curve.Turn == "off" ? c.With("turnStrength", 0) : c;
        }

        private sealed class Start
        {
            public bool Kept = true;
            public double Forward = double.NaN, KickReady = double.NaN, Trail, Behind, Exit = double.NaN, Beside = double.NaN, MaxChange, Entry;
        }

        private static TurnCurve Case(SimConfig config, string turn, string profile, double control, string speed, double stick, double degrees)
        {
            var curve = new TurnCurve
            {
                Model = config.ControlModel == ControlModel.Attached ? "attached" : "free",
                Turn = turn,
                Profile = profile,
                BallControl = control,
                Speed = speed,
                Degrees = degrees,
                Starts = config.TouchIntervalTicks,
            };

            var kept = new List<Start>();
            for (int phase = 0; phase < curve.Starts; phase++)
            {
                Start s = Run(config, control, stick, degrees, phase);
                curve.EntryMps = s.Entry;
                curve.MaxStepChangeMps = Math.Max(curve.MaxStepChangeMps, s.MaxChange);
                if (s.Kept) kept.Add(s);
            }

            curve.Kept = kept.Count;
            if (kept.Count == 0) return curve;

            double forwardSum = 0, exitSum = 0;
            curve.ForwardMaxSeconds = curve.TrailMaxMetres = curve.BehindMaxSeconds = curve.KickReadyMaxSeconds = 0;
            foreach (Start s in kept)
            {
                forwardSum += s.Forward;
                exitSum += s.Exit;
                curve.ForwardMaxSeconds = Math.Max(curve.ForwardMaxSeconds, s.Forward);
                curve.TrailMaxMetres = Math.Max(curve.TrailMaxMetres, s.Trail);
                curve.BehindMaxSeconds = Math.Max(curve.BehindMaxSeconds, s.Behind);
                curve.KickReadyMaxSeconds = double.IsNaN(s.KickReady) || double.IsNaN(curve.KickReadyMaxSeconds) ? double.NaN : Math.Max(curve.KickReadyMaxSeconds, s.KickReady);
                if (!double.IsNaN(s.Beside)) curve.PassBesideMinMetres = double.IsNaN(curve.PassBesideMinMetres) ? s.Beside : Math.Min(curve.PassBesideMinMetres, s.Beside);
            }

            curve.ForwardMeanSeconds = forwardSum / kept.Count;
            curve.ExitMps = exitSum / kept.Count;
            return curve;
        }

        // Along +x at the stick for 2 s and `phase` steps from the ball at the feet, then the stick
        // turned by `degrees` for 3 s. A run is kept when the player has control at every step from
        // the turn on and the ball gets ahead.
        private static Start Run(SimConfig config, double control, double stick, double degrees, int phase)
        {
            var sim = new Simulator(config, new SimState
            {
                Seed = Seed,
                Ball = new BallState { Id = 100, Position = new Vec3(0.6, 0, config.BallRadius) },
                Players = new[] { new PlayerState { Id = 1, OwnerControllerId = Controller, Capabilities = PlayerCapabilities.Reference.With("ballControl", control) } },
            });

            double r = degrees * Math.PI / 180;
            var d = new Vec2(SimMath.Cos(r), SimMath.Sin(r));
            long turnAt = (long)(TurnSeconds * config.TickRate) + phase;
            long end = turnAt + (long)(WindowSeconds * config.TickRate);
            var step = new List<Command>(1);
            var events = new List<SimEvent>();
            var start = new Start();
            long reversed = -1, forward = -1;
            double? was = null;
            for (long t = 0; t < end; t++)
            {
                if (t == turnAt) start.Entry = SimMath.Sqrt(sim.PeekState().Players[0].Velocity.LengthSquared);
                step.Clear();
                step.Add(new Command(Controller, sim.Tick, (t < turnAt ? new Vec2(1, 0) : d) * stick));
                events.Clear();
                Vec2 before = sim.PeekState().Ball.Velocity.Horizontal;
                sim.Step(step, events);
                if (t < turnAt) continue;

                SimState s = sim.PeekState();
                PlayerState p = s.Players[0];
                start.MaxChange = Math.Max(start.MaxChange, SimMath.Sqrt((s.Ball.Velocity.Horizontal - before).LengthSquared));
                Vec2 offset = s.Ball.Position.Horizontal - p.Position;
                bool inControl = config.ControlModel == ControlModel.Attached
                    ? s.Ball.AttachedTo == p.Id
                    : offset.LengthSquared <= config.ControlReach * config.ControlReach;
                if (!inControl) start.Kept = false;

                double ahead = offset.X * d.X + offset.Y * d.Y;
                if (was.HasValue && Math.Sign(was.Value) != Math.Sign(ahead))
                {
                    double beside = Math.Abs(offset.Y * d.X - offset.X * d.Y);
                    start.Beside = double.IsNaN(start.Beside) ? beside : Math.Min(start.Beside, beside);
                }

                was = ahead;
                if (reversed < 0 && p.Velocity.X * d.X + p.Velocity.Y * d.Y > 0) reversed = t;
                if (reversed < 0) continue;

                if (forward < 0)
                {
                    if (ahead >= Ahead)
                    {
                        forward = t;
                        start.Forward = (t - reversed) * config.TickSeconds;
                        start.Exit = p.Velocity.X * d.X + p.Velocity.Y * d.Y;
                    }
                    else
                    {
                        start.Trail = Math.Max(start.Trail, -ahead);
                        if (ahead < 0) start.Behind += config.TickSeconds;
                    }
                }

                bool ready = ahead >= Ahead && offset.LengthSquared <= config.KickReach * config.KickReach && s.Ball.Position.Z <= config.KickMaxBallHeight;
                if (ready && double.IsNaN(start.KickReady)) start.KickReady = (t - reversed) * config.TickSeconds;
            }

            if (forward < 0) start.Kept = false;
            return start;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<TurnCurve> curves)
        {
            w.Write("config_hash,model,turn,profile,ball_control,speed,turn_deg,entry_mps,starts,kept,forward_mean_s,forward_max_s,trail_max_m,behind_max_s,kick_ready_max_s,exit_mps,pass_beside_min_m,max_step_change_mps\n");
            foreach (TurnCurve c in curves)
            {
                w.Write(string.Join(",",
                    ConfigOf(config, c).Hash.ToString("x16", CultureInfo.InvariantCulture), c.Model, c.Turn, c.Profile, F(c.BallControl), c.Speed,
                    c.Degrees.ToString("F0", CultureInfo.InvariantCulture), F(c.EntryMps), I(c.Starts), I(c.Kept),
                    F(c.ForwardMeanSeconds), F(c.ForwardMaxSeconds), F(c.TrailMaxMetres), F(c.BehindMaxSeconds), F(c.KickReadyMaxSeconds),
                    F(c.ExitMps), F(c.PassBesideMinMetres), F(c.MaxStepChangeMps)));
                w.Write('\n');
            }
        }

        /// <summary>The figures a reader wants first, one line: the 180° turns.</summary>
        public static string KeyNumbers(IReadOnlyList<TurnCurve> curves)
        {
            var parts = new List<string>();
            foreach (TurnCurve c in curves)
            {
                if (c.Degrees != 180) continue;
                string detail = c.Kept == 0
                    ? $"kept 0/{c.Starts}"
                    : FormattableString.Invariant($"kept {c.Kept}/{c.Starts}, ahead in {c.ForwardMaxSeconds:F2} s, trails {c.TrailMaxMetres:F2} m");
                parts.Add($"{c.Model}/{c.Turn}/{c.Profile}/{c.Speed}: {detail}");
            }

            return string.Join("; ", parts);
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
