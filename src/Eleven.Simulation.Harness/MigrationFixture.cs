using System;
using System.Collections.Generic;
using System.Globalization;
using Eleven.Simulation.Encoding;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A recording made under one state schema, and its play, for checking that its start state,
    /// migrated to a later schema, replays as it played (#166, INV-7, INV-10). Five a side with
    /// the seat switching by itself, home's CPUs at one skill and away's each at his own, recorded
    /// from <see cref="From"/> seconds in for <see cref="Seconds"/>.
    /// <para>
    /// Its play is what a state hash can't carry across a schema: every footballer's place,
    /// velocity, heading, fatigue and controller, the ball, the score and phase, and each CPU's
    /// timing, every tick; then every event.
    /// </para>
    /// </summary>
    public static class MigrationFixture
    {
        public const ulong Seed = 0x166;
        public const int PerSide = 5;
        public const int From = 10;
        public const int Seconds = 10;

        /// <summary>Home's CPUs all play at one skill, as every side did before #165.</summary>
        public static CpuSkill HomeSkill => CpuSkill.Club;

        /// <summary>Each of away's outfielders at his own, as a career's do (#159).</summary>
        public static CpuSkill AwaySkill(int id) => new CpuSkill
        {
            DecisionInterval = 0.10 + 0.03 * (id % 5),
            ReactionDelay = 0.12 + 0.02 * (id % 4),
            PerceptionError = 0.01 + 0.01 * (id % 3),
            VisionRange = 40 + 15 * (id % 5),
        };

        public static CpuSkill SkillOf(int id) => id <= PerSide ? HomeSkill : AwaySkill(id);

        /// <summary>Plays the match and records it.</summary>
        public static CommandRecording Record(SimConfig config)
        {
            var sim = new Simulator(config, Squads.WithSkills(Squads.Match(config, Seed, PerSide), SkillOf));
            var switcher = new SwitchController(SquadTrials.SeatController, SwitchMode.Auto);
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            CommandRecording? recording = null;
            long start = From * (long)config.TickRate, end = start + Seconds * (long)config.TickRate;
            while (sim.Tick < end)
            {
                if (sim.Tick == start) recording = CommandRecording.StartFrom(sim);
                commands.Clear();
                commands.Add(SquadTrials.Seat(sim, SquadTrials.SeatController, switcher.Decide(sim, events, manual: false)));
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                recording?.Record(commands);
                events.Clear();
                sim.Step(commands, events);
            }

            return recording!;
        }

        /// <summary>A recording's play, replayed: its steps, a digest of every tick, its events and a digest of them, and its switches.</summary>
        public static string Play(CommandRecording recording, SimConfig config)
        {
            var events = new List<SimEvent>();
            Simulator sim = Snapshot.Restore(recording.StartSnapshot, config);
            ulong play = Tick(Fnv1a64.OffsetBasis, sim.PeekState());
            var step = new List<SimEvent>();
            for (int i = 0; i < recording.StepCount; i++)
            {
                step.Clear();
                sim.Step(recording.Step(i), step);
                events.AddRange(step);
                play = Tick(play, sim.PeekState());
            }

            ulong happened = Fnv1a64.OffsetBasis;
            int switches = 0;
            foreach (SimEvent e in events)
            {
                var w = new CanonicalWriter(32);
                w.WriteInt64(e.Tick);
                w.WriteInt32((int)e.Kind);
                w.WriteInt32(e.Subject);
                w.WriteInt32((int)e.Rejection);
                w.WriteInt32(e.Other);
                happened = Fnv1a64.Append(happened, Fnv1a64.Hash(w.ToArray()));
                if (e.Kind == SimEventKind.Switched) switches++;
            }

            return string.Format(CultureInfo.InvariantCulture, "steps {0}\nplay {1:x16}\nevents {2} {3:x16}\nswitches {4}\n", recording.StepCount, play, events.Count, happened, switches);
        }

        // One tick's play folded into the digest so far.
        private static ulong Tick(ulong digest, SimState s)
        {
            var w = new CanonicalWriter(1024);
            w.WriteInt64(s.Tick);
            foreach (PlayerState p in s.Players)
            {
                w.WriteInt32(p.Id);
                w.WriteInt32(p.OwnerControllerId);
                w.WriteDouble(p.Position.X, "x");
                w.WriteDouble(p.Position.Y, "y");
                w.WriteDouble(p.Velocity.X, "vx");
                w.WriteDouble(p.Velocity.Y, "vy");
                w.WriteDouble(p.Heading, "heading");
                w.WriteDouble(p.Fatigue, "fatigue");
            }

            w.WriteDouble(s.Ball.Position.X, "ball x");
            w.WriteDouble(s.Ball.Position.Y, "ball y");
            w.WriteDouble(s.Ball.Position.Z, "ball z");
            w.WriteDouble(s.Ball.Velocity.X, "ball vx");
            w.WriteDouble(s.Ball.Velocity.Y, "ball vy");
            w.WriteDouble(s.Ball.Velocity.Z, "ball vz");
            w.WriteInt32(s.Ball.HeldBy);
            w.WriteInt32(s.Ball.AttachedTo);
            foreach (TeamState t in s.Teams) w.WriteInt32(t.Goals);
            w.WriteByte((byte)s.Match.Phase);
            w.WriteInt32(s.Match.ClockTicks);
            foreach (CpuState c in s.Cpus)
            {
                w.WriteInt32(c.PlayerId);
                w.WriteInt64(c.NextDecisionTick);
                w.WriteInt32((int)c.Plan.Mode);
                w.WriteInt64(c.Plan.ActsTick);
                w.WriteByte(c.HasNext ? (byte)1 : (byte)0);
                w.WriteInt64(c.Next.ActsTick);
            }

            return Fnv1a64.Append(digest, Fnv1a64.Hash(w.ToArray()));
        }
    }
}
