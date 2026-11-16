using System;

namespace Eleven.Simulation
{
    /// <summary>
    /// What a keeper perceives (#69, INV-9): his own body, phase and capabilities, and the ball as
    /// seen now. No command, no other controller's input, no wind-up intent, no seed and no
    /// reference to state: his brain decides on this alone.
    ///
    /// Positions and directions are in his own goal's frame (#82): the pitch as seen with the goal
    /// he guards on +x. For the keeper of the −x goal that is the world turned half a turn about
    /// the centre spot. So one brain keeps either goal, and never needs to know which.
    /// </summary>
    public readonly struct KeeperObservation
    {
        public KeeperObservation(long tick, Vec2 position, Vec2 velocity, double heading, KeeperPhase phase, int phaseTicks,
            KeeperCapabilities capabilities, double runSpeed, Vec3 ballPosition, Vec3 ballVelocity, Vec3 ballSpin, bool ballHeld, bool holding,
            int heldTicks = 0, bool inMatch = false)
        {
            Tick = tick;
            Position = position;
            Velocity = velocity;
            Heading = heading;
            Phase = phase;
            PhaseTicks = phaseTicks;
            Capabilities = capabilities;
            RunSpeed = runSpeed;
            BallPosition = ballPosition;
            BallVelocity = ballVelocity;
            BallSpin = ballSpin;
            BallHeld = ballHeld;
            Holding = holding;
            HeldTicks = heldTicks;
            InMatch = inMatch;
        }

        public long Tick { get; }

        /// <summary>m, his position on the pitch.</summary>
        public Vec2 Position { get; }

        /// <summary>m/s.</summary>
        public Vec2 Velocity { get; }

        /// <summary>rad.</summary>
        public double Heading { get; }

        public KeeperPhase Phase { get; }

        /// <summary>Steps left in his phase.</summary>
        public int PhaseTicks { get; }

        public KeeperCapabilities Capabilities { get; }

        /// <summary>m/s, his top speed on his feet.</summary>
        public double RunSpeed { get; }

        public Vec3 BallPosition { get; }

        public Vec3 BallVelocity { get; }

        public Vec3 BallSpin { get; }

        /// <summary>Whether anyone holds the ball.</summary>
        public bool BallHeld { get; }

        /// <summary>Whether he holds it.</summary>
        public bool Holding { get; }

        /// <summary>Steps he has held it for (#85).</summary>
        public int HeldTicks { get; }

        /// <summary>Whether this is a match, with a referee (#85): then a held ball is his to distribute.</summary>
        public bool InMatch { get; }
    }

    /// <summary>Another footballer as a CPU outfielder sees him (#83).</summary>
    public readonly struct SeenPlayer
    {
        public SeenPlayer(Vec2 position, Vec2 velocity, bool teammate, bool keeper, double fatigue = 0)
        {
            Fatigue = fatigue;
            Position = position;
            Velocity = velocity;
            Teammate = teammate;
            Keeper = keeper;
        }

        /// <summary>m, where he seems to be: off by the observer's perception error.</summary>
        public Vec2 Position { get; }

        public Vec2 Velocity { get; }

        public bool Teammate { get; }

        public bool Keeper { get; }

        /// <summary>0–1, a teammate's fatigue as he reads it from his teammate (#100); 0 for an opponent, whose he can't.</summary>
        public double Fatigue { get; }
    }

    /// <summary>Who holds the ball, as a CPU outfielder sees it (#83).</summary>
    public enum SeenHolder
    {
        None = 0,
        OwnKeeper = 1,
        OpponentKeeper = 2,
    }

    /// <summary>
    /// What a CPU outfielder perceives (#83, INV-9): his own body, the ball and the footballers in
    /// his sight, as seen now. No command, no other controller's input or plan, no wind-up intent,
    /// no seed and no reference to state: his brain decides on this alone.
    ///
    /// Positions and directions are in his own side's frame: the pitch as seen attacking +x. For a
    /// side attacking −x that is the world turned half a turn about the centre spot, so one brain
    /// plays either way. In practice, with no sides, it is the world as it is.
    ///
    /// The ball and the others are where he places them, not where they are: each is off by up to
    /// his skill's perception error times its distance. His own body he knows exactly.
    /// </summary>
    public sealed class OutfieldObservation
    {
        private readonly SeenPlayer[] _others;

