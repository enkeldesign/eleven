using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>The geometric goal-line crossing: the ball wholly beyond x = goal line.</summary>
    public class GoalLineTests
    {
        private static List<SimEvent> RollFor(Simulator sim, int ticks)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++) sim.Step(new Command[0], events);
            return events.Where(e => e.Kind == SimEventKind.GoalLineCrossed).ToList();
        }

        [Fact]
        public void Crossing_emits_one_event_and_remaining_beyond_emits_none()
        {
            var config = Config();
            double start = config.GoalLineX + config.BallRadius - 1;
            var sim = Simulator(config, State(config, ballPosition: new Vec3(start, 0, config.BallRadius), ballVelocity: new Vec3(4, 0, 0)));

            var crossings = RollFor(sim, 60 * 10);

            Assert.Single(crossings);
            Assert.True(sim.PeekState().Ball.Position.X - config.BallRadius > config.GoalLineX + 1);
        }

        [Fact]
        public void A_ball_already_beyond_emits_nothing_once_its_state_says_so()
        {
            var config = Config();
            SimState state = State(config, ballPosition: new Vec3(config.GoalLineX + 5, 0, config.BallRadius));
            state.Ball.BeyondGoalLine = true;

            Assert.Empty(RollFor(Simulator(config, state), 120));
        }

        [Fact]
        public void A_ball_touching_the_line_has_not_crossed()
        {
            var config = Config();
            SimState state = State(config, ballPosition: new Vec3(config.GoalLineX + config.BallRadius, 0, config.BallRadius));

            Assert.Empty(RollFor(Simulator(config, state), 60));
        }

        [Fact]
        public void A_ball_that_came_back_behind_the_line_crosses_again()
        {
            var config = Config();

            // Beyond after the previous step, but back behind the line now and heading out again.
            SimState state = State(config, ballPosition: new Vec3(config.GoalLineX, 0, config.BallRadius), ballVelocity: new Vec3(3, 0, 0));
            state.Ball.BeyondGoalLine = true;

            Assert.Single(RollFor(Simulator(config, state), 300));
        }
    }
}
