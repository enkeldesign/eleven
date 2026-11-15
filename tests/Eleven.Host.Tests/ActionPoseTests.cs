using System;
using System.Collections.Generic;
using Eleven.Host;
using Eleven.Presentation;
using Eleven.Simulation;
using Xunit;

namespace Eleven.Host.Tests
{
    /// <summary>#101: each footballer's pose and facing, read from the simulation's state.</summary>
    public class ActionPoseTests
    {
        private static readonly SimConfig C = new SimConfig(controlModel: ControlModel.Free);

        // An outfielder at the origin, an opponent 10 m off, a keeper at his line.
        private static SimState World()
        {
            return new SimState
            {
                Ball = new BallState { Id = 100, Position = new Vec3(5, 5, C.BallRadius) },
                Teams = new[] { new TeamState { Id = 1, AttackSign = 1 }, new TeamState { Id = 2, AttackSign = -1 } },
                Players = new[]
                {
                    new PlayerState { Id = 1, OwnerControllerId = 1, TeamId = 1, Capabilities = PlayerCapabilities.Reference },
                    new PlayerState { Id = 2, OwnerControllerId = 2, TeamId = 2, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(10, 0) },
                    new PlayerState { Id = 3, OwnerControllerId = 3, TeamId = 2, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(40, 0) },
                },
                Keepers = new[] { new KeeperState { PlayerId = 3, Capabilities = KeeperCapabilities.Reference } },
            };
        }

        [Fact]
        public void A_footballer_stands_still_and_runs_in_two_strides_that_change_with_the_ground_he_covers()
        {
            var poses = new ActionPoses();
            SimState s = World();
            Assert.Equal(Pose.Stand, poses.Of(s, 0, C));

            s.Players[0].Velocity = new Vec2(6, 0);
            var seen = new HashSet<Pose>();
            for (int i = 0; i < C.TickRate; i++)
            {
                poses.Observe(s, Array.Empty<SimEvent>(), C);
                seen.Add(poses.Of(s, 0, C));
            }

            Assert.Equal(new HashSet<Pose> { Pose.RunA, Pose.RunB }, seen);
            Assert.Equal(Pose.RunA, poses.Of(s, 0, C, still: true));
        }

        [Fact]
        public void A_wind_up_and_a_kick_just_struck_are_drawn_as_a_kick_and_then_let_go()
        {
            var poses = new ActionPoses();
            SimState s = World();
            s.Players[0].WindUpTicks = 3;
            Assert.Equal(Pose.Kick, poses.Of(s, 0, C));

            s.Players[0].WindUpTicks = 0;
            s.Tick = 10;
            poses.Observe(s, new[] { new SimEvent(10, SimEventKind.Kicked, 1) }, C);
            Assert.Equal(Pose.Kick, poses.Of(s, 0, C));
            s.Tick += (long)Math.Round(ActionPoses.Hold * C.TickRate);
            Assert.Equal(Pose.Stand, poses.Of(s, 0, C));
        }

        [Theory]
        [InlineData(SimEventKind.Headed, Pose.Header)]
        [InlineData(SimEventKind.Volleyed, Pose.Volley)]
        public void A_header_or_a_volley_is_drawn_as_such_and_not_as_the_kick_it_also_is(SimEventKind kind, Pose pose)
        {
            var poses = new ActionPoses();
            SimState s = World();
            s.Tick = 4;
            poses.Observe(s, new[] { new SimEvent(4, kind, 1), new SimEvent(4, SimEventKind.Kicked, 1) }, C);
            Assert.Equal(pose, poses.Of(s, 0, C));
        }

        [Fact]
        public void A_touch_just_taken_is_a_receive()
        {
            var poses = new ActionPoses();
            SimState s = World();
            poses.Observe(s, new[] { new SimEvent(0, SimEventKind.Touched, 1) }, C);
            Assert.Equal(Pose.Receive, poses.Of(s, 0, C));
        }

        [Fact]
        public void A_slow_carrier_with_an_opponent_close_shields_and_one_alone_does_not()
        {
            var poses = new ActionPoses();
            SimState s = World();
            s.Ball.AttachedTo = 1;
            s.Players[1].Position = new Vec2(C.ShieldRange * 0.5, 0);
            Assert.Equal(Pose.Shield, poses.Of(s, 0, C));
            s.Players[1].Position = new Vec2(C.ShieldRange * 2, 0);
            Assert.Equal(Pose.Stand, poses.Of(s, 0, C));
        }

        [Fact]
        public void A_slide_is_drawn_along_its_line_and_a_fall_after_it()
        {
            var poses = new ActionPoses();
            SimState s = World();
            s.Players[0].Challenge = ChallengePhase.Slide;
            s.Players[0].ChallengeLine = new Vec2(0, 1);
            Assert.Equal(Pose.Slide, poses.Of(s, 0, C));
            Assert.Equal(Math.PI / 2, ActionPoses.Facing(s, 0, Pose.Slide), 9);
            s.Players[0].Challenge = ChallengePhase.Grounded;
            Assert.Equal(Pose.Grounded, poses.Of(s, 0, C));
        }

        [Fact]
        public void A_keeper_dives_gloves_first_and_holds_the_ball_in_his_gloves()
        {
            var poses = new ActionPoses();
            SimState s = World();
            s.Keepers[0].Phase = KeeperPhase.Dive;
            s.Keepers[0].DiveVelocity = new Vec2(0, -4);
            Assert.Equal(Pose.KeeperDive, poses.Of(s, 2, C));
            // The sprite's gloves are on its left: facing a quarter turn clockwise of the dive.
            Assert.Equal(-Math.PI, ActionPoses.Facing(s, 2, Pose.KeeperDive), 9);

            s.Keepers[0].Phase = KeeperPhase.Position;
            s.Ball.HeldBy = 3;
            Assert.Equal(Pose.KeeperHold, poses.Of(s, 2, C));
        }

        [Fact]
        public void Choosing_a_pose_changes_nothing_in_the_state()
        {
            var poses = new ActionPoses();
            SimState s = World();
            s.Players[0].Velocity = new Vec2(3, 1);
            var sim = new Simulator(C, s);
            ulong before = sim.StateHash();
            SimState seen = sim.PeekState();
            for (int i = 0; i < seen.Players.Length; i++)
            {
                poses.Observe(seen, Array.Empty<SimEvent>(), C);
                Pose pose = poses.Of(seen, i, C);
                ActionPoses.Facing(seen, i, pose);
            }

            Assert.Equal(before, sim.StateHash());
        }
    }
}
