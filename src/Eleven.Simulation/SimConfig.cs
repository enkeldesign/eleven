using System;
using Eleven.Simulation.Encoding;

namespace Eleven.Simulation
{
    /// <summary>
    /// Immutable, versioned simulation config (INV-5). Simulation behaviour is the code version
    /// plus <see cref="Hash"/>.
    ///
    /// The physics values are spike placeholders chosen to exercise the code, not tuned feel.
    /// Changing a field's meaning, or adding or reordering fields, bumps <see cref="FormatVersion"/>.
    /// Version 2 dropped the maths backend: ADR-0002 leaves one maths model. Version 3 moved
    /// player movement out to each player's <see cref="PlayerCapabilities"/> (#36). Version 4 added
    /// the manual kick's wind-up and aftertouch, made <see cref="KickLoft"/> and <see cref="KickSideSpin"/>
    /// maxima, and moved the kick's direction error to the player (#37). Version 5 added
    /// <see cref="BounceSpinRetention"/> (#40).
    ///
    /// The ball- and kick-feel values tools may vary, and their valid ranges, are listed in
    /// <see cref="Tunables"/>; the constructor refuses values outside them.
    /// </summary>
    public sealed partial class SimConfig
    {
        public const int FormatVersion = 5;

        public SimConfig(
            int tickRate = 60,
            double gravity = 9.81,
            double ballRadius = 0.11,
            double bounceRestitution = 0.55,
            double bounceTangentialRetention = 0.8,
            double bounceSpinRetention = 0.25,
            double settleSpeed = 0.3,
            double rollingFriction = 0.08,
            double stopSpeed = 0.05,
            double magnusCoefficient = 0.006,
            double airSpinDecay = 0.5,
            double groundSpinDecay = 3.0,
            double kickReach = 0.9,
            double kickMaxBallHeight = 0.5,
            double kickSpeed = 18.0,
            double kickLoft = 1.05,
            double kickSideSpin = 12.0,
            double kickWindUpSeconds = 0.1,
            double aftertouchSeconds = 0.2,
            double aftertouchBend = 0.1,
            double aftertouchLift = 2.0,
            int kickCooldownTicks = 30,
            double goalLineX = 52.5)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (kickCooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(kickCooldownTicks));

            TickRate = tickRate;
            Gravity = gravity;
            BallRadius = ballRadius;
            BounceRestitution = bounceRestitution;
            BounceTangentialRetention = bounceTangentialRetention;
            BounceSpinRetention = bounceSpinRetention;
            SettleSpeed = settleSpeed;
            RollingFriction = rollingFriction;
            StopSpeed = stopSpeed;
            MagnusCoefficient = magnusCoefficient;
            AirSpinDecay = airSpinDecay;
            GroundSpinDecay = groundSpinDecay;
            KickReach = kickReach;
            KickMaxBallHeight = kickMaxBallHeight;
            KickSpeed = kickSpeed;
            KickLoft = kickLoft;
            KickSideSpin = kickSideSpin;
            KickWindUpSeconds = kickWindUpSeconds;
            AftertouchSeconds = aftertouchSeconds;
            AftertouchBend = aftertouchBend;
            AftertouchLift = aftertouchLift;
            KickCooldownTicks = kickCooldownTicks;
            GoalLineX = goalLineX;

            CheckTunableRanges();

            // Fails on non-finite values, so a broken config can't be constructed.
            Hash = Fnv1a64.Hash(Encode());

            // Whole steps, nearest. Math.Round is exact, so every runtime agrees.
            KickWindUpTicks = (int)Math.Round(KickWindUpSeconds * TickRate);
            AftertouchTicks = (int)Math.Round(AftertouchSeconds * TickRate);
        }

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

        /// <summary>
        /// Fraction of the ball's spin kept at each ground bounce (#40), so the curve of repeated hops
        /// dies within a few bounces. It shortens the life of physical spin; the aftertouch window,
        /// which gates the player's shaping input, is separate.
        /// </summary>
        public double BounceSpinRetention { get; }

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

