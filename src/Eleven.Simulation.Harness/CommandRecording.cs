// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation.Encoding;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The owned commands of a session exactly as they were passed to the simulation, recorded
    /// before any assistance (INV-10), together with the snapshot the session started from. Replaying
    /// it headlessly reproduces the session tick for tick under the same build and config.
    ///
    /// Each step's command list is kept as passed, in order, including commands the simulation
    /// rejects, so the replay sees exactly what the live simulation saw.
    ///
    /// Encoding (canonical, see docs/simulation/determinism.md): magic "ELRC", format version
    /// (int32), snapshot length (int32) and bytes, step count (int32), then per step a command count
    /// (int32) and per command: controller id (int32), tick (int64), move x and y (the raw IEEE-754
    /// bits as uint64, so even invalid input is preserved), kick (bool).
    /// </summary>
    public sealed class CommandRecording
    {
        public const int FormatVersion = 1;
        private static readonly byte[] Magic = { (byte)'E', (byte)'L', (byte)'R', (byte)'C' };

        private readonly List<Command[]> _steps = new List<Command[]>();

        public CommandRecording(byte[] startSnapshot)
        {
            StartSnapshot = startSnapshot ?? throw new ArgumentNullException(nameof(startSnapshot));
            StartTick = Snapshot.Read(startSnapshot).Tick;
        }

        /// <summary>Starts a recording at <paramref name="simulator"/>'s current state.</summary>
        public static CommandRecording StartFrom(Simulator simulator) => new CommandRecording(Snapshot.Capture(simulator));

        public byte[] StartSnapshot { get; }

        public long StartTick { get; }

        /// <summary>Steps recorded so far; the replay ends at tick <see cref="StartTick"/> + this.</summary>
        public int StepCount => _steps.Count;

        /// <summary>Records one step's commands. Call with the same list, just before passing it to <see cref="Simulator.Step"/>.</summary>
        public void Record(IReadOnlyList<Command> commands)
        {
            var copy = new Command[commands.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = commands[i];
            _steps.Add(copy);
        }

        /// <summary>Restores the start snapshot into a fresh simulator and replays every recorded step.</summary>
        public Simulator Replay(SimConfig config, List<SimEvent>? events = null)
        {
            Simulator simulator = Snapshot.Restore(StartSnapshot, config);
            events ??= new List<SimEvent>();
            foreach (Command[] step in _steps) simulator.Step(step, events);
            return simulator;
        }

        public byte[] Encode()
        {
            var w = new CanonicalWriter(1024);
            w.WriteBytes(Magic);
            w.WriteInt32(FormatVersion);
            w.WriteInt32(StartSnapshot.Length);
            w.WriteBytes(StartSnapshot);
            w.WriteInt32(_steps.Count);
            foreach (Command[] step in _steps)
            {
                w.WriteInt32(step.Length);
                foreach (Command c in step)
                {
                    w.WriteInt32(c.ControllerId);
                    w.WriteInt64(c.Tick);
                    w.WriteUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(c.Move.X)));
                    w.WriteUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(c.Move.Y)));
                    w.WriteBool(c.Kick);
                }
            }

            return w.ToArray();
        }

        public static CommandRecording Decode(byte[] data)
        {
            var r = new CanonicalReader(data);
            foreach (byte b in Magic)
            {
                if (r.ReadByte() != b) throw new FormatException("Not an ELEVEN command recording.");
            }

            int version = r.ReadInt32();
            if (version != FormatVersion) throw new FormatException($"Recording format {version}; this build reads {FormatVersion}.");

            var snapshot = new byte[ReadCount(r)];
            for (int i = 0; i < snapshot.Length; i++) snapshot[i] = r.ReadByte();
            var recording = new CommandRecording(snapshot);

            int steps = ReadCount(r);
            for (int s = 0; s < steps; s++)
            {
                var step = new Command[ReadCount(r)];
                for (int i = 0; i < step.Length; i++)
                {
                    int controller = r.ReadInt32();
                    long tick = r.ReadInt64();
                    double x = BitConverter.Int64BitsToDouble(unchecked((long)r.ReadUInt64()));
                    double y = BitConverter.Int64BitsToDouble(unchecked((long)r.ReadUInt64()));
                    step[i] = new Command(controller, tick, new Vec2(x, y), r.ReadBool());
                }

                recording._steps.Add(step);
            }

            if (!r.AtEnd) throw new FormatException("Trailing bytes after recording.");
            return recording;
        }

        private static int ReadCount(CanonicalReader r)
        {
            int count = r.ReadInt32();
            if (count < 0) throw new FormatException("Negative count.");
            return count;
        }
    }
}
