using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>
    /// Training's knobs (#196), all technical references, not tuned feel. UEFA's Elite Club Injury
    /// Study (Ekstrand, Hägglund and Waldén 2011) measures about 3–4 injuries per 1,000 hours of
    /// training, most of them minor: about 0.004 a footballer a training day of an hour and a
    /// quarter. Gabbett (2016): heavier training builds players, and brings injuries.
    /// </summary>
    public sealed class TrainingRules
    {
        /// <summary>Development's pace (#194) by intensity: Light, Normal, Hard.</summary>
        public double[] Growth = { 0.8, 1.0, 1.25 };

        /// <summary>Recovery's pace (#177) by intensity.</summary>
        public double[] Recovery = { 1.2, 1.0, 0.8 };

        /// <summary>Training injuries' rate by intensity, times <see cref="InjuryPerDay"/>.</summary>
        public double[] Injury = { 0.6, 1.0, 1.5 };

        /// <summary>A fit footballer's chance of an injury in a day's Normal training.</summary>
        public double InjuryPerDay = 0.004;

        /// <summary>Auto: Light below this XI's mean condition; Hard from <see cref="HardFrom"/> with <see cref="HardGapDays"/> or more to the next match.</summary>
        public double LightBelow = 85, HardFrom = 97;

        public int HardGapDays = 7;

        public static TrainingRules Reference => new TrainingRules();
    }

    /// <summary>A footballer injured in training (#196): who, and his days out.</summary>
    public readonly struct TrainingInjury
    {
        public readonly int Player, Days;

        public TrainingInjury(int player, int days)
        {
            Player = player;
            Days = days;
        }
    }

    /// <summary>
    /// Training (#196): each club's intensity sets, between matches, how fast its footballers
    /// develop and recover, and how often they're injured in training.
    /// </summary>
    public static class Training
    {
        /// <summary>The intensity a manager chooses on Auto: by his XI's mean condition at its last kick-off and the days to his next match.</summary>
        public static TrainingIntensity Choose(double xiCondition, int gapDays, TrainingRules? rules = null) => TrainingIntensity.Normal;

        /// <summary>An intensity's row of a knob: Light, Normal or Hard; Auto reads as Normal.</summary>
        public static double Of(double[] row, TrainingIntensity intensity) => 1;

        /// <summary>
        /// The <paramref name="days"/> to the next matchday: each club's footballers recover by its
        /// intensity, the injured heal, and each fit footballer may be injured in training, his
        /// layoff in the minor bands (#176). Returns those injured.
        /// </summary>
        public static List<TrainingInjury> Pass(World world, Season season, int days, IReadOnlyDictionary<int, TrainingIntensity> intensity, SquadRules squad, TrainingRules? rules = null)
        {
            Availability.Pass(world, days, squad);
            return new List<TrainingInjury>();
        }
    }
}
