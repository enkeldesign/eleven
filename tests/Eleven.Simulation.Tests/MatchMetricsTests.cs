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

        [Fact]
        public void The_same_match_gives_the_same_metrics()
        {
            Assert.Equal(Play(5, 3).Result.ToText(), Play(5, 3).Result.ToText());
        }
    }
}
