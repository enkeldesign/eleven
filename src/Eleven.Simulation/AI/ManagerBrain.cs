namespace Eleven.Simulation.AI
{
    /// <summary>
    /// A CPU side's manager (#110): pure and stateless, he decides on his
    /// <see cref="ManagerObservation"/> alone (INV-9). Code in this namespace can't name the
    /// simulation's state (ELEVEN003). He asks for a substitution; the simulation decides whether
    /// it is made (INV-12). An injured footballer at once, then, late in the match, the most tired
    /// (docs/simulation/substitutions.md). With cpuTactics, he also switches plans by the score, the
    /// clock and the men on the pitch, and makes tactical substitutions (#127, docs/simulation/managers.md).
    /// </summary>
    public static class ManagerBrain
    {
        /// <summary>What he asks for now: a substitution (who goes off and who comes on, or none) and a plan to switch to (or none).</summary>
        /// <summary>What he asks for by his own profile (#182).</summary>
        public static ManagerDecision Decide(ManagerObservation seen, SimConfig config, ManagerProfile profile) => Decide(seen, config);

        public static ManagerDecision Decide(ManagerObservation seen, SimConfig config)
        {
            bool tactics = config.CpuTactics >= 0.5;
            byte to = tactics ? Plan(seen, config) : (byte)0;
            (int off, int on) = Substitution(seen, config, tactics);
            return new ManagerDecision(off, on, to);
        }

        private const int Balanced = 0, Chase = 1, Protect = 2;

        // A tactical substitution's least fatigue, as a share of cpuSubFatigue (#127).
        private const double FreshShare = 0.5;

        // The plan he wants (#127), 1–3, or 0 for the one he has or none he may take. His plans are
        // balanced, chase and protect, by convention; his spread shifts when he reacts.
        private static byte Plan(ManagerObservation seen, SimConfig config)
        {
            if (!seen.MaySwitch) return 0;
            int margin = seen.GoalsFor - seen.GoalsAgainst;
            double share = seen.MatchShare - seen.Spread;
            int want = Balanced;
            if (-margin >= config.CpuChaseMargin && share >= config.CpuChaseFrom) want = Chase;
            else if (margin > 0 && share >= config.CpuProtectFrom) want = Protect;
            else if (margin >= 0 && seen.OnPitch < seen.TheirsOnPitch) want = Protect;
            return want == seen.Plan ? (byte)0 : (byte)(want + 1);
        }

        // While chasing, his most tired forward; while protecting, his most tired midfielder (#127):
        // from cpuSubAfter, fresh legs where the plan needs them, for legs that are tiring.
        private static int Tactical(ManagerObservation seen, SimConfig config)
        {
            if (seen.MatchShare < config.CpuSubAfter) return 0;
            ManagedLine line = seen.Plan == Chase ? ManagedLine.Forward : seen.Plan == Protect ? ManagedLine.Middle : ManagedLine.None;
            if (line == ManagedLine.None) return 0;
            // Only a footballer at least half as tired as #110's threshold: never fresh legs for fresh legs.
            int off = 0;
            double most = config.CpuSubFatigue * FreshShare;
            foreach (ManagedPlayer p in seen.Players)
            {
                if (p.Keeper || p.Line != line || p.Injury != InjuryLevel.None || p.Fatigue < most || (p.Fatigue == most && off != 0)) continue;
                most = p.Fatigue;
                off = p.Id;
            }

            return off;
        }

        // The substitution of #110: the injured at once, then late on the most tired.
        private static (int Off, int On) Substitution(ManagerObservation seen, SimConfig config, bool tactics)
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

            // A tactical substitution (#127), then late in the match the most tired outfielder, if he is tired enough.
            int tactical = tactics ? Tactical(seen, config) : 0;
            if (tactical != 0) return (tactical, on);
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
