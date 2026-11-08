using Eleven.Simulation.Maths;

namespace Eleven.Simulation
{
    /// <summary>
    /// Where the ball stands against the goal line (#68), kept in <see cref="BallState.Line"/>.
    /// The line's outer edge is <see cref="SimConfig.GoalLineX"/>. Numeric values are stable and
    /// encoded as one byte.
    /// </summary>
    public enum LineState : byte
    {
        /// <summary>The ball's centre is in front of the line.</summary>
        InPlay = 0,

        /// <summary>The whole ball is beyond the line, and its centre crossed it outside the mouth or over the bar.</summary>
        Out = 1,

        /// <summary>The whole ball is beyond the line, and its centre crossed it between the posts and under the crossbar.</summary>
        Goal = 2,

        /// <summary>The ball's centre crossed the line between the posts and under the crossbar; part of the ball is still on it.</summary>
        CrossingMouth = 3,

        /// <summary>The ball's centre crossed the line outside the mouth or over the bar; part of the ball is still on it.</summary>
        CrossingWide = 4,
    }

    /// <summary>
    /// The parts of the goal's frame (#68), as the attacker sees them, facing the goal from the
    /// pitch: the right post is at −y, the left at +y. The <see cref="SimEvent.Other"/> of a
    /// <see cref="SimEventKind.HitFrame"/>.
    /// </summary>
    public enum FramePart
    {
        None = 0,
        RightPost = 1,
        LeftPost = 2,
        Crossbar = 3,
    }

    /// <summary>
    /// The goal's frame (#68): two posts and a crossbar, round, as three capsules whose axes run
    /// from the posts' feet to their tops and between the tops, at the ball's radius plus half the
    /// post width. docs/simulation/goal.md has the rule.
    /// </summary>
    internal static class GoalFrame
    {
        // m. A ball centre this close to a capsule's surface is touching it: far above rounding,
        // far below anything visible.
        private const double Touch = 1e-9;

        /// <summary>
        /// Resolves the frame along the step's path, the straight segment the integration moved the
        /// ball's centre along from <paramref name="start"/>. A ball that starts touching the frame
        /// and moving into it is turned at once and moves the whole step along its new velocity;
        /// then the first contact along the path stops it there and turns it. A turn reverses the
        /// velocity's component along the contact normal, scaled by the restitution, and keeps the
        /// rest and the spin. So the path stays one straight segment, from <paramref name="start"/>
        /// to where the ball ends. Returns the parts hit, at most two (a corner), first first.
        /// </summary>
        public static void Resolve(Vec3 start, ref BallState ball, SimConfig config, out FramePart first, out FramePart second)
        {
            second = FramePart.None;
            TouchAtStart(start, ref ball, config, out first);
            double soonest = FirstContact(start, ball.Position - start, config, out FramePart hit, out Vec3 a, out Vec3 b);
            if (hit == FramePart.None) return;
            StopAndTurn(start + (ball.Position - start) * soonest, ref ball, a, b, config.FrameRestitution);
            if (first == FramePart.None) first = hit;
            else second = hit;
        }

        /// <summary>
        /// The first half of <see cref="Resolve"/>: a ball touching the frame at the step's start and
        /// moving into it is turned at once, and moves the whole step from <paramref name="start"/>
        /// along its new velocity. Returns the part it was touching, or none.
        /// </summary>
        public static void TouchAtStart(Vec3 start, ref BallState ball, SimConfig config, out FramePart part)
        {
            part = FramePart.None;
            double reach = Reach(config);
            if (!NearFrame(start, ball.Position, config, reach + Touch)) return;

            Vec3 path = ball.Position - start;
            for (int i = 0; i < 3; i++)
            {
                Axis(i, config, out Vec3 a, out Vec3 b, out FramePart touched);
                Vec3 off = start - Closest(start, a, b);
                if (off.LengthSquared > (reach + Touch) * (reach + Touch) || off.LengthSquared == 0 || Dot(off, path) >= 0) continue;

                Turn(ref ball, off * (1 / SimMath.Sqrt(off.LengthSquared)), config.FrameRestitution);
                Vec3 end = start + ball.Velocity * config.TickSeconds;
                ball.Position = end.Z < config.BallRadius ? new Vec3(end.X, end.Y, config.BallRadius) : end;
                part = touched;
                return;
            }
        }

        /// <summary>
        /// The second half: the fraction of the path from <paramref name="start"/> along
        /// <paramref name="path"/> at which the ball first meets the frame, with the part and its
        /// axis; infinity and none when it doesn't.
        /// </summary>
        public static double FirstContact(Vec3 start, Vec3 path, SimConfig config, out FramePart part, out Vec3 axisA, out Vec3 axisB)
        {
            double soonest = double.PositiveInfinity;
            part = FramePart.None;
            axisA = axisB = Vec3.Zero;
            double reach = Reach(config);
            if (!NearFrame(start, start + path, config, reach + Touch)) return soonest;

            for (int i = 0; i < 3; i++)
            {
                Axis(i, config, out Vec3 a, out Vec3 b, out FramePart hit);
                double s = Sweep(start, path, a, b, reach);
                if (s < soonest)
                {
                    soonest = s;
                    part = hit;
                    axisA = a;
                    axisB = b;
                }
            }

            return soonest;
        }

