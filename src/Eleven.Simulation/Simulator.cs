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
    /// 3. aftertouch: each open window shapes the ball by its owner's input, then counts down,
    /// 4. kicks: accept intents into wind-ups, count wind-ups down, and connect or miss those that end,
    /// 5. integrate the ball,
    /// 6. check the goal line,
    /// 7. advance the tick.
    ///
    /// So a window opened by a contact first acts on the next step, and a wind-up of W steps
    /// started on step N connects on step N + W.
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
            _state = Validate(initial ?? throw new ArgumentNullException(nameof(initial)), config).Clone();
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
                StepAftertouch(ref _state.Players[i], FindAccepted(_state.Players[i].OwnerControllerId));
            }

            for (int i = 0; i < _state.Players.Length; i++)
            {
                StepKick(i, FindAccepted(_state.Players[i].OwnerControllerId), tick, events);
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
            if (!IsFinite(c.Move) || !IsFinite(c.Aftertouch)) return CommandRejection.Invalid;
            if (c.Kick.HasValue)
            {
                KickIntent kick = c.Kick.Value;
                if (!IsFinite(kick.Direction) || !SimMath.IsFinite(kick.Strength) || !SimMath.IsFinite(kick.Loft)) return CommandRejection.Invalid;
            }

            foreach (PlayerState player in _state.Players)
            {
                if (player.OwnerControllerId == c.ControllerId) return CommandRejection.None;
            }

            return CommandRejection.Unowned;
        }

        private static bool IsFinite(Vec2 v) => SimMath.IsFinite(v.X) && SimMath.IsFinite(v.Y);

        private static Command Normalise(Command c)
        {
            KickIntent? kick = null;
            if (c.Kick.HasValue)
            {
                KickIntent k = c.Kick.Value;
                kick = new KickIntent(k.Direction, Clamp01(k.Strength), Clamp01(k.Loft));
            }

            return new Command(c.ControllerId, c.Tick, AtMostUnit(c.Move), kick, AtMostUnit(c.Aftertouch));
        }

        private static Vec2 AtMostUnit(Vec2 v)
        {
            if (v.LengthSquared <= 1) return v;

            // Scale by the largest component first, so a huge finite vector can't overflow when squared.
            double largest = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
            Vec2 scaled = v * (1 / largest);
            return scaled * (1 / SimMath.Sqrt(scaled.LengthSquared));
        }

        // Plain comparisons rather than Math.Min and Max, whose treatment of −0 runtimes needn't share.
        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

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

            // The stick asks for a velocity; the player's own capabilities decide how fast they
            // run and how quickly they get there, whether speeding up, braking or turning (#36).
            Vec2 move = command.HasValue ? command.Value.Move : Vec2.Zero;
            Vec2 change = move * player.Capabilities.MaxSpeed - player.Velocity;
            double maxChange = player.Capabilities.Acceleration * dt;
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

        // Aftertouch (#37): while the window is open, the owner's input shapes the ball they kicked,
        // measured against the kick's intended line. The window gates input only; spin the ball
        // already carries keeps acting after it closes.
        private void StepAftertouch(ref PlayerState player, Command? command)
        {
            if (player.AftertouchTicks == 0) return;

            Vec2 input = command.HasValue ? command.Value.Aftertouch : Vec2.Zero;
            if (input.X != 0 || input.Y != 0) Shape(player.AftertouchTicks, player.AftertouchDirection, input);
            player.AftertouchTicks--;
        }

        // The window's steps weigh n / (1 + 2 + … + A) on the step with n left: they decay linearly
        // and sum to 1, so the window's length sets when input acts, not how much it can do.
        private void Shape(int stepsLeft, double line, Vec2 input)
        {
            ref BallState ball = ref _state.Ball;
            int window = Config.AftertouchTicks;
            double weight = 2.0 * stepsLeft / ((double)window * (window + 1));

            double cos = SimMath.Cos(line), sin = SimMath.Sin(line);
            double forward = input.X * cos + input.Y * sin;
            double side = input.Y * cos - input.X * sin; // + is left of the line

            Vec3 v = ball.Velocity;
            double turn = weight * side * Config.AftertouchBend;
            if (turn != 0)
            {
                // Direct shaping, SWOS-style: the horizontal velocity turns, its speed unchanged.
                double c = SimMath.Cos(turn), s = SimMath.Sin(turn);
                v = new Vec3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
            }

            ball.Velocity = new Vec3(v.X, v.Y, v.Z - weight * forward * Config.AftertouchLift);
            ball.Spin = new Vec3(ball.Spin.X, ball.Spin.Y, ball.Spin.Z + weight * side * Config.KickSideSpin);
        }

        // The manual kick (#37): an intent accepted while the player is ready starts a wind-up that
        // holds it; the step that ends the wind-up connects, or misses if the ball is out of reach.
        private void StepKick(int index, Command? command, long tick, List<SimEvent> events)
        {
            ref PlayerState player = ref _state.Players[index];
            if (command.HasValue && command.Value.Kick.HasValue && player.WindUpTicks == 0 && player.KickCooldown == 0)
            {
                KickIntent intent = command.Value.Kick.Value;
                Vec2 d = intent.Direction;

                // One more than the wind-up, because this step counts it down too.
                player.WindUpTicks = Config.KickWindUpTicks + 1;
                player.WindUpDirection = d.X == 0 && d.Y == 0 ? player.Heading : SimMath.Atan2(d.Y, d.X);
                player.WindUpStrength = intent.Strength;
                player.WindUpLoft = intent.Loft;
            }

            if (player.WindUpTicks == 0) return;
            player.WindUpTicks--;
            if (player.WindUpTicks == 0) Connect(index, tick, events);
        }

        private void Connect(int index, long tick, List<SimEvent> events)
        {
            ref PlayerState player = ref _state.Players[index];
            ref BallState ball = ref _state.Ball;
            double line = player.WindUpDirection, strength = player.WindUpStrength, loft = player.WindUpLoft;
            player.WindUpDirection = player.WindUpStrength = player.WindUpLoft = 0;

            Vec2 offset = ball.Position.Horizontal - player.Position;
            if (ball.Position.Z > Config.KickMaxBallHeight || offset.LengthSquared > Config.KickReach * Config.KickReach)
            {
                events.Add(new SimEvent(tick, SimEventKind.KickMissed, player.Id));
                return;
            }

            // The intended line plus the kicker's own bounded execution error: never retargeted.
            var draws = new StreamDraws(_state.Seed, tick, RandomStream.KickDirection, player.Id);
            double direction = line + (2 * draws.NextUniform() - 1) * player.Capabilities.KickError;
            double speed = strength * player.Capabilities.KickPower * Config.KickSpeed;
            double elevation = loft * Config.KickLoft;
            double horizontal = speed * SimMath.Cos(elevation);
            ball.Velocity = new Vec3(horizontal * SimMath.Cos(direction), horizontal * SimMath.Sin(direction), speed * SimMath.Sin(elevation));
            ball.Spin = Vec3.Zero;

            // The ball has a new kicker, so only they can shape it now.
            for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].AftertouchTicks = 0;
            player.AftertouchTicks = Config.AftertouchTicks;
            player.AftertouchDirection = line;
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

        private static SimState Validate(SimState state, SimConfig config)
        {
            // Encoding refuses non-finite values, so this checks every authoritative field.
            state.Encode(new CanonicalWriter());

            for (int i = 0; i < state.Players.Length; i++)
            {
                PlayerState p = state.Players[i];
                if (i > 0 && p.Id <= state.Players[i - 1].Id)
                {
                    throw new ArgumentException("Players must be in strictly ascending id order.", nameof(state));
                }

                string owner = $"Player {p.Id}";
                p.Capabilities.CheckRanges(owner);

                // Kick state this config could have produced (#37). NaN fails the encoding above.
                InRange(owner, nameof(p.KickCooldown), p.KickCooldown, config.KickCooldownTicks);
                InRange(owner, nameof(p.WindUpTicks), p.WindUpTicks, config.KickWindUpTicks);
                InRange(owner, nameof(p.AftertouchTicks), p.AftertouchTicks, config.AftertouchTicks);
                if (p.WindUpStrength < 0 || p.WindUpStrength > 1 || p.WindUpLoft < 0 || p.WindUpLoft > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(state), $"{owner}: wind-up strength and loft must be within [0, 1].");
                }
            }

            return state;
        }

        private static void InRange(string owner, string field, int value, int max)
        {
            if (value < 0 || value > max)
            {
                throw new ArgumentOutOfRangeException(field, value, $"{owner}: {field} must be within [0, {max}] under this config.");
            }
        }
    }
}
