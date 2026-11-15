using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>The restart sequencer (#85): out of play, goals and restarts, decided from the state.</summary>
    public class RestartTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private const int Home = OneVOne.HomeTeam, Away = OneVOne.AwayTeam;

        private static double R => C.BallRadius;

        // The 1v1 world with the referee on and the ball live where given, last played by `last`.
        private static SimState Live(Vec3 at, Vec3 velocity, int last, int homeAttackSign = 1)
        {
            SimState s = OneVOne.Start(C, seed: 21, homeAttackSign);
            s.Players[0].Position = new Vec2(0, -20);
            s.Players[2].Position = new Vec2(0, 20);

            // The keepers stand aside, so the ball reaches the line.
            s.Players[1].Position = new Vec2(-30, 25);
            s.Players[3].Position = new Vec2(30, -25);
            s.Ball.Position = at;
            s.Ball.Velocity = velocity;
            s.Ball.LastPlayedBy = last;
            s.Restart = new RestartState { On = true, LastTouchedTeam = last == 0 ? 0 : (last <= 2 ? Home : Away) };
            return s;
        }

        private static List<SimEvent> Run(Simulator sim, int ticks, bool brains = false, List<SimState>? states = null, Func<long, IEnumerable<Command>>? others = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var commands = new List<Command>();
                if (others != null) commands.AddRange(others(sim.Tick));
                if (brains)
                {
                    KeeperControllers.Append(sim, commands);
                    CpuControllers.Append(sim, commands);
                    RestartControllers.Append(sim, commands);
                }

                sim.Step(commands, events);
                states?.Add(sim.PeekState().Clone());
            }

            return events;
        }

        // ---- Exactly one, and the right one -----------------------------------------------------------

        public static IEnumerable<object[]> Crossings()
        {
            // where, how fast, who played it last (player id), home's attack sign → the award.
            double x = C.GoalLineX - 3, y = C.TouchlineY - 2;
            yield return new object[] { "home scores at +x", x, 0.0, 12.0, 0.0, 1, 1, RestartKind.KickOff, Away };
            yield return new object[] { "away scores at -x", -x, 0.0, -12.0, 0.0, 3, 1, RestartKind.KickOff, Home };
            yield return new object[] { "away's own goal at -x", -x, 0.0, -12.0, 0.0, 1, 1, RestartKind.KickOff, Home };
            yield return new object[] { "home wide at +x: goal kick", x, 8.0, 12.0, 0.0, 1, 1, RestartKind.GoalKick, Away };
            yield return new object[] { "away wide at +x: corner", x, 8.0, 12.0, 0.0, 3, 1, RestartKind.Corner, Home };
            yield return new object[] { "away wide at -x: goal kick", -x, 8.0, -12.0, 0.0, 3, 1, RestartKind.GoalKick, Home };
            yield return new object[] { "home wide at -x: corner", -x, -8.0, -12.0, 0.0, 1, 1, RestartKind.Corner, Away };
            yield return new object[] { "home over the touchline", 10.0, y, 0.0, 12.0, 1, 1, RestartKind.ThrowIn, Away };
            yield return new object[] { "away over the far touchline", -10.0, -y, 0.0, -12.0, 3, 1, RestartKind.ThrowIn, Home };
            yield return new object[] { "ends switched: home scores at -x", -x, 0.0, -12.0, 0.0, 1, -1, RestartKind.KickOff, Away };
            yield return new object[] { "ends switched: home wide at -x: goal kick", -x, 8.0, -12.0, 0.0, 1, -1, RestartKind.GoalKick, Away };
            yield return new object[] { "a keeper's save over his line: corner", x, 8.0, 12.0, 0.0, 4, 1, RestartKind.Corner, Home };
        }

        [Theory]
        [MemberData(nameof(Crossings))]
        public void Every_crossing_awards_exactly_one_restart_to_the_right_side(string name, double x, double y, double vx, double vy, int last, int homeAttackSign, RestartKind kind, int team)
        {
            var sim = new Simulator(C, Live(new Vec3(x, y, R), new Vec3(vx, vy, 0), last, homeAttackSign));
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 4 * 60, states: states);

            // One award, however long the ball stays beyond the line, and one set-up.
            SimEvent award = Assert.Single(events, e => e.Kind == SimEventKind.RestartAwarded);
            Assert.True((kind, team) == ((RestartKind)award.Other, award.Subject), $"{name}: awarded {(RestartKind)award.Other} to {award.Subject}");
            Assert.Single(events, e => e.Kind == SimEventKind.RestartPlaced);
            Assert.Equal(kind == RestartKind.KickOff, events.Any(e => e.Kind == SimEventKind.Goal));

            // The ball is dead for the stop time, then set up and waits for its taker.
            SimState placed = states[(int)award.Tick + C.RestartStopTicks];
            Assert.Equal(RestartPhase.Ready, placed.Restart.Phase);
            Assert.Equal(RestartPhase.Stopped, states[(int)award.Tick + C.RestartStopTicks - 1].Restart.Phase);
            Assert.Equal(placed.Restart.Point, placed.Ball.Position.Horizontal);
            Assert.Equal(Vec3.Zero, placed.Ball.Velocity);
            PlayerState taker = placed.Players.Single(p => p.Id == placed.Restart.TakerId);
            Assert.Equal(team, taker.TeamId);
        }

        [Fact]
        public void Each_restart_places_the_ball_and_the_footballers_where_the_laws_put_them()
        {
            foreach (object[] c in Crossings())
            {
                var sim = new Simulator(C, Live(new Vec3((double)c[1], (double)c[2], R), new Vec3((double)c[3], (double)c[4], 0), (int)c[5], (int)c[6]));
                var states = new List<SimState>();
                Run(sim, 4 * 60, states: states);
                Assert.Contains(states, st => st.Restart.Phase == RestartPhase.Ready);
                SimState s = states.First(st => st.Restart.Phase == RestartPhase.Ready);
                RestartState r = s.Restart;
                Vec2 ball = s.Ball.Position.Horizontal;
                double end = Math.Sign(ball.X);
                switch (r.Kind)
                {
                    case RestartKind.KickOff: Assert.Equal(Vec2.Zero, ball); break;
                    case RestartKind.ThrowIn: Assert.Equal(C.TouchlineY, Math.Abs(ball.Y), 9); break;
                    case RestartKind.GoalKick:
                        Assert.Equal(C.GoalLineX - Simulator.GoalArea, Math.Abs(ball.X), 9);
                        Assert.True(Math.Abs(ball.Y) <= C.GoalWidth / 2 + Simulator.GoalArea + 1e-9);
                        break;
                    case RestartKind.Corner:
                        Assert.InRange(Math.Abs(ball.X), C.GoalLineX - 1, C.GoalLineX);
                        Assert.InRange(Math.Abs(ball.Y), C.TouchlineY - 1, C.TouchlineY);
                        break;
                }

                // The taker just behind it; every opponent at the law's distance or further; no one moving.
                PlayerState taker = s.Players.Single(p => p.Id == r.TakerId);
                Assert.Equal(Simulator.TakerBehind, Math.Sqrt((taker.Position - ball).LengthSquared), 9);
                double distance = r.Kind == RestartKind.ThrowIn ? Simulator.ThrowInDistance : Simulator.RestartDistance;
                Assert.All(s.Players.Where(p => p.TeamId != r.AwardedTeam), p => Assert.True(Math.Sqrt((p.Position - ball).LengthSquared) >= distance - 1e-9, $"{c[0]}: player {p.Id} {Math.Sqrt((p.Position - ball).LengthSquared)} m off"));
                Assert.All(s.Players, p => Assert.Equal(Vec2.Zero, p.Velocity));

                // At a kick-off everyone is in his own half, the keepers on their lines.
                if (r.Kind == RestartKind.KickOff)
                {
                    foreach (PlayerState p in s.Players)
                    {
                        int own = s.Teams.Single(t => t.Id == p.TeamId).AttackSign;
                        Assert.True(p.Position.X * own <= 1e-9, $"{c[0]}: player {p.Id} at {p.Position.X} is in the other half");
                    }
                }
            }
        }

        // ---- The ball is dead until the taker kicks it ------------------------------------------------

        [Fact]
        public void A_dead_ball_is_no_ones_and_only_the_taker_moves_once_it_is_set_up()
        {
            var sim = new Simulator(C, Live(new Vec3(10, C.TouchlineY - 1, R), new Vec3(0, 10, 0), 1));
            var states = new List<SimState>();
            // Everyone runs at the ball and kicks at it all the time.
            List<SimEvent> events = Run(sim, 3 * 60, states: states, others: t => new[]
            {
                new Command(OneVOne.HomeController, t, new Vec2(0, 1), new KickIntent(new Vec2(1, 0), 1, 0)),
                new Command(OneVOne.AwayController, t, Vec2.Zero, t % 20 == 0 ? new KickIntent(new Vec2(1, 0), 1, 0) : (KickIntent?)null),
            });

            Assert.Contains(events, e => e.Kind == SimEventKind.RestartPlaced);
            SimEvent award = events.First(e => e.Kind == SimEventKind.RestartAwarded);
            SimEvent placed = events.First(e => e.Kind == SimEventKind.RestartPlaced);
            Assert.Equal(OneVOne.AwayPlayer, placed.Subject);

            // While stopped and until it is taken: no touch, no challenge, no save, and no kick by anyone but the taker.
            SimEvent taken = events.First(e => e.Kind == SimEventKind.RestartTaken);
            Assert.DoesNotContain(events, e => e.Tick > award.Tick && e.Tick < taken.Tick
                && (e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.ChallengeStarted || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.Kicked));
            Assert.Equal(OneVOne.AwayPlayer, taken.Subject);
            Assert.True(taken.Tick >= placed.Tick + C.RestartReadyTicks, $"taken at {taken.Tick}, set up at {placed.Tick}");

            // Held still: the non-takers don't move while it is set up.
            for (int i = (int)placed.Tick + 1; i < taken.Tick; i++)
            {
                Assert.Equal(states[i - 1].Players[0].Position, states[i].Players[0].Position);
                Assert.Equal(states[i - 1].Players[1].Position, states[i].Players[1].Position);
            }

            // Taken by a kick on the ordinary ball, which is then live.
            Assert.Contains(events, e => e.Kind == SimEventKind.Kicked && e.Subject == OneVOne.AwayPlayer && e.Tick == taken.Tick);
            Assert.Equal(RestartPhase.Live, states[(int)taken.Tick].Restart.Phase);
        }

        [Fact]
        public void A_throw_leaves_at_the_throws_speed_and_cannot_score()
        {
            // On a narrow pitch, away throws from near the +x corner straight into the +x goal, with
            // the keeper aside: his own goal, so if it counted it would be home's.
            SimConfig c = C.With("touchlineY", 15);
            SimState s = OneVOne.Start(c, seed: 21);
            s.Players[2].Position = new Vec2(c.GoalLineX - 4, c.TouchlineY + 0.6);
            s.Players[0].Position = new Vec2(0, -10);
            s.Players[1].Position = new Vec2(-30, 0);
            s.Players[3].Position = new Vec2(30, -10);
            s.Ball.Position = new Vec3(c.GoalLineX - 4, c.TouchlineY, R);
            s.Restart = new RestartState { On = true, Phase = RestartPhase.Ready, Kind = RestartKind.ThrowIn, AwardedTeam = Away, Point = s.Ball.Position.Horizontal, TakerId = OneVOne.AwayPlayer };
            var sim = new Simulator(c, s);
            var states = new List<SimState>();
            Vec2 aim = new Vec2(c.GoalLineX, 0) - s.Ball.Position.Horizontal;
            List<SimEvent> events = Run(sim, 3 * 60, states: states, others: t => new[] { new Command(OneVOne.AwayController, t, Vec2.Zero, t == 0 ? new KickIntent(aim, 1, 0.05) : (KickIntent?)null) });

            SimEvent taken = Assert.Single(events, e => e.Kind == SimEventKind.RestartTaken);
            Assert.Equal((int)RestartKind.ThrowIn, taken.Other);
            double speed = Math.Sqrt(states[(int)taken.Tick].Ball.Velocity.LengthSquared);
            Assert.InRange(speed, c.ThrowSpeed - 0.5, c.ThrowSpeed);
            Assert.Contains(events, e => e.Kind == SimEventKind.GoalLineCrossed);

            // Into a goal with no one else touching it: no goal; out of play over the goal line.
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Goal);
            SimEvent award = events.Last(e => e.Kind == SimEventKind.RestartAwarded);
            Assert.Contains((RestartKind)award.Other, new[] { RestartKind.Corner, RestartKind.GoalKick });
        }

        [Fact]
        public void A_kicked_restart_flies_as_any_kick_does()
        {
            // The same kick from the same spot, as a goal kick and in open play, moves the ball the same way.
            SimState s = OneVOne.Start(C, seed: 21);
            var point = new Vec2(-(C.GoalLineX - Simulator.GoalArea), 9);
            s.Players[0].Position = point - new Vec2(Simulator.TakerBehind, 0);
            s.Players[2].Position = new Vec2(10, 20);
            s.Ball.Position = new Vec3(point.X, point.Y, R);
            SimState open = s.Clone();
            s.Restart = new RestartState { On = true, Phase = RestartPhase.Ready, Kind = RestartKind.GoalKick, AwardedTeam = Home, Point = point, TakerId = OneVOne.HomePlayer };
            open.Restart = new RestartState { On = true };
            Func<long, IEnumerable<Command>> kick = t => new[] { new Command(OneVOne.HomeController, t, Vec2.Zero, t == 0 ? new KickIntent(new Vec2(1, 0.2), 0.8, 0.4) : (KickIntent?)null) };
            var a = new List<SimState>();
            var b = new List<SimState>();
            Run(new Simulator(C, s), 90, states: a, others: kick);
            Run(new Simulator(C, open), 90, states: b, others: kick);
            for (int i = 0; i < a.Count; i++) Assert.Equal(b[i].Ball.Position, a[i].Ball.Position);
        }

        // ---- No match stalls -----------------------------------------------------------------------------

        [Fact]
        public void A_cpu_taker_takes_at_once_and_a_humans_restart_is_taken_for_him_after_the_wait()
        {
            // Away (CPU) awarded: taken as soon as it may be.
            var cpu = new Simulator(C, Live(new Vec3(10, C.TouchlineY - 1, R), new Vec3(0, 10, 0), 1));
            List<SimEvent> e1 = Run(cpu, 4 * 60, brains: true);
            Assert.Contains(e1, e => e.Kind == SimEventKind.RestartTaken);
            SimEvent placed1 = e1.First(e => e.Kind == SimEventKind.RestartPlaced);
            // Kicked as soon as it may be: taken when the kick connects, a wind-up later.
            Assert.Equal(placed1.Tick + C.RestartReadyTicks + C.KickWindUpTicks + 1, e1.First(e => e.Kind == SimEventKind.RestartTaken).Tick);

            // Home (human) awarded, and the human sends nothing: taken for him after the wait.
            var human = new Simulator(C, Live(new Vec3(10, C.TouchlineY - 1, R), new Vec3(0, 10, 0), 3));
            List<SimEvent> e2 = Run(human, 5 * 60, brains: true);
            SimEvent placed2 = e2.First(e => e.Kind == SimEventKind.RestartPlaced);
            Assert.Equal(OneVOne.HomePlayer, placed2.Subject);
            SimEvent taken = e2.First(e => e.Kind == SimEventKind.RestartTaken);
            Assert.Equal(placed2.Tick + C.RestartReadyTicks + C.RestartAutoTicks + C.KickWindUpTicks + 1, taken.Tick);

            // A human who takes it himself takes it when he kicks.
            var own = new Simulator(C, Live(new Vec3(10, C.TouchlineY - 1, R), new Vec3(0, 10, 0), 3));
            List<SimEvent> e3 = Run(own, 5 * 60, brains: true, others: t => new[] { new Command(OneVOne.HomeController, t, Vec2.Zero, quickPass: true) });
            SimEvent placed3 = e3.First(e => e.Kind == SimEventKind.RestartPlaced);
            Assert.InRange(e3.First(e => e.Kind == SimEventKind.RestartTaken).Tick, placed3.Tick + C.RestartReadyTicks, placed3.Tick + C.RestartReadyTicks + C.KickWindUpTicks + 1);
        }

        [Fact]
        public void A_six_minute_match_with_restarts_runs_on_never_stalls_and_is_the_same_twice_from_a_snapshot_and_its_recording()
        {
            const int Ticks = 6 * 60 * 60;
            var first = new List<ulong>();
            var recorded = new List<Command[]>();
            var sim = new Simulator(C, OneVOne.KickOff(C, seed: 0x85, bothCpu: true));
            var events = new List<SimEvent>();
            byte[]? snapshot = null;
            for (int i = 0; i < Ticks; i++)
            {
                if (i == 1800) snapshot = Snapshot.Capture(sim);
                var commands = new List<Command>();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                recorded.Add(commands.ToArray());
                sim.Step(commands, events);
                first.Add(sim.StateHash());
            }

            // Six minutes of CPU-against-CPU football: restarts awarded and taken, each once, and never stuck dead for long.
            int awarded = events.Count(e => e.Kind == SimEventKind.RestartAwarded), taken = events.Count(e => e.Kind == SimEventKind.RestartTaken);
            Assert.True(taken >= 3, $"{awarded} awarded, {taken} taken");
            Assert.InRange(awarded, taken - 1, taken);
            long longestDead = 0, deadFrom = -1;
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.RestartAwarded) deadFrom = e.Tick;
                if (e.Kind == SimEventKind.RestartTaken && deadFrom >= 0) { longestDead = Math.Max(longestDead, e.Tick - deadFrom); deadFrom = -1; }
            }

            Assert.True(longestDead <= C.RestartStopTicks + C.RestartReadyTicks + C.RestartAutoTicks + 60, $"dead for {longestDead} steps");

            var replay = new List<ulong>();
            CommandRecording recording = CommandRecording.StartFrom(new Simulator(C, OneVOne.KickOff(C, seed: 0x85, bothCpu: true)));
            foreach (Command[] step in recorded) recording.Record(step);
            CommandRecording.Decode(recording.Encode()).Replay(C, tickHashes: replay);
            replay.RemoveAt(0);
            Assert.Equal(first, replay);

            var resumed = new List<ulong>();
            Simulator back = Snapshot.Restore(snapshot!, C);
            for (int i = 1800; i < Ticks; i++)
            {
                back.Step(recorded[i], new List<SimEvent>());
                resumed.Add(back.StateHash());
            }

            Assert.Equal(first.Skip(1800), resumed);
        }

        // ---- Plain data, checked; practice untouched ---------------------------------------------------------

        [Fact]
        public void The_restart_state_round_trips_mid_restart_and_a_bad_one_is_refused()
        {
            var sim = new Simulator(C, OneVOne.KickOff(C, seed: 3));
            Simulator back = Snapshot.Restore(Snapshot.Capture(sim), C);
            Assert.Equal(sim.StateHash(), back.StateHash());
            RestartState r = back.PeekState().Restart;
            Assert.Equal((true, RestartPhase.Ready, RestartKind.KickOff, Home, OneVOne.HomePlayer), (r.On, r.Phase, r.Kind, r.AwardedTeam, r.TakerId));

            SimState With(Action<SimState> change)
            {
                SimState s = OneVOne.KickOff(C, seed: 3);
                change(s);
                return s;
            }

            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Restart.TakerId = OneVOne.AwayPlayer)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Restart.AwardedTeam = 7)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Restart.Kind = RestartKind.None)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(C, With(s => s.Restart.PhaseTicks = 9999)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Teams = Array.Empty<TeamState>())));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Restart = new RestartState { Phase = RestartPhase.Stopped })));
        }

        [Fact]
        public void Without_a_referee_the_ball_is_never_out_of_play()
        {
            // The 1v1 world as #82 left it, and practice: a ball over a touchline or a goal line awards nothing.
            SimState s = Live(new Vec3(10, C.TouchlineY - 1, R), new Vec3(0, 10, 0), 1);
            s.Restart = default;
            List<SimEvent> events = Run(new Simulator(C, s), 3 * 60);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.RestartAwarded);
            Assert.DoesNotContain(Run(new Simulator(C, SpikeScenario.InitialState(C)), 600, others: t => new[] { new Command(SpikeScenario.ControllerId, t, new Vec2(1, 0)) }), e => e.Kind == SimEventKind.RestartAwarded);
        }
    }
}
