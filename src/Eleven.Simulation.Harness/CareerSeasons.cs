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
    public static class CareerSeasons
    {
        /// <summary>
        /// A fixture's quick-sim result: its own seed from the season's and the fixture's id; a
        /// cup tie as a knockout; the final, at a neutral ground, without home advantage.
        /// </summary>
        public static MatchResult Quick(SimConfig config, World world, Season season, Fixture fixture, int perSide)
        {
            bool cup = fixture.Competition == CompetitionId.Cup;
            SimConfig c = cup ? config.With("knockout", 1) : config;
            QuickSimModel model = QuickSimModel.For(perSide);
            if (fixture.Neutral) model = WithoutHome(model);
            ulong seed = CounterRng.Hash(season.Seed, fixture.Id, RandomStream.QuickSim, 0, 0);
            return QuickSim.Play(c, CareerQuickSim.Side(world, fixture.Home, perSide), CareerQuickSim.Side(world, fixture.Away, perSide), seed, model: model,
                homePlayers: CareerQuickSim.HomeIds(perSide), awayPlayers: CareerQuickSim.AwayIds(perSide));
        }

        private static QuickSimModel WithoutHome(QuickSimModel m)
        {
            var tiers = new double[QuickSimModel.Tiers];
            for (int t = 0; t < tiers.Length; t++) tiers[t] = m.TierRating(t);
            return new QuickSimModel(m.BaseRate, 0, tiers, m.CapabilityWeight, m.Rho, m.FoulsPerSide, m.CautionsPerSide, m.SendingsOffPerSide, m.ShootoutConversion);
        }

        /// <summary>Plays the next matchday: each fixture by the hook if it gives a result, else the quick-sim; then on to the next.</summary>
        public static List<Fixture> PlayMatchday(SimConfig config, World world, Season season, CompetitionRules rules, int perSide, Func<Fixture, MatchResult?>? hook = null)
        {
            List<Fixture> day = SeasonEngine.Next(season, rules);
            foreach (Fixture f in day) SeasonEngine.Record(season, rules, f.Id, hook?.Invoke(f) ?? Quick(config, world, season, f, perSide));
            SeasonEngine.Advance(season, rules);
            return day;
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
