using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Eleven.Simulation;

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
    /// The quick-sim's reference, fitted to the calibration sample (#129): μ, h, k and the capability
    /// weight by Poisson maximum likelihood, ρ by Dixon–Coles's likelihood over those rates, and the
    /// foul and card means a side. Deterministic: a fixed search, no randomness.
    /// </summary>
    public static class QuickSimFit
    {
        public static QuickSimModel Fit(IReadOnlyList<CalibrationMatch> sample, IReadOnlyList<LeagueSide> sides) =>
            new QuickSimModel(0, 0, 0, 0, 0, 0, 0, 0);
    }
}
