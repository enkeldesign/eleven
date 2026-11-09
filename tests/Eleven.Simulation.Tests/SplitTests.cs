using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Candidate B, SPLIT (#28, #76): PASS asks for the footballer's quick pass at the press; KICK
    /// reads a flick as a manual kick along it, a hold → flick as a harder one and a re-touch during
    /// the kick's window as aftertouch; the stick keeps its one role. And the live A ↔ B switch.
    /// </summary>
    public class SplitTests
    {
        private const int Controller = 7;

        // A 400 × 400 px screen at one pixel per point: the stick zone on the left, the right-hand
        // area on the right, PASS a disc at the thumb's rest in it, and KICK the rest of the area.
        private static readonly TouchRegion StickZone = new TouchRegion(0, 0, 200, 400);
        private static readonly TouchRegion ActionZone = new TouchRegion(200, 0, 200, 400);
        private static readonly TouchLayout Layout = new TouchLayout(StickZone, ActionZone, 50, 1, passX: 330, passY: 330, passRadius: 40);

        private static readonly SplitSettings S = SplitSettings.Reference;

        private static SplitAdapter Adapter(SplitSettings? settings = null)
        {
            var adapter = new SplitAdapter(Controller, settings ?? S);
            adapter.SetLayout(Layout);
            return adapter;
        }

        private static TouchSample Down(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Began, x, y, t);

        private static TouchSample To(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Moved, x, y, t);

        private static TouchSample Up(int pointer, double x, double y, double t) => new TouchSample(pointer, TouchPhase.Ended, x, y, t);

        private static Command Feed(GestureAdapter adapter, long tick, params TouchSample[] samples)
        {
            foreach (TouchSample s in samples) adapter.Feed(s);
            return adapter.CommandFor(tick);
        }

        private static bool Neutral(Command c) => !c.Kick.HasValue && !c.QuickPass && c.Aftertouch.X == 0 && c.Aftertouch.Y == 0;

        [Fact]
        public void A_press_on_pass_asks_for_the_quick_pass_at_once_and_once()
        {
            SplitAdapter adapter = Adapter();
            Assert.Equal(TouchRole.Pass, adapter.Feed(Down(1, 330, 330, 0)));

            // Delivered on the next tick while the finger is still down: no hold threshold, no release wait.
            Command c = adapter.CommandFor(0);
            Assert.True(c.QuickPass);
            Assert.False(c.Kick.HasValue);

            Assert.True(Neutral(adapter.CommandFor(1)));
            adapter.Feed(To(1, 360, 300, 0.05));
            adapter.Feed(Up(1, 360, 300, 0.1));
            Assert.True(Neutral(adapter.CommandFor(2)), "lifting PASS, even dragged, asks for nothing more");
        }

        [Fact]
        public void A_press_anywhere_on_the_disc_is_pass_and_just_outside_it_is_kick()
        {
            SplitAdapter adapter = Adapter();
            Assert.Equal(TouchRole.Pass, adapter.Feed(Down(1, 330 + 39, 330, 0)));
            adapter.Feed(Up(1, 330 + 39, 330, 0.02));
            adapter.CommandFor(0);
            Assert.Equal(TouchRole.Kick, adapter.Feed(Down(2, 330 - 45, 330, 0.1)));
        }

        [Fact]
        public void A_press_on_kick_released_without_a_flick_asks_for_nothing()
        {
            // A tap on KICK isn't a pass, and a hold isn't a kick along the facing (Candidate C's, #77).
            Assert.True(Neutral(Feed(Adapter(), 0, Down(1, 250, 100, 0), Up(1, 251, 100, 0.05))));
            Assert.True(Neutral(Feed(Adapter(), 0, Down(1, 250, 100, 0), Up(1, 252, 101, 0.5))));
        }

        [Fact]
        public void An_immediate_flick_on_kick_is_a_manual_kick_along_the_gesture()
        {
            Command c = Feed(Adapter(), 0, Down(1, 250, 200, 0), To(1, 270, 180, 0.03), Up(1, 290, 160, 0.06));

            Assert.True(c.Kick.HasValue);
            Assert.False(c.QuickPass);
            // The vertical pitch (#72): 40 px right and 40 px up the screen is the world line (40, −40).
            Assert.Equal(new Vec2(40, -40), c.Kick!.Value.Direction);
            Assert.Equal(S.FlickStrength, c.Kick.Value.Strength);
            Assert.Equal(S.Loft, c.Kick.Value.Loft);
        }

        [Fact]
        public void A_flick_may_end_anywhere_even_over_pass_only_its_press_decides()
        {
            // From KICK onto PASS's disc: still a kick, along the line.
            Command c = Feed(Adapter(), 0, Down(1, 250, 250, 0), To(1, 290, 290, 0.03), Up(1, 330, 330, 0.06));
            Assert.True(c.Kick.HasValue);
            Assert.Equal(new Vec2(-80, -80), c.Kick!.Value.Direction);
        }

        [Fact]
        public void A_fast_short_flick_counts_and_a_slow_short_one_does_not()
        {
            Command fast = Feed(Adapter(), 0, Down(1, 250, 200, 0), To(1, 255, 200, 0.01), Up(1, 260, 200, 0.02));
            Assert.True(fast.Kick.HasValue, "a 10 pt flick in 20 ms");

            Command slow = Feed(Adapter(), 0, Down(1, 250, 200, 0), To(1, 255, 200, 0.05), Up(1, 260, 200, 0.1));
            Assert.True(Neutral(slow), "a 10 pt drag over 100 ms");
        }

        [Fact]
        public void Hold_then_flick_asks_for_a_stronger_kick_the_longer_the_hold()
        {
            double Strength(double hold)
            {
                Command c = Feed(Adapter(), 0, Down(1, 250, 200, 0), To(1, 250, 200, hold), To(1, 270, 180, hold + 0.02), Up(1, 290, 160, hold + 0.04));
                Assert.True(c.Kick.HasValue, $"no kick after a {hold:F2} s hold");
                return c.Kick!.Value.Strength;
            }

            double immediate = Strength(0.02), half = Strength(S.HoldSeconds + S.ChargeSeconds / 2), full = Strength(S.HoldSeconds + S.ChargeSeconds + 0.1);
            Assert.Equal(S.FlickStrength, immediate);
            Assert.True(half > immediate && half < full, $"half charge {half}, immediate {immediate}, full {full}");
            Assert.Equal(S.ChargedStrength, full);
        }

        [Fact]
        public void The_charge_shows_while_kick_is_held()
        {
            SplitAdapter adapter = Adapter();
            adapter.Feed(Down(1, 250, 200, 0));
            Assert.True(adapter.ActionHeld);
            Assert.Equal(0, adapter.Charge(S.HoldSeconds / 2));
            Assert.Equal(1, adapter.Charge(S.HoldSeconds + S.ChargeSeconds + 0.1));
            adapter.Feed(Up(1, 250, 200, 1));
            Assert.False(adapter.ActionHeld);
        }

        [Fact]
        public void A_retouch_on_kick_during_the_window_shapes_the_ball_and_starts_nothing()
        {
            SplitAdapter adapter = Adapter();
            adapter.ShapingOpen = true;
            Assert.Equal(TouchRole.Shaping, adapter.Feed(Down(1, 250, 200, 0)));
            adapter.Feed(To(1, 250 + S.AftertouchRange, 200, 0.05));
            Command c = adapter.CommandFor(0);
            Assert.False(c.Kick.HasValue || c.QuickPass);
            // Full right on screen is the world's −y at 0° (#72).
            Assert.Equal(new Vec2(0, -1), c.Aftertouch);

            adapter.Feed(Up(1, 250 + S.AftertouchRange, 200, 0.1));
            Assert.True(Neutral(adapter.CommandFor(1)), "lifting a re-touch is never a kick");
        }

        [Fact]
        public void Pass_never_replaces_a_kick_waiting_for_its_tick()
        {
            SplitAdapter adapter = Adapter();
            adapter.Feed(Down(1, 250, 200, 0));
            adapter.Feed(To(1, 270, 180, 0.03));
            adapter.Feed(Up(1, 290, 160, 0.06));
            adapter.Feed(Down(2, 330, 330, 0.07));
            Command c = adapter.CommandFor(0);
            Assert.True(c.Kick.HasValue && !c.QuickPass, "the kick made first is the one delivered");
        }

        [Fact]
        public void The_stick_keeps_moving_the_player_while_the_right_thumb_passes_flicks_and_shapes()
        {
            SplitAdapter adapter = Adapter();
            adapter.Feed(Down(9, 100, 300, 0));
            adapter.Feed(To(9, 100, 250, 0.01));
            Vec2 up = adapter.CommandFor(0).Move;
            Assert.Equal(new Vec2(1, 0), up); // full stick up the screen: +x at 0°

            Command pass = Feed(adapter, 1, Down(1, 330, 330, 0.02));
            Assert.True(pass.QuickPass);
            Assert.Equal(up, pass.Move);

            Command kick = Feed(adapter, 2, Down(2, 250, 200, 0.1), To(2, 270, 180, 0.13), Up(2, 290, 160, 0.16));
            Assert.True(kick.Kick.HasValue);
            Assert.Equal(up, kick.Move);
        }

        [Fact]
        public void Finger_order_does_not_change_the_commands()
        {
            Command[] Run(bool stickFirst)
            {
                SplitAdapter adapter = Adapter();
                TouchSample stick = Down(9, 100, 300, 0), kick = Down(1, 250, 200, 0);
                adapter.Feed(stickFirst ? stick : kick);
                adapter.Feed(stickFirst ? kick : stick);
                adapter.Feed(To(9, 100, 250, 0.01));
                adapter.Feed(To(1, 270, 180, 0.03));
                adapter.Feed(Up(1, 290, 160, 0.06));
                return new[] { adapter.CommandFor(0), adapter.CommandFor(1) };
            }

            Command[] a = Run(true), b = Run(false);
            Assert.Equal(a[0].Move, b[0].Move);
            Assert.Equal(a[0].Kick, b[0].Kick);
            Assert.True(a[0].Kick.HasValue);
            Assert.Equal(a[1].Move, b[1].Move);
        }

        [Fact]
        public void A_second_finger_on_a_held_control_is_ignored()
        {
            SplitAdapter adapter = Adapter();
            adapter.Feed(Down(1, 250, 200, 0));
            Assert.Equal(TouchRole.None, adapter.Feed(Down(2, 260, 100, 0.01)));
            adapter.Feed(To(2, 300, 60, 0.03));
            adapter.Feed(Up(2, 340, 20, 0.05));
            Assert.True(Neutral(adapter.CommandFor(0)), "the second finger on KICK made a command");

            adapter.Feed(Down(3, 330, 330, 0.06));
            Assert.Equal(TouchRole.None, adapter.Feed(Down(4, 335, 335, 0.07)));
            Assert.True(adapter.CommandFor(1).QuickPass);
            Assert.True(Neutral(adapter.CommandFor(2)), "a second finger on PASS passed again");
        }

        [Fact]
        public void A_repeated_begin_for_a_finger_already_down_changes_nothing()
        {
            SplitAdapter adapter = Adapter();
            Assert.Equal(TouchRole.Pass, adapter.Feed(Down(1, 330, 330, 0)));
            Assert.True(adapter.CommandFor(0).QuickPass);
            Assert.Equal(TouchRole.Pass, adapter.Feed(Down(1, 330, 330, 0.02)));
            Assert.True(Neutral(adapter.CommandFor(1)), "a repeated press passed again");

            Assert.Equal(TouchRole.Kick, adapter.Feed(Down(2, 250, 200, 0.03)));
            adapter.Feed(To(2, 120, 200, 0.05)); // into the stick zone
            Assert.Equal(TouchRole.Kick, adapter.Feed(Down(2, 120, 200, 0.06)));
            Assert.False(adapter.StickActive);
        }

        [Fact]
        public void Cancelling_drops_a_gesture_in_progress_so_no_phantom_command_follows()
        {
            SplitAdapter adapter = Adapter();
            adapter.Feed(Down(1, 250, 200, 0));
            adapter.Feed(To(1, 270, 180, 0.03));
            adapter.Feed(Down(2, 330, 330, 0.04)); // a pass waiting for its tick
            adapter.Cancel();
            Assert.True(Neutral(adapter.CommandFor(0)), "the waiting pass survived the cancel");
            adapter.Feed(Down(1, 280, 170, 0.05)); // reported pressed again while still down
            adapter.Feed(Up(1, 290, 160, 0.06));
            Assert.True(Neutral(adapter.CommandFor(1)), "the held flick became a kick after the cancel");
            adapter.Feed(Up(2, 330, 330, 0.07));
            Assert.True(Neutral(adapter.CommandFor(2)), "lifting the cancelled PASS finger asked for something");
        }

        public static readonly TheoryData<int> Views = new TheoryData<int> { 0, 180 };

        [Theory]
        [MemberData(nameof(Views))]
        public void Kick_and_aftertouch_go_the_way_the_screen_shows_in_both_views(int degrees)
        {
            PitchOrientation view = degrees == 0 ? PitchOrientation.Practice : PitchOrientation.Practice.Flipped;
            SplitAdapter adapter = Adapter();
            adapter.Orient(view);

            Command kick = Feed(adapter, 0, Down(1, 250, 200, 0), To(1, 250, 180, 0.02), Up(1, 250, 160, 0.04));
            Assert.True(kick.Kick.HasValue, $"{degrees}°: no kick");
            Assert.Equal(view.ToWorld(new Vec2(0, 40)), kick.Kick!.Value.Direction);
            Assert.True(kick.Kick.Value.Direction.X * view.Up > 0, "a flick up the screen didn't kick up-field");

            adapter.ShapingOpen = true;
            adapter.Feed(Down(2, 250, 200, 0.1));
            adapter.Feed(To(2, 250, 200 + S.AftertouchRange, 0.12));
            Assert.Equal(view.ToWorld(new Vec2(0, -1)), adapter.CommandFor(1).Aftertouch);
        }

        [Fact]
        public void The_same_samples_always_make_the_same_commands()
        {
            List<Command> Run()
            {
                SplitAdapter adapter = Adapter();
                var commands = new List<Command>();
                adapter.Feed(Down(9, 100, 300, 0));
                adapter.Feed(To(9, 130, 280, 0.01));
                commands.Add(adapter.CommandFor(0));
                adapter.Feed(Down(1, 330, 330, 0.02));
                commands.Add(adapter.CommandFor(1));
                adapter.Feed(Up(1, 330, 330, 0.05));
                adapter.Feed(Down(2, 260, 150, 0.1));
                adapter.Feed(To(2, 230, 120, 0.13));
                adapter.Feed(Up(2, 210, 90, 0.15));
                commands.Add(adapter.CommandFor(2));
                return commands;
            }

            Assert.Equal(Run(), Run());
            Assert.True(Run()[1].QuickPass && Run()[2].Kick.HasValue);
        }

        [Fact]
        public void The_settings_are_knobs_with_a_versioned_description_and_a_s_values_as_references()
        {
            Assert.Equal(9, SplitSettings.Parameters.Count);
            Assert.All(SplitSettings.Parameters, p => Assert.Equal(SplitSettings.Group, p.Group));
            string a = ActionSurfaceSettings.Reference.Describe(), b = S.Describe();
            Assert.StartsWith("split/v1 ", b);
            Assert.Equal(a.Substring(a.IndexOf(' ')).Replace("action", "split"), b.Substring(b.IndexOf(' ')));
            Assert.Throws<ArgumentException>(() => S.With("splitHoldSeconds", 5));
            Assert.Equal(0.3, S.With("splitHoldSeconds", 0.3).HoldSeconds);
        }

        // ---- The live switch (#76) -------------------------------------------------------------

        private static TouchCandidates Candidates()
        {
            var candidates = new TouchCandidates(Controller, ActionSurfaceSettings.Reference, S);
            candidates.SetLayout(Layout);
            return candidates;
        }

        [Fact]
        public void A_switch_changes_how_the_same_touch_reads()
        {
            TouchCandidates candidates = Candidates();
            // On A, a tap on the right-hand area is the quick pass, at release.
            candidates.Feed(Down(1, 250, 200, 0));
            candidates.Feed(Up(1, 250, 200, 0.05));
            Assert.True(candidates.CommandFor(0).QuickPass);

            candidates.Switch(ControlCandidate.Split);
            Assert.Equal(ControlCandidate.Split, candidates.Active);
            Assert.StartsWith("split/v1", candidates.Describe());
            // On B the same tap is on KICK, and asks for nothing.
            candidates.Feed(Down(2, 250, 200, 0.1));
            candidates.Feed(Up(2, 250, 200, 0.15));
            Assert.True(Neutral(candidates.CommandFor(1)));
        }

        [Fact]
        public void A_finger_held_across_a_switch_makes_no_command_on_either_candidate()
        {
            foreach (ControlCandidate from in new[] { ControlCandidate.ActionSurface, ControlCandidate.Split })
            {
                TouchCandidates candidates = Candidates();
                candidates.Switch(from);
                ControlCandidate to = from == ControlCandidate.Split ? ControlCandidate.ActionSurface : ControlCandidate.Split;

                // The stick and a flick held, a pass waiting: then the switch.
                candidates.Feed(Down(9, 100, 300, 0));
                candidates.Feed(To(9, 100, 250, 0.01));
                candidates.Feed(Down(1, 250, 200, 0.02));
                candidates.Feed(To(1, 270, 180, 0.05));
                candidates.Switch(to);

                Command after = candidates.CommandFor(0);
                Assert.True(Neutral(after) && after.Move.X == 0 && after.Move.Y == 0, $"{from} → {to}: the held fingers still steer or act");

                // Reported pressed again, moved and lifted under the new candidate: still nothing.
                candidates.Feed(Down(1, 270, 180, 0.06));
                candidates.Feed(To(1, 300, 150, 0.07));
                candidates.Feed(Up(1, 300, 150, 0.08));
                candidates.Feed(Down(9, 100, 250, 0.09));
                candidates.Feed(To(9, 100, 200, 0.1));
                Command lifted = candidates.CommandFor(1);
                Assert.True(Neutral(lifted) && lifted.Move.X == 0 && lifted.Move.Y == 0, $"{from} → {to}: a finger held across the switch made a command");

                // And switching back finds nothing left over.
                candidates.Switch(from);
                Assert.True(Neutral(candidates.CommandFor(2)));
            }
        }

        [Fact]
        public void A_switch_drops_a_command_waiting_for_its_tick()
        {
            TouchCandidates candidates = Candidates();
            candidates.Feed(Down(1, 250, 200, 0));
            candidates.Feed(Up(1, 250, 200, 0.05)); // A's tap, waiting for its tick
            candidates.Switch(ControlCandidate.Split);
            Assert.True(Neutral(candidates.CommandFor(0)));
            candidates.Switch(ControlCandidate.ActionSurface);
            Assert.True(Neutral(candidates.CommandFor(1)), "the dropped tap came back with A");
        }
    }
}
