using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>Air drag (#24): a knob whose zero is the spike's no-drag flight.</summary>
    public class AirDragTests
    {
        [Fact]
        public void Drag_shortens_every_launch_and_never_lengthens_one()
        {
            SimConfig still = new SimConfig();
            SimConfig draggy = still.With("airDrag", 0.015);

            foreach (double speed in BallCurves.SpeedsMps)
            foreach (double loft in BallCurves.LoftsDegrees.Where(l => l > 0))
            {
                BallCurve without = BallCurves.Kick(still, "kick", speed, loft, 0);
                BallCurve with = BallCurves.Kick(draggy, "kick", speed, loft, 0);
                Assert.True(with.CarryMetres < without.CarryMetres, $"{speed} m/s at {loft}°: carry {with.CarryMetres} vs {without.CarryMetres}");
                Assert.True(with.PeakMetres <= without.PeakMetres, $"{speed} m/s at {loft}°: peak {with.PeakMetres} vs {without.PeakMetres}");
            }
        }

        [Fact]
        public void Even_extreme_drag_only_slows_the_ball()
        {
            SimConfig config = new SimConfig().With("airDrag", 1);
            BallCurve shot = BallCurves.Kick(config, "kick", 60, 20, 0);

            // Semi-implicit drag never reverses the ball: it goes forward a little and stops.
            Assert.True(shot.TotalMetres > 0);
            Assert.True(shot.TotalMetres < BallCurves.Kick(new SimConfig(), "kick", 60, 20, 0).TotalMetres);
            Assert.NotNull(shot.RestSeconds);
        }
    }
}
