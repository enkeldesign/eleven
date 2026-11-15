// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One standing challenge measured (#84).</summary>
    public sealed class ChallengeCase
    {
        public string Model = "";

        /// <summary>degrees. Where the challenger comes from, against the dribbler's run: 0 from in front, 180 from behind.</summary>
        public double ApproachDegrees;

        /// <summary>m/s. The dribbler's speed with the ball: 0 standing, a jog or a sprint.</summary>
        public double DribblerSpeed;

        public string Profile = "";
        public double Tackling;

        /// <summary><c>won</c>, <c>missed</c> or <c>none</c> (no challenge started).</summary>
        public string Outcome = "";

        /// <summary>s from the start until he committed.</summary>
        public double CommitSeconds = double.NaN;

        /// <summary>m. How far the ball was from him as the commit ended.</summary>
        public double ReachMetres = double.NaN;

        /// <summary>m/s. The ball's speed just after a won challenge.</summary>
        public double BallSpeed = double.NaN;

        /// <summary>s from the challenge's end until the dribbler touched the ball again, if he did within 2 s.</summary>
        public double DribblerBackSeconds = double.NaN;
    }

    /// <summary>
    /// The standing challenge (#84), measured: a dribbler running with the ball under FREE (or
    /// ATTACHED), and a challenger who runs straight at the ball from an approach angle, at three
    /// tackling profiles. Each case records whether he committed and when, how far off the ball was
    /// as the commit ended, the outcome, and whether the dribbler got the ball back. Technical
    /// evidence for the bench, not feel.
    /// </summary>
    public static class ChallengeBench
    {
        public static readonly IReadOnlyList<double> Approaches = new[] { 0.0, 45, 90, 135, 180 };

        public static readonly IReadOnlyList<double> Speeds = new[] { 0.0, 3.5, 7.0 };

        public const double GoodTackling = 0.9, PoorTackling = 0.3;

        /// <summary>m. How far from the ball the challenger starts.</summary>
        public const double StartDistance = 4;

        private const ulong Seed = 0xC4A11E46EUL;
        private const int Dribbler = 1, Challenger = 2;

        public static List<ChallengeCase> Characterise(SimConfig config)
        {
            var cases = new List<ChallengeCase>();
            double bench = PlayerCapabilities.Reference.Tackling;
            foreach (ControlModel model in new[] { ControlModel.Free, ControlModel.Attached })
            foreach (double approach in Approaches)
            foreach (double speed in Speeds)
            foreach ((string profile, double tackling) in new[] { ("good", GoodTackling), ("bench", bench), ("poor", PoorTackling) })
            {
                cases.Add(Case(config.WithControlModel(model), approach, speed, profile, tackling));
            }

            return cases;
        }

        public static ChallengeCase Case(SimConfig config, double approach, double speed, string profile, double tackling)
        {
            var result = new ChallengeCase
            {
                Model = config.ControlModel == ControlModel.Attached ? "attached" : "free",
                ApproachDegrees = approach, DribblerSpeed = speed, Profile = profile, Tackling = tackling,
            };

            // The dribbler runs along +x with the ball a stride ahead; the challenger comes at the
            // ball from the approach angle, measured from the dribbler's line of run.
            double stick = speed / PlayerCapabilities.Reference.MaxSpeed;
            var ball = new Vec2(0.6, 0);
            double a = approach * Math.PI / 180;
            Vec2 from = ball + new Vec2(SimMath.Cos(a), SimMath.Sin(a)) * StartDistance;
            var state = new SimState
            {
                Seed = Seed,
                Ball = new BallState { Id = 100, Position = new Vec3(ball.X, ball.Y, config.BallRadius), Velocity = new Vec3(speed, 0, 0), LastPlayedBy = Dribbler },
                Players = new[]
                {
                    new PlayerState { Id = Dribbler, OwnerControllerId = Dribbler, TeamId = 1, Capabilities = PlayerCapabilities.Reference, Velocity = new Vec2(speed, 0) },
                    new PlayerState
                    {
                        Id = Challenger, OwnerControllerId = Challenger, TeamId = 2, Capabilities = PlayerCapabilities.Reference.With("tackling", tackling),
                        Position = from, Heading = a + Math.PI,
                    },
                },
            };

            var sim = new Simulator(config, state);
            var events = new List<SimEvent>();
            long ended = -1;
            for (int t = 0; t < 4 * config.TickRate; t++)
            {
                SimState s = sim.PeekState();
                Vec2 toBall = s.Ball.Position.Horizontal - s.Players[1].Position;
                double length = SimMath.Sqrt(toBall.LengthSquared);
                Vec2 chase = length > 0 ? toBall * (1 / length) : Vec2.Zero;
                events.Clear();
                sim.Step(new[] { new Command(Dribbler, sim.Tick, new Vec2(stick, 0)), new Command(Challenger, sim.Tick, chase) }, events);
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.ChallengeStarted && e.Subject == Challenger && double.IsNaN(result.CommitSeconds)) result.CommitSeconds = e.Tick * config.TickSeconds;
                    if ((e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.ChallengeMissed) && e.Subject == Challenger && result.Outcome.Length == 0)
                    {
                        SimState after = sim.PeekState();
                        result.Outcome = e.Kind == SimEventKind.ChallengeWon ? "won" : "missed";
                        result.ReachMetres = SimMath.Sqrt((after.Ball.Position.Horizontal - after.Players[1].Position).LengthSquared);
                        if (e.Kind == SimEventKind.ChallengeWon) result.BallSpeed = SimMath.Sqrt(after.Ball.Velocity.Horizontal.LengthSquared);
                        ended = e.Tick;
                    }

                    if (ended >= 0 && double.IsNaN(result.DribblerBackSeconds) && e.Subject == Dribbler && e.Tick > ended && e.Tick - ended <= 2 * config.TickRate
                        && (e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Attached))
                    {
                        result.DribblerBackSeconds = (e.Tick - ended) * config.TickSeconds;
                    }
                }

                if (ended >= 0 && sim.Tick - ended > 2 * config.TickRate) break;
            }

            if (result.Outcome.Length == 0) result.Outcome = "none";
            return result;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<ChallengeCase> cases)
        {
            w.WriteLine("config_hash,model,approach_deg,dribbler_mps,profile,tackling,outcome,commit_s,reach_m,ball_mps,dribbler_back_s");
            string F(double v) => double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);
            foreach (ChallengeCase c in cases)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1},{2:F0},{3:F1},{4},{5:F3},{6},{7},{8},{9},{10}",
                    config.Hash, c.Model, c.ApproachDegrees, c.DribblerSpeed, c.Profile, c.Tackling, c.Outcome, F(c.CommitSeconds), F(c.ReachMetres), F(c.BallSpeed), F(c.DribblerBackSeconds)));
            }
        }

        /// <summary>By model and profile: challenges won, missed and not started.</summary>
        public static string KeyNumbers(IReadOnlyList<ChallengeCase> cases)
        {
            var parts = new List<string>();
            foreach (string model in new[] { "free", "attached" })
            foreach (string profile in new[] { "good", "bench", "poor" })
            {
                int won = 0, missed = 0, none = 0;
                foreach (ChallengeCase c in cases)
                {
                    if (c.Model != model || c.Profile != profile) continue;
                    if (c.Outcome == "won") won++;
                    else if (c.Outcome == "missed") missed++;
                    else none++;
                }

                parts.Add($"{model}/{profile}: {won} won, {missed} missed, {none} none");
            }

            return string.Join("; ", parts);
        }
    }
}
