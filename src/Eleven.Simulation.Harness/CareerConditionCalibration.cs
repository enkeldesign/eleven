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

        public static ConditionMatch Play(SimConfig config, World world, (int Home, int Away, ulong Seed) fixture, int index, double homeFatigue)
        {
            MatchResult r = CareerSquads.Play(config, world, fixture.Home, fixture.Away, CareerQuickSimCalibration.PerSide, fixture.Seed, homeFatigue: homeFatigue);
            return new ConditionMatch { Index = index, HomeFatigue = homeFatigue, Home = fixture.Home, Away = fixture.Away, HomeGoals = r.HomeGoals, AwayGoals = r.AwayGoals };
        }

        /// <summary>The sample's fixtures: every <see cref="Step"/>th of the eleven-a-side sample's, each at every level.</summary>
        public static List<(int Index, (int Home, int Away, ulong Seed) Fixture, double Level)> Matches(World world)
        {
            var all = CareerQuickSimCalibration.Fixtures(world);
            var matches = new List<(int, (int, int, ulong), double)>();
            for (int i = 0, n = 0; i < all.Count; i += Step, n++)
            {
                foreach (double level in Levels) matches.Add((n, all[i], level));
            }

            return matches;
        }

        public static void Write(TextWriter w, IReadOnlyList<ConditionMatch> sample)
        {
            w.Write(Header + "\n");
            foreach (ConditionMatch m in sample)
                w.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1:F2},{2},{3},{4},{5}\n", m.Index, m.HomeFatigue, m.Home, m.Away, m.HomeGoals, m.AwayGoals));
        }

        public static List<ConditionMatch> Read(IEnumerable<string> lines)
        {
            var sample = new List<ConditionMatch>();
            bool header = true;
            foreach (string line in lines)
            {
                if (header)
                {
                    if (line != Header) throw new FormatException($"Not a condition sample: header '{line}'.");
                    header = false;
                    continue;
                }

                if (line.Length == 0) continue;
                string[] f = line.Split(',');
                if (f.Length != 6) throw new FormatException($"A condition row has {f.Length} fields: '{line}'.");
                int I(int i) => int.Parse(f[i], CultureInfo.InvariantCulture);
                sample.Add(new ConditionMatch
                {
                    Index = I(0),
                    HomeFatigue = double.Parse(f[1], CultureInfo.InvariantCulture),
                    Home = I(2),
                    Away = I(3),
                    HomeGoals = I(4),
                    AwayGoals = I(5),
                });
            }

            return sample;
        }

        /// <summary>
        /// The fatigue weight w by Poisson maximum likelihood over the sample, every other value the
        /// model's: the home side's rating less w × its fatigue. With its standard error. Bounded,
        /// w is at least 0: a tired start never helps a side, so an estimate below it is noise.
        /// </summary>
        public static (double Weight, double Error) Fit(World world, IReadOnlyList<ConditionMatch> sample, QuickSimModel model, bool bounded = true)
        {
            var sides = new Dictionary<int, QuickSide>();
            QuickSide Side(int club) => sides.TryGetValue(club, out QuickSide s) ? s : sides[club] = CareerQuickSim.Side(world, club, CareerQuickSimCalibration.PerSide);
            double half = CareerQuickSimCalibration.HalfLength;

            // The Poisson log-likelihood of both sides' goals at weight w (the scoreline's ρ left out, as the rates' fit does).
            double Likelihood(double w)
            {
                QuickSimModel m = With(model, w);
                double sum = 0;
                foreach (ConditionMatch c in sample)
                {
                    QuickSide home = Side(c.Home);
                    (double lh, double la) = QuickSim.Rates(new QuickSide(home.Tier, home.Capabilities, c.HomeFatigue), Side(c.Away), half, m);
                    sum += c.HomeGoals * QuickSimFit.Ln(lh) - lh + c.AwayGoals * QuickSimFit.Ln(la) - la;
                }

                return sum;
            }

            // A coarse grid, then Newton's steps on the numeric derivatives.
            double best = 0, bestL = double.NegativeInfinity;
            for (int step = -40; step <= 80; step++)
            {
                double w = step / 20.0, l = Likelihood(w);
                if (l > bestL)
                {
                    bestL = l;
                    best = w;
                }
            }

            const double H = 1e-3;
            for (int i = 0; i < 20; i++)
            {
                double up = Likelihood(best + H), down = Likelihood(best - H), at = Likelihood(best);
                double d1 = (up - down) / (2 * H), d2 = (up - 2 * at + down) / (H * H);
                if (!(d2 < 0)) break;
                double next = best - d1 / d2;
                if (Math.Abs(next - best) < 1e-10) break;
                best = next;
            }

            double curvature = (Likelihood(best + H) - 2 * Likelihood(best) + Likelihood(best - H)) / (H * H);
            double error = curvature < 0 ? Eleven.Simulation.Maths.SimMath.Sqrt(-1 / curvature) : 0;
            return (Math.Round(bounded ? Math.Max(0, best) : best, 6), error);
        }

        /// <summary>The full simulation's sample against the model's quick-sim, by the home side's fatigue at kick-off.</summary>
        public static string Compare(SimConfig config, World world, IReadOnlyList<ConditionMatch> sample, int draws, QuickSimModel model)
        {
            var sides = new Dictionary<int, QuickSide>();
            QuickSide Side(int club) => sides.TryGetValue(club, out QuickSide s) ? s : sides[club] = CareerQuickSim.Side(world, club, CareerQuickSimCalibration.PerSide);
            var text = new System.Text.StringBuilder();
            text.Append("| Home fatigue | Matches | Full: goals | Full: home W/D | Quick: goals | Quick: home W/D |\n| --- | --- | --- | --- | --- | --- |\n");
            foreach (double level in Levels)
            {
                int n = 0, fh = 0, fa = 0, fw = 0, fd = 0;
                long qh = 0, qa = 0, qw = 0, qd = 0;
                foreach (ConditionMatch m in sample)
                {
                    if (m.HomeFatigue != level) continue;
                    n++;
                    fh += m.HomeGoals;
                    fa += m.AwayGoals;
                    if (m.HomeGoals > m.AwayGoals) fw++;
                    else if (m.HomeGoals == m.AwayGoals) fd++;
                    QuickSide home = Side(m.Home);
                    var tired = new QuickSide(home.Tier, home.Capabilities, level);
                    for (int d = 0; d < draws; d++)
                    {
                        MatchResult r = QuickSim.Play(config, tired, Side(m.Away), (ulong)(m.Index * 1000 + d) ^ 0x177UL, model: model);
                        qh += r.HomeGoals;
                        qa += r.AwayGoals;
                        if (r.HomeGoals > r.AwayGoals) qw++;
                        else if (r.HomeGoals == r.AwayGoals) qd++;
                    }
                }

                if (n == 0) continue;
                double q = (double)n * draws;
                text.Append(FormattableString.Invariant(
                    $"| {level:F1} | {n} | {(double)fh / n:F2}–{(double)fa / n:F2} | {100.0 * fw / n:F0} %/{100.0 * fd / n:F0} % | {qh / q:F2}–{qa / q:F2} | {100.0 * qw / q:F0} %/{100.0 * qd / q:F0} % |\n"));
            }

            return text.ToString();
        }

        /// <summary>The model with its fatigue weight set: every other value the same.</summary>
        public static QuickSimModel With(QuickSimModel m, double fatigueWeight)
        {
            var tiers = new double[QuickSimModel.Tiers];
            for (int t = 0; t < tiers.Length; t++) tiers[t] = m.TierRating(t);
            return new QuickSimModel(m.BaseRate, m.Home, tiers, m.CapabilityWeight, m.Rho, m.FoulsPerSide, m.CautionsPerSide, m.SendingsOffPerSide, m.ShootoutConversion,
                m.MinorInjuriesPerSide, m.SeriousInjuriesPerSide, fatigueWeight);
        }
    }
}
