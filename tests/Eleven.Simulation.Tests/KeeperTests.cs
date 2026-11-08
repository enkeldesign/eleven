using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Eleven.Simulation.Tests.KeeperWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The keeper's body (#69): his phases and their timers (INV-12), his bounded reach, and the
    /// save, held or parried. His commands here are scripted; his brain is KeeperBrainTests'.
    /// docs/simulation/keeper.md.
    /// </summary>
    public class KeeperTests
    {
        private static readonly SimConfig C = new SimConfig();

        private static Command Keep(long tick, Vec2 move = default, Vec3? dive = null) => new Command(KeeperController, tick, move, dive: dive);

        private static int Count(List<SimEvent> events, SimEventKind kind) => events.Count(e => e.Kind == kind);

        [Fact]
        public void A_kick_sets_him_reacting_for_his_reaction_time_and_a_dive_waits_for_it()
        {
            SimState start = Penalty(C);
            var sim = new Simulator(C, start);
            Vec3 target = new Vec3(Spot(C).X, 2, 1);
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, C.TickRate,
                shooter: t => new Command(ShooterController, t, Vec2.Zero, t == 0 ? Shot(C, start, 0, 0.8) : (KickIntent?)null),
                keeper: t => Keep(t, dive: t >= C.KickWindUpTicks ? target : (Vec3?)null), states);

            long kicked = events.Single(e => e.Kind == SimEventKind.Kicked).Tick;
            Assert.Equal(C.KickWindUpTicks, kicked);
            SimEvent reacted = Assert.Single(events, e => e.Kind == SimEventKind.KeeperReacted);
            Assert.Equal(kicked, reacted.Tick);

            int reaction = (int)Math.Round(KeeperCapabilities.Reference.ReactionTime * C.TickRate);
            for (long t = kicked; t < kicked + reaction; t++)
            {
                Assert.Equal(KeeperPhase.React, states[(int)t].Keepers[0].Phase);
            }

            // He asked to dive on every step from the kick's own; the dive comes when his reaction
            // ends, not before.
            SimEvent dived = Assert.Single(events, e => e.Kind == SimEventKind.KeeperDived);
            Assert.Equal(kicked + reaction, dived.Tick);
        }

        [Fact]
        public void He_sets_while_reacting_whatever_his_brain_asks()
        {
            SimState start = Penalty(C);
            start.Players[1].Velocity = new Vec2(0, 3);
            var sim = new Simulator(C, start);
            var states = new List<SimState>();
            Run(sim, C.TickRate / 2,
                shooter: t => new Command(ShooterController, t, Vec2.Zero, t == 0 ? Shot(C, start, 0, 0.8) : (KickIntent?)null),
                keeper: t => Keep(t, move: new Vec2(0, 1)), states);

            // Asked to run across through his reaction, he brakes instead.
            int at = states.FindIndex(s => s.Keepers[0].Phase == KeeperPhase.React);
            Assert.True(at >= 0, "he never reacted");
            for (int i = at + 1; i < states.Count && states[i].Keepers[0].Phase == KeeperPhase.React; i++)
            {
                Assert.True(states[i].Players[1].Velocity.LengthSquared <= states[i - 1].Players[1].Velocity.LengthSquared, $"step {i}: he sped up while reacting");
            }
        }

        [Theory]
        [InlineData(1.75, 30)]   // the distance needs 0.5 s at 3.5 m/s
        [InlineData(5.0, 36)]    // more than the longest dive, 0.6 s: it stops short
        public void A_dive_carries_him_at_his_dive_speed_for_the_time_it_needs_then_he_recovers(double sideways, int diveTicks)
        {
            var sim = new Simulator(C, State(C, new Vec3(C.GoalLineX - 30, -20, C.BallRadius)));
            Vec2 from = Body(sim).Position;
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 2 * C.TickRate, keeper: t => Keep(t, dive: t == 0 ? new Vec3(from.X, sideways, 1) : (Vec3?)null), states: states);

            Assert.Single(events, e => e.Kind == SimEventKind.KeeperDived);
            int dived = states.Count(s => s.Keepers[0].Phase == KeeperPhase.Dive);
            Assert.Equal(diveTicks, dived);

            double step = KeeperCapabilities.Reference.DiveSpeed * C.TickSeconds;
            double moved = states[dived].Players[1].Position.Y - from.Y;
            Assert.True(Math.Abs(moved - Math.Min(sideways, diveTicks * step)) < step, $"he moved {moved:F3} m");
            Assert.Equal(C.KeeperRecoverTicks, states.Count(s => s.Keepers[0].Phase == KeeperPhase.Recover));
            Assert.All(states.Where(s => s.Keepers[0].Phase == KeeperPhase.Recover), s => Assert.Equal(Vec2.Zero, s.Players[1].Velocity));
            Assert.Equal(KeeperPhase.Position, states.Last().Keepers[0].Phase);
        }

        [Fact]
        public void A_ball_beyond_his_reach_passes_him_untouched()
        {
            Vec2 at = Spot(C);
            double past = KeeperCapabilities.Reference.Reach + C.BallRadius + 0.02;
            var ball = new Vec3(at.X - 5, past, C.BallRadius);
            var withKeeper = new Simulator(C, State(C, ball, new Vec3(8, 0, 0)));
            SimState noKeeper = State(C, ball, new Vec3(8, 0, 0));
            noKeeper.Keepers = Array.Empty<KeeperState>();
            noKeeper.Players = noKeeper.Players.Take(1).ToArray();
            var without = new Simulator(C, noKeeper);

            List<SimEvent> events = Run(withKeeper, C.TickRate);
            Run(without, C.TickRate);
            Assert.False(Saved(events));
            Assert.Equal(without.PeekState().Ball.Position, withKeeper.PeekState().Ball.Position);
        }

        [Fact]
        public void A_shot_at_his_body_is_held_and_moves_with_him()
        {
            Vec2 at = Spot(C);
            var sim = new Simulator(C, State(C, new Vec3(at.X - 6, 0, 1.0), new Vec3(12, 0, 0)));
            List<SimEvent> events = Run(sim, C.TickRate / 2);
            Assert.Single(events, e => e.Kind == SimEventKind.KeeperHeld && e.Subject == Keeper);
            Assert.Equal(Keeper, sim.PeekState().Ball.HeldBy);

            // Held, it keeps where he caught it, as he moves.
            Vec3 offset = sim.PeekState().Ball.Position - new Vec3(Body(sim).Position.X, Body(sim).Position.Y, 0);
            Run(sim, C.TickRate, keeper: t => Keep(t, move: new Vec2(0, 1)));
            Vec3 now = sim.PeekState().Ball.Position - new Vec3(Body(sim).Position.X, Body(sim).Position.Y, 0);
            Assert.True(Math.Sqrt((now - offset).LengthSquared) < 1e-9, $"the ball moved {Math.Sqrt((now - offset).LengthSquared):F4} m against his hands");
            Assert.Equal(new Vec3(Body(sim).Velocity.X, Body(sim).Velocity.Y, 0), sim.PeekState().Ball.Velocity);
        }

        [Fact]
        public void A_fast_shot_at_his_fingertips_is_parried_away()
        {
            Vec2 at = Spot(C);
            double fingertips = KeeperCapabilities.Reference.Reach + C.BallRadius - 0.04;
            var sim = new Simulator(C, State(C, new Vec3(at.X - 3, fingertips, 1.0), new Vec3(18, 0, 0)));
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, C.TickRate / 2, states: states);

            SimEvent parried = Assert.Single(events, e => e.Kind == SimEventKind.KeeperParried);
            Assert.Equal(0, sim.PeekState().Ball.HeldBy);
            BallState after = states[(int)parried.Tick].Ball;
            Assert.True(after.Velocity.Y > 0, "the parry didn't turn the ball away from him");
            Assert.True(after.Velocity.LengthSquared < 18 * 18, "the parry added speed");
        }

        [Theory]
        [InlineData(1.0, SimEventKind.KeeperHeld)]
        [InlineData(0.0, SimEventKind.KeeperParried)]
        public void His_handling_decides_between_holding_and_parrying_the_same_shot(double handling, SimEventKind outcome)
        {
            Vec2 at = Spot(C);
            KeeperCapabilities keeper = KeeperCapabilities.Reference.With("handling", handling);
            var sim = new Simulator(C, State(C, new Vec3(at.X - 3, 0.2, 1.0), new Vec3(15, 0, 0), keeper));
            List<SimEvent> events = Run(sim, C.TickRate / 2);

            Assert.Equal(outcome, Assert.Single(events, e => e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.KeeperParried).Kind);
        }

        [Fact]
        public void A_parrys_direction_error_is_seeded_bounded_and_reproducible()
        {
            Vec2 at = Spot(C);
            KeeperCapabilities keeper = KeeperCapabilities.Reference.With("handling", 0);
            double Direction(ulong seed)
            {
                var sim = new Simulator(C, State(C, new Vec3(at.X - 3, 0.6, 1.0), new Vec3(20, 0, 0), keeper, seed: seed));
                var states = new List<SimState>();
                List<SimEvent> events = Run(sim, C.TickRate / 2, states: states);
                SimEvent parried = Assert.Single(events, e => e.Kind == SimEventKind.KeeperParried);
                Vec3 v = states[(int)parried.Tick].Ball.Velocity;
                return Math.Atan2(v.Y, v.X);
            }

            Assert.Equal(Direction(1), Direction(1));
            double[] spread = Enumerable.Range(1, 20).Select(s => Direction((ulong)s)).ToArray();
            Assert.True(spread.Distinct().Count() > 1, "the parry's error never varied with the seed");
            Assert.True(spread.Max() - spread.Min() <= 2 * C.KeeperParryError + 1e-9, $"the parry's error spread {spread.Max() - spread.Min():F3} rad");
        }

        [Fact]
        public void He_plays_the_ball_with_his_hands_never_with_a_touch()
        {
            var config = new SimConfig(controlModel: ControlModel.Free);
            Vec2 at = Spot(config);
            var sim = new Simulator(config, State(config, new Vec3(at.X - 3, 0, config.BallRadius), new Vec3(4, 0, 0)));
            List<SimEvent> events = Run(sim, config.TickRate, keeper: t => Keep(t));

            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Touched && e.Subject == Keeper);
            Assert.Contains(events, e => e.Kind == SimEventKind.KeeperHeld);
        }

        [Fact]
        public void A_ball_he_holds_cannot_be_kicked()
        {
            Vec2 at = Spot(C);
            SimState start = State(C, new Vec3(at.X - 3, 0, C.BallRadius), new Vec3(4, 0, 0), shooterAt: new Vec2(at.X - 0.6, 0));
            var sim = new Simulator(C, start);
            Run(sim, C.TickRate);
            Assert.Equal(Keeper, sim.PeekState().Ball.HeldBy);

            List<SimEvent> events = Run(sim, C.TickRate, shooter: t => new Command(ShooterController, t, Vec2.Zero, KickIntent.AlongHeading(1, 0)));
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Kicked);
            Assert.Equal(Keeper, sim.PeekState().Ball.HeldBy);
        }

        [Fact]
        public void Snapshots_in_react_dive_and_recover_each_replay_the_run()
        {
            SimState start = Penalty(C);
            Func<long, Command?> shooter = t => new Command(ShooterController, t, Vec2.Zero, t == 0 ? Shot(C, start, 2.5, 0.8) : (KickIntent?)null);
            Func<long, Command?> keeper = t => Keep(t, dive: t >= C.KickWindUpTicks ? new Vec3(Spot(C).X, 2.5, 0.5) : (Vec3?)null);
            int ticks = 3 * C.TickRate;

            var whole = new List<ulong>();
            var sim = new Simulator(C, start);
            var snapshots = new Dictionary<KeeperPhase, (long Tick, byte[] Bytes)>();
            for (int i = 0; i < ticks; i++)
            {
                KeeperPhase phase = K(sim).Phase;
                if (phase != KeeperPhase.Position && !snapshots.ContainsKey(phase)) snapshots[phase] = (sim.Tick, Snapshot.Capture(sim));
                Run(sim, 1, shooter, keeper);
                whole.Add(sim.StateHash());
            }

            Assert.Equal(new[] { KeeperPhase.React, KeeperPhase.Dive, KeeperPhase.Recover }, snapshots.Keys.OrderBy(p => p).ToArray());
            foreach ((KeeperPhase phase, (long tick, byte[] bytes)) in snapshots)
            {
                Simulator restored = Snapshot.Restore(bytes, C);
                var resumed = new List<ulong>();
                while (restored.Tick < ticks)
                {
                    Run(restored, 1, shooter, keeper);
                    resumed.Add(restored.StateHash());
                }

                Assert.True(whole.Skip((int)tick).SequenceEqual(resumed), $"restored in {phase} at tick {tick}, the run differed");
            }
        }

        [Fact]
        public void A_keeper_must_be_a_player_and_only_a_keeper_holds_the_ball()
        {
            SimState missing = State(C, new Vec3(0, 0, C.BallRadius));
            missing.Keepers[0].PlayerId = 9;
            Assert.Throws<ArgumentException>(() => new Simulator(C, missing));

            SimState held = State(C, new Vec3(0, 0, C.BallRadius));
            held.Ball.HeldBy = Shooter;
            Assert.Throws<ArgumentException>(() => new Simulator(C, held));
        }
    }
}
