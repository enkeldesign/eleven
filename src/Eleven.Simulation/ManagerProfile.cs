namespace Eleven.Simulation
{
    /// <summary>
    /// A CPU manager's own knobs (#182): #127's, for one side. His controller's input, not state or
    /// config: his decisions are recorded commands (INV-10), so a replay needs no profile.
    /// <see cref="From"/> gives the config's, which every side used before.
    /// </summary>
    public readonly struct ManagerProfile
    {
        public ManagerProfile(bool tactics, double chaseFrom, double chaseMargin, double protectFrom, double spread, double subAfter, double subFatigue)
        {
            Tactics = tactics;
            ChaseFrom = chaseFrom;
            ChaseMargin = chaseMargin;
            ProtectFrom = protectFrom;
            Spread = spread;
            SubAfter = subAfter;
            SubFatigue = subFatigue;
        }

        /// <summary>Whether he switches plans and makes tactical substitutions (cpuTactics).</summary>
        public bool Tactics { get; }

        public double ChaseFrom { get; }
        public double ChaseMargin { get; }
        public double ProtectFrom { get; }

        /// <summary>How far, as a share of the match, his reading of the clock may be off (cpuTacticSpread).</summary>
        public double Spread { get; }

        public double SubAfter { get; }
        public double SubFatigue { get; }

        /// <summary>The config's knobs, as every side had them before #182.</summary>
        public static ManagerProfile From(SimConfig config) =>
            new ManagerProfile(config.CpuTactics >= 0.5, config.CpuChaseFrom, config.CpuChaseMargin, config.CpuProtectFrom, config.CpuTacticSpread, config.CpuSubAfter, config.CpuSubFatigue);
    }
}
