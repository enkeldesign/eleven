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
        internal TunableParameter(string group, string name, string unit, double min, double max, double? startMin = null, double? startMax = null)
        {
            Group = "";
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
        /// <see cref="KickGroup"/>, <see cref="PassGroup"/> or <see cref="PlayerGroup"/>. Metadata only;
        /// no config value or hash depends on it.
        /// </summary>
        public string Group { get; }

        public const string BallGroup = "Ball";
        public const string KickGroup = "Kick";
        public const string PassGroup = "Pass";
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
    /// The ball-, kick- and pass-feel surface (#24, #37, #38): the supported way for tools, presets and the later
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
                goalLineX: GoalLineX);
        }

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
