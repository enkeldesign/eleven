// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;

namespace Eleven.Simulation
{
    /// <summary>Kinds of event a step can emit. Numeric values are stable.</summary>
    public enum SimEventKind
    {
        /// <summary>Subject: the player that kicked.</summary>
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

        /// <summary>
        /// Subject: the player. Emitted when a kick's wind-up ends with the ball out of reach or too
        /// high, so the kick doesn't connect.
        /// </summary>
        KickMissed = 5,

        /// <summary>
        /// Subject: the passer. Emitted when a quick pass is resolved (#38); <see cref="SimEvent.Other"/>
        /// is the chosen receiver's id, or 0 for a free pass. Presentation can show the receiver.
        /// </summary>
        QuickPassResolved = 6,

        /// <summary>Subject: the player whose touch controlled the ball (#59). Presentation can show it.</summary>
        Touched = 7,
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

        /// <summary>A second id where the kind has one: the receiver of <see cref="SimEventKind.QuickPassResolved"/>; otherwise 0.</summary>
        public readonly int Other;

        public SimEvent(long tick, SimEventKind kind, int subject, CommandRejection rejection = CommandRejection.None, int other = 0)
        {
            Tick = tick;
            Kind = kind;
            Subject = subject;
            Rejection = rejection;
            Other = other;
        }

        public bool Equals(SimEvent other) =>
            Tick == other.Tick && Kind == other.Kind && Subject == other.Subject && Rejection == other.Rejection && Other == other.Other;

        public override bool Equals(object? obj) => obj is SimEvent other && Equals(other);

        public override int GetHashCode() => unchecked((int)Tick * 31 + (int)Kind * 7 + Subject * 3 + (int)Rejection + Other * 13);

        public override string ToString() =>
            Rejection != CommandRejection.None ? $"{Tick} {Kind} {Subject} {Rejection}"
            : Kind == SimEventKind.QuickPassResolved ? $"{Tick} {Kind} {Subject} {Other}"
            : $"{Tick} {Kind} {Subject}";
    }
}
