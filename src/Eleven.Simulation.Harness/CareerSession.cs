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
            int opponent = fixture.Home == Club ? fixture.Away : fixture.Home;
            SimConfig c = MatchConfig(fixture);
            return Role == CareerRole.PlayerManager
                ? QuickMatch.Match(c, World, Club, opponent, PerSide, seed)
                : CareerSquads.Match(c, World, Club, opponent, PerSide, seed, bothCpu: true, homeTactics: MatchPlans.Plans, awayTactics: MatchPlans.Plans);
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
            CareerSeasons.PlayMatchday(Config, World, Season, Rules, PerSide, f => f.Id == fixture.Id ? result : null);
        }

        /// <summary>The whole matchday quick-simmed, the human's fixture too.</summary>
        public void Simulate() => CareerSeasons.PlayMatchday(Config, World, Season, Rules, PerSide);

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

        /// <summary>On to the next season, once this one is over.</summary>
        public void NextSeason() => Season = SeasonEngine.Rollover(World, Season, Seed, Rules);
    }
}
