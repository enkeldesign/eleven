using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.AI;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>#106: eleven a side, ten outfielders and a keeper, in a technical-reference 4-4-2.</summary>
    public class ElevenTests
    {
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        [Theory]
        [InlineData(3, 1, 1, 1)]
        [InlineData(5, 2, 2, 1)]
        [InlineData(8, 3, 3, 2)]
        [InlineData(10, 4, 4, 2)]
        public void A_side_plays_in_three_lines_and_ten_a_side_is_a_four_four_two(int n, int back, int middle, int forward)
        {
            Assert.Equal((back, middle, forward), OutfieldBrain.Lines(n));
        }

        [Fact]
        public void A_config_at_another_tick_rate_keeps_every_tunable()
        {
            SimConfig at = C.AtTickRate(120);
            Assert.Equal(120, at.TickRate);
            Assert.NotEqual(C.Hash, at.Hash);
            foreach (TunableParameter p in SimConfig.Tunables) Assert.Equal(C.Get(p.Name), at.Get(p.Name));
            Assert.Equal(C.HalfLength, at.HalfTicks / (double)at.TickRate, 9);
            Assert.Equal(C.Hash, at.AtTickRate(60).Hash);
        }

        [Fact]
        public void Ten_a_side_kicks_off_with_everyone_on_the_pitch_and_in_his_own_half()
        {
            SimState s = Squads.Match(C, 1, 10);
            Assert.Equal(22, s.Players.Length);
            Assert.All(s.Players, p => Assert.InRange(Math.Abs(p.Position.Y), 0, C.TouchlineY - 1));
            foreach (PlayerState p in s.Players)
            {
                int sign = s.Teams.Single(t => t.Id == p.TeamId).AttackSign;
                Assert.True(sign * p.Position.X < 0, $"{p.Id} at {p.Position.X:F1} is not in his own half");
            }

            Assert.Equal(Enumerable.Range(1, 10), s.Players.Where(p => p.TeamId == Squads.HomeTeam && p.Slot > 0).Select(p => p.Slot).OrderBy(x => x));
        }

        [Fact]
        public void A_short_eleven_a_side_match_finishes_in_its_lines_without_bunching()
        {
            var sim = new Simulator(C, Squads.Match(C, 0x106000001UL, 10, bothCpu: true));
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            double back = 0, middle = 0, forward = 0;
            int samples = 0;
            while (sim.Tick < 4 * C.HalfTicks && sim.PeekState().Match.Phase != MatchPhase.FullTime)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                if (s.Restart.Phase != RestartPhase.Live || s.Tick % C.TickRate != 0) continue;
                foreach (TeamState team in s.Teams)
                {
                    PlayerState[] side = s.Players.Where(p => p.TeamId == team.Id && p.Slot > 0).ToArray();
                    back += team.AttackSign * side.Where(p => p.Slot <= 4).Average(p => p.Position.X);
                    middle += team.AttackSign * side.Where(p => p.Slot > 4 && p.Slot <= 8).Average(p => p.Position.X);
                    forward += team.AttackSign * side.Where(p => p.Slot > 8).Average(p => p.Position.X);
                    samples++;
                }
            }

            Assert.Equal(MatchPhase.FullTime, sim.PeekState().Match.Phase);
            back /= samples;
            middle /= samples;
            forward /= samples;
            Assert.True(back < middle && middle < forward, $"lines at {back:F1}, {middle:F1}, {forward:F1} m up the pitch");
        }
    }
}
