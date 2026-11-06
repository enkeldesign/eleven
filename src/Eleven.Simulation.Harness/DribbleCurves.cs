// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One dribbling case under one ball-control profile (#59).</summary>
    public sealed class DribbleCurve
    {
        /// <summary>
        /// <c>straight-jog</c>, <c>sprint</c>, <c>turn-45</c>, <c>turn-90</c>, <c>receive-and-go</c>,
        /// <c>difficult-first-touch</c> or <c>kick-release</c>.
        /// </summary>
        public string Case = "";

        /// <summary><c>good</c> or <c>poor</c>.</summary>
        public string Profile = "";

        public double BallControl;

        /// <summary>Touches over the measured window.</summary>
        public int Touches;

        /// <summary>m. Mean and largest offset of the ball ahead of the player, along their direction of travel.</summary>
        public double MeanLeadMetres;

        public double MaxLeadMetres;

        /// <summary>m. Largest distance between the player and the ball over the window.</summary>
        public double MaxSeparationMetres;

        /// <summary>s. Time in the window with the ball beyond control reach.</summary>
        public double LostSeconds;

        /// <summary>s, receiving cases: from the first touch until the ball is within reach and moving with the player; NaN if never.</summary>
        public double SettleSeconds = double.NaN;

        /// <summary>m, receiving cases: how far the ball travelled over that time; NaN if never settled.</summary>
        public double SettleMetres = double.NaN;

        /// <summary>m/s, kick-release: the ball's speed the step after contact.</summary>
        public double ReleaseSpeedMps = double.NaN;

        /// <summary>kick-release: touches from contact through the kick cooldown; must be 0.</summary>
        public int TouchesAfterKick;
    }

    /// <summary>
    /// The FREE model's characterisation (#59): straight dribbling at a jog and a sprint, 45° and
    /// 90° turns at a sprint, receive-and-go, a difficult first touch and the release on a kick,
    /// each under a good and a poor ball-control profile. Technical references for the bench, not
    /// feel; docs/simulation/dribble.md says what each measures.
    /// </summary>
    public static class DribbleCurves
    {
        public const double GoodControl = 0.9;
        public const double PoorControl = 0.3;
        private const int Controller = 1;
        private const ulong Seed = 0x0E1E7E2059UL;

        public static List<DribbleCurve> Characterise(SimConfig config)
        {
            var curves = new List<DribbleCurve>();
            foreach ((string profile, double control) in new[] { ("good", GoodControl), ("poor", PoorControl) })
            {
                curves.Add(Straight(config, "straight-jog", profile, control, 0.5));
                curves.Add(Straight(config, "sprint", profile, control, 1));
                curves.Add(Turn(config, "turn-45", profile, control, 45));
                curves.Add(Turn(config, "turn-90", profile, control, 90));
                curves.Add(Receive(config, "receive-and-go", profile, control, 8));
                curves.Add(Receive(config, "difficult-first-touch", profile, control, 16));
                curves.Add(KickRelease(config, profile, control));
            }

            return curves;
        }

        private static SimState Start(SimConfig config, double control, Vec2 ball, Vec2 ballVelocity) => new SimState
        {
            Seed = Seed,
            Ball = new BallState { Id = 100, Position = new Vec3(ball.X, ball.Y, config.BallRadius), Velocity = new Vec3(ballVelocity.X, ballVelocity.Y, 0) },
            Players = new[] { new PlayerState { Id = 1, OwnerControllerId = Controller, Capabilities = PlayerCapabilities.Reference.With("ballControl", control) } },
        };

        // Steps the simulation, measuring from `from` on; returns the events.
        private static List<SimEvent> Measure(Simulator sim, SimConfig config, Func<long, Command> command, long ticks, long from, DribbleCurve curve)
        {
            var all = new List<SimEvent>();
            var step = new List<Command>(1);
            double leadSum = 0;
            int measured = 0;
            curve.MaxLeadMetres = double.NegativeInfinity;
            for (long t = 0; t < ticks; t++)
            {
                step.Clear();
                step.Add(command(sim.Tick));
                int first = all.Count;
                sim.Step(step, all);
                if (t < from) continue;

                for (int i = first; i < all.Count; i++)
                {
                    if (all[i].Kind == SimEventKind.Touched) curve.Touches++;
                }

                SimState s = sim.PeekState();
                PlayerState p = s.Players[0];
                Vec2 offset = s.Ball.Position.Horizontal - p.Position;
                double separation = SimMath.Sqrt(offset.LengthSquared);
                double speed = SimMath.Sqrt(p.Velocity.LengthSquared);
                double lead = speed > 1e-6 ? (offset.X * p.Velocity.X + offset.Y * p.Velocity.Y) / speed : separation;
                leadSum += lead;
                measured++;
                curve.MaxLeadMetres = Math.Max(curve.MaxLeadMetres, lead);
                curve.MaxSeparationMetres = Math.Max(curve.MaxSeparationMetres, separation);
                if (separation > config.ControlReach) curve.LostSeconds += config.TickSeconds;
            }

            curve.MeanLeadMetres = measured > 0 ? leadSum / measured : double.NaN;
            return all;
        }

        private static DribbleCurve Straight(SimConfig config, string name, string profile, double control, double stick)
        {
            var curve = new DribbleCurve { Case = name, Profile = profile, BallControl = control };
            var sim = new Simulator(config, Start(config, control, new Vec2(0.6, 0), Vec2.Zero));
            Measure(sim, config, tick => new Command(Controller, tick, new Vec2(stick, 0)), 5 * config.TickRate, 2 * config.TickRate, curve);
            return curve;
        }

        private static DribbleCurve Turn(SimConfig config, string name, string profile, double control, double degrees)
        {
            var curve = new DribbleCurve { Case = name, Profile = profile, BallControl = control };
            double r = degrees * Math.PI / 180;
            var turned = new Vec2(SimMath.Cos(r), SimMath.Sin(r));
            long turnAt = 2 * config.TickRate;
            var sim = new Simulator(config, Start(config, control, new Vec2(0.6, 0), Vec2.Zero));
            Measure(sim, config, tick => new Command(Controller, tick, tick < turnAt ? new Vec2(1, 0) : turned), turnAt + 2 * config.TickRate, turnAt, curve);
            return curve;
        }

        // A ball rolled head-on at a player who jogs on to meet it and goes.
        private static DribbleCurve Receive(SimConfig config, string name, string profile, double control, double ballSpeed)
        {
            var curve = new DribbleCurve { Case = name, Profile = profile, BallControl = control };
            var sim = new Simulator(config, Start(config, control, new Vec2(10, 0), new Vec2(-ballSpeed, 0)));
            var step = new List<Command>(1);
            var events = new List<SimEvent>();
            long ticks = 4 * config.TickRate;
            long firstTouch = -1;
            Vec2 ballAtTouch = Vec2.Zero;
            for (long t = 0; t < ticks; t++)
            {
                step.Clear();
                step.Add(new Command(Controller, sim.Tick, new Vec2(0.5, 0)));
                events.Clear();
                Vec2 ballBefore = sim.PeekState().Ball.Position.Horizontal;
                sim.Step(step, events);
                foreach (SimEvent e in events)
                {
                    if (e.Kind != SimEventKind.Touched) continue;
                    curve.Touches++;
                    if (firstTouch < 0)
                    {
                        firstTouch = t;
                        ballAtTouch = ballBefore;
                    }
                }

                if (firstTouch < 0 || !double.IsNaN(curve.SettleSeconds)) continue;
                SimState s = sim.PeekState();
                PlayerState p = s.Players[0];
                Vec2 offset = s.Ball.Position.Horizontal - p.Position;
                Vec2 relative = s.Ball.Velocity.Horizontal - p.Velocity;
                if (offset.LengthSquared <= config.ControlReach * config.ControlReach && relative.LengthSquared < 1)
                {
                    curve.SettleSeconds = (t - firstTouch) * config.TickSeconds;
                    curve.SettleMetres = SimMath.Sqrt((s.Ball.Position.Horizontal - ballAtTouch).LengthSquared);
                }
            }

            SimState end = sim.PeekState();
            curve.MaxSeparationMetres = SimMath.Sqrt((end.Ball.Position.Horizontal - end.Players[0].Position).LengthSquared);
            curve.MeanLeadMetres = curve.MaxLeadMetres = double.NaN;
            return curve;
        }

        private static DribbleCurve KickRelease(SimConfig config, string profile, double control)
        {
            var curve = new DribbleCurve { Case = "kick-release", Profile = profile, BallControl = control };
            long kickAt = 2 * config.TickRate;
            var sim = new Simulator(config, Start(config, control, new Vec2(0.6, 0), Vec2.Zero));
            var step = new List<Command>(1);
            var events = new List<SimEvent>();
            long contact = -1;
            for (long t = 0; t < kickAt + config.KickWindUpTicks + config.KickCooldownTicks + 2; t++)
            {
                step.Clear();
                step.Add(new Command(Controller, sim.Tick, new Vec2(0.5, 0), t == kickAt ? KickIntent.AlongHeading(0.6, 0) : (KickIntent?)null));
                events.Clear();
                sim.Step(step, events);
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Touched && t < kickAt) curve.Touches++;
                    if (e.Kind == SimEventKind.Touched && contact >= 0) curve.TouchesAfterKick++;
                    if (e.Kind == SimEventKind.Kicked && contact < 0)
                    {
                        contact = t;
                        curve.ReleaseSpeedMps = SimMath.Sqrt(sim.PeekState().Ball.Velocity.LengthSquared);
                    }
                }
            }

            curve.MeanLeadMetres = curve.MaxLeadMetres = curve.MaxSeparationMetres = double.NaN;
            return curve;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<DribbleCurve> curves)
        {
            w.Write("config_hash,case,profile,ball_control,touches,mean_lead_m,max_lead_m,max_separation_m,lost_s,settle_s,settle_m,release_mps,touches_after_kick\n");
            foreach (DribbleCurve c in curves)
            {
                w.Write(string.Join(",",
                    config.Hash.ToString("x16", CultureInfo.InvariantCulture), c.Case, c.Profile, F(c.BallControl),
                    c.Touches.ToString(CultureInfo.InvariantCulture), F(c.MeanLeadMetres), F(c.MaxLeadMetres), F(c.MaxSeparationMetres),
                    F(c.LostSeconds), F(c.SettleSeconds), F(c.SettleMetres), F(c.ReleaseSpeedMps),
                    c.TouchesAfterKick.ToString(CultureInfo.InvariantCulture)));
                w.Write('\n');
            }
        }

        /// <summary>The figures a reader wants first, one line.</summary>
        public static string KeyNumbers(IReadOnlyList<DribbleCurve> curves)
        {
            var parts = new List<string>();
            foreach (DribbleCurve c in curves)
            {
                string detail = c.Case switch
                {
                    "straight-jog" or "sprint" => FormattableString.Invariant($"lead {c.MeanLeadMetres:F2} m (max {c.MaxLeadMetres:F2}), lost {c.LostSeconds:F2} s"),
                    "turn-45" or "turn-90" => FormattableString.Invariant($"max separation {c.MaxSeparationMetres:F2} m, lost {c.LostSeconds:F2} s"),
                    "kick-release" => FormattableString.Invariant($"released at {c.ReleaseSpeedMps:F1} m/s, {c.TouchesAfterKick} touches after"),
                    _ => double.IsNaN(c.SettleSeconds)
                        ? FormattableString.Invariant($"not settled, {c.MaxSeparationMetres:F1} m away after 4 s")
                        : FormattableString.Invariant($"settled in {c.SettleSeconds:F2} s over {c.SettleMetres:F2} m"),
                };
                parts.Add($"{c.Case}/{c.Profile}: {detail}");
            }

            return string.Join("; ", parts);
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
