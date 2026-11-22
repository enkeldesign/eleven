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
        public QuickSide(double tier, PlayerCapabilities capabilities)
        {
            Tier = tier;
            Capabilities = capabilities;
        }

        /// <summary>The side's CPU skill as a tier: apprentice 0, club 1, pro 2, elite 3, or anywhere between (#163).</summary>
        public double Tier { get; }

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
            double foulsPerSide, double cautionsPerSide, double sendingsOffPerSide, double shootoutConversion = ReferenceShootoutConversion)
        {
            ShootoutConversion = shootoutConversion;
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
            baseRate: 0.157396, home: 0.025975, tierRatings: new[] { 0, 0.230584, 0.243057, 0.706305 }, capabilityWeight: 9.166028, rho: -0.014,
            foulsPerSide: 1.846591, cautionsPerSide: 0.4375, sendingsOffPerSide: 0.074811);

        /// <summary>
        /// The fit at eleven a side (#163, docs/simulation/quicksim.md): the reference's ratings
        /// scaled, and μ, h, ρ and the foul and card means refitted to the full simulation's
        /// 11v11 matches between the world's clubs. `eleven-sim quicksim career-fit` reproduces it.
        /// </summary>
        public static QuickSimModel ElevenASide { get; } = Reference;

        /// <summary>The model for a match of <paramref name="perSide"/> outfielders a side: eleven a side's, else the reference.</summary>
        public static QuickSimModel For(int perSide) => perSide >= 10 ? ElevenASide : Reference;

        /// <summary>
        /// A shootout kick's chance of a goal (#163): the full simulation's, from #122's penalty
        /// benchmark at eleven a side, 160 shootouts: 1,450 of 1,875 kicks scored (77.3 %, ± 1.0 %).
        /// </summary>
        public const double ReferenceShootoutConversion = 0.773;

        /// <summary>A shootout kick's chance of a goal.</summary>
        public double ShootoutConversion { get; }

        /// <summary>μ: the log of a side's goals in a match of reference halves, sides level.</summary>
        public double BaseRate { get; }

        /// <summary>h: home's advantage on the log scale.</summary>
        public double Home { get; }

        /// <summary>The CPU tiers: apprentice, club, pro, elite.</summary>
        public const int Tiers = 4;

        private readonly double[] _tierRatings;

        /// <summary>A tier's rating on the log scale of goals, apprentice 0 … elite 3; the apprentice tier's is 0.</summary>
        /// <summary>
        /// A tier's rating: each whole tier's own, and between two tiers the line between their
        /// ratings (#163), so a whole tier rates exactly as before. Below apprentice or above
        /// elite, theirs.
        /// </summary>
        public double TierRating(double tier)
        {
            if (!(tier > 0)) return _tierRatings[0];
            if (tier >= Tiers - 1) return _tierRatings[Tiers - 1];
            int below = (int)tier;
            double share = tier - below;
            return share == 0 ? _tierRatings[below] : _tierRatings[below] + share * (_tierRatings[below + 1] - _tierRatings[below]);
        }

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

        /// <summary>Goals a side are drawn up to at least this; the grid grows with the rate (<see cref="Limit"/>).</summary>
        public const int MaxGoals = 12;

        /// <summary>The Poisson tail a side's grid leaves out: past its limit, less than this.</summary>
        public const double Tail = 1e-12;

        /// <summary>
        /// Goals a side a match: the most a rate is taken as. The calibrated rates are a few goals a
        /// match; this keeps the sampler's maths in range for any valid sides, however far the fit's
        /// ratings extrapolate (Codex's finding on #150).
        /// </summary>
        public const double MaxRate = 30;

        /// <summary>The goals a side's grid holds at <paramref name="rate"/>: at least <see cref="MaxGoals"/>, and enough that the tail beyond is under <see cref="Tail"/>.</summary>
        public static int Limit(double rate)
        {
            double p = SimMath.Exp(-rate), cumulative = p;
            int k = 0;
            while ((k < MaxGoals || 1 - cumulative >= Tail) && k < 400)
            {
                k++;
                p *= rate / k;
                cumulative += p;
            }

            return k;
        }

        /// <summary>
        /// ρ within Dixon–Coles's bounds for these rates, so every τ is non-negative:
        /// max(−1/λ_h, −1/λ_a) ≤ ρ ≤ min(1/(λ_h·λ_a), 1).
        /// </summary>
        public static double BoundedRho(double rho, double homeRate, double awayRate)
        {
            double low = homeRate > 0 && awayRate > 0 ? -Math.Min(1 / homeRate, 1 / awayRate) : 0;
            double high = homeRate * awayRate > 0 ? Math.Min(1 / (homeRate * awayRate), 1) : 1;
            return rho < low ? low : rho > high ? high : rho;
        }

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

        /// <summary>The two sides' goal rates for a match with halves of <paramref name="halfSeconds"/>, each at most <see cref="MaxRate"/>.</summary>
        public static (double Home, double Away) Rates(QuickSide home, QuickSide away, double halfSeconds, QuickSimModel model)
        {
            double gap = Rating(home, model) - Rating(away, model);
            double scale = halfSeconds / ReferenceHalf;
            return (Rate(model.BaseRate + model.Home + gap, scale), Rate(model.BaseRate - gap, scale));
        }

        // The log-rate's exponent is bounded before Exp, then the rate at MaxRate: past the
        // calibrated ratings the model extrapolates, and the bound keeps its maths in range.
        private const double LogBound = 40;

        private static double Rate(double logRate, double scale)
        {
            double bounded = logRate < -LogBound ? -LogBound : logRate > LogBound ? LogBound : logRate;
            double rate = scale * SimMath.Exp(bounded);
            return rate > MaxRate ? MaxRate : rate;
        }

        // Dixon–Coles's τ: the low-score correction.
        private static double Tau(int x, int y, double lh, double la, double rho) =>
            x == 0 && y == 0 ? 1 - lh * la * rho
            : x == 0 && y == 1 ? 1 + lh * rho
            : x == 1 && y == 0 ? 1 + la * rho
            : x == 1 && y == 1 ? 1 - rho
            : 1;

        // Poisson's chances of 0 to `limit` at rate λ.
        private static double[] Poisson(double rate, int limit)
        {
            var p = new double[limit + 1];
            p[0] = SimMath.Exp(-rate);
            for (int k = 1; k <= limit; k++) p[k] = p[k - 1] * rate / k;
            return p;
        }

        // Every scoreline's chance over the grid, row by row (home goals), normalised; its row width.
        private static double[] Grid(double homeRate, double awayRate, double rho, out int width)
        {
            int nh = Limit(homeRate), na = Limit(awayRate);
            double r = BoundedRho(rho, homeRate, awayRate);
            double[] ph = Poisson(homeRate, nh), pa = Poisson(awayRate, na);
            width = na + 1;
            var grid = new double[(nh + 1) * width];
            double sum = 0;
            for (int x = 0; x <= nh; x++)
            {
                for (int y = 0; y <= na; y++)
                {
                    double v = Tau(x, y, homeRate, awayRate, r) * ph[x] * pa[y];
                    grid[x * width + y] = v;
                    sum += v;
                }
            }

            for (int i = 0; i < grid.Length; i++) grid[i] /= sum;
            return grid;
        }

        /// <summary>
        /// The chance of the scoreline <paramref name="homeGoals"/>–<paramref name="awayGoals"/>: Dixon–Coles
        /// corrected, ρ bounded for these rates, normalised over a grid that holds all but a
        /// <see cref="Tail"/> of each side's goals.
        /// </summary>
        public static double Probability(int homeGoals, int awayGoals, double homeRate, double awayRate, double rho)
        {
            double[] grid = Grid(homeRate, awayRate, rho, out int width);
            int rows = grid.Length / width;
            return homeGoals < 0 || awayGoals < 0 || homeGoals >= rows || awayGoals >= width ? 0 : grid[homeGoals * width + awayGoals];
        }

        // The draws' entities: what each uniform is for. A knockout's extra time and shootout
        // (#163) draw on their own, so a league match's draws, and its result, never move.
        private const int ScorelineDraw = 0, GoalTimeDraw = 1, FoulsDraw = 2, CautionsDraw = 3, SendingsOffDraw = 4;
        private const int ExtraScorelineDraw = 5, ExtraGoalTimeDraw = 6, ShootoutDraw = 7;

        // A sudden death this long has a chance of about 10^-99 at the reference conversion; past it
        // the home side is given the shootout, so a knockout always ends.
        private const int MaxShootoutKicks = 1000;

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

            // The scoreline, and each goal at a uniform moment of the two halves.
            (int hg, int ag) = Scoreline(seed, ScorelineDraw, lh, la, m.Rho);
            var goals = new List<ResultGoal>();
            AddGoals(goals, seed, GoalTimeDraw, hg, ag, half, 1, homeTeam, awayTeam);

            // A knockout level after two halves (#163): extra time at the same rates, two halves of
            // extraTimeShare of a half; still level, a shootout. As the full simulation plays it (#111).
            int extraTime = 0, homeKicks = 0, awayKicks = 0, homeScored = 0, awayScored = 0;
            double played = 2 * half;
            if (config.Knockout >= 0.5 && hg == ag)
            {
                extraTime = 1;
                double extraHalf = half * config.ExtraTimeShare;
                (double eh, double ea) = Rates(home, away, extraHalf, m);
                (int ehg, int eag) = Scoreline(seed, ExtraScorelineDraw, eh, ea, m.Rho);
                AddGoals(goals, seed, ExtraGoalTimeDraw, ehg, eag, extraHalf, 3, homeTeam, awayTeam);
                hg += ehg;
                ag += eag;
                played += 2 * extraHalf;
                if (hg == ag) (homeKicks, homeScored, awayKicks, awayScored) = Shootout(seed, (int)config.ShootoutKicks, m.ShootoutConversion);
            }

            double scale = half / ReferenceHalf;
            int Side(int what, int side, double perSide) => Count(scale * perSide, Draw(seed, what, side));
            return new MatchResult(homeTeam, awayTeam, hg, ag, half, played, goals.ToArray(), 0,
                homeFouls: Side(FoulsDraw, 0, m.FoulsPerSide), awayFouls: Side(FoulsDraw, 1, m.FoulsPerSide),
                homeCautions: Side(CautionsDraw, 0, m.CautionsPerSide), awayCautions: Side(CautionsDraw, 1, m.CautionsPerSide),
                homeSendingsOff: Side(SendingsOffDraw, 0, m.SendingsOffPerSide), awaySendingsOff: Side(SendingsOffDraw, 1, m.SendingsOffPerSide),
                extraTime: extraTime, homeShootoutKicks: homeKicks, awayShootoutKicks: awayKicks, homeShootoutGoals: homeScored, awayShootoutGoals: awayScored);
        }

        // A scoreline: one draw against the grid's cumulative chances.
        private static (int Home, int Away) Scoreline(ulong seed, int what, double homeRate, double awayRate, double rho)
        {
            double[] grid = Grid(homeRate, awayRate, rho, out int width);
            double u = Draw(seed, what, 0), cumulative = 0;
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

            return (cell / width, cell % width);
        }

        // Each goal at a uniform moment of two halves of `halfSeconds`, the first of them
        // `firstHalf`, in the order they fall.
        private static void AddGoals(List<ResultGoal> goals, ulong seed, int what, int home, int away, double halfSeconds, int firstHalf, int homeTeam, int awayTeam)
        {
            var times = new List<(double At, int Team)>();
            for (int i = 0; i < home + away; i++) times.Add((Draw(seed, what, i) * 2 * halfSeconds, i < home ? homeTeam : awayTeam));
            times.Sort((a, b) => a.At.CompareTo(b.At));
            foreach ((double at, int team) in times)
            {
                bool second = at >= halfSeconds;
                goals.Add(new ResultGoal(team, 0, second ? firstHalf + 1 : firstHalf, second ? at - halfSeconds : at));
            }
        }

        // A shootout (#163): the home side first, then in turn, each kick in at `conversion`. Over,
        // as in the full simulation (#111), once one side can't be caught in the first `each`
        // kicks, or after them when both have taken as many and one has scored more.
        private static (int HomeKicks, int HomeGoals, int AwayKicks, int AwayGoals) Shootout(ulong seed, int each, double conversion)
        {
            int hk = 0, hs = 0, ak = 0, @as = 0;
            for (int kick = 0; kick < MaxShootoutKicks; kick++)
            {
                bool scored = Draw(seed, ShootoutDraw, kick) < conversion;
                if (kick % 2 == 0)
                {
                    hk++;
                    if (scored) hs++;
                }
                else
                {
                    ak++;
                    if (scored) @as++;
                }

                bool decided = hk <= each && ak <= each && !(hk == each && ak == each)
                    ? hs > @as + (each - ak) || @as > hs + (each - hk)
                    : hk == ak && hs != @as;
                if (decided) return (hk, hs, ak, @as);
            }

            return (hk, hs + 1, ak, @as);
        }
    }
}
