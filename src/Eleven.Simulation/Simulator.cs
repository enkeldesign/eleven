using System;
using System.Collections.Generic;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Maths;
using Eleven.Simulation.Randomness;

namespace Eleven.Simulation
{
    /// <summary>
    /// The fixed-step simulation. Holds one <see cref="SimConfig"/> and one <see cref="SimState"/>;
    /// <see cref="Step"/> is the only thing that changes the state.
    ///
    /// Order within a step:
    /// 1. resolve commands for this tick,
    /// 2. count down kick cooldowns, move players and update their headings,
    /// 3. apply kicks,
    /// 4. integrate the ball,
    /// 5. check the goal line,
    /// 6. advance the tick.
    /// </summary>
    public sealed class Simulator
    {
        private readonly SimState _state;
        private readonly CanonicalWriter _hashScratch = new CanonicalWriter();

        // Per-step scratch, cleared every step. Not state: nothing in it survives a step.
        private readonly List<Command> _accepted = new List<Command>();

        /// <param name="initial">Copied, so the caller's object is never mutated.</param>
        public Simulator(SimConfig config, SimState initial)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _state = Validate(initial ?? throw new ArgumentNullException(nameof(initial))).Clone();
        }

        public SimConfig Config { get; }

        public long Tick => _state.Tick;

        /// <summary>A copy of the current state.</summary>
        public SimState CopyState() => _state.Clone();

        /// <summary>Hash of the current state's canonical encoding.</summary>
        public ulong StateHash() => _state.ComputeHash(_hashScratch);

        /// <summary>Reads the live state without copying. Callers must not mutate it.</summary>
        public SimState PeekState() => _state;

        /// <summary>
        /// Runs one tick: applies <paramref name="commands"/> meant for <see cref="Tick"/> and
        /// advances to <see cref="Tick"/> + 1. Emitted events are appended to <paramref name="events"/>.
        /// </summary>
        public void Step(IReadOnlyList<Command> commands, List<SimEvent> events)
        {
            long tick = _state.Tick;
            ResolveCommands(tick, commands, events);

            double dt = Config.TickSeconds;
            for (int i = 0; i < _state.Players.Length; i++)
            {
                Command? command = FindAccepted(_state.Players[i].OwnerControllerId);
                StepPlayer(ref _state.Players[i], command, dt);
            }

            for (int i = 0; i < _state.Players.Length; i++)
            {
                Command? command = FindAccepted(_state.Players[i].OwnerControllerId);
                if (command.HasValue && command.Value.Kick)
                {
                    TryKick(ref _state.Players[i], tick, events);
                }
            }

            StepBall(tick, dt, events);
            CheckGoalLine(tick, events);

            _state.Tick = tick + 1;
        }

        private void ResolveCommands(long tick, IReadOnlyList<Command> commands, List<SimEvent> events)
        {
            _accepted.Clear();

            // Reject per command first, then drop every controller that has more than one valid
            // command this tick, then order by controller id. No step depends on input order.
            for (int i = 0; i < commands.Count; i++)
            {
                Command c = commands[i];
                CommandRejection rejection = Check(c, tick);
                if (rejection != CommandRejection.None)
                {
                    events.Add(new SimEvent(tick, SimEventKind.CommandRejected, c.ControllerId, rejection));
                    continue;
                }

                _accepted.Add(Normalise(c));
            }

            _accepted.Sort((a, b) => a.ControllerId.CompareTo(b.ControllerId));

            for (int i = 0; i < _accepted.Count;)
            {
                int end = i + 1;
                while (end < _accepted.Count && _accepted[end].ControllerId == _accepted[i].ControllerId) end++;

                if (end - i > 1)
                {
                    for (int k = i; k < end; k++)
                    {
                        events.Add(new SimEvent(tick, SimEventKind.CommandRejected, _accepted[i].ControllerId, CommandRejection.Duplicate));
                    }

                    _accepted.RemoveRange(i, end - i);
                }
                else
                {
                    i = end;
                }
            }
        }

        private CommandRejection Check(Command c, long tick)
        {
            if (c.Tick < tick) return CommandRejection.Stale;
            if (c.Tick > tick) return CommandRejection.Early;
            if (!SimMath.IsFinite(c.Move.X) || !SimMath.IsFinite(c.Move.Y)) return CommandRejection.Invalid;

            foreach (PlayerState player in _state.Players)
            {
                if (player.OwnerControllerId == c.ControllerId) return CommandRejection.None;
            }

            return CommandRejection.Unowned;
        }

        private Command Normalise(Command c)
        {
            if (c.Move.LengthSquared <= 1) return c;

            // Scale by the largest component first, so a huge finite move can't overflow when squared.
            double largest = Math.Max(Math.Abs(c.Move.X), Math.Abs(c.Move.Y));
            Vec2 scaled = c.Move * (1 / largest);
            return new Command(c.ControllerId, c.Tick, scaled * (1 / SimMath.Sqrt(scaled.LengthSquared)), c.Kick);
        }

        private Command? FindAccepted(int controllerId)
        {
            foreach (Command c in _accepted)
            {
                if (c.ControllerId == controllerId) return c;
            }

            return null;
        }

