using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.AI
{
    /// <summary>
    /// A CPU outfielder's brain (#83): pure and stateless, it decides on his
    /// <see cref="OutfieldObservation"/> alone (INV-9), in his own side's frame, attacking +x. It
    /// reads the ball's path with the simulation's own <see cref="BallFlight"/> (INV-13). Code in
    /// this namespace can't name the simulation's state (ELEVEN003). It asks for a plan; the
    /// simulation decides when he may make one and when it takes effect (INV-12), and his body does
    /// what any footballer's does with the same stick and kick. docs/simulation/cpu.md has the rule.
    /// </summary>
    public static class OutfieldBrain
    {
        // Not written yet: he asks for nothing.
        public static CpuPlan Decide(OutfieldObservation seen, SimConfig config) => new CpuPlan(CpuMode.None, Vec2.Zero);
    }
}
