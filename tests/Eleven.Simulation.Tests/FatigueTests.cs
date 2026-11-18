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

        // Home's outfielder in the two-sided world, no referee, the ball far off, his stick `stick`
        // for `seconds`. Fatigue is a match world's (two sides), not practice's.
        private static Simulator Run(double seconds, Vec2 stick, double stamina = 0.6, double fatigue = 0)
        {
            SimState s = OneVOne.Start(C, seed: 2, homeAttackSign: 1);
            s.Ball.Position = new Vec3(0, 40, C.BallRadius);
            s.Players[0].Position = new Vec2(-40, -20);
            s.Players[0].Capabilities = PlayerCapabilities.Reference.With("stamina", stamina);
            s.Players[0].Fatigue = fatigue;
            Simulator sim = Simulator(C, s);
            int ticks = (int)(seconds * C.TickRate);
            for (int t = 0; t < ticks; t++) Step(sim, new Command(OneVOne.HomeController, t, stick));
            return sim;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0.02)]
        public void At_fatigueFrom_one_a_dive_faster_than_his_top_speed_tires_him_finitely(double drain)
        {
            // fatigueFrom = 1 leaves no band to tire in: faster than his fresh top speed, as a
            // keeper's dive can be, he tires at the knob's full rate, and never by a division by 0.
            SimConfig config = C.With("fatigueFrom", 1).With("fatigueDrain", drain);
            SimState s = OneVOne.Start(config, seed: 2, homeAttackSign: 1);
            int keeper = Array.FindIndex(s.Players, p => p.Id == s.Keepers[0].PlayerId);
            s.Keepers[0].Phase = KeeperPhase.Dive;
            s.Keepers[0].PhaseTicks = 20;
            s.Keepers[0].DiveVelocity = new Vec2(0, 8);
            Simulator sim = Simulator(config, s);
            for (int t = 0; t < 10; t++) Step(sim);
            double fatigue = sim.PeekState().Players[keeper].Fatigue;
            Assert.True(double.IsFinite(fatigue), $"fatigue {fatigue}");
            Assert.InRange(fatigue, 0, 1);
            if (drain == 0) Assert.Equal(0, fatigue);
            else Assert.True(fatigue > 0);
        }

        [Fact]
        public void Practice_is_timeless()
        {
            SimState s = State(C, ballPosition: new Vec3(0, 40, C.BallRadius));
            Simulator sim = Simulator(C, s);
            for (int t = 0; t < 30 * C.TickRate; t++) Step(sim, Move(t, 1, 0));
            Assert.Equal(0, sim.PeekState().Players[0].Fatigue);
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
        public void A_tired_cpu_footballer_leaves_the_press_to_a_fresh_teammate()
        {
            // Five a side, a loose ball between two of home's midfielders the same distance off.
            SimConfig c = SpikeScenario.Config();
            SimState s = Squads.Match(c, 9, 5, bothCpu: true);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.AwardedTeam = 0;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            int a = Array.FindIndex(s.Players, p => p.TeamId == Squads.HomeTeam && p.Slot == 3);
            int b = Array.FindIndex(s.Players, p => p.TeamId == Squads.HomeTeam && p.Slot == 4);
            for (int i = 0; i < s.Players.Length; i++)
            {
                if (s.Players[i].Slot > 0) s.Players[i].Position = new Vec2(s.Players[i].TeamId == Squads.HomeTeam ? -30 : 30, 20);
            }

            s.Players[a].Position = new Vec2(-5, -6);
            s.Players[b].Position = new Vec2(-5, 6);
            s.Ball.Position = new Vec3(0, 0, c.BallRadius);
            s.Ball.Velocity = Vec3.Zero;
            s.Ball.LastPlayedBy = 0;

            // Fresh, the tie goes to +y (#92); with him spent, to the other.
            s.Players[b].Fatigue = 1;
            CpuMode Mode(int player)
            {
                int cpu = Array.FindIndex(s.Cpus, x => x.PlayerId == s.Players[player].Id);
                return AI.OutfieldBrain.Decide(Perception.OfOutfielder(s, cpu), c).Mode;
            }

            Assert.Contains(Mode(a), new[] { CpuMode.SeekBall, CpuMode.Approach });
            Assert.DoesNotContain(Mode(b), new[] { CpuMode.SeekBall, CpuMode.Approach });
        }

        [Fact]
        public void A_tired_cpu_footballer_reckons_his_run_at_his_tired_speed()
        {
            // #118: alone on a moving ball, a spent footballer decides as a fresh one whose top speed
            // and acceleration are already what fatigue leaves his body (INV-13). The scenario turns
            // on the speed: a fresh footballer at full speed decides otherwise.
            SimConfig c = SpikeScenario.Config();
            SimState s = Squads.Match(c, 9, 5, bothCpu: true);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.AwardedTeam = 0;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            int a = Array.FindIndex(s.Players, p => p.TeamId == Squads.HomeTeam && p.Slot == 3);
            for (int i = 0; i < s.Players.Length; i++)
            {
                if (s.Players[i].Slot > 0) s.Players[i].Position = new Vec2(s.Players[i].TeamId == Squads.HomeTeam ? -30 : 30, 20);
            }

            s.Players[a].Position = new Vec2(-6, -12);
            s.Ball.Position = new Vec3(-20, 0, c.BallRadius);
            s.Ball.Velocity = new Vec3(9, 0, 0);
            s.Ball.LastPlayedBy = 0;
            CpuPlan Plan(double fatigue, double speedShare)
            {
                s.Players[a].Fatigue = fatigue;
                s.Players[a].Capabilities = PlayerCapabilities.Reference
                    .With("maxSpeed", PlayerCapabilities.Reference.MaxSpeed * speedShare)
                    .With("acceleration", PlayerCapabilities.Reference.Acceleration * speedShare);
                int cpu = Array.FindIndex(s.Cpus, x => x.PlayerId == s.Players[a].Id);
                return AI.OutfieldBrain.Decide(Perception.OfOutfielder(s, cpu), c);
            }

            CpuPlan tired = Plan(1, 1), slow = Plan(0, c.FatigueFloor), full = Plan(0, 1);
            Assert.True((full.Move - slow.Move).LengthSquared > 1e-6, $"the run turns on the speed: {full.Move} at full speed, {slow.Move} at the floor");
            Assert.Equal(slow.Mode, tired.Mode);
            Assert.True((tired.Move - slow.Move).LengthSquared < 1e-12, $"spent he runs to {tired.Move}, as a footballer of his tired speed runs to {slow.Move}");
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
