using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #103: at five a side the human's side's free kicks and penalties are his. The switcher takes
    /// over the taker when the restart is placed, and the restart waits for the human's own kick;
    /// the CPU's restarts are taken automatically (#85).
    /// </summary>
    public class HumanRestartTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private const int Human = SquadTrials.SeatController;

        // A 5v5 match with the human at home's first outfielder, far from a restart awarded to his
        // side at the point given; his teammate the second outfielder nearest it.
        private static Simulator Awarded(RestartKind kind, Vec2 point)
        {
            SimState s = Squads.Match(C, 7, League.PerSide);
            s.Players[0].Position = new Vec2(-30, -20);
            s.Players[1].Position = point + new Vec2(-1, 1);
            s.Restart.Phase = RestartPhase.Stopped;
            s.Restart.Kind = kind;
            s.Restart.AwardedTeam = Squads.HomeTeam;
            s.Restart.Point = point;
            s.Restart.PhaseTicks = 1;
            s.Restart.TakerId = 0;
            return new Simulator(C, s);
        }

        private static List<SimEvent> Step(Simulator sim, SwitchController switcher, List<SimEvent> last, KickIntent? kick = null)
        {
            var commands = new List<Command>();
            int me = Array.FindIndex(sim.PeekState().Players, p => p.OwnerControllerId == Human);
            commands.Add(new Command(Human, sim.Tick, default, kick, switchTo: switcher.Decide(sim, last, manual: false)));
            KeeperControllers.Append(sim, commands);
            CpuControllers.Append(sim, commands);
            RestartControllers.Append(sim, commands);
            var events = new List<SimEvent>();
            sim.Step(commands, events);
            return events;
        }

        [Theory]
        [InlineData(RestartKind.FreeKick, 10, 8)]
        [InlineData(RestartKind.Penalty, 39, 0)]
        public void The_humans_side_restart_waits_for_his_own_kick(RestartKind kind, double x, double y)
        {
            Simulator sim = Awarded(kind, new Vec2(x, y));
            var switcher = new SwitchController(Human);
            var events = new List<SimEvent>();
            int taker = 0;
            for (int i = 0; i < 4 * C.TickRate && taker == 0; i++)
            {
                events = Step(sim, switcher, events);
                SimState s = sim.PeekState();
                if (s.Restart.Phase == RestartPhase.Ready && s.Players.Any(p => p.Id == s.Restart.TakerId && p.OwnerControllerId == Human)) taker = s.Restart.TakerId;
            }

            Assert.NotEqual(0, taker);
            Assert.Equal(Squads.HomeOutfielder(1), taker);
            Assert.Equal(kind, sim.PeekState().Restart.Kind);

            // Through the set-up and into the wait he is left alone: nobody takes it for him.
            int wait = sim.PeekState().Restart.PhaseTicks + C.RestartAutoTicks / 2;
            for (int i = 0; i < wait; i++)
            {
                events = Step(sim, switcher, events);
                Assert.DoesNotContain(events, e => e.Kind == SimEventKind.RestartTaken);
            }

            // His kick takes it.
            for (int i = 0; i < C.TickRate; i++)
            {
                events = Step(sim, switcher, events, new KickIntent(new Vec2(1, 0), 0.8, 0.05));
                if (events.Any(e => e.Kind == SimEventKind.RestartTaken)) break;
            }

            Assert.Contains(events, e => e.Kind == SimEventKind.RestartTaken && e.Subject == taker && e.Other == (int)kind);
        }

        [Fact]
        public void The_cpus_free_kick_is_taken_without_waiting()
        {
            SimState s = Squads.Match(C, 7, League.PerSide);
            s.Restart.Phase = RestartPhase.Stopped;
            s.Restart.Kind = RestartKind.FreeKick;
            s.Restart.AwardedTeam = Squads.AwayTeam;
            s.Restart.Point = new Vec2(-10, 8);
            s.Restart.PhaseTicks = 1;
            var sim = new Simulator(C, s);
            var switcher = new SwitchController(Human);
            var events = new List<SimEvent>();
            bool taken = false;
            for (int i = 0; i < C.RestartStopTicks + C.RestartReadyTicks + C.TickRate && !taken; i++)
            {
                events = Step(sim, switcher, events);
                taken = events.Any(e => e.Kind == SimEventKind.RestartTaken);
            }

            Assert.True(taken, "the CPU's free kick wasn't taken within a second of the set-up");
        }
    }
}
