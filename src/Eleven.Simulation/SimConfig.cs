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
    /// <see cref="BounceSpinRetention"/> (#40). Version 6 added the quick pass's range and weight (#38).
    /// Version 7 added the ball control model and its touches (#59).
    ///
    /// The ball- and kick-feel values tools may vary, and their valid ranges, are listed in
    /// <see cref="Tunables"/>; the constructor refuses values outside them.
    /// </summary>
    public sealed partial class SimConfig
    {
        public const int FormatVersion = 7;

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
            double passMaxRange = 30,
            double passArrivalSpeed = 4,
            double passBackWeight = 2,
            double passFreeDistance = 10,
            int kickCooldownTicks = 30,
            double goalLineX = 52.5,
            ControlModel controlModel = ControlModel.Off,
            double controlReach = 0.9,
            double dribbleLeadSlow = 0.5,
            double dribbleLeadFast = 0.85,
            double touchInterval = 0.25,
            double touchStrength = 5,
            double touchError = 0.3,
            double receiveStrength = 12)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (kickCooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(kickCooldownTicks));
            if (controlModel != ControlModel.Off && controlModel != ControlModel.Free) throw new ArgumentOutOfRangeException(nameof(controlModel));

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
            PassMaxRange = passMaxRange;
            PassArrivalSpeed = passArrivalSpeed;
            PassBackWeight = passBackWeight;
            PassFreeDistance = passFreeDistance;
            KickCooldownTicks = kickCooldownTicks;
            GoalLineX = goalLineX;
            ControlModel = controlModel;
            ControlReach = controlReach;
            DribbleLeadSlow = dribbleLeadSlow;
            DribbleLeadFast = dribbleLeadFast;
            TouchInterval = touchInterval;
            TouchStrength = touchStrength;
            TouchError = touchError;
            ReceiveStrength = receiveStrength;

            CheckTunableRanges();

            // Fails on non-finite values, so a broken config can't be constructed.
            Hash = Fnv1a64.Hash(Encode());

            // Whole steps, nearest. Math.Round is exact, so every runtime agrees.
            KickWindUpTicks = (int)Math.Round(KickWindUpSeconds * TickRate);
            AftertouchTicks = (int)Math.Round(AftertouchSeconds * TickRate);
            TouchIntervalTicks = Math.Max(1, (int)Math.Round(TouchInterval * TickRate));
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

        /// <summary>m. The furthest teammate a quick pass considers (#38).</summary>
        public double PassMaxRange { get; }

        /// <summary>
        /// m/s. How much faster than the receiver's run along it a quick pass is weighted to arrive,
        /// at the predicted receive point: a standing receiver gets this speed, a runner the speed of
        /// their run plus this, so they can take it in stride.
        /// </summary>
        public double PassArrivalSpeed { get; }

        /// <summary>
        /// How much a quick pass's choice prefers receivers ahead: a receiver straight behind costs
        /// (1 + this) times their distance, one to the side (1 + this / 2) times, one ahead just the distance.
        /// </summary>
        public double PassBackWeight { get; }

        /// <summary>m. How far a quick pass with no candidate is weighted to run along the heading.</summary>
        public double PassFreeDistance { get; }

        /// <summary><see cref="KickWindUpSeconds"/> in whole steps, nearest.</summary>
        public int KickWindUpTicks { get; }

        /// <summary><see cref="AftertouchSeconds"/> in whole steps, nearest.</summary>
        public int AftertouchTicks { get; }

        /// <summary>Ticks after a kick connects before the same player can start another.</summary>
        public int KickCooldownTicks { get; }

        /// <summary>m. x of the goal line; the ball crosses it when wholly beyond, x − radius &gt; this.</summary>
        public double GoalLineX { get; }

        /// <summary>How players control the ball in possession (#29, #59).</summary>
        public ControlModel ControlModel { get; }

        /// <summary>m. Horizontal distance from player to ball centre within which a touch can control the ball (#59).</summary>
        public double ControlReach { get; }

        /// <summary>m. How far ahead of a slow player a touch places the ball, at the next touch (#59).</summary>
        public double DribbleLeadSlow { get; }

        /// <summary>m. How far ahead of a player at full speed a touch places the ball; between the two it follows speed (#59).</summary>
        public double DribbleLeadFast { get; }

        /// <summary>s. From a touch to the next one the same player can make (#59).</summary>
        public double TouchInterval { get; }

        /// <summary>
        /// m/s. The largest change one touch can make to the ball's velocity, beyond cushioning it,
        /// at full ball control (#59); half of it at none. A turn at speed that needs more redirects
        /// the ball only partly.
        /// </summary>
        public double TouchStrength { get; }

        /// <summary>
        /// m/s. How much of the ball's speed relative to the player one touch can take off, its
        /// cushioning first touch, at full ball control (#59); half of it at none. A ball arriving
        /// faster than this is only partly controlled.
        /// </summary>
        public double ReceiveStrength { get; }

        /// <summary>
        /// rad. A touch's largest direction error at no ball control on an easy touch (#59). It
        /// shrinks with the player's ball control and grows with the turn and the overload.
        /// </summary>
        public double TouchError { get; }

        /// <summary><see cref="TouchInterval"/> in whole steps, nearest, at least one.</summary>
        public int TouchIntervalTicks { get; }

        /// <summary>FNV-1a 64 of <see cref="Encode"/>. Stable across runs, processes and platforms.</summary>
        public ulong Hash { get; }

        /// <summary>
        /// Rebuilds a config from <see cref="Encode"/>'s bytes (#44), so evidence can carry the exact
        /// config it was made under. Fails clearly on another format version, truncation or trailing
        /// bytes; the constructor's own checks still apply.
        /// </summary>
        public static SimConfig Decode(byte[] data)
        {
            var r = new CanonicalReader(data ?? throw new ArgumentNullException(nameof(data)));
            int format = r.ReadInt32();
            if (format != FormatVersion) throw new FormatException($"Unsupported config format {format}; this build reads config format {FormatVersion}.");

            var config = new SimConfig(
                tickRate: r.ReadInt32(),
                gravity: r.ReadDouble(nameof(Gravity)),
                ballRadius: r.ReadDouble(nameof(BallRadius)),
                bounceRestitution: r.ReadDouble(nameof(BounceRestitution)),
                bounceTangentialRetention: r.ReadDouble(nameof(BounceTangentialRetention)),
                bounceSpinRetention: r.ReadDouble(nameof(BounceSpinRetention)),
                settleSpeed: r.ReadDouble(nameof(SettleSpeed)),
                rollingFriction: r.ReadDouble(nameof(RollingFriction)),
                stopSpeed: r.ReadDouble(nameof(StopSpeed)),
                magnusCoefficient: r.ReadDouble(nameof(MagnusCoefficient)),
                airSpinDecay: r.ReadDouble(nameof(AirSpinDecay)),
                groundSpinDecay: r.ReadDouble(nameof(GroundSpinDecay)),
                kickReach: r.ReadDouble(nameof(KickReach)),
                kickMaxBallHeight: r.ReadDouble(nameof(KickMaxBallHeight)),
                kickSpeed: r.ReadDouble(nameof(KickSpeed)),
                kickLoft: r.ReadDouble(nameof(KickLoft)),
                kickSideSpin: r.ReadDouble(nameof(KickSideSpin)),
                kickWindUpSeconds: r.ReadDouble(nameof(KickWindUpSeconds)),
                aftertouchSeconds: r.ReadDouble(nameof(AftertouchSeconds)),
                aftertouchBend: r.ReadDouble(nameof(AftertouchBend)),
                aftertouchLift: r.ReadDouble(nameof(AftertouchLift)),
                passMaxRange: r.ReadDouble(nameof(PassMaxRange)),
                passArrivalSpeed: r.ReadDouble(nameof(PassArrivalSpeed)),
                passBackWeight: r.ReadDouble(nameof(PassBackWeight)),
                passFreeDistance: r.ReadDouble(nameof(PassFreeDistance)),
                kickCooldownTicks: r.ReadInt32(),
                goalLineX: r.ReadDouble(nameof(GoalLineX)),
                controlModel: ReadControlModel(r.ReadInt32()),
                controlReach: r.ReadDouble(nameof(ControlReach)),
                dribbleLeadSlow: r.ReadDouble(nameof(DribbleLeadSlow)),
                dribbleLeadFast: r.ReadDouble(nameof(DribbleLeadFast)),
                touchInterval: r.ReadDouble(nameof(TouchInterval)),
                touchStrength: r.ReadDouble(nameof(TouchStrength)),
                touchError: r.ReadDouble(nameof(TouchError)),
                receiveStrength: r.ReadDouble(nameof(ReceiveStrength)));

            if (!r.AtEnd) throw new FormatException("Trailing bytes after the config.");
            return config;
        }

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
            w.WriteDouble(PassMaxRange, nameof(PassMaxRange));
            w.WriteDouble(PassArrivalSpeed, nameof(PassArrivalSpeed));
            w.WriteDouble(PassBackWeight, nameof(PassBackWeight));
            w.WriteDouble(PassFreeDistance, nameof(PassFreeDistance));
            w.WriteInt32(KickCooldownTicks);
            w.WriteDouble(GoalLineX, nameof(GoalLineX));
            w.WriteInt32((int)ControlModel);
            w.WriteDouble(ControlReach, nameof(ControlReach));
            w.WriteDouble(DribbleLeadSlow, nameof(DribbleLeadSlow));
            w.WriteDouble(DribbleLeadFast, nameof(DribbleLeadFast));
            w.WriteDouble(TouchInterval, nameof(TouchInterval));
            w.WriteDouble(TouchStrength, nameof(TouchStrength));
            w.WriteDouble(TouchError, nameof(TouchError));
            w.WriteDouble(ReceiveStrength, nameof(ReceiveStrength));
            return w.ToArray();
        }

        private static ControlModel ReadControlModel(int value) =>
            value == (int)ControlModel.Off || value == (int)ControlModel.Free
                ? (ControlModel)value
                : throw new FormatException($"Unknown ball control model {value}.");
    }
}
