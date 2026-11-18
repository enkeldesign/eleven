using System;
using System.Collections.Generic;

namespace Eleven.Simulation
{
    /// <summary>
    /// A side as the quick-sim reads it (#129): its CPU tier, 0 (apprentice) to 3 (elite), and its
    /// outfielders' capabilities. Nothing else of a match: no state is shared with one played.
    /// </summary>
    public readonly struct QuickSide
    {
        public QuickSide(int tier, PlayerCapabilities capabilities)
        {
            Tier = tier;
            Capabilities = capabilities;
        }

        public int Tier { get; }

        public PlayerCapabilities Capabilities { get; }
    }

    /// <summary>
    /// The quick-sim's model (#129, ADR 0004): Dixon–Coles. Each side's goals are Poisson; a low-score
    /// correction ρ adjusts 0–0, 1–0, 0–1 and 1–1. The rates read the sides' ratings:
    /// home λ = exp(μ + h + k·(r_home − r_away)), away λ = exp(μ − k·(r_home − r_away)), for the
    /// reference half of 180 s and scaled by the half's length. A side's rating is its tier plus a
    /// weight on its capabilities against the reference. Fouls and cards are Poisson a side.
    /// Its reference is fitted to the full simulation (docs/simulation/quicksim.md).
    /// </summary>
    public sealed class QuickSimModel
    {
        public QuickSimModel(double baseRate, double home, double strength, double capabilityWeight, double rho,
            double foulsPerSide, double cautionsPerSide, double sendingsOffPerSide)
        {
            BaseRate = baseRate;
            Home = home;
            Strength = strength;
            CapabilityWeight = capabilityWeight;
            Rho = rho;
            FoulsPerSide = foulsPerSide;
            CautionsPerSide = cautionsPerSide;
            SendingsOffPerSide = sendingsOffPerSide;
        }

        public static QuickSimModel Reference { get; } = new QuickSimModel(0, 0, 0, 0, 0, 0, 0, 0);

        /// <summary>μ: the log of a side's goals in a match of reference halves, sides level.</summary>
        public double BaseRate { get; }

        /// <summary>h: home's advantage on the log scale.</summary>
        public double Home { get; }

        /// <summary>k: the log-rate per point of rating between the sides.</summary>
        public double Strength { get; }

        /// <summary>The rating per unit of the capability index (<see cref="QuickSim.CapabilityIndex"/>).</summary>
        public double CapabilityWeight { get; }

        /// <summary>ρ: Dixon–Coles's low-score correction.</summary>
        public double Rho { get; }

        public double FoulsPerSide { get; }

        public double CautionsPerSide { get; }

        public double SendingsOffPerSide { get; }
    }

    /// <summary>
    /// The quick-sim (#129): a match nobody plays, resolved from its sides' data and a seed into a
    /// <see cref="MatchResult"/> (INV-14). The one approximation INV-1 permits, calibrated against
    /// the simulation. Deterministic: counter-based draws on <see cref="Randomness.RandomStream.QuickSim"/>
    /// and the portable maths (INV-4, INV-6).
    /// </summary>
    public static class QuickSim
    {
        /// <summary>s. The half the model's rates are for.</summary>
        public const double ReferenceHalf = 180;

        /// <summary>Goals a side are drawn up to this; the tail beyond it is negligible at these rates.</summary>
        public const int MaxGoals = 12;

        /// <summary>
        /// A side's capabilities against the reference: the mean, over every capability, of its
        /// difference from the reference as a share of the capability's range, kick error counted
        /// the other way round (less is better).
        /// </summary>
        public static double CapabilityIndex(PlayerCapabilities capabilities) => 0;

        /// <summary>A side's rating: its tier plus the weighted capability index.</summary>
        public static double Rating(QuickSide side, QuickSimModel model) => 0;

        /// <summary>The two sides' goal rates for a match with halves of <paramref name="halfSeconds"/>.</summary>
        public static (double Home, double Away) Rates(QuickSide home, QuickSide away, double halfSeconds, QuickSimModel model) => (0, 0);

        /// <summary>The chance of the scoreline <paramref name="homeGoals"/>–<paramref name="awayGoals"/>, Dixon–Coles corrected and normalised over the drawn grid.</summary>
        public static double Probability(int homeGoals, int awayGoals, double homeRate, double awayRate, double rho) => 0;

        /// <summary>
        /// The result of a match between <paramref name="home"/> (team id <paramref name="homeTeam"/>)
        /// and <paramref name="away"/>, from <paramref name="seed"/> alone.
        /// </summary>
        public static MatchResult Play(SimConfig config, QuickSide home, QuickSide away, ulong seed, int homeTeam = 1, int awayTeam = 2, QuickSimModel? model = null) =>
            new MatchResult(homeTeam, awayTeam, 0, 0, config.HalfLength, 0, Array.Empty<ResultGoal>(), 0);
    }
}
