using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Eleven.Simulation
{
    /// <summary>
    /// A value tools may vary, a config value or a player capability: its unit, its valid range
    /// (inclusive), and, where the retro-football research gives one, the starting range a feel
    /// test bench should explore.
    /// </summary>
    public sealed class TunableParameter
    {
        /// <summary>
        /// Public so tools outside the simulation can describe their own knobs the same way, such as
        /// a touch adapter's gesture parameters (#28). Metadata only: no config or state depends on it.
        /// </summary>
        public TunableParameter(string group, string name, string unit, double min, double max, double? startMin = null, double? startMax = null)
        {
            Group = group;
            Name = name;
            Unit = unit;
            Min = min;
            Max = max;
            StartMin = startMin;
            StartMax = startMax;
        }

        /// <summary>
        /// The name tools use: a <see cref="SimConfig"/> constructor parameter, e.g. <c>kickSpeed</c>,
        /// or a <see cref="PlayerCapabilities"/> value, e.g. <c>maxSpeed</c>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Which part of the feel it belongs to, for tools that list it (#45): <see cref="BallGroup"/>,
        /// <see cref="KickGroup"/>, <see cref="PassGroup"/>, <see cref="ControlGroup"/>,
        /// <see cref="AttachedGroup"/>, <see cref="GoalGroup"/>, <see cref="KeeperGroup"/> or <see cref="PlayerGroup"/>. Metadata only; no config value or hash depends on it.
        /// </summary>
        public string Group { get; }

        public const string BallGroup = "Ball";
        public const string KickGroup = "Kick";
        public const string PassGroup = "Pass";

        /// <summary>Ball control in possession (#59): touches, reach and lead.</summary>
        public const string ControlGroup = "Control";

        /// <summary>The attached comparator (#60): its lead, correction and release reach.</summary>
        public const string AttachedGroup = "Attached";

        /// <summary>The goal (#68): its mouth, its frame and how the frame returns the ball.</summary>
        public const string GoalGroup = "Goal";

        /// <summary>The keeper (#69): his dive, recovery and save, and his own capabilities.</summary>
        public const string KeeperGroup = KeeperCapabilities.Group;
        public const string PlayerGroup = "Player";

        public string Unit { get; }

        /// <summary>Lowest valid value. Anything outside [Min, Max] is refused.</summary>
        public double Min { get; }

        public double Max { get; }

        /// <summary>
        /// The research-derived starting range (docs/simulation/ball.md and movement.md), or null
        /// where the research gives none. A starting point for play, to be narrowed by playable
        /// evidence, not a target.
        /// </summary>
        public double? StartMin { get; }

        public double? StartMax { get; }
    }

    /// <summary>
    /// The ball-, kick-, pass-, ball-control- and goal-feel surface (#24, #37, #38, #59, #68): the supported way for tools, presets and the later
    /// tuning panel to read and vary config. Values change only by building a new config with <see cref="With(string, double)"/>,
    /// so every variant is validated and hashed like any other config.
    /// </summary>
    public sealed partial class SimConfig
    {
        /// <summary>
        /// The tunable values. The valid ranges keep the ball physical and the integration well
        /// behaved (restitution and retention at most 1, decay at most one tick's worth). The
        /// starting ranges come from the retro-football research and football measurements in
        /// docs/simulation/ball.md and kick.md; values without one are technical references. Neither
        /// range is a feel target.
        /// </summary>
        public static readonly ReadOnlyCollection<TunableParameter> Tunables = Array.AsReadOnly(new[]
        {
            new TunableParameter(TunableParameter.BallGroup, "gravity", "m/s²", 0.1, 50),
            new TunableParameter(TunableParameter.BallGroup, "ballRadius", "m", 0.05, 0.5),
            new TunableParameter(TunableParameter.BallGroup, "bounceRestitution", "fraction", 0, 1, 0.5, 0.75),
            new TunableParameter(TunableParameter.BallGroup, "bounceTangentialRetention", "fraction", 0, 1, 0.45, 0.85),
            new TunableParameter(TunableParameter.BallGroup, "bounceSpinRetention", "fraction", 0, 1, 0.15, 0.35),
            new TunableParameter(TunableParameter.BallGroup, "settleSpeed", "m/s", 0, 5),
            new TunableParameter(TunableParameter.BallGroup, "rollingFriction", "fraction of g", 0, 2, 0.05, 0.3),
            new TunableParameter(TunableParameter.BallGroup, "stopSpeed", "m/s", 0, 2),
            new TunableParameter(TunableParameter.BallGroup, "magnusCoefficient", "1/rad", 0, 0.05, 0.004, 0.008),
            new TunableParameter(TunableParameter.BallGroup, "airSpinDecay", "1/s", 0, 60),
            new TunableParameter(TunableParameter.BallGroup, "groundSpinDecay", "1/s", 0, 60),
            new TunableParameter(TunableParameter.KickGroup, "kickSpeed", "m/s", 0, 60, 15, 32),
            new TunableParameter(TunableParameter.KickGroup, "kickLoft", "rad", 0, 1.5, 0, 1.05),
            new TunableParameter(TunableParameter.KickGroup, "kickSideSpin", "rad/s", 0, 100, 0, 40),
            new TunableParameter(TunableParameter.KickGroup, "kickWindUpSeconds", "s", 0, 0.5),
            new TunableParameter(TunableParameter.KickGroup, "aftertouchSeconds", "s", 0, 1, 0.15, 0.30),
            new TunableParameter(TunableParameter.KickGroup, "aftertouchBend", "rad", 0, 0.5),
            new TunableParameter(TunableParameter.KickGroup, "aftertouchLift", "m/s", 0, 10),
            new TunableParameter(TunableParameter.PassGroup, "passMaxRange", "m", 5, 60),
            new TunableParameter(TunableParameter.PassGroup, "passArrivalSpeed", "m/s", 0, 15),
            new TunableParameter(TunableParameter.PassGroup, "passBackWeight", "factor", 0, 10),
            new TunableParameter(TunableParameter.PassGroup, "passFreeDistance", "m", 1, 40),
            new TunableParameter(TunableParameter.ControlGroup, "controlReach", "m", 0.3, 2, 0.7, 1.2),
            new TunableParameter(TunableParameter.ControlGroup, "dribbleLeadSlow", "m", 0.2, 1.5, 0.35, 0.9),
            new TunableParameter(TunableParameter.ControlGroup, "dribbleLeadFast", "m", 0.3, 3, 0.8, 2.0),
            new TunableParameter(TunableParameter.ControlGroup, "touchInterval", "s", 0.05, 1),
            new TunableParameter(TunableParameter.ControlGroup, "touchStrength", "m/s", 1, 20),
            new TunableParameter(TunableParameter.ControlGroup, "touchError", "rad", 0, 0.6),
            new TunableParameter(TunableParameter.ControlGroup, "receiveStrength", "m/s", 1, 30),
            new TunableParameter(TunableParameter.ControlGroup, "turnAngle", "rad", Math.PI / 2, Math.PI),
            new TunableParameter(TunableParameter.ControlGroup, "turnStrength", "m/s", 0, 20),
            new TunableParameter(TunableParameter.ControlGroup, "cutAngle", "rad", Math.PI / 12, Math.PI / 2),
            new TunableParameter(TunableParameter.ControlGroup, "cutStrength", "m/s", 0, 20),
            new TunableParameter(TunableParameter.ControlGroup, "cutInterval", "s", 0.02, 1),
            new TunableParameter(TunableParameter.ControlGroup, "pushSpeed", "m/s", 0, 5),
            new TunableParameter(TunableParameter.ControlGroup, "pushFromSpeed", "fraction of maxSpeed", 0.5, 1),
            new TunableParameter(TunableParameter.ControlGroup, "challengeRange", "m", 0.3, 2, 0.5, 1.2),
            new TunableParameter(TunableParameter.ControlGroup, "challengeCommit", "s", 0.05, 0.6, 0.17, 0.33),
            new TunableParameter(TunableParameter.ControlGroup, "challengeRecover", "s", 0.05, 1, 0.17, 0.4),
            new TunableParameter(TunableParameter.ControlGroup, "challengeReach", "m", 0.3, 2),
            new TunableParameter(TunableParameter.ControlGroup, "challengeImpulse", "m/s", 0, 15),
            new TunableParameter(TunableParameter.GoalGroup, "touchlineY", "m", 15, 45),
            new TunableParameter(TunableParameter.GoalGroup, "restartStop", "s", 0.1, 3),
            new TunableParameter(TunableParameter.GoalGroup, "restartReady", "s", 0.1, 2, 0.2, 1.0),
            new TunableParameter(TunableParameter.GoalGroup, "restartAuto", "s", 0.2, 5, 0.33, 1.5),
            new TunableParameter(TunableParameter.GoalGroup, "throwSpeed", "m/s", 3, 25),
            new TunableParameter(TunableParameter.GoalGroup, "halfLength", "s", 60, 600, 120, 600),
            new TunableParameter(TunableParameter.GoalGroup, "halfTimeBreak", "s", 0.5, 30),
            new TunableParameter(TunableParameter.KickGroup, "volleyMaxHeight", "m", 0.6, 1.6, 0.9, 1.2),
            new TunableParameter(TunableParameter.KickGroup, "headerMinHeight", "m", 1.0, 2.0, 1.4, 1.7),
            new TunableParameter(TunableParameter.KickGroup, "headerMaxHeight", "m", 1.8, 3.0, 2.1, 2.5),
            new TunableParameter(TunableParameter.KickGroup, "headerSpeed", "fraction of kickSpeed", 0.2, 1, 0.4, 0.7),
            new TunableParameter(TunableParameter.KickGroup, "headerReach", "m", 0.2, 1.2),
            new TunableParameter(TunableParameter.KickGroup, "volleyError", "rad per m/s", 0, 0.1),
            new TunableParameter(TunableParameter.KickGroup, "headerError", "rad", 0, 0.8),
            new TunableParameter(TunableParameter.ControlGroup, "dribbleSpeedPoor", "fraction of top speed", 0.5, 1, 0.8, 0.9),
            new TunableParameter(TunableParameter.ControlGroup, "dribbleSpeedGood", "fraction of top speed", 0.5, 1, 0.9, 1),
            new TunableParameter(TunableParameter.ControlGroup, "shieldSpeed", "fraction of top speed", 0, 1, 0.3, 0.6),
            new TunableParameter(TunableParameter.ControlGroup, "shieldRange", "m", 0.5, 5, 1.5, 2.5),
            new TunableParameter(TunableParameter.ControlGroup, "shieldHold", "probability", 0, 1, 0.5, 0.75),
            new TunableParameter(TunableParameter.ControlGroup, "slideRange", "m", 1, 6, 2, 3),
            new TunableParameter(TunableParameter.ControlGroup, "slideSpeed", "m/s", 3, 12, 7, 9),
            new TunableParameter(TunableParameter.ControlGroup, "slideTime", "s", 0.1, 1, 0.3, 0.5),
            new TunableParameter(TunableParameter.ControlGroup, "slideReach", "m", 0.3, 1.5, 0.7, 1.1),
            new TunableParameter(TunableParameter.ControlGroup, "slideRecover", "s", 0.2, 3, 0.8, 1.5),
            new TunableParameter(TunableParameter.AttachedGroup, "attachedLeadSlow", "m", 0.2, 1.5, 0.4, 0.7),
            new TunableParameter(TunableParameter.AttachedGroup, "attachedLeadFast", "m", 0.3, 2, 0.7, 1.1),
            new TunableParameter(TunableParameter.AttachedGroup, "attachedAcceleration", "m/s²", 1, 60, 8, 30),
            new TunableParameter(TunableParameter.AttachedGroup, "attachedSpeedCap", "m/s", 0.5, 10, 2, 4),
            new TunableParameter(TunableParameter.AttachedGroup, "attachedDeadband", "m", 0, 0.6, 0.1, 0.3),
            new TunableParameter(TunableParameter.AttachedGroup, "attachedReleaseReach", "m", 0.5, 3, 1.1, 1.7),
            new TunableParameter(TunableParameter.GoalGroup, "goalWidth", "m", 3, 12),
            new TunableParameter(TunableParameter.GoalGroup, "goalHeight", "m", 1.5, 4),
            new TunableParameter(TunableParameter.GoalGroup, "postWidth", "m", 0.05, 0.3, 0.1, 0.12),
            new TunableParameter(TunableParameter.GoalGroup, "frameRestitution", "fraction", 0, 1, 0.55, 0.85),
            new TunableParameter(TunableParameter.KeeperGroup, "keeperDiveSeconds", "s", 0.1, 1.5, 0.45, 0.90),
            new TunableParameter(TunableParameter.KeeperGroup, "keeperRecoverSeconds", "s", 0.1, 3),
            new TunableParameter(TunableParameter.KeeperGroup, "keeperHoldSpeed", "m/s", 0, 50),
            new TunableParameter(TunableParameter.KeeperGroup, "keeperParryRestitution", "fraction", 0, 1),
            new TunableParameter(TunableParameter.KeeperGroup, "keeperParryError", "rad", 0, 1),
        });

        /// <summary>The current value of a tunable, by its name in <see cref="Tunables"/>.</summary>
        public double Get(string name) => name switch
        {
            "gravity" => Gravity,
            "ballRadius" => BallRadius,
            "bounceRestitution" => BounceRestitution,
            "bounceTangentialRetention" => BounceTangentialRetention,
            "bounceSpinRetention" => BounceSpinRetention,
            "settleSpeed" => SettleSpeed,
            "rollingFriction" => RollingFriction,
            "stopSpeed" => StopSpeed,
            "magnusCoefficient" => MagnusCoefficient,
            "airSpinDecay" => AirSpinDecay,
            "groundSpinDecay" => GroundSpinDecay,
            "kickSpeed" => KickSpeed,
            "kickLoft" => KickLoft,
            "kickSideSpin" => KickSideSpin,
            "kickWindUpSeconds" => KickWindUpSeconds,
            "aftertouchSeconds" => AftertouchSeconds,
            "aftertouchBend" => AftertouchBend,
            "aftertouchLift" => AftertouchLift,
            "passMaxRange" => PassMaxRange,
            "passArrivalSpeed" => PassArrivalSpeed,
            "passBackWeight" => PassBackWeight,
            "passFreeDistance" => PassFreeDistance,
            "controlReach" => ControlReach,
            "dribbleLeadSlow" => DribbleLeadSlow,
            "dribbleLeadFast" => DribbleLeadFast,
            "touchInterval" => TouchInterval,
            "touchStrength" => TouchStrength,
            "touchError" => TouchError,
            "receiveStrength" => ReceiveStrength,
            "turnAngle" => TurnAngle,
            "turnStrength" => TurnStrength,
            "cutAngle" => CutAngle,
            "cutStrength" => CutStrength,
            "cutInterval" => CutInterval,
            "pushSpeed" => PushSpeed,
            "pushFromSpeed" => PushFromSpeed,
            "challengeRange" => ChallengeRange,
            "challengeCommit" => ChallengeCommit,
            "challengeRecover" => ChallengeRecover,
            "challengeReach" => ChallengeReach,
            "challengeImpulse" => ChallengeImpulse,
            "touchlineY" => TouchlineY,
            "restartStop" => RestartStop,
            "restartReady" => RestartReady,
            "restartAuto" => RestartAuto,
            "throwSpeed" => ThrowSpeed,
            "halfLength" => HalfLength,
            "halfTimeBreak" => HalfTimeBreak,
            "volleyMaxHeight" => VolleyMaxHeight,
            "headerMinHeight" => HeaderMinHeight,
            "headerMaxHeight" => HeaderMaxHeight,
            "headerSpeed" => HeaderSpeed,
            "headerReach" => HeaderReach,
            "volleyError" => VolleyError,
            "headerError" => HeaderError,
            "dribbleSpeedPoor" => DribbleSpeedPoor,
            "dribbleSpeedGood" => DribbleSpeedGood,
            "shieldSpeed" => ShieldSpeed,
            "shieldRange" => ShieldRange,
            "shieldHold" => ShieldHold,
            "slideRange" => SlideRange,
            "slideSpeed" => SlideSpeed,
            "slideTime" => SlideTime,
            "slideReach" => SlideReach,
            "slideRecover" => SlideRecover,
            "attachedLeadSlow" => AttachedLeadSlow,
            "attachedLeadFast" => AttachedLeadFast,
            "attachedAcceleration" => AttachedAcceleration,
            "attachedSpeedCap" => AttachedSpeedCap,
            "attachedDeadband" => AttachedDeadband,
            "attachedReleaseReach" => AttachedReleaseReach,
            "goalWidth" => GoalWidth,
            "goalHeight" => GoalHeight,
            "postWidth" => PostWidth,
            "frameRestitution" => FrameRestitution,
            "keeperDiveSeconds" => KeeperDiveSeconds,
            "keeperRecoverSeconds" => KeeperRecoverSeconds,
            "keeperHoldSpeed" => KeeperHoldSpeed,
            "keeperParryRestitution" => KeeperParryRestitution,
            "keeperParryError" => KeeperParryError,
            _ => throw new ArgumentException($"'{name}' is not a tunable config value.", nameof(name)),
        };

        /// <summary>This config with one tunable changed. Fails on unknown names and out-of-range values.</summary>
        public SimConfig With(string name, double value) =>
            With(new[] { new KeyValuePair<string, double>(name, value) });

        /// <summary>This config with several tunables changed, applied in order.</summary>
        public SimConfig With(IEnumerable<KeyValuePair<string, double>> overrides)
        {
            double gravity = Gravity, ballRadius = BallRadius, bounceRestitution = BounceRestitution;
            double bounceTangentialRetention = BounceTangentialRetention, settleSpeed = SettleSpeed;
            double bounceSpinRetention = BounceSpinRetention;
            double rollingFriction = RollingFriction, stopSpeed = StopSpeed, magnusCoefficient = MagnusCoefficient;
            double airSpinDecay = AirSpinDecay, groundSpinDecay = GroundSpinDecay;
            double kickSpeed = KickSpeed, kickLoft = KickLoft, kickSideSpin = KickSideSpin;
            double kickWindUpSeconds = KickWindUpSeconds, aftertouchSeconds = AftertouchSeconds;
            double aftertouchBend = AftertouchBend, aftertouchLift = AftertouchLift;
            double passMaxRange = PassMaxRange, passArrivalSpeed = PassArrivalSpeed;
            double passBackWeight = PassBackWeight, passFreeDistance = PassFreeDistance;
            double controlReach = ControlReach, dribbleLeadSlow = DribbleLeadSlow, dribbleLeadFast = DribbleLeadFast;
            double touchInterval = TouchInterval, touchStrength = TouchStrength, touchError = TouchError;
            double receiveStrength = ReceiveStrength;
            double turnAngle = TurnAngle, turnStrength = TurnStrength;
            double cutAngle = CutAngle, cutStrength = CutStrength, cutInterval = CutInterval;
            double pushSpeed = PushSpeed, pushFromSpeed = PushFromSpeed;
            double challengeRange = ChallengeRange, challengeCommit = ChallengeCommit, challengeRecover = ChallengeRecover, challengeReach = ChallengeReach, challengeImpulse = ChallengeImpulse;
            double touchlineY = TouchlineY, restartStop = RestartStop, restartReady = RestartReady, restartAuto = RestartAuto, throwSpeed = ThrowSpeed;
            double halfLength = HalfLength, halfTimeBreak = HalfTimeBreak;
            double volleyMaxHeight = VolleyMaxHeight, headerMinHeight = HeaderMinHeight, headerMaxHeight = HeaderMaxHeight, headerSpeed = HeaderSpeed, headerReach = HeaderReach, volleyError = VolleyError, headerError = HeaderError;
            double dribbleSpeedPoor = DribbleSpeedPoor, dribbleSpeedGood = DribbleSpeedGood;
            double shieldSpeed = ShieldSpeed, shieldRange = ShieldRange, shieldHold = ShieldHold;
            double slideRange = SlideRange, slideSpeed = SlideSpeed, slideTime = SlideTime, slideReach = SlideReach, slideRecover = SlideRecover;
            double attachedLeadSlow = AttachedLeadSlow, attachedLeadFast = AttachedLeadFast;
            double attachedAcceleration = AttachedAcceleration, attachedSpeedCap = AttachedSpeedCap;
            double attachedDeadband = AttachedDeadband, attachedReleaseReach = AttachedReleaseReach;
            double goalWidth = GoalWidth, goalHeight = GoalHeight, postWidth = PostWidth, frameRestitution = FrameRestitution;
            double keeperDiveSeconds = KeeperDiveSeconds, keeperRecoverSeconds = KeeperRecoverSeconds, keeperHoldSpeed = KeeperHoldSpeed;
            double keeperParryRestitution = KeeperParryRestitution, keeperParryError = KeeperParryError;

            foreach (KeyValuePair<string, double> o in overrides)
            {
                switch (o.Key)
                {
                    case "gravity": gravity = o.Value; break;
                    case "ballRadius": ballRadius = o.Value; break;
                    case "bounceRestitution": bounceRestitution = o.Value; break;
                    case "bounceTangentialRetention": bounceTangentialRetention = o.Value; break;
                    case "bounceSpinRetention": bounceSpinRetention = o.Value; break;
                    case "settleSpeed": settleSpeed = o.Value; break;
                    case "rollingFriction": rollingFriction = o.Value; break;
                    case "stopSpeed": stopSpeed = o.Value; break;
                    case "magnusCoefficient": magnusCoefficient = o.Value; break;
                    case "airSpinDecay": airSpinDecay = o.Value; break;
                    case "groundSpinDecay": groundSpinDecay = o.Value; break;
                    case "kickSpeed": kickSpeed = o.Value; break;
                    case "kickLoft": kickLoft = o.Value; break;
                    case "kickSideSpin": kickSideSpin = o.Value; break;
                    case "kickWindUpSeconds": kickWindUpSeconds = o.Value; break;
                    case "aftertouchSeconds": aftertouchSeconds = o.Value; break;
                    case "aftertouchBend": aftertouchBend = o.Value; break;
                    case "aftertouchLift": aftertouchLift = o.Value; break;
                    case "passMaxRange": passMaxRange = o.Value; break;
                    case "passArrivalSpeed": passArrivalSpeed = o.Value; break;
                    case "passBackWeight": passBackWeight = o.Value; break;
                    case "passFreeDistance": passFreeDistance = o.Value; break;
                    case "controlReach": controlReach = o.Value; break;
                    case "dribbleLeadSlow": dribbleLeadSlow = o.Value; break;
                    case "dribbleLeadFast": dribbleLeadFast = o.Value; break;
                    case "touchInterval": touchInterval = o.Value; break;
                    case "touchStrength": touchStrength = o.Value; break;
                    case "touchError": touchError = o.Value; break;
                    case "receiveStrength": receiveStrength = o.Value; break;
                    case "turnAngle": turnAngle = o.Value; break;
                    case "turnStrength": turnStrength = o.Value; break;
                    case "cutAngle": cutAngle = o.Value; break;
                    case "cutStrength": cutStrength = o.Value; break;
                    case "cutInterval": cutInterval = o.Value; break;
                    case "pushSpeed": pushSpeed = o.Value; break;
                    case "pushFromSpeed": pushFromSpeed = o.Value; break;
                    case "challengeRange": challengeRange = o.Value; break;
                    case "challengeCommit": challengeCommit = o.Value; break;
                    case "challengeRecover": challengeRecover = o.Value; break;
                    case "challengeReach": challengeReach = o.Value; break;
                    case "challengeImpulse": challengeImpulse = o.Value; break;
                    case "touchlineY": touchlineY = o.Value; break;
                    case "restartStop": restartStop = o.Value; break;
                    case "restartReady": restartReady = o.Value; break;
                    case "restartAuto": restartAuto = o.Value; break;
                    case "throwSpeed": throwSpeed = o.Value; break;
                    case "halfLength": halfLength = o.Value; break;
                    case "halfTimeBreak": halfTimeBreak = o.Value; break;
                    case "volleyMaxHeight": volleyMaxHeight = o.Value; break;
                    case "headerMinHeight": headerMinHeight = o.Value; break;
                    case "headerMaxHeight": headerMaxHeight = o.Value; break;
                    case "headerSpeed": headerSpeed = o.Value; break;
                    case "headerReach": headerReach = o.Value; break;
                    case "volleyError": volleyError = o.Value; break;
                    case "headerError": headerError = o.Value; break;
                    case "dribbleSpeedPoor": dribbleSpeedPoor = o.Value; break;
                    case "dribbleSpeedGood": dribbleSpeedGood = o.Value; break;
                    case "shieldSpeed": shieldSpeed = o.Value; break;
                    case "shieldRange": shieldRange = o.Value; break;
                    case "shieldHold": shieldHold = o.Value; break;
                    case "slideRange": slideRange = o.Value; break;
                    case "slideSpeed": slideSpeed = o.Value; break;
                    case "slideTime": slideTime = o.Value; break;
                    case "slideReach": slideReach = o.Value; break;
                    case "slideRecover": slideRecover = o.Value; break;
                    case "attachedLeadSlow": attachedLeadSlow = o.Value; break;
                    case "attachedLeadFast": attachedLeadFast = o.Value; break;
                    case "attachedAcceleration": attachedAcceleration = o.Value; break;
                    case "attachedSpeedCap": attachedSpeedCap = o.Value; break;
                    case "attachedDeadband": attachedDeadband = o.Value; break;
                    case "attachedReleaseReach": attachedReleaseReach = o.Value; break;
                    case "goalWidth": goalWidth = o.Value; break;
                    case "goalHeight": goalHeight = o.Value; break;
                    case "postWidth": postWidth = o.Value; break;
                    case "frameRestitution": frameRestitution = o.Value; break;
                    case "keeperDiveSeconds": keeperDiveSeconds = o.Value; break;
                    case "keeperRecoverSeconds": keeperRecoverSeconds = o.Value; break;
                    case "keeperHoldSpeed": keeperHoldSpeed = o.Value; break;
                    case "keeperParryRestitution": keeperParryRestitution = o.Value; break;
                    case "keeperParryError": keeperParryError = o.Value; break;
                    default: throw new ArgumentException($"'{o.Key}' is not a tunable config value.", nameof(overrides));
                }
            }

            return new SimConfig(
                tickRate: TickRate,
                gravity: gravity,
                ballRadius: ballRadius,
                bounceRestitution: bounceRestitution,
                bounceTangentialRetention: bounceTangentialRetention,
                bounceSpinRetention: bounceSpinRetention,
                settleSpeed: settleSpeed,
                rollingFriction: rollingFriction,
                stopSpeed: stopSpeed,
                magnusCoefficient: magnusCoefficient,
                airSpinDecay: airSpinDecay,
                groundSpinDecay: groundSpinDecay,
                kickReach: KickReach,
                kickMaxBallHeight: KickMaxBallHeight,
                kickSpeed: kickSpeed,
                kickLoft: kickLoft,
                kickSideSpin: kickSideSpin,
                kickWindUpSeconds: kickWindUpSeconds,
                aftertouchSeconds: aftertouchSeconds,
                aftertouchBend: aftertouchBend,
                aftertouchLift: aftertouchLift,
                passMaxRange: passMaxRange,
                passArrivalSpeed: passArrivalSpeed,
                passBackWeight: passBackWeight,
                passFreeDistance: passFreeDistance,
                kickCooldownTicks: KickCooldownTicks,
                goalLineX: GoalLineX,
                goalWidth: goalWidth,
                goalHeight: goalHeight,
                postWidth: postWidth,
                frameRestitution: frameRestitution,
                keeperDiveSeconds: keeperDiveSeconds,
                keeperRecoverSeconds: keeperRecoverSeconds,
                keeperHoldSpeed: keeperHoldSpeed,
                keeperParryRestitution: keeperParryRestitution,
                keeperParryError: keeperParryError,
                controlModel: ControlModel,
                controlReach: controlReach,
                dribbleLeadSlow: dribbleLeadSlow,
                dribbleLeadFast: dribbleLeadFast,
                touchInterval: touchInterval,
                touchStrength: touchStrength,
                touchError: touchError,
                receiveStrength: receiveStrength,
                attachedLeadSlow: attachedLeadSlow,
                attachedLeadFast: attachedLeadFast,
                attachedAcceleration: attachedAcceleration,
                attachedSpeedCap: attachedSpeedCap,
                attachedDeadband: attachedDeadband,
                attachedReleaseReach: attachedReleaseReach,
                turnAngle: turnAngle,
                turnStrength: turnStrength,
                cutAngle: cutAngle,
                cutStrength: cutStrength,
                cutInterval: cutInterval,
                pushSpeed: pushSpeed,
                pushFromSpeed: pushFromSpeed,
                challengeRange: challengeRange,
                challengeCommit: challengeCommit,
                challengeRecover: challengeRecover,
                challengeReach: challengeReach,
                challengeImpulse: challengeImpulse,
                touchlineY: touchlineY,
                restartStop: restartStop,
                restartReady: restartReady,
                restartAuto: restartAuto,
                throwSpeed: throwSpeed,
                halfLength: halfLength,
                halfTimeBreak: halfTimeBreak,
                volleyMaxHeight: volleyMaxHeight,
                headerMinHeight: headerMinHeight,
                headerMaxHeight: headerMaxHeight,
                headerSpeed: headerSpeed,
                headerReach: headerReach,
                volleyError: volleyError,
                headerError: headerError,
                dribbleSpeedPoor: dribbleSpeedPoor,
                dribbleSpeedGood: dribbleSpeedGood,
                shieldSpeed: shieldSpeed,
                shieldRange: shieldRange,
                shieldHold: shieldHold,
                slideRange: slideRange,
                slideSpeed: slideSpeed,
                slideTime: slideTime,
                slideReach: slideReach,
                slideRecover: slideRecover);
        }

        /// <summary>This config under another ball control model (#59, #60), every other value kept.</summary>
        public SimConfig WithControlModel(ControlModel model) => new SimConfig(
            tickRate: TickRate,
            gravity: Gravity,
            ballRadius: BallRadius,
            bounceRestitution: BounceRestitution,
            bounceTangentialRetention: BounceTangentialRetention,
            bounceSpinRetention: BounceSpinRetention,
            settleSpeed: SettleSpeed,
            rollingFriction: RollingFriction,
            stopSpeed: StopSpeed,
            magnusCoefficient: MagnusCoefficient,
            airSpinDecay: AirSpinDecay,
            groundSpinDecay: GroundSpinDecay,
            kickReach: KickReach,
            kickMaxBallHeight: KickMaxBallHeight,
            kickSpeed: KickSpeed,
            kickLoft: KickLoft,
            kickSideSpin: KickSideSpin,
            kickWindUpSeconds: KickWindUpSeconds,
            aftertouchSeconds: AftertouchSeconds,
            aftertouchBend: AftertouchBend,
            aftertouchLift: AftertouchLift,
            passMaxRange: PassMaxRange,
            passArrivalSpeed: PassArrivalSpeed,
            passBackWeight: PassBackWeight,
            passFreeDistance: PassFreeDistance,
            kickCooldownTicks: KickCooldownTicks,
            goalLineX: GoalLineX,
            goalWidth: GoalWidth,
            goalHeight: GoalHeight,
            postWidth: PostWidth,
            frameRestitution: FrameRestitution,
            keeperDiveSeconds: KeeperDiveSeconds,
            keeperRecoverSeconds: KeeperRecoverSeconds,
            keeperHoldSpeed: KeeperHoldSpeed,
            keeperParryRestitution: KeeperParryRestitution,
            keeperParryError: KeeperParryError,
            controlModel: model,
            controlReach: ControlReach,
            dribbleLeadSlow: DribbleLeadSlow,
            dribbleLeadFast: DribbleLeadFast,
            touchInterval: TouchInterval,
            touchStrength: TouchStrength,
            touchError: TouchError,
            receiveStrength: ReceiveStrength,
            attachedLeadSlow: AttachedLeadSlow,
            attachedLeadFast: AttachedLeadFast,
            attachedAcceleration: AttachedAcceleration,
            attachedSpeedCap: AttachedSpeedCap,
            attachedDeadband: AttachedDeadband,
            attachedReleaseReach: AttachedReleaseReach,
            turnAngle: TurnAngle,
            turnStrength: TurnStrength,
            cutAngle: CutAngle,
            cutStrength: CutStrength,
            cutInterval: CutInterval,
            pushSpeed: PushSpeed,
            pushFromSpeed: PushFromSpeed,
            challengeRange: ChallengeRange,
            challengeCommit: ChallengeCommit,
            challengeRecover: ChallengeRecover,
            challengeReach: ChallengeReach,
            challengeImpulse: ChallengeImpulse,
            touchlineY: TouchlineY,
            restartStop: RestartStop,
            restartReady: RestartReady,
            restartAuto: RestartAuto,
            throwSpeed: ThrowSpeed,
            halfLength: HalfLength,
            halfTimeBreak: HalfTimeBreak,
            volleyMaxHeight: VolleyMaxHeight,
            headerMinHeight: HeaderMinHeight,
            headerMaxHeight: HeaderMaxHeight,
            headerSpeed: HeaderSpeed,
            headerReach: HeaderReach,
            volleyError: VolleyError,
            headerError: HeaderError,
            dribbleSpeedPoor: DribbleSpeedPoor,
            dribbleSpeedGood: DribbleSpeedGood,
            shieldSpeed: ShieldSpeed,
            shieldRange: ShieldRange,
            shieldHold: ShieldHold,
            slideRange: SlideRange,
            slideSpeed: SlideSpeed,
            slideTime: SlideTime,
            slideReach: SlideReach,
            slideRecover: SlideRecover);

        // NaN passes these comparisons on purpose: the encoding refuses non-finite values with its
        // own error, as it always has.
        private void CheckTunableRanges()
        {
            foreach (TunableParameter p in Tunables)
            {
                double value = Get(p.Name);
                if (value < p.Min || value > p.Max)
                {
                    throw new ArgumentOutOfRangeException(p.Name, value, $"{p.Name} must be within [{p.Min}, {p.Max}] {p.Unit}.");
                }
            }
        }
    }
}
