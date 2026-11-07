using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Fuzzed properties of the goal (#68): seeded random shots at the goal, on the ground and in
    /// the air, many aimed at the frame itself, under goal sizes, post widths, restitutions and ball
    /// radii drawn from their whole valid ranges.
    ///
    /// Each property has a companion test that feeds its check a deliberately broken trajectory and
    /// shows the check catches it, so a passing property means something. The energy allowance on a
    /// step the ball hits the frame is the bounce's (docs/simulation/ball.md): the contact stops the
    /// ball on the step's path, as the ground puts it back at its height.
    /// </summary>
    public class GoalPropertyTests
    {
        private const int Shots = 300;
        private const int StepsPerShot = 240;

        private readonly struct Observed
        {
            public Observed(BallState before, BallState after, List<SimEvent> events)
            {
                Before = before;
                After = after;
                Events = events;
            }

            public BallState Before { get; }
            public BallState After { get; }
            public List<SimEvent> Events { get; }
            public bool Has(SimEventKind kind) => Events.Any(e => e.Kind == kind);
        }

        private sealed class Shot
        {
            public Shot(int seed, SimConfig config, BallState ball)
            {
                Seed = seed;
                Config = config;
                Ball = ball;
            }

            public int Seed { get; }
            public SimConfig Config { get; }
            public BallState Ball { get; }
            public override string ToString() => $"shot {Seed}, config {Config.Hash:x16}";
        }

        // ---- The fuzz ----------------------------------------------------------------------

        private static IEnumerable<Shot> Shots_()
        {
            for (int seed = 1; seed <= Shots; seed++)
            {
                var random = new Random(seed);
                double Between(double lo, double hi) => lo + random.NextDouble() * (hi - lo);
                var overrides = new List<KeyValuePair<string, double>>();
                foreach (TunableParameter p in SimConfig.Tunables.Where(p => p.Group == TunableParameter.GoalGroup))
                {
                    overrides.Add(new KeyValuePair<string, double>(p.Name, Between(p.Min, p.Max)));
                }

                overrides.Add(new KeyValuePair<string, double>("ballRadius", Between(0.08, 0.2)));
                SimConfig config = new SimConfig().With(overrides);

                // Where the shot meets the goal-line plane: a third aimed at a post's or the bar's
                // axis, the rest anywhere around the mouth.
                double r = config.BallRadius;
                double y, z;
                switch (seed % 6)
                {
                    case 0: y = config.PostAxisY + Between(-0.3, 0.3); z = Between(r, config.GoalHeight); break;
                    case 1: y = -config.PostAxisY + Between(-0.3, 0.3); z = Between(r, config.GoalHeight); break;
                    case 2: y = Between(-config.PostAxisY, config.PostAxisY); z = config.CrossbarAxisZ + Between(-0.3, 0.3); break;
                    default: y = Between(-config.PostAxisY - 1.5, config.PostAxisY + 1.5); z = Between(r, config.CrossbarAxisZ + 1); break;
                }

                var target = new Vec3(config.GoalLineX, y, z);
                var start = new Vec3(config.GoalLineX - Between(0.5, 12), Between(-8, 8), seed % 4 == 0 ? r : Between(r, 3.5));
                Vec3 line = target - start;
                double length = Math.Sqrt(line.LengthSquared);
                double speed = Between(3, 45);

                // Lift it a little so gravity doesn't drop every shot short of its target.
                double flight = length / speed;
                var velocity = line * (speed / length) + new Vec3(0, 0, start.Z > r ? 0.5 * config.Gravity * flight : 0);
                var spin = new Vec3(Between(-40, 40), Between(-40, 40), Between(-40, 40));
                yield return new Shot(seed, config, new BallState { Id = 100, Position = start, Velocity = velocity, Spin = spin });
            }
        }

        private static List<Observed> Run(Shot s)
        {
            var sim = new Simulator(s.Config, new SimState { Ball = s.Ball });
            var observed = new List<Observed>(StepsPerShot);
            for (int i = 0; i < StepsPerShot; i++)
            {
                BallState before = sim.PeekState().Ball;
                var events = new List<SimEvent>();
                sim.Step(Array.Empty<Command>(), events);
                observed.Add(new Observed(before, sim.PeekState().Ball, events));
            }

            return observed;
        }

        private static readonly List<(Shot Shot, List<Observed> Steps)> Fuzzed = Shots_().Select(s => (s, Run(s))).ToList();

        // ---- The frame, for the checks --------------------------------------------------------

        private static double DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a, ap = p - a;
            double t = (ap.X * ab.X + ap.Y * ab.Y + ap.Z * ab.Z) / ab.LengthSquared;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            Vec3 closest = a + ab * t;
            return Math.Sqrt((p - closest).LengthSquared);
        }

        // How far the ball's centre is from the nearest of the frame's axes.
        private static double FromFrame(SimConfig c, Vec3 p)
        {
            var right = new Vec3(c.FrameAxisX, -c.PostAxisY, 0);
            var left = new Vec3(c.FrameAxisX, c.PostAxisY, 0);
            var rightTop = new Vec3(c.FrameAxisX, -c.PostAxisY, c.CrossbarAxisZ);
            var leftTop = new Vec3(c.FrameAxisX, c.PostAxisY, c.CrossbarAxisZ);
            return Math.Min(DistanceToSegment(p, right, rightTop), Math.Min(DistanceToSegment(p, left, leftTop), DistanceToSegment(p, rightTop, leftTop)));
        }

        // ---- The checks: each returns the first violation, or null --------------------------

        private static string? NeverInsideTheFrame(SimConfig c, Observed s)
        {
            double contact = c.BallRadius + c.PostWidth / 2;
            double from = FromFrame(c, s.After.Position);
            bool startedClear = FromFrame(c, s.Before.Position) >= contact - 1e-9;
            return startedClear && from < contact - 1e-9 ? $"the ball's centre is {from:F4} m from the frame's axis; contact is {contact:F4} m" : null;
        }

        private static string? AContactAddsNoEnergy(SimConfig c, Observed s)
        {
            if (!s.Has(SimEventKind.HitFrame)) return null;
            double before = Energy(c, s.Before), after = Energy(c, s.After);
            double dt = c.TickSeconds, g = c.Gravity;
            double magnus = Math.Sqrt(Vec3.Cross(s.Before.Spin, s.Before.Velocity).LengthSquared) * c.MagnusCoefficient;
            double contact = g * dt * (Math.Abs(s.Before.Velocity.Z) + (magnus + g) * dt);
            double allowed = dt * dt * magnus * magnus + contact * (s.Has(SimEventKind.Bounced) ? 2 : 1) + 1e-9 * (1 + before);
            return after - before > allowed ? $"energy {before} -> {after} (+{after - before}, allowed {allowed})" : null;
        }

        private static string? StaysFinite(SimConfig c, Observed s) =>
            Finite(s.After.Position) && Finite(s.After.Velocity) && Finite(s.After.Spin) ? null : $"non-finite after: {s.After.Position} {s.After.Velocity}";

        private static double Energy(SimConfig c, BallState b) => 0.5 * b.Velocity.LengthSquared + c.Gravity * (b.Position.Z - c.BallRadius);

        private static bool Finite(Vec3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        // Over a whole shot: a goal exactly when the whole ball crosses having crossed with its
        // centre between the posts' axes and under the bar's. Each step's path is the straight line
        // from where it starts to where it ends, contacts included, so the crossing point is exact.
        private static string? GoalsOnlyThroughTheMouth(SimConfig c, List<Observed> steps)
        {
            double L = c.GoalLineX;
            bool? mouth = null;
            for (int i = 0; i < steps.Count; i++)
            {
                Vec3 a = steps[i].Before.Position, b = steps[i].After.Position;
                if (b.X <= L)
                {
                    mouth = null;
                    if (steps[i].Has(SimEventKind.GoalLineCrossed)) return $"step {i}: a crossing with the centre in front of the line";
                    continue;
                }

                if (mouth == null)
                {
                    Vec3 at = a.X <= L ? a + (b - a) * ((L - a.X) / (b.X - a.X)) : a;
                    mouth = Math.Abs(at.Y) < c.PostAxisY && at.Z < c.CrossbarAxisZ;
                }

                bool crossed = steps[i].Has(SimEventKind.GoalLineCrossed), goal = steps[i].Has(SimEventKind.Goal);
                if (goal && !crossed) return $"step {i}: a goal without the whole ball crossing";
                if (crossed && goal != mouth) return $"step {i}: {(goal ? "a goal" : "no goal")} for a ball whose centre crossed {(mouth == true ? "in" : "outside")} the mouth";
            }

            return null;
        }

        // ---- Running a check ----------------------------------------------------------------

        private delegate string? Check(SimConfig config, Observed step);

        private static string? FirstViolation(Check check, Func<Observed, Observed>? mutate = null)
        {
            foreach ((Shot shot, List<Observed> steps) in Fuzzed)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    string? violation = check(shot.Config, mutate == null ? steps[i] : mutate(steps[i]));
                    if (violation != null) return $"{shot}, step {i}: {violation}";
                }
            }

            return null;
        }

        private static Observed WithAfter(Observed s, Func<BallState, BallState> change) => new Observed(s.Before, change(s.After), s.Events);

        // ---- The fuzz covers what it claims ---------------------------------------------------

        [Fact]
        public void The_fuzz_hits_both_posts_and_the_bar_and_scores_and_misses()
        {
            List<SimEvent> all = Fuzzed.SelectMany(f => f.Steps.SelectMany(s => s.Events)).ToList();
            int Hits(FramePart part) => all.Count(e => e.Kind == SimEventKind.HitFrame && e.Other == (int)part);

            Assert.True(Hits(FramePart.RightPost) >= 20, $"{Hits(FramePart.RightPost)} right-post hits");
            Assert.True(Hits(FramePart.LeftPost) >= 20, $"{Hits(FramePart.LeftPost)} left-post hits");
            Assert.True(Hits(FramePart.Crossbar) >= 20, $"{Hits(FramePart.Crossbar)} crossbar hits");
            Assert.True(all.Count(e => e.Kind == SimEventKind.Goal) >= 40, $"{all.Count(e => e.Kind == SimEventKind.Goal)} goals");
            int misses = all.Count(e => e.Kind == SimEventKind.GoalLineCrossed) - all.Count(e => e.Kind == SimEventKind.Goal);
            Assert.True(misses >= 40, $"{misses} crossings outside the mouth");
        }

        // ---- The properties -----------------------------------------------------------------------

        [Fact]
        public void The_ball_never_passes_into_the_frame() => Assert.Null(FirstViolation(NeverInsideTheFrame));

        [Fact]
        public void The_frame_check_catches_a_ball_inside_a_post() =>
            Assert.NotNull(FirstViolation(NeverInsideTheFrame, s =>
                WithAfter(s, b => { b.Position = new Vec3(Fuzzed[0].Shot.Config.FrameAxisX, Fuzzed[0].Shot.Config.PostAxisY, 1); return b; })));

        [Fact]
        public void A_frame_contact_adds_no_energy() => Assert.Null(FirstViolation(AContactAddsNoEnergy));

        [Fact]
        public void The_energy_check_catches_a_contact_that_returns_extra_speed() =>
            Assert.NotNull(FirstViolation(AContactAddsNoEnergy, s => WithAfter(s, b => { b.Velocity *= 1.2; return b; })));

        [Fact]
        public void Finite_shots_stay_finite() => Assert.Null(FirstViolation(StaysFinite));

        [Fact]
        public void A_goal_is_scored_exactly_when_the_ball_crossed_through_the_mouth()
        {
            foreach ((Shot shot, List<Observed> steps) in Fuzzed)
            {
                string? violation = GoalsOnlyThroughTheMouth(shot.Config, steps);
                Assert.True(violation == null, $"{shot}: {violation}");
            }
        }

        [Fact]
        public void The_mouth_check_catches_a_goal_scored_wide()
        {
            // Every crossing called a goal: those outside the mouth must be caught.
            bool caught = Fuzzed.Any(f => GoalsOnlyThroughTheMouth(f.Shot.Config, f.Steps.Select(s =>
                s.Has(SimEventKind.GoalLineCrossed) && !s.Has(SimEventKind.Goal)
                    ? new Observed(s.Before, s.After, s.Events.Append(new SimEvent(0, SimEventKind.Goal, 100)).ToList())
                    : s).ToList()) != null);
            Assert.True(caught);
        }

        [Fact]
        public void Every_shot_is_deterministic()
        {
            foreach ((Shot shot, List<Observed> steps) in Fuzzed.Take(30))
            {
                List<Observed> again = Run(shot);
                Assert.True(steps.Select(s => s.After).SequenceEqual(again.Select(s => s.After)), $"{shot} ran differently");
            }
        }
    }
}
