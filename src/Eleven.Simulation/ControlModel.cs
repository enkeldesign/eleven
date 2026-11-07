namespace Eleven.Simulation
{
    /// <summary>
    /// How a player controls the ball in possession (#29): the model id, part of the config's
    /// encoding and hash. Kicks, ball physics and the touch adapter are separate systems.
    /// </summary>
    public enum ControlModel
    {
        /// <summary>
        /// No ball control: the ball is touched only by kicks, as before #59. The kick, pass, ball
        /// and movement characterisations use it, so they keep measuring their own mechanic.
        /// </summary>
        Off = 0,

        /// <summary>
        /// The free ball (#59), #29's primary hypothesis: the ball stays an independent body, and a
        /// player in control pushes it ahead with discrete, bounded touches.
        /// </summary>
        Free = 1,

        /// <summary>
        /// The attached comparator (#60): while a player has the ball, a bounded controller steers
        /// its velocity toward a lead point ahead of them. It is never parented or teleported, and
        /// the ball still moves under its own physics. It is lost beyond a release reach, or to a kick.
        /// </summary>
        Attached = 2,
    }
}
