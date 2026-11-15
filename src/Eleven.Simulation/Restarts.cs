using System;
using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation
{
    /// <summary>
    /// The restart sequencer (#85, INV-8): out of play, goals and restarts, as rules over the state,
    /// run by the simulation on each step. A restart is restart → simulation → termination: it is
    /// awarded when the ball leaves play, set up after the dead-ball time, and ends when its taker
    /// kicks the ball, which is an ordinary kick on the ordinary ball. No mode carries football: a
    /// match turns the sequencer on (<see cref="RestartState.On"/>), and practice leaves it off.
    /// docs/simulation/restarts.md.
    /// </summary>
    public sealed partial class Simulator
    {
        /// <summary>m. How far an opponent must be from a restart's ball: Law 8's, 13's, 16's and 17's 9.15 m.</summary>
        public const double RestartDistance = 9.15;

        /// <summary>m. From a throw-in: Law 15's 2 m.</summary>
        public const double ThrowInDistance = 2;

        /// <summary>m. The goal area's depth and reach past each post: Law 1's 5.5 m.</summary>
        public const double GoalArea = 5.5;

        /// <summary>m. How far behind the ball its taker is placed.</summary>
        public const double TakerBehind = 0.6;

        // The restart's state this config and these teams could have produced.
        private static void ValidateRestart(SimState state, SimConfig config)
        {
            RestartState r = state.Restart;
            if (r.Phase > RestartPhase.Ready || r.Kind > RestartKind.Corner) throw new ArgumentException("Unknown restart phase or kind.", nameof(state));
            if (!r.On)
            {
                if (r.Phase != RestartPhase.Live) throw new ArgumentException("With the sequencer off, the ball is live.", nameof(state));
                return;
            }

            if (state.Teams.Length != 2) throw new ArgumentException("The restart sequencer needs a match: two sides.", nameof(state));
            bool IsTeam(int id) => Array.FindIndex(state.Teams, t => t.Id == id) >= 0;
            if (r.LastTouchedTeam != 0 && !IsTeam(r.LastTouchedTeam)) throw new ArgumentException($"Restart: last touched by side {r.LastTouchedTeam}, which isn't one of the two.", nameof(state));
            if (r.Phase == RestartPhase.Live) return;
            if (r.Kind == RestartKind.None || !IsTeam(r.AwardedTeam)) throw new ArgumentException("A stopped or ready restart names its kind and its side.", nameof(state));
            int most = r.Phase == RestartPhase.Stopped ? config.RestartStopTicks : config.RestartReadyTicks;
            if (r.PhaseTicks < 0 || r.PhaseTicks > most || r.WaitTicks < 0) throw new ArgumentOutOfRangeException(nameof(state), $"Restart: {r.PhaseTicks} steps left is outside [0, {most}].");
            if (r.Phase == RestartPhase.Ready && Array.FindIndex(state.Players, p => p.Id == r.TakerId && p.TeamId == r.AwardedTeam) < 0)
            {
                throw new ArgumentException($"Restart: its taker {r.TakerId} isn't one of side {r.AwardedTeam}'s footballers.", nameof(state));
            }
        }
    }

    /// <summary>
    /// Takes restarts for their takers (#85). Not written yet: it takes nothing.
    /// </summary>
    public static class RestartControllers
    {
        public static void Append(Simulator simulator, List<Command> commands)
        {
        }
    }
}
