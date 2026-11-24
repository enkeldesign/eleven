using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A manager's memory of opponents (#191): each meeting his club plays remembered from its
    /// result, and what the meetings he remembers with an opponent do to his plans for the next.
    /// Only head-to-heads, and only the result contract's facts (#80's perception boundary).
    /// </summary>
    public static class ManagerMemory
    {
        /// <summary>
        /// Both managers of fixture <paramref name="fixture"/> remember it from
        /// <paramref name="result"/>, whose home side is the fixture's: each from his own side, the
        /// latest <see cref="MemoryRules.Depth"/> of each opponent kept.
        /// </summary>
        public static void Remember(World world, Season season, Fixture fixture, MatchResult result, MemoryRules? rules = null)
        {
        }

        /// <summary>His meetings with <paramref name="opponent"/>, oldest first.</summary>
        public static List<Meeting> With(Manager manager, int opponent) => new List<Meeting>();

        /// <summary>
        /// What his meetings with <paramref name="opponent"/> do to his plans: his line's shift in
        /// metres, his step-up's, and how much earlier he protects, as a share of the match. All 0
        /// with no memory of them.
        /// </summary>
        public static (double Line, double StepUp, double ProtectEarlier) Shift(Manager manager, int opponent, MemoryRules? rules = null) => (0, 0, 0);
    }
}
