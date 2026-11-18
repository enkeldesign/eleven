using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The three plans a side stores in the host's matches (#128), each with the name its dashboard
    /// gives it: #127's balanced (the reference), chase (#126's high press) and protect (#126's low
    /// block). A side starts on the balanced plan, so a match that never switches plays as before.
    /// </summary>
    public static class MatchPlans
    {
        /// <summary>The plans, in their stored order: 1, 2 and 3 in a switch.</summary>
        public static TeamTactic[] Plans => new[] { TeamTactic.Reference(0), TacticTrials.HighPress, TacticTrials.LowBlock };

        /// <summary>Each plan's name, in the same order.</summary>
        public static string[] Names => new[] { "Balanced", "High press", "Low block" };
    }
}
