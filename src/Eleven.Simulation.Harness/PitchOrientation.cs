// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// How the pitch is shown (#72): which way along the goal-to-goal axis, world +x or −x, is up the
    /// screen. The controlled team's attacked goal is always at the top, so this follows the end it
    /// attacks: +x in practice, and in a match whatever its authoritative end switch says (INV-8).
    ///
    /// Presentation and input mapping only. It is in no simulation state, config or hash: the camera
    /// draws the world through it, and the touch adapter maps screen directions to world ones through
    /// it before a command is made (INV-10).
    ///
    /// Each mapping is a rotation by ±90°, with no scaling or mirroring, so screen left and right
    /// stay left and right and a curl keeps its hand. It only swaps components and subtracts from 0,
    /// so it is exact in every runtime, round-trips bit for bit, and never makes a −0.
    /// docs/ui/visual-system.md has the contract.
    /// </summary>
    public readonly struct PitchOrientation : IEquatable<PitchOrientation>
    {
        // The default, false, is +x up: practice.
        private readonly bool _minusXUp;

        private PitchOrientation(bool minusXUp)
        {
            _minusXUp = minusXUp;
        }

        /// <summary>Practice (#68): the controlled team attacks the one goal, at +x.</summary>
        public static PitchOrientation Practice => default;

        /// <summary>
        /// The view for a controlled team attacking along world x in direction <paramref name="sign"/>:
        /// +1 toward +x, −1 toward −x. Their attacked goal is up the screen.
        /// </summary>
        public static PitchOrientation Attacking(int sign) => new PitchOrientation(sign < 0);

        /// <summary>+1 when world +x is up the screen, −1 when world −x is.</summary>
        public int Up => _minusXUp ? -1 : 1;

        /// <summary>0 for +x up, 180 for −x up: the half-time flip turns the view by half a turn.</summary>
        public int Degrees => _minusXUp ? 180 : 0;

        /// <summary>The other end up: what a half-time end switch shows.</summary>
        public PitchOrientation Flipped => new PitchOrientation(!_minusXUp);

        /// <summary>
        /// Degrees to roll the camera about its view axis so that the attacked end is up the screen.
        /// </summary>
        public double CameraRoll => 0;

        /// <summary>A world vector as the screen shows it: +x right, +y up the screen.</summary>
        public Vec2 ToScreen(Vec2 world) => world;

        /// <summary>A screen vector, +x right and +y up the screen, as the world direction it shows.</summary>
        public Vec2 ToWorld(Vec2 screen) => screen;

        public bool Equals(PitchOrientation other) => _minusXUp == other._minusXUp;

        public override bool Equals(object? obj) => obj is PitchOrientation other && Equals(other);

        public override int GetHashCode() => _minusXUp ? 1 : 0;

        public static bool operator ==(PitchOrientation a, PitchOrientation b) => a.Equals(b);

        public static bool operator !=(PitchOrientation a, PitchOrientation b) => !a.Equals(b);

        /// <summary>For logs: <c>0</c> or <c>180</c>.</summary>
        public override string ToString() => _minusXUp ? "180" : "0";
    }
}
