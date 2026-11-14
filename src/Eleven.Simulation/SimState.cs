using System;
using Eleven.Simulation.Encoding;

namespace Eleven.Simulation
{
    /// <summary>
    /// The complete authoritative state (INV-3): everything that can affect the next tick that
    /// isn't in <see cref="SimConfig"/>. Plain data, ids instead of references, no behaviour
    /// beyond copying and encoding.
    /// </summary>
    public sealed class SimState
    {
        /// <summary>
        /// Steps completed so far. Step N applies the commands for tick N and turns state N into
        /// state N + 1 (see <see cref="SimulationInfo.TickConvention"/>).
        /// </summary>
        public long Tick;

        /// <summary>Seed of every random stream for this run.</summary>
        public ulong Seed;

        public BallState Ball;

        /// <summary>Controllable players, in ascending id order. Slice 1 has exactly one.</summary>
        public PlayerState[] Players = Array.Empty<PlayerState>();

        /// <summary>The keepers (#69), in ascending player id order, each one of <see cref="Players"/>.</summary>
        public KeeperState[] Keepers = Array.Empty<KeeperState>();

        /// <summary>
        /// The two sides of a match (#82), in ascending id order; empty for practice, which has one
        /// goal, at +x, and no sides. Each player's <see cref="PlayerState.TeamId"/> is one of them.
        /// </summary>
        public TeamState[] Teams = Array.Empty<TeamState>();

        /// <summary>The CPU outfielders (#83), in ascending player id order, each one of <see cref="Players"/> and not a keeper.</summary>
        public CpuState[] Cpus = Array.Empty<CpuState>();

        public SimState Clone()
        {
            var copy = (SimState)MemberwiseClone();
            copy.Teams = (TeamState[])Teams.Clone();
            copy.Cpus = (CpuState[])Cpus.Clone();
            copy.Players = (PlayerState[])Players.Clone();
            copy.Keepers = (KeeperState[])Keepers.Clone();
            return copy;
        }