        internal OutfieldObservation(long tick, Vec2 position, Vec2 velocity, double heading, double maxSpeed, bool kicking,
            Vec3 ballPosition, Vec3 ballVelocity, Vec3 ballSpin, SeenHolder holder, bool bothGoals, SeenPlayer[] others, double reactionDelay = 0,
            int slot = 0, int sideSize = 0, double fatigue = 0)
        {
            Fatigue = fatigue;
            ReactionDelay = reactionDelay;
            Slot = slot;
            SideSize = sideSize;
            Tick = tick;
            Position = position;
            Velocity = velocity;
            Heading = heading;
            MaxSpeed = maxSpeed;
            Kicking = kicking;
            BallPosition = ballPosition;
            BallVelocity = ballVelocity;
            BallSpin = ballSpin;
            Holder = holder;
            BothGoals = bothGoals;
            _others = others;
        }

        public long Tick { get; }

        public Vec2 Position { get; }

        public Vec2 Velocity { get; }

        public double Heading { get; }

        /// <summary>m/s, his own top speed.</summary>
        public double MaxSpeed { get; }

        /// <summary>
        /// s. His own reaction delay (#94): how long after this observation a plan he makes on it
        /// takes effect. He knows his own body; the simulation still times the plan (INV-12).
        /// </summary>
        public double ReactionDelay { get; }

        /// <summary>His place in his side's shape (#96), from the back; 0 for none. His own, as his reaction delay is.</summary>
        public int Slot { get; }

        /// <summary>How many places his side's shape has (#96): its outfielders who have one.</summary>
        public int SideSize { get; }

        /// <summary>0–1, his own fatigue (#100). Others' he reads only as the speed he sees.</summary>
        public double Fatigue { get; }

        /// <summary>Whether he is winding up a kick, or can't start one yet.</summary>
        public bool Kicking { get; }

        public Vec3 BallPosition { get; }

        public Vec3 BallVelocity { get; }

        public Vec3 BallSpin { get; }

        public SeenHolder Holder { get; }

        /// <summary>Whether there is a goal at each end: a match. Practice has the +x goal only.</summary>
        public bool BothGoals { get; }

        /// <summary>The other footballers within his sight, in ascending player id order.</summary>
        public System.Collections.Generic.IReadOnlyList<SeenPlayer> Others => _others;
    }

    /// <summary>A keeper's brain's decision (#69): where he moves on his feet, and the point he dives for, if he does.</summary>
    public readonly struct KeeperDecision
    {
        public KeeperDecision(Vec2 move, Vec3? dive = null, KickIntent? kick = null)
        {
            Move = move;
            Dive = dive;
            Kick = kick;
        }

        /// <summary>A kick (#85): from his hands, the ball he holds, distributed. An ordinary kick intent.</summary>
        public KickIntent? Kick { get; }

        /// <summary>Length at most 1, as a stick's: a fraction of his top speed.</summary>
        public Vec2 Move { get; }

        public Vec3? Dive { get; }
    }

    /// <summary>The perception boundary (INV-9): the only way an AI sees the simulation.</summary>
    public static class Perception
    {
        /// <summary>What the keeper at <paramref name="keeper"/> in <see cref="SimState.Keepers"/> perceives in <paramref name="state"/>.</summary>
        public static KeeperObservation OfKeeper(SimState state, int keeper)
        {
            KeeperState k = state.Keepers[keeper];
            PlayerState body = Array.Find(state.Players, p => p.Id == k.PlayerId);
            BallState ball = state.Ball;
            int held = k.HoldTicks;
            if (GuardedEnd(state, keeper) > 0)
            {
                return new KeeperObservation(state.Tick, body.Position, body.Velocity, body.Heading, k.Phase, k.PhaseTicks, k.Capabilities,
                    body.Capabilities.MaxSpeed, ball.Position, ball.Velocity, ball.Spin, ball.HeldBy != 0, ball.HeldBy == k.PlayerId, held, state.Restart.On);
            }

            // The −x goal's keeper: the world turned half a turn about the centre spot.
            return new KeeperObservation(state.Tick, Turn(body.Position), Turn(body.Velocity), body.Heading > 0 ? body.Heading - Math.PI : body.Heading + Math.PI,
                k.Phase, k.PhaseTicks, k.Capabilities, body.Capabilities.MaxSpeed, Turn(ball.Position), Turn(ball.Velocity), Turn(ball.Spin),
                ball.HeldBy != 0, ball.HeldBy == k.PlayerId, held, state.Restart.On);
        }

