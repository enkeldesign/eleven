using System;
using System.Collections.Generic;

namespace Eleven.Simulation
{
    /// <summary>
    /// A knockout's ending (#111): level after extra time, kicks from the penalty mark. Each kick
    /// is a penalty through the restart sequencer (INV-8): set up, taken by its taker's own
    /// command, and judged here. Five each (shootoutKicks), the sides in turn, then sudden death;
    /// over as soon as one side can't be caught. docs/simulation/knockout.md.
    /// </summary>
    public sealed partial class Simulator
    {
        // Whether the two sides have as many goals.
        private bool Level() => _state.Teams.Length == 2 && _state.Teams[0].Goals == _state.Teams[1].Goals;

        // In a shootout only two footballers move: the taker until his kick, and from the kick the
        // keeper he faces. Everyone else stands where he was put.
        private bool HeldInShootout(in PlayerState player)
        {
            RestartState r = _state.Restart;
            if (r.Phase == RestartPhase.Ready) return player.Id != r.TakerId;
            if (r.Phase == RestartPhase.Live) return !(KeeperOf(player.Id) >= 0 && player.TeamId != r.AwardedTeam);
            return true;
        }

        private void StartShootout(long tick, List<SimEvent> events)
        {
            ref MatchState m = ref _state.Match;
            m.Phase = MatchPhase.Shootout;
            m.ClockTicks = 0;
            m.PhaseTicks = 0;
            CloseSubstitutionWindows();
            events.Add(new SimEvent(tick, SimEventKind.ShootoutStarted, 0));
            BeginShootoutKick(tick, events);
        }

        // The next kick: the side that kicked off the match first, then in turn. Its taker is its
        // next outfielder on the pitch, by id, round again when all have taken; its keeper only if
        // it has no outfielder. Everyone else waits in the centre circle; the keepers keep their goals.
        private void BeginShootoutKick(long tick, List<SimEvent> events)
        {
            int first = Array.FindIndex(_state.Teams, t => t.Id == _state.Match.FirstKickOff), second = 1 - first;
            int side = _state.Teams[first].ShootoutKicks <= _state.Teams[second].ShootoutKicks ? first : second;
            int team = _state.Teams[side].Id;

            int outfielders = 0;
            foreach (PlayerState p in _state.Players)
            {
                if (p.TeamId == team && !p.Off && KeeperOf(p.Id) < 0) outfielders++;
            }

            int turn = outfielders > 0 ? _state.Teams[side].ShootoutKicks % outfielders : 0, taker = -1;
            for (int i = 0, seen = 0; i < _state.Players.Length && taker < 0; i++)
            {
                PlayerState p = _state.Players[i];
                if (p.TeamId != team || p.Off) continue;
                bool keeper = KeeperOf(p.Id) >= 0;
                if (outfielders > 0 ? !keeper && seen++ == turn : keeper) taker = i;
            }

            int end = AttackSignOf(team);
            var mark = new Vec2(end * (Config.GoalLineX - PenaltyMark), 0);
            for (int i = 0, waiting = 0; i < _state.Players.Length; i++)
            {
                ref PlayerState p = ref _state.Players[i];
                if (p.Off || KeeperOf(p.Id) >= 0) continue;
                p.Velocity = Vec2.Zero;

                // The taker at the mark, so the set-up takes him as the nearest; the rest in rows
                // either side of the halfway line, each side in its own half, 2 m apart.
                if (i == taker)
                {
                    p.Position = mark;
                    continue;
                }

                double y = 2.0 * (waiting / 2 + 1) * (waiting % 2 == 0 ? 1 : -1);
                waiting++;
                p.Position = new Vec2(-AttackSignOf(p.TeamId) * 3, y);
            }

            Stop(RestartKind.Penalty, team, mark, tick, events);
            _state.Match.PhaseTicks = 0;
        }

        // A kick in flight: scored when the ball is in the goal the kicking side attacks; missed
        // when it leaves the pitch elsewhere, the keeper holds it, or shootoutKickTime runs out.
        private void StepShootout(long tick, List<SimEvent> events)
        {
            ref RestartState r = ref _state.Restart;
            if (r.Phase != RestartPhase.Live) return;
            ref MatchState m = ref _state.Match;
            ref BallState ball = ref _state.Ball;
            m.PhaseTicks++;

            bool scored = _crossedEnd != 0 && _crossedMouth && TeamAttacking(_crossedEnd) == r.AwardedTeam;
            bool wide = (ball.Position.Y < 0 ? -ball.Position.Y : ball.Position.Y) - Config.BallRadius > Config.TouchlineY;
            if (!scored && _crossedEnd == 0 && !wide && ball.HeldBy == 0 && m.PhaseTicks < Config.ShootoutKickTicks) return;

            int kicking = r.AwardedTeam, side = Array.FindIndex(_state.Teams, t => t.Id == kicking);
            _state.Teams[side].ShootoutKicks++;
            if (scored) _state.Teams[side].ShootoutGoals++;
            events.Add(new SimEvent(tick, SimEventKind.ShootoutKick, r.TakerId, other: scored ? 1 : 0));
            m.PhaseTicks = 0;

            // Over when one side can't be caught in the kicks left of the first shootoutKicks each,
            // or, after those, when the sides have taken as many and one has scored more.
            TeamState a = _state.Teams[0], b = _state.Teams[1];
            int each = (int)Config.ShootoutKicks;
            bool decided = a.ShootoutKicks <= each && b.ShootoutKicks <= each && !(a.ShootoutKicks == each && b.ShootoutKicks == each)
                ? a.ShootoutGoals > b.ShootoutGoals + (each - b.ShootoutKicks) || b.ShootoutGoals > a.ShootoutGoals + (each - a.ShootoutKicks)
                : a.ShootoutKicks == b.ShootoutKicks && a.ShootoutGoals != b.ShootoutGoals;
            if (decided)
            {
                m.Phase = MatchPhase.FullTime;
                events.Add(new SimEvent(tick, SimEventKind.FullTime, 0));
                return;
            }

            BeginShootoutKick(tick, events);
        }
    }
}