        /// <summary>
        /// Canonical encoding, in this order: Tick (int64), Seed (uint64), Ball (its goal-line state one byte), player count (int32),
        /// each player, keeper count (int32), each keeper, team count (int32), each team, CPU count
        /// (int32), each CPU outfielder. Field order within each is as declared, capabilities in their own
        /// declaration order. Fails on NaN or ±Infinity anywhere.
        /// </summary>
        public void Encode(CanonicalWriter w)
        {
            w.WriteInt64(Tick);
            w.WriteUInt64(Seed);

            w.WriteInt32(Ball.Id);
            WriteVec3(w, Ball.Position, "Ball.Position");
            WriteVec3(w, Ball.Velocity, "Ball.Velocity");
            WriteVec3(w, Ball.Spin, "Ball.Spin");
            w.WriteByte((byte)Ball.Line);
            w.WriteInt32(Ball.AttachedTo);
            w.WriteInt32(Ball.HeldBy);

            w.WriteInt32(Players.Length);
            foreach (PlayerState player in Players)
            {
                w.WriteInt32(player.Id);
                w.WriteInt32(player.OwnerControllerId);
                w.WriteInt32(player.TeamId);
                w.WriteDouble(player.Capabilities.MaxSpeed, "Player.Capabilities.MaxSpeed");
                w.WriteDouble(player.Capabilities.Acceleration, "Player.Capabilities.Acceleration");
                w.WriteDouble(player.Capabilities.KickPower, "Player.Capabilities.KickPower");
                w.WriteDouble(player.Capabilities.KickError, "Player.Capabilities.KickError");
                w.WriteDouble(player.Capabilities.PassVision, "Player.Capabilities.PassVision");
                w.WriteDouble(player.Capabilities.PassAnticipation, "Player.Capabilities.PassAnticipation");
                w.WriteDouble(player.Capabilities.BallControl, "Player.Capabilities.BallControl");
                WriteVec2(w, player.Position, "Player.Position");
                WriteVec2(w, player.Velocity, "Player.Velocity");
                w.WriteDouble(player.Heading, "Player.Heading");
                w.WriteInt32(player.KickCooldown);
                w.WriteInt32(player.WindUpTicks);
                w.WriteDouble(player.WindUpDirection, "Player.WindUpDirection");
                w.WriteDouble(player.WindUpStrength, "Player.WindUpStrength");
                w.WriteDouble(player.WindUpLoft, "Player.WindUpLoft");
                w.WriteInt32(player.WindUpTarget);
                w.WriteInt32(player.AftertouchTicks);
                w.WriteDouble(player.AftertouchDirection, "Player.AftertouchDirection");
                w.WriteInt32(player.TouchCooldown);
                w.WriteByte(player.Turning ? (byte)1 : (byte)0);
                w.WriteInt32(player.CutCooldown);
            }

            w.WriteInt32(Keepers.Length);
            foreach (KeeperState keeper in Keepers)
            {
                w.WriteInt32(keeper.PlayerId);
                w.WriteInt32((int)keeper.Phase);
                w.WriteInt32(keeper.PhaseTicks);
                WriteVec2(w, keeper.DiveVelocity, "Keeper.DiveVelocity");
                w.WriteDouble(keeper.DiveTop, "Keeper.DiveTop");
                w.WriteDouble(keeper.Capabilities.ReactionTime, "Keeper.Capabilities.ReactionTime");
                w.WriteDouble(keeper.Capabilities.DiveSpeed, "Keeper.Capabilities.DiveSpeed");
                w.WriteDouble(keeper.Capabilities.Reach, "Keeper.Capabilities.Reach");
                w.WriteDouble(keeper.Capabilities.Handling, "Keeper.Capabilities.Handling");
            }

            w.WriteInt32(Teams.Length);
            foreach (TeamState team in Teams)
            {
                w.WriteInt32(team.Id);
                w.WriteInt32(team.AttackSign);
            }

            w.WriteInt32(Cpus.Length);
            foreach (CpuState cpu in Cpus)
            {
                w.WriteInt32(cpu.PlayerId);
                w.WriteDouble(cpu.Skill.DecisionInterval, "Cpu.Skill.DecisionInterval");
                w.WriteDouble(cpu.Skill.ReactionDelay, "Cpu.Skill.ReactionDelay");
                w.WriteDouble(cpu.Skill.PerceptionError, "Cpu.Skill.PerceptionError");
                w.WriteDouble(cpu.Skill.VisionRange, "Cpu.Skill.VisionRange");
                w.WriteInt64(cpu.NextDecisionTick);
                WritePlan(w, cpu.Plan);
                w.WriteByte(cpu.HasNext ? (byte)1 : (byte)0);
                WritePlan(w, cpu.Next);
            }
        }

        private static void WritePlan(CanonicalWriter w, CpuPlanState plan)
        {
            w.WriteInt32((int)plan.Mode);
            WriteVec2(w, plan.Move, "Cpu.Plan.Move");
            w.WriteByte(plan.HasKick ? (byte)1 : (byte)0);
            WriteVec2(w, plan.KickDirection, "Cpu.Plan.KickDirection");
            w.WriteDouble(plan.KickStrength, "Cpu.Plan.KickStrength");
            w.WriteDouble(plan.KickLoft, "Cpu.Plan.KickLoft");
            w.WriteByte(plan.QuickPass ? (byte)1 : (byte)0);
            w.WriteInt64(plan.ObservedTick);
            w.WriteInt64(plan.ActsTick);
        }

        private static CpuPlanState ReadPlan(CanonicalReader r)
        {
            int mode = r.ReadInt32();
            if (mode < (int)CpuMode.None || mode > (int)CpuMode.Recover) throw new FormatException($"Unknown CPU mode {mode}.");
            return new CpuPlanState
            {
                Mode = (CpuMode)mode,
                Move = ReadVec2(r, "Cpu.Plan.Move"),
                HasKick = ReadFlag(r.ReadByte(), "Cpu.Plan.HasKick"),
                KickDirection = ReadVec2(r, "Cpu.Plan.KickDirection"),
                KickStrength = r.ReadDouble("Cpu.Plan.KickStrength"),
                KickLoft = r.ReadDouble("Cpu.Plan.KickLoft"),
                QuickPass = ReadFlag(r.ReadByte(), "Cpu.Plan.QuickPass"),
                ObservedTick = r.ReadInt64(),
                ActsTick = r.ReadInt64(),
            };
        }

