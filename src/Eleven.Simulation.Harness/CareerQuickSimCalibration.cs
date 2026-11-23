using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Eleven.Career;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One match of the eleven-a-side sample: the fixture, and the full simulation's result.</summary>
    public sealed class CareerCalibrationMatch
    {
        public int Index;
        public int Home, Away;
        public ulong Seed;
        public int HomeGoals, AwayGoals, HomeFouls, AwayFouls, HomeCautions, AwayCautions, HomeSendingsOff, AwaySendingsOff;

        /// <summary>Each side's footballers injured (#172), by their worst injury: minor, serious.</summary>
        public int HomeMinor, AwayMinor, HomeSerious, AwaySerious;

        /// <summary>The result's text form, hashed: any change in the match shows.</summary>
        public ulong ResultHash;
    }

    /// <summary>The eleven-a-side fit, with each parameter's standard error.</summary>
    public sealed class CareerFitResult
    {
        public QuickSimModel Model = QuickSimModel.Reference;
        public double BaseRateError, HomeError, ScaleError, RhoError;

        /// <summary>s: the reference's ratings' scale at eleven a side.</summary>
        public double Scale;
    }

    /// <summary>
    /// The quick-sim at eleven a side (#163). The reference model was fitted to the full
    /// simulation at 5v5 (#129, #151). The world's clubs play 11v11, where the full simulation
    /// scores about half as often and its ratings' gaps are narrower. The sample is the full
    /// simulation over fixtures between the reference world's clubs. The fit keeps the
    /// reference's tier ratings and capability weight in proportion, scaled by s, and refits μ,
    /// h, s, ρ and the foul and card means.
    /// </summary>
    public static class CareerQuickSimCalibration
    {
        public const ulong WorldSeed = 0x157;
        public const int PerSide = 10;
        public const double HalfLength = 180;
        public const int Matches = 480;

        /// <summary>The fixtures CI plays and diffs against the reference's first rows.</summary>
        public const int CiFixtures = 8;

        public const ulong FirstSeed = 0x163000UL;

        public static World World() => WorldGenerator.Generate(WorldSeed);

        public static SimConfig Config() => SpikeScenario.Config().With("halfLength", HalfLength);

        /// <summary>
        /// The sample's fixtures: <paramref name="count"/> ordered pairs of the world's clubs,
        /// every 17th of all of them in id order, so they spread over the clubs and over every gap
        /// between divisions, each on its own seed.
        /// </summary>
        public static List<(int Home, int Away, ulong Seed)> Fixtures(World world, int count = Matches)
        {
            List<int> ids = world.Clubs.Select(c => c.Id).OrderBy(i => i).ToList();
            int others = ids.Count - 1, pairs = ids.Count * others;
            if (count > pairs) throw new ArgumentOutOfRangeException(nameof(count), count, $"{ids.Count} clubs make {pairs} pairs.");
            var fixtures = new List<(int, int, ulong)>(count);
            for (int i = 0; i < count; i++)
            {
                int k = (int)((long)i * 17 % pairs), home = k / others, other = k % others;
                fixtures.Add((ids[home], ids[other >= home ? other + 1 : other], FirstSeed + (ulong)i));
            }

            return fixtures;
        }

        /// <summary>One fixture played by the full simulation (<see cref="CareerSquads.Play"/>), every brain running.</summary>
        public static CareerCalibrationMatch Play(SimConfig config, World world, (int Home, int Away, ulong Seed) fixture, int index)
        {
            MatchResult r = CareerSquads.Play(config, world, fixture.Home, fixture.Away, PerSide, fixture.Seed);
            return new CareerCalibrationMatch
            {
                Index = index,
                Home = fixture.Home,
                Away = fixture.Away,
                Seed = fixture.Seed,
                HomeGoals = r.HomeGoals,
                AwayGoals = r.AwayGoals,
                HomeFouls = r.HomeFouls,
                AwayFouls = r.AwayFouls,
                HomeCautions = r.HomeCautions,
                AwayCautions = r.AwayCautions,
                HomeSendingsOff = r.HomeSendingsOff,
                AwaySendingsOff = r.AwaySendingsOff,
                ResultHash = Fnv1a64.Hash(System.Text.Encoding.UTF8.GetBytes(r.ToText())),
                HomeMinor = Injured(r, r.HomeTeam, 1),
                AwayMinor = Injured(r, r.AwayTeam, 1),
                HomeSerious = Injured(r, r.HomeTeam, 2),
                AwaySerious = Injured(r, r.AwayTeam, 2),
            };
        }

        private const string Header = "index,home,away,seed,home_goals,away_goals,home_fouls,away_fouls,home_cautions,away_cautions,home_sendings_off,away_sendings_off,result_hash,home_minor,away_minor,home_serious,away_serious";

        // A side's footballers whose worst injury was this level (#172).
        private static int Injured(MatchResult r, int team, int level)
        {
            int n = 0;
            foreach (ResultPlayer p in r.Players)
            {
                if (p.Team == team && p.Injury == level) n++;
            }

            return n;
        }

        public static void Write(TextWriter w, IReadOnlyList<CareerCalibrationMatch> sample)
        {
            w.Write(Header + "\n");
            foreach (CareerCalibrationMatch m in sample)
            {
                w.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:x},{4},{5},{6},{7},{8},{9},{10},{11},{12:x16},{13},{14},{15},{16}\n",
                    m.Index, m.Home, m.Away, m.Seed, m.HomeGoals, m.AwayGoals, m.HomeFouls, m.AwayFouls, m.HomeCautions, m.AwayCautions, m.HomeSendingsOff, m.AwaySendingsOff, m.ResultHash,
                    m.HomeMinor, m.AwayMinor, m.HomeSerious, m.AwaySerious));
            }
        }

        public static List<CareerCalibrationMatch> Read(IEnumerable<string> lines)
        {
            var sample = new List<CareerCalibrationMatch>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("index", StringComparison.Ordinal)) continue;
                string[] f = line.Split(',');
                int I(int i) => int.Parse(f[i], CultureInfo.InvariantCulture);
                sample.Add(new CareerCalibrationMatch
                {
                    Index = I(0),
                    Home = I(1),
                    Away = I(2),
                    Seed = ulong.Parse(f[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    HomeGoals = I(4),
                    AwayGoals = I(5),
                    HomeFouls = I(6),
                    AwayFouls = I(7),
                    HomeCautions = I(8),
                    AwayCautions = I(9),
                    HomeSendingsOff = I(10),
                    AwaySendingsOff = I(11),
                    ResultHash = ulong.Parse(f[12], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    HomeMinor = f.Length > 16 ? I(13) : 0,
                    AwayMinor = f.Length > 16 ? I(14) : 0,
                    HomeSerious = f.Length > 16 ? I(15) : 0,
                    AwaySerious = f.Length > 16 ? I(16) : 0,
                });
            }

            return sample;
        }

        /// <summary>
        /// The eleven-a-side fit to <paramref name="sample"/>: each side's goals a Poisson
        /// observation with log-rate μ + h·home + s·(r_side − r_other) at the sample's halves,
        /// where r is the <paramref name="reference"/>'s rating of each club's side. μ, h and s by
        /// Newton's method, with their errors from the information; ρ by the Dixon–Coles
        /// likelihood on a grid of 0.001; the foul and card means a side. The model is the
        /// reference's ratings scaled by s.
        /// </summary>
        public static CareerFitResult Fit(World world, IReadOnlyList<CareerCalibrationMatch> sample, QuickSimModel? reference = null)
        {
            QuickSimModel r = reference ?? QuickSimModel.Reference;
            var sides = new Dictionary<int, QuickSide>();
            QuickSide Side(int club) => sides.TryGetValue(club, out QuickSide s) ? s : sides[club] = CareerQuickSim.Side(world, club, PerSide);

            const int P = 3;
            double offset = QuickSimFit.Ln(HalfLength / QuickSim.ReferenceHalf);
            var rows = new List<(double[] X, int Goals)>(2 * sample.Count);
            foreach (CareerCalibrationMatch m in sample)
            {
                double d = QuickSim.Rating(Side(m.Home), r) - QuickSim.Rating(Side(m.Away), r);
                rows.Add((new[] { 1, 1, d }, m.HomeGoals));
                rows.Add((new[] { 1, 0, -d }, m.AwayGoals));
            }

            var beta = new double[P];
            double[,] Information(out double[] gradient)
            {
                var g = new double[P];
                var h = new double[P, P];
                foreach ((double[] x, int y) in rows)
                {
                    double eta = offset;
                    for (int i = 0; i < P; i++) eta += beta[i] * x[i];
                    double rate = SimMath.Exp(eta);
                    for (int i = 0; i < P; i++)
                    {
                        g[i] += (y - rate) * x[i];
                        for (int j = 0; j < P; j++) h[i, j] += rate * x[i] * x[j];
                    }
                }

                gradient = g;
                return h;
            }

            for (int iteration = 0; iteration < 100; iteration++)
            {
                double[,] hessian = Information(out double[] gradient);
                double[] step = QuickSimFit.Solve(hessian, gradient);
                double size = 0;
                for (int i = 0; i < P; i++)
                {
                    beta[i] += step[i];
                    size += step[i] * step[i];
                }

                if (size < 1e-24) break;
            }

            double[,] information = Information(out _);
            var errors = new double[P];
            for (int i = 0; i < P; i++)
            {
                var unit = new double[P];
                unit[i] = 1;
                double variance = QuickSimFit.Solve(information, unit)[i];
                errors[i] = variance > 0 ? SimMath.Sqrt(variance) : 0;
            }

            double scale = beta[2];
            var tiers = new double[QuickSimModel.Tiers];
            for (int t = 0; t < QuickSimModel.Tiers; t++) tiers[t] = scale * r.TierRating(t);
            var rated = new QuickSimModel(beta[0], beta[1], tiers, scale * r.CapabilityWeight, 0, 0, 0, 0);

            // ρ over those rates, as the reference's fit does it.
            var cells = new List<(int X, int Y, double Lh, double La)>(sample.Count);
            foreach (CareerCalibrationMatch m in sample)
            {
                (double lh, double la) = QuickSim.Rates(Side(m.Home), Side(m.Away), HalfLength, rated);
                cells.Add((m.HomeGoals, m.AwayGoals, lh, la));
            }

            double RhoLikelihood(double rho)
            {
                double sum = 0;
                foreach ((int x, int y, double lh, double la) in cells)
                {
                    double p = QuickSim.Probability(x, y, lh, la, rho) / QuickSim.Probability(x, y, lh, la, 0);
                    if (!(p > 0)) return double.NegativeInfinity;
                    sum += QuickSimFit.Ln(p);
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

            const double H = 0.01;
            double curvature = (RhoLikelihood(bestRho + H) - 2 * best + RhoLikelihood(bestRho - H)) / (H * H);

            // The foul and card means a side, for a match of reference halves.
            double sides2 = 2.0 * sample.Count * (HalfLength / QuickSim.ReferenceHalf);
            var model = new QuickSimModel(beta[0], beta[1], tiers, scale * r.CapabilityWeight, bestRho,
                sample.Sum(m => m.HomeFouls + m.AwayFouls) / sides2, sample.Sum(m => m.HomeCautions + m.AwayCautions) / sides2,
                sample.Sum(m => m.HomeSendingsOff + m.AwaySendingsOff) / sides2, r.ShootoutConversion,
                sample.Sum(m => m.HomeMinor + m.AwayMinor) / sides2, sample.Sum(m => m.HomeSerious + m.AwaySerious) / sides2);
            return new CareerFitResult
            {
                Model = model,
                Scale = scale,
                BaseRateError = errors[0],
                HomeError = errors[1],
                ScaleError = errors[2],
                RhoError = curvature < 0 ? SimMath.Sqrt(-1 / curvature) : 0,
            };
        }

        /// <summary>The full simulation's sample against the model's quick-sim, by the gap between the clubs' divisions.</summary>
        public static string Compare(SimConfig config, World world, IReadOnlyList<CareerCalibrationMatch> sample, int draws, QuickSimModel model)
        {
            var sides = new Dictionary<int, QuickSide>();
            QuickSide Side(int club) => sides.TryGetValue(club, out QuickSide s) ? s : sides[club] = CareerQuickSim.Side(world, club, PerSide);
            var division = world.Clubs.ToDictionary(c => c.Id, c => c.Division);
            var text = new StringBuilder();
            text.Append("| Away's division less home's | Matches | Full: goals | Full: home W/D | Quick: goals | Quick: home W/D |\n| --- | --- | --- | --- | --- | --- |\n");
            foreach (int gap in new[] { -2, -1, 0, 1, 2, int.MaxValue })
            {
                List<CareerCalibrationMatch> group = sample.Where(m => gap == int.MaxValue || division[m.Away] - division[m.Home] == gap).ToList();
                if (group.Count == 0) continue;
                double fh = group.Average(m => (double)m.HomeGoals), fa = group.Average(m => (double)m.AwayGoals);
                double fw = group.Average(m => m.HomeGoals > m.AwayGoals ? 1.0 : 0), fd = group.Average(m => m.HomeGoals == m.AwayGoals ? 1.0 : 0);
                double qh = 0, qa = 0, qw = 0, qd = 0;
                foreach (CareerCalibrationMatch m in group)
                {
                    for (int d = 0; d < draws; d++)
                    {
                        MatchResult q = QuickSim.Play(config, Side(m.Home), Side(m.Away), m.Seed * 1000 + (ulong)d, model: model);
                        qh += q.HomeGoals;
                        qa += q.AwayGoals;
                        if (q.HomeGoals > q.AwayGoals) qw++;
                        else if (q.HomeGoals == q.AwayGoals) qd++;
                    }
                }

                double n = group.Count * (double)draws;
                text.Append(string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2:F2}–{3:F2} | {4:P0}/{5:P0} | {6:F2}–{7:F2} | {8:P0}/{9:P0} |\n",
                    gap == int.MaxValue ? "all" : gap.ToString("+0;−0;0", CultureInfo.InvariantCulture), group.Count, fh, fa, fw, fd, qh / n, qa / n, qw / n, qd / n));
            }

            return text.ToString();
        }
    }
}
