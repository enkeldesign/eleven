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
    /// 4. kicks: accept intents (a manual kick, or a quick pass the footballer resolves) into wind-ups,
    ///    count wind-ups down, and connect or miss those that end,
    /// 5. ball control: touches under FREE, or ATTACHED's release, take and correction, and the
    ///    turn with the ball under either (#66),
    /// 6. integrate the ball, then resolve the goal's frame along the step's path (#68),
    /// 7. judge the goal line, on where the ball ends and where its centre crossed the line,
    /// 8. advance the tick.
    ///
    /// So a window opened by a contact first acts on the next step, and a wind-up of W steps
    /// started on step N connects on step N + W.
    /// </summary>
    public sealed partial class Simulator
    {
        private readonly SimState _state;
        private readonly CanonicalWriter _hashScratch = new CanonicalWriter();

        // Per-step scratch, cleared every step. Not state: nothing in it survives a step.
        private readonly List<Command> _accepted = new List<Command>();

        // Whether a kick connected on this step: then no one touches the ball on it (#59).
        private bool _kickConnected;

        // Whether a challenge won the ball this step (#84): scratch, like _kickConnected.
        private bool _challengeWon;

        // Where each keeper's body started this step, so the ball's contact with him is swept in
        // his own frame (#69, Codex on #71). Rebuilt every step.
        private readonly List<Vec2> _keeperFrom = new List<Vec2>();

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
            _kickConnected = false;
            _challengeWon = false;
            _crossedEnd = 0;
            _foulBy = _foulOn = -1;
            _crossedMouth = false;
            ResolveCommands(tick, commands, events);

            // Substitutions (#110) first, on the state their commands were checked on: nothing
            // later in the step, a restart taken or the end of a break, can undo what was allowed.
            StepSubstitutions(tick, events);
            StepCpus(tick);

            double dt = Config.TickSeconds;
            _keeperFrom.Clear();
            foreach (KeeperState k in _state.Keepers) _keeperFrom.Add(_state.Players[IndexOfPlayer(k.PlayerId)].Position);
            for (int i = 0; i < _state.Players.Length; i++)
            {
                Command? command = FindAccepted(_state.Players[i].OwnerControllerId);
                StepPlayer(ref _state.Players[i], command, dt, KeeperOf(_state.Players[i].Id));
            }

            for (int i = 0; i < _state.Players.Length; i++)
            {
                StepAftertouch(ref _state.Players[i], FindAccepted(_state.Players[i].OwnerControllerId));
            }

            for (int i = 0; i < _state.Players.Length; i++)
            {
                StepKick(i, FindAccepted(_state.Players[i].OwnerControllerId), tick, events);
            }

            // Standing challenges (#84), after kicks: a kick that connects first has taken the ball
            // away, and a challenge that wins it keeps it from every touch this step.
            StepChallenges(tick, events);

            // After kicks, so a kick's own step and its cooldown keep the ball free (#59): no one
            // touches on the step a kick connects, whatever the cooldown, and the kicker not through it.
            StepControl(tick, events);

            // Keepers' phases (#69), after kicks, so a kick sets them reacting on its own step.
            StepKeepers(tick, events);

            Vec3 start = _state.Ball.Position;
            StepBall(tick, dt, start, events);

            // Offside (#107), on this step's touches, before the goal line: an offence stops play
            // before a goal from it can count.
            StepOffside(tick, start, events);
            CheckGoalLine(tick, start, events);

            // Out of play, goals and restarts (#85), on where the ball ended the step, then the
            // match's clock and halves (#86), then who controls whom (#93).
            StepRestart(tick, events);
            StepMatch(tick, events);
            StepSwitches(tick, events);

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
            if (c.Dive.HasValue && !(SimMath.IsFinite(c.Dive.Value.X) && SimMath.IsFinite(c.Dive.Value.Y) && SimMath.IsFinite(c.Dive.Value.Z))) return CommandRejection.Invalid;
            if (c.Kick.HasValue)
            {
                KickIntent kick = c.Kick.Value;
                if (!IsFinite(kick.Direction) || !SimMath.IsFinite(kick.Strength) || !SimMath.IsFinite(kick.Loft)) return CommandRejection.Invalid;
            }

            if (c.Plan.HasValue)
            {
                CpuPlan plan = c.Plan.Value;
                if (!IsFinite(plan.Move)) return CommandRejection.Invalid;
                if (plan.Kick.HasValue && !(IsFinite(plan.Kick.Value.Direction) && SimMath.IsFinite(plan.Kick.Value.Strength) && SimMath.IsFinite(plan.Kick.Value.Loft))) return CommandRejection.Invalid;
            }

            foreach (PlayerState player in _state.Players)
            {
                if (player.OwnerControllerId != c.ControllerId) continue;
                if (c.SwitchTo != 0 && !MaySwitch(player, c.SwitchTo)) return CommandRejection.SwitchRefused;
                if (c.SubOff != 0 || c.SubOn != 0)
                {
                    CommandRejection why = MaySubstitute(player.TeamId, c.SubOff, c.SubOn);
                    if (why != CommandRejection.None) return why;
                }

                if (!c.Plan.HasValue) return CommandRejection.None;

                // A plan (#83) only from a CPU outfielder's controller, and only when he may decide.
                int cpu = CpuOf(player.Id);
                return cpu >= 0 && _state.Cpus[cpu].MayDecide(tick) ? CommandRejection.None : CommandRejection.PlanRefused;
            }

            return CommandRejection.Unowned;
        }

        private int CpuOf(int playerId)
        {
            for (int i = 0; i < _state.Cpus.Length; i++)
            {
                if (_state.Cpus[i].PlayerId == playerId) return i;
            }

            return -1;
        }

        /// <summary>A CPU outfielder's decision interval in whole steps, at least one (#83).</summary>
        public static int DecisionTicksFor(CpuSkill skill, SimConfig config) => Math.Max(1, (int)Math.Round(skill.DecisionInterval * config.TickRate));

        /// <summary>
        /// A CPU outfielder's reaction delay in whole steps, at least one (#83): a plan decided on
        /// one tick's observation can't act before the next.
        /// </summary>
        public static int ReactionTicksFor(CpuSkill skill, SimConfig config) => Math.Max(1, (int)Math.Round(skill.ReactionDelay * config.TickRate));

        // The CPU outfielders' timers (#83, INV-12), before anyone moves. A waiting plan whose tick
        // has come becomes the plan in effect. A plan accepted this tick waits out the reaction
        // delay, and sets when he may decide next. The plan moves no one here: his controller
        // sends its stick, kick and quick pass as ordinary commands.
        private void StepCpus(long tick)
        {
            for (int i = 0; i < _state.Cpus.Length; i++)
            {
                ref CpuState cpu = ref _state.Cpus[i];
                if (cpu.HasNext && tick >= cpu.Next.ActsTick)
                {
                    cpu.Plan = cpu.Next;
                    cpu.HasNext = false;
                    cpu.Next = default;
                }

                Command? command = FindAccepted(_state.Players[IndexOfPlayer(cpu.PlayerId)].OwnerControllerId);
                if (!command.HasValue || !command.Value.Plan.HasValue) continue;

                CpuPlan plan = command.Value.Plan.Value;
                cpu.Next = new CpuPlanState
                {
                    Mode = plan.Mode,
                    Move = plan.Move,
                    HasKick = plan.Kick.HasValue,
                    KickDirection = plan.Kick.HasValue ? plan.Kick.Value.Direction : Vec2.Zero,
                    KickStrength = plan.Kick.HasValue ? plan.Kick.Value.Strength : 0,
                    KickLoft = plan.Kick.HasValue ? plan.Kick.Value.Loft : 0,
                    QuickPass = plan.QuickPass,
                    ObservedTick = tick,
                    ActsTick = tick + ReactionTicksFor(cpu.Skill, Config),
                };
                cpu.HasNext = true;
                cpu.NextDecisionTick = tick + DecisionTicksFor(cpu.Skill, Config);
            }
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

            CpuPlan? plan = null;
            if (c.Plan.HasValue)
            {
                CpuPlan p = c.Plan.Value;
                KickIntent? planned = p.Kick.HasValue ? new KickIntent(p.Kick.Value.Direction, Clamp01(p.Kick.Value.Strength), Clamp01(p.Kick.Value.Loft)) : (KickIntent?)null;
                plan = new CpuPlan(p.Mode, AtMostUnit(p.Move), planned, p.QuickPass);
            }

            return new Command(c.ControllerId, c.Tick, AtMostUnit(c.Move), kick, AtMostUnit(c.Aftertouch), c.QuickPass, c.Dive, plan, c.SwitchTo, c.SubOff, c.SubOn);
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

        // The stick of a player's accepted command this step; none is neutral.
        private Vec2 MoveOf(in PlayerState player)
        {
            Command? command = FindAccepted(player.OwnerControllerId);
            return command.HasValue ? command.Value.Move : Vec2.Zero;
        }

        private Command? FindAccepted(int controllerId)
        {
            foreach (Command c in _accepted)
            {
                if (c.ControllerId == controllerId) return c;
            }

            return null;
        }

        private void StepPlayer(ref PlayerState player, Command? command, double dt, int keeper)
        {
            if (player.KickCooldown > 0) player.KickCooldown--;
            if (player.TouchCooldown > 0) player.TouchCooldown--;
            if (player.CutCooldown > 0) player.CutCooldown--;

            // The stick asks for a velocity; the player's own capabilities decide how fast they
            // run and how quickly they get there, whether speeding up, braking or turning (#36).
            // A keeper's body follows his phase (#69): reacting he sets, braking whatever is asked;
            // diving he flies at his dive's velocity; recovering he is on the ground.
            // In a challenge (#84) he keeps his line through the commit, and brakes through the recovery.
            Vec2 move = command.HasValue ? command.Value.Move : Vec2.Zero;
            if (Held(player))
            {
                // Set up for a restart (#85): only its taker moves.
                player.Velocity = Vec2.Zero;
                return;
            }

            if (player.Challenge == ChallengePhase.Slide)
            {
                // A slide (#98) carries him along its line, slowing from slideSpeed to half of it as it ends.
                player.Velocity = player.ChallengeLine * (Config.SlideSpeed * (0.5 + 0.5 * player.ChallengeTicks / Config.SlideTicks));
                player.Position += player.Velocity * dt;
                return;
            }

            if (player.Challenge == ChallengePhase.Grounded)
            {
                player.Velocity = Vec2.Zero;
                return;
            }

            if (player.Challenge == ChallengePhase.Commit) move = player.ChallengeLine;
            else if (player.Challenge == ChallengePhase.Recover) move = Vec2.Zero;
            KeeperPhase phase = keeper >= 0 ? _state.Keepers[keeper].Phase : KeeperPhase.Position;
            if (phase == KeeperPhase.Dive) player.Velocity = _state.Keepers[keeper].DiveVelocity;
            else if (phase == KeeperPhase.Recover) player.Velocity = Vec2.Zero;
            else player.Velocity = Accelerate(player.Velocity, phase == KeeperPhase.React ? Vec2.Zero : move, Body(player), dt);
            player.Position += player.Velocity * dt;
            Tire(ref player, dt);

            // Below a millimetre per second the player counts as stopped and keeps its last heading.
            if (player.Velocity.LengthSquared > 1e-6)
            {
                player.Heading = SimMath.Atan2(player.Velocity.Y, player.Velocity.X);
            }
        }

        /// <summary>
        /// m. Under FREE, a dribbler still has the ball while it is this close (#96): his own touches,
        /// a poor dribbler's heavier ones included, never leave him running free. A technical constant.
        /// </summary>
        public const double DribbleReach = 4.0;

        /// <summary>The most places a side's shape has (#96): ten outfielders.</summary>
        public const int MaxSlot = 10;

        /// <summary>m. A shielded ball this close to its place behind the carrier, and at rest with him, needs no touch (#97).</summary>
        public const double ShieldSettled = 0.15;

        /// <summary>
        /// Fatigue (#100), in a match world only: above fatigueFrom of his fresh top speed he tires, faster the nearer his top
        /// speed; below it he rests, faster the slower he goes. Stamina scales both: reference 0.6 is
        /// the knobs' own rates; 1 tires at 0.6 of them and rests at 1.4.
        /// </summary>
        private void Tire(ref PlayerState player, double dt)
        {
            // Only in a match world, two sides: practice and the benches measure the body as it is.
            if (_state.Teams.Length != 2) return;
            double fraction = SimMath.Sqrt(player.Velocity.LengthSquared) / player.Capabilities.MaxSpeed;
            double from = Config.FatigueFrom, stamina = player.Capabilities.Stamina;
            // At fatigueFrom 1 there is no band to tire in: only a body faster than his top speed
            // (a keeper's dive) is above it, and tires at the full rate.
            // The drain's arithmetic, in this order, is the recorded references' (INV-1).
            double band = 1 - from;
            double change = fraction > from
                ? (band > 0 ? Config.FatigueDrain * (fraction - from) / band : Config.FatigueDrain) * (1.6 - stamina) * dt
                : -Config.FatigueRecover * (1 - fraction / from) * (0.4 + stamina) * dt;
            double fatigue = player.Fatigue + change;
            player.Fatigue = fatigue < 0 ? 0 : fatigue > 1 ? 1 : fatigue;
        }

        /// <summary>
        /// Whether a kick intent is a slide tackle (#98): he hasn't the ball and can't kick it (out of
        /// his kick reach, or not at his feet), and an opponent controls a ball on the ground within
        /// slideRange of him.
        /// </summary>
        private bool SlideWanted(in PlayerState player)
        {
            BallState ball = _state.Ball;
            if (ball.HeldBy != 0 || ball.Position.Z > Config.KickMaxBallHeight || KeeperOf(player.Id) >= 0) return false;
            double distance = (ball.Position.Horizontal - player.Position).LengthSquared;
            if (distance <= Config.KickReach * Config.KickReach || distance > Config.SlideRange * Config.SlideRange) return false;
            int controller = ControllerOfBall();
            return controller >= 0 && _state.Players[controller].TeamId != player.TeamId;
        }

        /// <summary>m. The man a slide meets: his body, a fraction of the slide's reach (#98).</summary>
        private double SlideBodyReach => 0.5 * Config.SlideReach;

        // One step of a slide (#98): it meets the ball, or the man who has it, whichever it reaches
        // first; the ball first wins it, knocked on along the line; the man first is a foul, which
        // #99 adjudicates. Meeting neither by its end, it misses. Either way he goes to ground.
        private void StepSlide(ref PlayerState player, long tick, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;
            int had = ControllerOfBall();
            bool free = ball.HeldBy == 0 && ball.Position.Z <= Config.KickMaxBallHeight && !_kickConnected && !_challengeWon;
            double toBall = (ball.Position.Horizontal - player.Position).LengthSquared;
            bool ballMet = free && toBall <= Config.SlideReach * Config.SlideReach;
            double toMan = double.PositiveInfinity;
            if (had >= 0 && _state.Players[had].TeamId != player.TeamId) toMan = (_state.Players[had].Position - player.Position).LengthSquared;
            bool manMet = toMan <= SlideBodyReach * SlideBodyReach;

            if (manMet && !(ballMet && toBall <= toMan))
            {
                events.Add(new SimEvent(tick, SimEventKind.SlideFouled, player.Id, other: _state.Players[had].Id));
                RecordFoul(IndexOfPlayer(player.Id), had, tick, events);
            }
            else if (ballMet)
            {
                if (ball.AttachedTo != 0) Release(tick, events);
                Vec2 line = player.ChallengeLine;
                ball.Velocity = new Vec3(line.X * Config.ChallengeImpulse, line.Y * Config.ChallengeImpulse, 0);
                ball.Spin = Vec3.Zero;
                ball.LastPlayedBy = player.Id;
                for (int k = 0; k < _state.Players.Length; k++)
                {
                    _state.Players[k].AftertouchTicks = 0;
                    _state.Players[k].Turning = false;
                }

                if (had >= 0) _state.Players[had].TouchCooldown = Config.TouchIntervalTicks;
                _challengeWon = true;
                events.Add(new SimEvent(tick, SimEventKind.SlideWon, player.Id, other: had >= 0 ? _state.Players[had].Id : 0));
            }
            else if (--player.ChallengeTicks > 0)
            {
                return;
            }
            else
            {
                events.Add(new SimEvent(tick, SimEventKind.SlideMissed, player.Id));
            }

            player.Challenge = ChallengePhase.Grounded;
            player.ChallengeTicks = Config.SlideRecoverTicks;
            player.Velocity = Vec2.Zero;
        }

        /// <summary>
        /// Whether a carrier shields (#97): he has the ball, moves slower than shieldSpeed of his top
        /// speed, and an opponent is within shieldRange. <paramref name="away"/> is the way from the
        /// nearest such opponent to him: where he keeps the ball.
        /// </summary>
        private bool Shields(in PlayerState player, out Vec2 away)
        {
            away = Vec2.Zero;
            if (!Carrying(player)) return false;
            double slow = Config.ShieldSpeed * Body(player).MaxSpeed;
            if (player.Velocity.LengthSquared >= slow * slow) return false; // shieldSpeed 0: never
            double nearest = Config.ShieldRange * Config.ShieldRange;
            bool found = false;
            foreach (PlayerState other in _state.Players)
            {
                if (other.Id == player.Id || other.TeamId == player.TeamId) continue;
                Vec2 from = player.Position - other.Position;
                double d = from.LengthSquared;
                if (d > nearest || d == 0) continue;
                nearest = d;
                away = from * (1 / SimMath.Sqrt(d));
                found = true;
            }

            return found;
        }

        // A challenge that reached the ball through a shielding carrier, his body between them
        // along the challenge's line (#97): held by the contest's draw. Even, it holds at shieldHold; the carrier's strength over
        // the challenger's adds to it, and the challenger's tackling takes from it.
        private bool ShieldHolds(int carrier, in PlayerState challenger, long tick)
        {
            PlayerState c = _state.Players[carrier];
            if (!Shields(c, out _)) return false;
            // Between: the ball lies beyond him along the challenge's line. Footballers have no
            // bodies to collide, so the challenger may end his commit past him; his line says
            // which way he came.
            Vec2 toBall = _state.Ball.Position.Horizontal - c.Position, line = challenger.ChallengeLine;
            if (toBall.X * line.X + toBall.Y * line.Y <= 0) return false;
            double hold = Config.ShieldHold + 0.5 * (c.Capabilities.Strength - challenger.Capabilities.Strength) - 0.3 * (challenger.Capabilities.Tackling - 0.5);
            hold = hold < 0.05 ? 0.05 : hold > 0.95 ? 0.95 : hold;
            var draws = new StreamDraws(_state.Seed, tick, RandomStream.Shield, challenger.Id);
            return draws.NextUniform() < hold;
        }

        /// <summary>
        /// Whether the footballer has the ball at his feet (#96): attached to him under ATTACHED;
        /// under FREE, played by him last, on the ground and within <see cref="DribbleReach"/>.
        /// </summary>
        private bool Carrying(in PlayerState player) => Carries(_state, Config, player);

        /// <summary>
        /// Whether <paramref name="player"/> is carrying the ball in <paramref name="state"/>: it is
        /// attached to him (ATTACHED), or (FREE) he played it last and it is on the ground within
        /// <see cref="DribbleReach"/> of him; never while a keeper holds it. Public so a reader
        /// of the state, such as the host's poses (#101), uses the simulation's own rule.
        /// </summary>
        public static bool Carries(SimState state, SimConfig config, in PlayerState player)
        {
            BallState ball = state.Ball;
            if (ball.HeldBy != 0) return false;
            if (ball.AttachedTo != 0) return ball.AttachedTo == player.Id;
            if (ball.LastPlayedBy != player.Id || ball.Position.Z > config.KickMaxBallHeight) return false;
            return (ball.Position.Horizontal - player.Position).LengthSquared <= DribbleReach * DribbleReach;
        }

        /// <summary>
        /// The body he runs with (#96): with the ball at his feet, his top speed is the dribble share
        /// of it, from dribbleSpeedPoor at no ball control to dribbleSpeedGood at full. The movement
        /// and every prediction of his own run use it, so they agree (INV-13).
        /// </summary>
        private PlayerCapabilities Body(in PlayerState player)
        {
            PlayerCapabilities body = player.Capabilities;

            // Tired (#100), his top speed and acceleration fall toward fatigueFloor of themselves.
            double fresh = Config.FatigueFloor + (1 - Config.FatigueFloor) * (1 - player.Fatigue);
            body.MaxSpeed *= fresh;
            body.Acceleration *= fresh;

            // Hurt but playing on (#109): injuredSpeed of both.
            if (player.Injury == InjuryLevel.Minor)
            {
                body.MaxSpeed *= Config.InjuredSpeed;
                body.Acceleration *= Config.InjuredSpeed;
            }
            if (!Carrying(player)) return body;
            body.MaxSpeed *= Config.DribbleSpeedPoor + (Config.DribbleSpeedGood - Config.DribbleSpeedPoor) * body.BallControl;
            return body;
        }

        /// <summary>
        /// One step of a player's velocity toward what the stick asks for (#36): limited by the
        /// player's acceleration, whether speeding up, braking or turning. Static so the touch can
        /// predict where the player will be with the simulation's own movement (INV-13).
        /// </summary>
        private static Vec2 Accelerate(Vec2 velocity, Vec2 move, in PlayerCapabilities capabilities, double dt)
        {
            Vec2 change = move * capabilities.MaxSpeed - velocity;
            double maxChange = capabilities.Acceleration * dt;
            double changeSquared = change.LengthSquared;
            if (changeSquared > maxChange * maxChange)
            {
                change = change * (maxChange / SimMath.Sqrt(changeSquared));
            }

            return velocity + change;
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

            // A footballer in a challenge (#84) starts no kick; one already winding up carries on.
            bool ready = player.WindUpTicks == 0 && player.KickCooldown == 0 && player.Challenge == ChallengePhase.Ready && MayKick(player);
            if (command.HasValue && ready && command.Value.Kick.HasValue)
            {
                // A manual kick, which also wins over a quick pass in the same command.
                KickIntent intent = command.Value.Kick.Value;
                Vec2 d = intent.Direction;

                // Without the ball, near an opponent's (#98): the same intent is a slide tackle.
                if (SlideWanted(player))
                {
                    Vec2 line = d.X == 0 && d.Y == 0 ? new Vec2(SimMath.Cos(player.Heading), SimMath.Sin(player.Heading)) : d * (1 / SimMath.Sqrt(d.LengthSquared));
                    player.Challenge = ChallengePhase.Slide;
                    player.ChallengeTicks = Config.SlideTicks;
                    player.ChallengeLine = line;
                    player.Velocity = line * Config.SlideSpeed;
                    player.Turning = false;
                    events.Add(new SimEvent(tick, SimEventKind.SlideStarted, player.Id));
                    return;
                }

                StartWindUp(ref player, d.X == 0 && d.Y == 0 ? player.Heading : SimMath.Atan2(d.Y, d.X), intent.Strength, intent.Loft, 0);
            }
            else if (command.HasValue && ready && command.Value.QuickPass)
            {
                ResolveQuickPass(index);
                events.Add(new SimEvent(tick, SimEventKind.QuickPassResolved, player.Id, other: player.WindUpTarget));
            }

            if (player.WindUpTicks == 0) return;
            player.WindUpTicks--;
            if (player.WindUpTicks == 0) Connect(index, tick, events);
        }

        private void StartWindUp(ref PlayerState player, double line, double strength, double loft, int target)
        {
            // One more than the wind-up, because this step counts it down too.
            player.WindUpTicks = Config.KickWindUpTicks + 1;
            player.Turning = false;
            player.WindUpDirection = line;
            player.WindUpStrength = strength;
            player.WindUpLoft = loft;
            player.WindUpTarget = target;
        }

        // ---- The quick pass (#38) -------------------------------------------------------------------

        // How far ahead the quick pass looks for where it can meet a receiver.
        private const double PassHorizonSeconds = 8;

        /// <summary>
        /// The footballer resolves a quick pass, inside the simulation and after the command was
        /// recorded (INV-10). Perception picks the candidates, anticipation reads their runs, and the
        /// pass becomes an ordinary ground kick into the same wind-up as a manual kick, so the
        /// passer's execution error applies too. docs/simulation/pass.md has the rule.
        /// </summary>
        private void ResolveQuickPass(int index)
        {
            ref PlayerState passer = ref _state.Players[index];
            Vec2 origin = _state.Ball.Position.Horizontal;
            double cos = SimMath.Cos(passer.Heading), sin = SimMath.Sin(passer.Heading);
            double hardest = passer.Capabilities.KickPower * Config.KickSpeed;

            // The choice is made on what the passer sees now: ahead costs the distance, to the side
            // (1 + w/2) times it, straight back (1 + w) times. Prediction then aims the pass. Choosing
            // on predicted points would charge a runner for running away, the opposite of preferring
            // receivers ahead and advancing (#26).
            int target = 0;
            double best = double.PositiveInfinity;
            for (int j = 0; j < _state.Players.Length; j++)
            {
                PlayerState other = _state.Players[j];
                if (!IsPassCandidate(passer, other, cos, sin)) continue;

                Vec2 to = other.Position - origin;
                double distance = SimMath.Sqrt(to.LengthSquared);
                double forward = distance > 0 ? (cos * to.X + sin * to.Y) / distance : 1;
                double cost = distance * (1 + Config.PassBackWeight * (1 - forward) / 2);
                if (cost < best)
                {
                    best = cost;
                    target = j;
                }
            }

            // Aim into the chosen receiver's run, or, with no candidate, a free pass along the heading.
            bool chosen = best < double.PositiveInfinity;
            Vec2 read = chosen ? _state.Players[target].Velocity * passer.Capabilities.PassAnticipation : Vec2.Zero;
            Vec2 aim = chosen
                ? ReceivePoint(_state.Players[target].Position, read, origin, hardest)
                : origin + new Vec2(cos, sin) * Config.PassFreeDistance;
            int targetId = chosen ? _state.Players[target].Id : 0;

            // Weighted for the run onto it: the receiver's as the passer reads it, or, for a free
            // pass, the passer's own, so a pass into space from a sprint goes ahead of them (#65).
            // Never negative, so running away from the line adds nothing.
            Vec2 line = aim - origin;
            double length = SimMath.Sqrt(line.LengthSquared);
            double speed = PassSpeed(length, RunAlong(chosen ? read : passer.Velocity, line, length));
            StartWindUp(ref passer,
                length > 0 ? SimMath.Atan2(line.Y, line.X) : passer.Heading,
                hardest > 0 ? Clamp01(speed / hardest) : 1,
                0,
                targetId);
        }

        /// <summary>
        /// The documented candidate set: a teammate other than the passer, within passMaxRange of
        /// them and within ± their passVision of their heading.
        /// </summary>
        private bool IsPassCandidate(PlayerState passer, PlayerState other, double cos, double sin)
        {
            // Never a footballer off the pitch (#108), wherever he stands.
            if (other.Id == passer.Id || other.TeamId != passer.TeamId || other.Off) return false;
            Vec2 to = other.Position - passer.Position;
            double distance = SimMath.Sqrt(to.LengthSquared);
            if (distance == 0 || distance > Config.PassMaxRange) return false;
            double off = Math.Abs(SimMath.Atan2(cos * to.Y - sin * to.X, cos * to.X + sin * to.Y));
            return off <= passer.Capabilities.PassVision;
        }

        /// <summary>
        /// The ground pass that arrives at <paramref name="run"/> + passArrivalSpeed after
        /// <paramref name="distance"/>, under constant rolling friction: faster than the receiver's
        /// run along the pass by passArrivalSpeed, so a runner can take it in stride and a standing
        /// receiver gets passArrivalSpeed.
        /// </summary>
        private double PassSpeed(double distance, double run)
        {
            double arrival = Config.PassArrivalSpeed + run;
            return SimMath.Sqrt(arrival * arrival + 2 * Config.RollingFriction * Config.Gravity * distance);
        }

        // m/s. How fast a run read as `read` goes along the pass `line` of `length`; never negative.
        private static double RunAlong(Vec2 read, Vec2 line, double length) =>
            length > 0 ? Math.Max(0, (read.X * line.X + read.Y * line.Y) / length) : 0;

        /// <summary>
        /// Where the passer expects to meet the receiver: their position plus the run the passer
        /// reads (anticipation × velocity), at the first ball step after contact by which a pass
        /// weighted for that point can get there. Whether a step is reachable can switch back, for a
        /// runner coming past the ball with a weak kick, so the search scans forward in strides of a
        /// tenth of a second for the first reachable one and bisects inside that stride. With no
        /// reachable step within the horizon the pass goes where the receiver is. It rolls the ball
        /// with the simulation's own integration (INV-13).
        /// </summary>
        private Vec2 ReceivePoint(Vec2 position, Vec2 read, Vec2 origin, double hardest)
        {
            double dt = Config.TickSeconds;
            int windUp = Config.KickWindUpTicks;
            int horizon = (int)(PassHorizonSeconds * Config.TickRate);

            // After ball step j the receiver has moved windUp + j - 1 steps on from now.
            Vec2 At(int steps) => position + read * ((windUp + steps - 1) * dt);

            bool Reaches(int steps)
            {
                Vec2 to = At(steps) - origin;
                double distance = SimMath.Sqrt(to.LengthSquared);
                return Rolls(Math.Min(PassSpeed(distance, RunAlong(read, to, distance)), hardest), distance, steps);
            }

            if (Reaches(1)) return At(1);
            int stride = Math.Max(1, Config.TickRate / 10);
            for (int lo = 1; lo < horizon; lo += stride)
            {
                int hi = Math.Min(lo + stride, horizon);
                if (!Reaches(hi)) continue;

                // Unreachable at lo, reachable at hi: bisect for the step between.
                while (hi - lo > 1)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (Reaches(mid)) hi = mid;
                    else lo = mid;
                }

                return At(hi);
            }

            return position;
        }

        // Whether a ground pass launched at `speed` rolls `distance` within `steps` ball steps.
        private bool Rolls(double speed, double distance, int steps)
        {
            if (distance <= 0) return true;
            var ball = new BallState
            {
                Position = new Vec3(0, 0, Config.BallRadius),
                Velocity = new Vec3(speed, 0, 0),
            };

            for (int i = 0; i < steps; i++)
            {
                IntegrateBall(ref ball, Config, Config.TickSeconds);
                if (ball.Position.Horizontal.LengthSquared >= distance * distance) return true;
                if (ball.Velocity.X == 0) return false;
            }

            return false;
        }

        private void Connect(int index, long tick, List<SimEvent> events)
        {
            ref PlayerState player = ref _state.Players[index];
            ref BallState ball = ref _state.Ball;
            double line = player.WindUpDirection, strength = player.WindUpStrength, loft = player.WindUpLoft;
            player.WindUpDirection = player.WindUpStrength = player.WindUpLoft = 0;
            player.WindUpTarget = 0;

            // A keeper kicks the ball he holds out of his hands (#85): it drops at his feet, along his line.
            if (ball.HeldBy == player.Id)
            {
                ball.HeldBy = 0;
                ball.Position = new Vec3(player.Position.X + 0.3 * SimMath.Cos(line), player.Position.Y + 0.3 * SimMath.Sin(line), Config.BallRadius);
                ball.Velocity = Vec3.Zero;
            }

            // The ball's height decides what the kick is (#94): on the ground a kick, up to volley
            // height a volley, in the head's band a header. Between the bands, and above them, the
            // body can't play it.
            Vec2 offset = ball.Position.Horizontal - player.Position;
            double z = ball.Position.Z;
            bool header = z >= Config.HeaderMinHeight && z <= Config.HeaderMaxHeight;
            bool volley = !header && z > Config.KickMaxBallHeight && z <= Config.VolleyMaxHeight;
            double reach = header ? Config.HeaderReach : Config.KickReach;
            if (ball.HeldBy != 0 || (z > Config.KickMaxBallHeight && !header && !volley) || offset.LengthSquared > reach * reach)
            {
                events.Add(new SimEvent(tick, SimEventKind.KickMissed, player.Id));
                return;
            }

            // The intended line plus the kicker's own bounded execution error: never retargeted. A
            // volley errs more the faster the ball comes; a header by the player's heading.
            double incoming = SimMath.Sqrt(ball.Velocity.LengthSquared);
            double bound = header ? player.Capabilities.KickError + Config.HeaderError * (1 - player.Capabilities.Heading)
                : volley ? player.Capabilities.KickError + Config.VolleyError * incoming
                : player.Capabilities.KickError;
            var draws = new StreamDraws(_state.Seed, tick, RandomStream.KickDirection, player.Id);
            double direction = line + (2 * draws.NextUniform() - 1) * bound;
            double speed = header ? strength * Config.HeaderSpeed * (0.6 + 0.4 * player.Capabilities.Heading) * Config.KickSpeed
                : RestartSpeed(player, strength, strength * player.Capabilities.KickPower * Config.KickSpeed);
            double elevation = (header ? 0.5 : 1) * loft * Config.KickLoft;
            double horizontal = speed * SimMath.Cos(elevation);
            ball.Velocity = new Vec3(horizontal * SimMath.Cos(direction), horizontal * SimMath.Sin(direction), speed * SimMath.Sin(elevation));
            ball.Spin = Vec3.Zero;

            // The ball has a new kicker, so only they can shape it now.
            for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].AftertouchTicks = 0;
            player.AftertouchTicks = Config.AftertouchTicks;
            player.AftertouchDirection = line;
            player.KickCooldown = Config.KickCooldownTicks;
            _kickConnected = true;
            _state.Ball.LastPlayedBy = player.Id;
            events.Add(new SimEvent(tick, SimEventKind.Kicked, player.Id));
            if (header) events.Add(new SimEvent(tick, SimEventKind.Headed, player.Id));
            else if (volley) events.Add(new SimEvent(tick, SimEventKind.Volleyed, player.Id));
            TakeRestart(player, tick, events);
        }

        // ---- Ball control in possession (#59, docs/simulation/dribble.md) ---------------------------

        // m/s. A player slower than this traps the ball rather than pushing it on: a technical
        // constant of the model, not a feel value.
        private const double TrapSpeed = 0.5;

        /// <summary>
        /// Under <see cref="ControlModel.Free"/>, at most one player touches the ball on a step: the
        /// nearest eligible one, the lower id on a tie. Eligible means in control this step (an
        /// accepted command), not winding up a kick or in its cooldown, past the last touch's
        /// interval, and with the ball within reach and low enough. A ball beyond reach is never
        /// touched, so nothing reattaches it. It runs after kicks, so a kick's own step and its
        /// cooldown keep the ball free. A turn's two touches (#66) come when the turn starts and
        /// ends, not on the touch interval.
        /// </summary>
        // m. How far past the control reach the last player to play the ball still controls it.
        private const double ControlMargin = 1.25;

        // The stick's share along the line to the ball that counts as closing on it.
        private const double Closing = 0.5;

        // rad. The largest error of a knock-away, for a challenger with no tackling at all.
        private const double ChallengeError = 0.6;

        /// <summary>
        /// The index of the player who controls the ball (#84), or −1 when it is loose: the one it
        /// is attached to, or the last to play it while it is on the ground within a little more
        /// than their control reach. A keeper's ball, in his hands, is no one's to challenge, and
        /// under the kick-only model no one controls it.
        /// </summary>
        private int ControllerOfBall()
        {
            // Only the ball control models have possession to contest; under the kick-only model
            // no one controls the ball.
            if (Config.ControlModel != ControlModel.Free && Config.ControlModel != ControlModel.Attached) return -1;
            BallState ball = _state.Ball;
            if (ball.HeldBy != 0 || ball.Position.Z > Config.KickMaxBallHeight) return -1;
            int attached = IndexOfPlayer(ball.AttachedTo);
            if (attached >= 0) return attached;
            // A ball in a kick's flight, while anyone's aftertouch window is open, is no one's: a
            // challenge never redirects a manual kick.
            foreach (PlayerState p in _state.Players)
            {
                if (p.AftertouchTicks > 0) return -1;
            }

            // Nor is one knocked away by a challenger still recovering: the beaten player may play it.
            int last = IndexOfPlayer(ball.LastPlayedBy);
            if (last < 0 || KeeperOf(ball.LastPlayedBy) >= 0 || _state.Players[last].Challenge != ChallengePhase.Ready) return -1;
            double reach = Config.ControlReach * ControlMargin;
            return (ball.Position.Horizontal - _state.Players[last].Position).LengthSquared <= reach * reach ? last : -1;
        }

        /// <summary>
        /// The standing challenge (#84, INV-12). A footballer closing on a ball an opponent controls
        /// commits to it once it is within the challenge range: for the commit he keeps the line his
        /// stick asked for as he committed, and can't steer. As the commit ends, a ball within his reach is
        /// knocked away along that line; one that has gone is missed. Either way he recovers,
        /// braking, and can't play the ball until it ends. Nothing moves anyone to the ball and no
        /// chance decides it: the ball is won if it is still there. docs/simulation/challenge.md.
        /// </summary>
        /// <summary>
        /// A challenge's commit in whole steps (#84): the config's commit time, shorter the better
        /// the challenger's tackling (0.8 of it at full tackling, 1.25 of it at none), at least one.
        /// </summary>
        public static int CommitTicksFor(double tackling, SimConfig config) =>
            Math.Max(1, (int)Math.Round(config.ChallengeCommit * (1.25 - 0.45 * tackling) * config.TickRate));

        private void StepChallenges(long tick, List<SimEvent> events)
        {
            if (!Live) return;
            for (int i = 0; i < _state.Players.Length; i++)
            {
                ref PlayerState player = ref _state.Players[i];
                ref BallState ball = ref _state.Ball;
                if (player.Challenge == ChallengePhase.Slide)
                {
                    StepSlide(ref player, tick, events);
                    continue;
                }

                if (player.Challenge == ChallengePhase.Grounded)
                {
                    if (--player.ChallengeTicks > 0) continue;
                    player.Challenge = ChallengePhase.Ready;
                    player.ChallengeLine = Vec2.Zero;
                    continue;
                }

                if (player.Challenge == ChallengePhase.Recover)
                {
                    if (--player.ChallengeTicks > 0) continue;
                    player.Challenge = ChallengePhase.Ready;
                    player.ChallengeLine = Vec2.Zero;
                    continue;
                }

                if (player.Challenge == ChallengePhase.Commit)
                {
                    if (--player.ChallengeTicks > 0) continue;

                    // The reach, a little longer the better his tackling. A ball kicked away, won by
                    // someone else this step, held or in the air is out of it.
                    double reach = Config.ChallengeReach * (0.75 + 0.25 * player.Capabilities.Tackling);
                    bool inFlight = false;
                    foreach (PlayerState p in _state.Players) inFlight |= p.AftertouchTicks > 0;
                    bool there = !_kickConnected && !_challengeWon && !inFlight && ball.HeldBy == 0 && ball.Position.Z <= Config.KickMaxBallHeight
                        && (ball.Position.Horizontal - player.Position).LengthSquared <= reach * reach;
                    int had = there ? ControllerOfBall() : -1;

                    // Through a shielding carrier (#97): his body is between, and the challenge is a
                    // contest of strength and tackling, drawn from its own stream.
                    if (there && had >= 0 && ShieldHolds(had, player, tick))
                    {
                        events.Add(new SimEvent(tick, SimEventKind.ShieldHeld, _state.Players[had].Id, other: player.Id));
                        events.Add(new SimEvent(tick, SimEventKind.ChallengeMissed, player.Id));
                        player.Challenge = ChallengePhase.Recover;
                        player.ChallengeTicks = Config.ChallengeRecoverTicks;
                        continue;
                    }

                    if (there)
                    {
                        Vec2 line = player.ChallengeLine;
                        double error = ChallengeError * (1 - player.Capabilities.Tackling);
                        if (error > 0)
                        {
                            var draws = new StreamDraws(_state.Seed, tick, RandomStream.Challenge, player.Id);
                            double angle = (2 * draws.NextUniform() - 1) * error;
                            double c = SimMath.Cos(angle), s = SimMath.Sin(angle);
                            line = new Vec2(line.X * c - line.Y * s, line.X * s + line.Y * c);
                        }

                        // Knocked along his line, unless that is into the dribbler's feet: then it
                        // comes off them, the line mirrored about the way to him.
                        if (had >= 0)
                        {
                            Vec2 away = _state.Players[had].Position - ball.Position.Horizontal;
                            double length = SimMath.Sqrt(away.LengthSquared);
                            if (length > 0)
                            {
                                Vec2 u = away * (1 / length);
                                double into = line.X * u.X + line.Y * u.Y;
                                if (into > 0) line -= u * (2 * into);
                            }
                        }

                        if (ball.AttachedTo != 0) Release(tick, events);
                        ball.Velocity = new Vec3(line.X * Config.ChallengeImpulse, line.Y * Config.ChallengeImpulse, 0);
                        ball.Spin = Vec3.Zero;
                        ball.LastPlayedBy = player.Id;
                        for (int k = 0; k < _state.Players.Length; k++)
                        {
                            _state.Players[k].AftertouchTicks = 0;
                            _state.Players[k].Turning = false;
                        }

                        // The beaten footballer may play the loose ball, but not on the next step:
                        // he is a stride's touch from it, as after any touch of his own.
                        if (had >= 0) _state.Players[had].TouchCooldown = Config.TouchIntervalTicks;

                        _challengeWon = true;
                        events.Add(new SimEvent(tick, SimEventKind.ChallengeWon, player.Id, other: had >= 0 ? _state.Players[had].Id : 0));
                    }
                    else
                    {
                        events.Add(new SimEvent(tick, SimEventKind.ChallengeMissed, player.Id));

                        // Missing the ball, into the man who has it: a foul (#99).
                        int man = ControllerOfBall();
                        if (man >= 0 && _state.Players[man].TeamId != player.TeamId
                            && (_state.Players[man].Position - player.Position).LengthSquared <= Config.ChallengeFoulReach * Config.ChallengeFoulReach)
                        {
                            RecordFoul(i, man, tick, events);
                        }
                    }

                    player.Challenge = ChallengePhase.Recover;
                    player.ChallengeTicks = Config.ChallengeRecoverTicks;
                    continue;
                }

                // Ready: he commits when he is closing on a ball an opponent controls, within range.
                if (player.WindUpTicks > 0 || KeeperOf(player.Id) >= 0) continue;
                Command? command = FindAccepted(player.OwnerControllerId);
                if (!command.HasValue) continue;
                int controller = ControllerOfBall();
                if (controller < 0 || controller == i || _state.Players[controller].TeamId == player.TeamId) continue;
                Vec2 to = ball.Position.Horizontal - player.Position;
                double distanceSquared = to.LengthSquared;
                if (distanceSquared > Config.ChallengeRange * Config.ChallengeRange || distanceSquared == 0) continue;
                Vec2 toward = to * (1 / SimMath.Sqrt(distanceSquared));
                Vec2 stick = command.Value.Move;
                if (stick.X * toward.X + stick.Y * toward.Y < Closing) continue;

                // Only a commit that will reach the ball (#96): run along his line through the
                // commit, with the simulation's own movement (INV-13), against the ball carried on
                // with its dribbler. One that would end short of it, chasing alongside, waits.
                Vec2 commitLine = stick * (1 / SimMath.Sqrt(stick.LengthSquared));
                int commit = CommitTicksFor(player.Capabilities.Tackling, Config);
                double dt = Config.TickSeconds;
                Vec2 velocity = player.Velocity, at = player.Position;
                PlayerCapabilities body = Body(player);
                for (int k = 0; k < commit; k++)
                {
                    velocity = Accelerate(velocity, commitLine, body, dt);
                    at += velocity * dt;
                }

                // A controlled ball goes where its dribbler goes: his touches and the ATTACHED
                // steering keep it ahead of him, at his pace.
                Vec2 ballThen = ball.Position.Horizontal + _state.Players[controller].Velocity * (commit * dt);
                double reachThen = Config.ChallengeReach * (0.75 + 0.25 * player.Capabilities.Tackling);
                if ((ballThen - at).LengthSquared > reachThen * reachThen) continue;

                // He goes the way his stick asks, so he can lead the tackle into the ball's path.
                player.Challenge = ChallengePhase.Commit;
                player.ChallengeTicks = commit;
                player.ChallengeLine = commitLine;
                player.Turning = false;
                events.Add(new SimEvent(tick, SimEventKind.ChallengeStarted, player.Id));
            }
        }

        private void StepControl(long tick, List<SimEvent> events)
        {
            // A dead ball is no one's to touch (#85).
            if (!Live)
            {
                _state.Ball.AttachedTo = 0;
                return;
            }

            if (Config.ControlModel == ControlModel.Attached)
            {
                StepAttached(tick, events);
                return;
            }

            // Only ATTACHED holds the ball: any other model ends an attachment on its first step,
            // which is how a switch away from ATTACHED stops its correction at once (#60).
            _state.Ball.AttachedTo = 0;

            if (Config.ControlModel != ControlModel.Free) return;

            // A turn ends when the player loses the ball, beyond their reach (Codex on #73), so
            // their next possession starts with ordinary touches.
            double reachSquared = Config.ControlReach * Config.ControlReach;
            for (int i = 0; i < _state.Players.Length; i++)
            {
                if (_state.Players[i].Turning && (_state.Ball.Position.Horizontal - _state.Players[i].Position).LengthSquared > reachSquared) _state.Players[i].Turning = false;
            }

            if (_kickConnected || _challengeWon || _state.Ball.HeldBy != 0) return;
            if (_state.Ball.Position.Z > Config.KickMaxBallHeight) return;

            // A ball an opponent controls is not there for a touch (#84): it takes a challenge.
            int controller = ControllerOfBall();
            double nearest = double.MaxValue;
            int toucher = -1;
            TurnTouch kind = TurnTouch.None;
            bool cut = false;
            for (int i = 0; i < _state.Players.Length; i++)
            {
                PlayerState p = _state.Players[i];
                if (p.WindUpTicks > 0 || p.KickCooldown > 0 || KeeperOf(p.Id) >= 0) continue;
                if (p.Challenge != ChallengePhase.Ready || (controller >= 0 && _state.Players[controller].TeamId != p.TeamId)) continue;
                Command? command = FindAccepted(p.OwnerControllerId);
                if (!command.HasValue) continue;

                // The touch interval is a stride's cadence on the run, and a turn breaks it: its
                // trap comes as the turn starts and its touch out as the player comes round. So does
                // a cut (#75): its touch comes on its first step on which the player can play the
                // ball, at most once a stride.
                TurnTouch turn = TurnFor(p, command.Value.Move);
                bool cutting = turn == TurnTouch.None && p.CutCooldown == 0 && CutAsked(p, command.Value.Move);
                bool due = p.TouchCooldown == 0 || (turn == TurnTouch.Trap && !p.Turning) || turn == TurnTouch.Out || cutting;
                if (!due) continue;
                double distanceSquared = (_state.Ball.Position.Horizontal - p.Position).LengthSquared;
                if (distanceSquared > reachSquared || distanceSquared >= nearest) continue;
                nearest = distanceSquared;
                toucher = i;
                kind = turn;
                cut = cutting;
            }

            if (toucher < 0) return;
            // An ordinary touch at a sprint may be a push (#63); a turn's or a cut's never is.
            bool pushed = false;
            bool touched = kind == TurnTouch.Trap
                ? Trap(toucher, tick, true)
                : Touch(toucher, tick, kind == TurnTouch.Out ? Config.TurnStrength : cut ? Config.CutStrength : Config.TouchStrength, kind == TurnTouch.None && !cut, out pushed);
            if (!touched) return;

            ref PlayerState player = ref _state.Players[toucher];
            if (cut)
            {
                // The follow-up: the next touch comes the cut interval later, the way the stick
                // asks by then, or with the cadence if that is sooner.
                player.TouchCooldown = Math.Min(Config.CutIntervalTicks, Config.TouchIntervalTicks);
            }

            // One cut a stride. A turn's touch out stands for a cut's touch as the player comes
            // round from it, so a turn never has a cut touch on top of its own.
            if ((cut || kind == TurnTouch.Out) && Config.CutStrength > 0) player.CutCooldown = Config.TouchIntervalTicks;

            _state.Ball.LastPlayedBy = player.Id;
            events.Add(new SimEvent(tick, SimEventKind.Touched, player.Id));
            if (kind != TurnTouch.None) events.Add(new SimEvent(tick, SimEventKind.Turned, player.Id, other: (int)kind));
            if (cut) events.Add(new SimEvent(tick, SimEventKind.Cut, player.Id));
            if (pushed) events.Add(new SimEvent(tick, SimEventKind.Pushed, player.Id));
        }

        // ---- The cut with the ball under FREE (#75, docs/simulation/cut.md) ------------------------

        // Whether the stick asks this player for a cut: a direction more than the cut angle from
        // where they are moving, faster than the trap speed, and no more than the turn angle, beyond
        // which it is a turn whether or not the turn is on. Never when the cut is off.
        private bool CutAsked(in PlayerState p, Vec2 move)
        {
            if (Config.CutStrength <= 0) return false;
            double speedSquared = p.Velocity.LengthSquared, stickSquared = move.LengthSquared;
            if (speedSquared < TrapSpeed * TrapSpeed || stickSquared == 0) return false;
            double along = p.Velocity.X * move.X + p.Velocity.Y * move.Y;
            double norm = SimMath.Sqrt(speedSquared * stickSquared);
            return along < Config.CutCos * norm && along >= Config.TurnCos * norm;
        }

        /// <summary>
        /// ATTACHED (#60). The holder keeps the ball under a bounded correction until a kick, the
        /// loss reach or a nearer eligible player takes it; a loose ball is taken by the nearest
        /// eligible player within the control reach. Gained within <see cref="SimConfig.ControlReach"/>
        /// and lost beyond <see cref="SimConfig.AttachedLossReach"/>, so control has hysteresis.
        /// docs/simulation/attached.md has the rule.
        /// </summary>
        private void StepAttached(long tick, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;

            // A kick released it, and nothing takes it on the kick's own step. A keeper's ball is
            // in his hands.
            if (ball.HeldBy != 0)
            {
                Release(tick, events);
                return;
            }

            if (_kickConnected || _challengeWon)
            {
                Release(tick, events);
                return;
            }

            bool high = ball.Position.Z > Config.KickMaxBallHeight;
            int holder = IndexOfPlayer(ball.AttachedTo);
            if (holder >= 0)
            {
                PlayerState h = _state.Players[holder];
                double loss = Config.AttachedLossReach;
                bool beyond = (ball.Position.Horizontal - h.Position).LengthSquared > loss * loss;
                if (high || beyond || !FindAccepted(h.OwnerControllerId).HasValue)
                {
                    Release(tick, events);
                    holder = -1;
                }
            }

            // The nearest eligible player takes a loose ball, or one strictly nearer than its holder
            // takes it over; the lower id on a tie.
            if (!high)
            {
                double reachSquared = Config.ControlReach * Config.ControlReach;
                double nearest = holder >= 0 ? (ball.Position.Horizontal - _state.Players[holder].Position).LengthSquared : double.MaxValue;
                int taker = -1;
                for (int i = 0; i < _state.Players.Length; i++)
                {
                    if (i == holder) continue;
                    PlayerState p = _state.Players[i];
                    if (p.WindUpTicks > 0 || p.KickCooldown > 0 || KeeperOf(p.Id) >= 0) continue;

                    // An opponent of its holder takes it by a challenge, not by being nearer, and a
                    // footballer just beaten by one only after his stride (#84).
                    if (p.TouchCooldown > 0 || p.Challenge != ChallengePhase.Ready || (holder >= 0 && _state.Players[holder].TeamId != p.TeamId)) continue;
                    if (!FindAccepted(p.OwnerControllerId).HasValue) continue;
                    double distanceSquared = (ball.Position.Horizontal - p.Position).LengthSquared;
                    if (distanceSquared > reachSquared || distanceSquared >= nearest) continue;
                    nearest = distanceSquared;
                    taker = i;
                }

                if (taker >= 0)
                {
                    if (holder >= 0) Release(tick, events);
                    Take(taker, tick, events);
                    holder = taker;
                }
            }

            if (holder < 0) return;

            // The turn (#66): its trap as it starts, no correction while the player comes round,
            // and its touch out, in place of a correction, as they do. Not through a wind-up: the
            // holder keeps the ball under the correction until contact.
            PlayerState held = _state.Players[holder];
            TurnTouch turn = held.WindUpTicks > 0 ? TurnTouch.None : TurnFor(held, MoveOf(held));
            switch (turn)
            {
                case TurnTouch.Trap:
                    if (!held.Turning && Trap(holder, tick, false)) events.Add(new SimEvent(tick, SimEventKind.Turned, held.Id, other: (int)TurnTouch.Trap));
                    break;
                case TurnTouch.Out:
                    Out(holder);
                    events.Add(new SimEvent(tick, SimEventKind.Turned, held.Id, other: (int)TurnTouch.Out));
                    break;
                default:
                    Steer(holder);
                    break;
            }
        }

        private int IndexOfPlayer(int id)
        {
            if (id == 0) return -1;
            for (int i = 0; i < _state.Players.Length; i++)
            {
                if (_state.Players[i].Id == id) return i;
            }

            return -1;
        }

        // Losing the ball ends the holder's turn too (#66, Codex on #73).
        private void Release(long tick, List<SimEvent> events)
        {
            if (_state.Ball.AttachedTo == 0) return;
            events.Add(new SimEvent(tick, SimEventKind.Released, _state.Ball.AttachedTo));
            int holder = IndexOfPlayer(_state.Ball.AttachedTo);
            if (holder >= 0) _state.Players[holder].Turning = false;
            _state.Ball.AttachedTo = 0;
        }

        // Taking control is a first touch, as under FREE: it cushions the ball's speed relative to the
        // taker by at most the receive strength, scaled by ball control. A ball arriving faster than
        // that is only partly slowed, and the correction may not hold it. It clears spin, and closes
        // every aftertouch window, since the ball has a new controller. A new possession starts with
        // the ordinary correction, whatever turn the taker was in before (#66, Codex on #73).
        private void Take(int index, long tick, List<SimEvent> events)
        {
            _state.Players[index].Turning = false;
            ref BallState ball = ref _state.Ball;
            PlayerState taker = _state.Players[index];
            Vec2 relative = ball.Velocity.Horizontal - taker.Velocity;
            double speed = SimMath.Sqrt(relative.LengthSquared);
            double cushion = Config.ReceiveStrength * (0.5 + 0.5 * taker.Capabilities.BallControl);
            Vec2 kept = speed > cushion ? relative * ((speed - cushion) / speed) : Vec2.Zero;
            Vec2 velocity = taker.Velocity + kept;
            ball.Velocity = new Vec3(velocity.X, velocity.Y, ball.Velocity.Z);
            ball.Spin = Vec3.Zero;
            ball.AttachedTo = taker.Id;
            ball.LastPlayedBy = taker.Id;
            for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].AftertouchTicks = 0;
            events.Add(new SimEvent(tick, SimEventKind.Attached, taker.Id));
        }

        // The bounded correction: the ball's horizontal velocity, never its position, is steered
        // toward the lead point ahead of the holder. It closes at the speed from which the capped
        // acceleration could stop it at the deadband's edge, so it doesn't overshoot, and inside the
        // deadband it only keeps pace with the holder. Every step's change is capped, so a ball far
        // from its target can't snap back. Both caps scale with ball control.
        private void Steer(int index)
        {
            ref BallState ball = ref _state.Ball;
            PlayerState holder = _state.Players[index];
            double dt = Config.TickSeconds;
            double scale = 0.5 + 0.5 * holder.Capabilities.BallControl;
            double acceleration = Config.AttachedAcceleration * scale;

            double speed = SimMath.Sqrt(holder.Velocity.LengthSquared);
            double fraction = speed / holder.Capabilities.MaxSpeed;
            if (fraction > 1) fraction = 1;
            Vec2 along = speed >= TrapSpeed
                ? holder.Velocity * (1 / speed)
                : new Vec2(SimMath.Cos(holder.Heading), SimMath.Sin(holder.Heading));
            double lead = Config.AttachedLeadSlow + (Config.AttachedLeadFast - Config.AttachedLeadSlow) * fraction;
            if (Shields(holder, out Vec2 away))
            {
                // Shielding (#97): steered round to his far side from the opponent.
                along = away;
                lead = Config.AttachedLeadSlow;
            }

            Vec2 error = holder.Position + along * lead - ball.Position.Horizontal;
            double distance = SimMath.Sqrt(error.LengthSquared);
            double closing = 0;
            if (distance > Config.AttachedDeadband)
            {
                closing = SimMath.Sqrt(2 * acceleration * (distance - Config.AttachedDeadband));
                double cap = Config.AttachedSpeedCap * scale;
                if (closing > cap) closing = cap;
            }

            Vec2 wanted = closing > 0 ? holder.Velocity + error * (closing / distance) : holder.Velocity;
            Vec2 change = wanted - ball.Velocity.Horizontal;
            double length = SimMath.Sqrt(change.LengthSquared);
            double most = acceleration * dt;
            if (length > most) change = change * (most / length);
            ball.Velocity = new Vec3(ball.Velocity.X + change.X, ball.Velocity.Y + change.Y, ball.Velocity.Z);
        }

        /// <summary>
        /// One touch (#59): a bounded impulse toward the lead point. The touch predicts, with the
        /// simulation's own movement (INV-13), where the player will be at their next touch under
        /// this step's stick, and sends the ball to arrive the speed-dependent lead ahead of that,
        /// along their direction of travel, under its own rolling physics. So it pushes the ball on
        /// when the player runs, follows a turn, and settles it in front of the feet when they stop
        /// or receive. The change of velocity is capped, cushioning by receive strength and
        /// redirecting by <paramref name="strength"/> (the touch strength, or a turn's on its touch
        /// out, #66), both scaled by ball control, so a fast ball or a sharp
        /// turn at speed is only partly redirected, and the touch's direction
        /// error grows with the turn and the overload and shrinks with ball control. Returns false
        /// when there is nothing to do: a standing player, not moving off, with the ball at rest.
        /// </summary>
        private bool Touch(int index, long tick, double strength, bool mayPush, out bool pushed)
        {
            pushed = false;
            ref PlayerState player = ref _state.Players[index];
            ref BallState ball = ref _state.Ball;
            Command? command = FindAccepted(player.OwnerControllerId);
            Vec2 move = command.HasValue ? command.Value.Move : Vec2.Zero;
            Vec2 ballVelocity = ball.Velocity.Horizontal;

            // A shielding carrier moves the ball to his far side, standing or not (#97).
            bool shielding = Shields(player, out Vec2 shieldAway);
            if (shielding && (player.Position + shieldAway * Config.DribbleLeadSlow - ball.Position.Horizontal).LengthSquared < ShieldSettled * ShieldSettled
                && (ballVelocity - player.Velocity).LengthSquared <= Config.StopSpeed * Config.StopSpeed)
            {
                return false;
            }

            if (!shielding && Standing(player, move)) return false;

            Vec2 wanted = TouchTarget(player, move, Config.DribbleLeadSlow, Config.DribbleLeadFast);
            if (mayPush && Config.PushSpeed > 0 && PushWanted(player, move, out Vec2 push))
            {
                wanted = push;
                pushed = true;
            }

            double turn = TurnBetween(ballVelocity, wanted);

            // Bounded. For a ball coming at the player, the part of the change that cushions it,
            // taking off its speed relative to the player, is capped by receive strength: the first
            // touch. The rest, which redirects or pushes it, as on a turn, is capped by touch
            // strength. Both scale with ball control. A touch that needs more only partly reaches
            // what it wanted.
            double control = player.Capabilities.BallControl;
            double scale = 0.5 + 0.5 * control;
            Vec2 change = wanted - ballVelocity;
            Vec2 cushion = Vec2.Zero;
            Vec2 relative = ballVelocity - player.Velocity;
            Vec2 apart = ball.Position.Horizontal - player.Position;
            double relativeSpeed = SimMath.Sqrt(relative.LengthSquared);
            bool incoming = apart.X * relative.X + apart.Y * relative.Y < 0;
            if (incoming && relativeSpeed > 0)
            {
                Vec2 back = relative * (-1 / relativeSpeed);
                double taken = change.X * back.X + change.Y * back.Y;
                if (taken > relativeSpeed) taken = relativeSpeed;
                if (taken > 0) cushion = back * taken;
            }

            double overload = 0;
            change = Bounded(cushion, Config.ReceiveStrength * scale, ref overload) + Bounded(change - cushion, strength * scale, ref overload);

            Vec2 sent = Executed(ballVelocity + change, player, turn, overload, tick);
            ball.Velocity = new Vec3(sent.X, sent.Y, ball.Velocity.Z);
            ball.Spin = Vec3.Zero;
            player.TouchCooldown = Config.TouchIntervalTicks;
            player.Turning = false;

            // The ball has a new controller, so no earlier kicker shapes it now, as on a new kick.
            for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].AftertouchTicks = 0;
            return true;
        }

        // Push-and-chase (#63, an experiment; docs/simulation/push.md). At a sprint the touch is a
        // push: the ball leaves along the footballer's line of travel, the push speed faster than
        // they will be running by their next touch. Nothing else is added: the ball then slows
        // under its own rolling friction, beyond their reach, and they catch it by running, if they
        // can. A sprint is the stick asking for at least the push-from share of full speed, and the
        // footballer reaching that share of their max speed by their next touch (INV-13: the
        // simulation's own movement predicts it).
        private bool PushWanted(in PlayerState player, Vec2 move, out Vec2 push)
        {
            push = Vec2.Zero;
            double from = Config.PushFromSpeed;
            if (move.LengthSquared < from * from) return false;

            double dt = Config.TickSeconds;
            Vec2 velocity = player.Velocity;
            PlayerCapabilities body = Body(player);
            for (int k = 1; k < Config.TouchIntervalTicks; k++) velocity = Accelerate(velocity, move, body, dt);
            double speed = SimMath.Sqrt(velocity.LengthSquared);
            if (speed < from * body.MaxSpeed || speed < TrapSpeed) return false;

            // Only a ball they are already sprinting with: one rolling their way. A first touch on
            // a ball at rest, across them or coming at them is a receiving touch, and never starts
            // a chase.
            Vec2 ball = _state.Ball.Velocity.Horizontal;
            if (ball.X * velocity.X + ball.Y * velocity.Y < TrapSpeed * speed) return false;

            push = velocity * ((speed + Config.PushSpeed) / speed);
            return true;
        }

        // A standing player, not moving off, with the ball at rest relative to them: nothing to do.
        private bool Standing(in PlayerState player, Vec2 move) =>
            player.Velocity.LengthSquared < TrapSpeed * TrapSpeed && move.LengthSquared == 0
            && (_state.Ball.Velocity.Horizontal - player.Velocity).LengthSquared <= Config.StopSpeed * Config.StopSpeed;

        /// <summary>
        /// The velocity a touch sends the ball at: the one that rolls it, by the player's next touch,
        /// to the lead ahead of where they will be then, along their direction of travel, or their
        /// heading when nearly stopped. Where they will be is predicted under this step's stick with
        /// the simulation's own movement (INV-13). FREE's touch uses its dribble lead; the turn's
        /// touch out under ATTACHED uses ATTACHED's (#66).
        /// </summary>
        private Vec2 TouchTarget(in PlayerState player, Vec2 move, double leadSlow, double leadFast)
        {
            // Where the player will be by their next touch: the ball moves `steps` times before that
            // touch looks at it, the player `steps - 1` times more as the state is drawn, so the lead
            // is what is seen and the ball stays within reach for the touch.
            double dt = Config.TickSeconds;
            int steps = Config.TouchIntervalTicks;
            Vec2 velocity = player.Velocity, position = player.Position;
            PlayerCapabilities body = Body(player);
            for (int k = 1; k < steps; k++)
            {
                velocity = Accelerate(velocity, move, body, dt);
                position += velocity * dt;
            }

            double speed = SimMath.Sqrt(velocity.LengthSquared);
            double fraction = speed / player.Capabilities.MaxSpeed;
            if (fraction > 1) fraction = 1;
            Vec2 along = speed >= TrapSpeed
                ? velocity * (1 / speed)
                : new Vec2(SimMath.Cos(player.Heading), SimMath.Sin(player.Heading));
            double lead = leadSlow + (leadFast - leadSlow) * fraction;

            // Shielding (#97): just off his feet on the far side from the opponent.
            if (Shields(player, out Vec2 away))
            {
                along = away;
                lead = leadSlow;
            }

            return Roll(position + along * lead - _state.Ball.Position.Horizontal, steps * dt);
        }

        // The velocity that rolls the ball `to` away by `interval` under its own rolling friction,
        // or stops it there sooner.
        private Vec2 Roll(Vec2 to, double interval)
        {
            double distance = SimMath.Sqrt(to.LengthSquared);
            if (distance == 0) return Vec2.Zero;
            double rolling = Config.RollingFriction * Config.Gravity;
            double launch = interval > 0 && distance >= 0.5 * rolling * interval * interval
                ? distance / interval + 0.5 * rolling * interval
                : SimMath.Sqrt(2 * rolling * distance);
            return to * (launch / distance);
        }

        // How far the ball's own direction is from where a touch sends it, 0–π; 0 for a ball nearly at rest.
        private static double TurnBetween(Vec2 ballVelocity, Vec2 wanted)
        {
            if (ballVelocity.LengthSquared <= TrapSpeed * TrapSpeed || wanted.LengthSquared == 0) return 0;
            double cross = ballVelocity.X * wanted.Y - ballVelocity.Y * wanted.X;
            double dot = ballVelocity.X * wanted.X + ballVelocity.Y * wanted.Y;
            return Math.Abs(SimMath.Atan2(cross, dot));
        }

        // A change capped at `cap`; `overload` keeps the largest share by which a cap was exceeded, at most 1.
        private static Vec2 Bounded(Vec2 part, double cap, ref double overload)
        {
            double length = SimMath.Sqrt(part.LengthSquared);
            if (length <= cap) return part;
            double over = length / cap - 1;
            if (over > overload) overload = over > 1 ? 1 : over;
            return part * (cap / length);
        }

        // The footballer's execution error on a touch: bounded, seeded and contextual (INV-6).
        private Vec2 Executed(Vec2 sent, in PlayerState player, double turn, double overload, long tick)
        {
            double error = Config.TouchError * (1 - player.Capabilities.BallControl) * (1 + turn / (Math.PI / 2) + overload);
            if (error <= 0) return sent;
            var draws = new StreamDraws(_state.Seed, tick, RandomStream.Touch, player.Id);
            double angle = (2 * draws.NextUniform() - 1) * error;
            double c = SimMath.Cos(angle), s = SimMath.Sin(angle);
            return new Vec2(sent.X * c - sent.Y * s, sent.X * s + sent.Y * c);
        }

        // ---- The turn with the ball (#66, docs/simulation/turn.md) ----------------------------------

        // The turn's touches: its trap, then its touch out. The values are SimEvent.Other's for Turned.
        private enum TurnTouch { None = 0, Trap = 1, Out = 2 }

        // m. How far to the side of the pivot the trap sends the ball: at the planted foot, beside
        // the body, on the side the turn goes, so it passes the player rather than through them. A
        // technical constant of the model, not a feel value.
        private const double TurnFootOffset = 0.3;

        // Which of the turn's touches this player would make now, under their stick. A turn starts
        // when the stick asks for a direction more than the turn angle from where they are moving,
        // faster than the trap speed. Its trap holds until they move the stick's way, within 90° of
        // it, when the touch out plays the ball on. None when the turn is off.
        private TurnTouch TurnFor(in PlayerState p, Vec2 move)
        {
            if (Config.TurnStrength <= 0) return TurnTouch.None;
            double along = p.Velocity.X * move.X + p.Velocity.Y * move.Y;
            if (p.Turning) return along > 0 ? TurnTouch.Out : TurnTouch.Trap;
            double speedSquared = p.Velocity.LengthSquared, stickSquared = move.LengthSquared;
            if (speedSquared < TrapSpeed * TrapSpeed || stickSquared == 0) return TurnTouch.None;
            return along < Config.TurnCos * SimMath.Sqrt(speedSquared * stickSquared) ? TurnTouch.Trap : TurnTouch.None;
        }

        // The pivot: where the player's motion along their direction of travel ends, under this
        // step's stick, with the simulation's own movement (INV-13), and the steps until then. Within
        // a turn the stick is at least 90° from that direction, so the motion along it ends, and the
        // bound only guards the loop.
        private Vec2 Pivot(in PlayerState p, Vec2 move, out int steps)
        {
            double dt = Config.TickSeconds;
            Vec2 v = p.Velocity, x = p.Position, u = p.Velocity;
            PlayerCapabilities body = Body(p);
            int bound = (int)(2 * body.MaxSpeed / (body.Acceleration * dt)) + 2;
            steps = 0;
            while (steps < bound)
            {
                Vec2 next = Accelerate(v, move, body, dt);
                if (next.X * u.X + next.Y * u.Y <= 0) break;
                v = next;
                x += v * dt;
                steps++;
            }

            return x;
        }

        // The trap's velocity: the one that rolls the ball beside the pivot by the time the player
        // gets there, or stops it there sooner. Its change is bounded by the turn's strength, scaled
        // by ball control; one that needs more only partly arrests the ball.
        private Vec2 TrapVelocity(in PlayerState p, Vec2 move, out double turn, out double overload)
        {
            Vec2 ballVelocity = _state.Ball.Velocity.Horizontal;
            Vec2 v = p.Velocity;
            double speed = SimMath.Sqrt(v.LengthSquared);
            Vec2 side = Vec2.Zero;
            if (speed > 0)
            {
                // The side the turn goes; to the left on an exact reversal.
                double cross = v.X * move.Y - v.Y * move.X;
                side = (cross >= 0 ? new Vec2(-v.Y, v.X) : new Vec2(v.Y, -v.X)) * (1 / speed);
            }

            Vec2 pivot = Pivot(p, move, out int steps);
            Vec2 wanted = Roll(pivot + side * TurnFootOffset - _state.Ball.Position.Horizontal, steps * Config.TickSeconds);
            turn = TurnBetween(ballVelocity, wanted);
            overload = 0;
            return ballVelocity + Bounded(wanted - ballVelocity, Config.TurnStrength * (0.5 + 0.5 * p.Capabilities.BallControl), ref overload);
        }

        // The trap: the turn's first touch. Under FREE it carries the touch's execution error and
        // sets the touch cooldown; under ATTACHED, like its take, it has no error.
        private bool Trap(int index, long tick, bool free)
        {
            ref PlayerState player = ref _state.Players[index];
            ref BallState ball = ref _state.Ball;
            Vec2 move = MoveOf(player);
            if (Standing(player, move)) return false;

            Vec2 sent = TrapVelocity(player, move, out double turn, out double overload);
            if (free) sent = Executed(sent, player, turn, overload, tick);
            ball.Velocity = new Vec3(sent.X, sent.Y, ball.Velocity.Z);
            ball.Spin = Vec3.Zero;
            player.Turning = true;
            if (free) player.TouchCooldown = Config.TouchIntervalTicks;
            for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].AftertouchTicks = 0;
            return true;
        }

        // The touch out under ATTACHED: FREE's touch target with ATTACHED's lead, bounded by the
        // turn's strength and without an error. Its correction resumes on the next step.
        private void Out(int index)
        {
            ref PlayerState player = ref _state.Players[index];
            ref BallState ball = ref _state.Ball;
            Vec2 move = MoveOf(player);
            Vec2 ballVelocity = ball.Velocity.Horizontal;
            Vec2 wanted = TouchTarget(player, move, Config.AttachedLeadSlow, Config.AttachedLeadFast);
            double overload = 0;
            Vec2 sent = ballVelocity + Bounded(wanted - ballVelocity, Config.TurnStrength * (0.5 + 0.5 * player.Capabilities.BallControl), ref overload);
            ball.Velocity = new Vec3(sent.X, sent.Y, ball.Velocity.Z);
            player.Turning = false;
        }

        // The ball integrates, then the goal's frame resolves along the step's path (#68): the
        // straight segment from where the ball started the step to where it ends.
        // The ball (#68, #69). Held, it moves with its keeper, where he caught it. Free, it
        // integrates; then the frame turns a ball touching it at the step's start, and the first
        // contact along the step's path, with the frame or a keeper, stops it there. With no keeper
        // in its way, this is exactly BallFlight's step (INV-13).
        private void StepBall(long tick, double dt, Vec3 start, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;
            int holder = KeeperOf(ball.HeldBy);
            if (holder >= 0)
            {
                Vec2 v = _state.Players[IndexOfPlayer(ball.HeldBy)].Velocity;
                ball.Velocity = new Vec3(v.X, v.Y, 0);
                ball.Position = new Vec3(ball.Position.X + v.X * dt, ball.Position.Y + v.Y * dt, ball.Position.Z);
                return;
            }

            if (IntegrateBall(ref ball, Config, dt)) events.Add(new SimEvent(tick, SimEventKind.Bounced, ball.Id));
            int end = EndOf(start);
            GoalFrame.TouchAtStart(start, ref ball, Config, end, out FramePart touched);
            if (touched != FramePart.None) events.Add(new SimEvent(tick, SimEventKind.HitFrame, ball.Id, other: (int)touched));

            Vec3 path = ball.Position - start;
            double frame = GoalFrame.FirstContact(start, path, Config, end, out FramePart part, out Vec3 a, out Vec3 b);
            // A keeper moves through the step too, so his contact is swept in his frame: the ball's
            // path relative to his, against his envelope where he started the step.
            double soonest = double.PositiveInfinity;
            int saver = -1;
            for (int k = 0; k < _state.Keepers.Length && Live; k++)
            {
                Vec2 moved = Body(k).Position - _keeperFrom[k];
                Envelope(k, _keeperFrom[k], out Vec3 feet, out Vec3 top, out double reach);
                double s = GoalFrame.Sweep(start, path - new Vec3(moved.X, moved.Y, 0), feet, top, reach);
                if (s < soonest)
                {
                    soonest = s;
                    saver = k;
                }
            }

            if (saver >= 0 && soonest <= frame)
            {
                Vec2 moved = Body(saver).Position - _keeperFrom[saver];
                Save(saver, start + path * soonest, _keeperFrom[saver] + moved * soonest, moved * (1 - soonest), tick, events);
            }
            else if (part != FramePart.None)
            {
                GoalFrame.StopAndTurn(start + path * frame, ref ball, a, b, Config.FrameRestitution);
                events.Add(new SimEvent(tick, SimEventKind.HitFrame, ball.Id, other: (int)part));
            }
        }

        /// <summary>
        /// One step of the ball model. Static so that predictions run the simulation's own physics
        /// (INV-13): the quick pass weighs and leads its passes with it. Returns whether the ball bounced.
        /// </summary>
        internal static bool IntegrateBall(ref BallState ball, SimConfig config, double dt)
        {
            double radius = config.BallRadius;
            bool airborne = ball.Position.Z > radius || ball.Velocity.Z > 0;

            if (airborne)
            {
                // Spin curves the ball in flight only (#33): once rolling, it keeps its line.
                Vec3 magnus = Vec3.Cross(ball.Spin, ball.Velocity) * config.MagnusCoefficient;
                ball.Velocity += (magnus + new Vec3(0, 0, -config.Gravity)) * dt;
                ball.Position += ball.Velocity * dt;

                bool bounced = false;
                if (ball.Position.Z <= radius && ball.Velocity.Z < 0)
                {
                    double up = -ball.Velocity.Z * config.BounceRestitution;
                    if (up < config.SettleSpeed) up = 0;
                    double keep = config.BounceTangentialRetention;
                    ball.Velocity = new Vec3(ball.Velocity.X * keep, ball.Velocity.Y * keep, up);
                    ball.Position = new Vec3(ball.Position.X, ball.Position.Y, radius);

                    // Ground contact removes most of the spin (#40), so repeated hops stop curving.
                    ball.Spin *= config.BounceSpinRetention;
                    bounced = true;
                }

                ball.Spin *= Decay(config.AirSpinDecay, dt);
                return bounced;
            }

            // Rolling: friction against the direction of travel. Spin keeps decaying but no longer
            // curves the ball (SOL's research answer on #24: shaping is a short post-kick effect).
            Vec2 velocity = ball.Velocity.Horizontal;
            double speedSquared = velocity.LengthSquared;
            if (speedSquared > 0)
            {
                double speed = SimMath.Sqrt(speedSquared);
                double slowed = speed - config.RollingFriction * config.Gravity * dt;
                velocity = slowed <= config.StopSpeed ? Vec2.Zero : velocity * (slowed / speed);
            }

            ball.Velocity = new Vec3(velocity.X, velocity.Y, 0);
            ball.Position = new Vec3(ball.Position.X + velocity.X * dt, ball.Position.Y + velocity.Y * dt, radius);
            ball.Spin *= Decay(config.GroundSpinDecay, dt);
            return false;
        }

        // ---- The keeper (#69, docs/simulation/keeper.md) -------------------------------------------

        // m. The top of a keeper's envelope's axis on his feet, and on the ground: technical
        // constants of the body, not feel values. His reach extends the envelope beyond both.
        private const double StandingTop = 2.0;
        private const double LyingTop = 0.3;

        /// <summary>The index in <see cref="SimState.Keepers"/> of the keeper who is player <paramref name="playerId"/>, or −1.</summary>
        private int KeeperOf(int playerId)
        {
            if (playerId == 0) return -1;
            for (int k = 0; k < _state.Keepers.Length; k++)
            {
                if (_state.Keepers[k].PlayerId == playerId) return k;
            }

            return -1;
        }

        /// <summary>
        /// Each keeper's phase, after kicks: a phase from an earlier step counts down and ends; a kick
        /// that connected on this step sets a keeper on his feet reacting; and a keeper on his feet
        /// takes a dive his command asks for. So a dive starts no sooner than his reaction time after
        /// the kick, whatever his brain asks, and a dive asked on the kick's own step is refused.
        /// </summary>
        private void StepKeepers(long tick, List<SimEvent> events)
        {
            for (int k = 0; k < _state.Keepers.Length; k++)
            {
                ref KeeperState keeper = ref _state.Keepers[k];
                ref PlayerState body = ref _state.Players[IndexOfPlayer(keeper.PlayerId)];
                keeper.HoldTicks = _state.Ball.HeldBy == keeper.PlayerId ? keeper.HoldTicks + 1 : 0;

                if (keeper.Phase != KeeperPhase.Position && --keeper.PhaseTicks == 0)
                {
                    if (keeper.Phase == KeeperPhase.Dive)
                    {
                        // He lands, and stays down.
                        keeper.Phase = KeeperPhase.Recover;
                        keeper.PhaseTicks = Config.KeeperRecoverTicks;
                        keeper.DiveVelocity = Vec2.Zero;
                        keeper.DiveTop = 0;
                        body.Velocity = Vec2.Zero;
                    }
                    else
                    {
                        keeper.Phase = KeeperPhase.Position;
                    }
                }

                if (_kickConnected && keeper.Phase == KeeperPhase.Position)
                {
                    keeper.Phase = KeeperPhase.React;
                    keeper.PhaseTicks = ReactionTicks(keeper.Capabilities, Config);
                    events.Add(new SimEvent(tick, SimEventKind.KeeperReacted, keeper.PlayerId));
                }

                Command? command = FindAccepted(body.OwnerControllerId);
                if (keeper.Phase == KeeperPhase.Position && command.HasValue && command.Value.Dive.HasValue && _state.Ball.HeldBy != keeper.PlayerId)
                {
                    Dive(ref keeper, body, command.Value.Dive.Value);
                    events.Add(new SimEvent(tick, SimEventKind.KeeperDived, keeper.PlayerId));
                }
            }
        }

        // A dive carries his body toward the target at his dive speed for the steps the distance
        // needs, at most the longest dive, at the target's height: from the next step on.
        private void Dive(ref KeeperState keeper, in PlayerState body, Vec3 target)
        {
            Vec2 to = target.Horizontal - body.Position;
            double distance = SimMath.Sqrt(to.LengthSquared);
            double step = keeper.Capabilities.DiveSpeed * Config.TickSeconds;
            int ticks = distance > 0 ? (int)Math.Ceiling(distance / step - 1e-9) : 1;
            if (ticks < 1) ticks = 1;
            if (ticks > Config.KeeperDiveTicks) ticks = Config.KeeperDiveTicks;

            keeper.Phase = KeeperPhase.Dive;
            keeper.PhaseTicks = ticks;
            keeper.DiveVelocity = distance > 0 ? to * (keeper.Capabilities.DiveSpeed / distance) : Vec2.Zero;
            keeper.DiveTop = target.Z < LyingTop ? LyingTop : target.Z > StandingTop ? StandingTop : target.Z;
        }

        private PlayerState Body(int k) => _state.Players[IndexOfPlayer(_state.Keepers[k].PlayerId)];

        /// <summary>
        /// A keeper's envelope with his body at <paramref name="at"/>: the capsule his hands reach,
        /// round a vertical axis there, from the ground to a top his phase sets, at his reach plus
        /// the ball's radius for the ball's centre. It moves only with his body.
        /// </summary>
        private void Envelope(int k, Vec2 at, out Vec3 feet, out Vec3 top, out double reach)
        {
            KeeperState keeper = _state.Keepers[k];
            double height = keeper.Phase switch
            {
                KeeperPhase.Dive => keeper.DiveTop,
                KeeperPhase.Recover => LyingTop,
                _ => StandingTop,
            };

            feet = new Vec3(at.X, at.Y, 0);
            top = new Vec3(at.X, at.Y, height);
            reach = keeper.Capabilities.Reach + Config.BallRadius;
        }

        /// <summary>
        /// The ball meets keeper <paramref name="k"/>'s envelope. Relative to him, he holds a ball no
        /// faster than his hold speed, which falls with his handling, to half at none, and with how
        /// far out toward his reach its line passes his body, to nothing at his fingertips. Anything
        /// faster he parries: its velocity along the contact normal reverses, scaled by the parry
        /// restitution, and turns by a seeded error that shrinks with his handling. No random save
        /// chance: the outcome is the speed, where he meets it, and his handling. The contact is
        /// where both were at that moment of the step, the ball at <paramref name="ballAt"/> and his
        /// body at <paramref name="keeperAt"/>; a held ball goes on with him for the
        /// <paramref name="rest"/> of his step.
        /// </summary>
        private void Save(int k, Vec3 ballAt, Vec2 keeperAt, Vec2 rest, long tick, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;
            KeeperState keeper = _state.Keepers[k];
            PlayerState body = Body(k);
            Envelope(k, keeperAt, out Vec3 feet, out Vec3 top, out _);
            ball.Position = ballAt;

            var moving = new Vec3(body.Velocity.X, body.Velocity.Y, 0);
            Vec3 relative = ball.Velocity - moving;
            double speed = SimMath.Sqrt(relative.LengthSquared);
            double stretch = speed > 0 ? LineToAxis(ball.Position, relative, feet, top) / keeper.Capabilities.Reach : 1;
            if (stretch > 1) stretch = 1;
            double hold = Config.KeeperHoldSpeed * (0.5 + 0.5 * keeper.Capabilities.Handling) * (1 - stretch);

            ball.Spin = Vec3.Zero;
            ball.AttachedTo = 0;
            for (int i = 0; i < _state.Players.Length; i++) _state.Players[i].AftertouchTicks = 0;

            if (speed <= hold)
            {
                ball.HeldBy = keeper.PlayerId;
                ball.Velocity = moving;
                ball.Position = new Vec3(ballAt.X + rest.X, ballAt.Y + rest.Y, ballAt.Z);
                ball.LastPlayedBy = keeper.PlayerId;
                events.Add(new SimEvent(tick, SimEventKind.KeeperHeld, keeper.PlayerId));
                return;
            }

            Vec3 normal = ball.Position - GoalFrame.Closest(ball.Position, feet, top);
            double length = SimMath.Sqrt(normal.LengthSquared);
            if (length > 0)
            {
                normal = normal * (1 / length);
                double into = GoalFrame.Dot(relative, normal);
                if (into < 0) relative -= normal * ((1 + Config.KeeperParryRestitution) * into);
            }

            double error = Config.KeeperParryError * (1 - keeper.Capabilities.Handling);
            if (error > 0)
            {
                var draws = new StreamDraws(_state.Seed, tick, RandomStream.Keeper, keeper.PlayerId);
                double angle = (2 * draws.NextUniform() - 1) * error;
                double c = SimMath.Cos(angle), s = SimMath.Sin(angle);
                relative = new Vec3(relative.X * c - relative.Y * s, relative.X * s + relative.Y * c, relative.Z);
            }

            ball.Velocity = relative + moving;
            ball.LastPlayedBy = keeper.PlayerId;
            events.Add(new SimEvent(tick, SimEventKind.KeeperParried, keeper.PlayerId));
        }

        // How close the line through `p` along `direction` passes the axis from `a` to `b`.
        private static double LineToAxis(Vec3 p, Vec3 direction, Vec3 a, Vec3 b)
        {
            Vec3 w = b - a, r = p - a;
            double uu = GoalFrame.Dot(direction, direction), ww = GoalFrame.Dot(w, w), uw = GoalFrame.Dot(direction, w);
            double ur = GoalFrame.Dot(direction, r), wr = GoalFrame.Dot(w, r);
            double denominator = uu * ww - uw * uw;
            double along = denominator > 1e-12 ? (uu * wr - uw * ur) / denominator : 0;
            if (along < 0) along = 0;
            if (along > 1) along = 1;
            Vec3 onAxis = a + w * along;
            double t = GoalFrame.Dot(direction, onAxis - p) / uu;
            Vec3 gap = p + direction * t - onAxis;
            return SimMath.Sqrt(gap.LengthSquared);
        }

        /// <summary>
        /// The goal line (#68), after the frame, on where the ball ends the step and where its centre
        /// crossed the line on the step's path. The centre over the line decides through the mouth or
        /// wide, where it crossed; the whole ball over it scores or goes out, once. The state returns
        /// to in play only when the centre is back in front of the line. docs/simulation/goal.md.
        /// </summary>
        ///
        /// In a match (#82) each end has a line, the −x end's the +x end's turned half a turn. The
        /// ball is judged against the line of the half it started the step in, on its distance
        /// toward that end, so the one state serves both: the ball can only be over one line. A goal
        /// names the side attacking that end.
        private void CheckGoalLine(long tick, Vec3 start, List<SimEvent> events)
        {
            ref BallState ball = ref _state.Ball;
            if (!Live) return;
            double line = Config.GoalLineX;
            int goal = EndOf(start);

            // Toward the goal: x at the +x end, −x at the −x end.
            double from = goal * start.X, to = goal * ball.Position.X;
            if (to <= line)
            {
                ball.Line = LineState.InPlay;
                return;
            }

            // A ball whose centre started the step in front of the line was in play, whatever its
            // state says. One placed over the line in play is judged where it was placed.
            LineState state = from <= line ? LineState.InPlay : ball.Line;
            if (state == LineState.InPlay)
            {
                Vec3 at = from <= line ? start + (ball.Position - start) * ((line - from) / (to - from)) : start;
                state = GoalFrame.InMouth(at, Config) ? LineState.CrossingMouth : LineState.CrossingWide;
            }

            if (to - Config.BallRadius > line && (state == LineState.CrossingMouth || state == LineState.CrossingWide))
            {
                // Straight from a throw-in, a ball through the mouth scores nothing (#85, Law 15): it is
                // out of play over the goal line. Anyone else's touch, this step's included, makes it a goal.
                bool straightFromThrow = _state.Restart.On && _state.Restart.FromThrow
                    && (ball.LastPlayedBy == 0 || ball.LastPlayedBy == _state.Restart.TakerId);
                if (state == LineState.CrossingMouth && straightFromThrow) state = LineState.CrossingWide;
                events.Add(new SimEvent(tick, SimEventKind.GoalLineCrossed, ball.Id));
                // In a shootout (#111) a ball in the goal is a kick scored, not a goal of the match.
                if (state == LineState.CrossingMouth && _state.Match.Phase != MatchPhase.Shootout)
                {
                    events.Add(new SimEvent(tick, SimEventKind.Goal, ball.Id, other: Attacking(goal)));
                    Credit(Attacking(goal));
                }

                _crossedEnd = goal;
                _crossedMouth = state == LineState.CrossingMouth;
                state = state == LineState.CrossingMouth ? LineState.Goal : LineState.Out;
            }

            ball.Line = state;
        }

        /// <summary>
        /// The end a ball at <paramref name="at"/> is at (#82): +1 for the +x goal, −1 for the −x
        /// goal. Practice, with no sides, has the +x goal only.
        /// </summary>
        private int EndOf(Vec3 at) => _state.Teams.Length == 2 && at.X < 0 ? -1 : 1;

        // The id of the side attacking an end; 0 with no sides.
        private int Attacking(int end)
        {
            foreach (TeamState team in _state.Teams)
            {
                if (team.AttackSign == end) return team.Id;
            }

            return 0;
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

                // 0 means "no pass target" (#38), so ids are positive.
                if (p.Id <= 0) throw new ArgumentException($"Player ids must be positive; got {p.Id}.", nameof(state));

                string owner = $"Player {p.Id}";
                p.Capabilities.CheckRanges(owner);

                // Kick state this config could have produced (#37). NaN fails the encoding above.
                InRange(owner, nameof(p.Slot), p.Slot, MaxSlot);
                if (!(p.Fatigue >= 0 && p.Fatigue <= 1)) throw new ArgumentOutOfRangeException(nameof(state), $"{owner}: fatigue {p.Fatigue} is outside [0, 1].");
                InRange(owner, nameof(p.KickCooldown), p.KickCooldown, config.KickCooldownTicks);
                InRange(owner, nameof(p.WindUpTicks), p.WindUpTicks, config.KickWindUpTicks);
                InRange(owner, nameof(p.AftertouchTicks), p.AftertouchTicks, config.AftertouchTicks);
                InRange(owner, nameof(p.TouchCooldown), p.TouchCooldown, config.TouchIntervalTicks);
                InRange(owner, nameof(p.CutCooldown), p.CutCooldown, config.TouchIntervalTicks);

                // A challenge phase this config could have produced (#84), with a unit line when not ready.
                if (p.Challenge > ChallengePhase.Grounded) throw new ArgumentException($"{owner}: unknown challenge phase {(byte)p.Challenge}.", nameof(state));
                InRange(owner, nameof(p.ChallengeTicks), p.ChallengeTicks, p.Challenge switch
                {
                    ChallengePhase.Commit => CommitTicksFor(p.Capabilities.Tackling, config),
                    ChallengePhase.Recover => config.ChallengeRecoverTicks,
                    ChallengePhase.Slide => config.SlideTicks,
                    ChallengePhase.Grounded => config.SlideRecoverTicks,
                    _ => 0,
                });
                if (p.Challenge != ChallengePhase.Ready && p.ChallengeTicks == 0) throw new ArgumentException($"{owner}: a {p.Challenge} challenge needs steps left.", nameof(state));
                double lineLength = p.ChallengeLine.LengthSquared;
                if (p.Challenge == ChallengePhase.Ready ? lineLength != 0 : (lineLength < 1 - 1e-9 || lineLength > 1 + 1e-9))
                {
                    throw new ArgumentException($"{owner}: a challenge's line is a unit vector, and zero when ready.", nameof(state));
                }
                if (p.WindUpStrength < 0 || p.WindUpStrength > 1 || p.WindUpLoft < 0 || p.WindUpLoft > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(state), $"{owner}: wind-up strength and loft must be within [0, 1].");
                }

                // A pass target exists only during a wind-up, and is a teammate.
                if (p.WindUpTarget != 0)
                {
                    bool teammate = false;
                    foreach (PlayerState other in state.Players)
                    {
                        if (other.Id == p.WindUpTarget && other.Id != p.Id && other.TeamId == p.TeamId) teammate = true;
                    }

                    if (p.WindUpTicks == 0 || !teammate)
                    {
                        throw new ArgumentException($"{owner}: pass target {p.WindUpTarget} must be a teammate, during a wind-up.", nameof(state));
                    }
                }
            }

            // Keepers (#69): each a player, once, in ascending id order, with capabilities in range
            // and a phase this config could have produced.
            for (int i = 0; i < state.Keepers.Length; i++)
            {
                KeeperState k = state.Keepers[i];
                if (i > 0 && k.PlayerId <= state.Keepers[i - 1].PlayerId) throw new ArgumentException("Keepers must be in strictly ascending player id order.", nameof(state));
                if (Array.FindIndex(state.Players, p => p.Id == k.PlayerId) < 0) throw new ArgumentException($"Keeper {k.PlayerId} isn't a player in the state.", nameof(state));
                string owner = $"Keeper {k.PlayerId}";
                k.Capabilities.CheckRanges(owner);
                int most = k.Phase switch
                {
                    KeeperPhase.Position => 0,
                    KeeperPhase.React => ReactionTicks(k.Capabilities, config),
                    KeeperPhase.Dive => config.KeeperDiveTicks,
                    KeeperPhase.Recover => config.KeeperRecoverTicks,
                    _ => throw new ArgumentException($"{owner}: unknown phase {(int)k.Phase}.", nameof(state)),
                };
                InRange(owner, nameof(k.PhaseTicks), k.PhaseTicks, most);
                if (k.HoldTicks < 0 || (k.HoldTicks > 0 && state.Ball.HeldBy != k.PlayerId)) throw new ArgumentException($"{owner}: he has held the ball for {k.HoldTicks} steps, but doesn't hold it.", nameof(state));
                if (k.Phase != KeeperPhase.Position && k.PhaseTicks == 0) throw new ArgumentException($"{owner}: a {k.Phase} phase needs steps left.", nameof(state));
            }

            // The sides (#82): none, for practice, or two that attack opposite ways, with every
            // player on one of them.
            if (state.Teams.Length != 0)
            {
                if (state.Teams.Length != 2) throw new ArgumentException($"A state has no teams or two, not {state.Teams.Length}.", nameof(state));
                TeamState first = state.Teams[0], second = state.Teams[1];
                if (first.Id <= 0 || second.Id <= first.Id) throw new ArgumentException("Teams must have positive ids, in strictly ascending order.", nameof(state));
                if ((first.AttackSign != 1 && first.AttackSign != -1) || second.AttackSign != -first.AttackSign)
                {
                    throw new ArgumentException("The two teams must attack opposite ways: one +1, the other −1.", nameof(state));
                }

                foreach (PlayerState p in state.Players)
                {
                    if (p.TeamId != first.Id && p.TeamId != second.Id) throw new ArgumentException($"Player {p.Id}'s team {p.TeamId} isn't one of the state's two.", nameof(state));
                }
            }

            // CPU outfielders (#83): each a player who isn't a keeper, once, in ascending id order,
            // with a skill in range and plans this encoding knows.
            for (int i = 0; i < state.Cpus.Length; i++)
            {
                CpuState c = state.Cpus[i];
                if (i > 0 && c.PlayerId <= state.Cpus[i - 1].PlayerId) throw new ArgumentException("CPU outfielders must be in strictly ascending player id order.", nameof(state));
                if (Array.FindIndex(state.Players, p => p.Id == c.PlayerId) < 0) throw new ArgumentException($"CPU outfielder {c.PlayerId} isn't a player in the state.", nameof(state));
                if (Array.FindIndex(state.Keepers, k => k.PlayerId == c.PlayerId) >= 0) throw new ArgumentException($"Player {c.PlayerId} is a keeper, and can't be a CPU outfielder too.", nameof(state));
                c.Skill.CheckRanges($"CPU outfielder {c.PlayerId}");
                foreach (CpuPlanState plan in new[] { c.Plan, c.Next })
                {
                    if (plan.Mode < CpuMode.None || plan.Mode > CpuMode.Support) throw new ArgumentException($"CPU outfielder {c.PlayerId}: unknown mode {(int)plan.Mode}.", nameof(state));
                    if (plan.Move.LengthSquared > 1 + 1e-9 || plan.KickStrength < 0 || plan.KickStrength > 1 || plan.KickLoft < 0 || plan.KickLoft > 1)
                    {
                        throw new ArgumentOutOfRangeException(nameof(state), $"CPU outfielder {c.PlayerId}: a plan's stick is at most 1 long, and its kick's strength and loft within [0, 1].");
                    }
                }
            }

            // The bench (#110): each a substitute of one of the two sides, once, in ascending id
            // order; one who hasn't come on isn't a player on the pitch.
            for (int i = 0; i < state.Bench.Length; i++)
            {
                BenchPlayer b = state.Bench[i];
                if (b.Id <= 0 || (i > 0 && b.Id <= state.Bench[i - 1].Id)) throw new ArgumentException("Substitutes must have positive ids, in strictly ascending order.", nameof(state));
                if (Array.FindIndex(state.Teams, t => t.Id == b.TeamId) < 0) throw new ArgumentException($"Substitute {b.Id}'s team {b.TeamId} isn't one of the state's two.", nameof(state));
                if (!b.Used && Array.FindIndex(state.Players, p => p.Id == b.Id) >= 0) throw new ArgumentException($"Substitute {b.Id} hasn't come on, but is a player in the state.", nameof(state));
                b.Capabilities.CheckRanges($"Substitute {b.Id}");
                b.Skill.CheckRanges($"Substitute {b.Id}");
            }

            if (state.Ball.HeldBy != 0 && Array.FindIndex(state.Keepers, k => k.PlayerId == state.Ball.HeldBy) < 0)
            {
                throw new ArgumentException($"The ball is held by {state.Ball.HeldBy}, who isn't a keeper.", nameof(state));
            }

            ValidateRestart(state, config);
            ValidateMatch(state, config);

            // A goal-line state the encoding knows (#68).
            if (state.Ball.Line > LineState.CrossingWide) throw new ArgumentException($"Unknown goal-line state {(byte)state.Ball.Line}.", nameof(state));

            // The ball is attached to no one (0) or to a player who exists (#60).
            if (state.Ball.AttachedTo != 0)
            {
                bool exists = false;
                foreach (PlayerState p in state.Players)
                {
                    if (p.Id == state.Ball.AttachedTo) exists = true;
                }

                if (!exists) throw new ArgumentException($"The ball is attached to player {state.Ball.AttachedTo}, who isn't in the state.", nameof(state));
            }

            return state;
        }

        /// <summary>A keeper's reaction time in whole steps, nearest, at least one (#69).</summary>
        internal static int ReactionTicks(in KeeperCapabilities capabilities, SimConfig config) =>
            Math.Max(1, (int)Math.Round(capabilities.ReactionTime * config.TickRate));

        /// <summary>A keeper's reaction in whole steps under <paramref name="config"/>, for tools that report it (#69).</summary>
        public static int ReactionTicksFor(KeeperCapabilities capabilities, SimConfig config) => ReactionTicks(capabilities, config);

        private static void InRange(string owner, string field, int value, int max)
        {
            if (value < 0 || value > max)
            {
                throw new ArgumentOutOfRangeException(field, value, $"{owner}: {field} must be within [0, {max}] under this config.");
            }
        }
    }
}
