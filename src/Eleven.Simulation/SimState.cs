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

        /// <summary>The restart sequencer's state (#85): off in practice, on in a match.</summary>
        public RestartState Restart;

        /// <summary>The match's clock and halves (#86): none in practice.</summary>
        public MatchState Match;

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
        /// each player, keeper count (int32), each keeper, team count (int32), each team, the restart, CPU count
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
            w.WriteInt32(Ball.LastPlayedBy);

            w.WriteInt32(Players.Length);
            foreach (PlayerState player in Players)
            {
                w.WriteInt32(player.Id);
                w.WriteInt32(player.OwnerControllerId);
                w.WriteInt32(player.TeamId);
                w.WriteInt32(player.Slot);
                w.WriteDouble(player.Capabilities.MaxSpeed, "Player.Capabilities.MaxSpeed");
                w.WriteDouble(player.Capabilities.Acceleration, "Player.Capabilities.Acceleration");
                w.WriteDouble(player.Capabilities.KickPower, "Player.Capabilities.KickPower");
                w.WriteDouble(player.Capabilities.KickError, "Player.Capabilities.KickError");
                w.WriteDouble(player.Capabilities.PassVision, "Player.Capabilities.PassVision");
                w.WriteDouble(player.Capabilities.PassAnticipation, "Player.Capabilities.PassAnticipation");
                w.WriteDouble(player.Capabilities.BallControl, "Player.Capabilities.BallControl");
                w.WriteDouble(player.Capabilities.Tackling, "Player.Capabilities.Tackling");
                w.WriteDouble(player.Capabilities.Heading, "Player.Capabilities.Heading");
                w.WriteDouble(player.Capabilities.Strength, "Player.Capabilities.Strength");
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
                w.WriteByte((byte)player.Challenge);
                w.WriteInt32(player.ChallengeTicks);
                WriteVec2(w, player.ChallengeLine, "Player.ChallengeLine");
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
                w.WriteInt32(keeper.HoldTicks);
            }

            w.WriteInt32(Teams.Length);
            foreach (TeamState team in Teams)
            {
                w.WriteInt32(team.Id);
                w.WriteInt32(team.AttackSign);
                w.WriteInt32(team.Goals);
            }

            w.WriteByte(Restart.On ? (byte)1 : (byte)0);
            w.WriteByte((byte)Restart.Phase);
            w.WriteByte((byte)Restart.Kind);
            w.WriteInt32(Restart.AwardedTeam);
            w.WriteInt32(Restart.LastTouchedTeam);
            WriteVec2(w, Restart.Point, "Restart.Point");
            w.WriteInt32(Restart.PhaseTicks);
            w.WriteInt32(Restart.WaitTicks);
            w.WriteInt32(Restart.TakerId);
            w.WriteByte(Restart.FromThrow ? (byte)1 : (byte)0);

            w.WriteByte((byte)Match.Phase);
            w.WriteInt32(Match.ClockTicks);
            w.WriteInt32(Match.PhaseTicks);
            w.WriteInt32(Match.FirstKickOff);

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
            if (mode < (int)CpuMode.None || mode > (int)CpuMode.Support) throw new FormatException($"Unknown CPU mode {mode}.");
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
            state.Ball.LastPlayedBy = r.ReadInt32();

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
                    Slot = r.ReadInt32(),
                    Capabilities = new PlayerCapabilities
                    {
                        MaxSpeed = r.ReadDouble("Player.Capabilities.MaxSpeed"),
                        Acceleration = r.ReadDouble("Player.Capabilities.Acceleration"),
                        KickPower = r.ReadDouble("Player.Capabilities.KickPower"),
                        KickError = r.ReadDouble("Player.Capabilities.KickError"),
                        PassVision = r.ReadDouble("Player.Capabilities.PassVision"),
                        PassAnticipation = r.ReadDouble("Player.Capabilities.PassAnticipation"),
                        BallControl = r.ReadDouble("Player.Capabilities.BallControl"),
                        Tackling = r.ReadDouble("Player.Capabilities.Tackling"),
                        Heading = r.ReadDouble("Player.Capabilities.Heading"),
                        Strength = r.ReadDouble("Player.Capabilities.Strength"),
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
                    Challenge = ReadChallenge(r.ReadByte()),
                    ChallengeTicks = r.ReadInt32(),
                    ChallengeLine = ReadVec2(r, "Player.ChallengeLine"),
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
                    HoldTicks = r.ReadInt32(),
                };
            }

            int teams = r.ReadInt32();
            if (teams != 0 && teams != 2) throw new FormatException($"A state has no teams or two, not {teams}.");
            state.Teams = new TeamState[teams];
            for (int i = 0; i < teams; i++)
            {
                state.Teams[i] = new TeamState { Id = r.ReadInt32(), AttackSign = r.ReadInt32(), Goals = r.ReadInt32() };
            }

            state.Restart = new RestartState
            {
                On = ReadFlag(r.ReadByte(), "Restart.On"),
                Phase = r.ReadByte() is byte phase && phase <= (byte)RestartPhase.Ready ? (RestartPhase)phase : throw new FormatException("Unknown restart phase."),
                Kind = r.ReadByte() is byte kind && kind <= (byte)RestartKind.Corner ? (RestartKind)kind : throw new FormatException("Unknown restart kind."),
                AwardedTeam = r.ReadInt32(),
                LastTouchedTeam = r.ReadInt32(),
                Point = ReadVec2(r, "Restart.Point"),
                PhaseTicks = r.ReadInt32(),
                WaitTicks = r.ReadInt32(),
                TakerId = r.ReadInt32(),
                FromThrow = ReadFlag(r.ReadByte(), "Restart.FromThrow"),
            };

            state.Match = new MatchState
            {
                Phase = r.ReadByte() is byte matchPhase && matchPhase <= (byte)MatchPhase.FullTime ? (MatchPhase)matchPhase : throw new FormatException("Unknown match phase."),
                ClockTicks = r.ReadInt32(),
                PhaseTicks = r.ReadInt32(),
                FirstKickOff = r.ReadInt32(),
            };

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

        private static ChallengePhase ReadChallenge(byte value) =>
            value <= (byte)ChallengePhase.Recover ? (ChallengePhase)value : throw new FormatException($"Unknown challenge phase {value}.");

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

        /// <summary>Goals this side has scored in the match (#86).</summary>
        public int Goals;
    }

    /// <summary>Where a match is (#86). Numeric values are stable and encoded as one byte.</summary>
    public enum MatchPhase : byte
    {
        /// <summary>No match clock: practice, or a world without halves.</summary>
        None = 0,

        FirstHalf = 1,

        /// <summary>The break: the ball is dead, the clock stopped, and the sides then change ends.</summary>
        HalfTime = 2,

        SecondHalf = 3,

        /// <summary>The match is over: the ball is dead for good.</summary>
        FullTime = 4,
    }

    /// <summary>A match's clock and halves (#86): plain data. docs/simulation/match.md.</summary>
    public struct MatchState
    {
        public MatchPhase Phase;

        /// <summary>Steps of match clock played in this half: it runs only while the ball is in play.</summary>
        public int ClockTicks;

        /// <summary>Steps left of the half-time break.</summary>
        public int PhaseTicks;

        /// <summary>The side that kicked off the first half; the other kicks off the second.</summary>
        public int FirstKickOff;
    }

    /// <summary>Where the restart sequencer is (#85). Numeric values are stable and encoded as one byte.</summary>
    public enum RestartPhase : byte
    {
        /// <summary>The ball is in play.</summary>
        Live = 0,

        /// <summary>The ball went out of play or into a goal: it is dead, and the restart is awarded.</summary>
        Stopped = 1,

        /// <summary>The ball and the footballers are placed. Only the taker may move, and he puts the ball in play by kicking it.</summary>
        Ready = 2,
    }

    /// <summary>The restart awarded (#85). Numeric values are stable and encoded as one byte.</summary>
    public enum RestartKind : byte
    {
        None = 0,

        /// <summary>From the centre spot, to the side that conceded, or to start a half.</summary>
        KickOff = 1,

        /// <summary>Where the whole ball crossed a touchline, to the side that didn't touch it last.</summary>
        ThrowIn = 2,

        /// <summary>From the goal area, to the defenders, when an attacker touched it last over their goal line.</summary>
        GoalKick = 3,

        /// <summary>From the corner, to the attackers, when a defender touched it last over his own goal line.</summary>
        Corner = 4,
    }

    /// <summary>
    /// The restart sequencer's state (#85, INV-8): plain data. A match turns it on; practice
    /// leaves it off, and then the ball is never out of play. docs/simulation/restarts.md.
    /// </summary>
    public struct RestartState
    {
        /// <summary>Whether there is a referee: out of play, goals and restarts are judged.</summary>
        public bool On;

        public RestartPhase Phase;

        public RestartKind Kind;

        /// <summary>The side that takes the restart.</summary>
        public int AwardedTeam;

        /// <summary>The side of the footballer who last played the ball while it was in play; 0 before anyone has.</summary>
        public int LastTouchedTeam;

        /// <summary>m. Where the ball is placed for the restart.</summary>
        public Vec2 Point;

        /// <summary>Steps left in <see cref="Phase"/>: until the set-up when stopped, until it may be taken when ready.</summary>
        public int PhaseTicks;

        /// <summary>Steps it has waited, ready, since it could first be taken.</summary>
        public int WaitTicks;

        /// <summary>The footballer who takes it, once it is set up; 0 before.</summary>
        public int TakerId;

        /// <summary>Whether the ball is live from a throw-in no one else has played yet: it can't score.</summary>
        public bool FromThrow;
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

        /// <summary>
        /// The id of the player who last played the ball (#84): touched it, kicked it, knocked it
        /// away in a challenge, or saved it; 0 before anyone has. Whether an opponent controls the
        /// ball is read from this and where they are.
        /// </summary>
        public int LastPlayedBy;
    }

    /// <summary>
    /// A standing challenge's phase (#84): explicit, authoritative and timed by the simulation
    /// (INV-12). docs/simulation/challenge.md. Numeric values are stable and encoded as one byte.
    /// </summary>
    public enum ChallengePhase : byte
    {
        /// <summary>Not challenging: he can start one.</summary>
        Ready = 0,

        /// <summary>Committed: he keeps his line, can't steer, and reaches for the ball as it ends.</summary>
        Commit = 1,

        /// <summary>After a challenge, won or missed: he brakes, and can't play the ball or challenge.</summary>
        Recover = 2,

        /// <summary>A slide tackle (#98): committed along its line, slowing, meeting the ball or the man.</summary>
        Slide = 3,

        /// <summary>On the ground after a slide (#98): he can't move, play the ball or challenge.</summary>
        Grounded = 4,
    }

    public struct PlayerState
    {
        public int Id;

        /// <summary>The controller whose commands move this player (INV-10).</summary>
        public int OwnerControllerId;

        /// <summary>The player's team. Teammates share it; a quick pass only ever goes to a teammate (#38).</summary>
        public int TeamId;

        /// <summary>
        /// His place in his side's shape (#96): 1 to the side's outfielders, numbered from the back;
        /// 0 for none (a keeper, practice, or a side too small for a shape). The footballer keeps it
        /// whoever controls him.
        /// </summary>
        public int Slot;

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

        /// <summary>This player's standing challenge (#84): ready, committed or recovering.</summary>
        public ChallengePhase Challenge;

        /// <summary>Steps left in <see cref="Challenge"/>, counting the step that ends it; 0 when ready.</summary>
        public int ChallengeTicks;

        /// <summary>The line he committed along: a unit vector, fixed as the challenge starts; zero when ready.</summary>
        public Vec2 ChallengeLine;
    }
}
