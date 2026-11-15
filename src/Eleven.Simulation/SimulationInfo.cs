using System.Reflection;

namespace Eleven.Simulation
{
    /// <summary>Identity of the simulation build and of the conventions its evidence uses.</summary>
    public static class SimulationInfo
    {
        /// <summary>
        /// Version of the state schema. Bumped whenever the snapshot layout changes (INV-7).
        /// Version 2 dropped the maths backend from the header (ADR-0002). Version 3 added each
        /// player's capabilities (#36). Version 4 added kick capabilities and the kick's wind-up and
        /// aftertouch phases (#37). Version 5 added each player's team, pass capabilities and quick-pass
        /// target (#38). Version 6 added the touch cooldown (#59), version 7 the ball's attachment
        /// (#60), and version 8 replaced the ball's beyond-the-goal-line flag with its goal-line
        /// state (#68). Version 9 added the keepers and the held ball (#69). Version 10 added each
        /// player's turn with the ball (#66), version 11 their cut with it (#75),
        /// version 12 the two sides of a match and the way each attacks (#82),
        /// version 13 the CPU outfielders, their plans and their timers (#83),
        /// version 14 the standing challenge: each player's tackling, phase and line, and who last played the ball (#84),
        /// version 15 the restart sequencer (#85), version 16 the match's clock, halves and score (#86),
        /// and version 17 each player's heading (#94).
        /// </summary>
        public const int StateSchemaVersion = 17;

        /// <summary>
        /// How ticks, commands and hashes line up. Recorded in snapshots and hash logs so evidence
        /// produced under a different convention can't be compared by mistake.
        /// </summary>
        public const string TickConvention =
            "tick-v1: state N is the state after N steps; step N applies the commands for tick N and produces state N+1; hash N is the hash of state N";

        /// <summary>
        /// The build that produced this simulation: the assembly's informational version, which the
        /// .NET SDK suffixes with the source commit. Unity doesn't stamp it, so Unity builds report
        /// "unknown"; the Unity host shows its own build id.
        /// </summary>
        public static string BuildId { get; } =
            typeof(SimulationInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
    }
}
