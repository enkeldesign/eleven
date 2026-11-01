// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Keeps the last seconds of a run so a "this felt wrong" capture can be made at any moment (#44).
    ///
    /// The host feeds it every step: the owned commands exactly as passed to the simulator (INV-10),
    /// then the hash and events after the step. It takes a snapshot every
    /// <see cref="SnapshotEverySeconds"/> and drops whatever the window no longer needs, so a capture
    /// covers between <see cref="WindowSeconds"/> and one snapshot interval more, or the whole run if
    /// the run is younger.
    ///
    /// It only reads the simulator (<see cref="Snapshot.Capture"/>, <see cref="Simulator.StateHash"/>):
    /// recording and capturing never step, pause or change it.
    /// </summary>
    public sealed class FeelRecorder
    {
        public const double WindowSeconds = 10;
        public const double SnapshotEverySeconds = 1;

        private sealed class Step
        {
            public Step(Command[] commands, ulong hash, SimEvent[] events)
            {
                Commands = commands;
                Hash = hash;
                Events = events;
            }

            public Command[] Commands { get; }
            public ulong Hash { get; }
            public SimEvent[] Events { get; }
        }

        private sealed class Mark
        {
            public Mark(long tick, byte[] snapshot, ulong hash, int stepIndex)
            {
                Tick = tick;
                Snapshot = snapshot;
                Hash = hash;
                StepIndex = stepIndex;
            }

            public long Tick { get; }
            public byte[] Snapshot { get; }
            public ulong Hash { get; }

            /// <summary>Index in the step list of the first step after this snapshot.</summary>
            public int StepIndex { get; set; }
        }

        private readonly List<Mark> _marks = new List<Mark>();
        private readonly List<Step> _steps = new List<Step>();
        private SimConfig? _config;

        /// <summary>Starts recording a run at the simulator's current state; forgets any earlier run.</summary>
        public void Start(Simulator simulator)
        {
            _config = simulator.Config;
            _marks.Clear();
            _steps.Clear();
            _marks.Add(new Mark(simulator.Tick, Snapshot.Capture(simulator), simulator.StateHash(), 0));
        }

        /// <summary>
        /// Records the step just taken: <paramref name="commands"/> as passed to it, and the events it
        /// appended to <paramref name="events"/> from index <paramref name="firstEvent"/> on.
        /// </summary>
        public void Record(Simulator simulator, IReadOnlyList<Command> commands, IReadOnlyList<SimEvent> events, int firstEvent = 0)
        {
            if (_config == null || simulator.Config != _config) throw new InvalidOperationException("Start the recorder on this run first.");

            var copy = new Command[commands.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = commands[i];
            var stepEvents = new SimEvent[Math.Max(0, events.Count - firstEvent)];
            for (int i = 0; i < stepEvents.Length; i++) stepEvents[i] = events[firstEvent + i];
            ulong hash = simulator.StateHash();
            _steps.Add(new Step(copy, hash, stepEvents));

            int rate = _config.TickRate;
            long every = Math.Max(1, (long)Math.Round(SnapshotEverySeconds * rate));
            if (simulator.Tick - _marks[_marks.Count - 1].Tick >= every)
            {
                _marks.Add(new Mark(simulator.Tick, Snapshot.Capture(simulator), hash, _steps.Count));
            }

            Trim(simulator.Tick, (long)Math.Round(WindowSeconds * rate));
        }

        // Keeps the latest snapshot at least a window old, and everything after it.
        private void Trim(long now, long window)
        {
            int keep = 0;
            for (int i = 0; i < _marks.Count; i++)
            {
                if (now - _marks[i].Tick >= window) keep = i;
            }

            if (keep == 0) return;
            int dropSteps = _marks[keep].StepIndex;
            _marks.RemoveRange(0, keep);
            _steps.RemoveRange(0, dropSteps);
            foreach (Mark m in _marks) m.StepIndex -= dropSteps;
        }

        /// <summary>
        /// The capture of the window ending now. <paramref name="hostBuild"/> identifies the build that
        /// made it; the preset, control candidate and note describe what it was felt in.
        /// </summary>
        public FeelCapture Capture(Simulator simulator, string hostBuild, string activePreset, string controlCandidate, string note)
        {
            if (_config == null || simulator.Config != _config) throw new InvalidOperationException("Start the recorder on this run first.");

            Mark start = _marks[0];
            var recording = new CommandRecording(start.Snapshot);
            var capture = new FeelCapture
            {
                HostBuild = hostBuild ?? "",
                SimulationBuild = SimulationInfo.BuildId,
                TickConvention = SimulationInfo.TickConvention,
                StateSchemaVersion = SimulationInfo.StateSchemaVersion,
                ConfigFormatVersion = SimConfig.FormatVersion,
                RecordingFormatVersion = CommandRecording.FormatVersion,
                ConfigPayload = _config.Encode(),
                ConfigHash = _config.Hash,
                Seed = simulator.PeekState().Seed,
                StartTick = start.Tick,
                ActivePreset = activePreset ?? "",
                ControlCandidate = controlCandidate ?? "",
                Note = note ?? "",
            };

            capture.TickHashes.Add(start.Hash);
            for (int i = start.StepIndex; i < _steps.Count; i++)
            {
                Step step = _steps[i];
                recording.Record(step.Commands);
                capture.TickHashes.Add(step.Hash);
                capture.Events.AddRange(step.Events);
            }

            capture.StepCount = recording.StepCount;
            capture.RecordingPayload = recording.Encode();
            return capture;
        }
    }
}