        /// <summary>m. Horizontal distance from player to ball centre within which a kick connects.</summary>
        public double KickReach { get; }

        /// <summary>m. A ball centre higher than this can't be kicked.</summary>
        public double KickMaxBallHeight { get; }

        /// <summary>
        /// m/s. The hardest kick: launch speed at full strength by a player with full kick power
        /// (<see cref="PlayerCapabilities.KickPower"/>).
        /// </summary>
        public double KickSpeed { get; }

        /// <summary>rad. The highest launch elevation: a kick's loft (0–1) is a fraction of this.</summary>
        public double KickLoft { get; }

        /// <summary>
        /// rad/s. The most sidespin (about +z) a kick gets: full sideways aftertouch held through the
        /// whole window. A kick itself launches without spin.
        /// </summary>
        public double KickSideSpin { get; }

        /// <summary>s. From an accepted kick intent to contact. Simulation timing (INV-12).</summary>
        public double KickWindUpSeconds { get; }

        /// <summary>
        /// s. How long after contact the kicker's aftertouch input shapes the ball. It sets when and
        /// for how long input acts, not how much: the per-step weights always sum to 1.
        /// </summary>
        public double AftertouchSeconds { get; }

        /// <summary>
        /// rad. Total turn of the ball's horizontal velocity, without changing its speed, from full
        /// sideways aftertouch held through the whole window: direct, SWOS-style shaping.
        /// </summary>
        public double AftertouchBend { get; }

        /// <summary>
        /// m/s. Total upward speed added by full back aftertouch held through the whole window, or
        /// removed by full forward aftertouch.
        /// </summary>
        public double AftertouchLift { get; }

        /// <summary><see cref="KickWindUpSeconds"/> in whole steps, nearest.</summary>
        public int KickWindUpTicks { get; }

        /// <summary><see cref="AftertouchSeconds"/> in whole steps, nearest.</summary>
        public int AftertouchTicks { get; }

        /// <summary>Ticks after a kick connects before the same player can start another.</summary>
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
            w.WriteInt32(TickRate);
            w.WriteDouble(Gravity, nameof(Gravity));
            w.WriteDouble(BallRadius, nameof(BallRadius));
            w.WriteDouble(BounceRestitution, nameof(BounceRestitution));
            w.WriteDouble(BounceTangentialRetention, nameof(BounceTangentialRetention));
            w.WriteDouble(BounceSpinRetention, nameof(BounceSpinRetention));
            w.WriteDouble(SettleSpeed, nameof(SettleSpeed));
            w.WriteDouble(RollingFriction, nameof(RollingFriction));
            w.WriteDouble(StopSpeed, nameof(StopSpeed));
            w.WriteDouble(MagnusCoefficient, nameof(MagnusCoefficient));
            w.WriteDouble(AirSpinDecay, nameof(AirSpinDecay));
            w.WriteDouble(GroundSpinDecay, nameof(GroundSpinDecay));
            w.WriteDouble(KickReach, nameof(KickReach));
            w.WriteDouble(KickMaxBallHeight, nameof(KickMaxBallHeight));
            w.WriteDouble(KickSpeed, nameof(KickSpeed));
            w.WriteDouble(KickLoft, nameof(KickLoft));
            w.WriteDouble(KickSideSpin, nameof(KickSideSpin));
            w.WriteDouble(KickWindUpSeconds, nameof(KickWindUpSeconds));
            w.WriteDouble(AftertouchSeconds, nameof(AftertouchSeconds));
            w.WriteDouble(AftertouchBend, nameof(AftertouchBend));
            w.WriteDouble(AftertouchLift, nameof(AftertouchLift));
            w.WriteInt32(KickCooldownTicks);
            w.WriteDouble(GoalLineX, nameof(GoalLineX));
            return w.ToArray();
        }
    }
}
