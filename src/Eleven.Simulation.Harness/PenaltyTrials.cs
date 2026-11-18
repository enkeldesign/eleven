using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>What a penalty benchmark found: kicks taken, and how each ended.</summary>
    public sealed class PenaltyTrial
    {
        public int PerSide, Shootouts;

        /// <summary>Kicks taken, and how each ended: in the goal, saved by the keeper (held or parried), or missed.</summary>
        public int Kicks, Scored, Saved, Missed;

        /// <summary>Of those missed: off the frame, and still short of the line when the kick's time ran out.</summary>
        public int Frame, Short;
    }

    /// <summary>
    /// #122's penalty benchmark: the reference CPU taker against the reference keeper. Each trial is
    /// a knockout level at the end of extra time, so its shootout's kicks are penalties through the
    /// restart sequencer (INV-8), taken by the CPU taker's own command (INV-12) against the keeper's
    /// brain. Every kick is counted: scored, saved, or missed.
    /// </summary>
    public static class PenaltyTrials
    {
        public const int Shootouts = 40;
        public const ulong FirstSeed = 0x122000000UL;

        /// <summary>Plumbing: no shootouts are played yet.</summary>
        public static PenaltyTrial Run(SimConfig config, int perSide, int shootouts = Shootouts, ulong firstSeed = FirstSeed) =>
            new PenaltyTrial { PerSide = perSide };
    }
}
