using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Eleven.Simulation.Maths;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The scripted 60-second scenario: it exercises every claimed behaviour, repeats exactly, and
    /// continues identically from a snapshot. Determinism is compared within one maths backend;
    /// the two backends are not required to hash alike.
    /// </summary>
    public class ScenarioTests
    {
        public static IEnumerable<object[]> Backends() =>
            new[] { new object[] { MathBackend.SystemMath }, new object[] { MathBackend.Portable } };

        [Theory]
        [MemberData(nameof(Backends))]
        public void The_scenario_exercises_every_claimed_behaviour(MathBackend backend)
        {
            SimConfig config = SpikeScenario.Config(backend);
            var trace = new List<SimState>();
            ScenarioRun run = ScenarioRunner.Run(config, s => trace.Add(s.Clone()));
            Assert.Equal(SpikeScenario.DurationTicks(config) + 1, trace.Count);

            // Player movement: the dot travels well away from its start and turns around.
            Assert.True(trace.Max(s => s.Dots[0].Position.X) - trace[0].Dots[0].Position.X > 50);
            Assert.Contains(trace, s => Math.Abs(s.Dots[0].Heading) > 3);

            // Kicks, both of them.
            SimEvent[] kicks = run.Events.Where(e => e.Kind == SimEventKind.Kicked).ToArray();
            Assert.Equal(2, kicks.Length);

            // Airborne motion and bounces.
            Assert.True(trace.Max(s => s.Ball.Position.Z) > 2.5);
            Assert.True(run.Events.Count(e => e.Kind == SimEventKind.Bounced) >= 4);

            // Spin curve: after the first kick the ball's sideways velocity keeps growing in the
            // direction of spin × velocity while it flies.
            SimState launched = trace[(int)kicks[0].Tick + 1];
            SimState inFlight = trace[(int)kicks[0].Tick + 60];
            Assert.True(launched.Ball.Spin.Z > 0);
            Assert.True(inFlight.Ball.Velocity.Y - launched.Ball.Velocity.Y > 0.5);

            // Rolling friction: a stretch on the ground with speed falling, ending at rest.
            int rollingTicks = 0;
            for (int i = 1; i < trace.Count; i++)
            {
                BallState now = trace[i].Ball, before = trace[i - 1].Ball;
                bool grounded = now.Position.Z == config.BallRadius && now.Velocity.Z == 0 && before.Velocity.Z == 0;
                if (grounded && now.Velocity.Horizontal.LengthSquared > 0)
                {
                    Assert.True(now.Velocity.Horizontal.LengthSquared < before.Velocity.Horizontal.LengthSquared);
                    rollingTicks++;
                }
            }

            Assert.True(rollingTicks > 300);
            Assert.Equal(0, run.FinalState.Ball.Velocity.Horizontal.LengthSquared);

            // Goal line: crossed once going out; the return trip recrosses without an event.
            Assert.Single(run.Events, e => e.Kind == SimEventKind.GoalLineCrossed);
            Assert.True(run.FinalState.Ball.Position.X < config.GoalLineX);

            // Seeded randomness: the kick's direction error moves the ball off the dot's heading.
            double headingAtKick = trace[(int)kicks[0].Tick + 1].Dots[0].Heading;
            double launchDirection = Math.Atan2(launched.Ball.Velocity.Y, launched.Ball.Velocity.X);
            Assert.NotEqual(headingAtKick, launchDirection);

            Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.CommandRejected);
        }

        [Theory]
        [MemberData(nameof(Backends))]
        public void A_different_seed_changes_the_outcome(MathBackend backend)
        {
            SimConfig config = SpikeScenario.Config(backend);
            SimState other = SpikeScenario.InitialState(config);
            other.Seed ^= 1;

            ScenarioRun scripted = ScenarioRunner.Run(config);
            ScenarioRun reseeded = ScenarioRunner.Continue(new Simulator(config, other));

            Assert.NotEqual(scripted.FinalState.Ball.Position.X, reseeded.FinalState.Ball.Position.X);
        }

        [Theory]
        [MemberData(nameof(Backends))]
        public void Running_twice_gives_identical_hashes_every_tick(MathBackend backend)
        {
            ScenarioRun first = ScenarioRunner.Run(SpikeScenario.Config(backend));
            ScenarioRun second = ScenarioRunner.Run(SpikeScenario.Config(backend));

            Assert.Equal(first.TickHashes, second.TickHashes);
            Assert.Equal(first.Events, second.Events);
            Assert.Equal(first.SequenceDigest(), second.SequenceDigest());
        }

        [Theory]
        [MemberData(nameof(Backends))]
        public void Restoring_a_snapshot_into_a_fresh_simulator_continues_identically(MathBackend backend)
        {
            SimConfig config = SpikeScenario.Config(backend);
            ScenarioRun uninterrupted = ScenarioRunner.Run(config);

            // Mid-run, ball in the air, ball rolling beyond the line, dot kicking back, ball at rest.
            foreach (long at in new long[] { 1, 150, 400, 885, 2000 })
            {
                var original = new Simulator(config, SpikeScenario.InitialState(config));
                ScenarioRunner.Continue(original, stopAtTick: at);
                byte[] snapshot = Snapshot.Capture(original);

                ScenarioRun resumed = ScenarioRunner.Continue(Snapshot.Restore(snapshot, config));

                Assert.Equal(uninterrupted.TickHashes.Skip((int)at), resumed.TickHashes);
                Assert.Equal(uninterrupted.Events.Where(e => e.Tick >= at), resumed.Events);
            }
        }

        [Fact]
        public void Both_backends_agree_closely_although_their_hashes_may_not()
        {
            // A broken backend would drift far from System.Math; a correct one stays within
            // rounding. The hashes themselves are not compared.
            ScenarioRun system = ScenarioRunner.Run(SpikeScenario.Config(MathBackend.SystemMath));
            ScenarioRun portable = ScenarioRunner.Run(SpikeScenario.Config(MathBackend.Portable));

            Assert.Equal(system.Events, portable.Events);
            Assert.True(Distance(system.FinalState.Ball.Position, portable.FinalState.Ball.Position) < 1e-6);
            Assert.True(Distance(system.FinalState.Dots[0].Position, portable.FinalState.Dots[0].Position) < 1e-6);
        }

        [Fact]
        public void The_hash_log_identifies_its_run_and_covers_every_tick()
        {
            SimConfig config = SpikeScenario.Config(MathBackend.Portable);
            ScenarioRun run = ScenarioRunner.Run(config);
            var text = new StringWriter();
            HashLog.Write(text, run);
            string[] lines = text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal("# eleven-hashlog v1", lines[0]);
            Assert.Contains($"# scenario: {SpikeScenario.Name}", lines);
            Assert.Contains($"# build: {SimulationInfo.BuildId}", lines);
            Assert.Contains($"# state-schema: {SimulationInfo.StateSchemaVersion}", lines);
            Assert.Contains($"# tick-convention: {SimulationInfo.TickConvention}", lines);
            Assert.Contains($"# config-hash: {config.Hash:x16}", lines);
            Assert.Contains("# maths: Portable", lines);
            Assert.Contains($"# seed: {SpikeScenario.Seed:x16}", lines);
            Assert.Equal(SpikeScenario.DurationTicks(config) + 1, lines.Count(l => !l.StartsWith("#")));
            Assert.Equal($"{SpikeScenario.DurationTicks(config)} {run.TickHashes[run.TickHashes.Count - 1]:x16}", lines[lines.Length - 2]);
            Assert.Equal($"# digest: {run.SequenceDigest():x16}", lines[lines.Length - 1]);
        }

        private static double Distance(Vec3 a, Vec3 b) => Math.Sqrt((a - b).X * (a - b).X + (a - b).Y * (a - b).Y + (a - b).Z * (a - b).Z);

        private static double Distance(Vec2 a, Vec2 b) => Math.Sqrt((a - b).LengthSquared);
    }
}
