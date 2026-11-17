using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #126: three stored tactical plans a side, switched by an owned command at a cost: taken at
    /// the next stoppage (or at once), the shape settling from the old plan to the new, and a
    /// cooldown before the next switch.
    /// </summary>
    public class TacticTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();
        private const int Side = 10;
        private const int Seat = SquadTrials.SeatController;

        private static readonly TeamTactic Low = new TeamTactic { Formation = Formation.FiveThreeTwo, StepUp = 2, Line = -8, Width = 0.8, Press = -10 };
        private static readonly TeamTactic High = new TeamTactic { Formation = Formation.FourThreeThree, StepUp = 10, Line = 6, Width = 1.1, Press = 60 };
        private static readonly TeamTactic[] Plans = { TeamTactic.Reference(0), Low, High };

        private static SimState World(SimConfig? config = null) => Squads.Match(config ?? C, 7, Side, homeTactics: Plans);

        private static TeamState Home(Simulator sim) => sim.PeekState().Teams.Single(t => t.Id == Squads.HomeTeam);

        private static List<SimEvent> Switch(Simulator sim, byte to, int controller = Seat)
        {
            var events = new List<SimEvent>();
            sim.Step(new[] { new Command(controller, sim.Tick, Vec2.Zero, tacticTo: to) }, events);
            return events;
        }

        private static void StepToLive(Simulator sim)
        {
            var events = new List<SimEvent>();
            var commands = new List<Command>();
            while (sim.PeekState().Restart.Phase != RestartPhase.Live)
            {
                commands.Clear();
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
            }
        }

        private static void Refused(List<SimEvent> events, CommandRejection why) =>
            Assert.Contains(events, e => e.Kind == SimEventKind.CommandRejected && e.Subject == Seat && e.Rejection == why);

        [Fact]
        public void A_side_stores_three_plans_and_starts_on_its_first()
        {
            SimState s = World();
            Assert.Equal(2 * TeamTactics.PerSide, s.Tactics.Length);
            Assert.All(s.Tactics.Take(3), t => Assert.Equal(Squads.HomeTeam, t.TeamId));
            Assert.Equal(Formation.FiveThreeTwo, s.Tactics[1].Formation);
            TeamState home = s.Teams.Single(t => t.Id == Squads.HomeTeam);
            Assert.Equal((0, Formation.FourFourTwo), (home.Tactic, home.Formation));
        }

        [Fact]
        public void At_a_stoppage_a_switch_is_taken_at_once_and_the_side_settles()
        {
            var sim = new Simulator(C, World()); // the kick-off: a stoppage
            List<SimEvent> e = Switch(sim, 2);
            SimEvent made = Assert.Single(e, x => x.Kind == SimEventKind.TacticSwitched);
            Assert.Equal((Squads.HomeTeam, 1), (made.Subject, made.Other));
            TeamState home = Home(sim);
            Assert.Equal((1, 0, Formation.FiveThreeTwo), (home.Tactic, home.TacticFrom, home.Formation));
            Assert.Equal((C.PlanSettleTicks, C.PlanSettleTicks), (home.TacticSettle, home.TacticSettleOf));
            Assert.Equal(C.PlanCooldownTicks, home.TacticCooldown);
            Assert.Equal(0.0, Perception.TacticOf(sim.PeekState(), Squads.HomeTeam).Settled, 9);
        }

        [Fact]
        public void In_play_it_waits_for_the_next_stoppage()
        {
            var sim = new Simulator(C, World());
            StepToLive(sim);
            List<SimEvent> e = Switch(sim, 3);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.TacticSwitched);
            Assert.Equal((0, 3), (Home(sim).Tactic, Home(sim).TacticPending));

            // Play on to the next stoppage: the switch is taken there.
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            bool switched = false;
            while (!switched && sim.Tick < 6 * C.HalfTicks)
            {
                commands.Clear();
                events.Clear();
                CpuControllers.Append(sim, commands);
                KeeperControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                switched = events.Any(x => x.Kind == SimEventKind.TacticSwitched);
                if (switched) Assert.True(sim.PeekState().Restart.On && sim.PeekState().Restart.Phase != RestartPhase.Live || sim.PeekState().Match.Phase != MatchPhase.FirstHalf);
            }

            Assert.True(switched);
            Assert.Equal((2, 0), (Home(sim).Tactic, Home(sim).TacticPending));
        }

        [Fact]
        public void With_planSwitchAt_one_a_switch_in_play_is_taken_at_once()
        {
            SimConfig at = C.With("planSwitchAt", 1);
            var sim = new Simulator(at, World(at));
            StepToLive(sim);
            Assert.Contains(Switch(sim, 3), x => x.Kind == SimEventKind.TacticSwitched);
            Assert.Equal(2, Home(sim).Tactic);
        }

        [Fact]
        public void Inside_the_cooldown_a_switch_is_refused()
        {
            var sim = new Simulator(C, World());
            Assert.Contains(Switch(sim, 2), x => x.Kind == SimEventKind.TacticSwitched);
            Refused(Switch(sim, 3), CommandRejection.TacticCooldown);
            Assert.Equal(1, Home(sim).Tactic);
        }

        [Fact]
        public void A_switch_to_the_plan_it_plays_or_awaits_or_to_none_stored_is_refused()
        {
            var sim = new Simulator(C, World());
            Refused(Switch(sim, 1), CommandRejection.TacticRefused);
            Refused(Switch(sim, 4), CommandRejection.TacticRefused);

            var live = new Simulator(C, World());
            StepToLive(live);
            Switch(live, 2);
            Refused(Switch(live, 2), CommandRejection.TacticRefused);
        }

        [Fact]
        public void A_side_without_stored_plans_cannot_switch()
        {
            SimState s = Squads.Match(C, 7, Side);
            s.Tactics = Array.Empty<TeamTactic>();
            var sim = new Simulator(C, s);
            Refused(Switch(sim, 2), CommandRejection.TacticRefused);
        }

        [Fact]
        public void The_side_settles_over_planSettleSeconds_and_the_cooldown_runs_down()
        {
            var sim = new Simulator(C, World());
            Switch(sim, 2);
            double last = -1;
            var events = new List<SimEvent>();
            for (int i = 0; i < C.PlanSettleTicks; i++)
            {
                double settled = Perception.TacticOf(sim.PeekState(), Squads.HomeTeam).Settled;
                Assert.True(settled > last, $"step {i}: settled {settled} after {last}");
                last = settled;
                sim.Step(Array.Empty<Command>(), events);
            }

            Assert.Equal(1.0, Perception.TacticOf(sim.PeekState(), Squads.HomeTeam).Settled, 9);
            Assert.Equal(0, Home(sim).TacticSettle);
            Assert.True(Home(sim).TacticCooldown < C.PlanCooldownTicks);
        }

        [Fact]
        public void A_restart_takers_command_keeps_his_switch()
        {
            // The kick-off's taker is the human's footballer; the restart controller replaces his
            // command with the taker's, which must keep the switch he asked for in it.
            var sim = new Simulator(C, World());
            var commands = new List<Command> { new Command(Seat, sim.Tick, Vec2.Zero, tacticTo: 2) };
            RestartControllers.Append(sim, commands);
            Assert.Contains(commands, c => c.ControllerId == Seat && c.TacticTo == 2);
        }

        [Fact]
        public void A_cpu_side_plays_to_its_plan()
        {
            // The same match, home on its low block and then on its high press, both in a 4-4-2 so
            // only the instructions differ: it stands deeper on the first.
            double low = MeanDepth(1), high = MeanDepth(2);
            Assert.True(low < high - 5, $"low block at {low:F1} m, high press at {high:F1} m");
        }

        private static double MeanDepth(byte plan)
        {
            TeamTactic first = Plans[plan];
            first.Formation = Formation.FourFourTwo;
            var plans = new[] { first, first, first };
            var sim = new Simulator(C, Squads.Match(C, 0x126000001UL, Side, bothCpu: true, homeTactics: plans));
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            double sum = 0;
            int samples = 0;
            while (sim.Tick < C.HalfTicks)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                if (s.Restart.Phase != RestartPhase.Live || s.Tick % C.TickRate != 0) continue;
                sum += s.Players.Where(p => p.TeamId == Squads.HomeTeam && p.Slot > 0).Average(p => p.Position.X);
                samples++;
            }

            return sum / samples;
        }

        [Fact]
        public void A_recorded_switch_replays_and_a_snapshot_mid_settle_restores()
        {
            var sim = new Simulator(C, World());
            var recorder = CommandRecording.StartFrom(sim);
            var events = new List<SimEvent>();
            for (int i = 0; i < 2 * C.PlanSettleTicks; i++)
            {
                var step = new List<Command>();
                if (i == 0) step.Add(new Command(Seat, sim.Tick, Vec2.Zero, tacticTo: 2));
                RestartControllers.Append(sim, step);
                recorder.Record(step);
                sim.Step(step, events);
                if (i == C.PlanSettleTicks / 2)
                {
                    var restored = new Simulator(C, Snapshot.Read(Snapshot.Capture(sim)).State);
                    Assert.Equal(sim.StateHash(), restored.StateHash());
                    Assert.Equal(Home(sim).TacticSettle, Home(restored).TacticSettle);
                }
            }

            CommandRecording again = CommandRecording.Decode(recorder.Encode());
            Simulator replay = again.Replay(C);
            Assert.Equal(sim.StateHash(), replay.StateHash());
            Assert.Equal(1, Home(replay).Tactic);
        }
    }
}
