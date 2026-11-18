using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// #129's calibration sample: the full simulation over M3's six sides at eleven a side, each
    /// ordered pair <see cref="PerPair"/> times on fixed seeds, every footballer a CPU footballer at
    /// his side's tier. The quick-sim is fitted to it and compared with it. Each fixture is a
    /// whole, independent, deterministic match: a runner may play them in any order, or at once,
    /// and writes them in their fixed order.
    /// </summary>
    public static class QuickSimCalibration
    {
        public const int PerPair = 2;
        public const ulong FirstSeed = 0x129000000UL;

        /// <summary>The sample's fixtures, in their fixed order: home, away and seed.</summary>
        public static List<(int Home, int Away, ulong Seed)> Fixtures(int sides, int perPair = PerPair)
        {
            var fixtures = new List<(int Home, int Away, ulong Seed)>();
            for (int h = 0; h < sides; h++)
            {
                for (int a = 0; a < sides; a++)
                {
                    if (h == a) continue;
                    for (int i = 0; i < perPair; i++) fixtures.Add((h, a, FirstSeed + (ulong)((h * sides + a) * 16 + i)));
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
        public static QuickSimModel Fit(IReadOnlyList<CalibrationMatch> sample, IReadOnlyList<LeagueSide> sides)
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

            var tiers = new double[QuickSimModel.Tiers];
            for (int t = 1; t < QuickSimModel.Tiers; t++) tiers[t] = beta[1 + t];
            var rated = new QuickSimModel(beta[0], beta[1], tiers, beta[P - 1], 0, 0, 0, 0);

            // ρ: the Dixon–Coles correction's likelihood over those rates, on a grid of 0.001 in ±0.3.
            double bestRho = 0, best = double.NegativeInfinity;
            for (int step = -300; step <= 300; step++)
            {
                double rho = step / 1000.0, product = 1;
                bool valid = true;
                foreach (CalibrationMatch m in sample)
                {
                    (double lh, double la) = QuickSim.Rates(QuickSides.Of(sides[m.Home]), QuickSides.Of(sides[m.Away]), QuickSim.ReferenceHalf, rated);
                    double p = QuickSim.Probability(m.HomeGoals, m.AwayGoals, lh, la, rho) / QuickSim.Probability(m.HomeGoals, m.AwayGoals, lh, la, 0);
                    if (!(p > 0))
                    {
                        valid = false;
                        break;
                    }

                    product *= p;
                }

                if (valid && product > best)
                {
                    best = product;
                    bestRho = rho;
                }
            }

            double n = 2.0 * sample.Count;
            return new QuickSimModel(beta[0], beta[1], tiers, beta[P - 1], bestRho,
                sample.Sum(m => m.Fouls) / n, sample.Sum(m => m.Cautions) / n, sample.Sum(m => m.SendingsOff) / n);
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
}
