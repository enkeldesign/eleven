using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.AI;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #182: managers manage. With no profiles the commands are as before; a side's profile
    /// overrides the config for it alone; a manager's profile follows his temperament and
    /// competences; and a career match is managed on both sides, a player-manager's own side not.
    /// </summary>
    public class ManagerProfileTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        private static readonly ManagedPlayer[] Side =
        {
            new ManagedPlayer(1, 0.2, InjuryLevel.None, false, ManagedLine.Forward),
            new ManagedPlayer(2, 0.1, InjuryLevel.None, false, ManagedLine.Middle),
            new ManagedPlayer(11, 0, InjuryLevel.None, true, ManagedLine.None),
        };

        // A managed match's commands, tick by tick, for a few hundred ticks.
        private static List<string> Commands(SimConfig config, Func<Simulator, List<Command>, int> manage, int ticks = 900)
        {
            World w = WorldGenerator.Generate(0x182);
            var sim = new Simulator(config, CareerSquads.Match(config, w, w.Clubs[1].Id, w.Clubs[30].Id, 10, 0x182));
            var log = new List<string>();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            for (int t = 0; t < ticks; t++)
            {
                commands.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                manage(sim, commands);
                foreach (Command c in commands.Where(c => c.SubOff != 0 || c.TacticTo != 0)) log.Add($"{sim.Tick} {c.ControllerId} {c.SubOff} {c.SubOn} {c.TacticTo}");
                events.Clear();
                sim.Step(commands, events);
            }

            log.Add(sim.StateHash().ToString("x16"));
            return log;
        }

        [Fact]
        public void With_no_profiles_the_managers_command_as_before()
        {
            SimConfig managed = Config.With("cpuTactics", 1).With("cpuChaseFrom", 0).With("cpuChaseMargin", 1);
            List<string> before = Commands(managed, (s, c) => { ManagerControllers.Append(s, c); return 0; });
            Assert.Equal(before, Commands(managed, (s, c) => { ManagerControllers.Append(s, c, null); return 0; }));
            Assert.Equal(before, Commands(managed, (s, c) => { ManagerControllers.Append(s, c, new Dictionary<int, ManagerProfile>()); return 0; }));
            Assert.Equal(before, Commands(managed, (s, c) => { ManagerControllers.Append(s, c, new Dictionary<int, ManagerProfile> { [Squads.HomeTeam] = ManagerProfile.From(managed) }); return 0; }));
        }

        [Fact]
        public void A_profile_decides_for_its_side_whatever_the_config()
        {
            // Behind by one at 55 % of the match: the config's 0.67 waits, a bold 0.5 chases.
            var seen = new ManagerObservation(false, 0.55, Side, new[] { 40 }, 0, 1, 11, 11, 0, true, 0);
            Assert.Equal(0, ManagerBrain.Decide(seen, Config.With("cpuTactics", 1), ManagerProfile.From(Config.With("cpuTactics", 1))).TacticTo);
            var bold = new ManagerProfile(true, 0.5, 1, 0.83, 0.05, Config.CpuSubAfter, Config.CpuSubFatigue);
            Assert.Equal(2, ManagerBrain.Decide(seen, Config, bold).TacticTo);
            var off = new ManagerProfile(false, 0.5, 1, 0.83, 0.05, Config.CpuSubAfter, Config.CpuSubFatigue);
            Assert.Equal(0, ManagerBrain.Decide(seen, Config.With("cpuTactics", 1), off).TacticTo);

            // In a match: the config off, only the home side's profile manages, chasing from the start when level.
            var eager = new ManagerProfile(true, 0, 0, 0.99, 0, Config.CpuSubAfter, Config.CpuSubFatigue);
            List<string> log = Commands(Config, (s, c) => { ManagerControllers.Append(s, c, new Dictionary<int, ManagerProfile> { [Squads.HomeTeam] = eager }); return 0; });
            var switches = log.Take(log.Count - 1).Select(l => l.Split(' ')).Where(f => f[4] != "0").ToList();
            Assert.NotEmpty(switches);
            Assert.All(switches, f => Assert.True(int.Parse(f[1]) <= Squads.HomeKeeper(10), $"a switch by controller {f[1]}"));
        }

        [Fact]
        public void A_managers_profile_follows_his_temperament_and_competences()
        {
            Manager M(int temperament, int tactics, int adaptability)
            {
                var m = new Manager { Temperament = temperament };
                for (int i = 0; i < m.Competences.Length; i++) m.Competences[i] = 10;
                m.Competences[ManagerCompetences.Ids.IndexOf("tactics")] = tactics;
                m.Competences[ManagerCompetences.Ids.IndexOf("adaptability")] = adaptability;
                return m;
            }

            ManagerProfile cautious = CareerManagers.Profile(M(1, 10, 10), Config), bold = CareerManagers.Profile(M(20, 10, 10), Config);
            Assert.True(cautious.Tactics && bold.Tactics);
            Assert.Equal((0.67, 0.67), (cautious.ChaseFrom, cautious.ProtectFrom));
            Assert.Equal((0.5, 0.83), (bold.ChaseFrom, bold.ProtectFrom));
            Assert.Equal(1, bold.ChaseMargin);
            Assert.Equal(0.1, CareerManagers.Profile(M(10, 1, 10), Config).Spread, 9);
            Assert.Equal(0.02, CareerManagers.Profile(M(10, 20, 10), Config).Spread, 9);
            ManagerProfile rigid = CareerManagers.Profile(M(10, 10, 1), Config), adaptable = CareerManagers.Profile(M(10, 10, 20), Config);
            Assert.True(adaptable.SubAfter < rigid.SubAfter);
            Assert.True(adaptable.SubFatigue < rigid.SubFatigue);
            Assert.InRange(rigid.SubAfter, 0, 1);
            Assert.InRange(adaptable.SubFatigue, 0, 1);
        }

        [Fact]
        public void A_career_match_is_managed_on_both_sides_and_a_player_managers_own_side_is_his()
        {
            World w = WorldGenerator.Generate(0x182);
            Dictionary<int, ManagerProfile> both = CareerManagers.Profiles(w, w.Clubs[1].Id, w.Clubs[30].Id, Config);
            Assert.Equal(new[] { Squads.HomeTeam, Squads.AwayTeam }, both.Keys.OrderBy(k => k));
            Manager home = w.Managers.Single(m => m.Id == w.Clubs[1].ManagerId);
            Assert.Equal(CareerManagers.Profile(home, Config).ChaseFrom, both[Squads.HomeTeam].ChaseFrom);

            // Over a few matches, the managers switch plans; with the config's knobs they never would.
            int switches = 0;
            for (ulong seed = 0; seed < 4; seed++)
                CareerSquads.Play(Config, w, w.Clubs[(int)seed].Id, w.Clubs[(int)seed + 20].Id, 10, seed, observe: e => { if (e.Kind == SimEventKind.TacticSwitched) switches++; });
            Assert.True(switches > 0, "no manager switched a plan in four career matches");

            // A player-manager's side has no CPU manager: none of its controllers ever asks for a plan.
            SimState played = QuickMatch.Match(Config, w, w.Clubs[1].Id, w.Clubs[30].Id, 10, 3);
            Assert.False(ManagerControllers.CpuManages(played, Squads.HomeTeam));
            Assert.True(ManagerControllers.CpuManages(played, Squads.AwayTeam));
        }
    }
}
