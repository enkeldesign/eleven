using System;
using System.Collections.Generic;

namespace Eleven.Simulation
{
    /// <summary>
    /// Substitutions (#110, Law 3): a side brings on up to <c>substitutions</c> substitutes from
    /// its bench, at a stoppage, in at most <c>substitutionWindows</c> stoppages in play;
    /// half-time is free. A substitution is an owned command (INV-10), made at the start of the
    /// step, before anything moves: the substitute takes the slot, the place and the controller of the man he replaces,
    /// fresh, and the man replaced takes no further part. docs/simulation/substitutions.md.
    /// </summary>
    public sealed partial class Simulator
    {
        // Why `team` may not bring `on` on for `off` now, or None.
        private CommandRejection MaySubstitute(int team, int off, int on)
        {
            int at = IndexOfPlayer(off), bench = BenchOf(on);
            if (at < 0 || bench < 0) return CommandRejection.SubIneligible;
            PlayerState leaving = _state.Players[at];
            BenchPlayer coming = _state.Bench[bench];
            // A footballer sent off is not replaced (Law 3); one taken off injured is.
            if (leaving.TeamId != team || leaving.SentOff || coming.TeamId != team || coming.Used) return CommandRejection.SubIneligible;

            int t = Array.FindIndex(_state.Teams, x => x.Id == team);
            if (t < 0) return CommandRejection.SubIneligible;
            TeamState side = _state.Teams[t];
            if (side.Substitutions >= Config.Substitutions) return CommandRejection.SubNoneLeft;

            MatchPhase phase = _state.Match.Phase;
            if (phase == MatchPhase.FullTime) return CommandRejection.SubNotStopped;
            if (phase == MatchPhase.HalfTime) return CommandRejection.None;
            if (!_state.Restart.On || _state.Restart.Phase == RestartPhase.Live) return CommandRejection.SubNotStopped;
            return side.SubWindowOpen || side.SubWindows < Config.SubstitutionWindows ? CommandRejection.None : CommandRejection.SubNoWindow;
        }

        private int BenchOf(int id)
        {
            if (id == 0) return -1;
            for (int i = 0; i < _state.Bench.Length; i++)
            {
                if (_state.Bench[i].Id == id) return i;
            }

            return -1;
        }

        // Play is live again (#110): a side's next substitution is in a new window.
        private void CloseSubstitutionWindows()
        {
            for (int i = 0; i < _state.Teams.Length; i++) _state.Teams[i].SubWindowOpen = false;
        }

        private void StepSubstitutions(long tick, List<SimEvent> events)
        {
            foreach (Command c in _accepted)
            {
                if (c.SubOff == 0 && c.SubOn == 0) continue;
                int owner = -1;
                for (int i = 0; i < _state.Players.Length; i++)
                {
                    if (_state.Players[i].OwnerControllerId == c.ControllerId) owner = i;
                }

                // Checked again: an earlier substitution this step may have used the last one, or
                // him. Refused then with its reason, like any other; the rest of the command stands.
                if (owner < 0) continue;
                CommandRejection why = MaySubstitute(_state.Players[owner].TeamId, c.SubOff, c.SubOn);
                if (why != CommandRejection.None)
                {
                    events.Add(new SimEvent(tick, SimEventKind.CommandRejected, c.ControllerId, why));
                    continue;
                }

                Substitute(IndexOfPlayer(c.SubOff), BenchOf(c.SubOn), tick, events);
            }
        }

