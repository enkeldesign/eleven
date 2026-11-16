using System;
using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation
{
    /// <summary>
    /// The restart sequencer (#85, INV-8): out of play, goals and restarts, as rules over the state,
    /// run by the simulation on each step. A restart is restart → simulation → termination: it is
    /// awarded when the ball leaves play, set up after the dead-ball time, and ends when its taker
    /// kicks the ball, which is an ordinary kick on the ordinary ball. No mode carries football: a
    /// match turns the sequencer on (<see cref="RestartState.On"/>), and practice leaves it off.
    /// docs/simulation/restarts.md.
    /// </summary>
    public sealed partial class Simulator
    {
        /// <summary>m. How far an opponent must be from a restart's ball: Law 8's, 13's, 16's and 17's 9.15 m.</summary>
        public const double RestartDistance = 9.15;

        /// <summary>m. From a throw-in: Law 15's 2 m.</summary>
        public const double ThrowInDistance = 2;

        /// <summary>m. The goal area's depth and reach past each post: Law 1's 5.5 m.</summary>
        public const double GoalArea = 5.5;

        /// <summary>m. The penalty mark's distance from the goal line (Law 1, #99).</summary>
        public const double PenaltyMark = 11;

        /// <summary>m. A free kick this close to the goal it attacks is shot (#99), a technical constant.</summary>
        public const double FreeKickShot = 25;

        /// <summary>m. The penalty area's depth from the goal line, and its half-width (Law 1, #99).</summary>
        public const double PenaltyAreaDepth = 16.5, PenaltyAreaHalfWidth = 20.16;

        /// <summary>Whether <paramref name="at"/> is in the penalty area at the <paramref name="end"/> (+1 or −1) end (#99).</summary>
        public static bool InPenaltyArea(Vec2 at, int end, SimConfig config) =>
            at.X * end >= config.GoalLineX - PenaltyAreaDepth && at.X * end <= config.GoalLineX && (at.Y < 0 ? -at.Y : at.Y) <= PenaltyAreaHalfWidth;

        /// <summary>m. How far behind the ball its taker is placed.</summary>
        public const double TakerBehind = 0.6;

        // Set by the goal line on the step it judges a crossing (#85): which end, and whether it scored.
        private int _crossedEnd;

        // This step's foul, if any (#99): the offender and the footballer fouled, by index; -1 for none.
        private int _foulBy = -1, _foulOn = -1;

        // A foul (#99), with a referee to see it: the first of a step counts.
        private void RecordFoul(int offender, int fouled, long tick, List<SimEvent> events)
        {
            if (!_state.Restart.On || _foulBy >= 0 || offender < 0 || fouled < 0) return;
            _foulBy = offender;
            _foulOn = fouled;
            events.Add(new SimEvent(tick, SimEventKind.Foul, _state.Players[offender].Id, other: _state.Players[fouled].Id));
        }

        // The referee's call on this step's foul (#99): in the offender's penalty area a penalty;
        // elsewhere advantage while the fouled side has the ball, or a direct free kick at once.
        private void Judge(long tick, List<SimEvent> events)
        {
            PlayerState fouled = _state.Players[_foulOn];
            int team = fouled.TeamId, end = AttackSignOf(team);
            Vec2 at = fouled.Position;
            double most = Config.GoalLineX - 1, wide = Config.TouchlineY - 1;
            at = new Vec2(at.X > most ? most : at.X < -most ? -most : at.X, at.Y > wide ? wide : at.Y < -wide ? -wide : at.Y);
            if (InPenaltyArea(fouled.Position, end, Config))
            {
                Stop(RestartKind.Penalty, team, new Vec2(end * (Config.GoalLineX - PenaltyMark), 0), tick, events);
                return;
            }

            int holder = ControllerOfBall();
            int ticks = (int)System.Math.Round(Config.AdvantageTime * Config.TickRate);
            if (holder >= 0 && _state.Players[holder].TeamId == team && ticks > 0)
            {
                ref RestartState r = ref _state.Restart;
                r.AdvantageTeam = team;
                r.AdvantageTicks = ticks;
                r.AdvantagePoint = at;
                events.Add(new SimEvent(tick, SimEventKind.AdvantagePlayed, team));
                return;
            }

            Stop(RestartKind.FreeKick, team, at, tick, events);
        }

        // Advantage running (#99): given after all if the fouled side loses the ball within it.
        private void StepAdvantage(long tick, List<SimEvent> events)
        {
            ref RestartState r = ref _state.Restart;
            if (r.AdvantageTeam == 0) return;
            int holder = ControllerOfBall();
            if (holder >= 0 && _state.Players[holder].TeamId != r.AdvantageTeam)
            {
                Stop(RestartKind.FreeKick, r.AdvantageTeam, r.AdvantagePoint, tick, events);
                return;
            }

            if (--r.AdvantageTicks <= 0)
            {
                r.AdvantageTeam = r.AdvantageTicks = 0;
                r.AdvantagePoint = Vec2.Zero;
            }
        }
        private bool _crossedMouth;

        /// <summary>Whether the ball is in play: always without a referee, and only when live with one.</summary>
        private bool Live => (!_state.Restart.On || _state.Restart.Phase == RestartPhase.Live) && !MatchPaused;

        // Whether this player is held still while a restart is set up: everyone but its taker.
        private bool Held(in PlayerState player) =>
            _state.Restart.On && _state.Restart.Phase == RestartPhase.Ready && player.Id != _state.Restart.TakerId;

        // Whether this player may kick now: anyone in play; only the taker, once he may, at a restart.
        private bool MayKick(in PlayerState player) =>
            Live || (_state.Restart.Phase == RestartPhase.Ready && player.Id == _state.Restart.TakerId && _state.Restart.PhaseTicks == 0);

        // The side of a player, by id; 0 for none.
        private int TeamOf(int playerId)
        {
            int i = IndexOfPlayer(playerId);
            return i >= 0 ? _state.Players[i].TeamId : 0;
        }

        private int TeamAttacking(int end)
        {
            foreach (TeamState team in _state.Teams)
            {
                if (team.AttackSign == end) return team.Id;
            }

            return 0;
        }

        private int Opponents(int team)
        {
            foreach (TeamState t in _state.Teams)
            {
                if (t.Id != team) return t.Id;
            }

            return 0;
        }

        private int AttackSignOf(int team)
        {
            foreach (TeamState t in _state.Teams)
            {
                if (t.Id == team) return t.AttackSign;
            }

            return 1;
        }

        // A restart's kick connects (#85): the ball is in play. A throw-in leaves the hands at the
        // throw's speed, not a kick's.
        private double RestartSpeed(in PlayerState player, double strength, double kickSpeed)
        {
            if (!_state.Restart.On || _state.Restart.Phase != RestartPhase.Ready || player.Id != _state.Restart.TakerId) return kickSpeed;
            return _state.Restart.Kind == RestartKind.ThrowIn ? strength * Config.ThrowSpeed : kickSpeed;
        }

        private void TakeRestart(in PlayerState player, long tick, List<SimEvent> events)
        {
            ref RestartState r = ref _state.Restart;
            if (!r.On || r.Phase != RestartPhase.Ready || player.Id != r.TakerId) return;
            events.Add(new SimEvent(tick, SimEventKind.RestartTaken, player.Id, other: (int)r.Kind));
            r.FromThrow = r.Kind == RestartKind.ThrowIn;
            r.Phase = RestartPhase.Live;
            r.PhaseTicks = r.WaitTicks = 0;
        }

        /// <summary>
        /// The sequencer's step, after the ball and the goal line. Live: who played the ball last is
        /// noted, and the ball wholly over a line or into a goal stops play and awards the restart,
        /// once. Stopped: after the dead-ball time the restart is set up. Ready: the wait is counted;
        /// the taker's kick ends it (<see cref="TakeRestart"/>).
        /// </summary>
        private void StepRestart(long tick, List<SimEvent> events)
        {
            ref RestartState r = ref _state.Restart;
            if (!r.On || MatchPaused) return;
            switch (r.Phase)
            {
                case RestartPhase.Live:
                {
                    ref BallState ball = ref _state.Ball;
                    int team = TeamOf(ball.LastPlayedBy);
                    if (team != 0) r.LastTouchedTeam = team;
                    if (r.FromThrow && ball.LastPlayedBy != 0 && ball.LastPlayedBy != r.TakerId) r.FromThrow = false;

                    if (_crossedEnd != 0)
                    {
                        int end = _crossedEnd;
                        int attackers = TeamAttacking(end), defenders = Opponents(attackers);
                        double side = ball.Position.Y >= 0 ? 1 : -1;
                        if (_crossedMouth) Stop(RestartKind.KickOff, defenders, Vec2.Zero, tick, events);
                        else if (r.LastTouchedTeam == attackers) Stop(RestartKind.GoalKick, defenders, new Vec2(end * (Config.GoalLineX - GoalArea), side * (Config.GoalWidth / 2 + GoalArea)), tick, events);
                        else Stop(RestartKind.Corner, attackers, new Vec2(end * (Config.GoalLineX - 0.5), side * (Config.TouchlineY - 0.5)), tick, events);
                    }
                    else if ((ball.Position.Y < 0 ? -ball.Position.Y : ball.Position.Y) - Config.BallRadius > Config.TouchlineY)
                    {
                        double side = ball.Position.Y > 0 ? 1 : -1;
                        double x = ball.Position.X;
                        double most = Config.GoalLineX - 1;
                        if (x > most) x = most;
                        if (x < -most) x = -most;
                        int awarded = r.LastTouchedTeam != 0 ? Opponents(r.LastTouchedTeam) : TeamAttacking(x >= 0 ? -1 : 1);
                        Stop(RestartKind.ThrowIn, awarded, new Vec2(x, side * Config.TouchlineY), tick, events);
                    }
                    else if (_foulBy >= 0)
                    {
                        Judge(tick, events);
                    }
                    else
                    {
                        StepAdvantage(tick, events);
                    }

                    break;
                }

                case RestartPhase.Stopped:
                    if (--r.PhaseTicks <= 0) Place(tick, events);
                    break;

                case RestartPhase.Ready:
                    if (r.PhaseTicks > 0) r.PhaseTicks--;
                    else r.WaitTicks++;
                    break;
            }
        }

        private void Stop(RestartKind kind, int awarded, Vec2 point, long tick, List<SimEvent> events)
        {
            ref RestartState r = ref _state.Restart;
            r.Phase = RestartPhase.Stopped;
            r.Kind = kind;
            r.AwardedTeam = awarded;
            r.Point = point;
            r.PhaseTicks = Config.RestartStopTicks;
            r.WaitTicks = 0;
            r.TakerId = 0;
            r.FromThrow = false;
            r.AdvantageTeam = r.AdvantageTicks = 0;
            r.AdvantagePoint = Vec2.Zero;
            _state.Ball.AttachedTo = 0;
            events.Add(new SimEvent(tick, SimEventKind.RestartAwarded, awarded, other: (int)kind));
        }

        /// <summary>
        /// Sets a restart up: the ball at its point, at rest and free; its taker, the awarded side's
        /// outfielder nearest the point, just behind it facing the play; every footballer stopped,
        /// out of any kick, challenge or dive; opponents at least the law's distance from the ball;
        /// and for a kick-off, every footballer in his own half, the keepers on their lines.
        /// </summary>
        private void Place(long tick, List<SimEvent> events)
        {
            ref RestartState r = ref _state.Restart;
            ref BallState ball = ref _state.Ball;
            ball.Position = new Vec3(r.Point.X, r.Point.Y, Config.BallRadius);
            ball.Velocity = ball.Spin = Vec3.Zero;
            ball.Line = LineState.InPlay;
            ball.AttachedTo = ball.HeldBy = ball.LastPlayedBy = 0;

            // The taker: the awarded side's outfielder nearest the point; its keeper if it has none.
            int taker = -1;
            double nearest = double.MaxValue;
            for (int pass = 0; pass < 2 && taker < 0; pass++)
            {
                for (int i = 0; i < _state.Players.Length; i++)
                {
                    PlayerState p = _state.Players[i];
                    if (p.TeamId != r.AwardedTeam || (pass == 0 && KeeperOf(p.Id) >= 0)) continue;
                    double d = (p.Position - r.Point).LengthSquared;
                    if (d < nearest)
                    {
                        nearest = d;
                        taker = i;
                    }
                }
            }

            int sign = AttackSignOf(r.AwardedTeam);
            Vec2 facing = Facing(r.Kind, r.Point, sign);
            double distance = r.Kind == RestartKind.ThrowIn ? ThrowInDistance : RestartDistance;
            for (int i = 0; i < _state.Players.Length; i++)
            {
                ref PlayerState p = ref _state.Players[i];
                p.Velocity = Vec2.Zero;
                p.WindUpTicks = p.AftertouchTicks = p.TouchCooldown = p.CutCooldown = p.KickCooldown = 0;
                p.WindUpDirection = p.WindUpStrength = p.WindUpLoft = p.AftertouchDirection = 0;
                p.WindUpTarget = 0;
                p.Turning = false;
                p.Challenge = ChallengePhase.Ready;
                p.ChallengeTicks = 0;
                p.ChallengeLine = Vec2.Zero;

                int keeper = KeeperOf(p.Id);
                int own = AttackSignOf(p.TeamId);
                if (i == taker)
                {
                    p.Position = r.Point - facing * TakerBehind;
                    p.Heading = SimMath.Atan2(facing.Y, facing.X);
                }
                else if (r.Kind == RestartKind.KickOff)
                {
                    // In his own half, and an opponent out of the centre circle; keepers on their lines.
                    if (keeper >= 0) p.Position = new Vec2(-own * (Config.GoalLineX - 1.5), 0);
                    else if (p.Position.X * own > -TakerBehind) p.Position = new Vec2(-own * (p.TeamId == r.AwardedTeam ? 2 : RestartDistance), p.Position.Y);
                    p.Heading = own > 0 ? 0 : Math.PI;
                }

                if (i != taker && p.TeamId != r.AwardedTeam)
                {
                    Vec2 away = p.Position - r.Point;
                    double length = SimMath.Sqrt(away.LengthSquared);
                    if (length < distance) p.Position = r.Point + (length > 1e-9 ? away * (1 / length) : new Vec2(-sign, 0)) * distance;
                }

                if (keeper >= 0)
                {
                    // The ball is out of his hands: his hold ends with it.
                    _state.Keepers[keeper].HoldTicks = 0;
                    _state.Keepers[keeper].Phase = KeeperPhase.Position;
                    _state.Keepers[keeper].PhaseTicks = 0;
                    _state.Keepers[keeper].DiveVelocity = Vec2.Zero;
                }
            }

            // A penalty (#99, Law 14): the defending keeper on his line, between the posts; everyone
            // else but the taker outside the area and the law's distance from the mark.
            if (r.Kind == RestartKind.Penalty)
            {
                int end = AttackSignOf(r.AwardedTeam);
                double back = Config.GoalLineX - PenaltyMark - RestartDistance - 0.5;
                for (int i = 0; i < _state.Players.Length; i++)
                {
                    if (i == taker) continue;
                    ref PlayerState p = ref _state.Players[i];
                    bool defendingKeeper = KeeperOf(p.Id) >= 0 && p.TeamId != r.AwardedTeam;
                    if (defendingKeeper)
                    {
                        p.Position = new Vec2(end * (Config.GoalLineX - 0.3), 0);
                        p.Heading = end > 0 ? Math.PI : 0;
                    }
                    else if (InPenaltyArea(p.Position, end, Config) || (p.Position - r.Point).LengthSquared < RestartDistance * RestartDistance)
                    {
                        p.Position = new Vec2(end * back, p.Position.Y);
                    }
                }
            }

            r.Phase = RestartPhase.Ready;
            r.PhaseTicks = Config.RestartReadyTicks;
            r.WaitTicks = 0;
            r.TakerId = taker >= 0 ? _state.Players[taker].Id : 0;
            events.Add(new SimEvent(tick, SimEventKind.RestartPlaced, r.TakerId, other: (int)r.Kind));
        }

        // The way a restart's taker faces: up the pitch for a kick-off and a goal kick, into the
        // pitch for a throw-in, at the goal for a corner.
        private Vec2 Facing(RestartKind kind, Vec2 point, int sign)
        {
            switch (kind)
            {
                case RestartKind.ThrowIn: return new Vec2(0, point.Y > 0 ? -1 : 1);
                case RestartKind.Corner:
                {
                    Vec2 to = new Vec2((point.X < 0 ? -1 : 1) * Config.GoalLineX, 0) - point;
                    return to * (1 / SimMath.Sqrt(to.LengthSquared));
                }

                default: return new Vec2(sign, 0);
            }
        }

        // The restart's state this config and these teams could have produced.
        private static void ValidateRestart(SimState state, SimConfig config)
        {
            RestartState r = state.Restart;
            if (r.Phase > RestartPhase.Ready || r.Kind > RestartKind.IndirectFreeKick) throw new ArgumentException("Unknown restart phase or kind.", nameof(state));
            if (!r.On)
            {
                if (r.Phase != RestartPhase.Live) throw new ArgumentException("With the sequencer off, the ball is live.", nameof(state));
                return;
            }

            if (state.Teams.Length != 2) throw new ArgumentException("The restart sequencer needs a match: two sides.", nameof(state));
            bool IsTeam(int id) => Array.FindIndex(state.Teams, t => t.Id == id) >= 0;
            if (r.LastTouchedTeam != 0 && !IsTeam(r.LastTouchedTeam)) throw new ArgumentException($"Restart: last touched by side {r.LastTouchedTeam}, which isn't one of the two.", nameof(state));
            if (r.Phase == RestartPhase.Live) return;
            if (r.Kind == RestartKind.None || !IsTeam(r.AwardedTeam)) throw new ArgumentException("A stopped or ready restart names its kind and its side.", nameof(state));
            int most = r.Phase == RestartPhase.Stopped ? config.RestartStopTicks : config.RestartReadyTicks;
            if (r.PhaseTicks < 0 || r.PhaseTicks > most || r.WaitTicks < 0) throw new ArgumentOutOfRangeException(nameof(state), $"Restart: {r.PhaseTicks} steps left is outside [0, {most}].");
            if (r.Phase == RestartPhase.Ready && Array.FindIndex(state.Players, p => p.Id == r.TakerId && p.TeamId == r.AwardedTeam) < 0)
            {
                throw new ArgumentException($"Restart: its taker {r.TakerId} isn't one of side {r.AwardedTeam}'s footballers.", nameof(state));
            }
        }
    }

    /// <summary>
    /// Takes restarts for their takers (#85): owned commands like any other, appended before a
    /// step and recorded, so a replay runs nothing. A CPU footballer's restart is taken as soon as
    /// it may be. A human's is taken for him once it has waited <see cref="SimConfig.RestartAuto"/>,
    /// so a match never stalls waiting for a screen: his own command for that step gives way.
    /// </summary>
    public static class RestartControllers
    {
        public static void Append(Simulator simulator, List<Command> commands)
        {
            SimState state = simulator.PeekState();
            RestartState r = state.Restart;
            if (!r.On || r.Phase != RestartPhase.Ready) return;
            int taker = Array.FindIndex(state.Players, p => p.Id == r.TakerId);
            if (taker < 0) return;
            PlayerState body = state.Players[taker];

            // A CPU taker stands at the ball through the set-up and kicks as soon as he may; a
            // human taker is left alone until the wait runs out.
            bool cpu = Array.FindIndex(state.Cpus, c => c.PlayerId == body.Id) >= 0;
            if (!cpu && (r.PhaseTicks > 0 || r.WaitTicks < simulator.Config.RestartAutoTicks)) return;
            SimConfig config = simulator.Config;

            // A taker who has wandered off the ball (a human may, through the set-up) is walked
            // back to stand just off it first, slowing as he arrives; he kicks only from within
            // reach, so a kick is never wasted on a ball he can't meet and the match never stalls.
            Vec2 toBall = state.Ball.Position.Horizontal - body.Position;
            double away = SimMath.Sqrt(toBall.LengthSquared);
            Vec2 move = Vec2.Zero;
            if (away > 0.9 * config.KickReach)
            {
                double standOff = Math.Min(Simulator.TakerBehind, 0.6 * config.KickReach);
                Vec2 to = toBall * ((away - standOff) / away);
                double length = SimMath.Sqrt(to.LengthSquared);
                move = to * (1 / Math.Max(length, 2.0));
            }

            bool kick = move.LengthSquared == 0 && r.PhaseTicks == 0 && body.WindUpTicks == 0 && body.KickCooldown == 0;

            commands.RemoveAll(c => c.ControllerId == body.OwnerControllerId);
            commands.Add(new Command(body.OwnerControllerId, state.Tick, move, kick ? Take(state, config, r, body) : (KickIntent?)null));
        }

        /// <summary>
        /// The basic taker's kick, in the taker's side's frame: a short kick forward at a
        /// kick-off, a throw down the line, a long goal kick up the pitch, a cross at the penalty
        /// spot from a corner. Technical references, not tactics.
        /// </summary>
        public static KickIntent Take(SimState state, SimConfig config, RestartState r, PlayerState taker)
        {
            int sign = Array.Find(state.Teams, t => t.Id == r.AwardedTeam).AttackSign;
            double side = r.Point.Y >= 0 ? 1 : -1;
            switch (r.Kind)
            {
                case RestartKind.ThrowIn: return new KickIntent(new Vec2(sign * 0.7, -side * 0.7), 0.6, 0.4);
                case RestartKind.GoalKick: return new KickIntent(new Vec2(sign, -side * 0.3), 0.8, 0.45);
                case RestartKind.Corner: return new KickIntent(new Vec2((r.Point.X < 0 ? -1 : 1) * (config.GoalLineX - 11), 0) - r.Point, 0.6, 0.5);
                case RestartKind.Penalty:
                {
                    // Low and hard into a corner; the side the seed's taker favours by his id.
                    double corner = (taker.Id % 2 == 0 ? 1 : -1) * (config.GoalWidth / 2 - 0.6);
                    return new KickIntent(new Vec2(sign * config.GoalLineX, corner) - r.Point, 0.9, 0.04);
                }

                case RestartKind.FreeKick:
                {
                    // Within shooting distance at goal, otherwise a long ball up the pitch.
                    var goal = new Vec2(sign * config.GoalLineX, 0);
                    Vec2 to = goal - r.Point;
                    return to.LengthSquared <= Simulator.FreeKickShot * Simulator.FreeKickShot
                        ? new KickIntent(to + new Vec2(0, side * (config.GoalWidth / 2 - 0.8)), 0.9, 0.12)
                        : new KickIntent(new Vec2(sign, -side * 0.2), 0.85, 0.4);
                }

                default: return new KickIntent(new Vec2(sign, 0.3), 0.25, 0);
            }
        }
    }
}
