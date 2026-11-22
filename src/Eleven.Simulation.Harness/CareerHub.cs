using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>The hub's one forward action (#168).</summary>
    public enum HubAction
    {
        /// <summary>Play the human's fixture: player-manager.</summary>
        Play,

        /// <summary>Watch the human's fixture: manager-only.</summary>
        Watch,

        /// <summary>No fixture of the human's this matchday: play the others.</summary>
        NextMatchday,

        /// <summary>The season is over: roll into the next.</summary>
        NextSeason,
    }

    /// <summary>One line of the hub's table: its text, and whether it is the human's club.</summary>
    public readonly struct HubRow
    {
        public HubRow(string text, bool mine)
        {
            Text = text;
            Mine = mine;
        }

        public string Text { get; }

        public bool Mine { get; }
    }

    /// <summary>
    /// What the career hub says (#168), from #167's session: engine-free, so the host only
    /// shows it. Every line in plain English; M7 owns the Swedish.
    /// </summary>
    public static class CareerHub
    {
        /// <summary>A place in words: 1st, 2nd, 3rd, 4th, 11th, 12th, 13th, 21st.</summary>
        public static string Ordinal(int n)
        {
            int tens = n % 100, units = n % 10;
            string suffix = tens >= 11 && tens <= 13 ? "th" : units == 1 ? "st" : units == 2 ? "nd" : units == 3 ? "rd" : "th";
            return FormattableString.Invariant($"{n}{suffix}");
        }

        /// <summary>"Season 1 · Matchday 4 of 28", or "Season 1 · over".</summary>
        public static string Header(CareerSession s) => s.SeasonOver
            ? FormattableString.Invariant($"Season {s.Season.Number} · over")
            : FormattableString.Invariant($"Season {s.Season.Number} · Matchday {s.Season.Matchday + 1} of {SeasonCalendar.Of(s.Rules).Count}");

        /// <summary>"Division 1 · 5th · 12 points".</summary>
        public static string Standing(CareerSession s)
        {
            int points = Mine(s)?.Points ?? 0;
            return FormattableString.Invariant($"Division {s.Division} · {Ordinal(s.Position)} · {points} point{(points == 1 ? "" : "s")}");
        }

        private static TableRow? Mine(CareerSession s) => s.Table().Find(r => r.Club == s.Club);

        private static string Short(CareerSession s, int club) => s.World.Clubs.Find(c => c.Id == club)?.ShortName ?? "?";

        /// <summary>The human's next fixture, or why there is none.</summary>
        public static string NextLine(CareerSession s)
        {
            if (s.SeasonOver) return "The season is over.";
            Fixture? f = s.Next();
            if (f == null)
            {
                Matchday day = SeasonCalendar.Of(s.Rules)[s.Season.Matchday];
                bool bye = day.Kind == MatchdayKind.Cup && day.Round == 1 && s.Season.CupAlive.Contains(s.Club);
                return bye ? "No match for you: a bye into the round of 32." : "No match for you: out of the cup.";
            }

            string ground = f.Neutral ? "at a neutral ground" : f.Home == s.Club ? "at home" : "away";
            string what = f.Competition == CompetitionId.Cup
                ? FormattableString.Invariant($"Cup, {CompetitionRules.CupRounds[f.Round - 1]}")
                : FormattableString.Invariant($"League round {f.Round}");
            return $"{what}: {Short(s, f.Home)} v {Short(s, f.Away)}, {ground}";
        }

        /// <summary>The human's division's table: place, club, played, goal difference and points; his row marked.</summary>
        public static List<HubRow> TableRows(CareerSession s)
        {
            var rows = new List<HubRow>();
            List<TableRow> table = s.Table();
            for (int i = 0; i < table.Count; i++)
            {
                TableRow r = table[i];
                string difference = (r.GoalDifference > 0 ? "+" : "") + r.GoalDifference.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rows.Add(new HubRow(FormattableString.Invariant($"{i + 1,2}  {Short(s, r.Club),-4} {r.Played,2}  {difference}  {r.Points,2}"), r.Club == s.Club));
            }

            return rows;
        }

        /// <summary>The human's last results, the latest first: "W 2–1 v ROV (home)", "L 1–1 v ROT (away, cup, on penalties)".</summary>
        public static List<string> ResultLines(CareerSession s, int count)
        {
            var lines = new List<string>();
            foreach ((Fixture f, MatchResult r) in s.LastResults(count))
            {
                // From the club's side: its goals are the home ones.
                int mine = r.HomeGoals, theirs = r.AwayGoals;
                bool pens = mine == theirs && r.HomeShootoutGoals != r.AwayShootoutGoals;
                string verdict = mine > theirs || (pens && r.HomeShootoutGoals > r.AwayShootoutGoals) ? "W" : mine < theirs || pens ? "L" : "D";
                string where = f.Neutral ? "neutral" : f.Home == s.Club ? "home" : "away";
                string cup = f.Competition == CompetitionId.Cup ? ", cup" : "";
                string shootout = pens ? ", on penalties" : "";
                int other = f.Home == s.Club ? f.Away : f.Home;
                lines.Add(FormattableString.Invariant($"{verdict} {mine}–{theirs} v {Short(s, other)} ({where}{cup}{shootout})"));
            }

            return lines;
        }

        /// <summary>The season's end: the human's finish, the moves and the cup's winner; empty while it goes on.</summary>
        public static string Summary(CareerSession s)
        {
            if (!s.SeasonOver) return "";
            (List<int> up, List<int> down) = SeasonEngine.Movement(s.Season, s.Rules);
            string fate = up.Contains(s.Club) ? " You go up." : down.Contains(s.Club) ? " You go down." : "";
            string winner = s.World.Clubs.Find(c => c.Id == s.Season.CupWinner)?.Name ?? "nobody";
            return FormattableString.Invariant($"You finished {Ordinal(s.Position)} in division {s.Division}.{fate} Up: {string.Join(", ", up.ConvertAll(id => Short(s, id)))}. Down: {string.Join(", ", down.ConvertAll(id => Short(s, id)))}. Cup won by {winner}.");
        }

        /// <summary>The hub's forward action.</summary>
        public static HubAction Action(CareerSession s) =>
            s.SeasonOver ? HubAction.NextSeason
            : s.Next() == null ? HubAction.NextMatchday
            : s.Role == CareerRole.PlayerManager ? HubAction.Play : HubAction.Watch;
    }
}
