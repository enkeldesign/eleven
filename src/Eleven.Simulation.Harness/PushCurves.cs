// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One push speed at one ball-control profile, sprinting after the ball (#63).</summary>
    public sealed class PushCurve
    {
        /// <summary>m/s. 0 is FREE's short touches, the comparison.</summary>
        public double PushSpeed;

        /// <summary><c>good</c>, <c>bench</c> or <c>poor</c>.</summary>
        public string Profile = "";

        public double BallControl;

        /// <summary>Pushes over the run; 0 with the push off.</summary>
        public int Pushes;

        /// <summary>Touches of any kind over the run.</summary>
        public int Touches;

        /// <summary>s. Mean time from one push to the next touch; NaN with the push off, or if a push was never followed by a touch.</summary>
        public double CycleSeconds = double.NaN;

        /// <summary>m. Mean distance the footballer covers over that time.</summary>
        public double CycleMetres = double.NaN;

        /// <summary>m. The furthest the ball gets from the footballer over the run.</summary>
        public double PeakSeparationMetres;

        /// <summary>The share of the run with the ball beyond the control reach.</summary>
        public double BeyondReachShare;

        /// <summary>Whether a push was lost: no touch followed it within <see cref="PushCurves.LostSeconds"/>.</summary>
        public bool Lost;

        /// <summary>
        /// Over one cycle, the second where there is one, so at a full sprint: the share of sprint
        /// flicks that connect, one asked for at each step of the cycle. With the push off, over one
        /// touch interval.
        /// </summary>
        public double FlickConnectShare = double.NaN;

        /// <summary>s after the push. The last flick of the first connecting spell, and the first of the last: between them a flick misses. NaN where every flick connects or none does.</summary>
        public double FlickWindowClosesSeconds = double.NaN;

        public double FlickWindowReopensSeconds = double.NaN;
    }

    /// <summary>
    /// The push-and-chase characterisation (#63), beside FREE's short touches: a footballer
    /// sprinting after the ball, the stick full and straight at it, at each push speed and three ball-control profiles. It
    /// measures the cycle from a push to the catch, how far the ball runs ahead, how much of the
    /// run it is beyond reach, whether it is lost, and when in the chase a sprint flick connects.
    /// Technical references for an experiment, not feel; docs/simulation/push.md says what each is.
    /// </summary>
    public static class PushCurves
    {
        public const double GoodControl = 0.9;
        public const double PoorControl = 0.3;

        /// <summary>The push speeds measured, in m/s: off, then weak to overpushed.</summary>
        public static readonly IReadOnlyList<double> Speeds = new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 4.0 };

        private const int Controller = 1;
        private const ulong Seed = 0x0E1E7E2063UL;
        private const double RunSeconds = 12;

        /// <summary>s. A push not followed by a touch within this long counts as lost.</summary>
        public const double LostSeconds = 6;

        public static List<PushCurve> Characterise(SimConfig config) => Characterise(config, PlayerCapabilities.Reference);

        /// <summary>The same cases for this footballer: his own ball control is the "bench" profile, and "good" and "poor" are him with that one value changed.</summary>
        public static List<PushCurve> Characterise(SimConfig config, PlayerCapabilities body)
        {
            var curves = new List<PushCurve>();
            double bench = body.BallControl;
            foreach (double pushSpeed in Speeds)
            foreach ((string profile, double control) in new[] { ("good", GoodControl), ("bench", bench), ("poor", PoorControl) })
            {
                curves.Add(Case(ConfigOf(config, pushSpeed), pushSpeed, profile, body, control));
            }

            return curves;
        }

        /// <summary>The config a row was measured under: FREE, with its push speed.</summary>
        public static SimConfig ConfigOf(SimConfig config, double pushSpeed) =>
            config.WithControlModel(ControlModel.Free).With("pushSpeed", pushSpeed);

        private static SimState Start(SimConfig config, PlayerCapabilities body, double control)
        {
            PlayerCapabilities capabilities = body.With("ballControl", control);
            return new SimState
            {
                Seed = Seed,
                Ball = new BallState { Id = 100, Position = new Vec3(0.6, 0, config.BallRadius) },
                Players = new[] { new PlayerState { Id = 1, OwnerControllerId = Controller, Capabilities = capabilities, Position = Vec2.Zero } },
            };
        }

        private static PushCurve Case(SimConfig config, double pushSpeed, string profile, PlayerCapabilities body, double control)
        {
            var curve = new PushCurve { PushSpeed = pushSpeed, Profile = profile, BallControl = control };
            var sim = new Simulator(config, Start(config, body, control));
            var events = new List<SimEvent>();
            long ticks = (long)(RunSeconds * config.TickRate);
            long pushedAt = -1, flickFrom = -1, flickSpan = -1;
            int pushIndex = 0;
            long lostTicks = (long)(LostSeconds * config.TickRate);
            double pushedFrom = 0, cycleSeconds = 0, cycleMetres = 0;
            int cycles = 0, beyond = 0;
            for (long t = 0; t < ticks; t++)
            {
                events.Clear();
                sim.Step(new[] { new Command(Controller, sim.Tick, Chase(sim.PeekState())) }, events);
                SimState s = sim.PeekState();
                bool touched = false, pushed = false;
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Touched) touched = true;
                    if (e.Kind == SimEventKind.Pushed) pushed = true;
                }

                if (touched)
                {
                    curve.Touches++;
                    if (pushedAt >= 0)
                    {
                        cycles++;
                        cycleSeconds += (t - pushedAt) / (double)config.TickRate;
                        cycleMetres += s.Players[0].Position.X - pushedFrom;
                        if (t - pushedAt > lostTicks) curve.Lost = true;
                        // The flicks' cycle: the second push's where there is one, else the first's.
                        if (pushIndex <= 2)
                        {
                            flickFrom = pushedAt;
                            flickSpan = t - pushedAt;
                        }

                        pushedAt = -1;
                    }
                }

                if (pushed)
                {
                    curve.Pushes++;
                    pushedAt = t;
                    pushedFrom = s.Players[0].Position.X;
                    pushIndex++;
                    if (flickFrom < 0) flickFrom = t;
                }

                double separation = SimMath.Sqrt((s.Ball.Position.Horizontal - s.Players[0].Position).LengthSquared);
                if (separation > curve.PeakSeparationMetres) curve.PeakSeparationMetres = separation;
                if (separation > config.ControlReach) beyond++;
            }

            curve.BeyondReachShare = beyond / (double)ticks;
            if (pushedAt >= 0 && ticks - pushedAt > lostTicks) curve.Lost = true;
            if (cycles > 0)
            {
                curve.CycleSeconds = cycleSeconds / cycles;
                curve.CycleMetres = cycleMetres / cycles;
            }

            // Sprint flicks through one cycle: asked for at each step from that cycle's push on, or,
            // with the push off, at each step of one touch interval from the fourth second.
            long from = flickFrom >= 0 ? flickFrom : 4 * config.TickRate;
            long span = flickFrom >= 0 ? (flickSpan > 0 ? flickSpan : ticks - flickFrom - config.TickRate) : config.TouchIntervalTicks;
            int connected = 0;
            long closes = -1, reopens = -1;
            bool missedYet = false;
            for (long offset = 0; offset < span; offset++)
            {
                bool hit = FlickConnects(config, body, control, from + offset);
                if (hit)
                {
                    connected++;
                    if (!missedYet) closes = offset;
                    else if (reopens < 0) reopens = offset;
                }
                else
                {
                    missedYet = true;
                    reopens = -1;
                }
            }

            curve.FlickConnectShare = span > 0 ? connected / (double)span : double.NaN;
            if (missedYet && closes >= 0) curve.FlickWindowClosesSeconds = closes / (double)config.TickRate;
            if (missedYet && reopens >= 0) curve.FlickWindowReopensSeconds = reopens / (double)config.TickRate;
            return curve;
        }

        // The stick of someone chasing the ball: full, straight at it. A push's direction error
        // sends the ball off the footballer's line, and nobody would run straight past it.
        private static Vec2 Chase(SimState s)
        {
            Vec2 to = s.Ball.Position.Horizontal - s.Players[0].Position;
            double distance = SimMath.Sqrt(to.LengthSquared);
            return distance > 0.05 ? to * (1 / distance) : new Vec2(1, 0);
        }

        // Whether a sprint flick asked for on this step connects.
        private static bool FlickConnects(SimConfig config, PlayerCapabilities body, double control, long kickAt)
        {
            var sim = new Simulator(config, Start(config, body, control));
            var events = new List<SimEvent>();
            long until = kickAt + config.KickWindUpTicks + 2;
            for (long t = 0; t <= until; t++)
            {
                events.Clear();
                KickIntent? kick = t == kickAt ? KickIntent.AlongHeading(0.6, 0) : (KickIntent?)null;
                sim.Step(new[] { new Command(Controller, sim.Tick, Chase(sim.PeekState()), kick) }, events);
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Kicked) return true;
                    if (e.Kind == SimEventKind.KickMissed) return false;
                }
            }

            return false;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<PushCurve> curves)
        {
            w.Write("config_hash,push_speed_mps,profile,ball_control,pushes,touches,cycle_s,cycle_m,peak_separation_m,beyond_reach_share,lost,flick_connect_share,flick_closes_s,flick_reopens_s\n");
            foreach (PushCurve c in curves)
            {
                w.Write(string.Join(",",
                    ConfigOf(config, c.PushSpeed).Hash.ToString("x16", CultureInfo.InvariantCulture), F(c.PushSpeed), c.Profile, F(c.BallControl),
                    c.Pushes.ToString(CultureInfo.InvariantCulture), c.Touches.ToString(CultureInfo.InvariantCulture), F(c.CycleSeconds), F(c.CycleMetres),
                    F(c.PeakSeparationMetres), F(c.BeyondReachShare), c.Lost ? "yes" : "no", F(c.FlickConnectShare), F(c.FlickWindowClosesSeconds), F(c.FlickWindowReopensSeconds)));
                w.Write('\n');
            }
        }

        /// <summary>One line for the console: the bench profile at each push speed.</summary>
        public static string KeyNumbers(IReadOnlyList<PushCurve> curves)
        {
            var parts = new List<string>();
            foreach (PushCurve c in curves)
            {
                if (c.Profile != "bench") continue;
                parts.Add(c.PushSpeed == 0
                    ? string.Format(CultureInfo.InvariantCulture, "off: peak {0:F2} m, flicks {1:P0}", c.PeakSeparationMetres, c.FlickConnectShare)
                    : string.Format(CultureInfo.InvariantCulture, "{0:F1} m/s: {1}, peak {2:F2} m, flicks {3:P0}", c.PushSpeed,
                        c.Lost ? "lost" : string.Format(CultureInfo.InvariantCulture, "cycle {0:F2} s", c.CycleSeconds), c.PeakSeparationMetres, c.FlickConnectShare));
            }

            return string.Join("; ", parts);
        }

        private static string F(double value) => double.IsNaN(value) ? "" : value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
