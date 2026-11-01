namespace Eleven.Simulation
{
    /// <summary>
    /// An owned command (INV-10): one controller's intent for one tick, as recorded from an input
    /// adapter before any assistance.
    ///
    /// Rules, applied by <see cref="Simulator.Step"/>:
    /// <list type="bullet">
    /// <item>A command applies to the player whose <see cref="PlayerState.OwnerControllerId"/> equals its
    /// <see cref="ControllerId"/>. With no such player it is rejected as <see cref="CommandRejection.Unowned"/>.</item>
    /// <item>A command applies only at its <see cref="Tick"/>. Passed to a step after its tick it is
    /// rejected as <see cref="CommandRejection.Stale"/>; before its tick, as <see cref="CommandRejection.Early"/>.</item>
    /// <item>More than one command for the same controller at the same tick is a recording error:
    /// all of them are rejected as <see cref="CommandRejection.Duplicate"/>, so the result never
    /// depends on submission order.</item>
    /// <item>A non-finite value anywhere (move, aftertouch, or any part of the kick intent) is
    /// rejected as <see cref="CommandRejection.Invalid"/>. A move or aftertouch vector longer than 1 is
    /// scaled to length 1, and a kick's strength and loft are clamped to [0, 1].</item>
    /// <item>Accepted commands apply in ascending controller id, whatever order they were passed in.</item>
    /// <item>A player with no accepted command this tick behaves as if given a neutral command
    /// (no move, no kick, no aftertouch).</item>
    /// </list>
    /// Every rejection emits a <see cref="SimEventKind.CommandRejected"/> event.
    /// </summary>
    public readonly struct Command
    {
        public readonly int ControllerId;
        public readonly long Tick;
        public readonly Vec2 Move;

        /// <summary>A manual kick asked for this tick, or null. See <see cref="KickIntent"/>.</summary>
        public readonly KickIntent? Kick;

        /// <summary>
        /// Shaping input for the player's last kick (#37), length at most 1, on the pitch plane. It
        /// acts only while that kick's aftertouch window is open; otherwise it is ignored. Separate
        /// from <see cref="Move"/>, so the gesture mapping decides whether the stick doubles as it.
        /// </summary>
        public readonly Vec2 Aftertouch;

        public Command(int controllerId, long tick, Vec2 move, KickIntent? kick = null, Vec2 aftertouch = default)
        {
            ControllerId = controllerId;
            Tick = tick;
            Move = move;
            Kick = kick;
            Aftertouch = aftertouch;
        }
    }

    /// <summary>
    /// A generic manual kick (#37): where, how hard and how high. It names no action: a pass, a
    /// shot or a cross is a consequence of these values and the situation (#26).
    ///
    /// The simulation keeps the direction as given. It never retargets it; the kicker's own
    /// execution error is the only deviation, and it is bounded (<see cref="PlayerCapabilities.KickError"/>).
    /// </summary>
    public readonly struct KickIntent
    {
        /// <summary>
        /// The intended line on the pitch plane. Only its direction counts; exactly zero means the
        /// player's heading when the intent arrives.
        /// </summary>
        public readonly Vec2 Direction;

        /// <summary>0–1: the fraction of the player's kick power.</summary>
        public readonly double Strength;

        /// <summary>0–1: the fraction of the config's maximum loft, <see cref="SimConfig.KickLoft"/>.</summary>
        public readonly double Loft;

        public KickIntent(Vec2 direction, double strength, double loft)
        {
            Direction = direction;
            Strength = strength;
            Loft = loft;
        }

        /// <summary>Along the player's heading.</summary>
        public static KickIntent AlongHeading(double strength, double loft) => new KickIntent(Vec2.Zero, strength, loft);
    }

    /// <summary>Why a command was not applied. Numeric values are stable.</summary>
    public enum CommandRejection
    {
        None = 0,
        Unowned = 1,
        Stale = 2,
        Early = 3,
        Duplicate = 4,
        Invalid = 5,
    }
}
