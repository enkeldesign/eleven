// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Eleven.Simulation.Harness
{
    /// <summary>One eleven-a-side match of #127's measures: CPU against CPU, or the scripted seat against a CPU side.</summary>
    public sealed class ManagerTrial
    {
        /// <summary>"cpu" or "seat".</summary>
        public string Kind = "";

        /// <summary>The policy preset: "off", "cautious" or "bold".</summary>
        public string Preset = "";

        public ulong Seed;
        public bool Finished;
        public int HomeGoals, AwayGoals;
        public int HomeSwitches, AwaySwitches;

        /// <summary>Switches to each plan: balanced, chase, protect.</summary>
        public int ToBalanced, ToChase, ToProtect;

        /// <summary>The share of the match at each switch, and the switching side's margin then.</summary>
        public List<double> SwitchShares = new List<double>();

        public List<int> SwitchMargins = new List<int>();

        public int Substitutions;

        /// <summary>The most switches a side may make in the match's clock under the cooldown.</summary>
        public int SwitchBound;

        public double LengthSeconds;
        public ulong FinalHash;
    }

    /// <summary>
    /// #127's measures: two CPU sides, each with a balanced (reference), a chase (#126's high press)
    /// and a protect (#126's low block) plan, under each policy preset over fixed seeds; and the
    /// M4 review's scripted seat, chasing and shooting, against a CPU side so managed. Technical
    /// evidence of the decision distributions, not feel.
    /// </summary>
    public static class ManagerTrials
    {
        public const int CpuSeeds = 8, SeatSeeds = 4;
        public const ulong FirstSeed = 0x127100000UL;

        public static readonly TeamTactic[] Plans = { TeamTactic.Reference(0), TacticTrials.HighPress, TacticTrials.LowBlock };

        /// <summary>The presets: name and its knobs.</summary>
        public static IEnumerable<(string Name, SimConfig Config)> Presets(SimConfig config)
        {
            yield return ("off", config.With("cpuTactics", 0));
            yield return ("cautious", config.With("cpuTactics", 1).With("cpuChaseFrom", 0.67).With("cpuProtectFrom", 0.67));
            yield return ("bold", config.With("cpuTactics", 1).With("cpuChaseFrom", 0.5).With("cpuProtectFrom", 0.83));
        }

        public static List<ManagerTrial> Run(SimConfig config, int cpuSeeds = CpuSeeds, int seatSeeds = SeatSeeds)
        {
            var trials = new List<ManagerTrial>();
            foreach ((string name, SimConfig c) in Presets(config))
            {
                for (int i = 0; i < cpuSeeds; i++) trials.Add(One(c, "cpu", name, FirstSeed + (ulong)i));
                for (int i = 0; i < seatSeeds; i++) trials.Add(One(c, "seat", name, FirstSeed + 0x100 + (ulong)i));
            }

            return trials;
        }

        public static ManagerTrial One(SimConfig config, string kind, string preset, ulong seed)
        {
            bool seat = kind == "seat";
            var sim = new Simulator(config, Squads.Match(config, seed, 10, bothCpu: !seat, homeTactics: Plans, awayTactics: Plans));
            var recorder = new MatchRecorder();
            var trial = new ManagerTrial { Kind = kind, Preset = preset, Seed = seed };
            SwitchController? switcher = seat ? new SwitchController(SquadTrials.SeatController) : null;
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            int limit = 3 * 2 * config.HalfTicks;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                if (seat) commands.Add(SquadTrials.Seat(sim, SquadTrials.SeatController, switcher!.Decide(sim, events, manual: false)));
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                recorder.Observe(config, s, events);
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Substituted) trial.Substitutions++;
                    if (e.Kind != SimEventKind.TacticSwitched) continue;
                    if (e.Subject == Squads.HomeTeam) trial.HomeSwitches++;
                    else trial.AwaySwitches++;
                    if (e.Other == 0) trial.ToBalanced++;
                    else if (e.Other == 1) trial.ToChase++;
                    else trial.ToProtect++;
                    ManagerObservation seen = ManagerControllers.Observe(s, config, e.Subject);
                    trial.SwitchShares.Add(seen.MatchShare);
                    trial.SwitchMargins.Add(seen.GoalsFor - seen.GoalsAgainst);
                }
            }

            trial.Finished = recorder.Result != null;
            if (recorder.Result != null)
            {
                trial.HomeGoals = recorder.Result.HomeGoals;
                trial.AwayGoals = recorder.Result.AwayGoals;
            }

            // A switch, then a cooldown of match clock before the next: at most one more per cooldown.
            int clock = 2 * config.HalfTicks;
            trial.SwitchBound = config.PlanCooldownTicks > 0 ? 1 + clock / config.PlanCooldownTicks : int.MaxValue;
            trial.LengthSeconds = sim.Tick * config.TickSeconds;
            trial.FinalHash = sim.StateHash();
            return trial;
        }

        /// <summary>Whether no side switched more than its cooldown allows, and every match finished.</summary>
        public static bool Passes(IEnumerable<ManagerTrial> trials) =>
            trials.All(t => t.Finished && t.HomeSwitches <= t.SwitchBound && t.AwaySwitches <= t.SwitchBound);

        public static void WriteCsv(TextWriter w, SimConfig config, IEnumerable<ManagerTrial> trials)
        {
            w.WriteLine("config_hash,kind,preset,seed,finished,home_goals,away_goals,home_switches,away_switches,to_balanced,to_chase,to_protect,switch_shares,switch_margins,substitutions,switch_bound,length_s,final_hash");
            foreach (ManagerTrial t in trials)
            {
                w.WriteLine(string.Join(",",
                    config.Hash.ToString("x16"), t.Kind, t.Preset, t.Seed.ToString("x"), t.Finished ? "yes" : "no", t.HomeGoals, t.AwayGoals, t.HomeSwitches, t.AwaySwitches,
                    t.ToBalanced, t.ToChase, t.ToProtect, string.Join(" ", t.SwitchShares.Select(F)), string.Join(" ", t.SwitchMargins), t.Substitutions,
                    t.SwitchBound, F(t.LengthSeconds), t.FinalHash.ToString("x16")));
            }
        }

        public static string KeyNumbers(IReadOnlyList<ManagerTrial> trials)
        {
            var lines = new List<string>();
            foreach (IGrouping<(string, string), ManagerTrial> g in trials.GroupBy(t => (t.Kind, t.Preset)))
            {
                double first = g.SelectMany(t => t.SwitchShares).DefaultIfEmpty(double.NaN).Average();
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1}: goals {2:F1}-{3:F1}, switches {4:F1} a match (chase {5}, protect {6}, balanced {7}) at {8:F2} of the match, substitutions {9:F1}",
                    g.Key.Item1, g.Key.Item2, g.Average(t => t.HomeGoals), g.Average(t => t.AwayGoals), g.Average(t => t.HomeSwitches + t.AwaySwitches),
                    g.Sum(t => t.ToChase), g.Sum(t => t.ToProtect), g.Sum(t => t.ToBalanced), first, g.Average(t => t.Substitutions)));
            }

            return string.Join("; ", lines);
        }

        private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }
}
