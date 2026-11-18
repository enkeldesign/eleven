namespace Eleven.Simulation
{
    /// <summary>
    /// Why a human's standing challenge did or didn't start this step (#135), for the host and
    /// captures only: nothing in a step reads it (INV-12).
    /// </summary>
    public enum ChallengeHint : byte
    {
        None = 0,

        /// <summary>It started.</summary>
        Started = 1,

        /// <summary>The ball is beyond challengeRange.</summary>
        TooFar = 2,

        /// <summary>The stick doesn't point at least half toward the ball.</summary>
        NotClosing = 3,

        /// <summary>The commit would end short of the ball carried on with its dribbler.</summary>
        WontReach = 4,

        /// <summary>He is committing, recovering, sliding, down, or winding up a kick.</summary>
        NotReady = 5,
    }

    /// <summary>
    /// One human footballer pressing toward a ball an opponent controls, this step (#135): what
    /// became of his challenge, how far the ball was, and by how much a commit would have ended
    /// short of it (0 unless WontReach).
    /// </summary>
    public readonly struct ChallengeReport
    {
        public readonly int PlayerId;
        public readonly ChallengeHint Hint;
        public readonly double Distance;
        public readonly double Short;

        public ChallengeReport(int playerId, ChallengeHint hint, double distance, double shortBy = 0)
        {
            PlayerId = playerId;
            Hint = hint;
            Distance = distance;
            Short = shortBy;
        }

        public override string ToString() => System.FormattableString.Invariant($"{PlayerId} {Hint} {Distance:F2} {Short:F2}");
    }
}
