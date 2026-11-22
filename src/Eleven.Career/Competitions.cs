#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>A competition's id: the three divisions and the cup (#162).</summary>
    public enum CompetitionId : byte
    {
        Division1 = 1,
        Division2 = 2,
        Division3 = 3,
        Cup = 10,
    }

    /// <summary>
    /// The competitions' rules as data (#162, #80): three 12-club divisions meeting twice, 3/1/0
    /// points and 2 up, 2 down; a 36-club cup of single-leg ties settled by extra time and
    /// penalties, its final at a neutral ground.
    /// </summary>
    public sealed class CompetitionRules
    {
        public int LeagueClubs { get; set; } = 12;

        /// <summary>How often each pair of clubs meets: twice, once at each ground.</summary>
        public int Meetings { get; set; } = 2;

        public int PointsForWin { get; set; } = 3;

        public int PointsForDraw { get; set; } = 1;

        public int PointsForLoss { get; set; }

        public int Promoted { get; set; } = 2;

        public int Relegated { get; set; } = 2;

        public int CupEntrants { get; set; } = 36;

        /// <summary>A drawn cup tie goes to extra time and then penalties: the simulation's knockout.</summary>
        public bool CupExtraTimeAndPenalties { get; set; } = true;

        public bool CupNeutralFinal { get; set; } = true;

        /// <summary>A league season's rounds: each club meets each other <see cref="Meetings"/> times.</summary>
        public int LeagueRounds => (LeagueClubs - 1) * Meetings;

        /// <summary>The cup's rounds, by name.</summary>
        public static readonly ReadOnlyCollection<string> CupRounds = Array.AsReadOnly(new[]
        {
            "Preliminary round", "Round of 32", "Round of 16", "Quarter-finals", "Semi-finals", "Final",
        });

        public static CompetitionRules Reference => new CompetitionRules();
    }

    /// <summary>One match of a competition: who plays whom, where, and when in it.</summary>
    public sealed class Fixture
    {
        public int Id;
        public int Season;
        public CompetitionId Competition;

        /// <summary>The round, from 1.</summary>
        public int Round;

        /// <summary>The match's place in its round, from 0.</summary>
        public int Index;

        public int Home;
        public int Away;

        /// <summary>At a neutral ground: neither side is at home (the cup final).</summary>
        public bool Neutral;

        /// <summary>A stable id from the season, the competition, the round and the place.</summary>
        public static int MakeId(int season, CompetitionId competition, int round, int index) => 0;
    }

    /// <summary>A league's fixtures for a season (#162).</summary>
    public static class LeagueFixtures
    {
        public static List<Fixture> Generate(IReadOnlyList<int> clubs, ulong seed, int season, CompetitionId division) => new List<Fixture>();
    }

    /// <summary>The cup (#162): its first round, each round's draw, and a tie's winner.</summary>
    public static class Cup
    {
        /// <summary>The ties of the preliminary round and the clubs with a bye, from the entrants ranked strongest first.</summary>
        public static (List<int> Preliminary, List<int> Byes) FirstRound(IReadOnlyList<int> ranked) => (new List<int>(), new List<int>());

        /// <summary>A round's draw: the clubs paired at random from the round's own seed, the first drawn at home.</summary>
        public static List<Fixture> Draw(IReadOnlyList<int> clubs, ulong seed, int season, int round) => new List<Fixture>();

        /// <summary>The tie's winner: the club, by goals, then by the shootout.</summary>
        public static int Winner(Fixture tie, MatchResult result) => 0;
    }

    /// <summary>What a matchday plays: a league round in every division, or a cup round.</summary>
    public struct Matchday
    {
        public CompetitionId Competition;
        public int Round;
    }

    /// <summary>The season's matchdays in order (#162).</summary>
    public static class SeasonCalendar
    {
        public static List<Matchday> Of(CompetitionRules rules) => new List<Matchday>();
    }
}
