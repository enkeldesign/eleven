using System;

namespace Eleven.Simulation
{
    /// <summary>
    /// One of a side's three stored tactical plans (#126): a formation (#125) and four team
    /// instructions. Plain data in <see cref="SimState.Tactics"/>, three a side; the CPU reads the
    /// side's current one, blended while it settles, through its observation (INV-9).
    /// docs/simulation/tactics.md.
    /// </summary>
    public struct TeamTactic
    {
        /// <summary>The side whose plan it is.</summary>
        public int TeamId;

        public Formation Formation;

        /// <summary>m. How far the shape pushes up when the side has the ball. Reference 6.</summary>
        public double StepUp;

        /// <summary>m. The whole shape moved up (+) or down (−) the pitch from its formation's homes. Reference 0.</summary>
        public double Line;

        /// <summary>A scale on every line's half-width. Reference 1.</summary>
        public double Width;

        /// <summary>
        /// m, in the side's own frame. The first man to the ball goes for it only with the ball
        /// behind this line; beyond it he holds his place. Reference 60: anywhere.
        /// </summary>
        public double Press;

        public const double StepUpMax = 20, LineMax = 15, WidthMin = 0.5, WidthMax = 1.5, PressMin = -50, PressMax = 60;

        /// <summary>The plan every side played before #126: its formation, stepping up 6 m, pressing anywhere.</summary>
        public static TeamTactic Reference(int team, Formation formation = Formation.FourFourTwo) =>
            new TeamTactic { TeamId = team, Formation = formation, StepUp = 6, Line = 0, Width = 1, Press = PressMax };

        internal void CheckRanges(string owner)
        {
            if (!Formations.IsDefined(Formation)) throw new ArgumentException($"{owner}: unknown formation {(byte)Formation}.");
            Check(owner, nameof(StepUp), StepUp, 0, StepUpMax);
            Check(owner, nameof(Line), Line, -LineMax, LineMax);
            Check(owner, nameof(Width), Width, WidthMin, WidthMax);
            Check(owner, nameof(Press), Press, PressMin, PressMax);
        }

        private static void Check(string owner, string field, double value, double min, double max)
        {
            if (!(value >= min && value <= max)) throw new ArgumentOutOfRangeException(field, $"{owner}: {field} {value} is outside [{min}, {max}].");
        }
    }

    /// <summary>Plans a side stores (#126).</summary>
    public static class TeamTactics
    {
        public const int PerSide = 3;
    }
}

namespace Eleven.Simulation
{
    public sealed partial class Simulator
    {
        // Why `team` may not switch to its plan `to` (1–3) now, or None.
        private CommandRejection MaySwitchTactic(int team, byte to)
        {
            if (to > TeamTactics.PerSide || _state.Tactics.Length == 0) return CommandRejection.TacticRefused;
            return CommandRejection.None;
        }
    }
}
