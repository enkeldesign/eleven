using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Eleven.Simulation;
using Eleven.Simulation.Harness;

/// <summary>
/// eleven-sim hashlog &lt;dir&gt;  write the scenario's per-tick hash log
/// eleven-sim bench [runs]      measure mean simulation-step cost per tick
/// eleven-sim trace             print the ball and dot every 10 ticks (for scripting the scenario)
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string command = args.Length > 0 ? args[0] : "";
        switch (command)
        {
            case "hashlog" when args.Length == 2:
                return WriteHashLogs(args[1]);
            case "bench":
                return Bench(args.Length > 1 ? int.Parse(args[1]) : 30);
            case "trace":
                return Trace();
            default:
                Console.Error.WriteLine("usage: eleven-sim hashlog <dir> | bench [runs] | trace");
                return 2;
        }
    }

    private static int WriteHashLogs(string directory)
    {
        Directory.CreateDirectory(directory);
        ScenarioRun run = ScenarioRunner.Run(SpikeScenario.Config());
        string path = Path.Combine(directory, $"hashlog-{SpikeScenario.Id}-v{SpikeScenario.Version}.txt");
        using (var writer = new StreamWriter(path))
        {
            HashLog.Write(writer, run);
        }

        Console.WriteLine($"{run.TickHashes.Count} tick hashes, digest {run.SequenceDigest():x16} -> {path}");
        return 0;
    }

    /// <summary>
    /// Times only <see cref="Simulator.Step"/>: commands are generated before the timed loop and
    /// state hashing is timed separately. Warm-up runs come first, so tiered JIT compilation has
    /// settled before anything is timed.
    /// </summary>
    private static int Bench(int runs)
    {
        const int warmUpRuns = 20;
        Console.WriteLine($"runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"cpu count: {Environment.ProcessorCount}");
#if DEBUG
        Console.WriteLine("WARNING: Debug build; measure in Release.");
#endif
        Console.WriteLine($"method: {SpikeScenario.Name} ({SpikeScenario.DurationSeconds} s), {warmUpRuns} warm-up runs, " +
            $"then {runs} timed runs; Step only, state hashing timed separately");

        var bench = new ScenarioBench();
        for (int i = 0; i < warmUpRuns; i++) bench.Run(record: false);
        for (int i = 0; i < runs; i++) bench.Run(record: true);

        Console.WriteLine(bench.Summary());
        return 0;
    }

    private sealed class ScenarioBench
    {
        private readonly SimConfig _config;
        private readonly List<Command>[] _commands;
        private readonly List<SimEvent> _events = new List<SimEvent>(64);
        private readonly List<double> _stepNs = new List<double>();
        private readonly List<double> _hashNs = new List<double>();

        public ScenarioBench()
        {
            _config = SpikeScenario.Config();
            _commands = new List<Command>[SpikeScenario.DurationTicks(_config)];
            for (int t = 0; t < _commands.Length; t++)
            {
                _commands[t] = new List<Command>();
                SpikeScenario.CommandsFor(t, _commands[t]);
            }
        }

        public void Run(bool record)
        {
            int ticks = _commands.Length;
            var simulator = new Simulator(_config, SpikeScenario.InitialState(_config));
            _events.Clear();

            long start = Stopwatch.GetTimestamp();
            for (int t = 0; t < ticks; t++)
            {
                simulator.Step(_commands[t], _events);
            }

            long stepped = Stopwatch.GetTimestamp();
            ulong sink = 0;
            for (int t = 0; t < ticks; t++)
            {
                sink ^= simulator.StateHash();
            }

            long hashed = Stopwatch.GetTimestamp();
            GC.KeepAlive(sink);

            if (!record) return;
            _stepNs.Add((stepped - start) * 1e9 / Stopwatch.Frequency / ticks);
            _hashNs.Add((hashed - stepped) * 1e9 / Stopwatch.Frequency / ticks);
        }

        public string Summary()
        {
            var sorted = new List<double>(_stepNs);
            sorted.Sort();
            return $"step mean {Mean(_stepNs):F0} ns/tick (min {sorted[0]:F0}, median {sorted[sorted.Count / 2]:F0}, " +
                $"max {sorted[sorted.Count - 1]:F0}); state hash mean {Mean(_hashNs):F0} ns/tick";
        }
    }

    private static int Trace()
    {
        ScenarioRunner.Run(SpikeScenario.Config(), state =>
        {
            if (state.Tick % 10 != 0) return;
            BallState b = state.Ball;
            DotState d = state.Dots[0];
            Console.WriteLine(
                $"{state.Tick,5} ball ({b.Position.X,7:F2} {b.Position.Y,6:F2} {b.Position.Z,5:F2}) " +
                $"v ({b.Velocity.X,6:F2} {b.Velocity.Y,6:F2} {b.Velocity.Z,6:F2}) spin {b.Spin.Z,5:F2} " +
                $"dot ({d.Position.X,7:F2} {d.Position.Y,6:F2}) h {d.Heading,5:F2} cd {d.KickCooldown}");
        }).Events.ForEach(e => Console.WriteLine($"event {e}"));
        return 0;
    }

    private static double Mean(List<double> values)
    {
        double sum = 0;
        foreach (double v in values) sum += v;
        return sum / values.Count;
    }

    private static void ForEach<T>(this IReadOnlyList<T> list, Action<T> action)
    {
        foreach (T item in list) action(item);
    }
}
