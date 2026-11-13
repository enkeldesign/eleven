// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation.Encoding;

namespace Eleven.Simulation.Harness
{
    /// <summary>The outcome of replaying a <see cref="FeelCapture"/>.</summary>
    public sealed class FeelReplay
    {
        /// <summary>Every tick's hash and every event matched the capture.</summary>
        public bool Matches;

        /// <summary>Why it didn't, in words: a version mismatch, or the first divergence.</summary>
        public string Problem = "";

        /// <summary>The first tick whose hash differs, or null.</summary>
        public long? FirstDivergentTick;

        /// <summary>Tick hashes compared; 0 when a version mismatch stopped the replay before it began.</summary>
        public int TicksCompared;
    }

    /// <summary>
    /// The "this felt wrong" capture (#44): the last seconds of a run, with everything needed to
    /// replay them exactly and the context they were felt in. Evidence, not telemetry: it is made
    /// and kept locally, with no service (INV-15).
    ///
    /// Encoding (canonical, see docs/simulation/determinism.md): magic "ELFC", capture format
    /// (int32); host build, simulation build, tick convention (strings); state schema, config
    /// format, recording format (int32); config hash (uint64), seed (uint64), start tick (int64),
    /// step count (int32); active preset, control candidate, note (strings); config payload and
    /// recording payload (int32 length, then bytes); the tick hashes (int32 count, uint64 each);
    /// the events (int32 count, then tick int64, kind, subject, rejection, other int32 each).
    ///
    /// The versions come before the payloads, so a capture from another build is read far enough
    /// to say exactly which version differs, and is never replayed under the wrong rules.
    /// </summary>
    public sealed class FeelCapture
    {
        public const int FormatVersion = 1;
        private static readonly byte[] Magic = { (byte)'E', (byte)'L', (byte)'F', (byte)'C' };

        /// <summary>The host's build id: the CI run number and commit, for the web build.</summary>
        public string HostBuild = "";

        /// <summary>The simulation assembly's build id (<see cref="SimulationInfo.BuildId"/>).</summary>
        public string SimulationBuild = "";

        public string TickConvention = "";
        public int StateSchemaVersion;
        public int ConfigFormatVersion;
        public int RecordingFormatVersion;

        /// <summary><see cref="SimConfig.Encode"/> of the config the run used, and its hash.</summary>
        public byte[] ConfigPayload = Array.Empty<byte>();

        public ulong ConfigHash;

        /// <summary>The window's <see cref="CommandRecording"/>: the snapshot at its start and every owned command, encoded.</summary>
        public byte[] RecordingPayload = Array.Empty<byte>();

        /// <summary>The live run's state hash at the window's start and after every step in it.</summary>
        public List<ulong> TickHashes = new List<ulong>();

        /// <summary>Every event the live run emitted in the window.</summary>
        public List<SimEvent> Events = new List<SimEvent>();

        /// <summary>The preset the run was played under (technical-reference until #46).</summary>
        public string ActivePreset = "";

        /// <summary>The active touch control candidate and every one of its values (#28, #76): <c>action-surface/v1 …</c> or <c>split/v1 …</c>.</summary>
        public string ControlCandidate = "";

        /// <summary>What felt wrong, in the player's words; may be empty.</summary>
        public string Note = "";

        public long StartTick;
        public int StepCount;
        public ulong Seed;

        public byte[] Encode()
        {
            var w = new CanonicalWriter(4096);
            w.WriteBytes(Magic);
            w.WriteInt32(FormatVersion);
            w.WriteString(HostBuild);
            w.WriteString(SimulationBuild);
            w.WriteString(TickConvention);
            w.WriteInt32(StateSchemaVersion);
            w.WriteInt32(ConfigFormatVersion);
            w.WriteInt32(RecordingFormatVersion);
            w.WriteUInt64(ConfigHash);
            w.WriteUInt64(Seed);
            w.WriteInt64(StartTick);
            w.WriteInt32(StepCount);
            w.WriteString(ActivePreset);
            w.WriteString(ControlCandidate);
            w.WriteString(Note);
            WritePayload(w, ConfigPayload);
            WritePayload(w, RecordingPayload);
            w.WriteInt32(TickHashes.Count);
            foreach (ulong hash in TickHashes) w.WriteUInt64(hash);
            w.WriteInt32(Events.Count);
            foreach (SimEvent e in Events)
            {
                w.WriteInt64(e.Tick);
                w.WriteInt32((int)e.Kind);
                w.WriteInt32(e.Subject);
                w.WriteInt32((int)e.Rejection);
                w.WriteInt32(e.Other);
            }

            return w.ToArray();
        }

