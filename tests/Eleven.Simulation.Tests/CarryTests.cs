using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #138, from Erik's capture (build 223): "The receiving player stops when it gets the ball. It
    /// should keep its momentum." A footballer the human takes over keeps his run while the stick
    /// is neutral, until it moves or switchCarrySeconds runs out.
    /// </summary>
    public class CarryTests
    {
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        private const int Human = 1, Mate = 2;

        private static PlayerState P(Simulator sim, int id) => sim.PeekState().Players.First(p => p.Id == id);

        private static double Speed(PlayerState p) => Math.Sqrt(p.Velocity.LengthSquared);

        // The 2v2 world, live, the human on 1, his CPU teammate 2 running up-field at 5 m/s.
        private static Simulator Running(SimConfig? config = null, bool withBall = false)
        {
            SimConfig c = config ?? C;
            SimState s = Squads.Match(c, 5, perSide: 2);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.PhaseTicks = 0;
            int m = Array.FindIndex(s.Players, p => p.Id == Mate);
            s.Players[m].Position = new Vec2(-20, 10);
            s.Players[m].Velocity = new Vec2(5, 0);
            s.Players[m].Heading = 0;
            if (withBall)
            {
                s.Ball.Position = new Vec3(-19.5, 10, c.BallRadius);
                s.Ball.Velocity = new Vec3(5, 0, 0);
                if (c.ControlModel == ControlModel.Attached) s.Ball.AttachedTo = Mate;
            }

            return new Simulator(c, s);
        }

        // The switch, then `steps` steps with the human's stick at `stick`; the CPU plays everyone else but the mate.
        private static void SwitchThen(Simulator sim, int steps, Vec2 stick)
        {
            var events = new List<SimEvent>();
            sim.Step(new[] { new Command(Human, sim.Tick, Vec2.Zero, switchTo: Mate) }, events);
            Assert.Contains(events, e => e.Kind == SimEventKind.Switched);
            for (int i = 0; i < steps; i++) sim.Step(new[] { new Command(Human, sim.Tick, stick) }, events);
        }

        [Fact]
        public void Taken_over_with_a_neutral_stick_he_keeps_his_run()
        {
            var sim = Running();
            SwitchThen(sim, (int)(0.5 * C.TickRate), Vec2.Zero);
            PlayerState mate = P(sim, Mate);
            Assert.True(Speed(mate) > 4.5, $"{Speed(mate):F2} m/s half a second after the switch");
            Assert.True(mate.Velocity.X > 0.95 * Speed(mate), "still on his line up-field");
        }

        [Fact]
        public void With_the_ball_he_carries_it_on()
        {
            // Under ATTACHED, as Erik played: the ball is at the receiver's feet.
            var sim = Running(C.WithControlModel(ControlModel.Attached), withBall: true);
            SwitchThen(sim, (int)(0.5 * C.TickRate), Vec2.Zero);
            Assert.True(Speed(P(sim, Mate)) > 4.5);
            Assert.True(sim.PeekState().Ball.Position.X > -19.5 + 2, $"the ball went on with him: ball at {sim.PeekState().Ball.Position.X:F2}, he at {P(sim, Mate).Position.X:F2}, attached to {sim.PeekState().Ball.AttachedTo}");
        }

        [Fact]
        public void Any_stick_ends_the_carry_at_once()
        {
            var sim = Running();
            SwitchThen(sim, 1, new Vec2(0, 1));
            Assert.Equal(0, P(sim, Mate).CarryTicks);

            // Neutral again, he stops: the carry doesn't come back.
            var events = new List<SimEvent>();
            for (int i = 0; i < C.TickRate; i++) sim.Step(new[] { new Command(Human, sim.Tick, Vec2.Zero) }, events);
            Assert.True(Speed(P(sim, Mate)) < 0.2);
        }

        [Fact]
        public void The_carry_ends_after_switchCarrySeconds()
        {
            var sim = Running();
            SwitchThen(sim, C.SwitchCarryTicks + C.TickRate, Vec2.Zero);
            Assert.Equal(0, P(sim, Mate).CarryTicks);
            Assert.True(Speed(P(sim, Mate)) < 0.2, "stopped once the carry ran out");
        }

        [Fact]
        public void At_none_he_stops_as_before()
        {
            SimConfig none = C.With("switchCarrySeconds", 0);
            var sim = Running(none);
            SwitchThen(sim, (int)(0.5 * none.TickRate), Vec2.Zero);
            Assert.True(Speed(P(sim, Mate)) < 0.2);
        }

        [Fact]
        public void A_snapshot_mid_carry_restores_it()
        {
            var sim = Running();
            SwitchThen(sim, 10, Vec2.Zero);
            Assert.True(P(sim, Mate).CarryTicks > 0);
            var restored = new Simulator(C, Snapshot.Read(Snapshot.Capture(sim)).State);
            Assert.Equal(sim.StateHash(), restored.StateHash());
            Assert.Equal(P(sim, Mate).CarryTicks, P(restored, Mate).CarryTicks);
        }
    }
}
