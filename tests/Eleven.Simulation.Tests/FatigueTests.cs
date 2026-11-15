using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #100: fatigue. Running above fatigueFrom of his top speed tires a footballer, running below it
    /// rests him, at rates his stamina scales. Tired, his top speed and acceleration fall toward
    /// fatigueFloor of themselves. Half-time gives back a share. No physics of the ball changes.
    /// </summary>
    public class FatigueTests
    {
        private static readonly SimConfig C = Config();

        // The practice player, the ball far off, his stick `stick` for `seconds`.
        private static Simulator Run(double seconds, Vec2 stick, double stamina = 0.6, double fatigue = 0)
        {
            SimState s = State(C, ballPosition: new Vec3(0, 40, C.BallRadius));
            s.Players[0].Capabilities = PlayerCapabilities.Reference.With("stamina", stamina);
            s.Players[0].Fatigue = fatigue;
            Simulator sim = Simulator(C, s);
            int ticks = (int)(seconds * C.TickRate);
            for (int t = 0; t < ticks; t++) Step(sim, Move(t, stick.X, stick.Y));
            return sim;
        }

        [Fact]
        public void Sprinting_tires_him_and_standing_rests_him()
        {
            double tired = Run(30, new Vec2(1, 0)).PeekState().Players[0].Fatigue;
            Assert.InRange(tired, 0.2, 0.6);
            double rested = Run(20, Vec2.Zero, fatigue: tired).PeekState().Players[0].Fatigue;
            Assert.True(rested < tired / 2, $"tired {tired:F3}, rested {rested:F3}");
            Assert.Equal(0, Run(10, new Vec2(0.5, 0)).PeekState().Players[0].Fatigue, 9);
        }

        [Fact]
        public void Spent_he_keeps_only_the_floor_of_his_top_speed()
        {
            // Over a short sprint fatigue barely moves: his speed is his tiredness's.
            double fresh = Math.Sqrt(Run(3, new Vec2(1, 0)).PeekState().Players[0].Velocity.LengthSquared);
            double spent = Math.Sqrt(Run(3, new Vec2(1, 0), fatigue: 1).PeekState().Players[0].Velocity.LengthSquared);
            Assert.Equal(PlayerCapabilities.Reference.MaxSpeed, fresh, 1);
            Assert.InRange(spent, C.FatigueFloor * PlayerCapabilities.Reference.MaxSpeed - 0.1, C.FatigueFloor * PlayerCapabilities.Reference.MaxSpeed + 0.1);
        }

        [Fact]
        public void More_stamina_tires_slower()
        {
            double weak = Run(30, new Vec2(1, 0), stamina: 0.2).PeekState().Players[0].Fatigue;
            double strong = Run(30, new Vec2(1, 0), stamina: 1).PeekState().Players[0].Fatigue;
            Assert.True(strong < weak * 0.8, $"stamina 1: {strong:F3}, 0.2: {weak:F3}");
        }

        [Fact]
        public void Half_time_gives_back_its_share()
        {
            SimConfig c = SpikeScenario.Config().With("halfLength", 60);
            SimState s = OneVOne.Match(c, seed: 4, bothCpu: true);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.AwardedTeam = 0;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            s.Match.ClockTicks = c.HalfTicks - 1;
            for (int i = 0; i < s.Players.Length; i++) s.Players[i].Fatigue = 0.6;
            var sim = new Simulator(c, s);
            var events = new List<SimEvent>();
            sim.Step(Array.Empty<Command>(), events);
            Assert.Contains(events, e => e.Kind == SimEventKind.HalfTime);
            Assert.All(sim.PeekState().Players, p => Assert.InRange(p.Fatigue, 0.6 * (1 - c.HalfTimeRecovery) - 0.01, 0.6 * (1 - c.HalfTimeRecovery) + 0.01));
        }
    }
}
