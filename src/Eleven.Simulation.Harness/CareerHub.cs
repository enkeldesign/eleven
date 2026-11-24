using System;
using System.Collections.Generic;
using System.Linq;
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
            string last = LastMet(s, f.Home == s.Club ? f.Away : f.Home);
            return $"{what}: {Short(s, f.Home)} v {Short(s, f.Away)}, {ground}" + (last.Length > 0 ? $". {last}" : "");
        }

        /// <summary>
        /// The human's last meeting with <paramref name="opponent"/> (#191), from his club's
        /// manager's memory: "Last met: lost 1–2 (season 1, matchday 7)". Empty if they haven't met.
        /// </summary>
        public static string LastMet(CareerSession s, int opponent)
        {
            Club? c = s.World.Clubs.Find(x => x.Id == s.Club);
            Manager? m = c == null ? null : s.World.Managers.Find(x => x.Id == c.ManagerId);
            Meeting? last = m?.Memory.FindLast(x => x.Opponent == opponent);
            if (last == null) return "";
            string how = last.GoalsFor > last.GoalsAgainst ? "won" : last.GoalsFor < last.GoalsAgainst ? "lost" : "drew";
            return FormattableString.Invariant($"Last met: {how} {last.GoalsFor}–{last.GoalsAgainst} (season {last.Season}, matchday {last.Matchday})");
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

        /// <summary>Your squad (#179): the XI in its slots' order, the bench, then everyone else by level.</summary>
        public static List<SquadRow> SquadRows(CareerSession s)
        {
            Club club = s.World.Clubs.Find(c => c.Id == s.Club)!;
            HashSet<int> outs = s.OutOfNext();
            SquadSelection playing = CareerSquads.Selection(s.World, s.Club, CareerSession.PerSide, outs, s.Squad);
            List<int> picks = club.Picks.Count > 0 ? club.Picks : s.Picked();
            var placed = new List<int>();
            foreach (Player p in playing.Starters) placed.Add(p.Id);
            var xi = new List<int>();
            var bench = new List<int>();
            for (int i = 0; i < picks.Count; i++) (i <= CareerSession.PerSide ? xi : bench).Add(picks[i]);
            // The XI in the order it plays, its picks out of it last.
            xi.Sort((a, b) => Place(placed, a).CompareTo(Place(placed, b)));
            var rows = new List<SquadRow>();
            foreach (int id in xi) rows.Add(Row(s, id, SquadGroup.Xi, outs));
            foreach (int id in bench) rows.Add(Row(s, id, SquadGroup.Bench, outs));
            var rest = new List<SquadRow>();
            foreach (Player p in s.World.Players)
            {
                if (p.ClubId == s.Club && !picks.Contains(p.Id)) rest.Add(Row(s, p.Id, SquadGroup.NotPicked, outs));
            }

            rest.Sort((a, b) => a.Level != b.Level ? b.Level.CompareTo(a.Level) : a.Player.CompareTo(b.Player));
            rows.AddRange(rest);
            return rows;
        }

        private static int Place(List<int> placed, int id)
        {
            int at = placed.IndexOf(id);
            return at < 0 ? int.MaxValue : at;
        }

        private static readonly string[] PositionCodes = { "GK", "CB", "FB", "DM", "CM", "WM", "AM", "ST" };

        private static SquadRow Row(CareerSession s, int id, SquadGroup group, HashSet<int> outs)
        {
            Player p = s.World.Players.Find(x => x.Id == id)!;
            var positions = new List<string>();
            foreach (Position at in p.Positions) positions.Add(PositionCodes[(int)at]);
            return new SquadRow
            {
                Player = id,
                Name = $"{(p.FirstName.Length > 0 ? p.FirstName.Substring(0, 1) + ". " : "")}{p.LastName}",
                Age = p.Age,
                Positions = string.Join(", ", positions),
                Level = CareerSquads.Level(p),
                Condition = p.Fitness,
                Group = group,
                Out = outs.Contains(id) || p.InjuredDays > 0 ? Why(p) : "",
            };
        }

        // Why he's out, as the hub's out line says it (#176).
        private static string Why(Player p)
        {
            var why = new List<string>();
            if (p.InjuredDays > 0) why.Add($"injured, {p.InjuredDays} day{(p.InjuredDays == 1 ? "" : "s")}");
            var bans = new List<string>();
            if (p.LeagueBan > 0) bans.Add($"{p.LeagueBan} league match{(p.LeagueBan == 1 ? "" : "es")}");
            if (p.CupBan > 0) bans.Add($"{p.CupBan} cup match{(p.CupBan == 1 ? "" : "es")}");
            if (bans.Count > 0) why.Add("banned, " + string.Join(" and ", bans));
            return string.Join("; ", why);
        }

        /// <summary>Who can change places with <paramref name="player"/> (#179): everyone in another group who may take his place and isn't out, best first.</summary>
        public static List<SquadRow> SwapCandidates(CareerSession s, int player)
        {
            List<SquadRow> rows = SquadRows(s);
            SquadRow? me = rows.Find(r => r.Player == player);
            var candidates = new List<SquadRow>();
            if (me == null) return candidates;
            foreach (SquadRow r in rows)
            {
                if (r.Group == me.Group) continue;
                bool keeper = s.World.Players.Find(p => p.Id == r.Player)!.Positions[0] == Position.Goalkeeper;
                bool meKeeper = s.World.Players.Find(p => p.Id == player)!.Positions[0] == Position.Goalkeeper;
                // Each goes to the other's place: one out can't come in, nor a keeper onto the bench;
                // in the XI a keeper's place is a keeper's, an outfielder's an outfielder's.
                if (me.Group != SquadGroup.NotPicked && (r.Out.Length > 0 || (me.Group == SquadGroup.Bench && keeper) || (me.Group == SquadGroup.Xi && keeper != meKeeper))) continue;
                if (r.Group != SquadGroup.NotPicked && (me.Out.Length > 0 || (r.Group == SquadGroup.Bench && meKeeper) || (r.Group == SquadGroup.Xi && keeper != meKeeper))) continue;
                candidates.Add(r);
            }

            candidates.Sort((a, b) => a.Level != b.Level ? b.Level.CompareTo(a.Level) : a.Player.CompareTo(b.Player));
            return candidates;
        }

        /// <summary>Your picks out of your next match (#179), "Picked but out: A. Name; the AI's pick takes his place."; empty with none.</summary>
        public static string PickLine(CareerSession s)
        {
            Club club = s.World.Clubs.Find(c => c.Id == s.Club)!;
            if (club.Picks.Count == 0) return "";
            HashSet<int> outs = s.OutOfNext();
            var names = new List<string>();
            for (int i = 0; i <= CareerSession.PerSide && i < club.Picks.Count; i++)
            {
                if (!outs.Contains(club.Picks[i])) continue;
                Player p = s.World.Players.Find(x => x.Id == club.Picks[i])!;
                names.Add($"{(p.FirstName.Length > 0 ? p.FirstName.Substring(0, 1) + ". " : "")}{p.LastName}");
            }

            return names.Count == 0 ? ""
                : names.Count == 1 ? $"Picked but out: {names[0]}; the AI's pick takes his place."
                : $"Picked but out: {string.Join(", ", names)}; the AI's picks take their places.";
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

        /// <summary>Your club's training (#196) and what it does, e.g. "Training: Hard. Faster development, slower recovery, more injuries."</summary>
        public static string TrainingLine(CareerSession s) => "";

        /// <summary>
        /// The season's review (#194), from the season's turn until your club's first match of the
        /// new one: your club's biggest risers last season, its retirements and its youth. Empty
        /// in a first season.
        /// </summary>
        public static string ReviewLine(CareerSession s)
        {
            if (s.Season.Number <= 1) return "";
            foreach (Fixture f in s.Season.Fixtures)
            {
                if ((f.Home == s.Club || f.Away == s.Club) && s.Season.Results.ContainsKey(f.Id)) return "";
            }

            string Short(string first, string last) => (first.Length > 0 ? first.Substring(0, 1) + ". " : "") + last;
            List<Player> mine = s.World.Players.FindAll(p => p.ClubId == s.Club);
            var risers = new List<string>();
            foreach (Player p in mine.Where(p => p.Joined < s.Season.Number && p.LastSeasonPoints > 0).OrderByDescending(p => p.LastSeasonPoints).ThenBy(p => p.Id).Take(3))
            {
                int counted = p.Positions.Count > 0 && p.Positions[0] == Position.Goalkeeper ? 3 : PlayerAttributes.Count - 3;
                risers.Add(FormattableString.Invariant($"{Short(p.FirstName, p.LastName)} +{(double)p.LastSeasonPoints / counted:F1}"));
            }

            var parts = new List<string>();
            if (risers.Count > 0) parts.Add("most improved " + string.Join(", ", risers));
            List<string> retired = s.World.LastRetired.Where(r => r.ClubId == s.Club).Select(r => $"{Short(r.FirstName, r.LastName)}, {r.Age}").ToList();
            if (retired.Count > 0) parts.Add("Retired: " + string.Join("; ", retired));
            List<string> youth = mine.Where(p => p.Joined == s.Season.Number).OrderBy(p => p.Id).Select(p => $"{Short(p.FirstName, p.LastName)}, {p.Age}").ToList();
            if (youth.Count > 0) parts.Add("From the youth: " + string.Join("; ", youth));
            return parts.Count == 0 ? "" : "Last season: " + string.Join(". ", parts) + ".";
        }
    }

    /// <summary>Where a footballer is in your squad (#179).</summary>
    public enum SquadGroup
    {
        Xi,
        Bench,
        NotPicked,
    }

    /// <summary>A row of your squad (#179): who, where, his level and condition, and why he's out.</summary>
    public sealed class SquadRow
    {
        public int Player;
        public string Name = "", Positions = "", Out = "";
        public int Age;
        public double Level;
        public int Condition;
        public SquadGroup Group;

        /// <summary>The row's text: "J. Smith, 23 · CB, DM · 14.2 · 96" with the reason he's out after; his age since #194.</summary>
        public string Text => FormattableString.Invariant($"{Name}, {Age} · {Positions} · {Level:F1} · {Condition}") + (Out.Length > 0 ? $" · out: {Out}" : "");
    }
}
