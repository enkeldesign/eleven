using System.Collections.Generic;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Training measured (#196): a career rolled forward headless with every club at one
    /// intensity, for each in turn, then all on Auto: the young's growth, the condition at
    /// kick-off, training injuries and the days they cost, and the world's drift.
    /// </summary>
    public static class CareerTraining
    {
        /// <summary>One intensity's seasons, every club at it.</summary>
        public sealed class Run
        {
            public TrainingIntensity Intensity;
            public double YoungGrowth, KickOffCondition, InjuriesPerClubSeason, DaysPerClubSeason, Drift;
        }

        /// <summary>Each intensity in turn, then Auto, over <paramref name="seasons"/> seasons from <paramref name="seed"/>.</summary>
        public static List<Run> Measure(ulong seed, int seasons) => new List<Run>();

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons) => "";
    }
}
