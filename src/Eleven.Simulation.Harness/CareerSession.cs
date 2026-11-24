using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>The human's role in a career (#80): he plays his club's matches, or only manages them.</summary>
    public enum CareerRole
    {
        PlayerManager,
        ManagerOnly,
    }

    /// <summary>
    /// A career (#167): its own world, the human's club and role, and the season being played.
    /// The human's fixture is played by the full simulation or simulated; every other fixture
    /// by the quick-sim (#164). The host shows it; nothing here touches the screen.
    /// </summary>
    public sealed class CareerSession
    {
        /// <summary>Outfielders a side in a career match: eleven a side.</summary>
        public const int PerSide = 10;

        public ulong Seed;
        public World World = new World();
        public int Club;
        public CareerRole Role;
        public SimConfig Config = SpikeScenario.Config();
        public CompetitionRules Rules = CompetitionRules.Reference;

        /// <summary>What keeps a footballer out (#176): injuries' layoffs and the cards' bans.</summary>
        public SquadRules Squad = SquadRules.Reference;
        public Season Season = new Season();

        /// <summary>A new career's world, for choosing a club before it starts: the generator's, from the career's seed.</summary>
        public static World NewWorld(ulong seed) => WorldGenerator.Generate(seed);

        /// <summary>A new career at <paramref name="club"/> in <paramref name="world"/>: its first season, the cup ranked by division and reputation.</summary>
        public static CareerSession Start(World world, ulong seed, int club, CareerRole role, SimConfig config)
        {
            if (world.Clubs.Find(c => c.Id == club) == null) throw new ArgumentException($"No club {club} in the world.", nameof(club));
            var session = new CareerSession { Seed = seed, World = world, Club = club, Role = role, Config = config };
            session.Season = SeasonEngine.Start(world, 1, seed, SeasonEngine.FirstRanking(world), session.Rules);
            return session;
        }

        public bool SeasonOver => SeasonEngine.Finished(Season, Rules);

        /// <summary>The human's club's division, from 1.</summary>
        public int Division
        {
            get
            {
                for (int d = 0; d < Season.Divisions.Count; d++)
                {
                    if (Season.Divisions[d].Contains(Club)) return d + 1;
                }

                return 0;
            }
        }

        /// <summary>The human's fixture on the next matchday, or null: a bye, out of the cup, or the season over.</summary>
        public Fixture? Next()
        {
            if (SeasonOver) return null;
            foreach (Fixture f in SeasonEngine.Next(Season, Rules))
            {
                if (f.Home == Club || f.Away == Club) return f;
            }

            return null;
        }

        /// <summary>
        /// Whether the human controls a footballer in his fixture (Codex on #178): a player-manager
        /// plays his club's kick-off taker; a manager-only career's sides are both CPU, so the host
        /// sends no command of the human's, which would duplicate the CPU's for that footballer.
        /// </summary>
        public bool HumanPlays => Role == CareerRole.PlayerManager;

        /// <summary>A fixture's match config: a cup tie is a knockout.</summary>
        public SimConfig MatchConfig(Fixture fixture) => fixture.Competition == CompetitionId.Cup ? Config.With("knockout", 1) : Config;

        /// <summary>
        /// The kick-off of the human's fixture. His club is the simulation's home side, whichever
        /// the fixture's ground, since the human's controller is the home side's (#93); the full
        /// simulation has no home advantage (#163). A player-manager plays his club's footballer
        /// (<see cref="QuickMatch.Match"/>); a manager-only career's sides are both CPU, on their
        /// footballers' own values. Both sides keep their stored plans.
        /// </summary>
        public SimState MatchFor(Fixture fixture, ulong seed)
        {
            (int opponent, HashSet<int> mine, HashSet<int> theirs) = Sides(fixture);
            SimConfig c = MatchConfig(fixture);
            SimState state = Role == CareerRole.PlayerManager
                ? QuickMatch.Match(c, World, Club, opponent, PerSide, seed, mine, theirs, Squad)
                : CareerSquads.Match(c, World, Club, opponent, PerSide, seed, bothCpu: true, homeOut: mine, awayOut: theirs, rotation: Squad);
            // #177: each footballer starts at his own fatigue from his condition.
            CareerSquads.TiredStart(state, World, LineupFor(fixture), Squad);
            return state;
        }

        /// <summary>
        /// Each side's manager's profile for the human's fixture (#182), by team id, his club the
        /// simulation's home side (#191).
        /// </summary>
        public Dictionary<int, ManagerProfile> ProfilesFor(Fixture fixture, SimConfig config)
        {
            (int opponent, _, _) = Sides(fixture);
            return CareerManagers.Profiles(World, Club, opponent, config);
        }

        // The human's opponent, and each side's footballers out of the fixture (#176).
        private (int Opponent, HashSet<int> Mine, HashSet<int> Theirs) Sides(Fixture fixture)
        {
            int opponent = fixture.Home == Club ? fixture.Away : fixture.Home;
            MatchdayKind kind = fixture.Competition == CompetitionId.Cup ? MatchdayKind.Cup : MatchdayKind.League;
            return (opponent, Availability.OutOf(World, Club, kind), Availability.OutOf(World, opponent, kind));
        }

        /// <summary>
        /// The human's fixture's lineup (#176): simulation id to the world's footballers, his club
        /// the simulation's home side, each side without its footballers out.
        /// </summary>
        public Dictionary<int, int> LineupFor(Fixture fixture)
        {
            (int opponent, HashSet<int> mine, HashSet<int> theirs) = Sides(fixture);
            return CareerSquads.Lineup(World, Club, opponent, PerSide, mine, theirs, Squad);
        }

        /// <summary>
        /// The human's fixture played: <paramref name="played"/> is from his club's side, the
        /// simulation's home; it is recorded on the fixture's ground, mirrored for an away
        /// fixture, and the rest of the matchday is quick-simmed.
        /// </summary>
        public void Finish(Fixture fixture, MatchResult played)
        {
            if (Next()?.Id != fixture.Id) throw new InvalidOperationException($"Fixture {fixture.Id} isn't the human's next.");
            MatchResult result = fixture.Home == Club ? played : played.Mirrored();
            // Its footballers by the lineup it was played with: his club the simulation's home side, whatever the ground.
            var mine = new PlayedFixture(result, LineupFor(fixture));
            CareerSeasons.PlayMatchday(Config, World, Season, Rules, PerSide, f => f.Id == fixture.Id ? mine : null, Squad);
        }

        /// <summary>The whole matchday quick-simmed, the human's fixture too.</summary>
        public void Simulate() => CareerSeasons.PlayMatchday(Config, World, Season, Rules, PerSide, (Func<Fixture, PlayedFixture?>?)null, Squad);

        /// <summary>
        /// Two footballers change places in the human's picks (#179); the first swap starts from
        /// the AI's pick. False, and nothing changed, if either isn't his or one who is out would
        /// go into the XI or onto the bench.
        /// </summary>
        public bool Swap(int a, int b)
        {
            Club mine = World.Clubs.Find(c => c.Id == Club)!;
            Player? pa = World.Players.Find(p => p.Id == a), pb = World.Players.Find(p => p.Id == b);
            if (a == b || pa == null || pb == null || pa.ClubId != Club || pb.ClubId != Club) return false;
            List<int> picks = mine.Picks.Count > 0 ? new List<int>(mine.Picks) : Picked();
            int ia = picks.IndexOf(a), ib = picks.IndexOf(b);
            if (ia < 0 && ib < 0) return false;
            if (!Fits(pb, pa, ia) || !Fits(pa, pb, ib)) return false;
            if (ia >= 0) picks[ia] = b;
            if (ib >= 0) picks[ib] = a;
            mine.Picks.Clear();
            mine.Picks.AddRange(picks);
            return true;
        }

        /// <summary>The AI's pick again (#179): the picks cleared.</summary>
        public void AutoPick() => World.Clubs.Find(c => c.Id == Club)!.Picks.Clear();

        /// <summary>The XI and bench as the human's next fixture would pick them now: his eleven, then his bench (#179).</summary>
        public List<int> Picked()
        {
            SquadSelection s = CareerSquads.Selection(World, Club, PerSide, OutOfNext(), Squad);
            var ids = new List<int>();
            foreach (Player p in s.Starters) ids.Add(p.Id);
            foreach (Player p in s.Bench) ids.Add(p.Id);
            return ids;
        }

        /// <summary>The human's footballers out of his next match (#176): of its competition, or the league's with none.</summary>
        public HashSet<int> OutOfNext()
        {
            Fixture? f = Next();
            MatchdayKind kind = f != null && f.Competition == CompetitionId.Cup ? MatchdayKind.Cup : MatchdayKind.League;
            return Availability.OutOf(World, Club, kind);
        }

        // Whether he may take the place of `from` at this index of the picks: anyone may leave the
        // picks; one out may not come in; in the XI a keeper's place is a keeper's and an
        // outfielder's an outfielder's; and a keeper may not sit on the bench, as substitutes are outfielders.
        private bool Fits(Player p, Player from, int index)
        {
            if (index < 0) return true;
            if (OutOfNext().Contains(p.Id)) return false;
            return index <= PerSide ? IsKeeper(p) == IsKeeper(from) : !IsKeeper(p);
        }

        private static bool IsKeeper(Player p) => p.Positions.Count > 0 && p.Positions[0] == Eleven.Career.Position.Goalkeeper;

        /// <summary>The human's division's table.</summary>
        public List<TableRow> Table() => SeasonEngine.Table(Season, (CompetitionId)Division, Rules);

        /// <summary>The human's club's place in its table, from 1.</summary>
        public int Position => Table().FindIndex(r => r.Club == Club) + 1;

        /// <summary>The human's last <paramref name="count"/> results, the latest first, from his club's side.</summary>
        public List<(Fixture Fixture, MatchResult Result)> LastResults(int count)
        {
            List<Matchday> calendar = SeasonCalendar.Of(Rules);
            int DayOf(Fixture f) => calendar.FindIndex(d => (d.Kind == MatchdayKind.Cup) == (f.Competition == CompetitionId.Cup) && d.Round == f.Round);
            var played = new List<(Fixture Fixture, MatchResult Result, int Day)>();
            foreach (Fixture f in Season.Fixtures)
            {
                if ((f.Home == Club || f.Away == Club) && Season.Results.TryGetValue(f.Id, out MatchResult? r)) played.Add((f, f.Home == Club ? r : r.Mirrored(), DayOf(f)));
            }

            played.Sort((a, b) => b.Day.CompareTo(a.Day));
            var last = new List<(Fixture, MatchResult)>();
            for (int i = 0; i < played.Count && i < count; i++) last.Add((played[i].Fixture, played[i].Result));
            return last;
        }

        /// <summary>The career as a save (#170), stamped with the host's clock.</summary>
        public SaveFile ToSave(long savedAt) =>
            new SaveFile { Seed = Seed, Club = Club, Role = (byte)Role, SavedAt = savedAt, World = World, Season = Season };

        /// <summary>A career from its save, its matches on <paramref name="config"/>.</summary>
        public static CareerSession FromSave(SaveFile save, SimConfig config) =>
            new CareerSession { Seed = save.Seed, Club = save.Club, Role = (CareerRole)save.Role, Config = config, World = save.World, Season = save.Season };

        /// <summary>On to the next season, once this one is over.</summary>
        public void NextSeason() => Season = SeasonEngine.Rollover(World, Season, Seed, Rules, Squad);
    }
}
