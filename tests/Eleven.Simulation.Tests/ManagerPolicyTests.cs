using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.AI;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #127: a CPU manager's tactical policy. By the score, the clock and the men on the pitch he
    /// switches between his plans (balanced, chase, protect) and makes tactical substitutions.
    /// </summary>
    public class ManagerPolicyTests
    {
        private static readonly SimConfig Off = SpikeScenario.Config();
        private static readonly SimConfig On = Off.With("cpuTactics", 1);

        private const byte Balanced = 1, Chase = 2, Protect = 3;

        private static readonly ManagedPlayer[] Side =
        {
            new ManagedPlayer(1, 0.10, InjuryLevel.None, false, ManagedLine.Back),
            new ManagedPlayer(2, 0.30, InjuryLevel.None, false, ManagedLine.Middle),
            new ManagedPlayer(3, 0.15, InjuryLevel.None, false, ManagedLine.Middle),
            new ManagedPlayer(4, 0.05, InjuryLevel.None, false, ManagedLine.Forward),
            new ManagedPlayer(5, 0.12, InjuryLevel.None, false, ManagedLine.Forward),
            new ManagedPlayer(6, 0.90, InjuryLevel.None, true, ManagedLine.None),
        };

        private static ManagerObservation Seen(double share, int goalsFor, int goalsAgainst, int plan = 0, bool maySwitch = true,
            bool maySub = false, int onPitch = 11, int theirs = 11, double spread = 0) =>
            new ManagerObservation(maySub, share, Side, new[] { 40, 41 }, goalsFor, goalsAgainst, onPitch, theirs, plan, maySwitch, spread);

        private static byte Switch(ManagerObservation seen, SimConfig? c = null) => ManagerBrain.Decide(seen, c ?? On).TacticTo;

        [Fact]
        public void Off_by_default_he_never_switches()
        {
            Assert.Equal(1, On.CpuTactics);
            Assert.Equal(0, Off.CpuTactics);
            Assert.Equal(0, Switch(Seen(0.95, 0, 2), Off));
        }

        [Fact]
        public void Behind_late_he_chases_and_not_early()
        {
            Assert.Equal(Chase, Switch(Seen(On.CpuChaseFrom + 0.01, 0, 1)));
            Assert.Equal(0, Switch(Seen(On.CpuChaseFrom - 0.01, 0, 1)));
        }

        [Fact]
        public void Ahead_late_he_protects_and_not_early()
        {
            Assert.Equal(Protect, Switch(Seen(On.CpuProtectFrom + 0.01, 2, 1)));
            Assert.Equal(0, Switch(Seen(On.CpuProtectFrom - 0.01, 2, 1)));
        }

        [Fact]
        public void Level_again_he_goes_back_to_balanced()
        {
            Assert.Equal(Balanced, Switch(Seen(0.9, 1, 1, plan: 1)));
            Assert.Equal(Balanced, Switch(Seen(0.9, 1, 1, plan: 2)));
            Assert.Equal(0, Switch(Seen(0.9, 1, 1, plan: 0)));
        }

        [Fact]
        public void Short_of_men_and_not_behind_he_protects()
        {
            Assert.Equal(Protect, Switch(Seen(0.2, 0, 0, onPitch: 10, theirs: 11)));
            Assert.Equal(0, Switch(Seen(0.2, 0, 1, onPitch: 10, theirs: 11)));
        }

        [Fact]
        public void He_asks_nothing_he_already_has_or_may_not_have()
        {
            Assert.Equal(0, Switch(Seen(0.9, 0, 1, plan: 1)));
            Assert.Equal(0, Switch(Seen(0.9, 0, 1, maySwitch: false)));
        }

        [Fact]
        public void His_spread_moves_his_thresholds()
        {
            Assert.Equal(0, Switch(Seen(On.CpuChaseFrom + 0.02, 0, 1, spread: 0.05)));
            Assert.Equal(Chase, Switch(Seen(On.CpuChaseFrom + 0.06, 0, 1, spread: 0.05)));
            Assert.Equal(Chase, Switch(Seen(On.CpuChaseFrom - 0.04, 0, 1, spread: -0.05)));
        }

        [Fact]
        public void Chasing_he_brings_fresh_legs_up_front_and_protecting_in_the_middle()
        {
            double late = On.CpuSubAfter + 0.01;
            Assert.Equal(5, ManagerBrain.Decide(Seen(late, 0, 1, plan: 1, maySub: true), On).Off);
            Assert.Equal(2, ManagerBrain.Decide(Seen(late, 1, 0, plan: 2, maySub: true), On).Off);

            // Off, or balanced, #110's rule: the most tired outfielder at or above cpuSubFatigue.
            Assert.Equal(2, ManagerBrain.Decide(Seen(late, 0, 1, plan: 1, maySub: true), Off).Off);
        }

        [Fact]
        public void Each_side_draws_its_own_spread_and_the_same_one_every_time()
        {
            SimState s = Squads.Match(On, 9, 10, bothCpu: true);
            ManagerObservation home = ManagerControllers.Observe(s, On, Squads.HomeTeam), again = ManagerControllers.Observe(s, On, Squads.HomeTeam);
            ManagerObservation away = ManagerControllers.Observe(s, On, Squads.AwayTeam);
            Assert.Equal(home.Spread, again.Spread);
            Assert.NotEqual(home.Spread, away.Spread);
            Assert.InRange(Math.Abs(home.Spread), 0, On.CpuTacticSpread);
        }

        [Fact]
        public void He_reads_the_score_the_men_and_the_lines()
        {
            SimState s = Squads.Match(On, 9, 10, bothCpu: true);
            s.Teams[1].Goals = 2;
            ManagerObservation home = ManagerControllers.Observe(s, On, Squads.HomeTeam);
            Assert.Equal((0, 2, 11, 11), (home.GoalsFor, home.GoalsAgainst, home.OnPitch, home.TheirsOnPitch));
            Assert.Equal(4, home.Players.Count(p => p.Line == ManagedLine.Back));
            Assert.Equal(2, home.Players.Count(p => p.Line == ManagedLine.Forward));
        }

        [Fact]
        public void A_cpu_side_behind_late_switches_to_its_chase_plan_in_the_match()
        {
            TeamTactic[] plans = { TeamTactic.Reference(0), TacticTrials.HighPress, TacticTrials.LowBlock };
            SimState s = Squads.Match(On, 9, 10, bothCpu: true, homeTactics: plans);
            s.Teams[1].Goals = 1;
            s.Match.Phase = MatchPhase.SecondHalf;
            s.Match.ClockTicks = (int)(0.9 * On.HalfTicks);
            var sim = new Simulator(On, s);
            var commands = new List<Command>();
            ManagerControllers.Append(sim, commands);
            Assert.Contains(commands, c => c.TacticTo == Chase);
            var events = new List<SimEvent>();
            sim.Step(commands, events);
            Assert.Contains(events, e => e.Kind == SimEventKind.TacticSwitched && e.Subject == Squads.HomeTeam && e.Other == 1);
        }
    }
}
