// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Eleven.Simulation.Harness
{
    /// <summary>One CPU-against-CPU eleven-a-side match of #125's contrast: home in a formation, away in the 4-4-2.</summary>
    public sealed class FormationTrial
    {
        public Formation Home;
        public ulong Seed;
        public bool Finished;
        public int HomeGoals, AwayGoals;

        /// <summary>Kicks that next reached the goal line at the end the kicker's side attacks, the frame or the other side's keeper.</summary>
        public int HomeShots, AwayShots;

        /// <summary>0–1. The share of live play in which home's footballers last played the ball.</summary>
        public double HomePossession;

        /// <summary>m. Home's outfielders' mean x in its own frame, and how far that stands above its deepest outfielder's, sampled each second in live play.</summary>
        public double HomeDepth, HomeAboveLastMan;

        /// <summary>m. The ball's mean x in home's own frame where home won it from away; NaN if it never did.</summary>
        public double HomeWinsBallAt = double.NaN;

        public double LengthSeconds;
        public ulong FinalHash;
    }

    /// <summary>
    /// #125's contrast: the same CPU sides, home in each formation against the 4-4-2, over fixed
    /// seeds at the reference config. Technical evidence that formations change the football, not feel.
    /// </summary>
    public static class FormationTrials
    {
        public const int Seeds = 6;
        public const ulong FirstSeed = 0x125100000UL;

        public static List<FormationTrial> Run(SimConfig config, int seeds = Seeds)
        {
            var trials = new List<FormationTrial>();
            foreach (Formation f in Formations.All)
            {
                for (int i = 0; i < seeds; i++) trials.Add(One(config, f, FirstSeed + (ulong)i));
            }

            return trials;
        }

        public static FormationTrial One(SimConfig config, Formation home, ulong seed)
        {
            var sim = new Simulator(config, Squads.Match(config, seed, 10, bothCpu: true, homeFormation: home));
            var recorder = new MatchRecorder();
            var trial = new FormationTrial { Home = home, Seed = seed };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            int limit = 3 * 2 * config.HalfTicks;
            int shooter = 0;
            var possession = new Possession();
            long live = 0, homeLive = 0;
            double depth = 0, above = 0, wonAt = 0;
            int samples = 0, won = 0;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                recorder.Observe(config, s, events);
                int homeSign = Sign(s, Squads.HomeTeam);
                foreach (SimEvent e in events)
                {
                    int team = TeamOf(s, e.Subject);
                    switch (e.Kind)
                    {
                        case SimEventKind.Kicked:
                        case SimEventKind.Headed:
                        case SimEventKind.Volleyed:
                            shooter = team;
                            break;
                        case SimEventKind.Goal:
                        case SimEventKind.HitFrame:
                        case SimEventKind.GoalLineCrossed:
                            if (shooter != 0 && Sign(s, shooter) * s.Ball.Position.X > 0) Shot(trial, shooter);
                            shooter = 0;
                            break;
                        case SimEventKind.KeeperHeld:
                        case SimEventKind.KeeperParried:
                            if (shooter != 0 && team != shooter) Shot(trial, shooter);
                            shooter = 0;
                            break;
                        case SimEventKind.Touched:
                        case SimEventKind.ChallengeWon:
                        case SimEventKind.SlideWon:
                            if (team != 0 && team != shooter) shooter = 0;
                            break;
                    }

                }

                if (possession.Observe(s) == Squads.HomeTeam)
                {
                    wonAt += homeSign * s.Ball.Position.X;
                    won++;
                }

                bool inPlay = s.Restart.Phase == RestartPhase.Live && (s.Match.Phase == MatchPhase.FirstHalf || s.Match.Phase == MatchPhase.SecondHalf);
                if (!inPlay) continue;
                live++;
                if (possession.Team == Squads.HomeTeam) homeLive++;
                if (s.Tick % config.TickRate != 0) continue;
                double[] x = s.Players.Where(p => p.TeamId == Squads.HomeTeam && p.Slot > 0).Select(p => homeSign * p.Position.X).ToArray();
                depth += x.Average();
                above += x.Average() - x.Min();
                samples++;
            }

            trial.Finished = recorder.Result != null;
            if (recorder.Result != null)
            {
                trial.HomeGoals = recorder.Result.HomeGoals;
                trial.AwayGoals = recorder.Result.AwayGoals;
            }

            trial.HomePossession = live > 0 ? homeLive / (double)live : 0;
            trial.HomeDepth = samples > 0 ? depth / samples : 0;
            trial.HomeAboveLastMan = samples > 0 ? above / samples : 0;
            if (won > 0) trial.HomeWinsBallAt = wonAt / won;
            trial.LengthSeconds = sim.Tick * config.TickSeconds;
            trial.FinalHash = sim.StateHash();
            return trial;
        }

        private static void Shot(FormationTrial trial, int team)
        {
            if (team == Squads.HomeTeam) trial.HomeShots++;
            else trial.AwayShots++;
        }

        private static int Sign(SimState s, int team)
        {
            foreach (TeamState t in s.Teams)
            {
                if (t.Id == team) return t.AttackSign;
            }

            return 0;
        }

        internal static int TeamOf(SimState s, int id)
        {
            foreach (PlayerState p in s.Players)
            {
                if (p.Id == id) return p.TeamId;
            }

            return 0;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IEnumerable<FormationTrial> trials)
        {
            w.WriteLine("config_hash,formation,seed,finished,home_goals,away_goals,home_shots,away_shots,home_possession,home_depth_m,home_above_last_man_m,home_wins_ball_at_m,length_s,final_hash");
            foreach (FormationTrial t in trials)
            {
                w.WriteLine(string.Join(",",
                    config.Hash.ToString("x16"), Formations.Name(t.Home), t.Seed.ToString("x"), t.Finished ? "yes" : "no", t.HomeGoals, t.AwayGoals, t.HomeShots, t.AwayShots,
                    F(t.HomePossession), F(t.HomeDepth), F(t.HomeAboveLastMan), double.IsNaN(t.HomeWinsBallAt) ? "" : F(t.HomeWinsBallAt), F(t.LengthSeconds), t.FinalHash.ToString("x16")));
            }
        }

        /// <summary>Each formation's means over its seeds, one line each.</summary>
        public static string KeyNumbers(IReadOnlyList<FormationTrial> trials)
        {
            var lines = new List<string>();
            foreach (IGrouping<Formation, FormationTrial> g in trials.GroupBy(t => t.Home))
            {
                double Mean(Func<FormationTrial, double> f) => g.Average(f);
                double wins = g.Where(t => !double.IsNaN(t.HomeWinsBallAt)).Select(t => t.HomeWinsBallAt).DefaultIfEmpty(double.NaN).Average();
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: goals {1:F1}-{2:F1}, shots {3:F1}-{4:F1}, possession {5:P0}, depth {6:F1} m, above last man {7:F1} m, wins ball at {8:F1} m",
                    Formations.Name(g.Key), Mean(t => t.HomeGoals), Mean(t => t.AwayGoals), Mean(t => t.HomeShots), Mean(t => t.AwayShots),
                    Mean(t => t.HomePossession), Mean(t => t.HomeDepth), Mean(t => t.HomeAboveLastMan), wins));
            }

            return string.Join("; ", lines);
        }

        private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Which side has the ball (#125's contrast): the side that last played it, by the
    /// simulation's own record (touch, kick, header, volley, challenge or keeper). Observed once a
    /// step, after it.
    /// </summary>
    public sealed class Possession
    {
        /// <summary>The side in possession; 0 before anyone has played the ball.</summary>
        public int Team { get; private set; }

        /// <summary>The side that won the ball from the other in this step, or 0.</summary>
        public int Observe(SimState s)
        {
            int was = Team, team = FormationTrials.TeamOf(s, s.Ball.LastPlayedBy);
            if (team != 0) Team = team;

            return was != 0 && Team != was ? Team : 0;
        }
    }
}
