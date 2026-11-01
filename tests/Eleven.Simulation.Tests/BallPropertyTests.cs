using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Fuzzed properties of the ball model (#24), over seeded random balls and configs within the
    /// documented valid ranges (<see cref="SimConfig.Tunables"/>), with and without air drag.
    ///
    /// Each property has a companion test that feeds its check a deliberately broken trajectory and
    /// shows the check catches it, so a passing property means something.
    ///
    /// The energy allowance is the integrator's known numerical error, derived in
    /// docs/simulation/ball.md: per step, at most dt²·|m|² + g²·dt² (m: the Magnus acceleration at
    /// the start of the step), plus g·dt·(|vz| + (|m| + g)·dt) on a step that bounces (the ball is
    /// put back on the ground), plus 1e-9 relative rounding.
    /// </summary>
    public class BallPropertyTests
    {
        private const int Cases = 400;
        private const int StepsPerCase = 600;
        private const int BallId = 100;

        /// <summary>One step of one fuzzed ball: the state before and after, and whether it bounced.</summary>
        private readonly struct Observed
        {
            public Observed(BallState before, BallState after, bool bounced)
            {
                Before = before;
                After = after;
                Bounced = bounced;
            }

            public BallState Before { get; }
            public BallState After { get; }
            public bool Bounced { get; }
        }

        private sealed class Case
        {
            public Case(int seed, SimConfig config, BallState ball)
            {
                Seed = seed;
                Config = config;
                Ball = ball;
            }

            public int Seed { get; }
            public SimConfig Config { get; }
            public BallState Ball { get; }
            public override string ToString() => $"seed {Seed}, config {Config.Hash:x16}";
        }

        // ---- The fuzz ----------------------------------------------------------------------

        // Half the cases have no drag, a quarter a realistic 0.01–0.03 and a quarter anywhere in the
        // valid range. Balls start in the air, rolling, or at rest, with arbitrary spin.
        private static IEnumerable<Case> Cases_()
        {
            for (int seed = 1; seed <= Cases; seed++)
            {
                var random = new Random(seed);
                var overrides = new List<KeyValuePair<string, double>>();
                foreach (TunableParameter p in SimConfig.Tunables)
                {
                    overrides.Add(new KeyValuePair<string, double>(p.Name, p.Min + random.NextDouble() * (p.Max - p.Min)));
                }

                double drag = (seed % 4) switch
                {
                    0 or 1 => 0,
                    2 => 0.01 + 0.02 * random.NextDouble(),
                    _ => random.NextDouble(),
                };
                overrides.Add(new KeyValuePair<string, double>("airDrag", drag));
                SimConfig config = new SimConfig().With(overrides);

                double r = config.BallRadius;
                double U(double range) => (2 * random.NextDouble() - 1) * range;
                var spin = new Vec3(U(100), U(100), U(100));
                BallState ball = (seed % 5) switch
                {
                    0 => new BallState { Id = BallId, Position = new Vec3(U(60), U(40), r), Spin = spin },
                    1 => new BallState { Id = BallId, Position = new Vec3(U(60), U(40), r), Velocity = new Vec3(U(40), U(40), 0), Spin = spin },
                    _ => new BallState
                    {
                        Id = BallId,
                        Position = new Vec3(U(60), U(40), r + random.NextDouble() * 30),
                        Velocity = new Vec3(U(40), U(40), U(40)),
                        Spin = spin,
                    },
                };

                yield return new Case(seed, config, ball);
            }
        }

        private static List<Observed> Run(Case c)
        {
            var sim = new Simulator(c.Config, new SimState { Ball = c.Ball });
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            var observed = new List<Observed>(StepsPerCase);
            for (int i = 0; i < StepsPerCase; i++)
            {
                BallState before = sim.PeekState().Ball;
                events.Clear();
                sim.Step(commands, events);
                observed.Add(new Observed(before, sim.PeekState().Ball, events.Any(e => e.Kind == SimEventKind.Bounced)));
            }

            return observed;
        }

        private static readonly List<(Case Case, List<Observed> Steps)> Fuzzed =
            Cases_().Select(c => (c, Run(c))).ToList();

        // ---- The checks: each returns the first violation, or null --------------------------

        private static string? AboveGround(SimConfig config, Observed s) =>
            s.After.Position.Z < config.BallRadius ? $"centre at z={s.After.Position.Z}, radius {config.BallRadius}" : null;

        private static string? EnergyWithinAllowance(SimConfig config, Observed s)
        {
            double before = Energy(config, s.Before), after = Energy(config, s.After);
            double allowed = Allowance(config, s) + 1e-9 * (1 + before);
            return after - before > allowed ? $"energy {before} -> {after} (+{after - before}, allowed {allowed})" : null;
        }

        private static string? BounceCreatesNoEnergy(SimConfig config, Observed s) =>
            s.Bounced ? EnergyWithinAllowance(config, s) : null;

        private static string? SpinNeverGrows(SimConfig config, Observed s) =>
            s.After.Spin.LengthSquared > s.Before.Spin.LengthSquared ? $"spin² {s.Before.Spin.LengthSquared} -> {s.After.Spin.LengthSquared}" : null;

        private static string? RestStaysRest(SimConfig config, Observed s)
        {
            bool atRest = s.Before.Position.Z == config.BallRadius && IsZero(s.Before.Velocity);
            bool stillAtRest = s.After.Position.Z == config.BallRadius && IsZero(s.After.Velocity)
                && s.After.Position.X == s.Before.Position.X && s.After.Position.Y == s.Before.Position.Y;
            return atRest && !stillAtRest ? $"left rest: {s.Before.Position} -> {s.After.Position}, v {s.After.Velocity}" : null;
        }

        private static string? StaysFinite(SimConfig config, Observed s) =>
            Finite(s.After.Position) && Finite(s.After.Velocity) && Finite(s.After.Spin) ? null : $"non-finite after: {s.After.Position} {s.After.Velocity} {s.After.Spin}";

        // Per unit mass: kinetic plus potential above the resting height.
        private static double Energy(SimConfig config, BallState b) =>
            0.5 * b.Velocity.LengthSquared + config.Gravity * (b.Position.Z - config.BallRadius);

        private static double Allowance(SimConfig config, Observed s)
        {
            double dt = config.TickSeconds, g = config.Gravity;
            double magnus = Math.Sqrt(Vec3.Cross(s.Before.Spin, s.Before.Velocity).LengthSquared) * config.MagnusCoefficient;
            double allowed = dt * dt * magnus * magnus + g * g * dt * dt;
            if (s.Bounced) allowed += g * dt * (Math.Abs(s.Before.Velocity.Z) + (magnus + g) * dt);
            return allowed;
        }

        private static bool IsZero(Vec3 v) => v.X == 0 && v.Y == 0 && v.Z == 0;

        private static bool Finite(Vec3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        // ---- Running a check ----------------------------------------------------------------

        private delegate string? Check(SimConfig config, Observed step);

        private static string? FirstViolation(Check check, Func<Observed, Observed>? mutate = null)
        {
            foreach ((Case c, List<Observed> steps) in Fuzzed)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    Observed s = mutate == null ? steps[i] : mutate(steps[i]);
                    string? violation = check(c.Config, s);
                    if (violation != null) return $"{c}, step {i}: {violation}";
                }
            }

            return null;
        }

        private static void Holds(Check check) => Assert.Null(FirstViolation(check));

        private static void CatchesMutant(Check check, Func<Observed, Observed> mutate) =>
            Assert.NotNull(FirstViolation(check, mutate));

        private static Observed WithAfter(Observed s, Func<BallState, BallState> change) =>
            new Observed(s.Before, change(s.After), s.Bounced);

        // ---- The fuzz covers what it claims ---------------------------------------------------

        [Fact]
        public void The_fuzz_covers_bounces_rest_drag_and_no_drag()
        {
            Assert.Contains(Fuzzed, f => f.Steps.Any(s => s.Bounced));
            Assert.Contains(Fuzzed, f => f.Steps.Any(s => IsZero(s.After.Velocity)));
            Assert.Contains(Fuzzed, f => f.Case.Config.AirDrag == 0);
            Assert.Contains(Fuzzed, f => f.Case.Config.AirDrag > 0 && f.Case.Config.AirDrag < 0.05);
            Assert.Contains(Fuzzed, f => f.Case.Config.AirDrag > 0.5);
        }

        // ---- Properties, each with its mutant -------------------------------------------------

        [Fact]
        public void The_ball_never_sinks_below_the_ground() => Holds(AboveGround);

        [Fact]
        public void The_ground_check_catches_a_sinking_ball() =>
            CatchesMutant(AboveGround, s => WithAfter(s, b => { b.Position = new Vec3(b.Position.X, b.Position.Y, b.Position.Z - 0.2); return b; }));

        [Fact]
        public void A_bounce_creates_no_energy() => Holds(BounceCreatesNoEnergy);

        [Fact]
        public void The_bounce_check_catches_a_bounce_that_returns_extra_speed() =>
            CatchesMutant(BounceCreatesNoEnergy, s => s.Bounced
                ? WithAfter(s, b => { b.Velocity = new Vec3(b.Velocity.X, b.Velocity.Y, b.Velocity.Z * 3 + 1); return b; })
                : s);

        [Fact]
        public void Without_a_kick_energy_never_grows_beyond_the_integration_allowance() => Holds(EnergyWithinAllowance);

        [Fact]
        public void The_energy_check_catches_a_ball_that_speeds_up() =>
            CatchesMutant(EnergyWithinAllowance, s => WithAfter(s, b => { b.Velocity = b.Velocity * 1.01; return b; }));

        [Fact]
        public void Spin_never_grows() => Holds(SpinNeverGrows);

        [Fact]
        public void The_spin_check_catches_growing_spin() =>
            CatchesMutant(SpinNeverGrows, s => WithAfter(s, b => { b.Spin = b.Spin * 1.001; return b; }));

        [Fact]
        public void A_ball_at_rest_stays_at_rest() => Holds(RestStaysRest);

        [Fact]
        public void The_rest_check_catches_a_resting_ball_that_creeps() =>
            CatchesMutant(RestStaysRest, s => WithAfter(s, b => { b.Position = new Vec3(b.Position.X + 1e-6, b.Position.Y, b.Position.Z); return b; }));

        [Fact]
        public void Finite_inputs_stay_finite() => Holds(StaysFinite);

        [Fact]
        public void The_finiteness_check_catches_a_non_finite_value() =>
            CatchesMutant(StaysFinite, s => WithAfter(s, b => { b.Velocity = new Vec3(double.NaN, b.Velocity.Y, b.Velocity.Z); return b; }));

        [Fact]
        public void Every_run_is_deterministic()
        {
            foreach ((Case c, List<Observed> first) in Fuzzed.Take(100))
            {
                Assert.True(SameTrajectory(first, Run(c)), $"{c}: a second run differed");
            }
        }

        [Fact]
        public void The_determinism_check_catches_a_run_that_differs()
        {
            (Case c, List<Observed> first) = Fuzzed[0];
            List<Observed> second = Run(c);
            second[second.Count / 2] = WithAfter(second[second.Count / 2], b => { b.Velocity = new Vec3(b.Velocity.X + 1e-12, b.Velocity.Y, b.Velocity.Z); return b; });
            Assert.False(SameTrajectory(first, second));
        }

        private static bool SameTrajectory(List<Observed> a, List<Observed> b) =>
            a.Count == b.Count && a.Zip(b, (x, y) => Same(x.After, y.After) && x.Bounced == y.Bounced).All(same => same);

        private static bool Same(BallState a, BallState b) =>
            Same(a.Position, b.Position) && Same(a.Velocity, b.Velocity) && Same(a.Spin, b.Spin);

        private static bool Same(Vec3 a, Vec3 b) =>
            BitConverter.DoubleToInt64Bits(a.X) == BitConverter.DoubleToInt64Bits(b.X)
            && BitConverter.DoubleToInt64Bits(a.Y) == BitConverter.DoubleToInt64Bits(b.Y)
            && BitConverter.DoubleToInt64Bits(a.Z) == BitConverter.DoubleToInt64Bits(b.Z);
    }
}
