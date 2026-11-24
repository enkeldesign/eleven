#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Career
{
    /// <summary>
    /// The board's knobs (#200), all technical references, not tuned feel. A club's wage bill
    /// explains most of where it finishes (Szymanski and Kuypers, 1999), so a board expects its
    /// wage rank; a head coach's dismissal follows the gap between his results and what was
    /// expected before the season (Pieper, Nüesch and Franck, 2014; Bryson, Buraimo, Farnell and
    /// Simmons, 2023), so confidence moves with points against the expected.
    /// </summary>
    public sealed class BoardRules
    {
        /// <summary>A new manager's confidence, and where a new season's carry leans toward.</summary>
        public const double FreshStart = 60;

        /// <summary>The league objective: the expected place, this many places' leeway, never below safety.</summary>
        public int Leeway = 2;

        /// <summary>The cup's round to reach by division, a <see cref="Fixture.Round"/>: the quarter-finals, the round of 16, the round of 32.</summary>
        public int[] CupTarget = { 4, 3, 2 };

        /// <summary>A match's expected points, the expected place's: the top's, falling evenly to the bottom's.</summary>
        public double TopPoints = 2.0, BottomPoints = 0.9;

        /// <summary>A new manager's confidence, 0–100.</summary>
        public double StartConfidence = FreshStart;

        /// <summary>Confidence a league point above the match's expected, or lost a point below.</summary>
        public double PerPoint = 2.5;

        /// <summary>Knocked out of the cup before its target round; and each round reached beyond it.</summary>
        public double CupMissed = 6, CupBeyond = 3;

        /// <summary>The season's end: the league objective met, or missed; then each place above or below it.</summary>
        public double LeagueMet = 5, LeagueMissed = 5, PerPlace = 2;

        /// <summary>The season's end in deficit.</summary>
        public double Deficit = 8;

        /// <summary>A new season keeps this share of confidence's distance from its start.</summary>
        public double Carry = 0.5;

        /// <summary>Confidence's words, from these up: pleased, satisfied, concerned; delighted above the first.</summary>
        public double Delighted = 80, Pleased = 60, Satisfied = 40, Concerned = 20;

        /// <summary>A reputation moves this share of the way to its target a season, at most this many points.</summary>
        public double ReputationRate = 0.35;

        public int ReputationStep = 2;

        /// <summary>The cup's winner's reputation target, this much higher.</summary>
        public double CupWinnerReputation = 1;

        /// <summary>A manager's reputation target: his club's, this much higher a place above the expected.</summary>
        public double ManagerPerPlace = 0.5;

        public static BoardRules Reference => new BoardRules();
    }

    /// <summary>A club's board (#200): this season's objectives and its confidence, and last season's verdict for the review.</summary>
    public sealed class BoardState
    {
        /// <summary>The club's wage-bill rank in its division at the season's start, 1 the highest.</summary>
        public int ExpectedPlace;

        /// <summary>The place to reach, and the cup's round (a <see cref="Fixture.Round"/>).</summary>
        public int LeagueTarget, CupTarget;

        /// <summary>The board's confidence in its manager, 0–100.</summary>
        public double Confidence = BoardRules.FreshStart;

        /// <summary>Last season's place, expected place and target; 0 before a first season's end.</summary>
        public int LastPlace, LastExpected, LastTarget;

        /// <summary>Confidence at last season's end, before the new season's carry.</summary>
        public double LastConfidence;

        /// <summary>The club's reputation before last season's turn.</summary>
        public int LastReputation;
    }

    /// <summary>
    /// The board (#200): each season's objectives from the club's wage rank, its confidence in
    /// its manager match by match against the expected, and the clubs' and managers' reputations
    /// moving with what they achieve at the season's turn.
    /// </summary>
    public static class Board
    {
        /// <summary>Each club's expected place, league target and cup target, from its wage-bill rank in its division.</summary>
        public static void Objectives(World world, CompetitionRules competition, BoardRules? rules = null)
        {
        }

        /// <summary>A match's expected points for a club expected to finish <paramref name="place"/> of <paramref name="clubs"/>.</summary>
        public static double ExpectedPoints(int place, int clubs, BoardRules? rules = null) => 0;

        /// <summary>A matchday's results: each league match moves confidence by its points against the expected, each cup tie by the round reached.</summary>
        public static void Matchday(World world, IEnumerable<(Fixture Fixture, MatchResult Result)> played, BoardRules? rules = null)
        {
        }

        /// <summary>
        /// The season's end, before its movement: the league objective and the money judged, each
        /// club's and manager's reputation moved toward its target, last season's verdict kept,
        /// and confidence carried into the new season.
        /// </summary>
        public static void SeasonEnd(World world, Season season, CompetitionRules competition, BoardRules? rules = null)
        {
        }

        /// <summary>The reputation the generator would give a club of <paramref name="division"/> finishing <paramref name="place"/> of <paramref name="clubs"/>.</summary>
        public static double ReputationTarget(int division, int place, int clubs, bool cupWinner, BoardRules? rules = null) => 0;

        /// <summary>A reputation a season's step toward <paramref name="target"/>: a share of the way, at most a step, 1–20.</summary>
        public static int Toward(int reputation, double target, BoardRules? rules = null) => reputation;

        /// <summary>The league objective in words: "Win the league", "Win promotion", "Finish 6th or higher", "Avoid relegation".</summary>
        public static string League(Club club, int divisions, CompetitionRules competition) => "";

        /// <summary>The cup's objective in words, e.g. "reach the round of 16".</summary>
        public static string Cup(Club club) => "";

        /// <summary>Confidence in a word: delighted, pleased, satisfied, concerned, losing patience.</summary>
        public static string Words(double confidence, BoardRules? rules = null) => "";
    }
}
