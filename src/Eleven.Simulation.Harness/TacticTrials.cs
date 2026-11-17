// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Eleven.Simulation.Harness
{
    /// <summary>One CPU-against-CPU eleven-a-side match of #126's measures.</summary>
    public sealed class TacticTrial
    {
        /// <summary>"plan" for the plans' contrast, "switch" for the switch's cost.</summary>
        public string Kind = "";

        /// <summary>The home side's plan, or the switch preset's name.</summary>
        public string Name = "";

        public ulong Seed;
        public bool Finished;
        public int HomeGoals, AwayGoals, HomeShots, AwayShots;
        public double HomePossession;

        /// <summary>m. Home's outfielders' mean x in its own frame, sampled each second in live play.</summary>
        public double HomeDepth;

        /// <summary>Away's passes in its own 60 % of the pitch per home defensive action there; NaN with none.</summary>
        public double Ppda = double.NaN;

        /// <summary>The switch's cost: the tick home's switch was taken (−1 none), and the shots and goals away made in the window after it.</summary>
        public long SwitchedAt = -1;

        public int WindowShots, WindowGoals;

        public double LengthSeconds;
        public ulong FinalHash;
    }

    /// <summary>
    /// #126's measures. The plans' contrast: home on the reference plan, a low block and a high
    /// press, against the reference, CPU against CPU. The switch's cost: home switches from the
    /// reference to the high press at a third of the match, under each preset of the switch's
    /// knobs; the shots and goals away makes in the window after it, against the same seeds and
    /// window with no switch. Technical evidence, not feel.
    /// </summary>
    public static class TacticTrials
    {
        public const int PlanSeeds = 6, SwitchSeeds = 4;
        public const ulong FirstSeed = 0x126100000UL;

        /// <summary>s. The window after a switch in which away's shots and goals are its cost.</summary>
        public const double Window = 20;

        /// <summary>A low block: a 4-4-2 dropped 8 m, narrower, stepping up 2 m, pressing only from 10 m inside its own half.</summary>
        public static readonly TeamTactic LowBlock = new TeamTactic { Formation = Formation.FourFourTwo, StepUp = 2, Line = -8, Width = 0.85, Press = -10 };

        /// <summary>A high press: a 4-4-2 pushed up 6 m, wider, stepping up 10 m, pressing anywhere.</summary>
        public static readonly TeamTactic HighPress = new TeamTactic { Formation = Formation.FourFourTwo, StepUp = 10, Line = 6, Width = 1.1, Press = TeamTactic.PressMax };

        /// <summary>The switch presets: name, planSwitchAt, planSettleSeconds.</summary>
        public static readonly (string Name, double At, double Settle)[] Presets =
        {
            ("none", 0, 8), ("stoppage-8s", 0, 8), ("at-once-8s", 1, 8), ("stoppage-0s", 0, 0), ("stoppage-20s", 0, 20),
        };

        public static List<TacticTrial> Run(SimConfig config, int planSeeds = PlanSeeds, int switchSeeds = SwitchSeeds)
        {
            var trials = new List<TacticTrial>();
            foreach ((string name, TeamTactic plan) in new[] { ("reference", TeamTactic.Reference(0)), ("low-block", LowBlock), ("high-press", HighPress) })
            {
                for (int i = 0; i < planSeeds; i++) trials.Add(One(config, "plan", name, new[] { plan, plan, plan }, FirstSeed + (ulong)i, switchAt: false));
            }

            TeamTactic[] switching = { TeamTactic.Reference(0), HighPress, LowBlock };
            foreach ((string name, double at, double settle) in Presets)
            {
                SimConfig c = config.With("planSwitchAt", at).With("planSettleSeconds", settle);
                for (int i = 0; i < switchSeeds; i++) trials.Add(One(c, "switch", name, switching, FirstSeed + 0x100 + (ulong)i, switchAt: name != "none"));
            }

            return trials;
        }

        public static TacticTrial One(SimConfig config, string kind, string name, TeamTactic[] homePlans, ulong seed, bool switchAt)
        {
            var sim = new Simulator(config, Squads.Match(config, seed, 10, bothCpu: true, homeTactics: homePlans));
            var recorder = new MatchRecorder();
            var trial = new TacticTrial { Kind = kind, Name = name, Seed = seed };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            var possession = new Possession();
            int limit = 3 * 2 * config.HalfTicks;
            long switchTick = 2 * config.HalfTicks / 3, windowFrom = -1;
            int windowTicks = (int)(Window * config.TickRate);
            bool asked = false;
            int shooter = 0, awayPasses = 0, homeActions = 0, passer = 0;
            long live = 0, homeLive = 0;
            double depth = 0;
            int samples = 0;
            int homeController = sim.PeekState().Players.First(p => p.TeamId == Squads.HomeTeam && p.Slot > 0).OwnerControllerId;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                if (switchAt && !asked && sim.Tick >= switchTick)
                {
                    // Home's manager switches to its second plan through one of its footballers' controllers.
                    int at = commands.FindIndex(c => c.ControllerId == homeController);
                    if (at >= 0) commands[at] = commands[at].WithTactic(2);
                    else commands.Add(new Command(homeController, sim.Tick, Vec2.Zero, tacticTo: 2));
                    asked = true;
                }

                // The window is the same span of the clock with or without a switch: from a third of the match.
                if (!switchAt && windowFrom < 0 && sim.Tick >= switchTick) windowFrom = sim.Tick;
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                recorder.Observe(config, s, events);
                int homeSign = Sign(s, Squads.HomeTeam), awaySign = -homeSign;
                foreach (SimEvent e in events)
                {
                    int team = FormationTrials.TeamOf(s, e.Subject);
                    if (e.Kind == SimEventKind.TacticSwitched && e.Subject == Squads.HomeTeam)
                    {
                        trial.SwitchedAt = e.Tick;
                        windowFrom = e.Tick;
                    }

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
                            if (shooter != 0 && Sign(s, shooter) * s.Ball.Position.X > 0) Shot(trial, shooter, e.Tick, windowFrom, windowTicks, e.Kind == SimEventKind.Goal);
                            shooter = 0;
                            break;
                        case SimEventKind.KeeperHeld:
                        case SimEventKind.KeeperParried:
                            if (shooter != 0 && team != shooter) Shot(trial, shooter, e.Tick, windowFrom, windowTicks, false);
                            shooter = 0;
                            break;
                        case SimEventKind.Touched:
                        case SimEventKind.ChallengeWon:
                        case SimEventKind.SlideWon:
                            if (team != 0 && team != shooter) shooter = 0;
                            break;
                    }

                    // PPDA: away's passes in its own 60 %, and home's defensive actions there (a
                    // challenge or slide won, a foul, or a touch that cuts out an away pass).
                    bool inAwaysSixty = awaySign * s.Ball.Position.X < -config.GoalLineX + 0.6 * 2 * config.GoalLineX;
                    if (e.Kind == SimEventKind.QuickPassResolved)
                    {
                        passer = team;
                        if (team == Squads.AwayTeam && inAwaysSixty) awayPasses++;
                    }
                    else if (team == Squads.HomeTeam && inAwaysSixty && (e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.SlideWon || e.Kind == SimEventKind.Foul || (e.Kind == SimEventKind.Touched && passer == Squads.AwayTeam)))
                    {
                        homeActions++;
                    }

                    if (e.Kind == SimEventKind.Touched && team != 0) passer = 0;
                }

                possession.Observe(s);
                bool inPlay = s.Restart.Phase == RestartPhase.Live && (s.Match.Phase == MatchPhase.FirstHalf || s.Match.Phase == MatchPhase.SecondHalf);
                if (!inPlay) continue;
                live++;
                if (possession.Team == Squads.HomeTeam) homeLive++;
                if (s.Tick % config.TickRate != 0) continue;
                depth += s.Players.Where(p => p.TeamId == Squads.HomeTeam && p.Slot > 0).Average(p => homeSign * p.Position.X);
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
            if (homeActions > 0) trial.Ppda = awayPasses / (double)homeActions;
            trial.LengthSeconds = sim.Tick * config.TickSeconds;
            trial.FinalHash = sim.StateHash();
            return trial;
        }

        private static void Shot(TacticTrial trial, int team, long tick, long windowFrom, int windowTicks, bool goal)
        {
            if (team == Squads.HomeTeam)
            {
                trial.HomeShots++;
                return;
            }

            trial.AwayShots++;
            if (windowFrom >= 0 && tick >= windowFrom && tick < windowFrom + windowTicks)
            {
                trial.WindowShots++;
                if (goal) trial.WindowGoals++;
            }
        }

        private static int Sign(SimState s, int team)
        {
            foreach (TeamState t in s.Teams)
            {
                if (t.Id == team) return t.AttackSign;
            }

            return 0;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IEnumerable<TacticTrial> trials)
        {
            w.WriteLine("config_hash,kind,name,seed,finished,home_goals,away_goals,home_shots,away_shots,home_possession,home_depth_m,ppda,switched_s,window_shots,window_goals,length_s,final_hash");
            foreach (TacticTrial t in trials)
            {
                w.WriteLine(string.Join(",",
                    config.Hash.ToString("x16"), t.Kind, t.Name, t.Seed.ToString("x"), t.Finished ? "yes" : "no", t.HomeGoals, t.AwayGoals, t.HomeShots, t.AwayShots,
                    F(t.HomePossession), F(t.HomeDepth), double.IsNaN(t.Ppda) ? "" : F(t.Ppda), t.SwitchedAt < 0 ? "" : F(t.SwitchedAt * config.TickSeconds),
                    t.WindowShots, t.WindowGoals, F(t.LengthSeconds), t.FinalHash.ToString("x16")));
            }
        }

        public static string KeyNumbers(IReadOnlyList<TacticTrial> trials)
        {
            var lines = new List<string>();
            foreach (IGrouping<string, TacticTrial> g in trials.Where(t => t.Kind == "plan").GroupBy(t => t.Name))
            {
                double ppda = g.Where(t => !double.IsNaN(t.Ppda)).Select(t => t.Ppda).DefaultIfEmpty(double.NaN).Average();
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: goals {1:F1}-{2:F1}, shots {3:F1}-{4:F1}, possession {5:P0}, depth {6:F1} m, PPDA {7:F1}",
                    g.Key, g.Average(t => t.HomeGoals), g.Average(t => t.AwayGoals), g.Average(t => t.HomeShots), g.Average(t => t.AwayShots), g.Average(t => t.HomePossession), g.Average(t => t.HomeDepth), ppda));
            }

            foreach (IGrouping<string, TacticTrial> g in trials.Where(t => t.Kind == "switch").GroupBy(t => t.Name))
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture, "switch {0}: conceded {1:F2} shots, {2:F2} goals in {3:F0} s",
                    g.Key, g.Average(t => t.WindowShots), g.Average(t => t.WindowGoals), Window));
            }

            return string.Join("; ", lines);
        }

        private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }
}
