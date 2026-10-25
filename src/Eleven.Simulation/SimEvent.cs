using System;

namespace Eleven.Simulation
{
    /// <summary>Kinds of event a step can emit. Numeric values are stable.</summary>
    public enum SimEventKind
    {
        /// <summary>Subject: the dot that kicked.</summary>
        Kicked = 1,

        /// <summary>Subject: the ball. Emitted when an airborne ball hits the ground.</summary>
        Bounced = 2,

        /// <summary>
        /// Subject: the ball. Emitted on the step where the ball becomes wholly beyond the goal
        /// line (x − radius &gt; goal line x) having not been after the previous step. Geometry
        /// only, not a football goal.
        /// </summary>
        GoalLineCrossed = 3,

        /// <summary>Subject: the controller id. <see cref="SimEvent.Rejection"/> says why.</summary>
        CommandRejected = 4,
    }

    /// <summary>
    /// Something that happened during a step. Internal to the simulation and its harness; match
    /// consumers will read a result contract instead (INV-14).
    /// </summary>
    public readonly struct SimEvent : IEquatable<SimEvent>
    {
        /// <summary>The tick whose step emitted the event.</summary>
        public readonly long Tick;
        public readonly SimEventKind Kind;
        public readonly int Subject;
        public readonly CommandRejection Rejection;

        public SimEvent(long tick, SimEventKind kind, int subject, CommandRejection rejection = CommandRejection.None)
        {
            Tick = tick;
            Kind = kind;
            Subject = subject;
            Rejection = rejection;
        }

        public bool Equals(SimEvent other) =>
            Tick == other.Tick && Kind == other.Kind && Subject == other.Subject && Rejection == other.Rejection;

        public override bool Equals(object? obj) => obj is SimEvent other && Equals(other);

        public override int GetHashCode() => unchecked((int)Tick * 31 + (int)Kind * 7 + Subject * 3 + (int)Rejection);

        public override string ToString() =>
            Rejection == CommandRejection.None ? $"{Tick} {Kind} {Subject}" : $"{Tick} {Kind} {Subject} {Rejection}";
    }
}
