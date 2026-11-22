using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Eleven.Career;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;

namespace Eleven.Simulation.Harness
{
    /// <summary>One match of the eleven-a-side sample: the fixture, and the full simulation's result.</summary>
    public sealed class CareerCalibrationMatch
    {
        public int Index;
        public int Home, Away;
        public ulong Seed;
        public int HomeGoals, AwayGoals, HomeFouls, AwayFouls, HomeCautions, AwayCautions, HomeSendingsOff, AwaySendingsOff;

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

        /// <summary>The sample's fixtures: <paramref name="count"/> ordered pairs of the world's clubs.</summary>
        public static List<(int Home, int Away, ulong Seed)> Fixtures(World world, int count = Matches) => new List<(int, int, ulong)>();

        public static CareerCalibrationMatch Play(SimConfig config, World world, (int Home, int Away, ulong Seed) fixture, int index) => new CareerCalibrationMatch();

        public static void Write(TextWriter w, IReadOnlyList<CareerCalibrationMatch> sample) { }

        public static List<CareerCalibrationMatch> Read(IEnumerable<string> lines) => new List<CareerCalibrationMatch>();

        public static CareerFitResult Fit(World world, IReadOnlyList<CareerCalibrationMatch> sample, QuickSimModel? reference = null) => new CareerFitResult();

        /// <summary>The full simulation's sample against the model's quick-sim, by the gap between the clubs' divisions.</summary>
        public static string Compare(SimConfig config, World world, IReadOnlyList<CareerCalibrationMatch> sample, int draws, QuickSimModel model) => "";
    }
}
