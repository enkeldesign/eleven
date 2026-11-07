using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Eleven.Simulation;
using Eleven.Simulation.Harness;

/// <summary>
/// eleven-sim hashlog &lt;dir&gt;  write the scenario's per-tick hash log
/// eleven-sim bench [runs]      measure mean simulation-step cost per tick
/// eleven-sim trace             print the ball and player every 10 ticks (for scripting the scenario)
/// eleven-sim curves &lt;dir&gt; [name=value ...]
///                              characterise the ball under the default config with any tunables
///                              overridden (#24), e.g. curves out rollingFriction=0.15 kickSpeed=28
/// eleven-sim movement &lt;dir&gt; [name=value ...]
///                              characterise player movement for a capability profile (the reference
///                              by default) and the starting-range grid (#36), e.g. movement out maxSpeed=9
/// eleven-sim kicks &lt;dir&gt; [name=value ...]
///                              characterise the manual kick and aftertouch (#37) under the default config
///                              and the reference profile, either overridden by name, e.g. kicks out kickPower=0.8 aftertouchBend=0
/// eleven-sim replay &lt;capture.elfc&gt; replay a "this felt wrong" capture (#44) and compare every tick; exit 1 on any mismatch
/// eleven-sim capture &lt;file&gt; [seconds]
///                              capture the last 10 s of the scripted spike session at `seconds` (default 20),
///                              through the host's FeelRecorder: the committed fixture
/// eleven-sim candidate [name=value ...]
///                              a feel test bench candidate (#45): the reference values with any knobs changed,
///                              its config hash and practice start hash, e.g. candidate kickSpeed=28 maxSpeed=8.5
/// eleven-sim passes &lt;dir&gt; [name=value ...]
///                              measure the quick pass (#38) in the practice drills under the reference profile
///                              (or one with capabilities changed) and three contrasts, e.g. passes out passAnticipation=1
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
            case "curves" when args.Length >= 2:
                return Curves(args[1], args.Skip(2));
            case "movement" when args.Length >= 2:
                return Movement(args[1], args.Skip(2));
            case "kicks" when args.Length >= 2:
                return Kicks(args[1], args.Skip(2));
            case "dribble" when args.Length >= 2:
                return Dribble(args[1], args.Skip(2));
            case "passes" when args.Length >= 2:
                return Passes(args[1], args.Skip(2));
            case "replay" when args.Length == 2:
                return Replay(args[1]);
            case "candidate":
                return Candidate(args.Skip(1));
            case "capture" when args.Length >= 2:
                return Capture(args[1], args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 20);
            default:
                Console.Error.WriteLine("usage: eleven-sim hashlog <dir> | bench [runs] | trace | curves <dir> [name=value ...] | movement <dir> [name=value ...] | kicks <dir> [name=value ...] | passes <dir> [name=value ...] | dribble <dir> [name=value ...] | replay <capture> | capture <file> [seconds] | candidate [name=value ...]");
                return 2;
        }
    }

    /// <summary>
    /// The candidate the test bench would apply for these changes, and the hashes that identify its
    /// run: the same values give the same hashes in every runtime.
    /// </summary>
    private static int Candidate(IEnumerable<string> assignments)
    {
        var changes = new List<KeyValuePair<string, double>>();
        foreach (string a in assignments)
        {
            int equals = a.IndexOf('=');
            if (equals <= 0 || !double.TryParse(a.Substring(equals + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                Console.Error.WriteLine($"not name=value: {a}");
                return 2;
            }

            changes.Add(new KeyValuePair<string, double>(a.Substring(0, equals), value));
        }

        if (!BenchCandidate.Reference.TryWith(changes, out BenchCandidate? candidate, out string problem))
        {
            Console.Error.WriteLine(problem);
            return 1;
        }

        Console.WriteLine($"config {candidate!.Config.Hash:x16}  practice start {candidate.PracticeStartHash():x16}  preset {candidate.PresetName}  {candidate.ChangesFrom(BenchCandidate.Reference)} change(s) from the technical reference");
        string group = "";
        foreach (BenchKnob k in BenchCandidate.Knobs)
        {
            if (k.Group != group) Console.WriteLine($"{(group = k.Group)}:");
            TunableParameter p = k.Parameter;
            string start = p.StartMin.HasValue ? $"research start {p.StartMin:G6}–{p.StartMax:G6}" : "technical reference";
            string changed = candidate.Get(k.Name).Equals(BenchCandidate.Reference.Get(k.Name)) ? "" : $"  (was {BenchCandidate.Reference.Get(k.Name):G6})";
            Console.WriteLine($"  {k.Label,-28} {candidate.Get(k.Name),10:G6} {p.Unit,-22} valid {p.Min:G6}–{p.Max:G6}, {start}, step {k.Step:G6}{changed}");
        }

        return 0;
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

    private static List<KeyValuePair<string, double>>? ParseSettings(IEnumerable<string> settings)
    {
        var overrides = new List<KeyValuePair<string, double>>();
        foreach (string setting in settings)
        {
            int eq = setting.IndexOf('=');
            if (eq <= 0 || !double.TryParse(setting.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                Console.Error.WriteLine($"not a name=value setting: {setting}");
                return null;
            }

            overrides.Add(new KeyValuePair<string, double>(setting.Substring(0, eq), value));
        }

        return overrides;
    }

    private static int Curves(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        SimConfig config = new SimConfig().With(overrides);
        List<BallCurve> curves = BallCurves.Characterise(config);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "ball-curves.csv")))
        {
            BallCurves.WriteCsv(csv, config, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "ball-curves.txt")))
        {
            BallCurves.WriteSummary(summary, config, overrides.Select(o => o.Key).ToList(), curves);
        }

        Console.WriteLine($"config {config.Hash:x16}: {BallCurves.KeyNumbers(config, curves)} -> {directory}");
        return 0;
    }

    private static int Dribble(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        SimConfig config = SpikeScenario.Config().With(overrides);
        List<DribbleCurve> curves = DribbleCurves.Characterise(config);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "dribble-curves.csv")))
        {
            DribbleCurves.WriteCsv(csv, config, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {DribbleCurves.KeyNumbers(curves)} -> {directory}");
        return 0;
    }

    private static int Movement(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        PlayerCapabilities profile = PlayerCapabilities.Reference;
        foreach (KeyValuePair<string, double> o in overrides) profile = profile.With(o.Key, o.Value);

        var config = new SimConfig();
        List<MovementCurve> curves = MovementCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "movement-curves.csv")))
        {
            MovementCurves.WriteCsv(csv, config, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "movement-curves.txt")))
        {
            MovementCurves.WriteSummary(summary, config, profile, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {MovementCurves.KeyNumbers(curves)} -> {directory}");
        return 0;
    }

    // A name is a player capability or a config tunable; each fails on what it doesn't know.
    private static bool SplitSettings(IEnumerable<string> settings, out SimConfig config, out PlayerCapabilities profile)
    {
        config = new SimConfig();
        profile = PlayerCapabilities.Reference;
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return false;

        var configOverrides = new List<KeyValuePair<string, double>>();
        foreach (KeyValuePair<string, double> o in overrides)
        {
            if (PlayerCapabilities.Parameters.Any(p => p.Name == o.Key)) profile = profile.With(o.Key, o.Value);
            else configOverrides.Add(o);
        }

        config = config.With(configOverrides);
        return true;
    }

    private static int Passes(string directory, IEnumerable<string> settings)
    {
        if (!SplitSettings(settings, out SimConfig config, out PlayerCapabilities profile)) return 2;

        List<PassCurve> curves = PassCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "pass-curves.csv")))
        {
            PassCurves.WriteCsv(csv, config, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "pass-curves.txt")))
        {
            PassCurves.WriteSummary(summary, config, profile, curves);
        }

        // The free pass with no receiver (#65), beside the drills.
        List<FreePassCurve> free = FreePassCurves.Characterise(config, profile);
        using (var csv = new StreamWriter(Path.Combine(directory, "free-pass-curves.csv")))
        {
            FreePassCurves.WriteCsv(csv, config, free);
        }

        Console.WriteLine($"config {config.Hash:x16}, {PassCurves.KeyNumbers(curves)}; free passes: {FreePassCurves.KeyNumbers(free)} -> {directory}");
        return 0;
    }

    private static int Kicks(string directory, IEnumerable<string> settings)
    {
        if (!SplitSettings(settings, out SimConfig config, out PlayerCapabilities profile)) return 2;
        List<KickCurve> curves = KickCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "kick-curves.csv")))
        {
            KickCurves.WriteCsv(csv, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "kick-curves.txt")))
        {
            KickCurves.WriteSummary(summary, config, profile, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {KickCurves.KeyNumbers(config, curves)} -> {directory}");
        return 0;
    }

    private static int Replay(string path)
    {
        FeelCapture capture;
        try
        {
            capture = FeelCapture.Decode(File.ReadAllBytes(path));
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine($"{path}: {e.Message}");
            return 1;
        }

        Console.WriteLine($"capture: host {capture.HostBuild}, simulation {capture.SimulationBuild}, preset {capture.ActivePreset}, control {capture.ControlCandidate}");
        Console.WriteLine($"window: ticks {capture.StartTick}..{capture.StartTick + capture.StepCount}, seed {capture.Seed:x16}, config {capture.ConfigHash:x16}");
        if (capture.Note.Length > 0) Console.WriteLine($"note: {capture.Note}");

        FeelReplay replay = capture.Replay();
        if (!replay.Matches)
        {
            Console.Error.WriteLine($"{path}: {replay.Problem}");
            return 1;
        }

        Console.WriteLine($"replayed exactly: {replay.TicksCompared} tick hashes and {capture.Events.Count} events match");
        return 0;
    }

    private static int Capture(string path, double seconds)
    {
        SimConfig config = SpikeScenario.Config();
        var sim = new Simulator(config, SpikeScenario.InitialState(config));
        var recorder = new FeelRecorder();
        recorder.Start(sim);
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
        }

        FeelCapture capture = recorder.Capture(sim, $"eleven-sim {SpikeScenario.Name}", "technical-reference", "none",
            $"scripted fixture: the last {FeelRecorder.WindowSeconds:0} s of {SpikeScenario.Name} at {seconds:0.##} s");
        File.WriteAllBytes(path, capture.Encode());
        Console.WriteLine($"ticks {capture.StartTick}..{capture.StartTick + capture.StepCount}, {capture.Events.Count} events -> {path}");
        return 0;
    }

    private static int Trace()
    {
        ScenarioRunner.Run(SpikeScenario.Config(), state =>
        {
            if (state.Tick % 10 != 0) return;
            BallState b = state.Ball;
            PlayerState d = state.Players[0];
            Console.WriteLine(
                $"{state.Tick,5} ball ({b.Position.X,7:F2} {b.Position.Y,6:F2} {b.Position.Z,5:F2}) " +
                $"v ({b.Velocity.X,6:F2} {b.Velocity.Y,6:F2} {b.Velocity.Z,6:F2}) spin {b.Spin.Z,5:F2} " +
                $"player ({d.Position.X,7:F2} {d.Position.Y,6:F2}) h {d.Heading,5:F2} cd {d.KickCooldown} wind-up {d.WindUpTicks} window {d.AftertouchTicks}");
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
