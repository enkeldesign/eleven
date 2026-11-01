using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Eleven.Simulation
{
    /// <summary>
    /// A config value tools may vary: its unit, its valid range (inclusive), and, where the
    /// retro-football research gives one, the starting range a feel test bench should explore.
    /// </summary>
    public sealed class TunableParameter
    {
        internal TunableParameter(string name, string unit, double min, double max, double? startMin = null, double? startMax = null)
        {
            Name = name;
            Unit = unit;
            Min = min;
            Max = max;
            StartMin = startMin;
            StartMax = startMax;
        }

        /// <summary>The <see cref="SimConfig"/> constructor parameter it sets, e.g. <c>kickSpeed</c>.</summary>
        public string Name { get; }

        public string Unit { get; }

        /// <summary>Lowest valid value. The constructor refuses anything outside [Min, Max].</summary>
        public double Min { get; }

        public double Max { get; }

        /// <summary>
        /// The research-derived starting range (docs/simulation/ball.md), or null where the research
        /// gives none. A starting point for play, to be narrowed by playable evidence, not a target.
        /// </summary>
        public double? StartMin { get; }

        public double? StartMax { get; }
    }

    /// <summary>
    /// The ball-feel surface (#24): the supported way for tools, presets and the later tuning panel
    /// to read and vary config. Values change only by building a new config with <see cref="With(string, double)"/>,
    /// so every variant is validated and hashed like any other config.
    /// </summary>
    public sealed partial class SimConfig
    {
        /// <summary>
        /// The tunable values. The valid ranges keep the ball physical and the integration well
        /// behaved (restitution and retention at most 1, decay at most one tick's worth). The
        /// starting ranges come from the retro-football research and football measurements in
        /// docs/simulation/ball.md. Neither is a feel target.
        /// </summary>
        public static readonly ReadOnlyCollection<TunableParameter> Tunables = Array.AsReadOnly(new[]
        {
            new TunableParameter("gravity", "m/s²", 0.1, 50),
            new TunableParameter("ballRadius", "m", 0.05, 0.5),
            new TunableParameter("bounceRestitution", "fraction", 0, 1, 0.5, 0.75),
            new TunableParameter("bounceTangentialRetention", "fraction", 0, 1, 0.45, 0.85),
            new TunableParameter("settleSpeed", "m/s", 0, 5),
            new TunableParameter("rollingFriction", "fraction of g", 0, 2, 0.05, 0.3),
            new TunableParameter("stopSpeed", "m/s", 0, 2),
            new TunableParameter("magnusCoefficient", "1/rad", 0, 0.05, 0.004, 0.008),
            new TunableParameter("airSpinDecay", "1/s", 0, 60),
            new TunableParameter("groundSpinDecay", "1/s", 0, 60),
            new TunableParameter("kickSpeed", "m/s", 0, 60, 15, 32),
            new TunableParameter("kickLoft", "rad", 0, 1.5, 0, 1.05),
            new TunableParameter("kickSideSpin", "rad/s", 0, 100, 0, 40),
        });

        /// <summary>The current value of a tunable, by its name in <see cref="Tunables"/>.</summary>
        public double Get(string name) => name switch
        {
            "gravity" => Gravity,
            "ballRadius" => BallRadius,
            "bounceRestitution" => BounceRestitution,
            "bounceTangentialRetention" => BounceTangentialRetention,
            "settleSpeed" => SettleSpeed,
            "rollingFriction" => RollingFriction,
            "stopSpeed" => StopSpeed,
            "magnusCoefficient" => MagnusCoefficient,
            "airSpinDecay" => AirSpinDecay,
            "groundSpinDecay" => GroundSpinDecay,
            "kickSpeed" => KickSpeed,
            "kickLoft" => KickLoft,
            "kickSideSpin" => KickSideSpin,
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
            double rollingFriction = RollingFriction, stopSpeed = StopSpeed, magnusCoefficient = MagnusCoefficient;
            double airSpinDecay = AirSpinDecay, groundSpinDecay = GroundSpinDecay;
            double kickSpeed = KickSpeed, kickLoft = KickLoft, kickSideSpin = KickSideSpin;

            foreach (KeyValuePair<string, double> o in overrides)
            {
                switch (o.Key)
                {
                    case "gravity": gravity = o.Value; break;
                    case "ballRadius": ballRadius = o.Value; break;
                    case "bounceRestitution": bounceRestitution = o.Value; break;
                    case "bounceTangentialRetention": bounceTangentialRetention = o.Value; break;
                    case "settleSpeed": settleSpeed = o.Value; break;
                    case "rollingFriction": rollingFriction = o.Value; break;
                    case "stopSpeed": stopSpeed = o.Value; break;
                    case "magnusCoefficient": magnusCoefficient = o.Value; break;
                    case "airSpinDecay": airSpinDecay = o.Value; break;
                    case "groundSpinDecay": groundSpinDecay = o.Value; break;
                    case "kickSpeed": kickSpeed = o.Value; break;
                    case "kickLoft": kickLoft = o.Value; break;
                    case "kickSideSpin": kickSideSpin = o.Value; break;
                    default: throw new ArgumentException($"'{o.Key}' is not a tunable config value.", nameof(overrides));
                }
            }

            return new SimConfig(
                tickRate: TickRate,
                gravity: gravity,
                ballRadius: ballRadius,
                bounceRestitution: bounceRestitution,
                bounceTangentialRetention: bounceTangentialRetention,
                settleSpeed: settleSpeed,
                rollingFriction: rollingFriction,
                stopSpeed: stopSpeed,
                magnusCoefficient: magnusCoefficient,
                airSpinDecay: airSpinDecay,
                groundSpinDecay: groundSpinDecay,
                dotMaxSpeed: DotMaxSpeed,
                dotAcceleration: DotAcceleration,
                kickReach: KickReach,
                kickMaxBallHeight: KickMaxBallHeight,
                kickSpeed: kickSpeed,
                kickLoft: kickLoft,
                kickSideSpin: kickSideSpin,
                kickDirectionError: KickDirectionError,
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
