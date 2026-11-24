using Eleven.Simulation;

namespace Eleven.Career
{
    /// <summary>
    /// One meeting a manager remembers (#191): his club's match against an opponent, seen from his
    /// side, in the result contract's facts only. #80: a manager's memory respects the perception
    /// boundary, so it holds what a spectator of the match could know, nothing of the opponent's
    /// squad, ratings or plans.
    /// </summary>
    public sealed class Meeting
    {
        /// <summary>The opponent club's id.</summary>
        public int Opponent;

        /// <summary>The season's number, and the matchday as the hub shows it, from 1.</summary>
        public int Season, Matchday;

        public int GoalsFor, GoalsAgainst;

        /// <summary>Goals in the match's last quarter: the second half's second half, and extra time.</summary>
        public int LateFor, LateAgainst;

        /// <summary>The formation the opponent lined up in, as anyone watching would see.</summary>
        public Formation OpponentFormation = Formation.FourFourTwo;

        /// <summary>Whether the match was played in full, so its metrics (#128) are known.</summary>
        public bool Full;

        /// <summary>When played in full: his side's passes allowed per defensive action, the opponent's, and his possession, 0–1.</summary>
        public double Ppda, OpponentPpda, Possession;
    }

    /// <summary>What a manager's memory does to his plans for the next meeting (#191). Untuned technical references.</summary>
    public sealed class MemoryRules
    {
        /// <summary>The meetings he keeps with each opponent, the latest.</summary>
        public int Depth = 3;

        /// <summary>His line's shift, metres, per goal of his goal difference over the meetings remembered, and its cap.</summary>
        public double LineShiftPerGoal = 0.5, LineShiftMax = 2;

        /// <summary>How much earlier he protects, as a share of the match, per goal of a deficit, and its cap.</summary>
        public double ProtectShiftPerGoal = 0.03, ProtectShiftMax = 0.1;

        /// <summary>How much earlier still, when they've outscored him late.</summary>
        public double LateShift = 0.05;

        /// <summary>By how much their PPDA must be below his, in full matches, before his step-up eases, and by how much.</summary>
        public double PressMargin = 2, StepUpEase = 1;

        public static MemoryRules Reference { get; } = new MemoryRules();
    }
}
