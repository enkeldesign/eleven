namespace Eleven.Simulation.AI
{
    /// <summary>
    /// A CPU side's manager (#110): pure and stateless, he decides on his
    /// <see cref="ManagerObservation"/> alone (INV-9). Code in this namespace can't name the
    /// simulation's state (ELEVEN003). He asks for a substitution; the simulation decides whether
    /// it is made (INV-12). A simple policy, not tactics (M5): an injured footballer at once, then,
    /// late in the match, the most tired. docs/simulation/substitutions.md.
    /// </summary>
    public static class ManagerBrain
    {
        /// <summary>The substitution he asks for now: who goes off and who comes on, or (0, 0) for none.</summary>
        public static (int Off, int On) Decide(ManagerObservation seen, SimConfig config)
        {
            return (0, 0);
        }
    }
}
