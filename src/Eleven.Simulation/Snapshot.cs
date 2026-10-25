using System;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation
{
    /// <summary>
    /// A self-identifying snapshot (INV-3, INV-5, INV-7).
    ///
    /// Layout, all in the canonical encoding: magic "ELSN" (4 bytes), state schema version (int32),
    /// build id (string), tick convention (string), config hash (uint64), maths backend (int32),
    /// seed (uint64), tick (int64), then the state's canonical encoding.
    ///
    /// Restoring checks the schema version, config hash and maths backend, so a snapshot can only
    /// continue under the config that produced it. The build id is recorded but not enforced:
    /// states are meant to migrate across versions (INV-7), and there is no migration yet.
    /// </summary>
    public sealed class Snapshot
    {
        private static readonly byte[] Magic = { (byte)'E', (byte)'L', (byte)'S', (byte)'N' };

        private Snapshot(int schemaVersion, string buildId, string tickConvention, ulong configHash,
            MathBackend mathBackend, SimState state)
        {
            SchemaVersion = schemaVersion;
            BuildId = buildId;
            TickConvention = tickConvention;
            ConfigHash = configHash;
            MathBackend = mathBackend;
            State = state;
        }

        public int SchemaVersion { get; }
        public string BuildId { get; }
        public string TickConvention { get; }
        public ulong ConfigHash { get; }
        public MathBackend MathBackend { get; }
        public SimState State { get; }
        public ulong Seed => State.Seed;
        public long Tick => State.Tick;

        public static byte[] Capture(Simulator simulator)
        {
            SimState state = simulator.PeekState();
            var w = new CanonicalWriter(512);
            w.WriteBytes(Magic);
            w.WriteInt32(SimulationInfo.StateSchemaVersion);
            w.WriteString(SimulationInfo.BuildId);
            w.WriteString(SimulationInfo.TickConvention);
            w.WriteUInt64(simulator.Config.Hash);
            w.WriteInt32((int)simulator.Config.MathBackend);
            w.WriteUInt64(state.Seed);
            w.WriteInt64(state.Tick);
            state.Encode(w);
            return w.ToArray();
        }

        public static Snapshot Read(byte[] data)
        {
            var r = new CanonicalReader(data);
            foreach (byte b in Magic)
            {
                if (r.ReadByte() != b) throw new FormatException("Not an ELEVEN snapshot.");
            }

            int schema = r.ReadInt32();
            if (schema != SimulationInfo.StateSchemaVersion)
            {
                throw new FormatException($"Snapshot schema {schema}; this build reads {SimulationInfo.StateSchemaVersion}.");
            }

            string buildId = r.ReadString();
            string convention = r.ReadString();
            ulong configHash = r.ReadUInt64();
            var backend = (MathBackend)r.ReadInt32();
            ulong seed = r.ReadUInt64();
            long tick = r.ReadInt64();
            SimState state = SimState.Decode(r);

            if (!r.AtEnd) throw new FormatException("Trailing bytes after snapshot state.");
            if (state.Seed != seed || state.Tick != tick) throw new FormatException("Snapshot header disagrees with its state.");

            return new Snapshot(schema, buildId, convention, configHash, backend, state);
        }

        /// <summary>Restores into a fresh simulator. Fails if the snapshot came from another config.</summary>
        public static Simulator Restore(byte[] data, SimConfig config)
        {
            Snapshot snapshot = Read(data);
            if (snapshot.TickConvention != SimulationInfo.TickConvention)
                throw new InvalidOperationException("Snapshot uses a different tick convention.");
            if (snapshot.ConfigHash != config.Hash)
                throw new InvalidOperationException($"Snapshot config hash {snapshot.ConfigHash:x16}, given {config.Hash:x16}.");
            if (snapshot.MathBackend != config.MathBackend)
                throw new InvalidOperationException("Snapshot maths backend differs from the config's.");

            return new Simulator(config, snapshot.State);
        }
    }
}
