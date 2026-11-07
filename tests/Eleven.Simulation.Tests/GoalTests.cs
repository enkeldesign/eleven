using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The goal (#68): a goal is the whole ball over the line, having crossed it between the posts
    /// and under the crossbar, and the posts and crossbar return the ball by the documented rule.
    /// docs/simulation/goal.md.
    /// </summary>
    public class GoalTests
    {
        private static readonly SimConfig C = new SimConfig();

        private static double R => C.BallRadius;

        // The contact distance between the ball's centre and a post's or the bar's axis.
        private static double Reach => C.BallRadius + C.PostWidth / 2;

        private static Simulator Ball(Vec3 at, Vec3 velocity, LineState line = LineState.InPlay, SimConfig? config = null) =>
            new Simulator(config ?? C, new SimState { Seed = 5, Ball = new BallState { Id = 100, Position = at, Velocity = velocity, Line = line } });

        private static List<SimEvent> Run(Simulator sim, int ticks, List<SimState>? states = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                sim.Step(Array.Empty<Command>(), events);
                states?.Add(sim.PeekState().Clone());
            }

            return events;
        }

        private static int Count(List<SimEvent> events, SimEventKind kind) => events.Count(e => e.Kind == kind);

        [Fact]
        public void The_goal_stands_on_the_line_at_law_1s_sizes()
        {
            Assert.Equal(7.32, C.GoalWidth);
            Assert.Equal(2.44, C.GoalHeight);
            Assert.Equal(0.12, C.PostWidth);
            Assert.Equal(C.GoalLineX - 0.06, C.FrameAxisX, 12);
            Assert.Equal(3.72, C.PostAxisY, 12);
            Assert.Equal(2.50, C.CrossbarAxisZ, 12);
        }

        [Fact]
        public void A_ball_rolled_through_the_middle_scores_once_as_the_whole_ball_crosses()
        {
            Simulator sim = Ball(new Vec3(C.GoalLineX - 2, 0, R), new Vec3(6, 0, 0));
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 10 * C.TickRate, states);

            Assert.Equal(1, Count(events, SimEventKind.Goal));
            Assert.Equal(1, Count(events, SimEventKind.GoalLineCrossed));
            Assert.Equal(0, Count(events, SimEventKind.HitFrame));

            long at = events.Single(e => e.Kind == SimEventKind.Goal).Tick;
            Assert.Equal(at, events.Single(e => e.Kind == SimEventKind.GoalLineCrossed).Tick);
            Assert.True(states[(int)at].Ball.Position.X - R > C.GoalLineX, "the goal came before the whole ball was over");
            Assert.True(states[(int)at - 1].Ball.Position.X - R <= C.GoalLineX, "the goal came after the step the whole ball crossed");
            Assert.Equal(LineState.Goal, sim.PeekState().Ball.Line);
        }

        [Theory]
        [InlineData(+1)]
        [InlineData(-1)]
        public void A_ground_ball_just_inside_a_post_scores_and_just_outside_it_does_not(int side)
        {
            double inside = C.GoalWidth / 2 - R - 0.001, outside = C.GoalWidth / 2 + C.PostWidth + R + 0.001;

            List<SimEvent> scored = Run(Ball(new Vec3(C.GoalLineX - 2, side * inside, R), new Vec3(8, 0, 0)), 2 * C.TickRate);
            Assert.Equal(1, Count(scored, SimEventKind.Goal));
            Assert.Equal(0, Count(scored, SimEventKind.HitFrame));

            List<SimEvent> wide = Run(Ball(new Vec3(C.GoalLineX - 2, side * outside, R), new Vec3(8, 0, 0)), 2 * C.TickRate);
            Assert.Equal(0, Count(wide, SimEventKind.Goal));
            Assert.Equal(1, Count(wide, SimEventKind.GoalLineCrossed));
            Assert.Equal(0, Count(wide, SimEventKind.HitFrame));
        }

        [Theory]
        [InlineData(+1, FramePart.LeftPost)]
        [InlineData(-1, FramePart.RightPost)]
        public void A_fast_ball_struck_at_a_post_comes_back_off_it_by_the_restitution(int side, FramePart part)
        {
            // 30 m/s moves the ball 0.5 m a step: unobstructed, it would be wholly over the line
            // after one step. The frame stops it on the step it hits.
            Simulator sim = Ball(new Vec3(C.FrameAxisX - Reach - 0.05, side * C.PostAxisY, R), new Vec3(30, 0, 0));
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 2 * C.TickRate, states);

            SimEvent hit = Assert.Single(events, e => e.Kind == SimEventKind.HitFrame);
            Assert.Equal(0, hit.Tick);
            Assert.Equal((int)part, hit.Other);

            BallState after = states[0].Ball;
            double arriving = 30 - C.RollingFriction * C.Gravity * C.TickSeconds;
            Assert.Equal(-C.FrameRestitution * arriving, after.Velocity.X, 9);
            Assert.Equal(0, after.Velocity.Y, 9);
            Assert.True(after.Position.X <= C.FrameAxisX - Reach + 1e-9, $"the ball is at x = {after.Position.X:F4}, inside the post");

            Assert.Equal(0, Count(events, SimEventKind.Goal));
            Assert.Equal(0, Count(events, SimEventKind.GoalLineCrossed));
        }

        [Fact]
        public void A_ball_off_the_inside_of_a_post_goes_in()
        {
            // It overlaps the post's inner face by 3 cm, so it glances off it toward the middle.
            double y = C.GoalWidth / 2 - R + 0.03;
            Simulator sim = Ball(new Vec3(C.GoalLineX - 2, y, R), new Vec3(10, 0, 0));
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 2 * C.TickRate, states);

            SimEvent hit = Assert.Single(events, e => e.Kind == SimEventKind.HitFrame);
            Assert.Equal((int)FramePart.LeftPost, hit.Other);
            Assert.True(states[(int)hit.Tick].Ball.Velocity.Y < 0, "the post didn't turn the ball in");

            SimEvent goal = Assert.Single(events, e => e.Kind == SimEventKind.Goal);
            Assert.True(goal.Tick > hit.Tick);
        }

        [Fact]
        public void A_ball_struck_at_the_crossbar_comes_back_off_it()
        {
            Simulator sim = Ball(new Vec3(C.FrameAxisX - Reach - 0.05, 0, C.CrossbarAxisZ), new Vec3(25, 0, 0));
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 2 * C.TickRate, states);

            SimEvent hit = Assert.Single(events, e => e.Kind == SimEventKind.HitFrame);
            Assert.Equal((int)FramePart.Crossbar, hit.Other);
            Assert.True(states[(int)hit.Tick].Ball.Velocity.X < 0, "the bar didn't send the ball back");
            Assert.Equal(0, Count(events, SimEventKind.Goal));
            Assert.Equal(0, Count(events, SimEventKind.GoalLineCrossed));
        }

        [Theory]
        [InlineData(-0.02, true)]
        [InlineData(+0.02, false)]
        public void A_ball_just_under_the_bar_scores_and_just_over_it_does_not(double clearance, bool goal)
        {
            // Under: its top 2 cm below the bar's underside. Over: its bottom 2 cm above the bar's top.
            double z = clearance < 0 ? C.GoalHeight - R + clearance : C.GoalHeight + C.PostWidth + R + clearance;
            List<SimEvent> events = Run(Ball(new Vec3(C.GoalLineX - 0.6, 0, z), new Vec3(20, 0, 0)), 2 * C.TickRate);

            Assert.Equal(goal ? 1 : 0, Count(events, SimEventKind.Goal));
            Assert.Equal(1, Count(events, SimEventKind.GoalLineCrossed));
            Assert.Equal(0, Count(events, SimEventKind.HitFrame));
        }

        [Fact]
        public void A_scored_ball_half_back_over_the_line_cannot_score_again()
        {
            // Its centre is still over the line, so it never came back into play.
            List<SimEvent> events = Run(Ball(new Vec3(C.GoalLineX + 0.05, 0, R), new Vec3(2, 0, 0), LineState.Goal), 3 * C.TickRate);

            Assert.Equal(0, Count(events, SimEventKind.Goal));
            Assert.Equal(0, Count(events, SimEventKind.GoalLineCrossed));
        }

        [Fact]
        public void A_ball_back_in_play_can_score_again()
        {
            List<SimEvent> events = Run(Ball(new Vec3(C.GoalLineX - 1, 0, R), new Vec3(4, 0, 0), LineState.Goal), 3 * C.TickRate);

            Assert.Equal(1, Count(events, SimEventKind.Goal));
        }

        [Theory]
        [InlineData(0.0, true)]
        [InlineData(5.0, false)]
        public void A_ball_placed_over_the_line_is_judged_where_it_is(double y, bool goal)
        {
            List<SimEvent> events = Run(Ball(new Vec3(C.GoalLineX + 0.05, y, R), new Vec3(2, 0, 0)), 3 * C.TickRate);

            Assert.Equal(goal ? 1 : 0, Count(events, SimEventKind.Goal));
            Assert.Equal(1, Count(events, SimEventKind.GoalLineCrossed));
        }

        [Fact]
        public void An_unknown_line_state_is_refused()
        {
            Assert.Throws<ArgumentException>(() => Ball(new Vec3(0, 0, R), Vec3.Zero, (LineState)9));
        }

        // A shot by the controlled player, headless: it scores, runs the same twice, and replays
        // exactly from a snapshot taken while the ball is on its way.
        [Fact]
        public void A_shot_from_the_player_scores_runs_the_same_twice_and_replays_from_a_snapshot()
        {
            var config = new SimConfig(controlModel: ControlModel.Free);
            SimState Start() => new SimState
            {
                Seed = 0x60A1UL,
                Ball = new BallState { Id = 100, Position = new Vec3(config.GoalLineX - 11.4, 0.4, config.BallRadius) },
                Players = new[] { new PlayerState { Id = 1, OwnerControllerId = 1, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(config.GoalLineX - 12, 0.4) } },
            };

            Command[] Shot(long tick) => new[]
            {
                tick == 2 ? new Command(1, tick, Vec2.Zero, new KickIntent(new Vec2(1, 0), 0.8, 0.1)) : new Command(1, tick, Vec2.Zero),
            };

            (List<ulong> Hashes, List<SimEvent> Events, Simulator Sim) Play(Simulator sim, long ticks, Func<long, bool>? snapshotAt = null, List<byte[]>? snapshots = null)
            {
                var hashes = new List<ulong>();
                var events = new List<SimEvent>();
                for (long t = 0; t < ticks; t++)
                {
                    if (snapshotAt?.Invoke(sim.Tick) == true) snapshots!.Add(Snapshot.Capture(sim));
                    sim.Step(Shot(sim.Tick), events);
                    hashes.Add(sim.StateHash());
                }

                return (hashes, events, sim);
            }

            int ticks = 3 * config.TickRate;
            var snapshots = new List<byte[]>();
            var first = Play(new Simulator(config, Start()), ticks, t => t == 20, snapshots);
            var second = Play(new Simulator(config, Start()), ticks);

            Assert.Contains(first.Events, e => e.Kind == SimEventKind.Goal);
            Assert.Equal(first.Hashes, second.Hashes);
            Assert.Equal(first.Events, second.Events);

            Simulator restored = Snapshot.Restore(snapshots.Single(), config);
            var resumed = Play(restored, ticks - 20);
            Assert.Equal(first.Hashes.Skip(20), resumed.Hashes);
            Assert.Equal(first.Events.Where(e => e.Tick >= 20), resumed.Events);
        }
    }
}
