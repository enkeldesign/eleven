using System;
using System.Collections.Generic;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The vertical pitch (#72): the view shows the controlled team's attacked goal up the screen,
    /// and the touch adapter maps what the player does on screen into the world through the same
    /// orientation, before a command is made. These pin the mapping and its round trip in both
    /// views, and that a flip cancels gestures in progress rather than reversing them.
    /// </summary>
    public class PitchOrientationTests
    {
        private const int Controller = TestWorld.Controller;

        // The same 400 × 400 px screen as ActionSurfaceTests: the stick zone on the left, ACTION on the right.
        private static readonly TouchRegion StickZone = new TouchRegion(0, 0, 200, 400);
        private static readonly TouchRegion ActionZone = new TouchRegion(200, 0, 200, 400);
        private const double StickRadius = 50;

        private static readonly PitchOrientation Up = PitchOrientation.Practice;
        private static readonly PitchOrientation Flipped = PitchOrientation.Practice.Flipped;

        public static readonly TheoryData<int> Views = new TheoryData<int> { 0, 180 };

        private static PitchOrientation View(int degrees) => degrees == 0 ? Up : Flipped;

        // Screen directions: +x right, +y up the screen.
        private static readonly Vec2 ScreenUp = new Vec2(0, 1);
        private static readonly Vec2 ScreenRight = new Vec2(1, 0);

        private static ActionSurfaceAdapter Adapter(PitchOrientation view)
        {
            var adapter = new ActionSurfaceAdapter(Controller, ActionSurfaceSettings.Reference);
            adapter.SetLayout(StickZone, ActionZone, StickRadius, 1);
            adapter.Orient(view);
            return adapter;
        }

        private static TouchSample Down(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Began, x, y, t);

        private static TouchSample To(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Moved, x, y, t);

        private static TouchSample Up_(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Ended, x, y, t);

        [Fact]
        public void Practice_shows_the_plus_x_goal_up_and_a_flip_shows_the_other_end()
        {
            Assert.Equal(1, Up.Up);
            Assert.Equal(0, Up.Degrees);
            Assert.Equal(Up, PitchOrientation.Attacking(1));
            Assert.Equal(-1, Flipped.Up);
            Assert.Equal(180, Flipped.Degrees);
            Assert.Equal(Flipped, PitchOrientation.Attacking(-1));
            Assert.Equal(Up, Flipped.Flipped);
            Assert.Equal(PitchOrientation.Practice, default(PitchOrientation));
        }

        [Theory]
        [MemberData(nameof(Views))]
        public void The_attacked_goal_is_up_the_screen_and_left_and_right_stay_left_and_right(int degrees)
        {
            PitchOrientation view = View(degrees);
            var attack = new Vec2(view.Up, 0);
            Assert.Equal(ScreenUp, view.ToScreen(attack));
            Assert.Equal(attack, view.ToWorld(ScreenUp));

            // Screen right is the attacker's right: a quarter turn clockwise from the attack.
            Assert.Equal(new Vec2(0, -view.Up), view.ToWorld(ScreenRight));

            // The halfway and goal lines run along world y, across the screen.
            Assert.Equal(0, view.ToScreen(new Vec2(0, 1)).Y);
        }

        [Theory]
        [MemberData(nameof(Views))]
        public void The_mapping_is_a_rotation_and_round_trips_exactly(int degrees)
        {
            PitchOrientation view = View(degrees);
            double[] values = { 0, 1, -1, 0.1, -0.3, 3.7, -2.25, 1e-300, -1e300, 52.44 };
            foreach (double x in values)
            foreach (double y in values)
            {
                var w = new Vec2(x, y);
                Vec2 s = view.ToScreen(w);
                Assert.Equal(Bits(w), Bits(view.ToWorld(s)));
                Assert.Equal(Bits(w), Bits(view.ToScreen(view.ToWorld(w))));
                Assert.Equal(w.LengthSquared, s.LengthSquared);

                // A rotation keeps the hand of a turn: the cross product with the attack keeps its sign.
                var a = new Vec2(view.Up, 0);
                Vec2 sa = view.ToScreen(a);
                Assert.Equal(Math.Sign(a.X * w.Y - a.Y * w.X), Math.Sign(sa.X * s.Y - sa.Y * s.X));
            }
        }

        [Theory]
        [MemberData(nameof(Views))]
        public void Zero_components_never_come_out_as_negative_zero(int degrees)
        {
            PitchOrientation view = View(degrees);
            foreach (Vec2 v in new[] { new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1), new Vec2(-1, 0), new Vec2(0, -1) })
            {
                foreach (Vec2 mapped in new[] { view.ToWorld(v), view.ToScreen(v) })
                {
                    Assert.False(IsNegativeZero(mapped.X) || IsNegativeZero(mapped.Y), $"{degrees}°: {v} mapped to a −0 component");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Views))]
        public void The_camera_rolls_so_that_its_up_is_the_attacked_end(int degrees)
        {
            PitchOrientation view = View(degrees);
            double roll = view.CameraRoll * Math.PI / 180;

            // A camera rolled by `roll` about its view axis has its up at (−sin roll, cos roll) in the world.
            Assert.Equal(view.Up, -Math.Sin(roll), 12);
            Assert.Equal(0, Math.Cos(roll), 12);
            Vec2 up = view.ToWorld(ScreenUp);
            Assert.Equal(up.X, -Math.Sin(roll), 12);
            Assert.Equal(up.Y, Math.Cos(roll), 12);
        }

        [Theory]
        [MemberData(nameof(Views))]
        public void The_stick_flick_and_aftertouch_go_the_way_the_screen_shows(int degrees)
        {
            PitchOrientation view = View(degrees);
            ActionSurfaceAdapter adapter = Adapter(view);
            var attack = new Vec2(view.Up, 0);

            // Full stick up the screen: toward the attacked goal.
            adapter.Feed(Down(1, 100, 300, 0));
            adapter.Feed(To(1, 100, 250, 0.01));
            Assert.Equal(attack, adapter.CommandFor(0).Move);

            // Full stick right: the screen's right.
            adapter.Feed(To(1, 150, 300, 0.02));
            Assert.Equal(view.ToWorld(ScreenRight), adapter.CommandFor(1).Move);

            // A flick 40 px up and 20 px right.
            adapter.Feed(Down(2, 300, 200, 0.03));
            adapter.Feed(To(2, 310, 180, 0.05));
            adapter.Feed(Up_(2, 320, 160, 0.06));
            Command flick = adapter.CommandFor(2);
            Assert.True(flick.Kick.HasValue);
            Assert.Equal(view.ToWorld(new Vec2(20, 40)), flick.Kick!.Value.Direction);

            // Aftertouch dragged down the screen: back against an up-field kick.
            adapter.ShapingOpen = true;
            adapter.Feed(Down(3, 300, 200, 0.1));
            adapter.Feed(To(3, 300, 220, 0.12));
            Command shaping = adapter.CommandFor(3);
            Assert.Equal(view.ToWorld(new Vec2(0, -20 / ActionSurfaceSettings.Reference.AftertouchRange)), shaping.Aftertouch);
        }

        [Theory]
        [MemberData(nameof(Views))]
        public void A_flip_cancels_a_held_stick_and_a_pending_flick(int degrees)
        {
            PitchOrientation view = View(degrees);
            ActionSurfaceAdapter adapter = Adapter(view);
            adapter.Feed(Down(1, 100, 300, 0));
            adapter.Feed(To(1, 100, 250, 0.01));
            adapter.Feed(Down(2, 300, 200, 0.02));
            adapter.Feed(Up_(2, 340, 200, 0.05)); // a flick waiting for its tick

            adapter.Orient(view.Flipped);
            Command after = adapter.CommandFor(0);
            Assert.Equal(Vec2.Zero, after.Move);
            Assert.False(after.Kick.HasValue);
            Assert.False(after.QuickPass);

            // The held stick stays ignored until it lifts; a fresh press reads in the new view.
            adapter.Feed(To(1, 100, 240, 0.06));
            Assert.Equal(Vec2.Zero, adapter.CommandFor(1).Move);
            adapter.Feed(Up_(1, 100, 240, 0.07));
            adapter.Feed(Down(3, 100, 300, 0.08));
            adapter.Feed(To(3, 100, 250, 0.09));
            Assert.Equal(new Vec2(view.Flipped.Up, 0), adapter.CommandFor(2).Move);
        }

        public static readonly TheoryData<ControlModel, int> ModelsAndViews = new TheoryData<ControlModel, int>
        {
            { ControlModel.Free, 0 }, { ControlModel.Free, 180 }, { ControlModel.Attached, 0 }, { ControlModel.Attached, 180 },
        };

        // The player at the centre spot with the ball at their feet, on the attacked side.
        private static Simulator Practice(ControlModel model, PitchOrientation view)
        {
            SimConfig config = SpikeScenario.Config().WithControlModel(model);
            SimState state = TestWorld.State(config, new Vec3(0.5 * view.Up, 0, config.BallRadius));
            state.Players[0].Heading = view.Up > 0 ? 0 : Math.PI;
            return new Simulator(config, state);
        }

        [Theory]
        [MemberData(nameof(ModelsAndViews))]
        public void Under_both_models_stick_up_the_screen_runs_up_field(ControlModel model, int degrees)
        {
            PitchOrientation view = View(degrees);
            ActionSurfaceAdapter adapter = Adapter(view);
            Simulator sim = Practice(model, view);
            adapter.Feed(Down(1, 100, 300, 0));
            adapter.Feed(To(1, 100, 250, 0.01));
            for (int t = 0; t < sim.Config.TickRate; t++) sim.Step(new[] { adapter.CommandFor(sim.Tick) }, new List<SimEvent>());

            PlayerState p = sim.PeekState().Players[0];
            Assert.True(p.Position.X * view.Up > 3, $"{model}, {degrees}°: the player reached x = {p.Position.X:F2} m after 1 s of stick up");
            Assert.Equal(0, p.Position.Y, 9);
            Vec2 ball = sim.PeekState().Ball.Position.Horizontal;
            Assert.True(ball.X * view.Up > p.Position.X * view.Up, $"{model}, {degrees}°: the ball at x = {ball.X:F2} m isn't ahead of the player up the screen");
        }

        [Theory]
        [MemberData(nameof(ModelsAndViews))]
        public void Under_both_models_a_flick_up_the_screen_kicks_up_field(ControlModel model, int degrees)
        {
            PitchOrientation view = View(degrees);
            ActionSurfaceAdapter adapter = Adapter(view);
            Simulator sim = Practice(model, view);
            adapter.Feed(Down(2, 300, 200, 0));
            adapter.Feed(To(2, 300, 180, 0.02));
            adapter.Feed(Up_(2, 300, 160, 0.04));
            var events = new List<SimEvent>();
            for (int t = 0; t < sim.Config.TickRate / 2; t++) sim.Step(new[] { adapter.CommandFor(sim.Tick) }, events);

            Assert.Contains(events, e => e.Kind == SimEventKind.Kicked);
            Vec3 v = sim.PeekState().Ball.Velocity;
            Assert.True(v.X * view.Up > 5 * Math.Abs(v.Y), $"{model}, {degrees}°: the kick went ({v.X:F2}, {v.Y:F2}) m/s");
        }

        private static (long, long) Bits(Vec2 v) => (BitConverter.DoubleToInt64Bits(v.X), BitConverter.DoubleToInt64Bits(v.Y));

        private static bool IsNegativeZero(double d) => d == 0 && BitConverter.DoubleToInt64Bits(d) != 0;
    }
}