        /// <summary>
        /// The end the keeper at <paramref name="keeper"/> guards (#82): the one his side doesn't
        /// attack, +1 for the +x goal and −1 for the −x goal. Practice, with no sides, has the +x goal only.
        /// </summary>
        public static int GuardedEnd(SimState state, int keeper)
        {
            int team = Array.Find(state.Players, p => p.Id == state.Keepers[keeper].PlayerId).TeamId;
            foreach (TeamState side in state.Teams)
            {
                if (side.Id == team) return -side.AttackSign;
            }

            return 1;
        }

        /// <summary>
        /// What the CPU outfielder at <paramref name="cpu"/> in <see cref="SimState.Cpus"/> perceives
        /// in <paramref name="state"/> (#83). His errors are drawn from the counter-based stream for
        /// this tick and this footballer (INV-6), so the same state gives the same observation.
        /// </summary>
        public static OutfieldObservation OfOutfielder(SimState state, int cpu)
        {
            CpuState c = state.Cpus[cpu];
            PlayerState body = Array.Find(state.Players, p => p.Id == c.PlayerId);
            bool turned = AttackSign(state, body.TeamId) < 0;
            var draws = new Randomness.StreamDraws(state.Seed, state.Tick, Randomness.RandomStream.Perception, body.Id);

            Vec2 Seen(Vec2 at)
            {
                // Off by up to the error times the distance, each way, in his own frame: so he
                // misplaces things alike whichever way his side attacks.
                Vec2 away = at - body.Position;
                double reach = Maths.SimMath.Sqrt(away.LengthSquared) * c.Skill.PerceptionError * (turned ? -1 : 1);
                double dx = (2 * draws.NextUniform() - 1) * reach, dy = (2 * draws.NextUniform() - 1) * reach;
                return new Vec2(at.X + dx, at.Y + dy);
            }

            BallState ball = state.Ball;
            Vec2 ballAt = Seen(ball.Position.Horizontal);
            var ballSeen = new Vec3(ballAt.X, ballAt.Y, ball.Position.Z);

            var others = new System.Collections.Generic.List<SeenPlayer>();
            SeenHolder holder = SeenHolder.None;
            foreach (PlayerState other in state.Players)
            {
                if (other.Id == body.Id) continue;
                bool teammate = other.TeamId == body.TeamId;
                bool keeper = Array.FindIndex(state.Keepers, k => k.PlayerId == other.Id) >= 0;
                if (ball.HeldBy == other.Id) holder = teammate ? SeenHolder.OwnKeeper : SeenHolder.OpponentKeeper;

                // Always two draws for each footballer, seen or not, so no one's error depends on who else is in sight.
                Vec2 at = Seen(other.Position);
                if (other.Off) continue; // off the pitch (#108, #109)
                if ((other.Position - body.Position).LengthSquared > c.Skill.VisionRange * c.Skill.VisionRange) continue;
                others.Add(new SeenPlayer(turned ? Turn(at) : at, turned ? Turn(other.Velocity) : other.Velocity, teammate, keeper, teammate ? other.Fatigue : 0));
            }

            bool kicking = body.WindUpTicks > 0 || body.KickCooldown > 0;
            int side = 0;
            foreach (PlayerState p in state.Players) if (p.TeamId == body.TeamId && p.Slot > 0) side++;
            return turned
                ? new OutfieldObservation(state.Tick, Turn(body.Position), Turn(body.Velocity), body.Heading > 0 ? body.Heading - Math.PI : body.Heading + Math.PI,
                    body.Capabilities.MaxSpeed, kicking, Turn(ballSeen), Turn(ball.Velocity), Turn(ball.Spin), holder, state.Teams.Length == 2, others.ToArray(), c.Skill.ReactionDelay, body.Slot, side, body.Fatigue)
                : new OutfieldObservation(state.Tick, body.Position, body.Velocity, body.Heading, body.Capabilities.MaxSpeed, kicking,
                    ballSeen, ball.Velocity, ball.Spin, holder, state.Teams.Length == 2, others.ToArray(), c.Skill.ReactionDelay, body.Slot, side, body.Fatigue);
        }

