using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The scripted 60-second scenario: it exercises every claimed behaviour, repeats exactly, and
    /// continues identically from a snapshot.
    /// </summary>
    public class ScenarioTests
    {
        [Fact]
        public void The_scenario_exercises_every_claimed_behaviour()
        {
            SimConfig config = SpikeScenario.Config();
            var trace = new List<SimState>();
            ScenarioRun run = ScenarioRunner.Run(config, s => trace.Add(s.Clone()));
            Assert.Equal(SpikeScenario.DurationTicks(config) + 1, trace.Count);

            // Player movement: the player travels well away from its start and turns around.
            Assert.True(trace.Max(s => s.Players[0].Position.X) - trace[0].Players[0].Position.X > 50);
            Assert.Contains(trace, s => Math.Abs(s.Players[0].Heading) > 3);

            // Kicks, both of them, and wind-ups that ended out of reach.
            SimEvent[] kicks = run.Events.Where(e => e.Kind == SimEventKind.Kicked).ToArray();
            Assert.Equal(2, kicks.Length);
            Assert.Contains(run.Events, e => e.Kind == SimEventKind.KickMissed && e.Tick < kicks[0].Tick);
            Assert.True(trace.Count(s => s.Players[0].WindUpTicks > 0) > 2 * config.KickWindUpTicks);

            // Airborne motion and bounces.
            Assert.True(trace.Max(s => s.Ball.Position.Z) > 2.5);
            Assert.True(run.Events.Count(e => e.Kind == SimEventKind.Bounced) >= 4);

            // Aftertouch: sideways input bends the first kick left during its window and leaves it
            // spinning; after the window closes the spin keeps curving the ball while it flies.
            SimState launched = trace[(int)kicks[0].Tick + 1];
            SimState shaped = trace[(int)kicks[0].Tick + 1 + config.AftertouchTicks];
            SimState inFlight = trace[(int)kicks[0].Tick + 1 + config.AftertouchTicks + 40];
            Assert.Equal(0, launched.Ball.Spin.Z);
            Assert.Equal(0, shaped.Players[0].AftertouchTicks);
            Assert.True(Direction(shaped.Ball) - Direction(launched.Ball) > 0.05);
            Assert.True(shaped.Ball.Spin.Z > 0);
            Assert.True(inFlight.Ball.Position.Z > config.BallRadius);
            Assert.True(inFlight.Ball.Velocity.Y - shaped.Ball.Velocity.Y > 0.3);

            // Back aftertouch lifts the second kick above its launch.
            SimState returned = trace[(int)kicks[1].Tick + 1];
            SimState lifted = trace[(int)kicks[1].Tick + 1 + config.AftertouchTicks];
            double gravityOnly = returned.Ball.Velocity.Z - config.Gravity * config.TickSeconds * config.AftertouchTicks;
            Assert.True(lifted.Ball.Velocity.Z - gravityOnly > 0.9 * config.AftertouchLift);

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

            // Seeded randomness: the kicker's execution error moves the ball off the intended line,
            // within their capability, and never onto another line.
            double line = Math.Atan2(0.05, 1);
            Assert.Equal(line, launched.Players[0].AftertouchDirection, 15);
            Assert.NotEqual(line, Direction(launched.Ball));
            Assert.InRange(Direction(launched.Ball) - line, -launched.Players[0].Capabilities.KickError, launched.Players[0].Capabilities.KickError);

            Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.CommandRejected);
        }

        private static double Direction(BallState ball) => Math.Atan2(ball.Velocity.Y, ball.Velocity.X);

        [Fact]
        public void A_different_seed_changes_the_outcome()
        {
            SimConfig config = SpikeScenario.Config();
            SimState other = SpikeScenario.InitialState(config);
            other.Seed ^= 1;

            ScenarioRun scripted = ScenarioRunner.Run(config);
            ScenarioRun reseeded = ScenarioRunner.Continue(new Simulator(config, other));

            Assert.NotEqual(scripted.FinalState.Ball.Position.X, reseeded.FinalState.Ball.Position.X);
        }

        [Fact]
        public void Running_twice_gives_identical_hashes_every_tick()
        {
            ScenarioRun first = ScenarioRunner.Run(SpikeScenario.Config());
            ScenarioRun second = ScenarioRunner.Run(SpikeScenario.Config());

            Assert.Equal(first.TickHashes, second.TickHashes);
            Assert.Equal(first.Events, second.Events);
            Assert.Equal(first.SequenceDigest(), second.SequenceDigest());
        }

        [Fact]
        public void Restoring_a_snapshot_into_a_fresh_simulator_continues_identically()
        {
            SimConfig config = SpikeScenario.Config();
            ScenarioRun uninterrupted = ScenarioRunner.Run(config);

            // Mid-run, winding up to a miss, in the first kick's aftertouch window, ball in the air,
            // ball rolling beyond the line, winding up the return kick, in its window, ball at rest.
            foreach (long at in new long[] { 1, 80, 95, 150, 400, 885, 895, 2000 })
            {
                var original = new Simulator(config, SpikeScenario.InitialState(config));
                ScenarioRunner.Continue(original, stopAtTick: at);
                byte[] snapshot = Snapshot.Capture(original);
                PlayerState player = original.PeekState().Players[0];
                if (at == 80 || at == 885) Assert.True(player.WindUpTicks > 0);
                if (at == 95 || at == 895) Assert.True(player.AftertouchTicks > 0);

                ScenarioRun resumed = ScenarioRunner.Continue(Snapshot.Restore(snapshot, config));

                Assert.Equal(uninterrupted.TickHashes.Skip((int)at), resumed.TickHashes);
                Assert.Equal(uninterrupted.Events.Where(e => e.Tick >= at), resumed.Events);
            }
        }

        [Fact]
        public void The_hash_log_identifies_its_run_and_covers_every_tick()
        {
            SimConfig config = SpikeScenario.Config();
            ScenarioRun run = ScenarioRunner.Run(config);
            var text = new StringWriter();
            HashLog.Write(text, run);
            string[] lines = text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal("# eleven-hashlog v2", lines[0]);
            Assert.Contains($"# scenario: {SpikeScenario.Name}", lines);
            Assert.Contains($"# build: {SimulationInfo.BuildId}", lines);
            Assert.Contains($"# state-schema: {SimulationInfo.StateSchemaVersion}", lines);
            Assert.Contains($"# tick-convention: {SimulationInfo.TickConvention}", lines);
            Assert.Contains($"# config-hash: {config.Hash:x16}", lines);
            Assert.Contains($"# seed: {SpikeScenario.Seed:x16}", lines);
            Assert.Equal(SpikeScenario.DurationTicks(config) + 1, lines.Count(l => !l.StartsWith("#")));
            Assert.Equal($"{SpikeScenario.DurationTicks(config)} {run.TickHashes[run.TickHashes.Count - 1]:x16}", lines[lines.Length - 2]);
            Assert.Equal($"# digest: {run.SequenceDigest():x16}", lines[lines.Length - 1]);
        }
    }
}
