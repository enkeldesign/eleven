// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One ball-control case under one model and one ball-control profile (#59, #60).</summary>
    public sealed class DribbleCurve
    {
        /// <summary><c>free</c> or <c>attached</c>.</summary>
        public string Model = "";

        /// <summary>
        /// <c>straight-jog</c>, <c>sprint</c>, <c>sprint-stop</c>, <c>turn-45</c>, <c>turn-90</c>,
        /// <c>receive-and-go</c>, <c>difficult-first-touch</c>, <c>kick-release</c>,
        /// <c>sprint-flick</c> or <c>switch-in</c>.
        /// </summary>
        public string Case = "";

        /// <summary><c>good</c> or <c>poor</c>.</summary>
        public string Profile = "";

        public double BallControl;

        /// <summary>FREE touches over the measured window.</summary>
        public int Touches;

        /// <summary>ATTACHED: times the ball was taken, and lost, over the measured window.</summary>
        public int Acquired;

        public int Released;

        /// <summary>m. Mean and largest offset of the ball ahead of the player, along their direction of travel.</summary>
        public double MeanLeadMetres;

        public double MaxLeadMetres;

        /// <summary>m. Largest distance between the player and the ball over the window.</summary>
        public double MaxSeparationMetres;

        /// <summary>s. Time in the window without control: beyond the control reach under FREE, not attached under ATTACHED.</summary>
        public double LostSeconds;

        /// <summary>
        /// s, receiving cases: from the first control until the ball is controlled and moving with
        /// the player (within reach under FREE, attached under ATTACHED); NaN if never.
        /// </summary>
        public double SettleSeconds = double.NaN;

        /// <summary>m, receiving cases: how far the ball travelled over that time; NaN if never settled.</summary>
        public double SettleMetres = double.NaN;

        /// <summary>m/s, kick-release: the ball's speed the step after contact.</summary>
        public double ReleaseSpeedMps = double.NaN;

        /// <summary>kick-release: control events from contact through the kick cooldown; must be 0.</summary>
        public int TouchesAfterKick;

        /// <summary>
        /// sprint-flick: the share of flicks that connect, of one started at each step of a touch
        /// interval while sprinting with the ball. Being within reach when the flick starts doesn't
        /// guarantee contact: the ball rolls on through the wind-up.
        /// </summary>
        public double FlickConnectShare = double.NaN;

        /// <summary>m/s², ATTACHED: the largest change in the ball's horizontal velocity per second on a held step, rolling friction included.</summary>
        public double MaxCorrectionMps2 = double.NaN;

        /// <summary>s, switch-in: from the switch into this model, mid-dribble, to its first control of the ball; NaN if none within 2 s.</summary>
        public double ControlAfterSwitchSeconds = double.NaN;
    }

    /// <summary>
    /// The ball-control characterisation (#59, #60), under FREE and under ATTACHED, each at a good
    /// and a poor ball-control profile. The cases:
    /// - straight dribbling at a jog and a sprint, and a stop from a sprint;
    /// - 45° and 90° turns at a sprint;
    /// - receive-and-go, and a difficult first touch;
    /// - the release on a kick, and flicks while sprinting;
    /// - the switch into the model mid-dribble.
    /// Technical references for the bench, not feel; docs/simulation/dribble.md and attached.md
    /// say what each measures.
    /// </summary>
    public static class DribbleCurves
    {
        public const double GoodControl = 0.9;
        public const double PoorControl = 0.3;
        private const int Controller = 1;
        private const ulong Seed = 0x0E1E7E2059UL;

        /// <summary>Every case under FREE, then under ATTACHED, with <paramref name="config"/>'s values.</summary>
        public static List<DribbleCurve> Characterise(SimConfig config) => Characterise(config, PlayerCapabilities.Reference);

        /// <summary>The same cases for this footballer: "good" and "poor" are him with his ball control changed.</summary>
        public static List<DribbleCurve> Characterise(SimConfig config, PlayerCapabilities body)
        {
            var curves = new List<DribbleCurve>();
            foreach (ControlModel model in new[] { ControlModel.Free, ControlModel.Attached })
            {
                SimConfig c = config.WithControlModel(model);
                foreach ((string profile, double control) in new[] { ("good", GoodControl), ("poor", PoorControl) })
                {
                    curves.Add(Straight(c, "straight-jog", profile, body, control, 0.5));
                    curves.Add(Straight(c, "sprint", profile, body, control, 1));
                    curves.Add(SprintStop(c, profile, body, control));
                    curves.Add(Turn(c, "turn-45", profile, body, control, 45));
                    curves.Add(Turn(c, "turn-90", profile, body, control, 90));
                    curves.Add(Receive(c, "receive-and-go", profile, body, control, 8));
                    curves.Add(Receive(c, "difficult-first-touch", profile, body, control, 16));
                    curves.Add(KickRelease(c, profile, body, control));
                    curves.Add(SprintFlick(c, profile, body, control));
                    curves.Add(SwitchIn(c, profile, body, control));
                }
            }

            return curves;
        }

        private static string Name(ControlModel model) => model == ControlModel.Attached ? "attached" : "free";

        private static DribbleCurve New(SimConfig config, string name, string profile, PlayerCapabilities body, double control) =>
            new DribbleCurve { Model = Name(config.ControlModel), Case = name, Profile = profile, BallControl = control };

        private static bool IsControl(SimEventKind kind) => kind == SimEventKind.Touched || kind == SimEventKind.Attached;

        private static SimState Start(SimConfig config, PlayerCapabilities body, double control, Vec2 ball, Vec2 ballVelocity) => new SimState
        {
            Seed = Seed,
            Ball = new BallState { Id = 100, Position = new Vec3(ball.X, ball.Y, config.BallRadius), Velocity = new Vec3(ballVelocity.X, ballVelocity.Y, 0) },
            Players = new[] { new PlayerState { Id = 1, OwnerControllerId = Controller, Capabilities = body.With("ballControl", control) } },
        };

        // Steps the simulation, measuring from `from` on; returns the events.
        private static List<SimEvent> Measure(Simulator sim, SimConfig config, Func<long, Command> command, long ticks, long from, DribbleCurve curve)
        {
            bool attached = config.ControlModel == ControlModel.Attached;
            var all = new List<SimEvent>();
            var step = new List<Command>(1);
            double leadSum = 0;
            int measured = 0;
            curve.MaxLeadMetres = double.NegativeInfinity;
            if (attached) curve.MaxCorrectionMps2 = 0;
            for (long t = 0; t < ticks; t++)
            {
                step.Clear();
                step.Add(command(sim.Tick));
                int first = all.Count;
                BallState before = sim.PeekState().Ball;
                sim.Step(step, all);
                if (t < from) continue;

                bool taken = false;
                for (int i = first; i < all.Count; i++)
                {
                    if (all[i].Kind == SimEventKind.Touched) curve.Touches++;
                    if (all[i].Kind == SimEventKind.Attached) { curve.Acquired++; taken = true; }
                    if (all[i].Kind == SimEventKind.Released) curve.Released++;
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
                bool lost = attached ? s.Ball.AttachedTo == 0 : separation > config.ControlReach;
                if (lost) curve.LostSeconds += config.TickSeconds;

                if (attached && before.AttachedTo != 0 && s.Ball.AttachedTo != 0 && !taken)
                {
                    double change = SimMath.Sqrt((s.Ball.Velocity.Horizontal - before.Velocity.Horizontal).LengthSquared) / config.TickSeconds;
                    curve.MaxCorrectionMps2 = Math.Max(curve.MaxCorrectionMps2, change);
                }
            }

            curve.MeanLeadMetres = measured > 0 ? leadSum / measured : double.NaN;
            return all;
        }

        private static DribbleCurve Straight(SimConfig config, string name, string profile, PlayerCapabilities body, double control, double stick)
        {
            DribbleCurve curve = New(config, name, profile, body, control);
            var sim = new Simulator(config, Start(config, body, control, new Vec2(0.6, 0), Vec2.Zero));
            Measure(sim, config, tick => new Command(Controller, tick, new Vec2(stick, 0)), 5 * config.TickRate, 2 * config.TickRate, curve);
            return curve;
        }

        // Full stick for 2 s, then the stick released: does the ball stop with the player?
        private static DribbleCurve SprintStop(SimConfig config, string profile, PlayerCapabilities body, double control)
        {
            DribbleCurve curve = New(config, "sprint-stop", profile, body, control);
            long stopAt = 2 * config.TickRate;
            var sim = new Simulator(config, Start(config, body, control, new Vec2(0.6, 0), Vec2.Zero));
            Measure(sim, config, tick => new Command(Controller, tick, tick < stopAt ? new Vec2(1, 0) : Vec2.Zero), stopAt + 2 * config.TickRate, stopAt, curve);
            curve.MeanLeadMetres = curve.MaxLeadMetres = double.NaN;
            return curve;
        }

        private static DribbleCurve Turn(SimConfig config, string name, string profile, PlayerCapabilities body, double control, double degrees)
        {
            DribbleCurve curve = New(config, name, profile, body, control);
            double r = degrees * Math.PI / 180;
            var turned = new Vec2(SimMath.Cos(r), SimMath.Sin(r));
            long turnAt = 2 * config.TickRate;
            var sim = new Simulator(config, Start(config, body, control, new Vec2(0.6, 0), Vec2.Zero));
            Measure(sim, config, tick => new Command(Controller, tick, tick < turnAt ? new Vec2(1, 0) : turned), turnAt + 2 * config.TickRate, turnAt, curve);
            return curve;
        }

        // A ball rolled head-on at a player who jogs on to meet it and goes.
        private static DribbleCurve Receive(SimConfig config, string name, string profile, PlayerCapabilities body, double control, double ballSpeed)
        {
            DribbleCurve curve = New(config, name, profile, body, control);
            var sim = new Simulator(config, Start(config, body, control, new Vec2(10, 0), new Vec2(-ballSpeed, 0)));
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
                    if (e.Kind == SimEventKind.Touched) curve.Touches++;
                    if (e.Kind == SimEventKind.Attached) curve.Acquired++;
                    if (e.Kind == SimEventKind.Released) curve.Released++;
                    if (!IsControl(e.Kind) || firstTouch >= 0) continue;
                    firstTouch = t;
                    ballAtTouch = ballBefore;
                }

                if (firstTouch < 0 || !double.IsNaN(curve.SettleSeconds)) continue;
                SimState s = sim.PeekState();
                PlayerState p = s.Players[0];
                Vec2 offset = s.Ball.Position.Horizontal - p.Position;
                Vec2 relative = s.Ball.Velocity.Horizontal - p.Velocity;
                // Settled: in control and moving with the player. Under ATTACHED a held ball may sit
                // beyond the control reach, out to the loss reach, so being held is what counts.
                bool held = config.ControlModel == ControlModel.Attached
                    ? s.Ball.AttachedTo != 0
                    : offset.LengthSquared <= config.ControlReach * config.ControlReach;
                if (held && relative.LengthSquared < 1)
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

        private static DribbleCurve KickRelease(SimConfig config, string profile, PlayerCapabilities body, double control)
        {
            DribbleCurve curve = New(config, "kick-release", profile, body, control);
            long kickAt = 2 * config.TickRate;
            var sim = new Simulator(config, Start(config, body, control, new Vec2(0.6, 0), Vec2.Zero));
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
                    if (e.Kind == SimEventKind.Released) curve.Released++;
                    if (IsControl(e.Kind) && t < kickAt)
                    {
                        if (e.Kind == SimEventKind.Touched) curve.Touches++;
                        else curve.Acquired++;
                    }

                    if (IsControl(e.Kind) && contact >= 0) curve.TouchesAfterKick++;
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

        // Flicks while sprinting with the ball (SOL on #59): one started at each step of a touch
        // interval, from 2 s of sprinting, each in its own run, so every phase of the touch cycle is
        // tried once.
        private static DribbleCurve SprintFlick(SimConfig config, string profile, PlayerCapabilities body, double control)
        {
            DribbleCurve curve = New(config, "sprint-flick", profile, body, control);
            int attempts = Math.Max(1, config.TouchIntervalTicks);
            long first = 2 * config.TickRate;
            var step = new List<Command>(1);
            var events = new List<SimEvent>();
            int connected = 0;
            for (int k = 0; k < attempts; k++)
            {
                long kickAt = first + k;
                var sim = new Simulator(config, Start(config, body, control, new Vec2(0.6, 0), Vec2.Zero));
                bool resolved = false;
                for (long t = 0; !resolved && t <= kickAt + config.KickWindUpTicks + 2; t++)
                {
                    step.Clear();
                    step.Add(new Command(Controller, sim.Tick, new Vec2(1, 0), t == kickAt ? KickIntent.AlongHeading(0.6, 0) : (KickIntent?)null));
                    events.Clear();
                    sim.Step(step, events);
                    foreach (SimEvent e in events)
                    {
                        if (e.Kind == SimEventKind.Kicked) connected++;
                        if (e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KickMissed) resolved = true;
                        if (k != 0 || t >= first) continue;
                        if (e.Kind == SimEventKind.Touched) curve.Touches++;
                        if (e.Kind == SimEventKind.Attached) curve.Acquired++;
                    }
                }
            }

            curve.FlickConnectShare = (double)connected / attempts;
            curve.MeanLeadMetres = curve.MaxLeadMetres = curve.MaxSeparationMetres = double.NaN;
            return curve;
        }

        // A sprint dribble under the other model, switched into this one at 2 s (#60): how soon it
        // takes control, and the 2 s after the switch measured as a sprint.
        private static DribbleCurve SwitchIn(SimConfig config, string profile, PlayerCapabilities body, double control)
        {
            DribbleCurve curve = New(config, "switch-in", profile, body, control);
            ControlModel other = config.ControlModel == ControlModel.Attached ? ControlModel.Free : ControlModel.Attached;
            long switchAt = 2 * config.TickRate;
            var sim = new Simulator(config.WithControlModel(other), Start(config, body, control, new Vec2(0.6, 0), Vec2.Zero));
            var step = new List<Command>(1);
            var events = new List<SimEvent>();
            for (long t = 0; t < switchAt; t++)
            {
                step.Clear();
                step.Add(new Command(Controller, sim.Tick, new Vec2(1, 0)));
                events.Clear();
                sim.Step(step, events);
            }

            sim = ControlSwitch.To(sim, config.ControlModel);
            List<SimEvent> after = Measure(sim, config, tick => new Command(Controller, tick, new Vec2(1, 0)), 2 * config.TickRate, 0, curve);
            foreach (SimEvent e in after)
            {
                if (!IsControl(e.Kind)) continue;
                curve.ControlAfterSwitchSeconds = (e.Tick - switchAt) * config.TickSeconds;
                break;
            }

            return curve;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<DribbleCurve> curves)
        {
            w.Write("config_hash,model,case,profile,ball_control,touches,acquired,released,mean_lead_m,max_lead_m,max_separation_m,lost_s,settle_s,settle_m,release_mps,touches_after_kick,flick_connect,max_correction_mps2,control_after_switch_s\n");
            foreach (DribbleCurve c in curves)
            {
                SimConfig under = config.WithControlModel(c.Model == "attached" ? ControlModel.Attached : ControlModel.Free);
                w.Write(string.Join(",",
                    under.Hash.ToString("x16", CultureInfo.InvariantCulture), c.Model, c.Case, c.Profile, F(c.BallControl),
                    I(c.Touches), I(c.Acquired), I(c.Released),
                    F(c.MeanLeadMetres), F(c.MaxLeadMetres), F(c.MaxSeparationMetres),
                    F(c.LostSeconds), F(c.SettleSeconds), F(c.SettleMetres), F(c.ReleaseSpeedMps),
                    I(c.TouchesAfterKick), F(c.FlickConnectShare), F(c.MaxCorrectionMps2), F(c.ControlAfterSwitchSeconds)));
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
                    "sprint-stop" or "turn-45" or "turn-90" => FormattableString.Invariant($"max separation {c.MaxSeparationMetres:F2} m, lost {c.LostSeconds:F2} s"),
                    "kick-release" => FormattableString.Invariant($"released at {c.ReleaseSpeedMps:F1} m/s, {c.TouchesAfterKick} touches after"),
                    "sprint-flick" => FormattableString.Invariant($"{c.FlickConnectShare * 100:F0}% of flicks connect"),
                    "switch-in" => double.IsNaN(c.ControlAfterSwitchSeconds)
                        ? "no control within 2 s of the switch"
                        : FormattableString.Invariant($"control {c.ControlAfterSwitchSeconds:F2} s after the switch"),
                    _ => double.IsNaN(c.SettleSeconds)
                        ? FormattableString.Invariant($"not settled, {c.MaxSeparationMetres:F1} m away after 4 s")
                        : FormattableString.Invariant($"settled in {c.SettleSeconds:F2} s over {c.SettleMetres:F2} m"),
                };
                parts.Add($"{c.Model}/{c.Case}/{c.Profile}: {detail}");
            }

            return string.Join("; ", parts);
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
