using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>One match of #177's condition sample: a fixture, its home side's fatigue at kick-off, and the score.</summary>
    public sealed class ConditionMatch
    {
        public int Index;
        public double HomeFatigue;
        public int Home, Away, HomeGoals, AwayGoals;
    }

    /// <summary>
    /// A tired start measured (#177): the eleven-a-side sample's fixtures (#163) on the full
    /// simulation, the home side's outfielders starting at each of a few fatigue levels and the
    /// away side fresh; and the quick-sim's fatigue weight fitted to it.
    /// </summary>
    public static class CareerConditionCalibration
    {
        /// <summary>The home side's fatigue levels at kick-off.</summary>
        public static readonly double[] Levels = { 0, 0.3, 0.6 };

        /// <summary>Every <see cref="Step"/>th fixture of the eleven-a-side sample.</summary>
        public const int Step = 3;

        private const string Header = "index,home_fatigue,home,away,home_goals,away_goals";

        public static ConditionMatch Play(SimConfig config, World world, (int Home, int Away, ulong Seed) fixture, int index, double homeFatigue) => new ConditionMatch();

        public static void Write(TextWriter w, IReadOnlyList<ConditionMatch> sample)
        {
        }

        public static List<ConditionMatch> Read(IEnumerable<string> lines) => new List<ConditionMatch>();

        /// <summary>
        /// The fatigue weight w by Poisson maximum likelihood over the sample, every other value the
        /// model's: the home side's rating less w × its fatigue. With its standard error.
        /// </summary>
        public static (double Weight, double Error) Fit(World world, IReadOnlyList<ConditionMatch> sample, QuickSimModel model) => (0, 0);
    }
}
