// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The live FREE ↔ ATTACHED switch (#60), at a step boundary. The returned simulator runs the
    /// same state under the same config with only the model changed. Nothing in the state is
    /// touched: positions, velocities, spin, cooldowns, a wind-up and an aftertouch window carry
    /// over, and so does the state hash. The new model acts from its first step, under its own
    /// eligibility; nothing is backdated. A recording or capture of the run starts a new segment at
    /// the switch, because each holds one config.
    /// </summary>
    public static class ControlSwitch
    {
        public static Simulator To(Simulator simulator, ControlModel model)
        {
            if (simulator == null) throw new ArgumentNullException(nameof(simulator));
            return new Simulator(simulator.Config.WithControlModel(model), simulator.PeekState());
        }
    }
}
