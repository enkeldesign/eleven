using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Headless seasons (#164): every fixture of a season played by the quick-sim (#163), league
    /// matches as league matches and cup ties as knockouts, unless a hook plays it another way:
    /// M6.06's human fixture, played by the full simulation.
    /// </summary>
    /// <summary>A fixture played another way than the quick-sim (#176): its result, and its lineup when it names footballers (simulation id to the world's player id).</summary>
    public sealed class PlayedFixture
    {
        public PlayedFixture(MatchResult result, IReadOnlyDictionary<int, int>? lineup)
        {
            Result = result;
            Lineup = lineup;
        }

        public MatchResult Result { get; }

        /// <summary>Null: the fixture's own clubs' selections, home as the simulation's home side.</summary>
        public IReadOnlyDictionary<int, int>? Lineup { get; }
    }

    public static class CareerSeasons
    {
        /// <summary>
        /// A fixture's quick-sim result: its own seed from the season's and the fixture's id; a
        /// cup tie as a knockout; the final, at a neutral ground, without home advantage.
        /// </summary>
        public static MatchResult Quick(SimConfig config, World world, Season season, Fixture fixture, int perSide, ISet<int>? homeOut = null, ISet<int>? awayOut = null, SquadRules? squad = null)
        {
            bool cup = fixture.Competition == CompetitionId.Cup;
            SimConfig c = cup ? config.With("knockout", 1) : config;
            QuickSimModel model = QuickSimModel.For(perSide);
            if (fixture.Neutral) model = WithoutHome(model);
            ulong seed = CounterRng.Hash(season.Seed, fixture.Id, RandomStream.QuickSim, 0, 0);
            return QuickSim.Play(c, CareerQuickSim.Side(world, fixture.Home, perSide, homeOut, squad), CareerQuickSim.Side(world, fixture.Away, perSide, awayOut, squad), seed, model: model,
                homePlayers: CareerQuickSim.HomeIds(perSide), awayPlayers: CareerQuickSim.AwayIds(perSide));
        }

        /// <summary>A neutral ground's model (#163): the same in every way but its home advantage.</summary>
        public static QuickSimModel WithoutHome(QuickSimModel m)
        {
            var tiers = new double[QuickSimModel.Tiers];
            for (int t = 0; t < tiers.Length; t++) tiers[t] = m.TierRating(t);
            return new QuickSimModel(m.BaseRate, 0, tiers, m.CapabilityWeight, m.Rho, m.FoulsPerSide, m.CautionsPerSide, m.SendingsOffPerSide, m.ShootoutConversion,
                m.MinorInjuriesPerSide, m.SeriousInjuriesPerSide, m.FatigueWeight);
        }

        /// <summary>Plays the next matchday: each fixture by the hook if it gives a result, else the quick-sim; then on to the next.</summary>
        public static List<Fixture> PlayMatchday(SimConfig config, World world, Season season, CompetitionRules rules, int perSide, Func<Fixture, MatchResult?>? hook = null) =>
            PlayMatchday(config, world, season, rules, perSide, hook == null ? (Func<Fixture, PlayedFixture?>?)null : f => hook(f) is MatchResult r ? new PlayedFixture(r, null) : null);

        /// <summary>
        /// Plays the next matchday (#176): each club's footballers out (<see cref="Availability"/>)
        /// left out of its XI; each fixture by the hook if it gives one, else the quick-sim; then
        /// the matchday's bans served and days passed, its cards and injuries applied, and on.
        /// </summary>
        public static List<Fixture> PlayMatchday(SimConfig config, World world, Season season, CompetitionRules rules, int perSide, Func<Fixture, PlayedFixture?>? hook, SquadRules? squad = null,
            Action<TrainingInjury>? trained = null, TrainingRules? training = null)
        {
            SquadRules sr = squad ?? SquadRules.Reference;
            List<Fixture> day = SeasonEngine.Next(season, rules);
            MatchdayKind kind = SeasonCalendar.Of(rules)[season.Matchday].Kind;

            // Each club's XI's condition at this kick-off (#196), for its manager's training after it.
            var kickOff = new Dictionary<int, double>();
            foreach (Club c in world.Clubs) kickOff[c.Id] = XiCondition(world, c.Id, perSide, kind, sr);
            var played = new List<(Fixture Fixture, MatchResult Result, IReadOnlyDictionary<int, int> Lineup)>(day.Count);
            foreach (Fixture f in day)
            {
                // Who's out is read before the matchday: nothing it brings counts until the next.
                HashSet<int> homeOut = Availability.OutOf(world, f.Home, kind), awayOut = Availability.OutOf(world, f.Away, kind);
                PlayedFixture? other = hook?.Invoke(f);
                MatchResult r = other?.Result ?? Quick(config, world, season, f, perSide, homeOut, awayOut, sr);
                IReadOnlyDictionary<int, int> lineup = other?.Lineup ?? CareerSquads.Lineup(world, f.Home, f.Away, perSide, homeOut, awayOut, sr);
                SeasonEngine.Record(season, rules, f.Id, r);
                ManagerMemory.Remember(world, season, f, r);
                played.Add((f, r, lineup));
            }

            // The bans of those who sat it out are served before its own cards count; its injuries
            // count from its own day, and the days to the next matchday pass.
            Availability.Serve(world, day, kind, sr);
            foreach ((Fixture f, MatchResult r, IReadOnlyDictionary<int, int> lineup) in played) Availability.Apply(world, season, f, r, lineup, sr);

            // Development (#194): each starter's start counted, and the young grown by a matchday.
            var starters = new List<int>();
            foreach ((_, _, IReadOnlyDictionary<int, int> lineup) in played)
            {
                foreach (KeyValuePair<int, int> kv in lineup)
                {
                    if (kv.Key <= Squads.AwayKeeper(perSide)) starters.Add(kv.Value);
                }
            }

            // Training (#196): each club's intensity for the days to the next matchday, its own or its manager's.
            int gap = Availability.GapAfter(rules, season.Matchday);
            var intensity = new Dictionary<int, TrainingIntensity>();
            var growth = new Dictionary<int, double>();
            foreach (Club c in world.Clubs)
            {
                TrainingIntensity t = c.Training != TrainingIntensity.Auto ? c.Training : Training.Choose(kickOff[c.Id], gap, training);
                intensity[c.Id] = t;
                growth[c.Id] = Training.Of((training ?? TrainingRules.Reference).Growth, t);
            }

            Development.Matchday(world, season, starters, SeasonCalendar.Of(rules).Count, null, growth);
            foreach (TrainingInjury injury in Training.Pass(world, season, gap, intensity, sr, training)) trained?.Invoke(injury);

            // Money (#198): the matchday's home takings, then the days' wages and broadcasting.
            var fixtures = new List<Fixture>(played.Count);
            foreach ((Fixture f, _, _) in played) fixtures.Add(f);
            Finance.Matchday(world, fixtures);
            Finance.Pass(world, gap);

            // The board (#200): each result against its club's expected.
            var results = new List<(Fixture, MatchResult)>(played.Count);
            foreach ((Fixture f, MatchResult r, _) in played) results.Add((f, r));
            Board.Matchday(world, results);
            SeasonEngine.Advance(season, rules);
            return day;
        }

        /// <summary>A club's XI's mean condition (#196): its starters for a match of this kind, as the AI picks them.</summary>
        public static double XiCondition(World world, int club, int perSide, MatchdayKind kind, SquadRules? squad = null)
        {
            SquadSelection xi = CareerSquads.Selection(world, club, perSide, Availability.OutOf(world, club, kind), squad);
            if (xi.Starters.Count == 0) return 100;
            double sum = 0;
            foreach (Player p in xi.Starters) sum += p.Fitness;
            return sum / xi.Starters.Count;
        }

        /// <summary>Plays the season to its end.</summary>
        public static void PlaySeason(SimConfig config, World world, Season season, CompetitionRules rules, int perSide, Func<Fixture, MatchResult?>? hook = null)
        {
            while (!SeasonEngine.Finished(season, rules)) PlayMatchday(config, world, season, rules, perSide, hook);
        }

        /// <summary>From a new career's first season, <paramref name="seasons"/> seasons played and rolled over: every season, the last one not yet rolled.</summary>
        public static List<Season> RollForward(SimConfig config, World world, ulong careerSeed, int seasons, CompetitionRules rules, int perSide)
        {
            var played = new List<Season>(seasons);
            Season s = SeasonEngine.Start(world, 1, careerSeed, SeasonEngine.FirstRanking(world), rules);
            for (int i = 1; i <= seasons; i++)
            {
                PlaySeason(config, world, s, rules, perSide);
                played.Add(s);
                if (i < seasons) s = SeasonEngine.Rollover(world, s, careerSeed, rules);
            }

            return played;
        }
    }
}
