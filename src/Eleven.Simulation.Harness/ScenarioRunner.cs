// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;

namespace Eleven.Simulation.Harness
{
    /// <summary>The evidence from one scenario run.</summary>
    public sealed class ScenarioRun
    {
        internal ScenarioRun(SimConfig config, long firstTick, List<ulong> tickHashes, List<SimEvent> events, SimState finalState)
        {
            Config = config;
            FirstTick = firstTick;
            TickHashes = tickHashes;
            Events = events;
            FinalState = finalState;
        }

        public SimConfig Config { get; }

        /// <summary>The tick of <c>TickHashes[0]</c>.</summary>
        public long FirstTick { get; }

        /// <summary>Hash of state N for every N from <see cref="FirstTick"/> to the end, inclusive.</summary>
        public IReadOnlyList<ulong> TickHashes { get; }

        public IReadOnlyList<SimEvent> Events { get; }

        public SimState FinalState { get; }

        /// <summary>
        /// FNV-1a 64 over every tick hash in order (each as 8 little-endian bytes). Two runs agree on
        /// this only if they agree on every tick, so temporary divergence isn't hidden.
        /// </summary>
        public ulong SequenceDigest()
        {
            ulong h = Encoding.Fnv1a64.OffsetBasis;
            foreach (ulong hash in TickHashes) h = Encoding.Fnv1a64.Append(h, hash);
            return h;
        }
    }

    /// <summary>Runs the spike scenario, recording a hash per tick and every event.</summary>
    public static class ScenarioRunner
    {
        /// <summary>Runs the scenario from its initial state to the end.</summary>
        public static ScenarioRun Run(SimConfig config, Action<SimState>? observe = null) =>
            Continue(new Simulator(config, SpikeScenario.InitialState(config)), observe);

        /// <summary>
        /// Continues <paramref name="simulator"/> (for example one restored from a snapshot) to the
        /// end of the scenario with the scripted commands for the remaining ticks.
        /// </summary>
        public static ScenarioRun Continue(Simulator simulator, Action<SimState>? observe = null, long? stopAtTick = null)
        {
            long end = stopAtTick ?? SpikeScenario.DurationTicks(simulator.Config);
            long first = simulator.Tick;
            var hashes = new List<ulong> { simulator.StateHash() };
            var events = new List<SimEvent>();
            var commands = new List<Command>();
            observe?.Invoke(simulator.PeekState());

            while (simulator.Tick < end)
            {
                commands.Clear();
                SpikeScenario.CommandsFor(simulator.Tick, commands);
                simulator.Step(commands, events);
                hashes.Add(simulator.StateHash());
                observe?.Invoke(simulator.PeekState());
            }

            return new ScenarioRun(simulator.Config, first, hashes, events, simulator.CopyState());
        }
    }
}