        public static SimState Decode(CanonicalReader r)
        {
            var state = new SimState
            {
                Tick = r.ReadInt64(),
                Seed = r.ReadUInt64(),
            };

            state.Ball.Id = r.ReadInt32();
            state.Ball.Position = ReadVec3(r, "Ball.Position");
            state.Ball.Velocity = ReadVec3(r, "Ball.Velocity");
            state.Ball.Spin = ReadVec3(r, "Ball.Spin");
            state.Ball.Line = ReadLine(r.ReadByte());
            state.Ball.AttachedTo = r.ReadInt32();
            state.Ball.HeldBy = r.ReadInt32();

            int count = r.ReadInt32();
            if (count < 0 || count > 1024) throw new FormatException($"Implausible player count {count}.");
            state.Players = new PlayerState[count];
            for (int i = 0; i < count; i++)
            {
                state.Players[i] = new PlayerState
                {
                    Id = r.ReadInt32(),
                    OwnerControllerId = r.ReadInt32(),
                    TeamId = r.ReadInt32(),
                    Capabilities = new PlayerCapabilities
                    {
                        MaxSpeed = r.ReadDouble("Player.Capabilities.MaxSpeed"),
                        Acceleration = r.ReadDouble("Player.Capabilities.Acceleration"),
                        KickPower = r.ReadDouble("Player.Capabilities.KickPower"),
                        KickError = r.ReadDouble("Player.Capabilities.KickError"),
                        PassVision = r.ReadDouble("Player.Capabilities.PassVision"),
                        PassAnticipation = r.ReadDouble("Player.Capabilities.PassAnticipation"),
                        BallControl = r.ReadDouble("Player.Capabilities.BallControl"),
                    },
                    Position = ReadVec2(r, "Player.Position"),
                    Velocity = ReadVec2(r, "Player.Velocity"),
                    Heading = r.ReadDouble("Player.Heading"),
                    KickCooldown = r.ReadInt32(),
                    WindUpTicks = r.ReadInt32(),
                    WindUpDirection = r.ReadDouble("Player.WindUpDirection"),
                    WindUpStrength = r.ReadDouble("Player.WindUpStrength"),
                    WindUpLoft = r.ReadDouble("Player.WindUpLoft"),
                    WindUpTarget = r.ReadInt32(),
                    AftertouchTicks = r.ReadInt32(),
                    AftertouchDirection = r.ReadDouble("Player.AftertouchDirection"),
                    TouchCooldown = r.ReadInt32(),
                    Turning = ReadFlag(r.ReadByte(), "Player.Turning"),
                    CutCooldown = r.ReadInt32(),
                };
            }

            int keepers = r.ReadInt32();
            if (keepers < 0 || keepers > count) throw new FormatException($"Implausible keeper count {keepers}.");
            state.Keepers = new KeeperState[keepers];
            for (int i = 0; i < keepers; i++)
            {
                state.Keepers[i] = new KeeperState
                {
                    PlayerId = r.ReadInt32(),
                    Phase = ReadPhase(r.ReadInt32()),
                    PhaseTicks = r.ReadInt32(),
                    DiveVelocity = ReadVec2(r, "Keeper.DiveVelocity"),
                    DiveTop = r.ReadDouble("Keeper.DiveTop"),
                    Capabilities = new KeeperCapabilities
                    {
                        ReactionTime = r.ReadDouble("Keeper.Capabilities.ReactionTime"),
                        DiveSpeed = r.ReadDouble("Keeper.Capabilities.DiveSpeed"),
                        Reach = r.ReadDouble("Keeper.Capabilities.Reach"),
                        Handling = r.ReadDouble("Keeper.Capabilities.Handling"),
                    },
                };
            }

            int teams = r.ReadInt32();
            if (teams != 0 && teams != 2) throw new FormatException($"A state has no teams or two, not {teams}.");
            state.Teams = new TeamState[teams];
            for (int i = 0; i < teams; i++)
            {
                state.Teams[i] = new TeamState { Id = r.ReadInt32(), AttackSign = r.ReadInt32() };
            }

            int cpus = r.ReadInt32();
            if (cpus < 0 || cpus > count) throw new FormatException($"Implausible CPU count {cpus}.");
            state.Cpus = new CpuState[cpus];
            for (int i = 0; i < cpus; i++)
            {
                state.Cpus[i] = new CpuState
                {
                    PlayerId = r.ReadInt32(),
                    Skill = new CpuSkill
                    {
                        DecisionInterval = r.ReadDouble("Cpu.Skill.DecisionInterval"),
                        ReactionDelay = r.ReadDouble("Cpu.Skill.ReactionDelay"),
                        PerceptionError = r.ReadDouble("Cpu.Skill.PerceptionError"),
                        VisionRange = r.ReadDouble("Cpu.Skill.VisionRange"),
                    },
                    NextDecisionTick = r.ReadInt64(),
                    Plan = ReadPlan(r),
                    HasNext = ReadFlag(r.ReadByte(), "Cpu.HasNext"),
                    Next = ReadPlan(r),
                };
            }

            return state;
        }

