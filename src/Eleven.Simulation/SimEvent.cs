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
        /// Subject: the ball. Emitted on the step where the whole ball crosses the goal line
        /// (x − radius &gt; goal line x), between the posts or not. Geometry only: a crossing
        /// through the mouth also emits <see cref="Goal"/> (#68).
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

        /// <summary>Subject: the player who has taken the ball under ATTACHED (#60).</summary>
        Attached = 8,

        /// <summary>Subject: the player who has lost the ball under ATTACHED (#60): to a kick, beyond reach, or to a nearer player.</summary>
        Released = 9,

        /// <summary>
        /// Subject: the ball. Emitted on the step the whole ball crosses the goal line, when its
        /// centre crossed it between the posts and under the crossbar (#68). Always with
        /// <see cref="GoalLineCrossed"/> on the same step.
        /// </summary>
        Goal = 10,

        /// <summary>
        /// Subject: the ball. Emitted on the step the ball hits the goal's frame (#68);
        /// <see cref="SimEvent.Other"/> is the <see cref="FramePart"/>.
        /// </summary>
        HitFrame = 11,

        /// <summary>Subject: the keeper who has started reacting to a kick (#69).</summary>
        KeeperReacted = 12,

        /// <summary>Subject: the keeper who has dived (#69).</summary>
        KeeperDived = 13,

        /// <summary>Subject: the keeper who has caught and holds the ball (#69).</summary>
        KeeperHeld = 14,

        /// <summary>Subject: the keeper who has parried the ball (#69).</summary>
        KeeperParried = 15,

        /// <summary>
        /// Subject: the player who has played one of a turn's two touches with the ball (#66);
        /// <see cref="SimEvent.Other"/> is 1 for the trap and 2 for the touch out. Under FREE it comes
        /// with <see cref="Touched"/>. A later player model can animate it.
        /// </summary>
        Turned = 16,

        /// <summary>
        /// Subject: the player who has played a cut's touch with the ball under FREE (#75). It comes
        /// with <see cref="Touched"/>. A later player model can animate it.
        /// </summary>
        Cut = 17,

        /// <summary>
        /// A sprint's touch was a push (#63): the ball sent ahead faster than the footballer, to be
        /// chased. With <see cref="Touched"/>.
        /// </summary>
        Pushed = 18,
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

        /// <summary>
        /// A second value where the kind has one: the receiver of <see cref="SimEventKind.QuickPassResolved"/>,
        /// or the <see cref="FramePart"/> of <see cref="SimEventKind.HitFrame"/>; otherwise 0.
        /// </summary>
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
            : Kind == SimEventKind.QuickPassResolved || Kind == SimEventKind.HitFrame || Kind == SimEventKind.Turned ? $"{Tick} {Kind} {Subject} {Other}"
            : $"{Tick} {Kind} {Subject}";
    }
}
