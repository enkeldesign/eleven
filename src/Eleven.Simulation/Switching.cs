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
        // played by the CPU.
        // Not written yet: no switch is taken.
        private bool MaySwitch(in PlayerState player, int target) => false;

        private void StepSwitches(long tick, List<SimEvent> events)
        {
        }
    }
}
