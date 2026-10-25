using System.IO;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Writes a run's per-tick hash log: a self-identifying header, one line per tick, then the
    /// sequence digest. Text with LF line endings, so logs from any runtime diff directly.
    ///
    /// <code>
    /// # eleven-hashlog v1
    /// # scenario: spike-1/v1
    /// # build: …
    /// # state-schema: 1
    /// # tick-convention: …
    /// # config-hash: 0123456789abcdef
    /// # maths: Portable
    /// # seed: 0000000e1e7e2026
    /// 0 0123456789abcdef
    /// 1 …
    /// # digest: 0123456789abcdef
    /// </code>
    /// </summary>
    public static class HashLog
    {
        public static void Write(TextWriter writer, ScenarioRun run)
        {
            writer.NewLine = "\n";
            writer.WriteLine("# eleven-hashlog v1");
            writer.WriteLine($"# scenario: {SpikeScenario.Name}");
            writer.WriteLine($"# build: {SimulationInfo.BuildId}");
            writer.WriteLine($"# state-schema: {SimulationInfo.StateSchemaVersion}");
            writer.WriteLine($"# tick-convention: {SimulationInfo.TickConvention}");
            writer.WriteLine($"# config-hash: {run.Config.Hash:x16}");
            writer.WriteLine($"# maths: {run.Config.MathBackend}");
            writer.WriteLine($"# seed: {run.FinalState.Seed:x16}");

            for (int i = 0; i < run.TickHashes.Count; i++)
            {
                writer.WriteLine($"{run.FirstTick + i} {run.TickHashes[i]:x16}");
            }

            writer.WriteLine($"# digest: {run.SequenceDigest():x16}");
        }
    }
}
