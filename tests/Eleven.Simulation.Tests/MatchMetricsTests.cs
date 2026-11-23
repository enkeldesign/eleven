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
            Assert.StartsWith("eleven-result/8\n", text);
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
            s.Restart.Phase = RestartPhase.Live; // in play, the kick-off taken
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
        public void A_kick_off_the_frame_and_saved_is_one_shot_on_target()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, new Vec2(Hand.GoalLineX - 16, 3), new SimEvent(1, SimEventKind.Kicked, Striker));
            Feed(r, s, new Vec2(Hand.GoalLineX, Hand.GoalWidth / 2), new SimEvent(2, SimEventKind.HitFrame, OneVOne.BallId));
            Feed(r, s, new Vec2(Hand.GoalLineX - 1, 1), new SimEvent(3, SimEventKind.KeeperHeld, Squads.AwayKeeper(5)));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 1), (home.Shots, home.OnTarget));
        }

        [Fact]
        public void A_shot_play_stops_on_and_the_dead_ball_after_it_are_no_shot()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, new Vec2(Hand.GoalLineX - 14, 0), new SimEvent(1, SimEventKind.Kicked, Striker));

            // Offside: an indirect free kick to away; the ball rolls on, dead, into the frame and the net.
            s.Restart.Phase = RestartPhase.Stopped;
            Feed(r, s, new Vec2(Hand.GoalLineX - 12, 0), new SimEvent(2, SimEventKind.RestartAwarded, Squads.AwayTeam, other: (int)RestartKind.IndirectFreeKick));
            Feed(r, s, new Vec2(Hand.GoalLineX, Hand.GoalWidth / 2), new SimEvent(3, SimEventKind.HitFrame, OneVOne.BallId));
            Feed(r, s, new Vec2(Hand.GoalLineX + 0.5, 0), new SimEvent(4, SimEventKind.GoalLineCrossed, OneVOne.BallId));
            SideMetrics home = Home(r, s);
            Assert.Equal((0, 0.0), (home.Shots, home.Xg));
        }

        [Fact]
        public void A_kick_saved_in_the_step_it_is_ruled_offside_is_no_shot()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, new Vec2(Hand.GoalLineX - 8, 0), new SimEvent(1, SimEventKind.Kicked, Striker));
            s.Restart.Phase = RestartPhase.Stopped;
            Feed(r, s, new Vec2(Hand.GoalLineX - 1, 0), new SimEvent(2, SimEventKind.KeeperHeld, Squads.AwayKeeper(5)),
                new SimEvent(2, SimEventKind.Offside, Striker), new SimEvent(2, SimEventKind.RestartAwarded, Squads.AwayTeam, other: (int)RestartKind.IndirectFreeKick));
            SideMetrics home = Home(r, s);
            Assert.Equal((0, 0, 0.0), (home.Shots, home.OnTarget, home.Xg));
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
            Feed(r, s, rebound);
            Feed(r, s, rebound + new Vec2(0.4, 0), new SimEvent(3, SimEventKind.Kicked, Striker));
            Feed(r, s, new Vec2(Hand.GoalLineX + 0.5, 0), new SimEvent(4, SimEventKind.GoalLineCrossed, OneVOne.BallId), new SimEvent(4, SimEventKind.Goal, OneVOne.BallId, other: Squads.HomeTeam));
            SideMetrics home = Home(r, s);
            Assert.Equal(MetricsModel.Reference.Penalty + MetricsModel.Reference.Chance(rebound, Hand.GoalLineX, Hand.GoalWidth), home.Xg, 12);
            Assert.Equal((2, 2), (home.Shots, home.OnTarget));
        }

        private static string Of(SideMetrics m) => FormattableString.Invariant($"{m.Possession:R} {m.Shots} {m.OnTarget} {m.Xg:R} {m.Passes} {m.PassesCompleted} {m.Corners} {m.Ppda:R}");

        [Fact]
        public void The_metrics_so_far_grow_through_the_match_and_at_full_time_are_the_results()
        {
            SimConfig c = SpikeScenario.Config().With("halfLength", 90);
            var sim = new Simulator(c, Squads.Match(c, 3, 5, bothCpu: true));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var step = new List<SimEvent>();
            SideMetrics atHalf = default;
            while (recorder.Result == null && sim.Tick < 6 * c.HalfTicks)
            {
                commands.Clear();
                step.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                sim.Step(commands, step);
                recorder.Observe(c, sim.PeekState(), step);
                if (step.Any(e => e.Kind == SimEventKind.HalfTime)) atHalf = recorder.Now(0);
            }

            Assert.NotNull(recorder.Result);
            Assert.True(atHalf.Passes > 0 && atHalf.Possession > 0, "the first half's metrics, read at half-time");
            SideMetrics end = recorder.Now(0);
            Assert.InRange(atHalf.Passes, 0, end.Passes);
            Assert.InRange(atHalf.Shots, 0, end.Shots);
            Assert.Equal(Of(recorder.Result!.HomeMetrics), Of(end));
            Assert.Equal(Of(recorder.Result.AwayMetrics), Of(recorder.Now(1)));
        }

        [Fact]
        public void The_host_stores_three_named_plans_starting_on_the_reference()
        {
            TeamTactic[] plans = MatchPlans.Plans;
            Assert.Equal(TeamTactics.PerSide, plans.Length);
            Assert.Equal(new[] { "Balanced", "High press", "Low block" }, MatchPlans.Names);
            TeamTactic reference = TeamTactic.Reference(0);
            Assert.Equal((reference.Formation, reference.StepUp, reference.Line, reference.Width, reference.Press), (plans[0].Formation, plans[0].StepUp, plans[0].Line, plans[0].Width, plans[0].Press));
            Assert.True(plans[1].Line > 0 && plans[2].Line < 0, "the high press stands higher and the low block deeper");

            // A match with them starts on the first, and may switch to the others.
            SimConfig c = SpikeScenario.Config();
            SimState s = Squads.Match(c, 3, 5, homeTactics: plans, awayTactics: plans);
            Assert.Equal(2 * TeamTactics.PerSide, s.Tactics.Length);
            Assert.All(s.Teams, t => Assert.Equal(0, t.Tactic));
        }

        [Fact]
        public void A_shot_is_read_from_where_it_was_struck_not_where_the_ball_is_after_the_step()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 struck = new Vec2(Hand.GoalLineX - 20, 6);
            Feed(r, s, struck);
            Feed(r, s, struck + new Vec2(0.45, -0.1), new SimEvent(2, SimEventKind.Kicked, Striker));
            Feed(r, s, new Vec2(Hand.GoalLineX - 0.5, 0), new SimEvent(3, SimEventKind.KeeperHeld, Squads.AwayKeeper(5)));
            Assert.Equal(MetricsModel.Reference.Chance(struck, Hand.GoalLineX, Hand.GoalWidth), Home(r, s).Xg, 12);
        }

        [Fact]
        public void A_save_on_the_step_that_ends_the_half_is_counted()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, new Vec2(Hand.GoalLineX - 12, 0), new SimEvent(1, SimEventKind.Kicked, Striker));
            s.Match.Phase = MatchPhase.HalfTime;
            Feed(r, s, new Vec2(Hand.GoalLineX - 0.5, 0), new SimEvent(2, SimEventKind.KeeperParried, Squads.AwayKeeper(5)), new SimEvent(2, SimEventKind.HalfTime, 0));

            // And nothing in the break is.
            Feed(r, s, new Vec2(0, 0), new SimEvent(3, SimEventKind.Kicked, Striker));
            Feed(r, s, new Vec2(Hand.GoalLineX - 0.5, 0), new SimEvent(4, SimEventKind.KeeperParried, Squads.AwayKeeper(5)));
            Assert.Equal((1, 1), (Home(r, s).Shots, r.Result!.HomeMetrics.OnTarget));
        }

        [Fact]
        public void A_pass_met_by_a_teammates_first_time_kick_is_completed_and_one_met_by_an_opponents_is_not()
        {
            const int Mate = 2, Opponent = 7; // home's second outfielder; away's first at five a side
            Vec2 at = new Vec2(-10, 0);

            // The passer's own kick follows his pass's resolution; the teammate's first-time kick plays it.
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker, other: Mate));
            Feed(r, s, at, new SimEvent(2, SimEventKind.Kicked, Striker));
            Feed(r, s, at + new Vec2(10, 0), new SimEvent(3, SimEventKind.Kicked, Mate));
            Feed(r, s, at + new Vec2(30, 0), new SimEvent(4, SimEventKind.RestartAwarded, Squads.AwayTeam, other: (int)RestartKind.ThrowIn));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 1), (home.Passes, home.PassesCompleted));

            // Cleared first time by an opponent, then picked up by a teammate: not completed.
            r = new MatchRecorder();
            s = HandState();
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker, other: Mate));
            Feed(r, s, at, new SimEvent(2, SimEventKind.Kicked, Striker));
            Feed(r, s, at + new Vec2(10, 0), new SimEvent(3, SimEventKind.Kicked, Opponent));
            Feed(r, s, at + new Vec2(15, 0), new SimEvent(4, SimEventKind.Touched, Mate));
            home = Home(r, s);
            Assert.Equal((1, 0), (home.Passes, home.PassesCompleted));
        }

        [Fact]
        public void A_first_time_pass_back_completes_the_pass_it_meets()
        {
            const int Mate = 2;
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 at = new Vec2(-10, 0);
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker, other: Mate));
            Feed(r, s, at, new SimEvent(2, SimEventKind.Kicked, Striker));

            // The mate passes it back first time: his pass resolves as he winds up, before his kick.
            Feed(r, s, at + new Vec2(10, 0), new SimEvent(3, SimEventKind.QuickPassResolved, Mate, other: Striker));
            Feed(r, s, at + new Vec2(10, 0), new SimEvent(4, SimEventKind.Kicked, Mate));
            Feed(r, s, at, new SimEvent(5, SimEventKind.Touched, Striker));
            SideMetrics home = Home(r, s);
            Assert.Equal((2, 2), (home.Passes, home.PassesCompleted));
        }

        [Fact]
        public void A_first_time_pass_that_misses_the_ball_has_not_received_the_pass_it_met()
        {
            const int Mate = 2, Opponent = 7;
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 at = new Vec2(-10, 0);
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker, other: Mate));
            Feed(r, s, at, new SimEvent(2, SimEventKind.Kicked, Striker));

            // The mate winds up to pass first time, misses, and an opponent takes the ball.
            Feed(r, s, at + new Vec2(10, 0), new SimEvent(3, SimEventKind.QuickPassResolved, Mate, other: Striker));
            Feed(r, s, at + new Vec2(10, 0), new SimEvent(4, SimEventKind.KickMissed, Mate));
            Feed(r, s, at + new Vec2(11, 0), new SimEvent(5, SimEventKind.Touched, Opponent));
            SideMetrics home = Home(r, s);
            Assert.Equal((2, 0), (home.Passes, home.PassesCompleted));
        }

        [Fact]
        public void A_pass_whose_kick_misses_is_never_completed()
        {
            const int Mate = 2;
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 at = new Vec2(-10, 0);
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker, other: Mate));
            Feed(r, s, at, new SimEvent(2, SimEventKind.KickMissed, Striker));
            Feed(r, s, at + new Vec2(1, 0), new SimEvent(3, SimEventKind.Touched, Mate));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 0), (home.Passes, home.PassesCompleted));
        }

        [Fact]
        public void A_teammates_touch_before_the_pass_is_kicked_does_not_complete_it()
        {
            const int Mate = 2, Opponent = 7;
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 at = new Vec2(-10, 0);
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker, other: Mate));
            Feed(r, s, at, new SimEvent(2, SimEventKind.Touched, Mate));
            Feed(r, s, at, new SimEvent(3, SimEventKind.Kicked, Striker));
            Feed(r, s, at + new Vec2(10, 0), new SimEvent(4, SimEventKind.Touched, Opponent));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 0), (home.Passes, home.PassesCompleted));
        }

        [Fact]
        public void A_later_pass_whose_kick_misses_is_dropped_after_a_first_time_pass()
        {
            const int Mate = 2;
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 at = new Vec2(-10, 0);
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker, other: Mate));
            Feed(r, s, at, new SimEvent(2, SimEventKind.Kicked, Striker));
            Feed(r, s, at, new SimEvent(3, SimEventKind.QuickPassResolved, Mate, other: Striker));
            Feed(r, s, at, new SimEvent(4, SimEventKind.Kicked, Mate));
            Feed(r, s, at, new SimEvent(5, SimEventKind.Touched, Striker));

            // The mate's next pass misses its kick; his later kick is no sending of it.
            Feed(r, s, at, new SimEvent(6, SimEventKind.QuickPassResolved, Mate, other: Striker));
            Feed(r, s, at, new SimEvent(7, SimEventKind.KickMissed, Mate));
            Feed(r, s, at, new SimEvent(8, SimEventKind.Kicked, Mate));
            Feed(r, s, at, new SimEvent(9, SimEventKind.Touched, Striker));
            SideMetrics home = Home(r, s);
            Assert.Equal((3, 2), (home.Passes, home.PassesCompleted));
        }

        [Fact]
        public void A_passer_running_onto_his_own_pass_has_not_completed_it()
        {
            var r = new MatchRecorder();
            SimState s = HandState();
            Vec2 at = new Vec2(-10, 0);
            Feed(r, s, at, new SimEvent(1, SimEventKind.QuickPassResolved, Striker));
            Feed(r, s, at, new SimEvent(2, SimEventKind.Kicked, Striker));
            Feed(r, s, at + new Vec2(12, 0), new SimEvent(3, SimEventKind.Touched, Striker));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 0), (home.Passes, home.PassesCompleted));
        }

        [Fact]
        public void A_pass_a_teammate_takes_and_scores_from_is_a_shot_from_his_touch()
        {
            const int Mate = 2;
            var r = new MatchRecorder();
            SimState s = HandState();
            Feed(r, s, new Vec2(-10, 0), new SimEvent(1, SimEventKind.Kicked, Striker));
            Vec2 touch = new Vec2(Hand.GoalLineX - 8, 2);
            Feed(r, s, touch);
            Feed(r, s, touch, new SimEvent(3, SimEventKind.Touched, Mate));
            Feed(r, s, new Vec2(Hand.GoalLineX + 0.5, 0), new SimEvent(4, SimEventKind.GoalLineCrossed, OneVOne.BallId), new SimEvent(4, SimEventKind.Goal, OneVOne.BallId, other: Squads.HomeTeam));
            SideMetrics home = Home(r, s);
            Assert.Equal((1, 1), (home.Shots, home.OnTarget));
            Assert.Equal(MetricsModel.Reference.Chance(touch, Hand.GoalLineX, Hand.GoalWidth), home.Xg, 12);
        }

        [Fact]
        public void The_same_match_gives_the_same_metrics()
        {
            Assert.Equal(Play(5, 3).Result.ToText(), Play(5, 3).Result.ToText());
        }
    }
}
