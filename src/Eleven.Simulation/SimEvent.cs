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

        /// <summary>A footballer committed to a standing challenge (#84).</summary>
        ChallengeStarted = 19,

        /// <summary>A challenge reached the ball and knocked it away (#84). <see cref="SimEvent.Other"/> is the opponent who had it.</summary>
        ChallengeWon = 20,

        /// <summary>A challenge ended with the ball out of its reach (#84).</summary>
        ChallengeMissed = 21,

        /// <summary>The ball went out of play or into a goal (#85): a restart is awarded. Subject: the side awarded it. <see cref="SimEvent.Other"/>: its <see cref="RestartKind"/>.</summary>
        RestartAwarded = 22,

        /// <summary>The restart is set up (#85). Subject: its taker. <see cref="SimEvent.Other"/>: its <see cref="RestartKind"/>.</summary>
        RestartPlaced = 23,

        /// <summary>The taker kicked the ball: it is in play (#85). Subject: the taker. <see cref="SimEvent.Other"/>: the <see cref="RestartKind"/>.</summary>
        RestartTaken = 24,

        /// <summary>The first half is over (#86): the ball is dead for the break.</summary>
        HalfTime = 25,

        /// <summary>The second half has started its kick-off's set-up, with the ends changed (#86).</summary>
        SecondHalf = 26,

        /// <summary>The match is over (#86).</summary>
        FullTime = 27,

        /// <summary>A controller took over a teammate (#93). Subject: the footballer taken over. <see cref="SimEvent.Other"/>: the one left to the CPU.</summary>
        Switched = 28,

        /// <summary>A kick that connected was a header (#94). With <see cref="Kicked"/>.</summary>
        Headed = 29,

        /// <summary>A kick that connected was a volley (#94). With <see cref="Kicked"/>.</summary>
        Volleyed = 30,

        /// <summary>A shielding carrier held off a challenge that reached the ball (#97). Subject: the carrier. <see cref="SimEvent.Other"/>: the challenger.</summary>
        ShieldHeld = 31,

        /// <summary>A slide tackle began (#98). Subject: the slider.</summary>
        SlideStarted = 32,

        /// <summary>A slide met the ball first and knocked it away (#98). Subject: the slider. <see cref="SimEvent.Other"/>: who had it.</summary>
        SlideWon = 33,

        /// <summary>A slide met the man before the ball (#98): a foul once #99 adjudicates it. Subject: the slider. <see cref="SimEvent.Other"/>: the man.</summary>
        SlideFouled = 34,

        /// <summary>A slide ended meeting neither (#98). Subject: the slider.</summary>
        SlideMissed = 35,

        /// <summary>A foul (#99). Subject: the offender. <see cref="SimEvent.Other"/>: the footballer fouled.</summary>
        Foul = 36,

        /// <summary>Play goes on after a foul: the fouled side keeps the ball (#99). Subject: the fouled side.</summary>
        AdvantagePlayed = 37,

        /// <summary>An offside offence (#107). Subject: the footballer offside. <see cref="SimEvent.Other"/>: the side awarded the indirect free kick.</summary>
        Offside = 38,

        /// <summary>A caution (#108). Subject: the footballer cautioned. <see cref="SimEvent.Other"/>: his cautions now.</summary>
        Cautioned = 39,

        /// <summary>A sending-off (#108). Subject: the footballer sent off.</summary>
        SentOff = 40,

        /// <summary>An injury (#109). Subject: the footballer hurt. <see cref="SimEvent.Other"/>: its <see cref="InjuryLevel"/>.</summary>
        Injured = 41,
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
        /// the <see cref="FramePart"/> of <see cref="SimEventKind.HitFrame"/>, or the id of the side a
        /// <see cref="SimEventKind.Goal"/> is scored for (#82; 0 in practice, which has no sides); otherwise 0.
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
            : Kind == SimEventKind.QuickPassResolved || Kind == SimEventKind.HitFrame || Kind == SimEventKind.Turned || (Kind == SimEventKind.Goal && Other != 0)
                || Kind == SimEventKind.RestartAwarded || Kind == SimEventKind.RestartPlaced || Kind == SimEventKind.RestartTaken || Kind == SimEventKind.Switched || Kind == SimEventKind.Offside || Kind == SimEventKind.Cautioned || Kind == SimEventKind.Injured ? $"{Tick} {Kind} {Subject} {Other}"
            : $"{Tick} {Kind} {Subject}";
    }
}