        /// <summary>Reads a capture. Fails clearly on anything that isn't one, or on another capture format.</summary>
        public static FeelCapture Decode(byte[] data)
        {
            var r = new CanonicalReader(data ?? throw new ArgumentNullException(nameof(data)));
            foreach (byte b in Magic)
            {
                if (data.Length < Magic.Length || r.ReadByte() != b) throw new FormatException("Not an ELEVEN feel capture.");
            }

            int format = r.ReadInt32();
            if (format != FormatVersion) throw new FormatException($"Feel capture format {format}; this build reads capture format {FormatVersion}.");

            var capture = new FeelCapture
            {
                HostBuild = r.ReadString(),
                SimulationBuild = r.ReadString(),
                TickConvention = r.ReadString(),
                StateSchemaVersion = r.ReadInt32(),
                ConfigFormatVersion = r.ReadInt32(),
                RecordingFormatVersion = r.ReadInt32(),
                ConfigHash = r.ReadUInt64(),
                Seed = r.ReadUInt64(),
                StartTick = r.ReadInt64(),
                StepCount = r.ReadInt32(),
                ActivePreset = r.ReadString(),
                ControlCandidate = r.ReadString(),
                Note = r.ReadString(),
            };

            capture.ConfigPayload = ReadPayload(r);
            capture.RecordingPayload = ReadPayload(r);
            int hashes = ReadCount(r);
            for (int i = 0; i < hashes; i++) capture.TickHashes.Add(r.ReadUInt64());
            int events = ReadCount(r);
            for (int i = 0; i < events; i++)
            {
                long tick = r.ReadInt64();
                var kind = (SimEventKind)r.ReadInt32();
                int subject = r.ReadInt32();
                var rejection = (CommandRejection)r.ReadInt32();
                capture.Events.Add(new SimEvent(tick, kind, subject, rejection, r.ReadInt32()));
            }

            if (!r.AtEnd) throw new FormatException("Trailing bytes after the feel capture.");
            return capture;
        }

        /// <summary>
        /// Why this build can't replay the capture, or null if it can. Every version is checked by
        /// name before anything is replayed.
        /// </summary>
        public string? VersionProblem()
        {
            var problems = new List<string>();
            if (TickConvention != SimulationInfo.TickConvention) problems.Add($"tick convention \"{TickConvention}\", this build uses \"{SimulationInfo.TickConvention}\"");
            if (StateSchemaVersion != SimulationInfo.StateSchemaVersion) problems.Add($"state schema {StateSchemaVersion}, this build reads {SimulationInfo.StateSchemaVersion}");
            if (ConfigFormatVersion != SimConfig.FormatVersion) problems.Add($"config format {ConfigFormatVersion}, this build reads {SimConfig.FormatVersion}");
            if (RecordingFormatVersion != CommandRecording.FormatVersion) problems.Add($"recording format {RecordingFormatVersion}, this build reads {CommandRecording.FormatVersion}");
            return problems.Count == 0 ? null : $"Made by another build (host {HostBuild}): {string.Join("; ", problems)}. Not replayed.";
        }

        /// <summary>
        /// Replays the window under the captured config and compares every tick's hash and every
        /// event with what the live run produced.
        /// </summary>
        public FeelReplay Replay()
        {
            string? problem = VersionProblem();
            if (problem != null) return new FeelReplay { Problem = problem };

            SimConfig config = SimConfig.Decode(ConfigPayload);
            if (config.Hash != ConfigHash)
            {
                return new FeelReplay { Problem = $"The config payload hashes to {config.Hash:x16}, the capture says {ConfigHash:x16}. Not replayed." };
            }

            var events = new List<SimEvent>();
            var hashes = new List<ulong>();
            CommandRecording.Decode(RecordingPayload).Replay(config, events, hashes);

            var result = new FeelReplay { TicksCompared = Math.Min(hashes.Count, TickHashes.Count) };
            for (int i = 0; i < result.TicksCompared; i++)
            {
                if (hashes[i] == TickHashes[i]) continue;
                result.FirstDivergentTick = StartTick + i;
                result.Problem = $"Tick {StartTick + i}: replayed hash {hashes[i]:x16}, captured {TickHashes[i]:x16}.";
                return result;
            }

            if (hashes.Count != TickHashes.Count)
            {
                result.Problem = $"Replayed {hashes.Count} tick hashes, captured {TickHashes.Count}.";
                return result;
            }

            if (!SameEvents(events, Events))
            {
                result.Problem = $"Replayed {events.Count} events, captured {Events.Count}, and they differ.";
                return result;
            }

            result.Matches = true;
            return result;
        }

        private static bool SameEvents(List<SimEvent> a, List<SimEvent> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }

            return true;
        }

        private static void WritePayload(CanonicalWriter w, byte[] payload)
        {
            w.WriteInt32(payload.Length);
            w.WriteBytes(payload);
        }

        private static byte[] ReadPayload(CanonicalReader r)
        {
            var payload = new byte[ReadCount(r)];
            for (int i = 0; i < payload.Length; i++) payload[i] = r.ReadByte();
            return payload;
        }

        private static int ReadCount(CanonicalReader r)
        {
            int count = r.ReadInt32();
            if (count < 0) throw new FormatException("Negative count in the feel capture.");
            return count;
        }
    }
}
