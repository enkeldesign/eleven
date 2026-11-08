using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The cut with the ball under FREE (#75, docs/simulation/cut.md): when the stick asks for a
    /// change of direction between the cut and turn angles, the footballer touches the ball at once,
    /// whatever the touch cadence, then again a cut interval later, at most once a stride. These pin
    /// the contract and its relationships, not untuned feel values.
    /// </summary>
    public class CutTests
    {
        private const int A = TestWorld.Controller;
        private const double Good = 0.9;
        private const double Bench = 0.7;
        private const double Poor = 0.3;

        private static SimConfig Free(double? cutStrength = null, double? touchError = null, double? cutInterval = null)
        {
            SimConfig config = new SimConfig(controlModel: ControlModel.Free);
            if (cutStrength.HasValue) config = config.With("cutStrength", cutStrength.Value);
            if (touchError.HasValue) config = config.With("touchError", touchError.Value);
            if (cutInterval.HasValue) config = config.With("cutInterval", cutInterval.Value);
            return config;
        }

        private static SimState AtFeet(SimConfig config, double ballControl)
        {
            SimState state = TestWorld.State(config, new Vec3(0.6, 0, config.BallRadius));
            state.Players[0].Capabilities = PlayerCapabilities.Reference.With("ballControl", ballControl);
            return state;
        }

        /// <summary>A run along +x, then the stick turned, at once or swept, and held.</summary>
        private sealed class Run
        {
            public SimConfig Config = null!;
            public Simulator Sim = null!;

            /// <summary>The state before each step: Before[t] is state t.</summary>
            public readonly List<SimState> Before = new List<SimState>();

            public readonly List<List<SimEvent>> Events = new List<List<SimEvent>>();
            public readonly List<ulong> Hashes = new List<ulong>();

            /// <summary>The step on which the stick starts to turn.</summary>
            public int TurnAt;

            public SimState After(int t) => t + 1 < Before.Count ? Before[t + 1] : Sim.PeekState();

            public IEnumerable<int> Steps(SimEventKind kind) =>
                Enumerable.Range(0, Events.Count).Where(t => Events[t].Any(e => e.Kind == kind && e.Subject == TestWorld.PlayerId));

            public IEnumerable<int> Cuts() => Steps(SimEventKind.Cut);

            public IEnumerable<int> Touches() => Steps(SimEventKind.Touched);

            /// <summary>Whether the ball stayed within reach at every step from the turn on.</summary>
            public bool Kept() => Enumerable.Range(TurnAt, Events.Count - TurnAt).All(t => Separation(After(t)) <= Config.ControlReach);
        }

        private static double Separation(SimState s) => Math.Sqrt((s.Ball.Position.Horizontal - s.Players[0].Position).LengthSquared);

        // The stick: along +x, then from TurnAt toward `degrees`, swinging there over `sweep` steps
        // (the first already turned), or a zig-zag of ±degrees flipping every `zig` steps.
        private static Run Cut(SimConfig config, double ballControl, double degrees, double stick = 1, int phase = 0, double seconds = 2,
            int sweep = 0, int zig = 0, Func<int, KickIntent?>? kick = null)
        {
            var run = new Run
            {
                Config = config,
                Sim = new Simulator(config, AtFeet(config, ballControl)),
                TurnAt = 2 * config.TickRate + phase,
            };

            double r = degrees * Math.PI / 180;
            int ticks = run.TurnAt + (int)(seconds * config.TickRate);
            for (int t = 0; t < ticks; t++)
            {
                double a = 0;
                int k = t - run.TurnAt;
                if (k >= 0) a = zig > 0 ? (k / zig % 2 == 0 ? r : -r) : sweep > 1 && k + 1 < sweep ? r * (k + 1) / sweep : r;
                run.Before.Add(run.Sim.PeekState().Clone());
                var events = new List<SimEvent>();
                run.Sim.Step(new[] { new Command(A, t, new Vec2(Math.Cos(a), Math.Sin(a)) * stick, kick?.Invoke(t)) }, events);
                run.Events.Add(events);
                run.Hashes.Add(run.Sim.StateHash());
            }

            return run;
        }

        public static readonly TheoryData<double, double> Cases = new TheoryData<double, double>
        {
            { 45, 0.5 }, { 45, 1 }, { 90, 0.5 }, { 90, 1 },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_cut_touches_the_ball_on_its_first_step_at_every_touch_phase(double degrees, double stick)
        {
            SimConfig config = Free();
            for (int phase = 0; phase < config.TouchIntervalTicks; phase++)
            {
                Run run = Cut(config, Good, degrees, stick, phase, 0.3);
                int first = run.Cuts().FirstOrDefault(-1);
                Assert.True(first == run.TurnAt, $"{degrees}°, stick {stick}, phase {phase}: the cut touch came on step {first}, the cut on {run.TurnAt}");
                Assert.Contains(run.TurnAt, run.Touches());
            }
        }

        [Fact]
        public void The_follow_up_comes_a_cut_interval_later_as_an_ordinary_touch()
        {
            SimConfig config = Free();
            Run run = Cut(config, Good, 90, phase: 5, seconds: 0.5);
            int[] touches = run.Touches().Where(t => t >= run.TurnAt).Take(2).ToArray();
            Assert.Equal(new[] { run.TurnAt, run.TurnAt + config.CutIntervalTicks }, touches);
            Assert.DoesNotContain(touches[1], run.Cuts());

            // A cut interval no shorter than the touch interval leaves the follow-up to the cadence.
            SimConfig slow = Free(cutInterval: config.TouchInterval);
            Run cadence = Cut(slow, Good, 90, phase: 5, seconds: 0.5);
            Assert.Equal(new[] { cadence.TurnAt, cadence.TurnAt + slow.TouchIntervalTicks }, cadence.Touches().Where(t => t >= cadence.TurnAt).Take(2).ToArray());
        }

        [Fact]
        public void There_is_at_most_one_cut_a_stride()
        {
            // A zig-zag of ±45° flipping every 4 steps asks for a new cut at every flip.
            SimConfig config = Free();
            Run run = Cut(config, Good, 45, phase: 3, seconds: 2, zig: 4);
            int[] cuts = run.Cuts().ToArray();
            Assert.True(cuts.Length >= 2, $"only {cuts.Length} cut touches in the zig-zag");
            for (int i = 1; i < cuts.Length; i++)
            {
                Assert.True(cuts[i] - cuts[i - 1] >= config.TouchIntervalTicks, $"cut touches on steps {cuts[i - 1]} and {cuts[i]}");
            }

            Assert.Equal(config.TouchIntervalTicks, run.After(cuts[0]).Players[0].CutCooldown);
        }

        [Fact]
        public void A_turn_never_has_a_cut_touch_on_top_of_its_own()
        {
            SimConfig on = Free(), off = Free(cutStrength: 0);
            foreach (double degrees in new[] { 135.0, 180.0 })
            foreach (double stick in new[] { 0.5, 1.0 })
            for (int phase = 0; phase < on.TouchIntervalTicks; phase += 2)
            {
                Run cut = Cut(on, Bench, degrees, stick, phase, 2), plain = Cut(off, Bench, degrees, stick, phase, 2);
                string at = $"{degrees}°, stick {stick}, phase {phase}";
                Assert.Empty(cut.Cuts());
                for (int t = 0; t < cut.Events.Count; t++)
                {
                    SimState a = cut.After(t), b = plain.After(t);
                    Assert.True(a.Ball.Position.Equals(b.Ball.Position) && a.Ball.Velocity.Equals(b.Ball.Velocity) && a.Players[0].Position.Equals(b.Players[0].Position),
                        $"{at}, step {t}: the turn differs with the cut on");
                }

                // Its touch out stands for a cut's touch as the player comes round.
                int[] outs = Enumerable.Range(0, cut.Events.Count).Where(t => cut.Events[t].Any(e => e.Kind == SimEventKind.Turned && e.Other == 2)).ToArray();
                if (outs.Length > 0) Assert.Equal(on.TouchIntervalTicks, cut.After(outs[0]).Players[0].CutCooldown);
            }
        }

        [Fact]
        public void Good_and_bench_control_keep_a_sprint_cut_from_every_touch_phase_and_poor_control_from_fewer()
        {
            SimConfig config = Free();
            foreach (double degrees in new[] { 45.0, 90.0 })
            {
                int Kept(double control) => Enumerable.Range(0, config.TouchIntervalTicks).Count(phase => Cut(config, control, degrees, phase: phase).Kept());

                int good = Kept(Good), bench = Kept(Bench), poor = Kept(Poor);
                Assert.True(good == config.TouchIntervalTicks, $"{degrees}°: control {Good} kept {good} of {config.TouchIntervalTicks}");
                Assert.True(bench == config.TouchIntervalTicks, $"{degrees}°: control {Bench} kept {bench} of {config.TouchIntervalTicks}");
                Assert.True(poor <= config.TouchIntervalTicks / 2, $"{degrees}°: control {Poor} kept {poor} of {config.TouchIntervalTicks}");
            }
        }

        [Fact]
        public void With_no_execution_error_a_cut_is_kept_from_every_touch_phase_snapped_or_swept()
        {
            // What is left of failure is the footballer's error, not the touch's hidden phase.
            SimConfig config = Free(touchError: 0);
            foreach (double control in new[] { Good, Bench, Poor })
            foreach (double degrees in new[] { 45.0, 90.0 })
            foreach (int sweep in new[] { 0, 6 })
            for (int phase = 0; phase < config.TouchIntervalTicks; phase++)
            {
                Assert.True(Cut(config, control, degrees, 1, phase, 2, sweep).Kept(), $"control {control}, {degrees}° at a sprint, sweep {sweep} steps, phase {phase}: lost");
            }
        }

        [Fact]
        public void Each_cut_touch_is_bounded_and_the_ball_moves_only_by_its_velocity()
        {
            // No execution error, so the bound is the cut's strength for the redirect, with the
            // receive strength for any cushioning, as on any touch.
            SimConfig config = Free(touchError: 0);
            double rolling = config.RollingFriction * config.Gravity * config.TickSeconds;
            int cuts = 0;
            foreach (double control in new[] { Good, Bench, Poor })
            foreach (double degrees in new[] { 45.0, 90.0 })
            foreach (int phase in new[] { 0, 5, 10 })
            {
                Run run = Cut(config, control, degrees, phase: phase, seconds: 1.5, sweep: phase == 5 ? 6 : 0);
                double scale = 0.5 + 0.5 * control;
                double cap = (config.CutStrength + config.ReceiveStrength) * scale + rolling + 1e-9;
                for (int t = 0; t < run.Events.Count; t++)
                {
                    SimState before = run.Before[t], after = run.After(t);
                    Vec3 moved = after.Ball.Position - before.Ball.Position;
                    double stepped = Math.Sqrt(after.Ball.Velocity.LengthSquared) * config.TickSeconds;
                    Assert.True(Math.Sqrt(moved.LengthSquared) <= stepped + 1e-9, $"step {t}: the ball moved {Math.Sqrt(moved.LengthSquared):F4} m on a {stepped:F4} m step");

                    if (!run.Events[t].Any(e => e.Kind == SimEventKind.Cut)) continue;
                    cuts++;
                    double change = Math.Sqrt((after.Ball.Velocity.Horizontal - before.Ball.Velocity.Horizontal).LengthSquared);
                    Assert.True(change <= cap, $"control {control}, {degrees}°, phase {phase}, step {t}: a cut touch changed the ball's velocity by {change:F3} m/s, more than {cap:F3}");

                    // The ball runs ahead of the player and away from them, so nothing is cushioned:
                    // the cut's own strength is the bound.
                    double redirect = config.CutStrength * scale + rolling + 1e-9;
                    Assert.True(change <= redirect, $"control {control}, {degrees}°, phase {phase}, step {t}: a cut touch changed the ball's velocity by {change:F3} m/s, more than {redirect:F3}");
                }
            }

            Assert.True(cuts > 0, "no cut touch to check");
        }

        [Fact]
        public void A_change_of_direction_short_of_the_cut_angle_is_ordinary()
        {
            SimConfig config = Free();
            foreach (int phase in new[] { 0, 7 })
            {
                Run on = Cut(config, Good, 10, phase: phase), off = Cut(Free(cutStrength: 0), Good, 10, phase: phase);
                Assert.Empty(on.Cuts());
                Assert.Equal(off.Hashes, on.Hashes);
            }
        }

        [Fact]
        public void Cut_strength_zero_makes_no_cut_and_the_other_cut_knobs_then_change_nothing()
        {
            SimConfig off = Free(cutStrength: 0);
            Run run = Cut(off, Good, 90, phase: 4);
            Assert.Empty(run.Cuts());
            Assert.All(run.Before, s => Assert.Equal(0, s.Players[0].CutCooldown));

            Run other = Cut(off.With("cutAngle", 0.5).With("cutInterval", 0.2), Good, 90, phase: 4);
            Assert.Equal(run.Hashes, other.Hashes);
        }

        [Fact]
        public void Attached_never_cuts()
        {
            SimConfig on = new SimConfig(controlModel: ControlModel.Attached), off = on.With("cutStrength", 0);
            foreach (double degrees in new[] { 45.0, 90.0, 135.0 })
            {
                Run cut = Cut(on, Bench, degrees, phase: 3), plain = Cut(off, Bench, degrees, phase: 3);
                Assert.Empty(cut.Cuts());
                Assert.Equal(plain.Hashes, cut.Hashes);
            }
        }

        [Fact]
        public void No_cut_touch_through_a_kick()
        {
            SimConfig config = Free();
            KickIntent flick = KickIntent.AlongHeading(0.6, 0);

            // The same cut touches the ball at once without a kick.
            Run plain = Cut(config, Good, 90, phase: 2, seconds: 1);
            Assert.Equal(plain.TurnAt, plain.Cuts().FirstOrDefault(-1));

            // A kick asked just before the stick turns: no cut touch through its wind-up and cooldown.
            int asked = plain.TurnAt - 1;
            Run kicked = Cut(config, Good, 90, phase: 2, seconds: 1, kick: t => t == asked ? flick : (KickIntent?)null);
            int resolved = Enumerable.Range(0, kicked.Events.Count).First(t => kicked.Events[t].Any(e => e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KickMissed));
            Assert.DoesNotContain(kicked.Cuts(), t => t <= resolved + config.KickCooldownTicks);
        }

        [Fact]
        public void A_cut_is_deterministic_and_survives_snapshot_and_restore()
        {
            SimConfig config = Free();
            Run first = Cut(config, Bench, 90, phase: 6), second = Cut(config, Bench, 90, phase: 6);
            Assert.Equal(first.Hashes, second.Hashes);

            // Between the cut touch and its follow-up, inside the stride's cut cooldown.
            int at = first.TurnAt + 2;
            Assert.True(first.Before[at].Players[0].CutCooldown > 0, "no cut cooldown at the snapshot");
            var sim = new Simulator(config, AtFeet(config, Bench));
            // The same stick as the run's: cos 90° isn't exactly 0.
            var r = new Vec2(Math.Cos(Math.PI / 2), Math.Sin(Math.PI / 2));
            for (int t = 0; t < at; t++) sim.Step(new[] { new Command(A, t, t < first.TurnAt ? new Vec2(1, 0) : r) }, new List<SimEvent>());
            Assert.Equal(first.Hashes[at - 1], sim.StateHash());
            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), config);
            for (int t = at; t < first.Events.Count; t++)
            {
                restored.Step(new[] { new Command(A, t, r) }, new List<SimEvent>());
                Assert.Equal(first.Hashes[t], restored.StateHash());
            }
        }

        [Fact]
        public void The_cut_cooldown_round_trips_through_the_encoding_and_a_bad_one_is_refused()
        {
            SimConfig config = Free();
            SimState state = AtFeet(config, Good);
            state.Players[0].CutCooldown = 7;
            Simulator restored = Snapshot.Restore(Snapshot.Capture(new Simulator(config, state)), config);
            Assert.Equal(7, restored.PeekState().Players[0].CutCooldown);

            state.Players[0].CutCooldown = config.TouchIntervalTicks + 1;
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(config, state));
            state.Players[0].CutCooldown = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(config, state));
        }
    }
}
