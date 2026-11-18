using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The quick-sim's calibration sample (#129, grown in #151): the full simulation at eleven a
    /// side over the calibration league, <see cref="Sides"/>, every footballer a CPU footballer at
    /// his side's tier. Each ordered pair plays once a round, for <see cref="Rounds"/> rounds, on
    /// fixed seeds. The quick-sim is fitted to it and compared with it. Each fixture is a whole,
    /// independent, deterministic match: a runner may play them in any order, or at once, and
    /// writes them in their fixed order.
    /// </summary>
    public static class QuickSimCalibration
    {
        /// <summary>Rounds of the sample: every ordered pair once a round.</summary>
        public const int Rounds = 4;

        /// <summary>The most rounds <see cref="Fixtures"/> gives: each pairing has 16 seeds, all distinct.</summary>
        public const int MaxRounds = 16;

        public const ulong FirstSeed = 0x129000000UL;

        /// <summary>
        /// #151: the capability levels of the calibration sides, on the capability index
        /// (<see cref="QuickSim.CapabilityIndex"/>): every capability moved by this share of its range.
        /// </summary>
        public static readonly double[] CapabilityLevels = { -0.05, 0, 0.05 };

        /// <summary>#151: the fixtures CI plays and diffs against the reference's first rows, within its budget.</summary>
        public const int CiFixtures = 60;

        // The calibration league's names, invented, by tier and then capability level.
        private static readonly string[] Names =
        {
            "Saltmarsh", "Harbour", "Quarrytown",
            "Fenwick", "Millbrook", "Stonegate",
            "Ashby", "Riverside", "Northfield",
            "Larkhill", "Kestrel", "Highmoor",
        };

        /// <summary>
        /// #151: the calibration league, each CPU tier at each capability level, weakest first. A
        /// side's every capability is the reference moved by its level's share of the capability's
        /// range (kick error the other way), held within the range: tier and capabilities vary
        /// independently, so the fit can tell them apart.
        /// </summary>
        public static IReadOnlyList<LeagueSide> Sides()
        {
            CpuSkill[] skills = { CpuSkill.Apprentice, CpuSkill.Club, CpuSkill.Pro, CpuSkill.Elite };
            string[] tiers = { "apprentice", "club", "pro", "elite" };
            var sides = new List<LeagueSide>();
            for (int t = 0; t < skills.Length; t++)
            {
                for (int l = 0; l < CapabilityLevels.Length; l++)
                {
                    PlayerCapabilities c = PlayerCapabilities.Reference;
                    foreach (TunableParameter p in PlayerCapabilities.Parameters)
                    {
                        double moved = c.Get(p.Name) + (p.Name == "kickError" ? -1 : 1) * CapabilityLevels[l] * (p.Max - p.Min);
                        c = c.With(p.Name, moved < p.Min ? p.Min : moved > p.Max ? p.Max : moved);
                    }

                    sides.Add(new LeagueSide(Names[t * CapabilityLevels.Length + l], tiers[t], skills[t], c));
                }
            }

            return sides;
        }

        /// <summary>
        /// The sample's fixtures, in their fixed order: round by round, each round every ordered
        /// pair once, so any prefix spreads over the pairs. Home, away and seed; at most 16 rounds.
        /// </summary>
        public static List<(int Home, int Away, ulong Seed)> Fixtures(int sides, int rounds = Rounds)
        {
            var fixtures = new List<(int Home, int Away, ulong Seed)>();
            for (int r = 0; r < rounds; r++)
            {
                for (int h = 0; h < sides; h++)
                {
                    for (int a = 0; a < sides; a++)
                    {
                        if (h != a) fixtures.Add((h, a, FirstSeed + (ulong)((h * sides + a) * 16 + r)));
                    }
                }
            }

            return fixtures;
        }

        /// <summary>The sample's matches as the gate's CSV writes them (<see cref="Gate11.WriteMatches"/>).</summary>
        public static List<CalibrationMatch> Read(IEnumerable<string> csvLines, IReadOnlyList<LeagueSide> sides)
        {
            int Index(string name) => sides.Select((s, i) => (s, i)).First(t => t.s.Name == name).i;
            var rows = new List<CalibrationMatch>();
            foreach (string line in csvLines.Skip(1))
            {
                if (line.Length == 0) continue;
                string[] f = line.Split(',');
                rows.Add(new CalibrationMatch
                {
                    Seed = ulong.Parse(f[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    Home = Index(f[3]),
                    Away = Index(f[4]),
                    HomeGoals = int.Parse(f[6], CultureInfo.InvariantCulture),
                    AwayGoals = int.Parse(f[7], CultureInfo.InvariantCulture),
                    Fouls = int.Parse(f[11], CultureInfo.InvariantCulture),
                    Cautions = int.Parse(f[13], CultureInfo.InvariantCulture),
                    SendingsOff = int.Parse(f[14], CultureInfo.InvariantCulture),
                });
            }

            return rows;
        }

        /// <summary>
        /// The comparison's key numbers: the sample against the quick-sim drawn <paramref name="draws"/>
        /// times over the same fixtures (goals a side, win/draw/loss, cautions and goals a match).
        /// </summary>
        public static string Compare(SimConfig config, IReadOnlyList<CalibrationMatch> sample, IReadOnlyList<LeagueSide> sides, int draws, QuickSimModel? model = null)
        {
            double fh = 0, fa = 0, fc = 0, qh = 0, qa = 0, qc = 0;
            int fw = 0, fd = 0, qw = 0, qd = 0, n = 0;
            foreach (CalibrationMatch m in sample)
            {
                fh += m.HomeGoals;
                fa += m.AwayGoals;
                fc += m.Cautions;
                if (m.HomeGoals > m.AwayGoals) fw++;
                else if (m.HomeGoals == m.AwayGoals) fd++;
                for (int d = 0; d < draws; d++, n++)
                {
                    MatchResult r = QuickSim.Play(config, QuickSides.Of(sides[m.Home]), QuickSides.Of(sides[m.Away]), m.Seed * 1000 + (ulong)d, model: model);
                    qh += r.HomeGoals;
                    qa += r.AwayGoals;
                    qc += r.HomeCautions + r.AwayCautions;
                    if (r.HomeGoals > r.AwayGoals) qw++;
                    else if (r.HomeGoals == r.AwayGoals) qd++;
                }
            }

            int c = sample.Count;
            return FormattableString.Invariant(
                $"full {c} / quick {n}: home goals {fh / c:F2} / {qh / n:F2}, away {fa / c:F2} / {qa / n:F2}, home wins {100.0 * fw / c:F0} % / {100.0 * qw / n:F0} %, draws {100.0 * fd / c:F0} % / {100.0 * qd / n:F0} %, cautions {fc / c:F2} / {qc / n:F2}");
        }

        /// <summary>One fixture of the sample, played in full.</summary>
        public static GateMatch Play(SimConfig config, IReadOnlyList<LeagueSide> sides, (int Home, int Away, ulong Seed) fixture) =>
            Gate11.Play(config, sides, fixture.Home, fixture.Away, fixture.Seed, "calibration");
    }

    /// <summary>One full match of the calibration sample: the sides by their place, the seed, and what the quick-sim models.</summary>
    public sealed class CalibrationMatch
    {
        public int Home, Away, HomeGoals, AwayGoals, Fouls, Cautions, SendingsOff;
        public ulong Seed;
    }

    /// <summary>A league side as the quick-sim reads it (#129): its CPU tier and its capabilities.</summary>
    public static class QuickSides
    {
        /// <summary>A CPU skill's tier: apprentice 0, club 1, pro 2, elite 3.</summary>
        public static int Tier(CpuSkill skill) =>
            skill.DecisionInterval == CpuSkill.Elite.DecisionInterval ? 3
            : skill.DecisionInterval == CpuSkill.Pro.DecisionInterval ? 2
            : skill.DecisionInterval == CpuSkill.Club.DecisionInterval ? 1 : 0;

        public static QuickSide Of(LeagueSide side) => new QuickSide(Tier(side.Skill), side.Capabilities);
    }

    /// <summary>
    /// The quick-sim's reference, fitted to the calibration sample (#129): μ, h, the tiers' ratings
    /// and the capability weight by Poisson maximum likelihood, ρ by Dixon–Coles's likelihood over those rates, and the
    /// foul and card means a side. Deterministic: a fixed search, no randomness.
    /// </summary>
    public static class QuickSimFit
    {
        /// <summary>The fit to <paramref name="sample"/> (<see cref="FitWithErrors"/>), without its errors.</summary>
        public static QuickSimModel Fit(IReadOnlyList<CalibrationMatch> sample, IReadOnlyList<LeagueSide> sides) => FitWithErrors(sample, sides).Model;

        /// <summary>The fit to <paramref name="sample"/>, with each parameter's standard error (#151).</summary>
        public static QuickSimFitResult FitWithErrors(IReadOnlyList<CalibrationMatch> sample, IReadOnlyList<LeagueSide> sides)
        {
            // Each side's goals are a Poisson observation with log-rate β·x over the parameters
            // β = (μ, h, r_club, r_pro, r_elite, w): x holds 1, home's 1, the tiers' indicator
            // differences (the scorer's minus the other's) and the capability index's difference.
            // A concave likelihood: Newton's method.
            const int P = 2 + QuickSimModel.Tiers - 1 + 1;
            var rows = new List<(double[] X, int Goals)>();
            foreach (CalibrationMatch m in sample)
            {
                QuickSide h = QuickSides.Of(sides[m.Home]), a = QuickSides.Of(sides[m.Away]);
                double dc = QuickSim.CapabilityIndex(h.Capabilities) - QuickSim.CapabilityIndex(a.Capabilities);
                var xh = new double[P];
                var xa = new double[P];
                xh[0] = xa[0] = 1;
                xh[1] = 1;
                for (int t = 1; t < QuickSimModel.Tiers; t++)
                {
                    double d = (h.Tier == t ? 1 : 0) - (a.Tier == t ? 1 : 0);
                    xh[1 + t] = d;
                    xa[1 + t] = -d;
                }

                xh[P - 1] = dc;
                xa[P - 1] = -dc;
                rows.Add((xh, m.HomeGoals));
                rows.Add((xa, m.AwayGoals));
            }

            var beta = new double[P];
            for (int iteration = 0; iteration < 100; iteration++)
            {
                var gradient = new double[P];
                var hessian = new double[P, P];
                foreach ((double[] x, int y) in rows)
                {
                    double eta = 0;
                    for (int i = 0; i < P; i++) eta += beta[i] * x[i];
                    double rate = SimMath.Exp(eta);
                    for (int i = 0; i < P; i++)
                    {
                        gradient[i] += (y - rate) * x[i];
                        for (int j = 0; j < P; j++) hessian[i, j] += rate * x[i] * x[j];
                    }
                }

                double[] step = Solve(hessian, gradient);
                double size = 0;
                for (int i = 0; i < P; i++)
                {
                    beta[i] += step[i];
                    size += step[i] * step[i];
                }

                if (size < 1e-24) break;
            }

            // The standard errors: the inverse of the likelihood's Hessian (the information) at the fit.
            var information = new double[P, P];
            foreach ((double[] x, int _) in rows)
            {
                double eta = 0;
                for (int i = 0; i < P; i++) eta += beta[i] * x[i];
                double rate = SimMath.Exp(eta);
                for (int i = 0; i < P; i++)
                {
                    for (int j = 0; j < P; j++) information[i, j] += rate * x[i] * x[j];
                }
            }

            var errors = new double[P];
            for (int i = 0; i < P; i++)
            {
                var unit = new double[P];
                unit[i] = 1;
                double variance = Solve(information, unit)[i];
                errors[i] = variance > 0 ? SimMath.Sqrt(variance) : 0;
            }

            var tiers = new double[QuickSimModel.Tiers];
            for (int t = 1; t < QuickSimModel.Tiers; t++) tiers[t] = beta[1 + t];
            var rated = new QuickSimModel(beta[0], beta[1], tiers, beta[P - 1], 0, 0, 0, 0);

            // ρ: the Dixon–Coles correction's log-likelihood over those rates, on a grid of 0.001 in ±0.3.
            var cells = new List<(int X, int Y, double Lh, double La)>();
            foreach (CalibrationMatch m in sample)
            {
                (double lh, double la) = QuickSim.Rates(QuickSides.Of(sides[m.Home]), QuickSides.Of(sides[m.Away]), QuickSim.ReferenceHalf, rated);
                cells.Add((m.HomeGoals, m.AwayGoals, lh, la));
            }

            double RhoLikelihood(double rho)
            {
                double sum = 0;
                foreach ((int x, int y, double lh, double la) in cells)
                {
                    double p = QuickSim.Probability(x, y, lh, la, rho) / QuickSim.Probability(x, y, lh, la, 0);
                    if (!(p > 0)) return double.NegativeInfinity;
                    sum += Ln(p);
                }

                return sum;
            }

            double bestRho = 0, best = double.NegativeInfinity;
            for (int step = -300; step <= 300; step++)
            {
                double rho = step / 1000.0, likelihood = RhoLikelihood(rho);
                if (likelihood > best)
                {
                    best = likelihood;
                    bestRho = rho;
                }
            }

            // ρ's error from its log-likelihood's curvature at the fit, by a central difference.
            const double H = 0.01;
            double curvature = (RhoLikelihood(bestRho + H) - 2 * best + RhoLikelihood(bestRho - H)) / (H * H);
            double rhoError = curvature < 0 ? SimMath.Sqrt(-1 / curvature) : 0;

            double n = 2.0 * sample.Count;
            var model = new QuickSimModel(beta[0], beta[1], tiers, beta[P - 1], bestRho,
                sample.Sum(m => m.Fouls) / n, sample.Sum(m => m.Cautions) / n, sample.Sum(m => m.SendingsOff) / n);
            var tierErrors = new double[QuickSimModel.Tiers];
            for (int t = 1; t < QuickSimModel.Tiers; t++) tierErrors[t] = errors[1 + t];
            return new QuickSimFitResult(model, errors[0], errors[1], tierErrors, errors[P - 1], rhoError);
        }

        // The natural log of a positive finite x, with basic operations only (the portable maths has
        // none): x = m·2^k with m in [1, 2), then ln m = 2·atanh((m − 1)/(m + 1)) by its series.
        private const double Ln2 = 0.69314718055994530942;

        internal static double Ln(double x)
        {
            int k = 0;
            while (x >= 2)
            {
                x /= 2;
                k++;
            }

            while (x < 1)
            {
                x *= 2;
                k--;
            }

            double z = (x - 1) / (x + 1), z2 = z * z, term = z, sum = 0;
            for (int i = 1; i < 60; i += 2)
            {
                sum += term / i;
                term *= z2;
            }

            return 2 * sum + k * Ln2;
        }

        // A small linear system by Gaussian elimination with partial pivoting.
        private static double[] Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            var m = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) m[i, j] = a[i, j];
                m[i, n] = b[i];
            }

            for (int c = 0; c < n; c++)
            {
                int pivot = c;
                for (int r = c + 1; r < n; r++)
                {
                    if ((m[r, c] < 0 ? -m[r, c] : m[r, c]) > (m[pivot, c] < 0 ? -m[pivot, c] : m[pivot, c])) pivot = r;
                }

                for (int j = 0; j <= n; j++) (m[c, j], m[pivot, j]) = (m[pivot, j], m[c, j]);
                if (m[c, c] == 0) continue;
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = m[r, c] / m[c, c];
                    for (int j = c; j <= n; j++) m[r, j] -= f * m[c, j];
                }
            }

            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = m[i, i] == 0 ? 0 : m[i, n] / m[i, i];
            return x;
        }
    }

    /// <summary>
    /// #151: a fit and each parameter's standard error. μ, h, the tier ratings and w take theirs from
    /// the Poisson likelihood's curvature at the fit (the inverse of its Hessian); ρ from its own
    /// likelihood's curvature. The apprentice tier's rating is 0 by definition, its error 0.
    /// </summary>
    public sealed class QuickSimFitResult
    {
        public QuickSimFitResult(QuickSimModel model, double baseRateError, double homeError, double[] tierErrors, double capabilityWeightError, double rhoError)
        {
            Model = model;
            BaseRateError = baseRateError;
            HomeError = homeError;
            TierErrors = tierErrors;
            CapabilityWeightError = capabilityWeightError;
            RhoError = rhoError;
        }

        public QuickSimModel Model { get; }

        public double BaseRateError { get; }

        public double HomeError { get; }

        public IReadOnlyList<double> TierErrors { get; }

        public double CapabilityWeightError { get; }

        public double RhoError { get; }
    }
}
