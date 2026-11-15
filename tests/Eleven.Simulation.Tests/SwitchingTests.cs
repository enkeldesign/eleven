using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>Player switching (#93): which footballer the human controls.</summary>
    public class SwitchingTests
    {
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        private const int Human = 1; // home's first outfielder's controller
        private const int Mate = 2, Keeper = 3, Opponent = 4;

        private static PlayerState P(Simulator sim, int id) => sim.PeekState().Players.First(p => p.Id == id);

        // The 2v2 world, live, with the human on 1, his CPU teammate 2.
        private static SimState Live(ulong seed = 5)
        {
            SimState s = Squads.Match(C, seed, perSide: 2);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.PhaseTicks = 0;
            return s;
        }

        [Fact]
        public void A_switch_hands_the_teammate_to_the_human_and_his_footballer_to_the_cpu()
        {
            var sim = new Simulator(C, Live());
            var events = new List<SimEvent>();
            sim.Step(new[] { new Command(Human, 0, Vec2.Zero, switchTo: Mate) }, events);
            SimEvent switched = Assert.Single(events, e => e.Kind == SimEventKind.Switched);
            Assert.Equal((Mate, 1), (switched.Subject, switched.Other));
            Assert.Equal(Human, P(sim, Mate).OwnerControllerId);
            Assert.Equal(Mate, P(sim, 1).OwnerControllerId);
            Assert.Contains(sim.PeekState().Cpus, c => c.PlayerId == 1);
            Assert.DoesNotContain(sim.PeekState().Cpus, c => c.PlayerId == Mate);

            // The human's stick now moves the teammate; his old footballer is the CPU's.
            Vec2 before = P(sim, Mate).Position;
            for (int i = 1; i < 30; i++) sim.Step(new[] { new Command(Human, i, new Vec2(0, 1)) }, new List<SimEvent>());
            Assert.True(P(sim, Mate).Position.Y > before.Y + 1);
        }

        [Fact]
        public void A_switch_to_a_keeper_an_opponent_himself_or_no_one_is_refused()
        {
            foreach (int target in new[] { Keeper, Opponent, 1, 99 })
            {
                var sim = new Simulator(C, Live());
                ulong before = sim.StateHash();
                var events = new List<SimEvent>();
                sim.Step(new[] { new Command(Human, 0, Vec2.Zero, switchTo: target) }, events);
                Assert.Equal(CommandRejection.SwitchRefused, Assert.Single(events, e => e.Kind == SimEventKind.CommandRejected).Rejection);
                Assert.Equal(Human, P(sim, 1).OwnerControllerId);
                Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Switched);
            }
        }

        // Steps with every brain and the switcher, the human doing `human` each step.
        private static List<SimEvent> Run(Simulator sim, SwitchController switcher, int ticks, Func<Simulator, Command>? human = null, List<Command[]>? recorded = null,
            Func<long, bool>? manual = null, List<int>? switches = null)
        {
            var all = new List<SimEvent>();
            var last = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var commands = new List<Command>();
                int target = switcher.Decide(sim, last, manual?.Invoke(sim.Tick) ?? false);
                Command h = human?.Invoke(sim) ?? new Command(Human, sim.Tick, Vec2.Zero);
                commands.Add(new Command(h.ControllerId, sim.Tick, h.Move, h.Kick, h.Aftertouch, h.QuickPass, switchTo: target));
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                recorded?.Add(commands.ToArray());
                last.Clear();
                sim.Step(commands, last);
                all.AddRange(last);
                if (target != 0) switches?.Add(target);
            }

            return all;
        }

        [Fact]
        public void A_pass_hands_the_human_its_receiver()
        {
            // The human on the ball, his teammate open ahead.
            SimState s = Live();
            s.Players[0].Position = new Vec2(0, 0);
            s.Players[0].Heading = 0;
            s.Ball.Position = new Vec3(0.6, 0, C.BallRadius);
            s.Ball.LastPlayedBy = 1;
            s.Players[1].Position = new Vec2(12, 1);
            s.Players[3].Position = new Vec2(30, 20);
            s.Players[4].Position = new Vec2(30, -20);
            var sim = new Simulator(C, s);
            List<SimEvent> events = Run(sim, new SwitchController(Human), 60, human: x => new Command(Human, x.Tick, Vec2.Zero, quickPass: x.Tick == 0));
            SimEvent pass = events.First(e => e.Kind == SimEventKind.QuickPassResolved);
            Assert.Equal(Mate, pass.Other);
            // The first switch: to the receiver, on the step after the pass is played.
            Assert.Contains(events, e => e.Kind == SimEventKind.Switched);
            SimEvent switched = events.First(e => e.Kind == SimEventKind.Switched);
            Assert.Equal(Mate, switched.Subject);
            Assert.Equal(pass.Tick + 1, switched.Tick);
        }

        [Fact]
        public void At_a_turnover_control_goes_to_the_teammate_who_reaches_the_ball_first()
        {
            // An opponent wins the ball nearer the human's teammate than the human.
            SimState s = Live();
            s.Players[0].Position = new Vec2(-30, 20);
            s.Players[1].Position = new Vec2(5, 0);
            s.Players[3].Position = new Vec2(8, 0);
            s.Ball.Position = new Vec3(8.6, 0, C.BallRadius);
            s.Ball.LastPlayedBy = 1;
            var sim = new Simulator(C, s);
            List<SimEvent> events = Run(sim, new SwitchController(Human), 120);
            SimEvent turnover = events.First(e => (e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.ChallengeWon) && e.Subject >= Opponent && e.Subject < 6);
            Assert.Contains(events, e => e.Kind == SimEventKind.Switched);
            SimEvent switched = events.First(e => e.Kind == SimEventKind.Switched);
            Assert.Equal(Mate, switched.Subject);
            Assert.True(switched.Tick <= turnover.Tick + 1, $"switched at {switched.Tick}, the turnover at {turnover.Tick}");
        }

        [Fact]
        public void A_manual_switch_holds_until_the_next_possession_event()
        {
            var switcher = new SwitchController(Human);
            var sim = new Simulator(C, Live());
            var switches = new List<int>();
            List<SimEvent> events = Run(sim, switcher, 400, manual: t => t == 30, switches: switches);
            Assert.Equal(Mate, switches.First());
            Assert.Contains(events, e => e.Kind == SimEventKind.Switched);
            SimEvent manual = events.First(e => e.Kind == SimEventKind.Switched);
            Assert.Equal(30L, manual.Tick);

            // No automatic switch before the next possession event after it.
            SimEvent next = events.FirstOrDefault(e => e.Tick > manual.Tick && (e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.Attached || e.Kind == SimEventKind.RestartPlaced));
            long until = next.Tick == 0 ? long.MaxValue : next.Tick;
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Switched && e.Tick > manual.Tick && e.Tick <= until);
        }

        [Fact]
        public void A_match_with_switches_replays_from_its_recording_and_its_snapshots()
        {
            var recorded = new List<Command[]>();
            var sim = new Simulator(C, Squads.Match(C, seed: 0x93, perSide: 2));
            var switcher = new SwitchController(Human);
            List<SimEvent> events = Run(sim, switcher, 2400, recorded: recorded);
            Assert.True(events.Count(e => e.Kind == SimEventKind.Switched) >= 3, $"{events.Count(e => e.Kind == SimEventKind.Switched)} switches");

            var recording = CommandRecording.StartFrom(new Simulator(C, Squads.Match(C, seed: 0x93, perSide: 2)));
            foreach (Command[] step in recorded) recording.Record(step);
            Assert.Equal(sim.StateHash(), CommandRecording.Decode(recording.Encode()).Replay(C).StateHash());

            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), C);
            Assert.Equal(sim.StateHash(), restored.StateHash());
            Assert.Equal(sim.PeekState().Cpus.Select(c => c.PlayerId), restored.PeekState().Cpus.Select(c => c.PlayerId));
        }
    }
}