        private void StepPlayer(ref PlayerState player, Command? command, double dt)
        {
            if (player.KickCooldown > 0) player.KickCooldown--;

            Vec2 move = command.HasValue ? command.Value.Move : Vec2.Zero;
            Vec2 change = move * Config.DotMaxSpeed - player.Velocity;
            double maxChange = Config.DotAcceleration * dt;
            double changeSquared = change.LengthSquared;
            if (changeSquared > maxChange * maxChange)
            {
                change = change * (maxChange / SimMath.Sqrt(changeSquared));
            }

            player.Velocity += change;
            player.Position += player.Velocity * dt;

            // Below a millimetre per second the player counts as stopped and keeps its last heading.
            if (player.Velocity.LengthSquared > 1e-6)
            {
                player.Heading = SimMath.Atan2(player.Velocity.Y, player.Velocity.X);
            }
        }

        private void TryKick(ref PlayerState player, long tick, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;
            if (player.KickCooldown > 0 || ball.Position.Z > Config.KickMaxBallHeight) return;

            Vec2 offset = ball.Position.Horizontal - player.Position;
            if (offset.LengthSquared > Config.KickReach * Config.KickReach) return;

            var directionDraws = new StreamDraws(_state.Seed, tick, RandomStream.KickDirection, player.Id);
            var spinDraws = new StreamDraws(_state.Seed, tick, RandomStream.KickSpin, player.Id);

            double direction = player.Heading + (2 * directionDraws.NextUniform() - 1) * Config.KickDirectionError;
            double horizontal = Config.KickSpeed * SimMath.Cos(Config.KickLoft);
            ball.Velocity = new Vec3(
                horizontal * SimMath.Cos(direction),
                horizontal * SimMath.Sin(direction),
                Config.KickSpeed * SimMath.Sin(Config.KickLoft));
            ball.Spin = new Vec3(0, 0, Config.KickSideSpin * (0.5 + 0.5 * spinDraws.NextUniform()));

            player.KickCooldown = Config.KickCooldownTicks;
            events.Add(new SimEvent(tick, SimEventKind.Kicked, player.Id));
        }

        private void StepBall(long tick, double dt, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;
            double radius = Config.BallRadius;
            bool airborne = ball.Position.Z > radius || ball.Velocity.Z > 0;

            if (airborne)
            {
                // Spin curves the ball in flight only (#33): once rolling, it keeps its line.
                Vec3 magnus = Vec3.Cross(ball.Spin, ball.Velocity) * Config.MagnusCoefficient;
                ball.Velocity += (magnus + new Vec3(0, 0, -Config.Gravity)) * dt;
                ball.Position += ball.Velocity * dt;

                if (ball.Position.Z <= radius && ball.Velocity.Z < 0)
                {
                    double up = -ball.Velocity.Z * Config.BounceRestitution;
                    if (up < Config.SettleSpeed) up = 0;
                    double keep = Config.BounceTangentialRetention;
                    ball.Velocity = new Vec3(ball.Velocity.X * keep, ball.Velocity.Y * keep, up);
                    ball.Position = new Vec3(ball.Position.X, ball.Position.Y, radius);
                    events.Add(new SimEvent(tick, SimEventKind.Bounced, ball.Id));
                }

                ball.Spin *= Decay(Config.AirSpinDecay, dt);
                return;
            }

            // Rolling: friction against the direction of travel. Spin keeps decaying but no longer
            // curves the ball (SOL's research answer on #24: shaping is a short post-kick effect).
            Vec2 velocity = ball.Velocity.Horizontal;
            double speedSquared = velocity.LengthSquared;
            if (speedSquared > 0)
            {
                double speed = SimMath.Sqrt(speedSquared);
                double slowed = speed - Config.RollingFriction * Config.Gravity * dt;
                velocity = slowed <= Config.StopSpeed ? Vec2.Zero : velocity * (slowed / speed);
            }

            ball.Velocity = new Vec3(velocity.X, velocity.Y, 0);
            ball.Position = new Vec3(ball.Position.X + velocity.X * dt, ball.Position.Y + velocity.Y * dt, radius);
            ball.Spin *= Decay(Config.GroundSpinDecay, dt);
        }

        private void CheckGoalLine(long tick, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;
            bool beyond = ball.Position.X - Config.BallRadius > Config.GoalLineX;
            if (beyond && !ball.BeyondGoalLine)
            {
                events.Add(new SimEvent(tick, SimEventKind.GoalLineCrossed, ball.Id));
            }

            ball.BeyondGoalLine = beyond;
        }

        private static double Decay(double perSecond, double dt)
        {
            double factor = 1 - perSecond * dt;
            return factor > 0 ? factor : 0;
        }

        private static SimState Validate(SimState state)
        {
            // Encoding refuses non-finite values, so this checks every authoritative field.
            state.Encode(new CanonicalWriter());

            for (int i = 1; i < state.Players.Length; i++)
            {
                if (state.Players[i].Id <= state.Players[i - 1].Id)
                {
                    throw new ArgumentException("Players must be in strictly ascending id order.", nameof(state));
                }
            }

            return state;
        }
    }
}
