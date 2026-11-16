using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.AI;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #110: a CPU side's manager. A simple policy on what he perceives of his own side (INV-9):
    /// an injured footballer is replaced at once, and late in the match the most tired one.
    /// </summary>
    public class ManagerTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private const int Side = 10;

        private static ManagerObservation Seen(bool may = true, double share = 0.8, int[]? bench = null, params ManagedPlayer[] players) =>
            new ManagerObservation(may, share, players, bench ?? new[] { 40, 41 });

        private static ManagedPlayer Man(int id, double fatigue = 0, InjuryLevel injury = InjuryLevel.None, bool keeper = false) => new ManagedPlayer(id, fatigue, injury, keeper);

        [Fact]
        public void A_footballer_taken_off_injured_is_replaced_first_then_one_hurt_and_playing_on()
        {
            Assert.Equal((3, 40), ManagerBrain.Decide(Seen(share: 0.1, players: new[] { Man(1, 0.9), Man(2, injury: InjuryLevel.Minor), Man(3, injury: InjuryLevel.Serious) }), C));
            Assert.Equal((2, 40), ManagerBrain.Decide(Seen(share: 0.1, players: new[] { Man(1, 0.9), Man(2, injury: InjuryLevel.Minor), Man(3) }), C));
        }

        [Fact]
        public void Late_in_the_match_the_most_tired_outfielder_goes_off_if_he_is_tired_enough()
        {
            ManagedPlayer[] side = { Man(1, 0.6), Man(2, 0.8), Man(3, 0.95, keeper: true), Man(4, 0.2) };
            Assert.Equal((2, 40), ManagerBrain.Decide(Seen(share: C.CpuSubAfter + 0.01, players: side), C));

            // Earlier in the match he waits; and no one fresher than cpuSubFatigue goes off.
            Assert.Equal((0, 0), ManagerBrain.Decide(Seen(share: C.CpuSubAfter - 0.01, players: side), C));
            Assert.Equal((0, 0), ManagerBrain.Decide(Seen(share: 0.9, players: new[] { Man(1, C.CpuSubFatigue - 0.01), Man(4, 0.2) }), C));
        }

        [Fact]
        public void He_asks_for_nothing_when_he_may_not_substitute_or_has_no_one_to_bring_on()
        {
            ManagedPlayer[] side = { Man(1, 0.9), Man(2, injury: InjuryLevel.Serious) };
            Assert.Equal((0, 0), ManagerBrain.Decide(Seen(may: false, players: side), C));
            Assert.Equal((0, 0), ManagerBrain.Decide(Seen(bench: Array.Empty<int>(), players: side), C));
        }

        [Fact]
        public void He_perceives_his_own_side_only_and_whether_he_may_substitute()
        {
            SimState s = Squads.Match(C, 5, Side, bothCpu: true);
            int sentOff = Squads.AwayOutfielder(Side, 3);
            s.Players[Array.FindIndex(s.Players, p => p.Id == sentOff)].SentOff = true;
            s.Bench[Array.FindIndex(s.Bench, b => b.Id == Squads.AwayBench(Side, 0))].Used = true;
            ManagerObservation seen = ManagerControllers.Observe(s, C, Squads.AwayTeam);

            // The kick-off is a stoppage. His eleven but the one sent off; his bench but the one used.
            Assert.True(seen.MaySubstitute);
            Assert.Equal(Side, seen.Players.Count);
            Assert.All(seen.Players, p => Assert.Equal(Squads.AwayTeam, s.Players.Single(x => x.Id == p.Id).TeamId));
            Assert.DoesNotContain(seen.Players, p => p.Id == sentOff);
            Assert.Single(seen.Players, p => p.Keeper);
            Assert.Equal(Enumerable.Range(0, Squads.BenchSize).Skip(1).Select(i => Squads.AwayBench(Side, i)), seen.Bench);
            Assert.Equal(0, seen.MatchShare);

            // In play he may not; in the second half the share is past a half.
            s.Restart.Phase = RestartPhase.Live;
            s.Match.Phase = MatchPhase.SecondHalf;
            s.Match.ClockTicks = C.HalfTicks / 2;
            seen = ManagerControllers.Observe(s, C, Squads.AwayTeam);
            Assert.False(seen.MaySubstitute);
            Assert.Equal(0.75, seen.MatchShare, 2);
        }

        [Fact]
        public void A_cpu_sides_manager_substitutes_through_its_keepers_controller_and_a_humans_side_is_left_to_him()
        {
            // Late in the match at a stoppage, a tired footballer on each side; home's first is the human's.
            SimState s = Squads.Match(C, 5, Side);
            s.Match.Phase = MatchPhase.SecondHalf;
            s.Match.ClockTicks = (int)(0.8 * C.HalfTicks);
            int tiredHome = Squads.HomeOutfielder(4), tiredAway = Squads.AwayOutfielder(Side, 4);
            s.Players[Array.FindIndex(s.Players, p => p.Id == tiredHome)].Fatigue = 0.9;
            s.Players[Array.FindIndex(s.Players, p => p.Id == tiredAway)].Fatigue = 0.9;
            var sim = new Simulator(C, s);

            var commands = new List<Command>();
            KeeperControllers.Append(sim, commands);
            CpuControllers.Append(sim, commands);
            RestartControllers.Append(sim, commands);
            int before = commands.Count;
            ManagerControllers.Append(sim, commands);

            // One command a controller still, and the away keeper's carries the substitution.
            Assert.Equal(before, commands.Count);
            Command asked = Assert.Single(commands, c => c.SubOff != 0);
            Assert.Equal((Squads.AwayKeeper(Side), tiredAway, Squads.AwayBench(Side, 0)), (asked.ControllerId, asked.SubOff, asked.SubOn));

            var events = new List<SimEvent>();
            sim.Step(commands, events);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
            SimEvent made = Assert.Single(events, e => e.Kind == SimEventKind.Substituted);
            Assert.Equal((Squads.AwayBench(Side, 0), tiredAway), (made.Subject, made.Other));
        }

        [Fact]
        public void A_cpu_match_at_eleven_a_side_makes_substitutions_within_the_laws_limits()
        {
            SquadTrial trial = SquadTrials.One(C, Side, 0x92000001UL);
            Assert.True(trial.Finished);
            Assert.InRange(trial.HomeSubstitutions, 0, 5);
            Assert.InRange(trial.AwaySubstitutions, 0, 5);
            Assert.True(trial.HomeSubstitutions + trial.AwaySubstitutions > 0, "no substitution in the match");
            Assert.Equal(trial.HomeSubstitutions + trial.AwaySubstitutions, trial.SubstitutionMinutes.Count);
            Assert.All(trial.SubstitutionMinutes, m => Assert.InRange(m, 0, 90));
        }
    }
}
