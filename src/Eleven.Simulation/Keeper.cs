using System;
using System.Collections.ObjectModel;

namespace Eleven.Simulation
{
    /// <summary>
    /// A keeper's phase (#69): explicit, authoritative and timed by the simulation (INV-12), so his
    /// brain can ask but never skip one. docs/simulation/keeper.md. Numeric values are stable.
    /// </summary>
    public enum KeeperPhase
    {
        /// <summary>On his feet: he moves where his brain steers him, and can dive.</summary>
        Position = 0,

        /// <summary>A kick has connected: for his reaction time he sets, braking to a stop, and can't dive.</summary>
        React = 1,

        /// <summary>Launched toward his dive's target at his dive speed, for the time the distance needs.</summary>
        Dive = 2,

        /// <summary>On the ground after a dive: no movement, no dive.</summary>
        Recover = 3,
    }

    /// <summary>
    /// What one keeper can do (#69): the smallest internal inputs that vary his perception,
    /// reach and handling, explicitly not the final goalkeeper attribute taxonomy. His movement on
    /// his feet is his player's <see cref="PlayerCapabilities"/>. The simulation reads these and
    /// never changes them; it refuses values outside <see cref="Parameters"/>.
    /// </summary>
    public struct KeeperCapabilities
    {
        /// <summary>s. From the step a kick connects until he can dive: his perception's delay.</summary>
        public double ReactionTime;

        /// <summary>m/s. How fast his dive carries his body toward its target.</summary>
        public double DiveSpeed;

        /// <summary>m. His hands' reach from his body: the radius of his envelope.</summary>
        public double Reach;

        /// <summary>0–1. How well he holds what he reaches: more holds harder shots and parries more accurately.</summary>
        public double Handling;

        public const string Group = "Keeper";

        /// <summary>
        /// The capability values, with their valid ranges and, where #30's research gives one, a
        /// starting range (docs/simulation/keeper.md). Neither is a feel target.
        /// </summary>
        public static readonly ReadOnlyCollection<TunableParameter> Parameters = Array.AsReadOnly(new[]
        {
            new TunableParameter(Group, "reactionTime", "s", 0.05, 0.6, 0.15, 0.30),
            new TunableParameter(Group, "diveSpeed", "m/s", 1, 8, 2.5, 5.0),
            new TunableParameter(Group, "reach", "m", 0.3, 1.5),
            new TunableParameter(Group, "handling", "fraction", 0, 1),
        });

        /// <summary>The technical reference keeper. Not approved feel.</summary>
        public static KeeperCapabilities Reference => new KeeperCapabilities { ReactionTime = 0.20, DiveSpeed = 3.5, Reach = 0.8, Handling = 0.7 };

        /// <summary>A slower, less capable keeper, for contrast.</summary>
        public static KeeperCapabilities Slow => new KeeperCapabilities { ReactionTime = 0.30, DiveSpeed = 2.5, Reach = 0.6, Handling = 0.4 };

        /// <summary>A capability value by its name in <see cref="Parameters"/>.</summary>
        public double Get(string name) => name switch
        {
            "reactionTime" => ReactionTime,
            "diveSpeed" => DiveSpeed,
            "reach" => Reach,
            "handling" => Handling,
            _ => throw new ArgumentException($"'{name}' is not a keeper capability.", nameof(name)),
        };

        /// <summary>A copy with one value changed. Fails on unknown names; ranges are checked where the keeper is used.</summary>
        public KeeperCapabilities With(string name, double value)
        {
            KeeperCapabilities copy = this;
            switch (name)
            {
                case "reactionTime": copy.ReactionTime = value; break;
                case "diveSpeed": copy.DiveSpeed = value; break;
                case "reach": copy.Reach = value; break;
                case "handling": copy.Handling = value; break;
                default: throw new ArgumentException($"'{name}' is not a keeper capability.", nameof(name));
            }

            return copy;
        }

        internal void CheckRanges(string owner)
        {
            foreach (TunableParameter p in Parameters)
            {
                double value = Get(p.Name);
                if (value < p.Min || value > p.Max)
                {
                    throw new ArgumentOutOfRangeException(p.Name, value, $"{owner}: {p.Name} must be within [{p.Min}, {p.Max}] {p.Unit}.");
                }
            }
        }
    }

    /// <summary>
    /// A keeper (#69): the player <see cref="PlayerId"/>, owned by an AI controller, with his phase,
    /// its timer, his dive and his capabilities. Plain data (INV-3).
    /// </summary>
    public struct KeeperState
    {
        /// <summary>The <see cref="PlayerState"/> who is the keeper.</summary>
        public int PlayerId;

        public KeeperPhase Phase;

        /// <summary>Steps left in <see cref="Phase"/>, counting the step that ends it; 0 in <see cref="KeeperPhase.Position"/>.</summary>
        public int PhaseTicks;

        /// <summary>m/s. His body's velocity through a dive, fixed when it starts.</summary>
        public Vec2 DiveVelocity;

        /// <summary>m. The top of his envelope's axis through a dive: the height he dives at.</summary>
        public double DiveTop;

        public KeeperCapabilities Capabilities;
    }
}
