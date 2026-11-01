using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #38: the quick pass. The owned command only asks for a pass; the footballer's perception picks
    /// a teammate from a documented candidate set, reads their run, and weights the pass, all inside
    /// the simulation and after the command was recorded.
    /// </summary>
    public class PassTests
    {
        private const int FirstReceiver = 2;

        private static PlayerCapabilities Profile(double vision = 1.05, double anticipation = 0.75, double error = 0)
        {
            PlayerCapabilities c = PlayerCapabilities.Reference;
            c.PassVision = vision;
            c.PassAnticipation = anticipation;
            c.KickError = error;
            return c;
        }

        // The passer (controller 7) at the origin facing +x, the ball at rest just ahead, and the
        // given receivers, standing or already running at their stick, each on their own controller.
        private static SimState Drill(SimConfig config, PlayerCapabilities passer, params (Vec2 Position, Vec2 Stick, int Team)[] receivers)
        {
            SimState state = State(config, ballPosition: new Vec3(0.5, 0, config.BallRadius));
            state.Players[0].Capabilities = passer;
            var players = new List<PlayerState> { state.Players[0] };
            for (int i = 0; i < receivers.Length; i++)
            {
                players.Add(new PlayerState
                {
                    Id = FirstReceiver + i,
                    OwnerControllerId = 20 + i,
                    TeamId = receivers[i].Team,
                    Capabilities = PlayerCapabilities.Reference,
                    Position = receivers[i].Position,
                    Velocity = receivers[i].Stick * PlayerCapabilities.Reference.MaxSpeed,
                });
            }

            state.Players = players.ToArray();
            return state;
        }

        private static Command[] Commands(long tick, SimState initial, bool pass, KickIntent? kick = null)
        {
            var commands = new List<Command> { new Command(Controller, tick, Vec2.Zero, kick, quickPass: pass) };
            for (int i = 1; i < initial.Players.Length; i++)
            {
                PlayerState r = initial.Players[i];
                Vec2 stick = r.Velocity * (1 / PlayerCapabilities.Reference.MaxSpeed);
                commands.Add(new Command(r.OwnerControllerId, tick, stick));
            }

            return commands.ToArray();
        }

        private static PlayerState Passer(Simulator sim) => sim.PeekState().Players[0];

        // Asks for the pass on tick 0 and returns the simulator after that step.
        private static Simulator Ask(SimConfig config, SimState initial, KickIntent? kick = null)
        {
            var sim = new Simulator(config, initial);
            sim.Step(Commands(0, initial, true, kick), new List<SimEvent>());
            return sim;
        }

        private static (double Closest, double Speed) Follow(SimConfig config, SimState initial, int receiverId, int ticks = 360)
        {
            var sim = new Simulator(config, initial);
            var events = new List<SimEvent>();
            double closest = double.PositiveInfinity, speed = 0;
            bool launched = false;
            for (int t = 0; t < ticks; t++)
            {
                sim.Step(Commands(t, initial, t == 0), events);
                launched |= events.Any(e => e.Kind == SimEventKind.Kicked);
                if (!launched) continue;
                SimState s = sim.PeekState();
                PlayerState receiver = s.Players.Single(p => p.Id == receiverId);
                double gap = Math.Sqrt((s.Ball.Position.Horizontal - receiver.Position).LengthSquared);
                if (gap < closest)
                {
                    closest = gap;
                    speed = Math.Sqrt(s.Ball.Velocity.LengthSquared);
                }
            }

            return (closest, speed);
        }

        // ---- Choice -------------------------------------------------------------------------------

        // #26: an obvious advancing receiver beats a closer backward one, all else equal. With
        // all-round vision both are candidates, so the choice is the footballer's, not the cone's.
        [Fact]
        public void An_obvious_receiver_ahead_beats_a_closer_one_behind()
        {
            var config = Config();
            SimState state = Drill(config, Profile(vision: Math.PI), (new Vec2(10, 0), Vec2.Zero, 0), (new Vec2(-6, 0), Vec2.Zero, 0));

            Assert.Equal(FirstReceiver, Passer(Ask(config, state)).WindUpTarget);

            // Only the preference for receivers ahead makes the difference: without it the nearer wins.
            Assert.Equal(FirstReceiver + 1, Passer(Ask(config.With("passBackWeight", 0), state)).WindUpTarget);
        }

        // The headless drill (#26), with just the two receivers the criterion compares; the wide one
        // is the measurements' business.
        [Fact]
        public void The_headless_drill_picks_the_advancing_receiver_over_the_closer_one_behind()
        {
            PracticeReceiver[] pair = PassDrill.Standard.Where(r => r.Name != "wide").ToArray();
            Assert.Equal("ahead-advancing", PassDrill.Run(Config(), Profile(vision: Math.PI), pair).Target);

            // A full reader also meets the runner. The reference reads three quarters of his run, so
            // it falls short by design: anticipation is the footballer's.
            PassOutcome full = PassDrill.Run(Config(), Profile(vision: Math.PI, anticipation: 1), pair);
            Assert.True(full.ClosestApproachMetres < 1, $"misses the runner by {full.ClosestApproachMetres} m");
            Assert.True(PassDrill.Run(Config(), Profile(vision: Math.PI), pair).ClosestApproachMetres > 3);
        }

        [Fact]
        public void Only_teammates_inside_the_passers_view_and_range_are_candidates()
        {
            var config = Config();
            var receivers = new[]
            {
                (new Vec2(2, 9), Vec2.Zero, 0),      // 77° off the heading: outside 60° vision
                (new Vec2(45, 0), Vec2.Zero, 0),     // beyond passMaxRange
                (new Vec2(5, 0.5), Vec2.Zero, 1),    // straight ahead, but the other team
                (new Vec2(10, 10), Vec2.Zero, 0),    // 45°, 14 m: the only candidate
            };
            Assert.Equal(FirstReceiver + 3, Passer(Ask(config, Drill(config, Profile(), receivers))).WindUpTarget);

            // Without that one there is no candidate: a free pass along the heading.
            PlayerState free = Passer(Ask(config, Drill(config, Profile(), receivers.Take(3).ToArray())));
            Assert.Equal(0, free.WindUpTarget);
            Assert.Equal(0, free.WindUpDirection);
            Assert.Equal(0, free.WindUpLoft);
            Assert.InRange(free.WindUpStrength, 0.01, 1);
        }

        [Fact]
        public void Perception_decides_which_teammates_are_noticed()
        {
            var config = Config();
            SimState Setup(double vision) => Drill(config, Profile(vision: vision), (new Vec2(3, 8), Vec2.Zero, 0), (new Vec2(25, 0), Vec2.Zero, 0));

            // The near teammate at about 69° is noticed only with the wider view, and then preferred.
            Assert.Equal(FirstReceiver, Passer(Ask(config, Setup(1.4))).WindUpTarget);
            Assert.Equal(FirstReceiver + 1, Passer(Ask(config, Setup(0.9))).WindUpTarget);
        }

        [Fact]
        public void A_manual_kick_in_the_same_command_takes_precedence()
        {
            var config = Config();
            SimState state = Drill(config, Profile(), (new Vec2(10, 0), Vec2.Zero, 0));
            PlayerState p = Passer(Ask(config, state, new KickIntent(new Vec2(0, 1), 0.7, 0.2)));

            Assert.Equal(0, p.WindUpTarget);
            Assert.Equal(Math.PI / 2, p.WindUpDirection, 15);
            Assert.Equal(0.7, p.WindUpStrength);
            Assert.Equal(0.2, p.WindUpLoft);
        }

        // ---- Lead and weight ------------------------------------------------------------------------

        [Fact]
        public void A_perfect_reader_leads_a_moving_receiver_and_a_literal_one_passes_behind_them()
        {
            var config = Config();
            var run = (new Vec2(14, 8), new Vec2(0, -5.0 / 7), 0);
            (double led, _) = Follow(config, Drill(config, Profile(anticipation: 1), run), FirstReceiver);
            (double literal, _) = Follow(config, Drill(config, Profile(anticipation: 0), run), FirstReceiver);

            Assert.True(led < 0.5, $"led pass misses by {led} m");
            Assert.True(literal > 3, $"unled pass misses by {literal} m");
        }

        [Fact]
        public void Anticipation_changes_the_lead_deterministically()
        {
            var config = Config();
            var run = (new Vec2(14, 8), new Vec2(0, -5.0 / 7), 0);
            double Line(double anticipation) => Passer(Ask(config, Drill(config, Profile(anticipation: anticipation), run))).WindUpDirection;

            Assert.Equal(Line(0.5), Line(0.5));
            Assert.True(Line(0) > Line(0.5) && Line(0.5) > Line(1), $"{Line(0)} {Line(0.5)} {Line(1)}");
        }

        [Fact]
        public void A_pass_to_a_standing_teammate_arrives_at_the_configured_speed()
        {
            var config = Config();
            (double closest, double speed) = Follow(config, Drill(config, Profile(), (new Vec2(15.5, 0), Vec2.Zero, 0)), FirstReceiver);

            Assert.True(closest < 0.1, $"misses by {closest} m");
            Assert.InRange(speed, config.PassArrivalSpeed - 0.2, config.PassArrivalSpeed + 0.2);
        }

        [Fact]
        public void The_passers_execution_error_shapes_an_assisted_pass_too()
        {
            var config = Config();
            double Launch(double error)
            {
                SimState state = Drill(config, Profile(error: error), (new Vec2(15, 0), Vec2.Zero, 0));
                var sim = new Simulator(config, state);
                var events = new List<SimEvent>();
                for (int t = 0; t <= config.KickWindUpTicks; t++) sim.Step(Commands(t, state, t == 0), events);
                Assert.Contains(events, e => e.Kind == SimEventKind.Kicked);
                BallState ball = sim.PeekState().Ball;
                return Math.Atan2(ball.Velocity.Y, ball.Velocity.X);
            }

            Assert.Equal(0, Launch(0), 12);
            Assert.NotEqual(0, Launch(0.2));
            Assert.InRange(Launch(0.2), -0.2, 0.2);
        }

        // ---- Replay, snapshots, validation ----------------------------------------------------------

        [Fact]
        public void Commands_recorded_before_assistance_replay_the_pass_tick_for_tick()
        {
            var config = Config();
            SimState state = PassDrill.InitialState(config, PlayerCapabilities.Reference, PassDrill.Standard);
            var live = new Simulator(config, state);
            CommandRecording recording = CommandRecording.StartFrom(live);
            var liveHashes = new List<ulong> { live.StateHash() };
            var liveEvents = new List<SimEvent>();
            var commands = new List<Command>();
            for (int t = 0; t < 240; t++)
            {
                commands.Clear();
                PassDrill.CommandsFor(live.Tick, PassDrill.Standard, commands);
                recording.Record(commands);
                live.Step(commands, liveEvents);
                liveHashes.Add(live.StateHash());
            }

            Assert.Contains(liveEvents, e => e.Kind == SimEventKind.Kicked);
            var replayHashes = new List<ulong>();
            var replayEvents = new List<SimEvent>();
            CommandRecording.Decode(recording.Encode()).Replay(config, replayEvents, replayHashes);
            Assert.Equal(liveHashes, replayHashes);
            Assert.Equal(liveEvents, replayEvents);
        }

        [Fact]
        public void Snapshot_and_restore_during_a_quick_pass_wind_up_continue_identically()
        {
            var config = Config();
            SimState state = PassDrill.InitialState(config, PlayerCapabilities.Reference, PassDrill.Standard);
            List<ulong> Run(Simulator sim, int end)
            {
                var hashes = new List<ulong>();
                var commands = new List<Command>();
                while (sim.Tick < end)
                {
                    commands.Clear();
                    PassDrill.CommandsFor(sim.Tick, PassDrill.Standard, commands);
                    sim.Step(commands, new List<SimEvent>());
                    hashes.Add(sim.StateHash());
                }

                return hashes;
            }

            List<ulong> uninterrupted = Run(new Simulator(config, state), 120);
            var original = new Simulator(config, state);
            Run(original, 3);
            Assert.NotEqual(0, original.PeekState().Players[0].WindUpTarget);
            Assert.Equal(uninterrupted.Skip(3), Run(Snapshot.Restore(Snapshot.Capture(original), config), 120));
        }

        [Fact]
        public void The_simulator_refuses_a_pass_target_that_is_not_a_winding_up_players_teammate()
        {
            var config = Config();
            void Refused(Action<PlayerState[]> change)
            {
                SimState state = Drill(config, Profile(), (new Vec2(10, 0), Vec2.Zero, 0), (new Vec2(12, 0), Vec2.Zero, 1));
                change(state.Players);
                Assert.ThrowsAny<ArgumentException>(() => new Simulator(config, state));
            }

            Refused(p => p[0].WindUpTarget = FirstReceiver);                                     // no wind-up
            Refused(p => { p[0].WindUpTicks = 1; p[0].WindUpTarget = FirstReceiver + 1; });     // the other team
            Refused(p => { p[0].WindUpTicks = 1; p[0].WindUpTarget = PlayerId; });              // themselves
            Refused(p => { p[0].WindUpTicks = 1; p[0].WindUpTarget = 99; });                    // nobody
            Refused(p => p[0].Id = 0);                                                           // ids are positive

            SimState valid = Drill(config, Profile(), (new Vec2(10, 0), Vec2.Zero, 0));
            valid.Players[0].WindUpTicks = 1;
            valid.Players[0].WindUpTarget = FirstReceiver;
            _ = new Simulator(config, valid);
        }
    }
}