        private static bool ReadFlag(byte value, string field) =>
            value <= 1 ? value == 1 : throw new FormatException($"{field} must be 0 or 1, not {value}.");

        private static KeeperPhase ReadPhase(int value) =>
            value >= (int)KeeperPhase.Position && value <= (int)KeeperPhase.Recover ? (KeeperPhase)value : throw new FormatException($"Unknown keeper phase {value}.");

        /// <summary>FNV-1a 64 of the canonical encoding.</summary>
        public ulong ComputeHash(CanonicalWriter scratch)
        {
            scratch.Clear();
            Encode(scratch);
            return scratch.Hash();
        }

        private static LineState ReadLine(byte value) =>
            value <= (byte)LineState.CrossingWide ? (LineState)value : throw new FormatException($"Unknown goal-line state {value}.");

        private static void WriteVec3(CanonicalWriter w, Vec3 v, string field)
        {
            w.WriteDouble(v.X, field);
            w.WriteDouble(v.Y, field);
            w.WriteDouble(v.Z, field);
        }

        private static void WriteVec2(CanonicalWriter w, Vec2 v, string field)
        {
            w.WriteDouble(v.X, field);
            w.WriteDouble(v.Y, field);
        }

        private static Vec3 ReadVec3(CanonicalReader r, string field) =>
            new Vec3(r.ReadDouble(field), r.ReadDouble(field), r.ReadDouble(field));

        private static Vec2 ReadVec2(CanonicalReader r, string field) =>
            new Vec2(r.ReadDouble(field), r.ReadDouble(field));
    }

    /// <summary>
    /// One side of a match (#82): plain data. Which goal a team attacks is state, not a property of
    /// the team or of the screen, so the sides can change ends between halves.
    /// </summary>
    public struct TeamState
    {
        /// <summary>The side's id in this match, positive. Players carry it as <see cref="PlayerState.TeamId"/>; it is not a controller's id.</summary>
        public int Id;

        /// <summary>
        /// +1 or −1: the sign of world x this side attacks toward. It scores in the goal on that
        /// line, and its keeper guards the goal on the other.
        /// </summary>
        public int AttackSign;
    }

    public struct BallState
    {
        public int Id;

        /// <summary>Centre, m. Resting on the ground means z = ball radius.</summary>
        public Vec3 Position;

        /// <summary>m/s.</summary>
        public Vec3 Velocity;

        /// <summary>
        /// Angular velocity, rad/s. A kick launches the ball without spin; sideways aftertouch adds
        /// sidespin about +z (#37). Physical state: it keeps decaying, and curving the ball in flight,
        /// after the aftertouch window closes.
        /// </summary>
        public Vec3 Spin;

