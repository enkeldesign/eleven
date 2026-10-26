using System.Collections.Generic;
using Eleven.Simulation.Harness;
using Eleven.Simulation.Maths;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>INV-10: commands recorded before assistance replay a session exactly.</summary>
    public class CommandRecordingTests
    {
        // Records the scripted scenario from tick `from` for `steps` steps, the way a host would,
        // adding an unowned and a stale command so rejections are part of the recording too.
        private static (Simulator live, CommandRecording recording, List<SimEvent> events) RecordScenario(MathBackend backend, long from, int steps)
        {
            SimConfig config = SpikeScenario.Config(backend);
            var live = new Simulator(config, SpikeScenario.InitialState(config));
            ScenarioRunner.Continue(live, stopAtTick: from);

            CommandRecording recording = CommandRecording.StartFrom(live);
            var events = new List<SimEvent>();
            var commands = new List<Command>();
            for (int i = 0; i < steps; i++)
            {
                commands.Clear();
                SpikeScenario.CommandsFor(live.Tick, commands);
                if (i % 50 == 0) commands.Add(new Command(99, live.Tick, new Vec2(1, 0), true));
                if (i % 70 == 0) commands.Add(new Command(SpikeScenario.ControllerId, live.Tick - 1, new Vec2(double.NaN, 0), false));
                recording.Record(commands);
                live.Step(commands, events);
            }

            return (live, recording, events);
        }

        [Theory]
        [InlineData(MathBackend.Portable, 0)]
        [InlineData(MathBackend.SystemMath, 500)]
        public void Replaying_a_recording_reproduces_the_session(MathBackend backend, long from)
        {
            var (live, recording, liveEvents) = RecordScenario(backend, from, 1200);

            var replayEvents = new List<SimEvent>();
            Simulator replay = recording.Replay(SpikeScenario.Config(backend), replayEvents);

            Assert.Equal(from + 1200, replay.Tick);
            Assert.Equal(live.StateHash(), replay.StateHash());
            Assert.Equal(liveEvents, replayEvents);
            Assert.Contains(liveEvents, e => e.Rejection == CommandRejection.Unowned);
            Assert.Contains(liveEvents, e => e.Rejection == CommandRejection.Stale);
        }

        [Fact]
        public void A_recording_survives_its_encoding_bit_for_bit()
        {
            var (live, recording, _) = RecordScenario(MathBackend.Portable, 100, 600);

            byte[] bytes = recording.Encode();
            CommandRecording decoded = CommandRecording.Decode(bytes);

            Assert.Equal(bytes, decoded.Encode());
            Assert.Equal(100, decoded.StartTick);
            Assert.Equal(600, decoded.StepCount);
            Assert.Equal(live.StateHash(), decoded.Replay(SpikeScenario.Config(MathBackend.Portable)).StateHash());
        }
    }
}
