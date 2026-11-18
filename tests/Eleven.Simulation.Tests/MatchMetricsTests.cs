using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Eleven.Simulation.Maths;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #128: the match metrics in the result contract (schema 7), read from the match's events and
    /// state by the recorder (INV-1), and the expected-goals reference model.
    /// </summary>
    public class MatchMetricsTests
    {
        [Fact]
        public void Exp_matches_the_platform_to_within_ten_units_in_the_last_place()
        {
            for (double x = -60; x <= 60; x += 0.37)
            {
                double want = Math.Exp(x), got = SimMath.Exp(x);
                Assert.True(Math.Abs(got - want) <= 2e-15 * want, $"exp({x}) = {got}, want {want}");
            }

            Assert.Equal(1.0, SimMath.Exp(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SimMath.Exp(double.NaN));
        }

        [Fact]
        public void The_xg_model_is_a_logistic_on_distance_and_angle()
        {
            MetricsModel m = MetricsModel.Reference;
            double z = m.Intercept + m.PerMetre * 11 + m.PerRadian * 0.64;
            Assert.Equal(1 / (1 + Math.Exp(-z)), m.Chance(11, 0.64), 12);
            Assert.True(m.Chance(6, 1.0) > m.Chance(16, 1.0), "nearer is likelier");
            Assert.True(m.Chance(11, 0.9) > m.Chance(11, 0.3), "more of the goal is likelier");
            Assert.InRange(m.Chance(16.5, 0.43), 0.03, 0.12); // the edge of the box, centrally
        }

        private static (MatchResult Result, SimState Final, List<SimEvent> Events) Play(int perSide, ulong seed)
        {
            SimConfig c = SpikeScenario.Config().With("halfLength", 90);
            var sim = new Simulator(c, Squads.Match(c, seed, perSide, bothCpu: true));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var step = new List<SimEvent>();
            var all = new List<SimEvent>();
            while (recorder.Result == null && sim.Tick < 6 * c.HalfTicks)
            {
                commands.Clear();
                step.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, step);
                recorder.Observe(c, sim.PeekState(), step);
                all.AddRange(step);
            }

            Assert.NotNull(recorder.Result);
            return (recorder.Result!, sim.PeekState(), all);
        }

        [Theory]
        [InlineData(5, 3UL)]
        [InlineData(10, 11UL)]
        public void A_match_carries_metrics_that_agree_with_its_events(int perSide, ulong seed)
        {
            (MatchResult r, SimState s, List<SimEvent> events) = Play(perSide, seed);
            SideMetrics home = r.HomeMetrics, away = r.AwayMetrics;

            Assert.InRange(home.Possession + away.Possession, 0.999, 1.001);
            Assert.True(home.Possession > 0 && away.Possession > 0);

            int TeamOf(int id) => s.Players.First(p => p.Id == id).TeamId;
            foreach ((SideMetrics m, int team, int goals) in new[] { (home, r.HomeTeam, r.HomeGoals), (away, r.AwayTeam, r.AwayGoals) })
            {
                Assert.InRange(m.OnTarget, 0, m.Shots);
                Assert.InRange(m.PassesCompleted, 0, m.Passes);
                Assert.True(m.Passes > 0, "passes were played");
                int scored = r.Goals.Count(g => g.Team == team && g.Scorer != 0 && TeamOf(g.Scorer) == team);
                Assert.True(m.OnTarget >= scored, $"{scored} goals of its own but {m.OnTarget} on target");
                Assert.Equal(events.Count(e => e.Kind == SimEventKind.RestartAwarded && e.Other == (int)RestartKind.Corner && e.Subject == team), m.Corners);
                Assert.InRange(m.Xg, 0, m.Shots);
                if (m.Shots > 0) Assert.True(m.Xg > 0);
                Assert.True(m.Ppda >= 0);
            }

            Assert.True(home.Shots + away.Shots > 0, "the match had shots");
            string text = r.ToText();
            Assert.StartsWith("eleven-result/7\n", text);
            Assert.Contains($"shots={home.Shots}-{away.Shots}\n", text);
        }

        // The recorder fed one step's events by hand (#128, Codex's findings on #147), the ball at `ball`.
        private static readonly SimConfig Hand = SpikeScenario.Config();

        private static void Feed(MatchRecorder r, SimState s, Vec2 ball, params SimEvent[] events)
        {
            s.Ball.Position = new Vec3(ball.X, ball.Y, Hand.BallRadius);
            r.Observe(Hand, s, events);
        }

        private static SimState HandState()
        {
            SimState s = Squads.Match(Hand, 3, 5);
            s.Match.Phase = MatchPhase.FirstHalf;
            return s;
        }

        private const int Striker = 1; // home's first outfielder: home attacks +x in the first half

        // Full time, and home's metrics in the result.
        private static SideMetrics Home(MatchRecorder r, SimState s)
        {
            r.Observe(Hand, s, new[] { new SimEvent(9, SimEventKind.FullTime, 0) });
            Assert.NotNull(r.Result);
            return r.Result!.HomeMetrics;
        }

        [Fact]
        public void A_goal_from_a_kick_is_one_shot_on_target_though_it_crosses_the_line_and_scores_in_one_step()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, new Vec2(Hand.GoalLineX - 11, 0), new SimEvent(1, SimEventKind.Kicked, Striker));
            Feed(r, s, new Vec2(Hand.GoalLineX + 0.5, 0), new SimEvent(2, SimEventKind.GoalLineCrossed, OneVOne.BallId), new SimEvent(2, SimEventKind.Goal, OneVOne.BallId, other: Squads.HomeTeam));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 1), (home.Shots, home.OnTarget));
            Assert.Equal(MetricsModel.Reference.Chance(new Vec2(Hand.GoalLineX - 11, 0), Hand.GoalLineX, Hand.GoalWidth), home.Xg, 12);
        }

        [Fact]
        public void A_kick_off_the_frame_and_in_is_one_shot_on_target()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, new Vec2(Hand.GoalLineX - 16, 3), new SimEvent(1, SimEventKind.Kicked, Striker));
            Feed(r, s, new Vec2(Hand.GoalLineX, Hand.GoalWidth / 2), new SimEvent(2, SimEventKind.HitFrame, OneVOne.BallId));
            Feed(r, s, new Vec2(Hand.GoalLineX + 0.5, 0), new SimEvent(3, SimEventKind.GoalLineCrossed, OneVOne.BallId), new SimEvent(3, SimEventKind.Goal, OneVOne.BallId, other: Squads.HomeTeam));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 1), (home.Shots, home.OnTarget));
        }

        [Fact]
        public void A_penalty_kick_takes_the_penalty_chance_and_the_rebound_after_it_does_not()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 spot = new Vec2(Hand.GoalLineX - 11, 0);

            // The penalty's kick is raised before the restart is taken, in the same step.
            Feed(r, s, spot, new SimEvent(1, SimEventKind.Kicked, Striker), new SimEvent(1, SimEventKind.RestartTaken, Striker, other: (int)RestartKind.Penalty));
            Feed(r, s, new Vec2(Hand.GoalLineX - 0.5, 0), new SimEvent(2, SimEventKind.KeeperParried, Squads.AwayKeeper(5)));

            Vec2 rebound = new Vec2(Hand.GoalLineX - 6, 4);
            Feed(r, s, rebound, new SimEvent(3, SimEventKind.Kicked, Striker));
            Feed(r, s, new Vec2(Hand.GoalLineX + 0.5, 0), new SimEvent(4, SimEventKind.GoalLineCrossed, OneVOne.BallId), new SimEvent(4, SimEventKind.Goal, OneVOne.BallId, other: Squads.HomeTeam));
            SideMetrics home = Home(r, s);
            Assert.Equal(MetricsModel.Reference.Penalty + MetricsModel.Reference.Chance(rebound, Hand.GoalLineX, Hand.GoalWidth), home.Xg, 12);
            Assert.Equal((2, 2), (home.Shots, home.OnTarget));
        }

        [Fact]
        public void The_same_match_gives_the_same_metrics()
        {
            Assert.Equal(Play(5, 3).Result.ToText(), Play(5, 3).Result.ToText());
        }
    }
}
