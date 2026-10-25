using System;
using Eleven.Simulation.Harness;
using Eleven.Simulation.Maths;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>INV-3, INV-5, INV-7: snapshots identify what produced them and refuse the wrong config.</summary>
    public class SnapshotTests
    {
        private static Simulator ScenarioAt(MathBackend backend, long tick)
        {
            SimConfig config = SpikeScenario.Config(backend);
            var sim = new Simulator(config, SpikeScenario.InitialState(config));
            ScenarioRunner.Continue(sim, stopAtTick: tick);
            return sim;
        }

        [Fact]
        public void A_snapshot_identifies_its_schema_build_config_backend_seed_and_tick_convention()
        {
            Simulator sim = ScenarioAt(MathBackend.SystemMath, 120);
            Snapshot snapshot = Snapshot.Read(Snapshot.Capture(sim));

            Assert.Equal(SimulationInfo.StateSchemaVersion, snapshot.SchemaVersion);
            Assert.Equal(SimulationInfo.BuildId, snapshot.BuildId);
            Assert.NotEqual("unknown", snapshot.BuildId);
            Assert.Equal(SimulationInfo.TickConvention, snapshot.TickConvention);
            Assert.Equal(sim.Config.Hash, snapshot.ConfigHash);
            Assert.Equal(MathBackend.SystemMath, snapshot.MathBackend);
            Assert.Equal(SpikeScenario.Seed, snapshot.Seed);
            Assert.Equal(120, snapshot.Tick);
        }

        [Fact]
        public void Restoring_under_a_different_config_is_refused()
        {
            byte[] data = Snapshot.Capture(ScenarioAt(MathBackend.Portable, 60));

            Assert.Throws<InvalidOperationException>(() => Snapshot.Restore(data, SpikeScenario.Config(MathBackend.SystemMath)));
            Assert.Throws<InvalidOperationException>(() => Snapshot.Restore(data, new SimConfig(tickRate: 50)));
        }

        [Fact]
        public void Corrupt_snapshots_are_refused()
        {
            byte[] data = Snapshot.Capture(ScenarioAt(MathBackend.Portable, 60));

            byte[] truncated = new byte[data.Length - 1];
            Array.Copy(data, truncated, truncated.Length);
            Assert.Throws<FormatException>(() => Snapshot.Read(truncated));

            byte[] wrongMagic = (byte[])data.Clone();
            wrongMagic[0] = (byte)'X';
            Assert.Throws<FormatException>(() => Snapshot.Read(wrongMagic));
        }
    }
}
