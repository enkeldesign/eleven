using System.Reflection;

namespace Eleven.Simulation
{
    /// <summary>Identity of the simulation build and of the conventions its evidence uses.</summary>
    public static class SimulationInfo
    {
        /// <summary>
        /// Version of the state schema. Bumped whenever the snapshot layout changes (INV-7).
        /// </summary>
        public const int StateSchemaVersion = 1;

        /// <summary>
        /// How ticks, commands and hashes line up. Recorded in snapshots and hash logs so evidence
        /// produced under a different convention can't be compared by mistake.
        /// </summary>
        public const string TickConvention =
            "tick-v1: state N is the state after N steps; step N applies the commands for tick N and produces state N+1; hash N is the hash of state N";

        /// <summary>
        /// The build that produced this simulation: the assembly's informational version, which the
        /// .NET SDK suffixes with the source commit. Hosts that don't stamp it (for example Unity,
        /// until #3 adds a build id) report "unknown".
        /// </summary>
        public static string BuildId { get; } =
            typeof(SimulationInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
    }
}
