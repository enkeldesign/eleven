using System;
using System.Collections.Generic;

namespace Eleven.Simulation
{
    /// <summary>
    /// Player switching (#93): which footballer a controller owns is state (INV-10), and a switch
    /// is an owned command. At the end of the step a controller with an accepted switch takes over
    /// its target; the footballer it leaves goes to the target's CPU controller, with the target's
    /// skill and fresh timers, so his side's brain plays him. Nothing about it depends on the screen.
    /// docs/simulation/switching.md.
    /// </summary>
    public sealed partial class Simulator
    {
        // Whether the footballer `player` owns may be left for `target`: a teammate, not a keeper,
        // played by the CPU. Only a controller that isn't the CPU's, on an outfielder, switches: a
        // CPU footballer or a keeper has a role of his own to keep (#104).
        private bool MaySwitch(in PlayerState player, int target)
        {
            if (target == player.Id || CpuOf(player.Id) >= 0 || KeeperOf(player.Id) >= 0) return false;
            int t = IndexOfPlayer(target);
            if (t < 0 || KeeperOf(target) >= 0 || CpuOf(target) < 0) return false;
            return _state.Players[t].TeamId == player.TeamId;
        }

        private void StepSwitches(long tick, List<SimEvent> events)
        {
            foreach (Command c in _accepted)
            {
                if (c.SwitchTo == 0) continue;
                int from = -1;
                for (int i = 0; i < _state.Players.Length; i++)
                {
                    if (_state.Players[i].OwnerControllerId == c.ControllerId) from = i;
                }

                int to = IndexOfPlayer(c.SwitchTo);
                int cpu = CpuOf(c.SwitchTo);
                if (from < 0 || to < 0 || cpu < 0) continue;

                // The controllers change footballers; the CPU's state moves to the one it now plays.
                ref PlayerState left = ref _state.Players[from];
                ref PlayerState taken = ref _state.Players[to];
                int cpuController = taken.OwnerControllerId;
                taken.OwnerControllerId = c.ControllerId;
                left.OwnerControllerId = cpuController;

                // The human takes him over on his run (#138): a neutral stick keeps it for a while.
                taken.CarryTicks = Config.SwitchCarryTicks;
                left.CarryTicks = 0;
                CpuState moved = _state.Cpus[cpu];
                moved.PlayerId = left.Id;
                moved.Plan = default;
                moved.Next = default;
                moved.HasNext = false;
                moved.NextDecisionTick = tick + 1;
                _state.Cpus[cpu] = moved;
                Array.Sort(_state.Cpus, (a, b) => a.PlayerId.CompareTo(b.PlayerId));
                events.Add(new SimEvent(tick, SimEventKind.Switched, taken.Id, other: left.Id));
            }
        }
    }
}
