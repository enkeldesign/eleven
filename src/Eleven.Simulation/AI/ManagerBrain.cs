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
            if (!seen.MaySubstitute || seen.Bench.Count == 0) return (0, 0);
            int on = seen.Bench[0];

            // Taken off injured first, then hurt and playing on: the lowest id of each.
            foreach (ManagedPlayer p in seen.Players)
            {
                if (p.Injury == InjuryLevel.Serious) return (p.Id, on);
            }

            foreach (ManagedPlayer p in seen.Players)
            {
                if (p.Injury == InjuryLevel.Minor) return (p.Id, on);
            }

            // Late in the match, the most tired outfielder, if he is tired enough.
            if (seen.MatchShare < config.CpuSubAfter) return (0, 0);
            int off = 0;
            double most = config.CpuSubFatigue;
            foreach (ManagedPlayer p in seen.Players)
            {
                if (p.Keeper || p.Fatigue < most || (p.Fatigue == most && off != 0)) continue;
                most = p.Fatigue;
                off = p.Id;
            }

            return off != 0 ? (off, on) : (0, 0);
        }
    }
}
