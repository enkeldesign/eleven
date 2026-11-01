using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #44: the "this felt wrong" capture. The recorder keeps the last ten seconds of a run without
    /// touching it; a capture carries everything needed to replay that window, and replays it
    /// exactly, or says clearly why it can't.
    /// </summary>
    public class FeelCaptureTests
    {
        private const string HostBuild = "test-build+0000000";

        private static string FixturePath =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "tests", "Eleven.Simulation.Tests", "Fixtures", "spike-felt-wrong.elfc");

        // Plays the spike scenario for `seconds`, feeding the recorder exactly as the host does.
        private static (Simulator Sim, FeelRecorder Recorder, List<ulong> Hashes) Play(double seconds, SimConfig? config = null)
        {
            config ??= SpikeScenario.Config();
            var sim = new Simulator(config, SpikeScenario.InitialState(config));
            var recorder = new FeelRecorder();
            recorder.Start(sim);
            var hashes = new List<ulong> { sim.StateHash() };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            long end = (long)(seconds * config.TickRate);
            while (sim.Tick < end)
            {
                commands.Clear();
                SpikeScenario.CommandsFor(sim.Tick, commands);
                int first = events.Count;
                sim.Step(commands, events);
                recorder.Record(sim, commands, events, first);
                hashes.Add(sim.StateHash());
            }

            return (sim, recorder, hashes);
        }

        [Fact]
        public void A_capture_holds_the_last_ten_seconds_and_the_snapshot_that_starts_them()
        {
            (Simulator sim, FeelRecorder recorder, _) = Play(30);
            FeelCapture capture = recorder.Capture(sim, HostBuild, "technical-reference", "none", "");

            int rate = sim.Config.TickRate;
            Assert.InRange(sim.Tick - capture.StartTick, 10 * rate, 11 * rate);
            Assert.Equal(sim.Tick - capture.StartTick, capture.StepCount);
            Assert.Equal(capture.StepCount + 1, capture.TickHashes.Count);
            Assert.Equal(Snapshot.Read(CommandRecording.Decode(capture.RecordingPayload).StartSnapshot).Tick, capture.StartTick);
        }

        [Fact]
        public void A_run_younger_than_the_window_is_captured_whole()
        {
            (Simulator sim, FeelRecorder recorder, _) = Play(4);
            FeelCapture capture = recorder.Capture(sim, HostBuild, "technical-reference", "none", "");

            Assert.Equal(0, capture.StartTick);
            Assert.Equal(sim.Tick, capture.StepCount);
        }

        [Fact]
        public void A_capture_replays_its_per_tick_hashes_and_events_exactly()
        {
            // At 20 s the window holds the second kick and its bounces.
            (Simulator sim, FeelRecorder recorder, List<ulong> live) = Play(20);
            FeelCapture capture = FeelCapture.Decode(recorder.Capture(sim, HostBuild, "technical-reference", "none", "felt slow").Encode());

            FeelReplay replay = capture.Replay();
            Assert.True(replay.Matches, replay.Problem);
            Assert.Equal(capture.StepCount + 1, replay.TicksCompared);
            Assert.Equal(live.Skip((int)capture.StartTick), capture.TickHashes);
            Assert.Contains(capture.Events, e => e.Kind == SimEventKind.Kicked);
            Assert.Contains(capture.Events, e => e.Kind == SimEventKind.Bounced);
        }

        [Fact]
        public void Capturing_does_not_alter_the_simulation()
        {
            SimConfig config = SpikeScenario.Config();
            (_, _, List<ulong> plain) = Play(20);

            var sim = new Simulator(config, SpikeScenario.InitialState(config));
            var recorder = new FeelRecorder();
            recorder.Start(sim);
            var hashes = new List<ulong> { sim.StateHash() };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            while (sim.Tick < 20 * config.TickRate)
            {
                commands.Clear();
                SpikeScenario.CommandsFor(sim.Tick, commands);
                int first = events.Count;
                sim.Step(commands, events);
                recorder.Record(sim, commands, events, first);
                if (sim.Tick % 180 == 0) recorder.Capture(sim, HostBuild, "technical-reference", "none", "");
                hashes.Add(sim.StateHash());
            }

            Assert.Equal(plain, hashes);
        }

        [Fact]
        public void A_capture_records_its_build_versions_config_seed_and_context()
        {
            (Simulator sim, FeelRecorder recorder, _) = Play(12);
            FeelCapture capture = FeelCapture.Decode(recorder.Capture(sim, HostBuild, "technical-reference", "none", "kick felt late").Encode());

            Assert.Equal(HostBuild, capture.HostBuild);
            Assert.Equal(SimulationInfo.BuildId, capture.SimulationBuild);
            Assert.Equal(SimulationInfo.TickConvention, capture.TickConvention);
            Assert.Equal(SimulationInfo.StateSchemaVersion, capture.StateSchemaVersion);
            Assert.Equal(SimConfig.FormatVersion, capture.ConfigFormatVersion);
            Assert.Equal(CommandRecording.FormatVersion, capture.RecordingFormatVersion);
            Assert.Equal(sim.Config.Hash, capture.ConfigHash);
            Assert.Equal(sim.Config.Hash, SimConfig.Decode(capture.ConfigPayload).Hash);
            Assert.Equal(SpikeScenario.Seed, capture.Seed);
            Assert.Equal("technical-reference", capture.ActivePreset);
            Assert.Equal("none", capture.ControlCandidate);
            Assert.Equal("kick felt late", capture.Note);
        }

        // A capture made under a tuned candidate carries that candidate and replays under it.
        [Fact]
        public void A_capture_under_a_tuned_config_replays_under_that_config()
        {
            SimConfig tuned = SpikeScenario.Config().With("rollingFriction", 0.22).With("aftertouchSeconds", 0.27);
            (Simulator sim, FeelRecorder recorder, _) = Play(15, tuned);
            FeelCapture capture = FeelCapture.Decode(recorder.Capture(sim, HostBuild, "custom", "none", "").Encode());

            Assert.Equal(tuned.Hash, capture.ConfigHash);
            Assert.NotEqual(new SimConfig().Hash, capture.ConfigHash);
            FeelReplay replay = capture.Replay();
            Assert.True(replay.Matches, replay.Problem);
        }

        [Theory]
        [InlineData("schema")]
        [InlineData("tick")]
        [InlineData("config")]
        [InlineData("recording")]
        public void A_version_mismatch_is_reported_by_name_and_never_replayed(string field)
        {
            (Simulator sim, FeelRecorder recorder, _) = Play(5);
            FeelCapture capture = recorder.Capture(sim, HostBuild, "technical-reference", "none", "");
            string expected;
            switch (field)
            {
                case "schema": capture.StateSchemaVersion = 99; expected = "state schema 99"; break;
                case "tick": capture.TickConvention = "tick-v0: something else"; expected = "tick convention"; break;
                case "config": capture.ConfigFormatVersion = 99; expected = "config format 99"; break;
                default: capture.RecordingFormatVersion = 99; expected = "recording format 99"; break;
            }

            FeelReplay replay = FeelCapture.Decode(capture.Encode()).Replay();
            Assert.False(replay.Matches);
            Assert.Contains(expected, replay.Problem);
            Assert.Equal(0, replay.TicksCompared);
        }

        [Fact]
        public void A_divergence_names_the_first_tick_that_differs()
        {
            (Simulator sim, FeelRecorder recorder, _) = Play(12);
            FeelCapture capture = recorder.Capture(sim, HostBuild, "technical-reference", "none", "");
            Assert.True(capture.TickHashes.Count > 100, $"{capture.TickHashes.Count} tick hashes");
            capture.TickHashes[100] ^= 1;

            FeelReplay replay = capture.Replay();
            Assert.False(replay.Matches);
            Assert.Equal(capture.StartTick + 100, replay.FirstDivergentTick);
        }

        [Fact]
        public void Not_a_capture_or_another_capture_format_fails_clearly()
        {
            Assert.Contains("Not an ELEVEN feel capture", Assert.Throws<FormatException>(() => FeelCapture.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })).Message);

            (Simulator sim, FeelRecorder recorder, _) = Play(3);
            byte[] bytes = recorder.Capture(sim, HostBuild, "technical-reference", "none", "").Encode();
            bytes[4] = 99; // the capture format, little-endian int32 after the magic
            Assert.Contains("capture format 99", Assert.Throws<FormatException>(() => FeelCapture.Decode(bytes)).Message);
        }

        // The committed fixture: a capture made by `eleven-sim capture`, replayed headlessly in CI.
        [Fact]
        public void The_committed_fixture_replays_exactly()
        {
            Assert.True(File.Exists(FixturePath), $"no fixture at {FixturePath}");
            FeelCapture capture = FeelCapture.Decode(File.ReadAllBytes(FixturePath));
            FeelReplay replay = capture.Replay();
            Assert.True(replay.Matches, replay.Problem);
            Assert.InRange(capture.StepCount, 10 * 60, 11 * 60);
        }

        [Fact]
        public void A_config_decodes_from_its_encoding()
        {
            SimConfig tuned = new SimConfig().With("kickSpeed", 24.5).With("passBackWeight", 3.25);
            SimConfig decoded = SimConfig.Decode(tuned.Encode());
            Assert.Equal(tuned.Hash, decoded.Hash);
            Assert.Equal(tuned.Encode(), decoded.Encode());

            byte[] other = tuned.Encode();
            other[0] = 99;
            Assert.Contains("config format 99", Assert.Throws<FormatException>(() => SimConfig.Decode(other)).Message);
            Assert.Throws<FormatException>(() => SimConfig.Decode(tuned.Encode().Concat(new byte[] { 0 }).ToArray()));
            Assert.Throws<FormatException>(() => SimConfig.Decode(tuned.Encode().Take(20).ToArray()));
        }
    }
}
