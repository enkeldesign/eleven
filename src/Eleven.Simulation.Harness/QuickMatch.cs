using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Quick match (#160): two of the world's clubs, a size and a half's length, played at once.
    /// The engine-free half of the Home screen: which address opens on Home, the clubs in the
    /// picker's order, and the match's kick-off from the world through <see cref="CareerSquads"/>.
    /// The host only shows it.
    /// </summary>
    public static class QuickMatch
    {
        /// <summary>The world quick matches are played in: one seed, so a club is the same club on every visit and device.</summary>
        public const ulong WorldSeed = 0x160;

        /// <summary>The sizes offered, as outfielders a side: 5v5 and eleven a side.</summary>
        public static readonly int[] Sizes = { 5, 10 };

        public static readonly string[] SizeNames = { "5v5", "11v11" };

        /// <summary>The halves' lengths offered, in seconds.</summary>
        public static readonly double[] Halves = { 60, 120, 180, 300 };

        public static readonly string[] HalfNames = { "1 min", "2 min", "3 min", "5 min" };

        private static readonly Lazy<World> Shared = new Lazy<World>(() => WorldGenerator.Generate(WorldSeed));

        /// <summary>The quick-match world, generated once.</summary>
        public static World World => Shared.Value;

        /// <summary>Whether an address opens on Home.</summary>
        public static bool ShowsHome(string url) => false;

        /// <summary>The clubs in the picker's order.</summary>
        public static List<Club> Clubs(World world) => new List<Club>();

        /// <summary>A quick match's kick-off: the human on the home club.</summary>
        public static SimState Match(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed) =>
            Squads.Match(config, seed, perSide);

        /// <summary>The score panel's line.</summary>
        public static string Score(Club home, int homeGoals, int awayGoals, Club away) => "";
    }
}