        /// <summary>Stops the ball at <paramref name="at"/>, on the capsule round the axis from <paramref name="a"/> to <paramref name="b"/>, and turns it.</summary>
        public static void StopAndTurn(Vec3 at, ref BallState ball, Vec3 a, Vec3 b, double restitution)
        {
            Vec3 normal = at - Closest(at, a, b);
            ball.Position = at;
            if (normal.LengthSquared > 0) Turn(ref ball, normal * (1 / SimMath.Sqrt(normal.LengthSquared)), restitution);
        }

        private static double Reach(SimConfig config) => config.BallRadius + config.PostWidth / 2;

        /// <summary>
        /// Whether the ball's centre at <paramref name="at"/> is inside the goal mouth, as the goal
        /// line judges it where the centre crosses x = <see cref="SimConfig.GoalLineX"/>: between
        /// the posts' axes and below the crossbar's.
        /// </summary>
        public static bool InMouth(Vec3 at, SimConfig config) =>
            (at.Y < 0 ? -at.Y : at.Y) < config.PostAxisY && at.Z < config.CrossbarAxisZ;

        // The posts' axes run from their feet to their tops, the crossbar's between the tops.
        private static void Axis(int i, SimConfig config, out Vec3 a, out Vec3 b, out FramePart part)
        {
            double x = config.FrameAxisX, y = config.PostAxisY, z = config.CrossbarAxisZ;
            switch (i)
            {
                case 0: a = new Vec3(x, -y, 0); b = new Vec3(x, -y, z); part = FramePart.RightPost; break;
                case 1: a = new Vec3(x, y, 0); b = new Vec3(x, y, z); part = FramePart.LeftPost; break;
                default: a = new Vec3(x, -y, z); b = new Vec3(x, y, z); part = FramePart.Crossbar; break;
            }
        }

        // Whether the step's segment's box meets the frame's box, grown by `margin`.
        private static bool NearFrame(Vec3 p, Vec3 q, SimConfig config, double margin)
        {
            double minX = p.X < q.X ? p.X : q.X, maxX = p.X < q.X ? q.X : p.X;
            double minY = p.Y < q.Y ? p.Y : q.Y, maxY = p.Y < q.Y ? q.Y : p.Y;
            double minZ = p.Z < q.Z ? p.Z : q.Z;
            return maxX >= config.FrameAxisX - margin && minX <= config.FrameAxisX + margin
                && maxY >= -config.PostAxisY - margin && minY <= config.PostAxisY + margin
                && minZ <= config.CrossbarAxisZ + margin;
        }

        // The velocity's component into the frame reverses, scaled by the restitution.
        private static void Turn(ref BallState ball, Vec3 normal, double restitution)
        {
            double into = Dot(ball.Velocity, normal);
            if (into < 0) ball.Velocity -= normal * ((1 + restitution) * into);
        }

        /// <summary>
        /// The fraction of the segment from <paramref name="p"/> along <paramref name="d"/> at which
        /// a centre first comes within <paramref name="reach"/> of the axis from <paramref name="a"/>
        /// to <paramref name="b"/>, moving in; infinity when it doesn't within the segment. A centre
        /// already touching is never a later contact: the start of the step handled it.
        /// </summary>
        internal static double Sweep(Vec3 p, Vec3 d, Vec3 a, Vec3 b, double reach)
        {
            Vec3 off = p - Closest(p, a, b);
            if (off.LengthSquared <= (reach + Touch) * (reach + Touch)) return Dot(off, d) < 0 ? 0 : double.PositiveInfinity;

            // The side of the capsule: the infinite cylinder, within the axis.
            double first = double.PositiveInfinity;
            Vec3 ba = b - a, oa = p - a;
            double baba = Dot(ba, ba), bard = Dot(ba, d), baoa = Dot(ba, oa);
            double k2 = baba * Dot(d, d) - bard * bard;
            double k1 = baba * Dot(oa, d) - baoa * bard;
            double k0 = baba * Dot(oa, oa) - baoa * baoa - reach * reach * baba;
            if (k2 > 0 && k1 < 0)
            {
                double h = k1 * k1 - k2 * k0;
                if (h >= 0)
                {
                    double s = (-k1 - SimMath.Sqrt(h)) / k2;
                    double along = baoa + s * bard;
                    if (s >= 0 && s <= 1 && along >= 0 && along <= baba) first = s;
                }
            }

            // Its round ends.
            double atA = SweepSphere(p, d, a, reach), atB = SweepSphere(p, d, b, reach);
            if (atA < first) first = atA;
            if (atB < first) first = atB;
            return first;
        }

        private static double SweepSphere(Vec3 p, Vec3 d, Vec3 centre, double reach)
        {
            Vec3 oc = p - centre;
            double half = Dot(oc, d), c = Dot(oc, oc) - reach * reach;
            if (c <= 0 || half >= 0) return double.PositiveInfinity;
            double a = Dot(d, d), h = half * half - a * c;
            if (h < 0) return double.PositiveInfinity;
            double s = (-half - SimMath.Sqrt(h)) / a;
            return s >= 0 && s <= 1 ? s : double.PositiveInfinity;
        }

        internal static Vec3 Closest(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a;
            double t = Dot(p - a, ab) / Dot(ab, ab);
            return a + ab * (t < 0 ? 0 : t > 1 ? 1 : t);
        }

        internal static double Dot(Vec3 u, Vec3 v) => u.X * v.X + u.Y * v.Y + u.Z * v.Z;
    }
}
