using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.AI
{
    /// <summary>
    /// A keeper's brain (#69): pure and stateless, it decides on his <see cref="KeeperObservation"/>
    /// alone (INV-9), and predicts the ball with the simulation's own <see cref="BallFlight"/>
    /// (INV-13). Code in this namespace can't name the simulation's state (ELEVEN003). It asks; the
    /// simulation's phases decide what he may do (INV-12). docs/simulation/keeper.md has the rule.
    /// </summary>
    public static class KeeperBrain
    {
        // Technical constants of the basic keeper, not feel values (docs/simulation/keeper.md).

        // m. How far off the line he stands, toward the ball.
        private const double StandOff = 1.5;

        // m. Within this of where he wants to be, he eases in rather than runs.
        private const double Arrive = 1.0;

        // s. How far ahead he reads the ball.
        private const double Horizon = 3;

        // m. A ball crossing his line further than this outside the posts or over the bar he lets go.
        private const double LetGo = 0.5;

        // The fraction of his running speed he shuffles across at, on his feet.
        private const double Shuffle = 0.4;

        // s. How early he may leave his dive.
        private const double Margin = 0.05;

        public static KeeperDecision Decide(in KeeperObservation seen, SimConfig config)
        {
            // In a match he distributes a caught ball (#134): short to an open teammate, else long.
            if (seen.Holding && seen.InMatch && seen.HeldTicks >= config.KeeperHoldSeconds * config.TickRate)
            {
                return new KeeperDecision(Vec2.Zero, kick: Distribute(seen, config));
            }

            if (seen.BallHeld || seen.Phase == KeeperPhase.Dive || seen.Phase == KeeperPhase.Recover) return new KeeperDecision(Vec2.Zero);

            // Where the ball will cross his line, by its own flight.
            if (Crossing(seen, config, out Vec3 at, out double seconds)
                && (at.Y < 0 ? -at.Y : at.Y) <= config.PostAxisY + LetGo && at.Z <= config.CrossbarAxisZ + LetGo)
            {
                double lateral = at.Y - seen.Position.Y;
                double away = lateral < 0 ? -lateral : lateral;
                double square = seen.Capabilities.Reach / 2;
                var toward = new Vec2(0, lateral);

                // Within his reach, he squares up to it; with time to get there, he shuffles across.
                if (away <= square) return new KeeperDecision(Ease(toward));
                if (away - square <= Shuffle * seen.RunSpeed * seconds) return new KeeperDecision(Unit(toward) * Shuffle);

                // Otherwise he dives for it, once its arrival is within the dive's time.
                double dive = away / seen.Capabilities.DiveSpeed;
                if (dive > config.KeeperDiveSeconds) dive = config.KeeperDiveSeconds;
                if (seconds <= dive + Margin) return new KeeperDecision(Vec2.Zero, new Vec3(seen.Position.X, at.Y, at.Z));
                return new KeeperDecision(Unit(toward) * Shuffle);
            }

            // On the line from the goal's centre toward the ball, StandOff out, inside the posts.
            var centre = new Vec2(config.GoalLineX, 0);
            Vec2 to = seen.BallPosition.Horizontal - centre;
            double length = SimMath.Sqrt(to.LengthSquared);
            Vec2 spot = length > 0 ? centre + to * (StandOff / length) : centre + new Vec2(-StandOff, 0);
            double half = config.GoalWidth / 2;
            if (spot.Y > half) spot = new Vec2(spot.X, half);
            if (spot.Y < -half) spot = new Vec2(spot.X, -half);
            if (spot.X > config.GoalLineX - StandOff / 2) spot = new Vec2(config.GoalLineX - StandOff / 2, spot.Y);
            return new KeeperDecision(Ease(spot - seen.Position));
        }

        // Where, and in how many seconds, the ball first reaches his x moving toward the goal.
        private static bool Crossing(in KeeperObservation seen, SimConfig config, out Vec3 at, out double seconds)
        {
            at = Vec3.Zero;
            seconds = 0;
            double x = seen.Position.X;
            if (seen.BallVelocity.X <= 0 || seen.BallPosition.X >= x) return false;

            var path = new List<Vec3>();
            BallFlight.Predict(config, seen.BallPosition, seen.BallVelocity, seen.BallSpin, (int)(Horizon * config.TickRate), path);
            Vec3 before = seen.BallPosition;
            for (int i = 0; i < path.Count; i++)
            {
                Vec3 now = path[i];
                if (now.X >= x)
                {
                    double f = now.X > before.X ? (x - before.X) / (now.X - before.X) : 1;
                    at = before + (now - before) * f;
                    seconds = (i + f) * config.TickSeconds;
                    return true;
                }

                before = now;
            }

            return false;
        }

        // Toward `to` at full speed beyond Arrive, easing in within it.
        private static Vec2 Ease(Vec2 to)
        {
            double length = SimMath.Sqrt(to.LengthSquared);
            return length > Arrive ? to * (1 / length) : to * (1 / Arrive);
        }

        private static Vec2 Unit(Vec2 to)
        {
            double length = SimMath.Sqrt(to.LengthSquared);
            return length > 0 ? to * (1 / length) : Vec2.Zero;
        }

        // m. A teammate nearer than this is under his feet, not a pass.
        private const double ShortestThrow = 3;

        // The long kick's strength and loft (#85's): up the pitch, in the air.
        private const double LongStrength = 0.6, LongLoft = 0.35;

        /// <summary>
        /// His distribution (#134), in his own frame (his goal at +x): the most open teammate within
        /// keeperThrowRange, at least keeperOpenDistance from every opponent and with no opponent
        /// within half of it of the line to him, given the ball on the ground at the quick pass's
        /// weight (INV-13); the most open first, the nearer on a tie. With none, a long kick toward
        /// the furthest-forward teammate; with no teammate at all, up the pitch toward his side.
        /// </summary>
        private static KickIntent Distribute(in KeeperObservation seen, SimConfig config)
        {
            Vec2 me = seen.Position;
            Vec2 bestTo = Vec2.Zero, forwardTo = Vec2.Zero;
            double bestClear = -1, bestDistance = 0, forwardX = double.PositiveInfinity;
            foreach (SeenPlayer mate in seen.Others)
            {
                if (!mate.Teammate || mate.Keeper) continue;
                Vec2 to = mate.Position - me;
                if (mate.Position.X < forwardX)
                {
                    forwardX = mate.Position.X;
                    forwardTo = to;
                }

                double distance = SimMath.Sqrt(to.LengthSquared);
                if (distance < ShortestThrow || distance > config.KeeperThrowRange) continue;
                double clear = double.PositiveInfinity;
                bool screened = false;
                foreach (SeenPlayer opponent in seen.Others)
                {
                    if (opponent.Teammate) continue;
                    double near = SimMath.Sqrt((opponent.Position - mate.Position).LengthSquared);
                    if (near < clear) clear = near;
                    if (FromLine(me, mate.Position, opponent.Position) < config.KeeperOpenDistance / 2) screened = true;
                }

                if (screened || clear < config.KeeperOpenDistance) continue;
                if (clear > bestClear || (clear == bestClear && distance < bestDistance))
                {
                    bestClear = clear;
                    bestDistance = distance;
                    bestTo = to;
                }
            }

            if (bestClear >= 0)
            {
                double speed = BallFlight.PassSpeed(config, bestDistance);
                return new KickIntent(bestTo, seen.KickSpeed > 0 ? (speed < seen.KickSpeed ? speed / seen.KickSpeed : 1) : 1, 0);
            }

            if (forwardX < double.PositiveInfinity) return new KickIntent(forwardTo, LongStrength, LongLoft);
            double side = seen.Position.Y >= 0 ? 1 : -1;
            return new KickIntent(new Vec2(-1, side * 0.45), LongStrength, LongLoft);
        }

        // m. How far `point` is from the segment from `from` to `to`.
        private static double FromLine(Vec2 from, Vec2 to, Vec2 point)
        {
            Vec2 line = to - from;
            double length = line.LengthSquared;
            double t = length > 0 ? ((point - from).X * line.X + (point - from).Y * line.Y) / length : 0;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            Vec2 closest = from + line * t;
            return SimMath.Sqrt((point - closest).LengthSquared);
        }
    }
}
