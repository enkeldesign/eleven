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

        /// <summary>
        /// Whether an address opens on Home: the plain address (no parameters), or one that asks
        /// for it with <c>home=1</c>. Any other parameter opens as before, so every existing link,
        /// and every smoke pass, goes where it went.
        /// </summary>
        public static bool ShowsHome(string url)
        {
            string query = url ?? "";
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            int at = query.IndexOf('?');
            if (at < 0) return true;
            query = query.Substring(at + 1);
            if (query.Length == 0) return true;
            foreach (string part in query.Split('&'))
            {
                if (part == "home=1") return true;
            }

            return false;
        }

        /// <summary>The clubs in the picker's order: by division, the top first; then by reputation, highest first; then by id.</summary>
        public static List<Club> Clubs(World world)
        {
            var clubs = new List<Club>(world.Clubs);
            clubs.Sort((a, b) => a.Division != b.Division ? a.Division.CompareTo(b.Division)
                : a.Reputation != b.Reputation ? b.Reputation.CompareTo(a.Reputation)
                : a.Id.CompareTo(b.Id));
            return clubs;
        }

        /// <summary>
        /// A quick match's kick-off: the human on his club's kick-off taker, his team-mates CPU
        /// footballers; every footballer, keeper and bench on both sides on his own values from
        /// his ratings (#159), his CPU skill included, which a switch leaves with him (#166); both
        /// sides with the three stored plans (#126).
        /// </summary>
        public static SimState Match(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed, ISet<int>? homeOut = null, ISet<int>? awayOut = null, SquadRules? rotation = null,
            TeamTactic[]? homeTactics = null) =>
            CareerSquads.Match(config, world, homeClub, awayClub, perSide, seed, bothCpu: false, homeTactics: homeTactics, homeOut: homeOut, awayOut: awayOut, rotation: rotation);

        /// <summary>The score panel's line: the clubs' short names either side of the score.</summary>
        public static string Score(Club home, int homeGoals, int awayGoals, Club away) =>
            FormattableString.Invariant($"{home.ShortName} {homeGoals} – {awayGoals} {away.ShortName}");
    }
}
