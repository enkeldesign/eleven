// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One free pass: a quick pass with no receiver, from one kind of run (#65).</summary>
    public sealed class FreePassCurve
    {
        /// <summary><c>free</c> or <c>attached</c>.</summary>
        public string Model = "";

        /// <summary><c>standing</c>, <c>walking</c>, <c>jogging</c>, <c>sprinting</c> or <c>turning</c>.</summary>
        public string Case = "";

        /// <summary><c>reference</c> or <c>weak-kick</c>.</summary>
        public string Profile = "";

        public double KickPower;

        /// <summary>m/s, the passer's speed on the step the tap is accepted.</summary>
        public double PasserSpeedMps;

        /// <summary>m/s, the ball's speed after the contact step.</summary>
        public double LaunchSpeedMps;

        /// <summary>m, the ball's lead over the passer along the pass's line: at contact, then 0.25, 0.5 and 1 s after.</summary>
        public double LeadAtContactMetres;

        public double LeadQuarterMetres;

        public double LeadHalfMetres;

        public double LeadSecondMetres;

        /// <summary>Touches and takes of the ball by anyone in the second after contact; 0 when the pass is released.</summary>
        public int TouchesAfter;
    }

    /// <summary>
    /// The free pass's characterisation (#65): a quick pass with no team-mate in view, tapped while
    /// standing, walking, jogging, sprinting and turning, under FREE and ATTACHED, by the reference
    /// player and a weak kicker. A technical reference of the current rule, not approved feel
    /// (docs/simulation/pass.md).
    /// </summary>
    public static class FreePassCurves
    {
        private const int Controller = 1;
        private const ulong Seed = 0x0E1E7E2065UL;

        public static List<FreePassCurve> Characterise(SimConfig config, PlayerCapabilities profile)
        {
            var curves = new List<FreePassCurve>();
            foreach (ControlModel model in new[] { ControlModel.Free, ControlModel.Attached })
            {
                SimConfig c = config.WithControlModel(model);
                foreach ((string name, PlayerCapabilities capabilities) in new[] { ("reference", profile), ("weak-kick", profile.With("kickPower", 0.5)) })
                {
                    curves.Add(Pass(c, "standing", name, capabilities, Vec2.Zero, Vec2.Zero));
                    curves.Add(Pass(c, "walking", name, capabilities, new Vec2(0.3, 0), new Vec2(0.3, 0)));
                    curves.Add(Pass(c, "jogging", name, capabilities, new Vec2(0.5, 0), new Vec2(0.5, 0)));
                    curves.Add(Pass(c, "sprinting", name, capabilities, new Vec2(1, 0), new Vec2(1, 0)));
                    curves.Add(Pass(c, "turning", name, capabilities, new Vec2(1, 0), new Vec2(0, 1)));
                }
            }

            return curves;
        }

        // 2 s with the stick at `run`, then the stick at `then` and a tap 3 steps later; followed for
        // a second after contact.
        private static FreePassCurve Pass(SimConfig config, string name, string profile, PlayerCapabilities capabilities, Vec2 run, Vec2 then)
        {
            var curve = new FreePassCurve
            {
                Model = config.ControlModel == ControlModel.Attached ? "attached" : "free",
                Case = name,
                Profile = profile,
                KickPower = capabilities.KickPower,
            };

            var sim = new Simulator(config, new SimState
            {
                Seed = Seed,
                Ball = new BallState { Id = 100, Position = new Vec3(0.6, 0, config.BallRadius) },
                Players = new[] { new PlayerState { Id = 1, OwnerControllerId = Controller, Capabilities = capabilities } },
            });

            long changeAt = 2 * config.TickRate, tapAt = changeAt + 3;
            long contact = -1;
            Vec2 line = Vec2.Zero;
            var step = new List<Command>(1);
            var events = new List<SimEvent>();
            for (long t = 0; t < tapAt + config.KickWindUpTicks + 2 + config.TickRate; t++)
            {
                step.Clear();
                step.Add(new Command(Controller, sim.Tick, t < changeAt ? run : then, quickPass: t == tapAt));
                events.Clear();
                sim.Step(step, events);
                SimState s = sim.PeekState();
                if (t == tapAt) curve.PasserSpeedMps = SimMath.Sqrt(s.Players[0].Velocity.LengthSquared);

                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Kicked && contact < 0)
                    {
                        contact = t;
                        curve.LaunchSpeedMps = SimMath.Sqrt(s.Ball.Velocity.Horizontal.LengthSquared);
                        line = curve.LaunchSpeedMps > 0 ? s.Ball.Velocity.Horizontal * (1 / curve.LaunchSpeedMps) : Vec2.Zero;
                    }
                    else if (contact >= 0 && t <= contact + config.TickRate && (e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Attached))
                    {
                        curve.TouchesAfter++;
                    }
                }

                if (contact < 0) continue;
                Vec2 offset = s.Ball.Position.Horizontal - s.Players[0].Position;
                double lead = offset.X * line.X + offset.Y * line.Y;
                long since = t - contact;
                if (since == 0) curve.LeadAtContactMetres = lead;
                if (since == config.TickRate / 4) curve.LeadQuarterMetres = lead;
                if (since == config.TickRate / 2) curve.LeadHalfMetres = lead;
                if (since == config.TickRate) curve.LeadSecondMetres = lead;
            }

            return curve;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<FreePassCurve> curves)
        {
            w.Write("config_hash,model,case,profile,kick_power,passer_speed_mps,launch_speed_mps,lead_contact_m,lead_0_25_m,lead_0_5_m,lead_1_0_m,touches_after\n");
            foreach (FreePassCurve c in curves)
            {
                SimConfig under = config.WithControlModel(c.Model == "attached" ? ControlModel.Attached : ControlModel.Free);
                w.Write(string.Join(",",
                    under.Hash.ToString("x16", CultureInfo.InvariantCulture), c.Model, c.Case, c.Profile, F(c.KickPower),
                    F(c.PasserSpeedMps), F(c.LaunchSpeedMps), F(c.LeadAtContactMetres), F(c.LeadQuarterMetres), F(c.LeadHalfMetres), F(c.LeadSecondMetres),
                    c.TouchesAfter.ToString(CultureInfo.InvariantCulture)));
                w.Write('\n');
            }
        }

        /// <summary>The figures a reader wants first, one line.</summary>
        public static string KeyNumbers(IReadOnlyList<FreePassCurve> curves)
        {
            var parts = new List<string>();
            foreach (FreePassCurve c in curves)
            {
                if (c.Profile != "reference") continue;
                parts.Add(FormattableString.Invariant($"{c.Model}/{c.Case}: passer {c.PasserSpeedMps:F1} m/s, launch {c.LaunchSpeedMps:F1} m/s, lead {c.LeadAtContactMetres:F2} → {c.LeadSecondMetres:F2} m in 1 s"));
            }

            return string.Join("; ", parts);
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
