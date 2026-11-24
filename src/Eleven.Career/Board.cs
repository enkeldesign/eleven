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

        /// <summary>
        /// A match's expected points, the expected place's: the top's, falling evenly to the
        /// bottom's. Measured, not football's: `eleven-sim board 6` from seed 0x200 gives 1.54 a
        /// match to the best paid and 1.02 to the least, and a straight line through every place
        /// 1.50 and 1.02. The career's matches score 1.18 goals and draw 45 % (managers.md), so
        /// its table is flatter than football's.
        /// </summary>
        public double TopPoints = 1.50, BottomPoints = 1.02;

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
            rules ??= BoardRules.Reference;
            var bills = new Dictionary<int, long>();
            foreach (Player p in world.Players) bills[p.ClubId] = (bills.TryGetValue(p.ClubId, out long b) ? b : 0) + p.Wage;
            long Bill(Club c) => bills.TryGetValue(c.Id, out long b) ? b : 0;

            var divisions = new SortedDictionary<int, List<Club>>();
            foreach (Club c in world.Clubs)
            {
                if (!divisions.TryGetValue(c.Division, out List<Club>? clubs)) divisions[c.Division] = clubs = new List<Club>();
                clubs.Add(c);
            }

            foreach (KeyValuePair<int, List<Club>> d in divisions)
            {
                List<Club> ranked = d.Value;
                ranked.Sort((a, b) => Bill(a) != Bill(b) ? Bill(b).CompareTo(Bill(a))
                    : a.Reputation != b.Reputation ? b.Reputation.CompareTo(a.Reputation)
                    : a.Id.CompareTo(b.Id));
                int safety = Math.Max(1, Math.Min(ranked.Count, competition.LeagueClubs) - competition.Relegated);
                for (int i = 0; i < ranked.Count; i++)
                {
                    BoardState board = ranked[i].Board;
                    board.ExpectedPlace = i + 1;
                    board.LeagueTarget = Math.Min(i + 1 + rules.Leeway, safety);
                    board.CupTarget = rules.CupTarget[Math.Clamp(d.Key, 1, rules.CupTarget.Length) - 1];
                }
            }
        }

        /// <summary>A match's expected points for a club expected to finish <paramref name="place"/> of <paramref name="clubs"/>.</summary>
        public static double ExpectedPoints(int place, int clubs, BoardRules? rules = null)
        {
            rules ??= BoardRules.Reference;
            if (clubs <= 1) return rules.TopPoints;
            double t = (double)(Math.Clamp(place, 1, clubs) - 1) / (clubs - 1);
            return rules.TopPoints * (1 - t) + rules.BottomPoints * t;
        }

        /// <summary>A matchday's results: each league match moves confidence by its points against the expected, each cup tie by the round reached.</summary>
        public static void Matchday(World world, IEnumerable<(Fixture Fixture, MatchResult Result)> played, BoardRules? rules = null)
        {
            rules ??= BoardRules.Reference;
            var sizes = new Dictionary<int, int>();
            foreach (Club c in world.Clubs) sizes[c.Division] = (sizes.TryGetValue(c.Division, out int n) ? n : 0) + 1;
            foreach ((Fixture f, MatchResult r) in played)
            {
                Club? home = world.Clubs.Find(c => c.Id == f.Home), away = world.Clubs.Find(c => c.Id == f.Away);
                if (home == null || away == null) continue;
                if (f.Competition != CompetitionId.Cup)
                {
                    int homePoints = r.HomeGoals > r.AwayGoals ? 3 : r.HomeGoals == r.AwayGoals ? 1 : 0;
                    int awayPoints = r.AwayGoals > r.HomeGoals ? 3 : r.HomeGoals == r.AwayGoals ? 1 : 0;
                    Move(home, rules.PerPoint * (homePoints - ExpectedPoints(home.Board.ExpectedPlace, sizes[home.Division], rules)));
                    Move(away, rules.PerPoint * (awayPoints - ExpectedPoints(away.Board.ExpectedPlace, sizes[away.Division], rules)));
                    continue;
                }

                // A cup tie: its winner reaches the next round, its loser is out in this one.
                int winner = Eleven.Career.Cup.Winner(f, r);
                Club through = winner == home.Id ? home : away, outClub = winner == home.Id ? away : home;
                if (f.Round + 1 > through.Board.CupTarget) Move(through, rules.CupBeyond);
                if (f.Round < outClub.Board.CupTarget) Move(outClub, -rules.CupMissed);
            }
        }

        /// <summary>
        /// The season's end, before its movement: the league objective and the money judged, each
        /// club's and manager's reputation moved toward its target, last season's verdict kept,
        /// and confidence carried into the new season.
        /// </summary>
        public static void SeasonEnd(World world, Season season, CompetitionRules competition, BoardRules? rules = null)
        {
            rules ??= BoardRules.Reference;
            foreach (Club c in world.Clubs)
            {
                List<TableRow> table = SeasonEngine.Table(season, (CompetitionId)c.Division, competition);
                int place = table.FindIndex(r => r.Club == c.Id) + 1;
                if (place <= 0) continue;
                BoardState b = c.Board;
                double judged = b.Confidence + (place <= b.LeagueTarget ? rules.LeagueMet : -rules.LeagueMissed) + rules.PerPlace * (b.LeagueTarget - place) - (c.Balance < 0 ? rules.Deficit : 0);
                b.LastPlace = place;
                b.LastExpected = b.ExpectedPlace;
                b.LastTarget = b.LeagueTarget;
                b.LastConfidence = Math.Clamp(judged, 0, 100);
                b.Confidence = rules.StartConfidence + rules.Carry * (b.LastConfidence - rules.StartConfidence);

                double target = ReputationTarget(c.Division, place, table.Count, season.CupWinner == c.Id, rules);
                b.LastReputation = c.Reputation;
                c.Reputation = Toward(c.Reputation, target, rules);
                Manager? m = world.Managers.Find(x => x.Id == c.ManagerId);
                if (m != null) m.Reputation = Toward(m.Reputation, target + rules.ManagerPerPlace * (b.LastExpected - place), rules);
            }
        }

        /// <summary>The reputation the generator would give a club of <paramref name="division"/> finishing <paramref name="place"/> of <paramref name="clubs"/>.</summary>
        public static double ReputationTarget(int division, int place, int clubs, bool cupWinner, BoardRules? rules = null)
        {
            rules ??= BoardRules.Reference;
            double level = WorldGenerator.DivisionLevel[Math.Clamp(division, 1, WorldGenerator.DivisionLevel.Length) - 1];
            double spread = clubs > 1 ? WorldGenerator.ClubSpread * (clubs + 1 - 2.0 * place) / (clubs - 1) : 0;
            return level + spread + (cupWinner ? rules.CupWinnerReputation : 0);
        }

        /// <summary>A reputation a season's step toward <paramref name="target"/>: a share of the way, at most a step, 1–20.</summary>
        public static int Toward(int reputation, double target, BoardRules? rules = null)
        {
            rules ??= BoardRules.Reference;
            int step = (int)Math.Round(rules.ReputationRate * (target - reputation), MidpointRounding.AwayFromZero);
            return Math.Clamp(reputation + Math.Clamp(step, -rules.ReputationStep, rules.ReputationStep), 1, 20);
        }

        /// <summary>The league objective in words: "Win the league", "Win promotion", "Finish 6th or higher", "Avoid relegation".</summary>
        public static string League(Club club, int divisions, CompetitionRules competition)
        {
            int target = club.Board.LeagueTarget;
            if (target <= 1) return "Win the league";
            if (club.Division > 1 && target <= competition.Promoted) return "Win promotion";
            if (club.Division < divisions && target >= competition.LeagueClubs - competition.Relegated) return "Avoid relegation";
            return $"Finish {Ordinal(target)} or higher";
        }

        /// <summary>The cup's objective in words, e.g. "reach the round of 16".</summary>
        public static string Cup(Club club)
        {
            int round = club.Board.CupTarget;
            if (round > CompetitionRules.CupRounds.Count) return "win the cup";
            string name = CompetitionRules.CupRounds[Math.Max(1, round) - 1];
            return "reach the " + char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>Confidence in a word: delighted, pleased, satisfied, concerned, losing patience.</summary>
        public static string Words(double confidence, BoardRules? rules = null)
        {
            rules ??= BoardRules.Reference;
            return confidence >= rules.Delighted ? "delighted"
                : confidence >= rules.Pleased ? "pleased"
                : confidence >= rules.Satisfied ? "satisfied"
                : confidence >= rules.Concerned ? "concerned"
                : "losing patience";
        }

        // Confidence moved, within 0–100.
        private static void Move(Club c, double by) => c.Board.Confidence = Math.Clamp(c.Board.Confidence + by, 0, 100);

        private static string Ordinal(int n)
        {
            int tens = n % 100, units = n % 10;
            string suffix = tens >= 11 && tens <= 13 ? "th" : units == 1 ? "st" : units == 2 ? "nd" : units == 3 ? "rd" : "th";
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix;
        }
    }
}
