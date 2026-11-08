using System.Collections.Generic;

namespace Eleven.Simulation
{
    /// <summary>
    /// The ball on its own, by the simulation's own physics (INV-13): its integration and the goal's
    /// frame, exactly as a step moves a ball no player touches. The simulation steps the ball with it,
    /// and AI predicts with it, so a prediction is never a second approximation.
    /// </summary>
    public static class BallFlight
    {
        /// <summary>One step of a ball no player touches. Returns whether it bounced, and the parts of the frame it hit.</summary>
        internal static bool Advance(ref BallState ball, SimConfig config, out FramePart first, out FramePart second)
        {
            Vec3 start = ball.Position;
            bool bounced = Simulator.IntegrateBall(ref ball, config, config.TickSeconds);
            GoalFrame.Resolve(start, ref ball, config, out first, out second);
            return bounced;
        }

        /// <summary>
        /// The ball's path for <paramref name="steps"/> steps from what is seen of it now: its
        /// position after each step goes into <paramref name="positions"/>, and its velocity into
        /// <paramref name="velocities"/> when given. Nothing touches it on the way.
        /// </summary>
        public static void Predict(SimConfig config, Vec3 position, Vec3 velocity, Vec3 spin, int steps, List<Vec3> positions, List<Vec3>? velocities = null)
        {
            var ball = new BallState { Position = position, Velocity = velocity, Spin = spin };
            for (int i = 0; i < steps; i++)
            {
                Advance(ref ball, config, out _, out _);
                positions.Add(ball.Position);
                velocities?.Add(ball.Velocity);
            }
        }
    }
}
