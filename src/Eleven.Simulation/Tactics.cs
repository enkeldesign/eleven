using System;
using System.Collections.Generic;

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
    /// <summary>
    /// Switching tactical plans (#126): an owned command, checked when it is accepted and again
    /// at the start of the step, taken at the next stoppage or at once (planSwitchAt). A switch
    /// starts the side settling from the old plan to the new over planSettleSeconds, and its
    /// cooldown, planSwitchCooldown of the match clock.
    /// </summary>
    public sealed partial class Simulator
    {
        // Why `team` may not switch to its plan `to` (1–3) now, or None.
        private CommandRejection MaySwitchTactic(int team, byte to)
        {
            int t = Array.FindIndex(_state.Teams, x => x.Id == team);
            if (t < 0 || to < 1 || to > TeamTactics.PerSide || _state.Tactics.Length == 0) return CommandRejection.TacticRefused;
            TeamState side = _state.Teams[t];
            if (to - 1 == side.Tactic || to == side.TacticPending) return CommandRejection.TacticRefused;
            return side.TacticCooldown > 0 ? CommandRejection.TacticCooldown : CommandRejection.None;
        }

        // Whether play is stopped, so that a waiting switch is taken: a break, or a restart not yet taken.
        private bool TacticStopped => MatchBreak || (_state.Restart.On && _state.Restart.Phase != RestartPhase.Live);

        // Whether the match clock runs this step: a half under way, with the ball live (StepMatch's own rule).
        private bool ClockRuns => (_state.Match.Phase == MatchPhase.FirstHalf || _state.Match.Phase == MatchPhase.SecondHalf
            || _state.Match.Phase == MatchPhase.ExtraFirstHalf || _state.Match.Phase == MatchPhase.ExtraSecondHalf)
            && _state.Restart.Phase == RestartPhase.Live;

        private void StepTactics(long tick, List<SimEvent> events)
        {
            // The settling and the cooldown run down first, so a switch made now starts both whole.
            bool clock = ClockRuns;
            for (int t = 0; t < _state.Teams.Length; t++)
            {
                ref TeamState side = ref _state.Teams[t];
                if (side.TacticSettle > 0) side.TacticSettle--;
                if (side.TacticSettle == 0) side.TacticFrom = side.Tactic;
                if (clock && side.TacticCooldown > 0) side.TacticCooldown--;
            }

            if (_state.Tactics.Length == 0) return;
            foreach (Command c in _accepted)
            {
                if (c.TacticTo == 0) continue;
                int team = 0;
                foreach (PlayerState p in _state.Players)
                {
                    if (p.OwnerControllerId == c.ControllerId) team = p.TeamId;
                }

                // Checked again: another command this step may have switched, or set one waiting.
                CommandRejection why = MaySwitchTactic(team, c.TacticTo);
                if (why != CommandRejection.None)
                {
                    events.Add(new SimEvent(tick, SimEventKind.CommandRejected, c.ControllerId, why));
                    continue;
                }

                int i = Array.FindIndex(_state.Teams, x => x.Id == team);
                _state.Teams[i].TacticPending = c.TacticTo;
            }

            bool now = TacticStopped || Config.PlanSwitchAt >= 0.5;
            for (int t = 0; t < _state.Teams.Length; t++)
            {
                ref TeamState side = ref _state.Teams[t];
                if (side.TacticPending == 0 || !now) continue;
                int to = side.TacticPending - 1;
                side.TacticFrom = side.Tactic;
                side.Tactic = (byte)to;
                side.TacticPending = 0;
                side.TacticSettle = side.TacticSettleOf = Config.PlanSettleTicks;
                if (side.TacticSettle == 0) side.TacticFrom = side.Tactic;
                side.TacticCooldown = Config.PlanCooldownTicks;
                side.Formation = _state.Tactics[t * TeamTactics.PerSide + to].Formation;
                events.Add(new SimEvent(tick, SimEventKind.TacticSwitched, side.Id, other: to));
            }
        }
    }
}
