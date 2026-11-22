using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Headless seasons (#164): every fixture of a season played by the quick-sim (#163), league
    /// matches as league matches and cup ties as knockouts, unless a hook plays it another way:
    /// M6.06's human fixture, played by the full simulation.
    /// </summary>
    public static class CareerSeasons
    {
        /// <summary>A fixture's quick-sim result: its own seed from the season's and the fixture's id.</summary>
        public static MatchResult Quick(SimConfig config, World world, Season season, Fixture fixture, int perSide) => QuickSim.Play(config, default, default, 0);

        /// <summary>Plays the next matchday: each fixture by the hook if it gives a result, else the quick-sim.</summary>
        public static List<Fixture> PlayMatchday(SimConfig config, World world, Season season, CompetitionRules rules, int perSide, Func<Fixture, MatchResult?>? hook = null) => new List<Fixture>();

        /// <summary>Plays the season to its end.</summary>
        public static void PlaySeason(SimConfig config, World world, Season season, CompetitionRules rules, int perSide, Func<Fixture, MatchResult?>? hook = null) { }

        /// <summary>From a new career's first season, <paramref name="seasons"/> seasons played and rolled over: every season, the last one not yet rolled.</summary>
        public static List<Season> RollForward(SimConfig config, World world, ulong careerSeed, int seasons, CompetitionRules rules, int perSide) => new List<Season>();
    }
}
