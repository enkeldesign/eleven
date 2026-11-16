using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #99: fouls, through the one restart sequencer (INV-8). A challenge that meets the man before
    /// the ball is a foul. In the offender's penalty area it is a penalty; elsewhere the fouled side
    /// keeps advantage while it has the ball, and the free kick is given if it loses it within the
    /// window; otherwise a direct free kick at the foul.
    /// </summary>
    public class FoulTests
    {
        // The challenge's own mechanics, shielding off (#97).
        private static readonly SimConfig C = SpikeScenario.Config().With("shieldSpeed", 0);

        private const int Home = OneVOne.HomeTeam, Away = OneVOne.AwayTeam;
        private const int A = OneVOne.HomePlayer, B = OneVOne.AwayPlayer;

        // Home attacks +x with the ball at A's feet, A at `at`; B, away, a step from the end of a
        // standing challenge along +x from just behind A, the ball beyond his reach.
        private static SimState Fouling(Vec2 at)
        {
            SimState s = OneVOne.Start(C, seed: 7, homeAttackSign: 1);
            s.Restart = new RestartState { On = true, LastTouchedTeam = Home };
            s.Players[0].Position = at;
            s.Ball.Position = new Vec3(at.X + 0.7, at.Y, C.BallRadius);
            s.Ball.LastPlayedBy = A;
            s.Players[2].Position = at + new Vec2(-0.3, 0);
            s.Players[2].Challenge = ChallengePhase.Commit;
            s.Players[2].ChallengeTicks = 1;
            s.Players[2].ChallengeLine = new Vec2(1, 0);
            return s;
        }

        private static List<SimEvent> Run(Simulator sim, int ticks, Func<long, Vec2>? a = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                long t = sim.Tick;
                sim.Step(new[] { new Command(OneVOne.HomeController, t, a?.Invoke(t) ?? Vec2.Zero), new Command(OneVOne.AwayController, t, Vec2.Zero) }, events);
            }

            return events;
        }

        [Fact]
        public void A_challenge_that_meets_the_man_and_misses_the_ball_is_a_foul_and_the_fouled_side_keeps_advantage()
        {
            var sim = new Simulator(C, Fouling(new Vec2(-10, 0)));
            List<SimEvent> e = Run(sim, 1);
            Assert.Contains(e, x => x.Kind == SimEventKind.Foul && x.Subject == B && x.Other == A);
            Assert.Contains(e, x => x.Kind == SimEventKind.AdvantagePlayed && x.Subject == Home);
            RestartState r = sim.PeekState().Restart;
            Assert.Equal((RestartPhase.Live, Home), (r.Phase, r.AdvantageTeam));
        }

        [Fact]
        public void Advantage_kept_through_its_window_gives_nothing()
        {
            var sim = new Simulator(C, Fouling(new Vec2(-10, 0)));
            List<SimEvent> e = Run(sim, (int)(C.AdvantageTime * C.TickRate) + 30, _ => new Vec2(0.4, 0));
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.RestartAwarded);
            Assert.Equal(0, sim.PeekState().Restart.AdvantageTeam);
        }

        [Fact]
        public void Advantage_lost_within_its_window_gives_the_free_kick_where_the_foul_was()
        {
            var sim = new Simulator(C, Fouling(new Vec2(-10, 0)));
            Run(sim, 1);
            Vec2 foul = sim.PeekState().Restart.AdvantagePoint;

            // The opponents have the ball now.
            SimState s = sim.PeekState().Clone();
            s.Players[2].Challenge = ChallengePhase.Ready;
            s.Players[2].ChallengeTicks = 0;
            s.Players[2].ChallengeLine = Vec2.Zero;
            s.Players[2].Position = s.Ball.Position.Horizontal + new Vec2(0.3, 0);
            s.Ball.LastPlayedBy = B;
            s.Ball.Velocity = Vec3.Zero;
            var lost = new Simulator(C, s);
            List<SimEvent> e = Run(lost, C.RestartStopTicks + 2);
            Assert.Contains(e, x => x.Kind == SimEventKind.RestartAwarded && x.Subject == Home && x.Other == (int)RestartKind.FreeKick);
            SimState placed = lost.PeekState();
            Assert.Equal(RestartKind.FreeKick, placed.Restart.Kind);
            Assert.Equal(foul, placed.Restart.Point);

            // The opponents the law's distance off.
            foreach (PlayerState p in placed.Players.Where(p => p.TeamId == Away))
            {
                Assert.True(Math.Sqrt((p.Position - foul).LengthSquared) >= Simulator.RestartDistance - 1e-9, $"{p.Id} too near");
            }
        }

        [Fact]
        public void The_match_result_counts_each_sides_fouls_and_penalties()
        {
            SimState after = OneVOne.Match(C, seed: 3);
            after.Match.Phase = MatchPhase.FullTime;
            var recorder = new MatchRecorder();
            recorder.Observe(C, after, new[]
            {
                new SimEvent(10, SimEventKind.Foul, B, other: A),
                new SimEvent(10, SimEventKind.RestartAwarded, Home, other: (int)RestartKind.Penalty),
                new SimEvent(20, SimEventKind.Foul, A, other: B),
                new SimEvent(30, SimEventKind.Foul, B, other: A),
                new SimEvent(40, SimEventKind.FullTime, 0),
            });
            MatchResult result = Assert.IsType<MatchResult>(recorder.Result);
            Assert.Equal((1, 2, 1, 0), (result.HomeFouls, result.AwayFouls, result.HomePenalties, result.AwayPenalties));
            Assert.Contains("fouls=1-2\npenalties=1-0\n", result.ToText());
        }

        [Fact]
        public void A_foul_in_the_offenders_penalty_area_is_a_penalty_from_the_mark()
        {
            var sim = new Simulator(C, Fouling(new Vec2(C.GoalLineX - 8, 2)));
            List<SimEvent> e = Run(sim, C.RestartStopTicks + 2);
            Assert.Contains(e, x => x.Kind == SimEventKind.Foul);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.AdvantagePlayed);
            Assert.Contains(e, x => x.Kind == SimEventKind.RestartAwarded && x.Subject == Home && x.Other == (int)RestartKind.Penalty);

            SimState s = sim.PeekState();
            var mark = new Vec2(C.GoalLineX - Simulator.PenaltyMark, 0);
            Assert.Equal(RestartKind.Penalty, s.Restart.Kind);
            Assert.Equal(mark, s.Ball.Position.Horizontal);
            PlayerState keeper = s.Players.Single(p => p.Id == OneVOne.AwayKeeper);
            Assert.True(keeper.Position.X >= C.GoalLineX - 0.5, $"keeper at {keeper.Position.X:F2}");
            foreach (PlayerState p in s.Players.Where(p => p.Id != s.Restart.TakerId && p.Id != OneVOne.AwayKeeper))
            {
                Assert.True(!Simulator.InPenaltyArea(p.Position, 1, C), $"{p.Id} in the area at {p.Position.X:F2},{p.Position.Y:F2}");
                Assert.True(Math.Sqrt((p.Position - mark).LengthSquared) >= Simulator.RestartDistance - 1e-9, $"{p.Id} too near the mark");
            }
        }
    }
}