        /// <summary>The sign of world x a side attacks toward (#82); +1 with no sides.</summary>
        public static int AttackSign(SimState state, int team)
        {
            foreach (TeamState side in state.Teams)
            {
                if (side.Id == team) return side.AttackSign;
            }

            return 1;
        }

        /// <summary>A CPU outfielder's plan, made in his side's frame, as it is in the world's.</summary>
        public static CpuPlan InWorld(CpuPlan plan, int attackSign) =>
            attackSign > 0 ? plan
            : new CpuPlan(plan.Mode, Turn(plan.Move), plan.Kick.HasValue ? new KickIntent(Turn(plan.Kick.Value.Direction), plan.Kick.Value.Strength, plan.Kick.Value.Loft) : (KickIntent?)null, plan.QuickPass);

        /// <summary>A keeper's decision, made in his own goal's frame, as a command in the world's.</summary>
        public static KeeperDecision InWorld(KeeperDecision decision, int guardedEnd) =>
            guardedEnd > 0 ? decision
            : new KeeperDecision(Turn(decision.Move), decision.Dive.HasValue ? Turn(decision.Dive.Value) : (Vec3?)null,
                decision.Kick.HasValue ? new KickIntent(Turn(decision.Kick.Value.Direction), decision.Kick.Value.Strength, decision.Kick.Value.Loft) : (KickIntent?)null);

        // Half a turn about the centre spot. Spin, an axial vector, turns as any vector does.
        private static Vec2 Turn(Vec2 v) => new Vec2(-v.X, -v.Y);

        private static Vec3 Turn(Vec3 v) => new Vec3(-v.X, -v.Y, v.Z);
    }

    /// <summary>
    /// Runs each keeper's brain (#69): before a step, it appends each keeper's command for that
    /// step, his brain's decision on what he perceives, owned by his AI controller. The commands are
    /// recorded like any other, so a replay never runs a brain.
    /// </summary>
    public static class KeeperControllers
    {
        public static void Append(Simulator simulator, System.Collections.Generic.List<Command> commands)
        {
            SimState state = simulator.PeekState();
            for (int i = 0; i < state.Keepers.Length; i++)
            {
                KeeperObservation seen = Perception.OfKeeper(state, i);
                KeeperDecision decision = Perception.InWorld(AI.KeeperBrain.Decide(seen, simulator.Config), Perception.GuardedEnd(state, i));
                int controller = Array.Find(state.Players, p => p.Id == state.Keepers[i].PlayerId).OwnerControllerId;
                commands.Add(new Command(controller, state.Tick, decision.Move, decision.Kick, dive: decision.Dive));
            }
        }
    }

    /// <summary>
    /// Runs each CPU outfielder's brain (#83). Before a step it appends each one's command for that
    /// step: the stick of the plan he is acting on, that plan's kick or quick pass on the tick it
    /// takes effect, and, when the simulation lets him decide, his brain's new plan on what he
    /// perceives now. Owned by his AI controller and recorded like any other command, so a replay
    /// never runs a brain.
    /// </summary>
    public static class CpuControllers
    {
        public static void Append(Simulator simulator, System.Collections.Generic.List<Command> commands)
        {
            SimState state = simulator.PeekState();
            long tick = state.Tick;
            for (int i = 0; i < state.Cpus.Length; i++)
            {
                CpuState cpu = state.Cpus[i];
                PlayerState body = Array.Find(state.Players, p => p.Id == cpu.PlayerId);
                CpuPlanState acting = cpu.InEffect(tick);
                bool now = acting.Mode != CpuMode.None && acting.ActsTick == tick;

                CpuPlan? plan = null;
                if (cpu.MayDecide(tick))
                {
                    OutfieldObservation seen = Perception.OfOutfielder(state, i);
                    plan = Perception.InWorld(AI.OutfieldBrain.Decide(seen, simulator.Config), Perception.AttackSign(state, body.TeamId));
                }

                commands.Add(new Command(body.OwnerControllerId, tick, acting.Move, now ? acting.Kick : null, quickPass: now && acting.QuickPass, plan: plan));
            }
        }
    }
}
