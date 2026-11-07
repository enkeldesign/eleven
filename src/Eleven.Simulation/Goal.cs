namespace Eleven.Simulation
{
    /// <summary>
    /// Where the ball stands against the goal line (#68), kept in <see cref="BallState.Line"/>.
    /// The line's outer edge is <see cref="SimConfig.GoalLineX"/>. Numeric values are stable and
    /// encoded as one byte.
    /// </summary>
    public enum LineState : byte
    {
        /// <summary>The ball's centre is in front of the line.</summary>
        InPlay = 0,

        /// <summary>The whole ball is beyond the line, and its centre crossed it outside the mouth or over the bar.</summary>
        Out = 1,

        /// <summary>The whole ball is beyond the line, and its centre crossed it between the posts and under the crossbar.</summary>
        Goal = 2,

        /// <summary>The ball's centre crossed the line between the posts and under the crossbar; part of the ball is still on it.</summary>
        CrossingMouth = 3,

        /// <summary>The ball's centre crossed the line outside the mouth or over the bar; part of the ball is still on it.</summary>
        CrossingWide = 4,
    }

    /// <summary>
    /// The parts of the goal's frame (#68), as the attacker sees them, facing the goal from the
    /// pitch: the right post is at −y, the left at +y. The <see cref="SimEvent.Other"/> of a
    /// <see cref="SimEventKind.HitFrame"/>.
    /// </summary>
    public enum FramePart
    {
        None = 0,
        RightPost = 1,
        LeftPost = 2,
        Crossbar = 3,
    }
}
