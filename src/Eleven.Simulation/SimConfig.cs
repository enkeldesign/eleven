using System;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation
{
    /// <summary>
    /// Immutable, versioned simulation config (INV-5). Simulation behaviour is the code version
    /// plus <see cref="Hash"/>.
    ///
    /// The physics values are spike placeholders chosen to exercise the code, not tuned feel.
    /// Changing a field's meaning, or adding or reordering fields, bumps <see cref="FormatVersion"/>.
    /// </summary>
    public sealed class SimConfig
    {
        public const int FormatVersion = 1;

        public SimConfig(
            MathBackend mathBackend = MathBackend.Portable,
            int tickRate = 60,
            double gravity = 9.81,
            double ballRadius = 0.11,
            double bounceRestitution = 0.55,
            double bounceTangentialRetention = 0.8,
            double settleSpeed = 0.3,
            double rollingFriction = 0.08,
            double stopSpeed = 0.05,
            double magnusCoefficient = 0.006,
            double airSpinDecay = 0.5,
            double groundSpinDecay = 3.0,
            double dotMaxSpeed = 7.0,
            double dotAcceleration = 25.0,
            double kickReach = 0.9,
            double kickMaxBallHeight = 0.5,
            double kickSpeed = 18.0,
            double kickLoft = 0.45,
            double kickSideSpin = 12.0,
            double kickDirectionError = 0.05,
            int kickCooldownTicks = 30,
            double goalLineX = 52.5)
        {
            if (mathBackend != MathBackend.SystemMath && mathBackend != MathBackend.Portable)
                throw new ArgumentOutOfRangeException(nameof(mathBackend));
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (kickCooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(kickCooldownTicks));

            MathBackend = mathBackend;
            TickRate = tickRate;
            Gravity = gravity;
            BallRadius = ballRadius;
            BounceRestitution = bounceRestitution;
            BounceTangentialRetention = bounceTangentialRetention;
            SettleSpeed = settleSpeed;
            RollingFriction = rollingFriction;
            StopSpeed = stopSpeed;
            MagnusCoefficient = magnusCoefficient;
            AirSpinDecay = airSpinDecay;
            GroundSpinDecay = groundSpinDecay;
            DotMaxSpeed = dotMaxSpeed;
            DotAcceleration = dotAcceleration;
            KickReach = kickReach;
            KickMaxBallHeight = kickMaxBallHeight;
            KickSpeed = kickSpeed;
            KickLoft = kickLoft;
            KickSideSpin = kickSideSpin;
            KickDirectionError = kickDirectionError;
            KickCooldownTicks = kickCooldownTicks;
            GoalLineX = goalLineX;

            // Fails on non-finite values, so a broken config can't be constructed.
            Hash = Fnv1a64.Hash(Encode());
        }

        /// <summary>Which implementation sits behind the maths boundary.</summary>
        public MathBackend MathBackend { get; }

        /// <summary>Fixed steps per simulated second.</summary>
        public int TickRate { get; }

        /// <summary>Seconds per tick, 1 / <see cref="TickRate"/>.</summary>
        public double TickSeconds => 1.0 / TickRate;

        /// <summary>m/s², acting along −z.</summary>
        public double Gravity { get; }

        /// <summary>m. The ball rests with its centre at z = radius.</summary>
        public double BallRadius { get; }

        /// <summary>Fraction of downward speed returned upward at a bounce.</summary>
        public double BounceRestitution { get; }

        /// <summary>Fraction of horizontal speed kept at a bounce.</summary>
        public double BounceTangentialRetention { get; }

        /// <summary>m/s. A bounce returning less upward speed than this leaves the ball rolling.</summary>
        public double SettleSpeed { get; }

        /// <summary>Rolling deceleration as a fraction of gravity.</summary>
        public double RollingFriction { get; }

        /// <summary>m/s. A rolling ball slower than this stops.</summary>
        public double StopSpeed { get; }

        /// <summary>Magnus acceleration per unit of spin × velocity (spin-induced curve).</summary>
        public double MagnusCoefficient { get; }

        /// <summary>Fraction of spin lost per second in the air.</summary>
        public double AirSpinDecay { get; }

        /// <summary>Fraction of spin lost per second on the ground.</summary>
        public double GroundSpinDecay { get; }

        /// <summary>m/s at full stick.</summary>
        public double DotMaxSpeed { get; }

        /// <summary>m/s². Limit on how fast the dot's velocity changes.</summary>
        public double DotAcceleration { get; }

        /// <summary>m. Horizontal distance from dot to ball centre within which a kick connects.</summary>
        public double KickReach { get; }

        /// <summary>m. A ball centre higher than this can't be kicked.</summary>
        public double KickMaxBallHeight { get; }

        /// <summary>m/s. Launch speed of a kick.</summary>
        public double KickSpeed { get; }

        /// <summary>rad. Launch elevation of a kick.</summary>
        public double KickLoft { get; }

        /// <summary>rad/s. Largest sidespin a kick gives (about +z).</summary>
        public double KickSideSpin { get; }

        /// <summary>rad. Largest random deviation of a kick from the dot's heading.</summary>
        public double KickDirectionError { get; }

        /// <summary>Ticks after a kick before the same dot can kick again.</summary>
        public int KickCooldownTicks { get; }

        /// <summary>m. x of the goal line; the ball crosses it when wholly beyond, x − radius &gt; this.</summary>
        public double GoalLineX { get; }

        /// <summary>FNV-1a 64 of <see cref="Encode"/>. Stable across runs, processes and platforms.</summary>
        public ulong Hash { get; }

        /// <summary>The canonical encoding: format version, then every field in declaration order.</summary>
        public byte[] Encode()
        {
            var w = new CanonicalWriter();
            w.WriteInt32(FormatVersion);
            w.WriteInt32((int)MathBackend);
            w.WriteInt32(TickRate);
            w.WriteDouble(Gravity, nameof(Gravity));
            w.WriteDouble(BallRadius, nameof(BallRadius));
            w.WriteDouble(BounceRestitution, nameof(BounceRestitution));
            w.WriteDouble(BounceTangentialRetention, nameof(BounceTangentialRetention));
            w.WriteDouble(SettleSpeed, nameof(SettleSpeed));
            w.WriteDouble(RollingFriction, nameof(RollingFriction));
            w.WriteDouble(StopSpeed, nameof(StopSpeed));
            w.WriteDouble(MagnusCoefficient, nameof(MagnusCoefficient));
            w.WriteDouble(AirSpinDecay, nameof(AirSpinDecay));
            w.WriteDouble(GroundSpinDecay, nameof(GroundSpinDecay));
            w.WriteDouble(DotMaxSpeed, nameof(DotMaxSpeed));
            w.WriteDouble(DotAcceleration, nameof(DotAcceleration));
            w.WriteDouble(KickReach, nameof(KickReach));
            w.WriteDouble(KickMaxBallHeight, nameof(KickMaxBallHeight));
            w.WriteDouble(KickSpeed, nameof(KickSpeed));
            w.WriteDouble(KickLoft, nameof(KickLoft));
            w.WriteDouble(KickSideSpin, nameof(KickSideSpin));
            w.WriteDouble(KickDirectionError, nameof(KickDirectionError));
            w.WriteInt32(KickCooldownTicks);
            w.WriteDouble(GoalLineX, nameof(GoalLineX));
            return w.ToArray();
        }
    }
}
