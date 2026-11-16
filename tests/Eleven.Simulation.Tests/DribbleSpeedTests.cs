using System;
using Xunit;
using static Eleven.Simulation.Tests.TestWorld;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #96: running with the ball costs speed. A footballer with the ball reaches a share of his top
    /// speed that grows with his ball control, from dribbleSpeedPoor to dribbleSpeedGood; without it
    /// he reaches all of it. Under FREE and ATTACHED alike.
    /// </summary>
    public class DribbleSpeedTests
    {
        // His mean speed over the last two of six seconds at a full stick along +x, the ball at his
        // feet or far away.
        private static double TopSpeed(ControlModel model, bool withBall, double ballControl = 0.7)
        {
            var config = new SimConfig(controlModel: model);
            SimState s = State(config, ballPosition: withBall ? new Vec3(0.4, 0, config.BallRadius) : new Vec3(0, 30, config.BallRadius));
            PlayerCapabilities caps = PlayerCapabilities.Reference;
            caps.BallControl = ballControl;
            s.Players[0].Capabilities = caps;
            Simulator sim = Simulator(config, s);
            double distance = 0;
            int ticks = 6 * config.TickRate, from = 4 * config.TickRate;
            for (int t = 0; t < ticks; t++)
            {
                double x = sim.PeekState().Players[0].Position.X;
                Step(sim, Move(t, 1, 0));
                if (t >= from) distance += sim.PeekState().Players[0].Position.X - x;
            }

            return distance / ((ticks - from) * config.TickSeconds);
        }

        private static double Expected(SimConfig c, double ballControl) =>
            PlayerCapabilities.Reference.MaxSpeed * (c.DribbleSpeedPoor + (c.DribbleSpeedGood - c.DribbleSpeedPoor) * ballControl);

        [Theory]
        [InlineData(ControlModel.Free)]
        [InlineData(ControlModel.Attached)]
        public void With_the_ball_he_runs_at_his_dribble_share_of_top_speed_and_without_it_at_all_of_it(ControlModel model)
        {
            var c = new SimConfig(controlModel: model);
            double without = TopSpeed(model, withBall: false);
            Assert.Equal(PlayerCapabilities.Reference.MaxSpeed, without, 2);
            double with = TopSpeed(model, withBall: true);
            Assert.InRange(with, Expected(c, 0.7) - 0.25, Expected(c, 0.7) + 0.05);
        }

        [Theory]
        [InlineData(ControlModel.Free)]
        [InlineData(ControlModel.Attached)]
        public void Better_ball_control_carries_the_ball_faster(ControlModel model)
        {
            double poor = TopSpeed(model, withBall: true, ballControl: 0.1);
            double good = TopSpeed(model, withBall: true, ballControl: 1.0);
            Assert.True(good > poor + 0.3, $"ball control 1: {good:F2} m/s, 0.1: {poor:F2} m/s");
        }
    }
}