        /// <summary>
        /// Where the ball stood against the goal line after the previous step (#68): in play, its
        /// centre over the line through the mouth or wide of it, or wholly beyond as a goal or out.
        /// Needed so a crossing emits its events once, and so a goal is judged where the ball's
        /// centre crossed the line. docs/simulation/goal.md.
        /// </summary>
        public LineState Line;

        /// <summary>
        /// Under ATTACHED (#60), the id of the player the ball is attached to; 0 when it is free.
        /// Any other model clears it on its first step.
        /// </summary>
        public int AttachedTo;

        /// <summary>
        /// The id of the keeper holding the ball (#69); 0 when no one is. A held ball stays where it
        /// was caught and moves with him; no kick or touch reaches it.
        /// </summary>
        public int HeldBy;
    }

    public struct PlayerState
    {
        public int Id;

        /// <summary>The controller whose commands move this player (INV-10).</summary>
        public int OwnerControllerId;

        /// <summary>The player's team. Teammates share it; a quick pass only ever goes to a teammate (#38).</summary>
        public int TeamId;

        /// <summary>What this footballer can do. Read by the simulation, never changed by it.</summary>
        public PlayerCapabilities Capabilities;

        /// <summary>m, on the pitch plane.</summary>
        public Vec2 Position;

        /// <summary>m/s.</summary>
        public Vec2 Velocity;

        /// <summary>
        /// rad, direction of travel; kept when the player stops. A kick intent with no direction goes this way.
        /// </summary>
        public double Heading;

        /// <summary>
        /// Kick cooldown: ticks left before this player can start another kick. Set to the configured
        /// cooldown when a kick connects, decreased by one at the start of each later step. It runs
        /// alongside the aftertouch window, not after it.
        /// </summary>
        public int KickCooldown;

        /// <summary>
        /// Kick wind-up (#37): steps left until the kick connects, counting the step that connects;
        /// 0 when not winding up. An accepted intent starts it at the configured wind-up, and the step
        /// that brings it to 0 makes contact, or misses if the ball is out of reach.
        /// </summary>
        public int WindUpTicks;

        /// <summary>rad. The intended line, fixed when the intent arrived and never retargeted.</summary>
        public double WindUpDirection;

        /// <summary>0–1. The intent's strength, kept through the wind-up.</summary>
        public double WindUpStrength;

        /// <summary>0–1. The intent's loft, kept through the wind-up.</summary>
        public double WindUpLoft;

        /// <summary>
        /// The id of the teammate a quick pass is meant for (#38), kept through the wind-up so
        /// presentation can show the receiver; 0 for a manual kick, a free pass or no wind-up.
        /// Player ids are positive.
        /// </summary>
        public int WindUpTarget;

        /// <summary>
        /// Aftertouch input window (#37): steps left in which this player's aftertouch input shapes
        /// the ball they last kicked; 0 when closed. Opened at the configured length when a kick
        /// connects, closed when anyone's kick connects. It gates input only, not the ball's spin.
        /// </summary>
        public int AftertouchTicks;

        /// <summary>rad. The intended line of the kick being shaped: aftertouch is measured against it.</summary>
        public double AftertouchDirection;

        /// <summary>
        /// Touch cooldown (#59): ticks left before this player can touch the ball again. Set to the
        /// configured touch interval by a touch, decreased by one at the start of each later step.
        /// </summary>
        public int TouchCooldown;

        /// <summary>
        /// In a turn with the ball (#66): this player's last touch was a turn's trap, and they are not
        /// yet moving the way the stick asks. The turn's second touch, out of it, clears it.
        /// docs/simulation/turn.md.
        /// </summary>
        public bool Turning;

        /// <summary>
        /// Steps until this player can cut with the ball again under FREE (#75): a cut touch, or a
        /// turn's touch out, which stands for one as they come round, sets it to the touch interval,
        /// so there is at most one cut per stride. docs/simulation/cut.md.
        /// </summary>
        public int CutCooldown;
    }
}
