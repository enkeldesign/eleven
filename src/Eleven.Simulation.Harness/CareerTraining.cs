using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Training measured (#196): a career rolled forward headless with every club at one
    /// intensity, for each in turn, then all on Auto: the young's growth, the condition at
    /// kick-off, training injuries and the days they cost, and the world's drift.
    /// </summary>
    public static class CareerTraining
    {
        /// <summary>One intensity's seasons, every club at it.</summary>
        public sealed class Run
        {
            public TrainingIntensity Intensity;

            /// <summary>The under-24s' level gained a season, those still at it by the season's end.</summary>
            public double YoungGrowth;

            /// <summary>Every club's XI's mean condition at its league kick-offs.</summary>
            public double KickOffCondition;

            public double InjuriesPerClubSeason, DaysPerClubSeason;

            /// <summary>The world's mean level at the last season's start, less the first's (#194).</summary>
            public double Drift;
        }

        private static readonly TrainingIntensity[] Order = { TrainingIntensity.Light, TrainingIntensity.Normal, TrainingIntensity.Hard, TrainingIntensity.Auto };

        /// <summary>Each intensity in turn, then Auto, over <paramref name="seasons"/> seasons from <paramref name="seed"/>.</summary>
        public static List<Run> Measure(ulong seed, int seasons)
        {
            var runs = new List<Run>();
            foreach (TrainingIntensity t in Order) runs.Add(One(seed, seasons, t));
            return runs;
        }

        private static Run One(ulong seed, int seasons, TrainingIntensity intensity)
        {
            World w = CareerSession.NewWorld(seed);
            CareerSession s = CareerSession.Start(w, seed, w.Clubs[0].Id, CareerRole.ManagerOnly, SpikeScenario.Config());
            foreach (Club c in w.Clubs) c.Training = intensity;
            double start = w.Players.Average(Development.Level), last = start;
            double growth = 0, condition = 0;
            int conditions = 0, injuries = 0, days = 0;
            for (int i = 1; i <= seasons; i++)
            {
                last = w.Players.Average(Development.Level);
                Dictionary<int, double> young = w.Players.Where(p => p.Age < 24).ToDictionary(p => p.Id, Development.Level);
                while (!s.SeasonOver)
                {
                    List<Matchday> calendar = SeasonCalendar.Of(s.Rules);
                    if (calendar[s.Season.Matchday].Kind == MatchdayKind.League)
                    {
                        foreach (Club c in w.Clubs) condition += CareerSeasons.XiCondition(w, c.Id, CareerSession.PerSide, MatchdayKind.League, s.Squad);
                        conditions += w.Clubs.Count;
                    }

                    CareerSeasons.PlayMatchday(s.Config, w, s.Season, s.Rules, CareerSession.PerSide, (Func<Fixture, PlayedFixture?>?)null, s.Squad, injury =>
                    {
                        injuries++;
                        days += injury.Days;
                    });
                }

                List<double> gains = w.Players.Where(p => young.ContainsKey(p.Id)).Select(p => Development.Level(p) - young[p.Id]).ToList();
                growth += gains.Count > 0 ? gains.Average() : 0;
                if (i < seasons) s.NextSeason();
            }

            int clubSeasons = w.Clubs.Count * seasons;
            return new Run
            {
                Intensity = intensity,
                YoungGrowth = growth / seasons,
                KickOffCondition = conditions > 0 ? condition / conditions : 100,
                InjuriesPerClubSeason = (double)injuries / clubSeasons,
                DaysPerClubSeason = (double)days / clubSeasons,
                Drift = last - start,
            };
        }

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons)
        {
            var t = new StringBuilder();
            t.AppendLine(FormattableString.Invariant($"Every club at one intensity, {seasons} season{(seasons == 1 ? "" : "s")} from seed {seed:x}:"));
            t.AppendLine();
            t.AppendLine("| Training | Under-24s' level a season | XI's condition at kick-off | Training injuries a club a season | Days lost a club a season | Drift by the last season |");
            t.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (Run r in Measure(seed, seasons))
            {
                t.AppendLine(FormattableString.Invariant($"| {r.Intensity} | {r.YoungGrowth:+0.00;−0.00;0.00} | {r.KickOffCondition:F1} | {r.InjuriesPerClubSeason:F1} | {r.DaysPerClubSeason:F0} | {r.Drift:+0.00;−0.00;0.00} |"));
            }

            return t.ToString().Replace("\r\n", "\n");
        }
    }
}
