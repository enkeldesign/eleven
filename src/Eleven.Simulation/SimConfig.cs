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
    /// Version 7 added the ball control model and its touches (#59). Version 8 added ATTACHED's
    /// values (#60). Version 9 added the goal: its mouth, its posts and crossbar, and how the frame
    /// returns the ball (#68). Version 10 added the keeper's dive, recovery and save (#69). Version 11
    /// added the turn with the ball: its angle and strength (#66).
    ///
    /// The ball- and kick-feel values tools may vary, and their valid ranges, are listed in
    /// <see cref="Tunables"/>; the constructor refuses values outside them.
    /// </summary>
    public sealed partial class SimConfig
    {
        public const int FormatVersion = 29;

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
            double goalWidth = 7.32,
            double goalHeight = 2.44,
            double postWidth = 0.12,
            double frameRestitution = 0.7,
            double keeperDiveSeconds = 0.6,
            double keeperRecoverSeconds = 0.8,
            double keeperHoldSpeed = 22,
            double keeperParryRestitution = 0.4,
            double keeperParryError = 0.3,
            ControlModel controlModel = ControlModel.Off,
            double controlReach = 0.9,
            double dribbleLeadSlow = 0.5,
            double dribbleLeadFast = 0.85,
            double touchInterval = 0.25,
            double touchStrength = 5,
            double touchError = 0.3,
            double receiveStrength = 12,
            double attachedLeadSlow = 0.5,
            double attachedLeadFast = 0.8,
            double attachedAcceleration = 25,
            double attachedSpeedCap = 3,
            double attachedDeadband = 0.2,
            double attachedReleaseReach = 1.35,
            double turnAngle = 2.0,
            double turnStrength = 6,
            double cutAngle = 0.35,
            double cutStrength = 5,
            double cutInterval = 0.07,
            double pushSpeed = 0,
            double pushFromSpeed = 0.9,
            double challengeRange = 1.0,
            double challengeCommit = 0.2,
            double challengeRecover = 0.27,
            double challengeReach = 0.9,
            double challengeImpulse = 5.0,
            double touchlineY = 34.0,
            double restartStop = 0.5,
            double restartReady = 0.4,
            double restartAuto = 1.0,
            double throwSpeed = 12.0,
            double halfLength = 180.0,
            double halfTimeBreak = 2.0,
            double volleyMaxHeight = 1.1,
            double headerMinHeight = 1.4,
            double headerMaxHeight = 2.4,
            double headerSpeed = 0.55,
            double headerReach = 0.6,
            double volleyError = 0.02,
            double headerError = 0.25,
            double dribbleSpeedPoor = 0.85,
            double dribbleSpeedGood = 0.95,
            double shieldSpeed = 0.45,
            double shieldRange = 2.0,
            double shieldHold = 0.6,
            double slideRange = 3.0,
            double slideSpeed = 8.0,
            double slideTime = 0.4,
            double slideReach = 0.9,
            double slideRecover = 1.0,
            double advantageTime = 3.0,
            double challengeFoulReach = 0.45,
            double fatigueFrom = 0.7,
            double fatigueDrain = 0.012,
            double fatigueRecover = 0.02,
            double fatigueFloor = 0.85,
            double halfTimeRecovery = 0.5,
            double offsideFrom = 10,
            double cardsFrom = 10,
            double cautionFrom = 0.95,
            double sendOffFrom = 1.2,
            double cardNoise = 0.3,
            double dogsoRange = 25,
            double injuriesFrom = 10,
            double injuryRisk = 0.01,
            double injurySerious = 0.3,
            double injuredSpeed = 0.85,
            double substitutions = 5,
            double substitutionWindows = 3,
            double cpuSubAfter = 0.67,
            double cpuSubFatigue = 0.2,
            double knockout = 0,
            double extraTimeShare = 0.3333,
            double shootoutKicks = 5,
            double shootoutKickTime = 4,
            double planSwitchAt = 0,
            double planSettleSeconds = 8,
            double planSwitchCooldown = 30,
            double cpuTactics = 0,
            double cpuChaseFrom = 0.67,
            double cpuChaseMargin = 1,
            double cpuProtectFrom = 0.67,
            double cpuTacticSpread = 0.05)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (kickCooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(kickCooldownTicks));
            if (controlModel != ControlModel.Off && controlModel != ControlModel.Free && controlModel != ControlModel.Attached) throw new ArgumentOutOfRangeException(nameof(controlModel));

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
            GoalWidth = goalWidth;
            GoalHeight = goalHeight;
            PostWidth = postWidth;
            FrameRestitution = frameRestitution;
            KeeperDiveSeconds = keeperDiveSeconds;
            KeeperRecoverSeconds = keeperRecoverSeconds;
            KeeperHoldSpeed = keeperHoldSpeed;
            KeeperParryRestitution = keeperParryRestitution;
            KeeperParryError = keeperParryError;
            ControlModel = controlModel;
            ControlReach = controlReach;
            DribbleLeadSlow = dribbleLeadSlow;
            DribbleLeadFast = dribbleLeadFast;
            TouchInterval = touchInterval;
            TouchStrength = touchStrength;
            TouchError = touchError;
            ReceiveStrength = receiveStrength;
            AttachedLeadSlow = attachedLeadSlow;
            AttachedLeadFast = attachedLeadFast;
            AttachedAcceleration = attachedAcceleration;
            AttachedSpeedCap = attachedSpeedCap;
            AttachedDeadband = attachedDeadband;
            AttachedReleaseReach = attachedReleaseReach;
            TurnAngle = turnAngle;
            TurnStrength = turnStrength;
            CutAngle = cutAngle;
            CutStrength = cutStrength;
            CutInterval = cutInterval;
            PushSpeed = pushSpeed;
            PushFromSpeed = pushFromSpeed;
            ChallengeRange = challengeRange;
            ChallengeCommit = challengeCommit;
            ChallengeRecover = challengeRecover;
            ChallengeReach = challengeReach;
            ChallengeImpulse = challengeImpulse;
            TouchlineY = touchlineY;
            RestartStop = restartStop;
            RestartReady = restartReady;
            RestartAuto = restartAuto;
            ThrowSpeed = throwSpeed;
            HalfLength = halfLength;
            HalfTimeBreak = halfTimeBreak;
            VolleyMaxHeight = volleyMaxHeight;
            HeaderMinHeight = headerMinHeight;
            HeaderMaxHeight = headerMaxHeight;
            HeaderSpeed = headerSpeed;
            HeaderReach = headerReach;
            VolleyError = volleyError;
            HeaderError = headerError;
            DribbleSpeedPoor = dribbleSpeedPoor;
            DribbleSpeedGood = dribbleSpeedGood;
            ShieldSpeed = shieldSpeed;
            ShieldRange = shieldRange;
            ShieldHold = shieldHold;
            SlideRange = slideRange;
            SlideSpeed = slideSpeed;
            SlideTime = slideTime;
            SlideReach = slideReach;
            SlideRecover = slideRecover;
            AdvantageTime = advantageTime;
            ChallengeFoulReach = challengeFoulReach;
            FatigueFrom = fatigueFrom;
            FatigueDrain = fatigueDrain;
            FatigueRecover = fatigueRecover;
            FatigueFloor = fatigueFloor;
            HalfTimeRecovery = halfTimeRecovery;
            OffsideFrom = offsideFrom;
            CardsFrom = cardsFrom;
            CautionFrom = cautionFrom;
            SendOffFrom = sendOffFrom;
            CardNoise = cardNoise;
            DogsoRange = dogsoRange;
            InjuriesFrom = injuriesFrom;
            InjuryRisk = injuryRisk;
            InjurySerious = injurySerious;
            InjuredSpeed = injuredSpeed;
            Substitutions = substitutions;
            SubstitutionWindows = substitutionWindows;
            CpuSubAfter = cpuSubAfter;
            CpuSubFatigue = cpuSubFatigue;
            Knockout = knockout;
            ExtraTimeShare = extraTimeShare;
            ShootoutKicks = shootoutKicks;
            ShootoutKickTime = shootoutKickTime;
            PlanSwitchAt = planSwitchAt;
            PlanSettleSeconds = planSettleSeconds;
            PlanSwitchCooldown = planSwitchCooldown;
            CpuTactics = cpuTactics;
            CpuChaseFrom = cpuChaseFrom;
            CpuChaseMargin = cpuChaseMargin;
            CpuProtectFrom = cpuProtectFrom;
            CpuTacticSpread = cpuTacticSpread;

            CheckTunableRanges();

            // Fails on non-finite values, so a broken config can't be constructed.
            Hash = Fnv1a64.Hash(Encode());

            // Whole steps, nearest. Math.Round is exact, so every runtime agrees.
            KickWindUpTicks = (int)Math.Round(KickWindUpSeconds * TickRate);
            AftertouchTicks = (int)Math.Round(AftertouchSeconds * TickRate);
            TouchIntervalTicks = Math.Max(1, (int)Math.Round(TouchInterval * TickRate));
            KeeperDiveTicks = Math.Max(1, (int)Math.Round(KeeperDiveSeconds * TickRate));
            KeeperRecoverTicks = Math.Max(1, (int)Math.Round(KeeperRecoverSeconds * TickRate));
            TurnCos = Maths.SimMath.Cos(TurnAngle);
            CutCos = Maths.SimMath.Cos(CutAngle);
            CutIntervalTicks = Math.Max(1, (int)Math.Round(CutInterval * TickRate));
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

        /// <summary>
        /// m. x of the goal line's outer edge; the ball crosses it when wholly beyond, x − radius &gt;
        /// this. The goal stands on it (#68): docs/simulation/goal.md.
        /// </summary>
        public double GoalLineX { get; }

        /// <summary>m. The goal mouth's width between the posts' inner faces (Law 1: 7.32 m), centred on y = 0.</summary>
        public double GoalWidth { get; }

        /// <summary>m. The goal mouth's height from the ground to the crossbar's underside (Law 1: 2.44 m).</summary>
        public double GoalHeight { get; }

        /// <summary>
        /// m. The posts' and crossbar's width (Law 1: the same for both, at most 12 cm, and the goal
        /// line's width). They are round, and their back faces stand on <see cref="GoalLineX"/>.
        /// </summary>
        public double PostWidth { get; }

        /// <summary>
        /// Fraction of the ball's speed along the contact normal the frame returns (#68). The rest of
        /// its velocity, and its spin, are kept, so a contact never adds speed.
        /// </summary>
        public double FrameRestitution { get; }

        /// <summary>s. The longest a keeper's dive carries him (#69): a dive lasts the time its distance needs, up to this.</summary>
        public double KeeperDiveSeconds { get; }

        /// <summary>s. How long a keeper is on the ground after a dive (#69).</summary>
        public double KeeperRecoverSeconds { get; }

        /// <summary>
        /// m/s. The fastest ball a keeper holds (#69), relative to him, met at his body with full
        /// handling. It falls with his handling, to half at none, and with how far out he meets it,
        /// to nothing at his fingertips; anything faster he parries.
        /// </summary>
        public double KeeperHoldSpeed { get; }

        /// <summary>Fraction of the ball's speed along the contact normal, relative to the keeper, that a parry returns (#69).</summary>
        public double KeeperParryRestitution { get; }

        /// <summary>rad. A parry's largest direction error with no handling (#69); none with full handling.</summary>
        public double KeeperParryError { get; }

        /// <summary><see cref="KeeperDiveSeconds"/> in whole steps, nearest, at least one.</summary>
        public int KeeperDiveTicks { get; }

        /// <summary><see cref="KeeperRecoverSeconds"/> in whole steps, nearest, at least one.</summary>
        public int KeeperRecoverTicks { get; }

        /// <summary>m. x of the posts' and crossbar's axes: <see cref="GoalLineX"/> − <see cref="PostWidth"/> / 2.</summary>
        public double FrameAxisX => GoalLineX - PostWidth / 2;

        /// <summary>m. |y| of the posts' axes: (<see cref="GoalWidth"/> + <see cref="PostWidth"/>) / 2.</summary>
        public double PostAxisY => (GoalWidth + PostWidth) / 2;

        /// <summary>m. z of the crossbar's axis: <see cref="GoalHeight"/> + <see cref="PostWidth"/> / 2.</summary>
        public double CrossbarAxisZ => GoalHeight + PostWidth / 2;

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

        /// <summary>m. Under ATTACHED (#60), how far ahead of a slow holder the ball is steered.</summary>
        public double AttachedLeadSlow { get; }

        /// <summary>m. Under ATTACHED, how far ahead of a holder at full speed the ball is steered; between the two it follows speed.</summary>
        public double AttachedLeadFast { get; }

        /// <summary>
        /// m/s². Under ATTACHED, the largest change the controller makes to the ball's velocity per
        /// second, at full ball control; half of it at none. It bounds every step's correction, so a
        /// ball far from its target can't snap back.
        /// </summary>
        public double AttachedAcceleration { get; }

        /// <summary>m/s. Under ATTACHED, the fastest the controller closes the ball on its target, relative to the holder, at full ball control; half of it at none.</summary>
        public double AttachedSpeedCap { get; }

        /// <summary>m. Under ATTACHED, how far the ball may drift from its target before the controller corrects its position; inside it, the ball only keeps pace.</summary>
        public double AttachedDeadband { get; }

        /// <summary>
        /// m. Under ATTACHED, how far from its holder the ball may get before it is lost. The ball is
        /// gained within <see cref="ControlReach"/>, so it is lost only beyond the larger of the two
        /// (<see cref="AttachedLossReach"/>): every value in each range is valid on its own.
        /// </summary>
        public double AttachedReleaseReach { get; }

        /// <summary>m. Where ATTACHED loses the ball: the larger of <see cref="AttachedReleaseReach"/> and <see cref="ControlReach"/>.</summary>
        public double AttachedLossReach => AttachedReleaseReach > ControlReach ? AttachedReleaseReach : ControlReach;

        /// <summary>
        /// rad. The turn with the ball (#66): the smallest change of direction, between where the
        /// stick asks to go and where the player is moving, that the footballer plays as a turn
        /// rather than an ordinary touch. docs/simulation/turn.md.
        /// </summary>
        public double TurnAngle { get; }

        /// <summary>
        /// m/s. The turn's largest change to the ball's velocity, at each of its two touches, at full
        /// ball control (#66); half of it at none. 0 turns the turn off: every touch is ordinary.
        /// </summary>
        public double TurnStrength { get; }

        /// <summary>cos(<see cref="TurnAngle"/>), through the simulation's own maths.</summary>
        internal double TurnCos { get; }

        /// <summary>
        /// rad. The cut with the ball under FREE (#75): the smallest change of direction, between
        /// where the stick asks to go and where the player is moving, that the footballer plays as a
        /// cut, with a touch as it starts rather than on the touch cadence. A change beyond
        /// <see cref="TurnAngle"/> is a turn, not a cut. docs/simulation/cut.md.
        /// </summary>
        public double CutAngle { get; }

        /// <summary>
        /// m/s. The cut touch's largest change to the ball's velocity, at full ball control (#75);
        /// half of it at none. 0 turns the cut off: a change of direction short of a turn waits for
        /// the touch cadence.
        /// </summary>
        public double CutStrength { get; }

        /// <summary>
        /// s. How soon after a cut touch the footballer touches the ball again (#75): a quick
        /// follow-up, the way the stick asks by then, before the touch cadence resumes.
        /// </summary>
        public double CutInterval { get; }

        /// <summary>
        /// m/s. Push-and-chase under FREE (#63, an experiment): how much faster than the footballer
        /// a sprint's touch sends the ball. The ball runs ahead, beyond their reach, slows under its
        /// own rolling friction, and they catch it up. 0 is off: FREE's short touches, unchanged.
        /// </summary>
        public double PushSpeed { get; }

        /// <summary>
        /// Fraction of the footballer's max speed. A touch is a push (#63) when the stick asks for
        /// at least this much and they will be running at least this fast by their next touch.
        /// </summary>
        public double PushFromSpeed { get; }

        /// <summary>m. A footballer closing on a ball an opponent controls commits to a standing challenge (#84) when it is within this of him.</summary>
        public double ChallengeRange { get; }

        /// <summary>s. How long a challenge commits him before it reaches the ball: he keeps his line and can't steer.</summary>
        public double ChallengeCommit { get; }

        /// <summary>s. How long he recovers after a challenge, won or missed: he brakes and can't play the ball.</summary>
        public double ChallengeRecover { get; }

        /// <summary>m. As the commit ends, a ball within this of him is won; one further off is missed.</summary>
        public double ChallengeReach { get; }

        /// <summary>m/s. The speed a won challenge knocks the ball away at, along his line.</summary>
        public double ChallengeImpulse { get; }

        /// <summary>m. The touchlines' outer edges, at y = ± this: half Law 1's 68 m pitch width. A ball wholly beyond one is out of play (#85).</summary>
        public double TouchlineY { get; }

        /// <summary>s. How long the ball is dead after it goes out of play or a goal, before the restart is set up (#85).</summary>
        public double RestartStop { get; }

        /// <summary>s. From the restart being set up until its taker may take it (#85). #85's start: 12 to 60 steps.</summary>
        public double RestartReady { get; }

        /// <summary>s. How long a restart waits for a human taker, once he may take it, before his controller's fallback takes it for him (#85). #85's start: 20 to 90 steps.</summary>
        public double RestartAuto { get; }

        /// <summary>m/s. A throw-in's speed at full strength (#85): a throw is a kick intent, released by the hands.</summary>
        public double ThrowSpeed { get; }

        /// <summary>s. A half's length on the match clock (#86): the brief's 2 × 3 minutes. The clock runs only while the ball is in play.</summary>
        public double HalfLength { get; }

        /// <summary>s. The pause at half-time before the second half's kick-off is set up (#86). It is not match time.</summary>
        public double HalfTimeBreak { get; }

        /// <summary>m. A ball's centre above kick height and up to this is volleyed (#94): kicked in the air, its error growing with its speed.</summary>
        public double VolleyMaxHeight { get; }

        /// <summary>m. From this height a ball is headed (#94).</summary>
        public double HeaderMinHeight { get; }

        /// <summary>m. Up to this height a ball is headed, a jump included (#94).</summary>
        public double HeaderMaxHeight { get; }

        /// <summary>Fraction of the config's hardest kick a full-strength header reaches (#94).</summary>
        public double HeaderSpeed { get; }

        /// <summary>m. How far from his body, across the ground, a footballer heads a ball (#94).</summary>
        public double HeaderReach { get; }

        /// <summary>rad per m/s of the ball's incoming speed: a volley's extra error over the kicker's own (#94).</summary>
        public double VolleyError { get; }

        /// <summary>rad. A header's error at no heading at all, shrinking to nothing at full heading (#94).</summary>
        public double HeaderError { get; }

        /// <summary>Fraction of his top speed a footballer of no ball control reaches while he has the ball (#96).</summary>
        public double DribbleSpeedPoor { get; }

        /// <summary>Fraction of his top speed a footballer of full ball control reaches while he has the ball (#96).</summary>
        public double DribbleSpeedGood { get; }

        /// <summary>Below this share of his top speed, a footballer with the ball and an opponent within shieldRange shields it (#97): the ball kept on his far side from the opponent.</summary>
        public double ShieldSpeed { get; }

        /// <summary>m. How near an opponent makes a slow carrier shield the ball (#97).</summary>
        public double ShieldRange { get; }

        /// <summary>The chance an even shield holds off a challenge through the carrier (#97): strength and tackling move it.</summary>
        public double ShieldHold { get; }

        /// <summary>How far from an opponent's ball a kick intent without the ball becomes a slide tackle (#98).</summary>
        public double SlideRange { get; }

        /// <summary>How fast a slide carries the footballer along its line at its start (#98), slowing to half of it as it ends: 2.4 m in 0.4 s at the reference.</summary>
        public double SlideSpeed { get; }

        /// <summary>How long a slide lasts (#98).</summary>
        public double SlideTime { get; }

        /// <summary>How far from the sliding footballer the ball or the man is met (#98).</summary>
        public double SlideReach { get; }

        /// <summary>How long a footballer is on the ground after a slide (#98).</summary>
        public double SlideRecover { get; }

        /// <summary>s. After a foul, how long the fouled side may keep the ball under advantage before the foul is no longer given (#99).</summary>
        public double AdvantageTime { get; }

        /// <summary>m. A standing challenge that ends this close to the man it challenged, missing the ball, is a foul (#99).</summary>
        public double ChallengeFoulReach { get; }

        /// <summary>Above this share of his top speed a footballer's energy drains, below it it recovers (#100).</summary>
        public double FatigueFrom { get; }

        /// <summary>How fast energy drains at full speed, at the reference stamina (#100).</summary>
        public double FatigueDrain { get; }

        /// <summary>How fast energy recovers standing, at the reference stamina (#100).</summary>
        public double FatigueRecover { get; }

        /// <summary>The share of his top speed and acceleration a footballer keeps with no energy left (#100).</summary>
        public double FatigueFloor { get; }

        /// <summary>The share of a footballer's lost energy half-time gives back (#100).</summary>
        public double HalfTimeRecovery { get; }

        /// <summary>Offside (Law 11, #107) applies in matches of at least this many outfielders a side: 10, eleven a side only; small-sided football plays without it.</summary>
        public double OffsideFrom { get; }

        /// <summary>Cards (Law 12, #108) are shown in matches of at least this many outfielders a side: 10, eleven a side only, so a sending-off never empties a small side.</summary>
        public double CardsFrom { get; }

        /// <summary>A foul this severe or more is reckless: a caution (#108).</summary>
        public double CautionFrom { get; }

        /// <summary>A foul this severe or more uses excessive force: a sending-off (#108).</summary>
        public double SendOffFrom { get; }

        /// <summary>How far the referee's reading of a foul's severity may vary either way, drawn from its own stream (#108).</summary>
        public double CardNoise { get; }

        /// <summary>A foul on a carrier this near the goal he attacks, with no defender but the keeper between him and it, denies an obvious goal-scoring opportunity (#108).</summary>
        public double DogsoRange { get; }

        /// <summary>Injuries (#109) happen in matches of at least this many outfielders a side: 10, eleven a side only.</summary>
        public double InjuriesFrom { get; }

        /// <summary>The chance a foul of severity 1 injures the fouled footballer, in proportion to its severity, on its own stream (#109).</summary>
        public double InjuryRisk { get; }

        /// <summary>The share of injuries serious enough to take him off; the rest he plays on with (#109).</summary>
        public double InjurySerious { get; }

        /// <summary>Injured but playing on, his top speed and acceleration fall to this share (#109).</summary>
        public double InjuredSpeed { get; }

        /// <summary>Substitutes a side may bring on in a match (#110, Law 3).</summary>
        public double Substitutions { get; }

        /// <summary>Stoppages in play in which a side may substitute (#110, Law 3); half-time is free.</summary>
        public double SubstitutionWindows { get; }

        /// <summary>Share of the match played before a CPU manager replaces a tired footballer (#110).</summary>
        public double CpuSubAfter { get; }

        /// <summary>Fatigue from which a CPU manager replaces a footballer (#110).</summary>
        public double CpuSubFatigue { get; }

        /// <summary>Whether a level match goes to extra time and then a shootout (#111): 0 a league match, where a draw stands; 1 a knockout.</summary>
        public double Knockout { get; }

        /// <summary>Each half of extra time as a share of a half (#111): 15 of 45 minutes.</summary>
        public double ExtraTimeShare { get; }

        /// <summary>Kicks each side takes in a shootout before sudden death (#111).</summary>
        public double ShootoutKicks { get; }

        /// <summary>Seconds after a shootout's kick before it is called a miss (#111).</summary>
        public double ShootoutKickTime { get; }

        /// <summary>When a side's switch of tactical plan (#126) takes effect: 0 at the next stoppage, or at once in a break; 1 at once.</summary>
        public double PlanSwitchAt { get; }

        /// <summary>s. Over how long a side's homes blend from its old plan to its new one (#126).</summary>
        public double PlanSettleSeconds { get; }

        /// <summary>s of the match clock. The least time between two of a side's plan switches (#126).</summary>
        public double PlanSwitchCooldown { get; }

        /// <summary>Whether a CPU side's manager switches tactical plans and makes tactical substitutions (#127): 1 on, 0 off.</summary>
        public double CpuTactics { get; }

        /// <summary>The share of the match from which a CPU manager behind chases (#127), his spread aside.</summary>
        public double CpuChaseFrom { get; }

        /// <summary>Goals behind from which a CPU manager chases (#127).</summary>
        public double CpuChaseMargin { get; }

        /// <summary>The share of the match from which a CPU manager ahead protects his lead (#127), his spread aside.</summary>
        public double CpuProtectFrom { get; }

        /// <summary>How far either way a CPU manager's own draw shifts his thresholds (#127), as a share of the match.</summary>
        public double CpuTacticSpread { get; }

        /// <summary>Steps over which a side's plan settles (#126): planSettleSeconds at the tick rate.</summary>
        public int PlanSettleTicks => (int)Math.Round(PlanSettleSeconds * TickRate);

        /// <summary>Steps of the match clock between a side's plan switches (#126): planSwitchCooldown at the tick rate.</summary>
        public int PlanCooldownTicks => (int)Math.Round(PlanSwitchCooldown * TickRate);

        /// <summary>A half's length in steps of match clock (#86).</summary>
        public int HalfTicks => Math.Max(1, (int)Math.Round(HalfLength * TickRate));

        /// <summary>Steps of the half-time break (#86), at least one.</summary>
        public int HalfTimeTicks => Math.Max(1, (int)Math.Round(HalfTimeBreak * TickRate));

        /// <summary>Steps of match clock in each half of extra time (#111): extraTimeShare of a half, at least one.</summary>
        public int ExtraTicks => Math.Max(1, (int)Math.Round(HalfTicks * ExtraTimeShare));

        /// <summary>Steps after a shootout's kick before it is called a miss (#111), at least one.</summary>
        public int ShootoutKickTicks => Math.Max(1, (int)Math.Round(ShootoutKickTime * TickRate));

        /// <summary>Steps the ball is dead before a restart is set up, at least one (#85).</summary>
        public int RestartStopTicks => Math.Max(1, (int)Math.Round(RestartStop * TickRate));

        /// <summary>Steps from a restart's set-up until it may be taken, at least one (#85).</summary>
        public int RestartReadyTicks => Math.Max(1, (int)Math.Round(RestartReady * TickRate));

        /// <summary>Steps a restart waits for a human taker before the fallback takes it, at least one (#85).</summary>
        public int RestartAutoTicks => Math.Max(1, (int)Math.Round(RestartAuto * TickRate));

        /// <summary>Steps a challenge commits for, at least one (#84).</summary>
        public int ChallengeCommitTicks => Math.Max(1, (int)Math.Round(ChallengeCommit * TickRate));

        /// <summary>Steps a challenge's recovery lasts, at least one (#84).</summary>
        public int ChallengeRecoverTicks => Math.Max(1, (int)Math.Round(ChallengeRecover * TickRate));

        /// <summary>A slide's length in steps (#98), at least one.</summary>
        public int SlideTicks => Math.Max(1, (int)Math.Round(SlideTime * TickRate));

        /// <summary>The time on the ground after a slide in steps (#98), at least one.</summary>
        public int SlideRecoverTicks => Math.Max(1, (int)Math.Round(SlideRecover * TickRate));

        /// <summary>cos(<see cref="CutAngle"/>), through the simulation's own maths.</summary>
        internal double CutCos { get; }

        /// <summary><see cref="CutInterval"/> in whole steps, nearest, at least one.</summary>
        public int CutIntervalTicks { get; }

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
                goalWidth: r.ReadDouble(nameof(GoalWidth)),
                goalHeight: r.ReadDouble(nameof(GoalHeight)),
                postWidth: r.ReadDouble(nameof(PostWidth)),
                frameRestitution: r.ReadDouble(nameof(FrameRestitution)),
                keeperDiveSeconds: r.ReadDouble(nameof(KeeperDiveSeconds)),
                keeperRecoverSeconds: r.ReadDouble(nameof(KeeperRecoverSeconds)),
                keeperHoldSpeed: r.ReadDouble(nameof(KeeperHoldSpeed)),
                keeperParryRestitution: r.ReadDouble(nameof(KeeperParryRestitution)),
                keeperParryError: r.ReadDouble(nameof(KeeperParryError)),
                controlModel: ReadControlModel(r.ReadInt32()),
                controlReach: r.ReadDouble(nameof(ControlReach)),
                dribbleLeadSlow: r.ReadDouble(nameof(DribbleLeadSlow)),
                dribbleLeadFast: r.ReadDouble(nameof(DribbleLeadFast)),
                touchInterval: r.ReadDouble(nameof(TouchInterval)),
                touchStrength: r.ReadDouble(nameof(TouchStrength)),
                touchError: r.ReadDouble(nameof(TouchError)),
                receiveStrength: r.ReadDouble(nameof(ReceiveStrength)),
                attachedLeadSlow: r.ReadDouble(nameof(AttachedLeadSlow)),
                attachedLeadFast: r.ReadDouble(nameof(AttachedLeadFast)),
                attachedAcceleration: r.ReadDouble(nameof(AttachedAcceleration)),
                attachedSpeedCap: r.ReadDouble(nameof(AttachedSpeedCap)),
                attachedDeadband: r.ReadDouble(nameof(AttachedDeadband)),
                attachedReleaseReach: r.ReadDouble(nameof(AttachedReleaseReach)),
                turnAngle: r.ReadDouble(nameof(TurnAngle)),
                turnStrength: r.ReadDouble(nameof(TurnStrength)),
                cutAngle: r.ReadDouble(nameof(CutAngle)),
                cutStrength: r.ReadDouble(nameof(CutStrength)),
                cutInterval: r.ReadDouble(nameof(CutInterval)),
                pushSpeed: r.ReadDouble(nameof(PushSpeed)),
                pushFromSpeed: r.ReadDouble(nameof(PushFromSpeed)),
                challengeRange: r.ReadDouble(nameof(ChallengeRange)),
                challengeCommit: r.ReadDouble(nameof(ChallengeCommit)),
                challengeRecover: r.ReadDouble(nameof(ChallengeRecover)),
                challengeReach: r.ReadDouble(nameof(ChallengeReach)),
                challengeImpulse: r.ReadDouble(nameof(ChallengeImpulse)),
                touchlineY: r.ReadDouble(nameof(TouchlineY)),
                restartStop: r.ReadDouble(nameof(RestartStop)),
                restartReady: r.ReadDouble(nameof(RestartReady)),
                restartAuto: r.ReadDouble(nameof(RestartAuto)),
                throwSpeed: r.ReadDouble(nameof(ThrowSpeed)),
                halfLength: r.ReadDouble(nameof(HalfLength)),
                halfTimeBreak: r.ReadDouble(nameof(HalfTimeBreak)),
                volleyMaxHeight: r.ReadDouble(nameof(VolleyMaxHeight)),
                headerMinHeight: r.ReadDouble(nameof(HeaderMinHeight)),
                headerMaxHeight: r.ReadDouble(nameof(HeaderMaxHeight)),
                headerSpeed: r.ReadDouble(nameof(HeaderSpeed)),
                headerReach: r.ReadDouble(nameof(HeaderReach)),
                volleyError: r.ReadDouble(nameof(VolleyError)),
                headerError: r.ReadDouble(nameof(HeaderError)),
                dribbleSpeedPoor: r.ReadDouble(nameof(DribbleSpeedPoor)),
                dribbleSpeedGood: r.ReadDouble(nameof(DribbleSpeedGood)),
                shieldSpeed: r.ReadDouble(nameof(ShieldSpeed)),
                shieldRange: r.ReadDouble(nameof(ShieldRange)),
                shieldHold: r.ReadDouble(nameof(ShieldHold)),
                slideRange: r.ReadDouble(nameof(SlideRange)),
                slideSpeed: r.ReadDouble(nameof(SlideSpeed)),
                slideTime: r.ReadDouble(nameof(SlideTime)),
                slideReach: r.ReadDouble(nameof(SlideReach)),
                slideRecover: r.ReadDouble(nameof(SlideRecover)),
                advantageTime: r.ReadDouble(nameof(AdvantageTime)),
                challengeFoulReach: r.ReadDouble(nameof(ChallengeFoulReach)),
                fatigueFrom: r.ReadDouble(nameof(FatigueFrom)),
                fatigueDrain: r.ReadDouble(nameof(FatigueDrain)),
                fatigueRecover: r.ReadDouble(nameof(FatigueRecover)),
                fatigueFloor: r.ReadDouble(nameof(FatigueFloor)),
                halfTimeRecovery: r.ReadDouble(nameof(HalfTimeRecovery)),
                offsideFrom: r.ReadDouble(nameof(OffsideFrom)),
                cardsFrom: r.ReadDouble(nameof(CardsFrom)),
                cautionFrom: r.ReadDouble(nameof(CautionFrom)),
                sendOffFrom: r.ReadDouble(nameof(SendOffFrom)),
                cardNoise: r.ReadDouble(nameof(CardNoise)),
                dogsoRange: r.ReadDouble(nameof(DogsoRange)),
                injuriesFrom: r.ReadDouble(nameof(InjuriesFrom)),
                injuryRisk: r.ReadDouble(nameof(InjuryRisk)),
                injurySerious: r.ReadDouble(nameof(InjurySerious)),
                injuredSpeed: r.ReadDouble(nameof(InjuredSpeed)),
                substitutions: r.ReadDouble(nameof(Substitutions)),
                substitutionWindows: r.ReadDouble(nameof(SubstitutionWindows)),
                cpuSubAfter: r.ReadDouble(nameof(CpuSubAfter)),
                cpuSubFatigue: r.ReadDouble(nameof(CpuSubFatigue)),
                knockout: r.ReadDouble(nameof(Knockout)),
                extraTimeShare: r.ReadDouble(nameof(ExtraTimeShare)),
                shootoutKicks: r.ReadDouble(nameof(ShootoutKicks)),
                shootoutKickTime: r.ReadDouble(nameof(ShootoutKickTime)),
                planSwitchAt: r.ReadDouble(nameof(PlanSwitchAt)),
                planSettleSeconds: r.ReadDouble(nameof(PlanSettleSeconds)),
                planSwitchCooldown: r.ReadDouble(nameof(PlanSwitchCooldown)),
                cpuTactics: r.ReadDouble(nameof(CpuTactics)),
                cpuChaseFrom: r.ReadDouble(nameof(CpuChaseFrom)),
                cpuChaseMargin: r.ReadDouble(nameof(CpuChaseMargin)),
                cpuProtectFrom: r.ReadDouble(nameof(CpuProtectFrom)),
                cpuTacticSpread: r.ReadDouble(nameof(CpuTacticSpread)));

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
            w.WriteDouble(GoalWidth, nameof(GoalWidth));
            w.WriteDouble(GoalHeight, nameof(GoalHeight));
            w.WriteDouble(PostWidth, nameof(PostWidth));
            w.WriteDouble(FrameRestitution, nameof(FrameRestitution));
            w.WriteDouble(KeeperDiveSeconds, nameof(KeeperDiveSeconds));
            w.WriteDouble(KeeperRecoverSeconds, nameof(KeeperRecoverSeconds));
            w.WriteDouble(KeeperHoldSpeed, nameof(KeeperHoldSpeed));
            w.WriteDouble(KeeperParryRestitution, nameof(KeeperParryRestitution));
            w.WriteDouble(KeeperParryError, nameof(KeeperParryError));
            w.WriteInt32((int)ControlModel);
            w.WriteDouble(ControlReach, nameof(ControlReach));
            w.WriteDouble(DribbleLeadSlow, nameof(DribbleLeadSlow));
            w.WriteDouble(DribbleLeadFast, nameof(DribbleLeadFast));
            w.WriteDouble(TouchInterval, nameof(TouchInterval));
            w.WriteDouble(TouchStrength, nameof(TouchStrength));
            w.WriteDouble(TouchError, nameof(TouchError));
            w.WriteDouble(ReceiveStrength, nameof(ReceiveStrength));
            w.WriteDouble(AttachedLeadSlow, nameof(AttachedLeadSlow));
            w.WriteDouble(AttachedLeadFast, nameof(AttachedLeadFast));
            w.WriteDouble(AttachedAcceleration, nameof(AttachedAcceleration));
            w.WriteDouble(AttachedSpeedCap, nameof(AttachedSpeedCap));
            w.WriteDouble(AttachedDeadband, nameof(AttachedDeadband));
            w.WriteDouble(AttachedReleaseReach, nameof(AttachedReleaseReach));
            w.WriteDouble(TurnAngle, nameof(TurnAngle));
            w.WriteDouble(TurnStrength, nameof(TurnStrength));
            w.WriteDouble(CutAngle, nameof(CutAngle));
            w.WriteDouble(CutStrength, nameof(CutStrength));
            w.WriteDouble(CutInterval, nameof(CutInterval));
            w.WriteDouble(PushSpeed, nameof(PushSpeed));
            w.WriteDouble(PushFromSpeed, nameof(PushFromSpeed));
            w.WriteDouble(ChallengeRange, nameof(ChallengeRange));
            w.WriteDouble(ChallengeCommit, nameof(ChallengeCommit));
            w.WriteDouble(ChallengeRecover, nameof(ChallengeRecover));
            w.WriteDouble(ChallengeReach, nameof(ChallengeReach));
            w.WriteDouble(ChallengeImpulse, nameof(ChallengeImpulse));
            w.WriteDouble(TouchlineY, nameof(TouchlineY));
            w.WriteDouble(RestartStop, nameof(RestartStop));
            w.WriteDouble(RestartReady, nameof(RestartReady));
            w.WriteDouble(RestartAuto, nameof(RestartAuto));
            w.WriteDouble(ThrowSpeed, nameof(ThrowSpeed));
            w.WriteDouble(HalfLength, nameof(HalfLength));
            w.WriteDouble(HalfTimeBreak, nameof(HalfTimeBreak));
            w.WriteDouble(VolleyMaxHeight, nameof(VolleyMaxHeight));
            w.WriteDouble(HeaderMinHeight, nameof(HeaderMinHeight));
            w.WriteDouble(HeaderMaxHeight, nameof(HeaderMaxHeight));
            w.WriteDouble(HeaderSpeed, nameof(HeaderSpeed));
            w.WriteDouble(HeaderReach, nameof(HeaderReach));
            w.WriteDouble(VolleyError, nameof(VolleyError));
            w.WriteDouble(HeaderError, nameof(HeaderError));
            w.WriteDouble(DribbleSpeedPoor, nameof(DribbleSpeedPoor));
            w.WriteDouble(DribbleSpeedGood, nameof(DribbleSpeedGood));
            w.WriteDouble(ShieldSpeed, nameof(ShieldSpeed));
            w.WriteDouble(ShieldRange, nameof(ShieldRange));
            w.WriteDouble(ShieldHold, nameof(ShieldHold));
            w.WriteDouble(SlideRange, nameof(SlideRange));
            w.WriteDouble(SlideSpeed, nameof(SlideSpeed));
            w.WriteDouble(SlideTime, nameof(SlideTime));
            w.WriteDouble(SlideReach, nameof(SlideReach));
            w.WriteDouble(SlideRecover, nameof(SlideRecover));
            w.WriteDouble(AdvantageTime, nameof(AdvantageTime));
            w.WriteDouble(ChallengeFoulReach, nameof(ChallengeFoulReach));
            w.WriteDouble(FatigueFrom, nameof(FatigueFrom));
            w.WriteDouble(FatigueDrain, nameof(FatigueDrain));
            w.WriteDouble(FatigueRecover, nameof(FatigueRecover));
            w.WriteDouble(FatigueFloor, nameof(FatigueFloor));
            w.WriteDouble(HalfTimeRecovery, nameof(HalfTimeRecovery));
            w.WriteDouble(OffsideFrom, nameof(OffsideFrom));
            w.WriteDouble(CardsFrom, nameof(CardsFrom));
            w.WriteDouble(CautionFrom, nameof(CautionFrom));
            w.WriteDouble(SendOffFrom, nameof(SendOffFrom));
            w.WriteDouble(CardNoise, nameof(CardNoise));
            w.WriteDouble(DogsoRange, nameof(DogsoRange));
            w.WriteDouble(InjuriesFrom, nameof(InjuriesFrom));
            w.WriteDouble(InjuryRisk, nameof(InjuryRisk));
            w.WriteDouble(InjurySerious, nameof(InjurySerious));
            w.WriteDouble(InjuredSpeed, nameof(InjuredSpeed));
            w.WriteDouble(Substitutions, nameof(Substitutions));
            w.WriteDouble(SubstitutionWindows, nameof(SubstitutionWindows));
            w.WriteDouble(CpuSubAfter, nameof(CpuSubAfter));
            w.WriteDouble(CpuSubFatigue, nameof(CpuSubFatigue));
            w.WriteDouble(Knockout, nameof(Knockout));
            w.WriteDouble(ExtraTimeShare, nameof(ExtraTimeShare));
            w.WriteDouble(ShootoutKicks, nameof(ShootoutKicks));
            w.WriteDouble(ShootoutKickTime, nameof(ShootoutKickTime));
            w.WriteDouble(PlanSwitchAt, nameof(PlanSwitchAt));
            w.WriteDouble(PlanSettleSeconds, nameof(PlanSettleSeconds));
            w.WriteDouble(PlanSwitchCooldown, nameof(PlanSwitchCooldown));
            w.WriteDouble(CpuTactics, nameof(CpuTactics));
            w.WriteDouble(CpuChaseFrom, nameof(CpuChaseFrom));
            w.WriteDouble(CpuChaseMargin, nameof(CpuChaseMargin));
            w.WriteDouble(CpuProtectFrom, nameof(CpuProtectFrom));
            w.WriteDouble(CpuTacticSpread, nameof(CpuTacticSpread));
            return w.ToArray();
        }

        private static ControlModel ReadControlModel(int value) =>
            value == (int)ControlModel.Off || value == (int)ControlModel.Free || value == (int)ControlModel.Attached
                ? (ControlModel)value
                : throw new FormatException($"Unknown ball control model {value}.");
    }
}
