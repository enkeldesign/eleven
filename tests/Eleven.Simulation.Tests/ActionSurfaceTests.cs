using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Candidate A, ACTION SURFACE (#28): the touch adapter reads raw pointer samples into owned
    /// commands over the existing intents. A tap asks for the footballer's quick pass, a flick for a
    /// manual kick along the gesture, a hold → flick for a harder one, and a re-touch during the
    /// kick's window for aftertouch, while the stick keeps moving the player.
    /// </summary>
    public class ActionSurfaceTests
    {
        private const int Controller = 7;

        // A 400 × 400 px screen at one pixel per point: the stick zone on the left, ACTION on the right.
        private static readonly TouchRegion StickZone = new TouchRegion(0, 0, 200, 400);
        private static readonly TouchRegion ActionZone = new TouchRegion(200, 0, 200, 400);
        private const double StickRadius = 50;

        private static readonly ActionSurfaceSettings S = ActionSurfaceSettings.Reference;

        private static ActionSurfaceAdapter Adapter(ActionSurfaceSettings? settings = null)
        {
            var adapter = new ActionSurfaceAdapter(Controller, settings ?? S);
            adapter.SetLayout(StickZone, ActionZone, StickRadius, 1);
            return adapter;
        }

        private static TouchSample Down(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Began, x, y, t);

        private static TouchSample To(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Moved, x, y, t);

        private static TouchSample Up(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Ended, x, y, t);

        private static Command Feed(ActionSurfaceAdapter adapter, long tick, params TouchSample[] samples)
        {
            foreach (TouchSample s in samples) adapter.Feed(s);
            return adapter.CommandFor(tick);
        }

        private static bool Neutral(Command c) => !c.Kick.HasValue && !c.QuickPass && c.Aftertouch.X == 0 && c.Aftertouch.Y == 0;

        [Fact]
        public void A_tap_asks_for_the_assisted_quick_pass()
        {
            Command c = Feed(Adapter(), 0, Down(1, 300, 200, 0), Up(1, 301, 200, 0.08));

            Assert.True(c.QuickPass);
            Assert.False(c.Kick.HasValue);
        }

        [Fact]
        public void A_tap_is_delivered_at_release_without_waiting_for_the_hold_threshold()
        {
            ActionSurfaceAdapter adapter = Adapter();
            adapter.Feed(Down(1, 300, 200, 0));
            Assert.False(adapter.CommandFor(0).QuickPass); // still down: nothing yet
            adapter.Feed(Up(1, 300, 200, 0.03));           // lifted well before the 0.18 s threshold

            Assert.True(adapter.CommandFor(1).QuickPass);
            Assert.False(adapter.CommandFor(2).QuickPass); // delivered once
        }

        [Fact]
        public void An_immediate_flick_is_a_manual_kick_along_the_gesture()
        {
            Command c = Feed(Adapter(), 0, Down(1, 300, 200, 0), To(1, 320, 180, 0.03), Up(1, 340, 160, 0.06));

            Assert.True(c.Kick.HasValue);
            Assert.False(c.QuickPass);
            // The pitch is vertical (#72): screen up is toward the attacked goal, +x in practice,
            // and screen right is −y. 40 px right and 40 px up is the line (40, −40).
            Assert.Equal(new Vec2(40, -40), c.Kick!.Value.Direction);
            Assert.Equal(S.FlickStrength, c.Kick.Value.Strength);
            Assert.Equal(S.Loft, c.Kick.Value.Loft);
        }

        [Fact]
        public void A_fast_short_flick_counts_and_a_slow_short_one_is_a_tap()
        {
            // 10 pt in 20 ms is 500 pt/s: short of the 20 pt flick, but past 8 pt and fast enough.
            Command fast = Feed(Adapter(), 0, Down(1, 300, 200, 0), Up(1, 310, 200, 0.02));
            Assert.True(fast.Kick.HasValue);

            // The same 10 pt over 0.1 s is 100 pt/s: a tap that wobbled.
            Command slow = Feed(Adapter(), 0, Down(1, 300, 200, 0), To(1, 305, 200, 0.05), Up(1, 310, 200, 0.1));
            Assert.False(slow.Kick.HasValue);
            Assert.True(slow.QuickPass);
        }

        [Fact]
        public void Hold_then_flick_asks_for_a_stronger_kick_the_longer_the_hold()
        {
            double previous = S.FlickStrength;
            foreach (double hold in new[] { 0.25, 0.4, 0.6, 0.9 })
            {
                Command c = Feed(Adapter(), 0, Down(1, 300, 200, 0), To(1, 300, 200, hold), To(1, 320, 200, hold + 0.02), Up(1, 340, 200, hold + 0.04));
                Assert.True(c.Kick.HasValue, $"hold {hold}");
                double strength = c.Kick!.Value.Strength;
                Assert.True(strength > S.FlickStrength, $"hold {hold}: {strength}");
                Assert.True(strength >= previous && strength <= S.ChargedStrength, $"hold {hold}: {strength}");
                previous = strength;
            }

            Assert.Equal(S.ChargedStrength, previous); // past the full charge
        }

        [Fact]
        public void A_hold_released_without_a_flick_asks_for_nothing()
        {
            Command c = Feed(Adapter(), 0, Down(1, 300, 200, 0), Up(1, 302, 200, 0.4));

            Assert.True(Neutral(c));
        }

        [Fact]
        public void A_manual_kick_into_empty_space_is_not_snapped_to_a_receiver()
        {
            // The pass drill: receivers ahead (14, 3), behind (-7, -1) and wide (3, 6). The flick
            // goes at -45° on the pitch, into space at least 57° from every one of them: up and
            // right on the vertical pitch's screen (#72).
            SimConfig config = SpikeScenario.Config();
            var sim = new Simulator(config, PassDrill.InitialState(config, PlayerCapabilities.Reference, PassDrill.Standard));
            var adapter = new ActionSurfaceAdapter(PassDrill.PasserController, S);
            adapter.SetLayout(StickZone, ActionZone, StickRadius, 1);
            adapter.Feed(Down(1, 300, 200, 0));
            adapter.Feed(Up(1, 340, 160, 0.05)); // right and up the screen

            var events = new List<SimEvent>();
            var commands = new List<Command>();
            for (long tick = 0; tick < config.TickRate && !events.Any(e => e.Kind == SimEventKind.Kicked); tick++)
            {
                commands.Clear();
                commands.Add(adapter.CommandFor(tick));
                Assert.False(commands[0].QuickPass);
                sim.Step(commands, events);
            }

            Assert.Contains(events, e => e.Kind == SimEventKind.Kicked);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.QuickPassResolved);
            Vec3 v = sim.PeekState().Ball.Velocity;
            double angle = Math.Atan2(v.Y, v.X);
            Assert.InRange(angle, -Math.PI / 4 - PlayerCapabilities.Reference.KickError - 1e-9, -Math.PI / 4 + PlayerCapabilities.Reference.KickError + 1e-9);
        }

        [Fact]
        public void A_retouch_and_drag_during_the_window_shapes_the_ball_and_starts_nothing()
        {
            ActionSurfaceAdapter adapter = Adapter();
            Assert.True(Feed(adapter, 0, Down(1, 300, 200, 0), Up(1, 340, 200, 0.05)).Kick.HasValue);

            adapter.ShapingOpen = true; // the state now shows the kick winding up or its window open
            Assert.Equal(TouchRole.Shaping, adapter.Feed(Down(2, 300, 200, 0.2)));
            Command shaping = Feed(adapter, 1, To(2, 320, 200, 0.22));
            Assert.Equal(new Vec2(0, -20 / S.AftertouchRange), shaping.Aftertouch); // screen right is −y (#72)
            Assert.False(shaping.Kick.HasValue);
            Assert.False(shaping.QuickPass);

            // Lifting a quick re-touch is never a tap.
            Command lifted = Feed(adapter, 2, Up(2, 320, 200, 0.24));
            Assert.True(Neutral(lifted));
        }

        [Fact]
        public void Aftertouch_is_at_most_full_and_follows_the_screen()
        {
            ActionSurfaceAdapter adapter = Adapter();
            adapter.ShapingOpen = true;
            adapter.Feed(Down(2, 300, 200, 0));
            Command c = Feed(adapter, 0, To(2, 300, 100, 0.05)); // far up the screen

            Assert.Equal(1, c.Aftertouch.X, 12); // up the screen is toward the attacked goal, +x (#72), capped at full
            Assert.Equal(0, c.Aftertouch.Y, 12);
        }

        [Fact]
        public void Outside_the_window_a_press_on_action_starts_a_gesture()
        {
            ActionSurfaceAdapter adapter = Adapter();
            adapter.ShapingOpen = false;

            Assert.Equal(TouchRole.Action, adapter.Feed(Down(1, 300, 200, 0)));
        }

        [Fact]
        public void The_stick_keeps_moving_the_player_while_the_right_thumb_flicks_and_shapes()
        {
            ActionSurfaceAdapter adapter = Adapter();
            Assert.Equal(TouchRole.Stick, adapter.Feed(Down(1, 100, 200, 0)));
            adapter.Feed(To(1, 150, 200, 0.01)); // full stick right

            Command flick = Feed(adapter, 0, Down(2, 300, 200, 0.02), Up(2, 340, 200, 0.06));
            Assert.Equal(new Vec2(0, -1), flick.Move); // screen right is −y (#72)
            Assert.True(flick.Kick.HasValue);

            adapter.ShapingOpen = true;
            Command shaping = Feed(adapter, 1, Down(3, 300, 200, 0.1), To(3, 300, 220, 0.12));
            Assert.Equal(new Vec2(0, -1), shaping.Move);
            Assert.True(shaping.Aftertouch.X < 0); // down the screen, away from the attacked goal
        }

        [Fact]
        public void A_repeated_begin_for_a_finger_already_down_changes_nothing()
        {
            // Platforms can report a held finger's press again when another finger lands.
            ActionSurfaceAdapter adapter = Adapter();
            adapter.Feed(Down(1, 100, 200, 0));
            adapter.Feed(To(1, 150, 200, 0.01));
            adapter.Feed(Down(2, 300, 200, 0.02));

            Assert.Equal(TouchRole.Stick, adapter.Feed(Down(1, 150, 200, 0.03)));
            Assert.Equal(TouchRole.Action, adapter.Feed(Down(2, 300, 200, 0.04)));
            Command c = Feed(adapter, 0, Up(2, 340, 200, 0.06));
            Assert.Equal(new Vec2(0, -1), c.Move); // screen right is −y (#72)
            Assert.True(c.Kick.HasValue);
            Assert.Equal(new Vec2(0, -40), c.Kick!.Value.Direction); // from the first press, not the repeat
        }

        [Fact]
        public void Finger_order_does_not_change_the_commands()
        {
            // The same two touches, the stick's finger landing first or second.
            TouchSample[] stick = { Down(1, 100, 200, 0.00), To(1, 130, 160, 0.02) };
            TouchSample[] action = { Down(2, 300, 200, 0.00), To(2, 330, 170, 0.02), Up(2, 360, 140, 0.04) };

            List<Command> stickFirst = Run(stick.Concat(action));
            List<Command> actionFirst = Run(action.Concat(stick));
            List<Command> interleaved = Run(new[] { action[0], stick[0], action[1], stick[1], action[2] });

            Assert.Equal(Describe(stickFirst), Describe(actionFirst));
            Assert.Equal(Describe(stickFirst), Describe(interleaved));
            Assert.True(stickFirst[0].Kick.HasValue);
            Assert.NotEqual(Vec2.Zero, stickFirst[0].Move);

            static List<Command> Run(IEnumerable<TouchSample> samples)
            {
                ActionSurfaceAdapter adapter = Adapter();
                foreach (TouchSample s in samples) adapter.Feed(s);
                return new List<Command> { adapter.CommandFor(0), adapter.CommandFor(1) };
            }
        }

        [Fact]
        public void Cancelling_drops_a_gesture_in_progress_so_no_phantom_command_follows()
        {
            ActionSurfaceAdapter adapter = Adapter();
            adapter.Feed(Down(1, 100, 200, 0));
            adapter.Feed(To(1, 150, 200, 0.01));  // the stick held full right
            adapter.Feed(Down(2, 300, 200, 0));
            adapter.Feed(Up(2, 301, 200, 0.05));  // a tap, not yet delivered

            adapter.Cancel(); // reset, retry or new settings

            Command first = adapter.CommandFor(0);
            Assert.True(Neutral(first), "the pending tap is gone");
            Assert.Equal(Vec2.Zero, first.Move); // the stick's finger is ignored until it lifts

            adapter.Feed(Down(3, 300, 200, 0.1)); // a hold on ACTION...
            adapter.Cancel();                      // ...cancelled before it flicks
            Command held = Feed(adapter, 1, To(1, 100, 250, 0.11), To(3, 340, 200, 0.12), Up(3, 360, 200, 0.14), Up(1, 100, 250, 0.15));
            Assert.True(Neutral(held), "a finger down at the cancel never ends as a kick");
            Assert.Equal(Vec2.Zero, held.Move);

            // Fresh touches after the cancelled ones lift work again.
            Assert.True(Feed(adapter, 2, Down(4, 300, 200, 0.3), Up(4, 300, 200, 0.33)).QuickPass);
            Assert.Equal(TouchRole.Stick, adapter.Feed(Down(5, 100, 200, 0.34)));
        }

        [Fact]
        public void A_finger_held_across_a_cancel_stays_ignored_when_reported_pressed_again()
        {
            // Unity reports a held finger pressed again when its phase changes (Codex on #57): a
            // finger down at the cancel must stay ignored until it lifts, repeat or not.
            ActionSurfaceAdapter adapter = Adapter();
            adapter.Feed(Down(1, 100, 200, 0));
            adapter.Feed(To(1, 150, 200, 0.01));
            adapter.Feed(Down(2, 300, 200, 0.02));
            adapter.Cancel();

            Assert.Equal(TouchRole.None, adapter.Feed(Down(1, 150, 200, 0.03)));
            Assert.Equal(TouchRole.None, adapter.Feed(Down(2, 300, 200, 0.04)));
            Command c = Feed(adapter, 0, To(1, 160, 200, 0.05), To(2, 340, 200, 0.06), Up(2, 360, 200, 0.07));
            Assert.True(Neutral(c), "no phantom kick from a finger held across the cancel");
            Assert.Equal(Vec2.Zero, c.Move);

            // Once lifted, the same ids are new fingers.
            adapter.Feed(Up(1, 160, 200, 0.08));
            Assert.Equal(TouchRole.Stick, adapter.Feed(Down(1, 100, 200, 0.1)));
            Assert.Equal(TouchRole.Action, adapter.Feed(Down(2, 300, 200, 0.1)));
        }

        [Fact]
        public void New_settings_cancel_a_gesture_in_progress()
        {
            ActionSurfaceAdapter adapter = Adapter();
            adapter.Feed(Down(1, 300, 200, 0));
            adapter.Use(S.With("actionFlickStrength", 0.7));

            Assert.True(Neutral(Feed(adapter, 0, Up(1, 340, 200, 0.05))));
            Assert.Equal(0.7, adapter.Settings.FlickStrength);
        }

        [Fact]
        public void The_same_samples_always_make_the_same_commands()
        {
            TouchSample[] samples =
            {
                Down(1, 120, 220, 0.00), To(1, 160, 190, 0.03),
                Down(2, 260, 300, 0.05), To(2, 260, 300, 0.40), To(2, 290, 260, 0.42), Up(2, 310, 240, 0.44),
            };

            Assert.Equal(Describe(Run()), Describe(Run()));

            List<Command> Run()
            {
                ActionSurfaceAdapter adapter = Adapter();
                var commands = new List<Command>();
                foreach (TouchSample s in samples)
                {
                    adapter.Feed(s);
                    commands.Add(adapter.CommandFor(commands.Count));
                }

                return commands;
            }
        }

        [Fact]
        public void The_settings_are_knobs_with_research_ranges_and_no_second_aftertouch_duration()
        {
            foreach (TunableParameter p in ActionSurfaceSettings.Parameters)
            {
                Assert.Equal(ActionSurfaceSettings.Group, p.Group);
                double value = S.Get(p.Name);
                Assert.InRange(value, p.Min, p.Max);
                if (p.StartMin.HasValue) Assert.InRange(value, p.StartMin!.Value, p.StartMax!.Value);
            }

            // #28's research starting ranges.
            TunableParameter hold = ActionSurfaceSettings.Parameters.Single(p => p.Name == "actionHoldSeconds");
            TunableParameter flick = ActionSurfaceSettings.Parameters.Single(p => p.Name == "actionFlickDistance");
            Assert.Equal((0.12, 0.25), (hold.StartMin, hold.StartMax));
            Assert.Equal((12.0, 32.0), (flick.StartMin, flick.StartMax));

            // The aftertouch window is the simulation's aftertouchSeconds alone.
            Assert.DoesNotContain(ActionSurfaceSettings.Parameters, p => p.Name.IndexOf("aftertouchSeconds", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Throws<ArgumentException>(() => S.With("actionHoldSeconds", 5));
            Assert.Contains($"{ActionSurfaceSettings.Candidate}/v{ActionSurfaceSettings.Version}", S.Describe());
        }

        private static string Describe(IEnumerable<Command> commands) => string.Join(" | ", commands.Select(c =>
            $"{c.Tick} move={Bits(c.Move.X)},{Bits(c.Move.Y)} kick={(c.Kick.HasValue ? $"{Bits(c.Kick.Value.Direction.X)},{Bits(c.Kick.Value.Direction.Y)},{Bits(c.Kick.Value.Strength)},{Bits(c.Kick.Value.Loft)}" : "-")} pass={c.QuickPass} after={Bits(c.Aftertouch.X)},{Bits(c.Aftertouch.Y)}"));

        private static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("x16");
    }
}
