using System;
using System.Collections.ObjectModel;

namespace Eleven.Simulation
{
    /// <summary>
    /// What one footballer can do (#26): the smallest per-player capability surface the current
    /// mechanics need, explicitly not the final attribute taxonomy. #36 adds movement; later
    /// mechanics add only what they need.
    ///
    /// It lives in <see cref="PlayerState"/>, not in <see cref="SimConfig"/>, so every player can
    /// differ and snapshots, hashes and replays carry it. The simulation reads it and never changes
    /// it. <see cref="Simulator"/> refuses values outside <see cref="Parameters"/>.
    /// </summary>
    public struct PlayerCapabilities
    {
        /// <summary>m/s at full stick.</summary>
        public double MaxSpeed;

        /// <summary>m/s². How fast the player's velocity can change: speeding up, braking and turning alike.</summary>
        public double Acceleration;

        /// <summary>
        /// The capability values, with their valid ranges and the starting ranges from the
        /// research on #26 (docs/simulation/movement.md). The valid ranges are wide enough for the
        /// test bench to explore past the starting ranges; neither is a feel target.
        /// </summary>
        public static readonly ReadOnlyCollection<TunableParameter> Parameters = Array.AsReadOnly(new[]
        {
            new TunableParameter("maxSpeed", "m/s", 1, 15, 7, 10),
            new TunableParameter("acceleration", "m/s²", 1, 100, 12, 30),
        });

        /// <summary>
        /// The technical reference profile: the spike's movement values, inside the starting
        /// ranges. Not approved feel; the test bench (#27) is where Erik judges it.
        /// </summary>
        public static PlayerCapabilities Reference => new PlayerCapabilities { MaxSpeed = 7, Acceleration = 25 };

        /// <summary>A capability value by its name in <see cref="Parameters"/>.</summary>
        public double Get(string name) => name switch
        {
            "maxSpeed" => MaxSpeed,
            "acceleration" => Acceleration,
            _ => throw new ArgumentException($"'{name}' is not a player capability.", nameof(name)),
        };

        /// <summary>A copy with one value changed. Fails on unknown names; ranges are checked where the player is used.</summary>
        public PlayerCapabilities With(string name, double value)
        {
            PlayerCapabilities copy = this;
            switch (name)
            {
                case "maxSpeed": copy.MaxSpeed = value; break;
                case "acceleration": copy.Acceleration = value; break;
                default: throw new ArgumentException($"'{name}' is not a player capability.", nameof(name));
            }

            return copy;
        }

        /// <summary>
        /// Throws if a value is outside its valid range. NaN passes on purpose: the state encoding
        /// refuses non-finite values with its own error, as it does for every other field.
        /// </summary>
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
}
