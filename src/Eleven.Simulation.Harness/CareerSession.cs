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

        /// <summary>A new career's world, for choosing a club before it starts.</summary>
        public static World NewWorld(ulong seed) => new World();

        /// <summary>A new career at <paramref name="club"/> in <paramref name="world"/>: its first season.</summary>
        public static CareerSession Start(World world, ulong seed, int club, CareerRole role, SimConfig config) => new CareerSession();

        public bool SeasonOver => true;

        /// <summary>The human's club's division, from 1.</summary>
        public int Division => 0;

        /// <summary>The human's fixture on the next matchday, or null: a bye, out of the cup, or the season over.</summary>
        public Fixture? Next() => null;

        /// <summary>A fixture's match config: a cup tie is a knockout.</summary>
        public SimConfig MatchConfig(Fixture fixture) => Config;

        /// <summary>The kick-off of the human's fixture: his club is the simulation's home side.</summary>
        public SimState MatchFor(Fixture fixture, ulong seed) => Squads.Match(Config, seed, PerSide);

        /// <summary>The human's fixture played: its result, from his club's side, recorded on the fixture's ground; the rest of the matchday quick-simmed.</summary>
        public void Finish(Fixture fixture, MatchResult played) { }

        /// <summary>The whole matchday quick-simmed, the human's fixture too.</summary>
        public void Simulate() { }

        /// <summary>The human's division's table.</summary>
        public List<TableRow> Table() => new List<TableRow>();

        /// <summary>The human's club's place in its table, from 1.</summary>
        public int Position => 0;

        /// <summary>The human's last <paramref name="count"/> results, the latest first, from his club's side.</summary>
        public List<(Fixture Fixture, MatchResult Result)> LastResults(int count) => new List<(Fixture, MatchResult)>();

        /// <summary>On to the next season, once this one is over.</summary>
        public void NextSeason() { }
    }
}
