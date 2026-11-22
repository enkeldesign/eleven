using System;

namespace Eleven.Simulation.Encoding
{
    /// <summary>
    /// The match-result contract's own bytes (#170, INV-14): every field of a
    /// <see cref="MatchResult"/>, its metrics included, canonical and with its schema version,
    /// so a career's saved results read back exactly. A result of another schema is refused.
    /// </summary>
    public static class MatchResultCodec
    {
        public static void Write(CanonicalWriter w, MatchResult r)
        {
            w.WriteInt32(MatchResult.SchemaVersion);
            w.WriteInt32(r.HomeTeam);
            w.WriteInt32(r.AwayTeam);
            w.WriteInt32(r.HomeGoals);
            w.WriteInt32(r.AwayGoals);
            w.WriteDouble(r.HalfSeconds, "result.half");
            w.WriteDouble(r.PlayedSeconds, "result.played");
            w.WriteInt32(r.Restarts);
            w.WriteInt32(r.HomeFouls);
            w.WriteInt32(r.AwayFouls);
            w.WriteInt32(r.HomePenalties);
            w.WriteInt32(r.AwayPenalties);
            w.WriteInt32(r.HomeCautions);
            w.WriteInt32(r.AwayCautions);
            w.WriteInt32(r.HomeSendingsOff);
            w.WriteInt32(r.AwaySendingsOff);
            w.WriteInt32(r.HomeInjuries);
            w.WriteInt32(r.AwayInjuries);
            w.WriteInt32(r.HomeSubstitutions);
            w.WriteInt32(r.AwaySubstitutions);
            w.WriteInt32(r.ExtraTime);
            w.WriteInt32(r.HomeShootoutKicks);
            w.WriteInt32(r.AwayShootoutKicks);
            w.WriteInt32(r.HomeShootoutGoals);
            w.WriteInt32(r.AwayShootoutGoals);
            WriteMetrics(w, r.HomeMetrics);
            WriteMetrics(w, r.AwayMetrics);
            w.WriteInt32(r.Goals.Count);
            foreach (ResultGoal g in r.Goals)
            {
                w.WriteInt32(g.Team);
                w.WriteInt32(g.Scorer);
                w.WriteInt32(g.Half);
                w.WriteDouble(g.ClockSeconds, "result.goal");
            }
        }

        private static void WriteMetrics(CanonicalWriter w, SideMetrics m)
        {
            w.WriteDouble(m.Possession, "result.possession");
            w.WriteInt32(m.Shots);
            w.WriteInt32(m.OnTarget);
            w.WriteDouble(m.Xg, "result.xg");
            w.WriteInt32(m.Passes);
            w.WriteInt32(m.PassesCompleted);
            w.WriteInt32(m.Corners);
            w.WriteDouble(m.Ppda, "result.ppda");
        }

        private static SideMetrics ReadMetrics(CanonicalReader r) =>
            new SideMetrics(r.ReadDouble("result.possession"), r.ReadInt32(), r.ReadInt32(), r.ReadDouble("result.xg"), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble("result.ppda"));

        public static MatchResult Read(CanonicalReader r)
        {
            int schema = r.ReadInt32();
            if (schema != MatchResult.SchemaVersion) throw new FormatException($"Result schema {schema}; this build reads {MatchResult.SchemaVersion}.");
            int homeTeam = r.ReadInt32(), awayTeam = r.ReadInt32(), homeGoals = r.ReadInt32(), awayGoals = r.ReadInt32();
            double half = r.ReadDouble("result.half"), played = r.ReadDouble("result.played");
            int restarts = r.ReadInt32();
            int homeFouls = r.ReadInt32(), awayFouls = r.ReadInt32(), homePenalties = r.ReadInt32(), awayPenalties = r.ReadInt32();
            int homeCautions = r.ReadInt32(), awayCautions = r.ReadInt32(), homeOff = r.ReadInt32(), awayOff = r.ReadInt32();
            int homeInjuries = r.ReadInt32(), awayInjuries = r.ReadInt32(), homeSubs = r.ReadInt32(), awaySubs = r.ReadInt32();
            int extraTime = r.ReadInt32(), homeKicks = r.ReadInt32(), awayKicks = r.ReadInt32(), homeShootout = r.ReadInt32(), awayShootout = r.ReadInt32();
            SideMetrics homeMetrics = ReadMetrics(r), awayMetrics = ReadMetrics(r);
            int count = r.ReadInt32();
            if (count < 0 || count > 10_000) throw new FormatException($"Invalid goal count {count}.");
            var goals = new ResultGoal[count];
            for (int i = 0; i < count; i++) goals[i] = new ResultGoal(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble("result.goal"));
            return new MatchResult(homeTeam, awayTeam, homeGoals, awayGoals, half, played, goals, restarts,
                homeFouls, awayFouls, homePenalties, awayPenalties, homeCautions, awayCautions, homeOff, awayOff,
                homeInjuries, awayInjuries, homeSubs, awaySubs, extraTime, homeKicks, awayKicks, homeShootout, awayShootout)
            {
                HomeMetrics = homeMetrics,
                AwayMetrics = awayMetrics,
            };
        }
    }
}
