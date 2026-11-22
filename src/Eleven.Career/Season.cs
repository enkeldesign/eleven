#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>A club's line in a league table.</summary>
    public sealed class TableRow
    {
        public int Club;
        public int Played, Won, Drawn, Lost, For, Against, Points;

        public int GoalDifference => For - Against;
    }

    /// <summary>
    /// A season as plain data (#164): its number and seed, each division's clubs, the league's
    /// fixtures and the cup's ties drawn so far, every result by fixture id, the next matchday,
    /// and the clubs still in the cup.
    /// </summary>
    public sealed class Season
    {
        public int Number;
        public ulong Seed;

        /// <summary>Each division's clubs: division 1 first.</summary>
        public List<List<int>> Divisions = new List<List<int>>();

        /// <summary>The entrants to the cup, strongest first: last season's final tables.</summary>
        public List<int> CupRanking = new List<int>();

        /// <summary>The league's fixtures, then the cup's ties as each round is drawn.</summary>
        public List<Fixture> Fixtures = new List<Fixture>();

        public Dictionary<int, MatchResult> Results = new Dictionary<int, MatchResult>();

        /// <summary>The next matchday: its place in the calendar.</summary>
        public int Matchday;

        /// <summary>The clubs with a bye, waiting for the round of 32, and then the cup's survivors.</summary>
        public List<int> CupAlive = new List<int>();

        /// <summary>The cup's winner once its final is played, else 0.</summary>
        public int CupWinner;
    }

    /// <summary>
    /// The season engine (#164): the season's matchdays in turn, each league round in every
    /// division or a cup round drawn as it comes up; the results recorded; the tables; the end
    /// with its promotion and relegation; and the rollover into the next season.
    /// </summary>
    public static class SeasonEngine
    {
        /// <summary>A new season for the world's clubs in their divisions.</summary>
        public static Season Start(World world, int number, ulong careerSeed, IReadOnlyList<int> cupRanking, CompetitionRules rules) => new Season();

        /// <summary>The first season's cup ranking: by division, then reputation, highest first, then id.</summary>
        public static List<int> FirstRanking(World world) => new List<int>();

        public static bool Finished(Season season, CompetitionRules rules) => true;

        /// <summary>The next matchday's fixtures; a cup round is drawn the first time it is asked for.</summary>
        public static List<Fixture> Next(Season season, CompetitionRules rules) => new List<Fixture>();

        /// <summary>A result for one of the next matchday's fixtures.</summary>
        public static void Record(Season season, CompetitionRules rules, int fixtureId, MatchResult result) { }

        /// <summary>On to the next matchday, once every fixture of this one has its result.</summary>
        public static void Advance(Season season, CompetitionRules rules) { }

        /// <summary>A division's table: points, goal difference, goals, head to head among the tied, then a drawn lot.</summary>
        public static List<TableRow> Table(Season season, CompetitionId division, CompetitionRules rules) => new List<TableRow>();

        /// <summary>Who goes up and who goes down: the top of each division but the first, the bottom of each but the last.</summary>
        public static (List<int> Promoted, List<int> Relegated) Movement(Season season, CompetitionRules rules) => (new List<int>(), new List<int>());

        /// <summary>The season's end: the world's clubs in their new divisions, and the next season.</summary>
        public static Season Rollover(World world, Season season, ulong careerSeed, CompetitionRules rules) => new Season();
    }
}
