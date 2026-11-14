using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Candidate C, ONE FIRE (#28, #77): a tap on the one fire control asks for the footballer's
    /// quick pass at its release; a hold, released, asks for a manual kick along the footballer's
    /// heading, harder the longer the hold; a re-touch during the kick's window is aftertouch; the
    /// stick keeps its one role. And the live A ↔ B ↔ C switch.
    /// </summary>
    public class OneFireTests
    {
        private const int Controller = 7;

        // A 400 × 400 px screen at one pixel per point: the stick zone on the left, fire on the right.
        private static readonly TouchRegion StickZone = new TouchRegion(0, 0, 200, 400);
        private static readonly TouchRegion ActionZone = new TouchRegion(200, 0, 200, 400);
        private static readonly TouchLayout Layout = new TouchLayout(StickZone, ActionZone, 50, 1, passX: 330, passY: 330, passRadius: 40);

        private static readonly OneFireSettings S = OneFireSettings.Reference;

        private static OneFireAdapter Adapter(OneFireSettings? settings = null)
        {
            var adapter = new OneFireAdapter(Controller, settings ?? S);
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
        public void A_tap_on_fire_asks_for_the_quick_pass_at_its_release()
        {
            OneFireAdapter adapter = Adapter();
            Assert.Equal(TouchRole.Fire, adapter.Feed(Down(1, 250, 200, 0)));
            Assert.True(Neutral(adapter.CommandFor(0)), "nothing is asked while the finger is down");

            // At the release, well inside the hold threshold: no wait for the threshold to pass.
            adapter.Feed(Up(1, 250, 200, 0.05));
            Command c = adapter.CommandFor(1);
            Assert.True(c.QuickPass);
            Assert.False(c.Kick.HasValue);
            Assert.True(Neutral(adapter.CommandFor(2)), "the tap passed twice");
        }

        [Fact]
        public void A_hold_released_asks_for_a_kick_along_the_heading_and_never_a_pass()
        {
            Command c = Feed(Adapter(), 0, Down(1, 250, 200, 0), Up(1, 250, 200, S.HoldSeconds + 0.1));

            Assert.True(c.Kick.HasValue);
            Assert.False(c.QuickPass, "a held release passed");
            // KickIntent.AlongHeading: no direction of the adapter's own. The simulation reads the
            // footballer's heading on the tick it accepts the command.
            Assert.Equal(Vec2.Zero, c.Kick!.Value.Direction);
            Assert.Equal(S.Loft, c.Kick.Value.Loft);
        }

        [Fact]
        public void The_longer_the_hold_the_stronger_the_kick()
        {
            double Strength(double hold)
            {
                Command c = Feed(Adapter(), 0, Down(1, 250, 200, 0), Up(1, 250, 200, hold));
                Assert.True(c.Kick.HasValue, $"no kick after a {hold:F2} s hold");
                return c.Kick!.Value.Strength;
            }

            double least = Strength(S.HoldSeconds), half = Strength(S.HoldSeconds + S.ChargeSeconds / 2), full = Strength(S.HoldSeconds + S.ChargeSeconds + 0.1);
            Assert.Equal(S.HoldStrength, least);
            Assert.True(half > least && half < full, $"half charge {half}, least {least}, full {full}");
            Assert.Equal(S.ChargedStrength, full);
        }

        [Fact]
        public void A_drag_on_fire_aims_nothing()
        {
            // No flick: a tap that travels is still the pass, and a hold that travels still kicks
            // along the heading.
            Command tap = Feed(Adapter(), 0, Down(1, 250, 200, 0), To(1, 300, 150, 0.02), Up(1, 340, 110, 0.04));
            Assert.True(tap.QuickPass && !tap.Kick.HasValue, "a fast drag on fire wasn't the pass");

            Command hold = Feed(Adapter(), 0, Down(1, 250, 200, 0), To(1, 300, 150, 0.2), Up(1, 340, 110, 0.4));
            Assert.True(hold.Kick.HasValue && !hold.QuickPass);
            Assert.Equal(Vec2.Zero, hold.Kick!.Value.Direction);
        }

        [Fact]
        public void The_charge_shows_while_fire_is_held()
        {
            OneFireAdapter adapter = Adapter();
            Assert.Equal(0, adapter.Charge(1));
            adapter.Feed(Down(1, 250, 200, 0));
            Assert.True(adapter.ActionHeld);
            Assert.Equal(0, adapter.Charge(S.HoldSeconds / 2));
            Assert.Equal(0.5, adapter.Charge(S.HoldSeconds + S.ChargeSeconds / 2), 9);
            Assert.Equal(1, adapter.Charge(S.HoldSeconds + S.ChargeSeconds + 0.1));
            adapter.Feed(Up(1, 250, 200, 1));
            Assert.False(adapter.ActionHeld);
        }

        [Fact]
        public void A_retouch_during_the_window_shapes_the_ball_and_starts_nothing()
        {
            OneFireAdapter adapter = Adapter();
            adapter.ShapingOpen = true;
            Assert.Equal(TouchRole.Shaping, adapter.Feed(Down(1, 250, 200, 0)));
            adapter.Feed(To(1, 250 + S.AftertouchRange, 200, 0.05));
            Command c = adapter.CommandFor(0);
            Assert.False(c.Kick.HasValue || c.QuickPass);
            // Full right on screen is the world's −y at 0° (#72).
            Assert.Equal(new Vec2(0, -1), c.Aftertouch);

            adapter.Feed(Up(1, 250 + S.AftertouchRange, 200, 0.5));
            Assert.True(Neutral(adapter.CommandFor(1)), "lifting a re-touch is never a tap or a kick");
        }

        [Fact]
        public void Outside_the_window_a_touch_is_a_fire_gesture_not_shaping()
        {
            OneFireAdapter adapter = Adapter();
            adapter.ShapingOpen = false;
            Assert.Equal(TouchRole.Fire, adapter.Feed(Down(1, 250, 200, 0)));
            adapter.Feed(To(1, 290, 200, 0.02));
            Command c = adapter.CommandFor(0);
            Assert.True(c.Aftertouch.X == 0 && c.Aftertouch.Y == 0, "a drag outside the window shaped the ball");
            Assert.False(adapter.Shaping);
        }

        [Fact]
        public void The_stick_keeps_moving_the_player_while_the_right_thumb_taps_holds_and_shapes()
        {
            OneFireAdapter adapter = Adapter();
            adapter.Feed(Down(9, 100, 300, 0));
            adapter.Feed(To(9, 100, 250, 0.01));
            Vec2 up = adapter.CommandFor(0).Move;
            Assert.Equal(new Vec2(1, 0), up); // full stick up the screen: +x at 0°

            Command pass = Feed(adapter, 1, Down(1, 250, 200, 0.02), Up(1, 250, 200, 0.06));
            Assert.True(pass.QuickPass);
            Assert.Equal(up, pass.Move);

            Command kick = Feed(adapter, 2, Down(2, 250, 200, 0.1), Up(2, 250, 200, 0.5));
            Assert.True(kick.Kick.HasValue);
            Assert.Equal(up, kick.Move);

            adapter.ShapingOpen = true;
            adapter.Feed(Down(3, 250, 200, 0.6));
            adapter.Feed(To(3, 250, 200 - S.AftertouchRange, 0.65));
            Command shape = adapter.CommandFor(3);
            Assert.Equal(up, shape.Move);
            Assert.Equal(new Vec2(1, 0), shape.Aftertouch);
        }

        [Fact]
        public void Finger_order_does_not_change_the_commands()
        {
            Command[] Run(bool stickFirst)
            {
                OneFireAdapter adapter = Adapter();
                TouchSample stick = Down(9, 100, 300, 0), fire = Down(1, 250, 200, 0);
                adapter.Feed(stickFirst ? stick : fire);
                adapter.Feed(stickFirst ? fire : stick);
                adapter.Feed(To(9, 100, 250, 0.01));
                adapter.Feed(Up(1, 250, 200, 0.4));
                return new[] { adapter.CommandFor(0), adapter.CommandFor(1) };
            }

            Command[] a = Run(true), b = Run(false);
            Assert.Equal(a[0].Move, b[0].Move);
            Assert.Equal(a[0].Kick, b[0].Kick);
            Assert.True(a[0].Kick.HasValue);
            Assert.Equal(a[1].Move, b[1].Move);
        }

        [Fact]
        public void A_second_finger_on_fire_is_ignored()
        {
            OneFireAdapter adapter = Adapter();
            Assert.Equal(TouchRole.Fire, adapter.Feed(Down(1, 250, 200, 0)));
            Assert.Equal(TouchRole.None, adapter.Feed(Down(2, 260, 100, 0.01)));
            adapter.Feed(Up(2, 260, 100, 0.03));
            Assert.True(Neutral(adapter.CommandFor(0)), "the second finger's tap passed");
            adapter.Feed(Up(1, 250, 200, 0.5));
            Assert.True(adapter.CommandFor(1).Kick.HasValue, "the first finger's hold was lost");
        }

        [Fact]
        public void A_repeated_begin_for_a_finger_already_down_keeps_its_press()
        {
            OneFireAdapter adapter = Adapter();
            Assert.Equal(TouchRole.Fire, adapter.Feed(Down(1, 250, 200, 0)));
            // Reported pressed again late in the hold: the hold is still timed from the first press.
            Assert.Equal(TouchRole.Fire, adapter.Feed(Down(1, 250, 200, S.HoldSeconds + 0.05)));
            adapter.Feed(Up(1, 250, 200, S.HoldSeconds + 0.1));
            Command c = adapter.CommandFor(0);
            Assert.True(c.Kick.HasValue && !c.QuickPass, "the repeated press restarted the hold");
        }

        [Fact]
        public void Cancelling_drops_a_gesture_in_progress_so_no_phantom_command_follows()
        {
            OneFireAdapter adapter = Adapter();
            adapter.Feed(Down(1, 250, 200, 0));
            adapter.Cancel();
            adapter.Feed(Down(1, 250, 200, 0.3)); // reported pressed again while still down
            adapter.Feed(Up(1, 250, 200, 0.4));
            Assert.True(Neutral(adapter.CommandFor(0)), "the held finger kicked after the cancel");

            // A tap waiting for its tick is dropped too.
            adapter.Feed(Down(2, 250, 200, 0.5));
            adapter.Feed(Up(2, 250, 200, 0.55));
            adapter.Cancel();
            Assert.True(Neutral(adapter.CommandFor(1)), "the waiting pass survived the cancel");
        }

        [Fact]
        public void A_touch_the_system_takes_makes_no_command()
        {
            OneFireAdapter adapter = Adapter();
            adapter.Feed(Down(1, 250, 200, 0));
            adapter.Feed(new TouchSample(1, TouchPhase.Canceled, 250, 200, 0.4));
            Assert.True(Neutral(adapter.CommandFor(0)));
            Assert.False(adapter.ActionHeld);
        }

        public static readonly TheoryData<int> Views = new TheoryData<int> { 0, 180 };

        [Theory]
        [MemberData(nameof(Views))]
        public void The_kick_follows_the_heading_and_aftertouch_the_screen_in_both_views(int degrees)
        {
            PitchOrientation view = degrees == 0 ? PitchOrientation.Practice : PitchOrientation.Practice.Flipped;
            OneFireAdapter adapter = Adapter();
            adapter.Orient(view);

            // The kick has no screen direction to map: along the heading, whatever the view.
            Command kick = Feed(adapter, 0, Down(1, 250, 200, 0), Up(1, 250, 200, 0.4));
            Assert.True(kick.Kick.HasValue, $"{degrees}°: no kick");
            Assert.Equal(Vec2.Zero, kick.Kick!.Value.Direction);

            // The stick and aftertouch go the way the screen shows.
            adapter.Feed(Down(9, 100, 300, 0.5));
            adapter.Feed(To(9, 100, 250, 0.51));
            adapter.ShapingOpen = true;
            adapter.Feed(Down(2, 250, 200, 0.6));
            adapter.Feed(To(2, 250, 200 + S.AftertouchRange, 0.62));
            Command c = adapter.CommandFor(1);
            Assert.Equal(view.ToWorld(new Vec2(0, 1)), c.Move);
            Assert.True(c.Move.X * view.Up > 0, "the stick up the screen didn't move up-field");
            Assert.Equal(view.ToWorld(new Vec2(0, -1)), c.Aftertouch);
        }

        // #77: the authoritative facing sample is the heading on the tick the simulation accepts
        // the command. A turn during the wind-up doesn't move the line (KickTests has the rule; this
        // is C's own command through it).
        [Fact]
        public void The_simulation_kicks_along_the_heading_it_had_when_it_took_the_command()
        {
            // A player at the origin running along +x, the ball in reach ahead.
            SimConfig config = TestWorld.Config();
            SimState state = TestWorld.State(config, ballPosition: new Vec3(1.2, 0, config.BallRadius));
            PlayerCapabilities exact = PlayerCapabilities.Reference;
            exact.KickError = 0;
            state.Players[0].Capabilities = exact;
            state.Players[0].Velocity = new Vec2(4, 0);
            Simulator sim = TestWorld.Simulator(config, state);

            OneFireAdapter adapter = Adapter();
            adapter.Feed(Down(9, 100, 300, 0));
            adapter.Feed(To(9, 100, 250, 0.01)); // the stick up the screen: +x at 0°
            adapter.Feed(Down(1, 250, 200, 0.02));
            adapter.Feed(Up(1, 250, 200, 0.42));
            Command kick = adapter.CommandFor(0);
            Assert.True(kick.Kick.HasValue);
            TestWorld.Step(sim, kick);

            // Stick hard to the side through the wind-up: the footballer turns, the line doesn't.
            adapter.Feed(To(9, 150, 300, 0.43));
            for (long t = 1; t <= config.KickWindUpTicks; t++) TestWorld.Step(sim, adapter.CommandFor(t));

            Assert.True(Math.Abs(sim.PeekState().Players[0].Heading) > 0.3, "the footballer didn't turn");
            Vec3 v = sim.PeekState().Ball.Velocity;
            Assert.True(v.X > 0, "the ball wasn't kicked");
            Assert.Equal(0, Math.Atan2(v.Y, v.X), 12);
        }

        [Fact]
        public void The_same_samples_always_make_the_same_commands()
        {
            List<Command> Run()
            {
                OneFireAdapter adapter = Adapter();
                var commands = new List<Command>();
                adapter.Feed(Down(9, 100, 300, 0));
                adapter.Feed(To(9, 130, 280, 0.01));
                commands.Add(adapter.CommandFor(0));
                adapter.Feed(Down(1, 250, 200, 0.02));
                adapter.Feed(Up(1, 250, 200, 0.05));
                commands.Add(adapter.CommandFor(1));
                adapter.Feed(Down(2, 260, 150, 0.1));
                adapter.Feed(Up(2, 260, 150, 0.45));
                commands.Add(adapter.CommandFor(2));
                return commands;
            }

            Assert.Equal(Run(), Run());
            Assert.True(Run()[1].QuickPass && Run()[2].Kick.HasValue);
        }

        [Fact]
        public void The_settings_are_knobs_with_a_versioned_description_and_a_s_values_as_references()
        {
            Assert.Equal(6, OneFireSettings.Parameters.Count);
            Assert.All(OneFireSettings.Parameters, p => Assert.Equal(OneFireSettings.Group, p.Group));
            Assert.All(OneFireSettings.Parameters, p => Assert.InRange(S.Get(p.Name), p.Min, p.Max));
            ActionSurfaceSettings a = ActionSurfaceSettings.Reference;
            Assert.Equal(a.HoldSeconds, S.HoldSeconds);
            Assert.Equal(a.FlickStrength, S.HoldStrength);
            Assert.Equal(a.ChargeSeconds, S.ChargeSeconds);
            Assert.Equal(a.ChargedStrength, S.ChargedStrength);
            Assert.Equal(a.Loft, S.Loft);
            Assert.Equal(a.AftertouchRange, S.AftertouchRange);
            Assert.StartsWith("one-fire/v1 fireHoldSeconds=", S.Describe());
            Assert.Throws<ArgumentException>(() => S.With("fireHoldSeconds", 5));
            Assert.Throws<ArgumentException>(() => S.With("actionHoldSeconds", 0.2));
            Assert.Equal(0.3, S.With("fireHoldSeconds", 0.3).HoldSeconds);
            Assert.Equal(S.HoldStrength, S.With("fireHoldSeconds", 0.3).HoldStrength);
        }

        // ---- The live switch, three ways (#77) -------------------------------------------------

        private static readonly ControlCandidate[] All = { ControlCandidate.ActionSurface, ControlCandidate.Split, ControlCandidate.OneFire };

        private static TouchCandidates Candidates()
        {
            var candidates = new TouchCandidates(Controller, ActionSurfaceSettings.Reference, SplitSettings.Reference, S);
            candidates.SetLayout(Layout);
            return candidates;
        }

        [Fact]
        public void Each_candidate_reads_the_same_hold_its_own_way()
        {
            // A press on the right-hand area, held 0.4 s and released where it began.
            Command Hold(ControlCandidate candidate)
            {
                TouchCandidates candidates = Candidates();
                candidates.Switch(candidate);
                Assert.Equal(candidate, candidates.Active);
                Assert.StartsWith(TouchCandidates.NameOf(candidate) + "/v", candidates.Describe());
                candidates.Feed(Down(1, 250, 200, 0));
                candidates.Feed(Up(1, 250, 200, 0.4));
                return candidates.CommandFor(0);
            }

            Assert.True(Neutral(Hold(ControlCandidate.ActionSurface)), "A: a hold without a flick is let go");
            Assert.True(Neutral(Hold(ControlCandidate.Split)), "B: a hold on KICK without a flick asks for nothing");
            Assert.True(Hold(ControlCandidate.OneFire).Kick.HasValue, "C: a hold is a kick along the heading");
        }

        [Fact]
        public void The_candidates_have_names_a_link_can_ask_for()
        {
            Assert.Equal("one-fire", TouchCandidates.NameOf(ControlCandidate.OneFire));
            foreach (ControlCandidate candidate in All) Assert.Equal(candidate, TouchCandidates.Named(TouchCandidates.NameOf(candidate)));
            Assert.Null(TouchCandidates.Named("fire"));
        }

        [Fact]
        public void A_finger_held_across_any_switch_makes_no_command()
        {
            foreach (ControlCandidate from in All)
            {
                foreach (ControlCandidate to in All.Where(c => c != from))
                {
                    TouchCandidates candidates = Candidates();
                    candidates.Switch(from);

                    // The stick and a right-hand finger held: then the switch.
                    candidates.Feed(Down(9, 100, 300, 0));
                    candidates.Feed(To(9, 100, 250, 0.01));
                    candidates.Feed(Down(1, 250, 200, 0.02));
                    candidates.Feed(To(1, 270, 180, 0.05));
                    candidates.Switch(to);

                    Command after = candidates.CommandFor(0);
                    Assert.True(Neutral(after) && after.Move.X == 0 && after.Move.Y == 0, $"{from} → {to}: the held fingers still steer or act");

                    // Reported pressed again, moved and lifted late under the new candidate: nothing.
                    candidates.Feed(Down(1, 270, 180, 0.06));
                    candidates.Feed(To(1, 300, 150, 0.07));
                    candidates.Feed(Up(1, 300, 150, 0.6));
                    candidates.Feed(Down(9, 100, 250, 0.61));
                    candidates.Feed(To(9, 100, 200, 0.62));
                    Command lifted = candidates.CommandFor(1);
                    Assert.True(Neutral(lifted) && lifted.Move.X == 0 && lifted.Move.Y == 0, $"{from} → {to}: a finger held across the switch made a command");

                    // And every other candidate, switched to next, finds nothing left over.
                    foreach (ControlCandidate next in All)
                    {
                        candidates.Switch(next);
                        Assert.True(Neutral(candidates.CommandFor(2)), $"{from} → {to} → {next}: something was left over");
                    }
                }
            }
        }

        [Fact]
        public void A_reused_pointer_lifted_under_another_candidate_is_forgotten_by_all_three()
        {
            // The desktop mouse is always pointer −1 (Codex on #79). Held on C, switched to A and on
            // to B, lifted under B: each candidate's next press with it is a fresh gesture.
            const int Mouse = -1;
            TouchCandidates candidates = Candidates();
            candidates.Switch(ControlCandidate.OneFire);
            candidates.Feed(Down(Mouse, 250, 200, 0));
            candidates.Switch(ControlCandidate.ActionSurface);
            candidates.Switch(ControlCandidate.Split);
            candidates.Feed(Up(Mouse, 250, 200, 0.05));

            candidates.Switch(ControlCandidate.OneFire);
            Assert.Equal(TouchRole.Fire, candidates.Feed(Down(Mouse, 250, 200, 0.1)));
            candidates.Feed(Up(Mouse, 250, 200, 0.15));
            Assert.True(candidates.CommandFor(0).QuickPass, "C dropped the fresh tap");

            candidates.Switch(ControlCandidate.ActionSurface);
            Assert.Equal(TouchRole.Action, candidates.Feed(Down(Mouse, 250, 200, 0.2)));
            candidates.Feed(Up(Mouse, 250, 200, 0.25));
            Assert.True(candidates.CommandFor(1).QuickPass, "A dropped the fresh tap");
        }

        [Fact]
        public void A_switch_drops_a_command_waiting_for_its_tick()
        {
            TouchCandidates candidates = Candidates();
            candidates.Switch(ControlCandidate.OneFire);
            candidates.Feed(Down(1, 250, 200, 0));
            candidates.Feed(Up(1, 250, 200, 0.4)); // C's kick, waiting for its tick
            candidates.Switch(ControlCandidate.Split);
            Assert.True(Neutral(candidates.CommandFor(0)));
            candidates.Switch(ControlCandidate.OneFire);
            Assert.True(Neutral(candidates.CommandFor(1)), "the dropped kick came back with C");
        }
    }
}
