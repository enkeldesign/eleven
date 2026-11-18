using System;
using System.Collections.Generic;
using Eleven.Simulation.Maths;
using Eleven.Simulation.Randomness;

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
    /// home λ = exp(μ + h + r_home − r_away), away λ = exp(μ − (r_home − r_away)), for the reference
    /// half of 180 s and scaled by the half's length. A side's rating is its tier's rating (the
    /// apprentice tier's is 0) plus a weight on its capabilities against the reference. Fouls and
    /// cards are Poisson a side.
    /// Its reference is fitted to the full simulation (docs/simulation/quicksim.md).
    /// </summary>
    public sealed class QuickSimModel
    {
        public QuickSimModel(double baseRate, double home, double[] tierRatings, double capabilityWeight, double rho,
            double foulsPerSide, double cautionsPerSide, double sendingsOffPerSide)
        {
            if (tierRatings == null || tierRatings.Length != Tiers) throw new ArgumentException($"A rating for each of the {Tiers} tiers.", nameof(tierRatings));
            BaseRate = baseRate;
            Home = home;
            _tierRatings = (double[])tierRatings.Clone();
            CapabilityWeight = capabilityWeight;
            Rho = rho;
            FoulsPerSide = foulsPerSide;
            CautionsPerSide = cautionsPerSide;
            SendingsOffPerSide = sendingsOffPerSide;
        }

        /// <summary>The fit to the calibration sample (docs/simulation/quicksim.md): `eleven-sim quicksim fit` reproduces it.</summary>
        public static QuickSimModel Reference { get; } = new QuickSimModel(
            baseRate: 0.185773, home: 0.182322, tierRatings: new[] { 0, 0.378836, 0.253575, 0.951047 }, capabilityWeight: -2.076960, rho: 0.199,
            foulsPerSide: 2.85, cautionsPerSide: 0.725, sendingsOffPerSide: 0.1);

        /// <summary>μ: the log of a side's goals in a match of reference halves, sides level.</summary>
        public double BaseRate { get; }

        /// <summary>h: home's advantage on the log scale.</summary>
        public double Home { get; }

        /// <summary>The CPU tiers: apprentice, club, pro, elite.</summary>
        public const int Tiers = 4;

        private readonly double[] _tierRatings;

        /// <summary>A tier's rating on the log scale of goals, apprentice 0 … elite 3; the apprentice tier's is 0.</summary>
        public double TierRating(int tier) => _tierRatings[tier < 0 ? 0 : tier >= Tiers ? Tiers - 1 : tier];

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
        public static double CapabilityIndex(PlayerCapabilities capabilities)
        {
            PlayerCapabilities reference = PlayerCapabilities.Reference;
            double sum = 0;
            foreach (TunableParameter p in PlayerCapabilities.Parameters)
            {
                double share = (capabilities.Get(p.Name) - reference.Get(p.Name)) / (p.Max - p.Min);
                sum += p.Name == "kickError" ? -share : share;
            }

            return sum / PlayerCapabilities.Parameters.Count;
        }

        /// <summary>A side's rating: its tier's rating plus the weighted capability index.</summary>
        public static double Rating(QuickSide side, QuickSimModel model) => model.TierRating(side.Tier) + model.CapabilityWeight * CapabilityIndex(side.Capabilities);

        /// <summary>The two sides' goal rates for a match with halves of <paramref name="halfSeconds"/>.</summary>
        public static (double Home, double Away) Rates(QuickSide home, QuickSide away, double halfSeconds, QuickSimModel model)
        {
            double gap = Rating(home, model) - Rating(away, model);
            double scale = halfSeconds / ReferenceHalf;
            return (scale * SimMath.Exp(model.BaseRate + model.Home + gap), scale * SimMath.Exp(model.BaseRate - gap));
        }

        // Dixon–Coles's τ: the low-score correction.
        private static double Tau(int x, int y, double lh, double la, double rho) =>
            x == 0 && y == 0 ? 1 - lh * la * rho
            : x == 0 && y == 1 ? 1 + lh * rho
            : x == 1 && y == 0 ? 1 + la * rho
            : x == 1 && y == 1 ? 1 - rho
            : 1;

        // Poisson's chances of 0 to MaxGoals at rate λ.
        private static double[] Poisson(double rate)
        {
            var p = new double[MaxGoals + 1];
            p[0] = SimMath.Exp(-rate);
            for (int k = 1; k <= MaxGoals; k++) p[k] = p[k - 1] * rate / k;
            return p;
        }

        // Every scoreline's chance over the grid, row by row (home goals), normalised.
        private static double[] Grid(double homeRate, double awayRate, double rho)
        {
            double[] ph = Poisson(homeRate), pa = Poisson(awayRate);
            var grid = new double[(MaxGoals + 1) * (MaxGoals + 1)];
            double sum = 0;
            for (int x = 0; x <= MaxGoals; x++)
            {
                for (int y = 0; y <= MaxGoals; y++)
                {
                    double v = Tau(x, y, homeRate, awayRate, rho) * ph[x] * pa[y];
                    grid[x * (MaxGoals + 1) + y] = v;
                    sum += v;
                }
            }

            for (int i = 0; i < grid.Length; i++) grid[i] /= sum;
            return grid;
        }

        /// <summary>The chance of the scoreline <paramref name="homeGoals"/>–<paramref name="awayGoals"/>, Dixon–Coles corrected and normalised over the drawn grid.</summary>
        public static double Probability(int homeGoals, int awayGoals, double homeRate, double awayRate, double rho) =>
            homeGoals < 0 || awayGoals < 0 || homeGoals > MaxGoals || awayGoals > MaxGoals ? 0 : Grid(homeRate, awayRate, rho)[homeGoals * (MaxGoals + 1) + awayGoals];

        // The draws' entities: what each uniform is for.
        private const int ScorelineDraw = 0, GoalTimeDraw = 1, FoulsDraw = 2, CautionsDraw = 3, SendingsOffDraw = 4;

        private static double Draw(ulong seed, int what, int entity) => CounterRng.Uniform(seed, 0, RandomStream.QuickSim, what, entity);

        // A Poisson count at `rate`, by its inverse distribution function.
        private static int Count(double rate, double u)
        {
            double p = SimMath.Exp(-rate), cumulative = p;
            int k = 0;
            while (u >= cumulative && k < 100)
            {
                k++;
                p *= rate / k;
                cumulative += p;
            }

            return k;
        }

        /// <summary>
        /// The result of a match between <paramref name="home"/> (team id <paramref name="homeTeam"/>)
        /// and <paramref name="away"/>, from <paramref name="seed"/> alone.
        /// </summary>
        public static MatchResult Play(SimConfig config, QuickSide home, QuickSide away, ulong seed, int homeTeam = 1, int awayTeam = 2, QuickSimModel? model = null)
        {
            QuickSimModel m = model ?? QuickSimModel.Reference;
            double half = config.HalfLength;
            (double lh, double la) = Rates(home, away, half, m);

            // The scoreline: one draw against the grid's cumulative chances.
            double[] grid = Grid(lh, la, m.Rho);
            double u = Draw(seed, ScorelineDraw, 0), cumulative = 0;
            int cell = grid.Length - 1;
            for (int i = 0; i < grid.Length; i++)
            {
                cumulative += grid[i];
                if (u < cumulative)
                {
                    cell = i;
                    break;
                }
            }

            int hg = cell / (MaxGoals + 1), ag = cell % (MaxGoals + 1);

            // Each goal at a uniform moment of the two halves, in the order they fall.
            var times = new List<(double At, int Team)>();
            for (int i = 0; i < hg + ag; i++) times.Add((Draw(seed, GoalTimeDraw, i) * 2 * half, i < hg ? homeTeam : awayTeam));
            times.Sort((a, b) => a.At.CompareTo(b.At));
            var goals = new ResultGoal[times.Count];
            for (int i = 0; i < times.Count; i++)
            {
                bool second = times[i].At >= half;
                goals[i] = new ResultGoal(times[i].Team, 0, second ? 2 : 1, second ? times[i].At - half : times[i].At);
            }

            double scale = half / ReferenceHalf;
            int Side(int what, int side, double perSide) => Count(scale * perSide, Draw(seed, what, side));
            return new MatchResult(homeTeam, awayTeam, hg, ag, half, 2 * half, goals, 0,
                homeFouls: Side(FoulsDraw, 0, m.FoulsPerSide), awayFouls: Side(FoulsDraw, 1, m.FoulsPerSide),
                homeCautions: Side(CautionsDraw, 0, m.CautionsPerSide), awayCautions: Side(CautionsDraw, 1, m.CautionsPerSide),
                homeSendingsOff: Side(SendingsOffDraw, 0, m.SendingsOffPerSide), awaySendingsOff: Side(SendingsOffDraw, 1, m.SendingsOffPerSide));
        }
    }
}
