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

    /// <summary>What a career slot offers on Career (#171).</summary>
    public enum SlotChoice
    {
        /// <summary>An empty slot: New career.</summary>
        New,

        /// <summary>A saved career: Continue loads it.</summary>
        Continue,

        /// <summary>The career in play, in its own slot: Continue returns to its hub as it is.</summary>
        Resume,
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
        /// <summary>
        /// The human's footballers out of his next match (#176), "Out: A. Name (injured, 12 days),
        /// B. Name (banned, 1 league match)"; empty with none.
        /// </summary>
        public static string OutLine(CareerSession s)
        {
            var parts = new List<string>();
            var squad = new List<Player>();
            foreach (Player p in s.World.Players)
            {
                if (p.ClubId == s.Club && (p.InjuredDays > 0 || p.LeagueBan > 0 || p.CupBan > 0)) squad.Add(p);
            }

            squad.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (Player p in squad)
            {
                var why = new List<string>();
                if (p.InjuredDays > 0) why.Add($"injured, {p.InjuredDays} day{(p.InjuredDays == 1 ? "" : "s")}");
                var bans = new List<string>();
                if (p.LeagueBan > 0) bans.Add($"{p.LeagueBan} league match{(p.LeagueBan == 1 ? "" : "es")}");
                if (p.CupBan > 0) bans.Add($"{p.CupBan} cup match{(p.CupBan == 1 ? "" : "es")}");
                if (bans.Count > 0) why.Add("banned, " + string.Join(" and ", bans));
                string initial = p.FirstName.Length > 0 ? p.FirstName.Substring(0, 1) + ". " : "";
                parts.Add($"{initial}{p.LastName} ({string.Join("; ", why)})");
            }

            return parts.Count == 0 ? "" : "Out: " + string.Join(", ", parts);
        }

        /// <summary>The human's XI below full condition (#177), lowest first, "Condition: A. Name 72, B. Name 88"; empty with none.</summary>
        public static string ConditionLine(CareerSession s)
        {
            Fixture? next = s.Next();
            if (next == null) return "";
            var byId = new Dictionary<int, Player>(s.World.Players.Count);
            foreach (Player p in s.World.Players) byId[p.Id] = p;
            var tired = new List<Player>();
            foreach (KeyValuePair<int, int> e in s.LineupFor(next))
            {
                bool starter = false;
                for (int i = 0; i < CareerSession.PerSide; i++) starter |= e.Key == Squads.HomeOutfielder(i);
                starter |= e.Key == Squads.HomeKeeper(CareerSession.PerSide);
                if (starter && byId.TryGetValue(e.Value, out Player? p) && p.Fitness < 100) tired.Add(p);
            }

            tired.Sort((a, b) => a.Fitness != b.Fitness ? a.Fitness.CompareTo(b.Fitness) : a.Id.CompareTo(b.Id));
            var parts = new List<string>();
            foreach (Player p in tired) parts.Add($"{(p.FirstName.Length > 0 ? p.FirstName.Substring(0, 1) + ". " : "")}{p.LastName} {p.Fitness}");
            return parts.Count == 0 ? "" : "Condition: " + string.Join(", ", parts);
        }

        public static string Summary(CareerSession s)
        {
            if (!s.SeasonOver) return "";
            (List<int> up, List<int> down) = SeasonEngine.Movement(s.Season, s.Rules);
            string fate = up.Contains(s.Club) ? " You go up." : down.Contains(s.Club) ? " You go down." : "";
            string winner = s.World.Clubs.Find(c => c.Id == s.Season.CupWinner)?.Name ?? "nobody";
            return FormattableString.Invariant($"You finished {Ordinal(s.Position)} in division {s.Division}.{fate} Up: {string.Join(", ", up.ConvertAll(id => Short(s, id)))}. Down: {string.Join(", ", down.ConvertAll(id => Short(s, id)))}. Cup won by {winner}.");
        }

        /// <summary>
        /// What slot <paramref name="slot"/> offers on Career (#171), from its save and the career in
        /// play, if any, with its slot. The career in play resumes from its own slot as it is,
        /// whatever the slot's save says, so a save the device refused never loses it (Codex on #183).
        /// Every other slot offers its save, or a new career.
        /// </summary>
        public static SlotChoice SlotOffer(int slot, SlotSummary saved, CareerSession? playing, int playingSlot) =>
            playing != null && slot == playingSlot ? SlotChoice.Resume
            : saved.Empty ? SlotChoice.New
            : SlotChoice.Continue;

        /// <summary>
        /// A slot's line on Career: "1. Name (NAM) · Season 1 · Matchday 4", or "1. Empty". The career
        /// in play is its own: "· in play", and "· in play, not saved" when the slot's save is behind it.
        /// </summary>
        public static string SlotLine(int slot, SlotSummary saved, CareerSession? playing, int playingSlot)
        {
            if (SlotOffer(slot, saved, playing, playingSlot) == SlotChoice.Resume)
            {
                Club? club = playing!.World.Clubs.Find(c => c.Id == playing.Club);
                int season = playing.Season.Number, matchday = playing.Season.Matchday + 1;
                bool behind = saved.Empty || saved.Club != playing.Club || saved.Season != season || saved.Matchday != matchday;
                return FormattableString.Invariant($"{slot + 1}. {club?.Name} ({club?.ShortName}) · Season {season} · Matchday {matchday} · in play{(behind ? ", not saved" : "")}");
            }

            return saved.Empty
                ? FormattableString.Invariant($"{slot + 1}. Empty")
                : FormattableString.Invariant($"{slot + 1}. {saved.ClubName} ({saved.ClubShort}) · Season {saved.Season} · Matchday {saved.Matchday}{(saved.FromPrevious ? " · its previous save" : "")}");
        }

        /// <summary>The hub's forward action.</summary>
        public static HubAction Action(CareerSession s) =>
            s.SeasonOver ? HubAction.NextSeason
            : s.Next() == null ? HubAction.NextMatchday
            : s.Role == CareerRole.PlayerManager ? HubAction.Play : HubAction.Watch;
    }
}