        private void Substitute(int at, int bench, long tick, List<SimEvent> events)
        {
            PlayerState off = _state.Players[at];
            BenchPlayer sub = _state.Bench[bench];
            int keeper = KeeperOf(off.Id), cpu = CpuOf(off.Id);
            var on = new PlayerState
            {
                Id = sub.Id, OwnerControllerId = off.OwnerControllerId, TeamId = off.TeamId, Slot = off.Slot,
                Capabilities = sub.Capabilities, Position = off.Position, Heading = off.Heading,
            };

            if (off.Off)
            {
                // The man replaced was taken off injured (#109): the substitute comes on at the
                // touchline where he left, in a slot behind his side's last, his own controller's
                // with the bench's CPU playing him. A keeper's goal is still his to take.
                double side = off.Position.Y < 0 ? -1 : 1;
                on.Position = new Vec2(off.Position.X, side * (Config.TouchlineY - 1));
                on.OwnerControllerId = sub.Id;
                if (keeper < 0)
                {
                    int slots = 0;
                    foreach (PlayerState p in _state.Players)
                    {
                        if (p.TeamId == off.TeamId && p.Slot > slots) slots = p.Slot;
                    }

                    on.Slot = slots > 0 ? slots + 1 : 0;
                    var cpus = new CpuState[_state.Cpus.Length + 1];
                    Array.Copy(_state.Cpus, cpus, _state.Cpus.Length);
                    cpus[cpus.Length - 1] = new CpuState { PlayerId = sub.Id, Skill = sub.Skill, NextDecisionTick = tick + 1 };
                    _state.Cpus = cpus;
                }
            }
            else if (cpu >= 0)
            {
                // The CPU that played the man replaced plays the substitute, deciding afresh.
                CpuState moved = _state.Cpus[cpu];
                moved.PlayerId = sub.Id;
                moved.Plan = default;
                moved.Next = default;
                moved.HasNext = false;
                moved.NextDecisionTick = tick + 1;
                _state.Cpus[cpu] = moved;
            }

            if (keeper >= 0)
            {
                KeeperState goal = _state.Keepers[keeper];
                goal.PlayerId = sub.Id;
                goal.Phase = KeeperPhase.Position;
                goal.PhaseTicks = 0;
                goal.DiveVelocity = Vec2.Zero;
                goal.DiveTop = 0;
                _state.Keepers[keeper] = goal;
            }

            // Every reference to the man replaced is the substitute's.
            ref BallState ball = ref _state.Ball;
            if (ball.HeldBy == off.Id) ball.HeldBy = sub.Id;
            if (ball.AttachedTo == off.Id) ball.AttachedTo = sub.Id;
            if (_state.Restart.TakerId == off.Id) _state.Restart.TakerId = sub.Id;
            for (int i = 0; i < _state.Players.Length; i++)
            {
                if (_state.Players[i].WindUpTarget == off.Id) _state.Players[i].WindUpTarget = sub.Id;
            }

            // The players stay in ascending id order: the substitute's place in the array is found
            // again, and the offside flags, which are by place, follow their footballers; his own is clear.
            var players = new List<PlayerState>(_state.Players);
            ulong flags = _state.Restart.OffsideFlags, followed = 0;
            var ids = new List<int>();
            for (int i = 0; i < players.Count && i < 64; i++)
            {
                if (i != at && (flags & (1UL << i)) != 0) ids.Add(players[i].Id);
            }

            players[at] = on;
            players.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < players.Count && i < 64; i++)
            {
                if (ids.Contains(players[i].Id)) followed |= 1UL << i;
            }

            _state.Players = players.ToArray();
            _state.Restart.OffsideFlags = followed;
            Array.Sort(_state.Cpus, (a, b) => a.PlayerId.CompareTo(b.PlayerId));
            Array.Sort(_state.Keepers, (a, b) => a.PlayerId.CompareTo(b.PlayerId));

            _state.Bench[bench].Used = true;
            int t = Array.FindIndex(_state.Teams, x => x.Id == off.TeamId);
            ref TeamState team = ref _state.Teams[t];
            team.Substitutions++;
            if (_state.Match.Phase != MatchPhase.HalfTime && !team.SubWindowOpen)
            {
                team.SubWindowOpen = true;
                team.SubWindows++;
            }

            events.Add(new SimEvent(tick, SimEventKind.Substituted, sub.Id, other: off.Id));
        }
    }
}
