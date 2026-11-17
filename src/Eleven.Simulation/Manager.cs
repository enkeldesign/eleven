using System;
using System.Collections.Generic;

namespace Eleven.Simulation
{
    /// <summary>One of a manager's own footballers, as he reads him (#110).</summary>
    public readonly struct ManagedPlayer
    {
        public ManagedPlayer(int id, double fatigue, InjuryLevel injury, bool keeper)
        {
            Id = id;
            Fatigue = fatigue;
            Injury = injury;
            Keeper = keeper;
        }

        public int Id { get; }

        /// <summary>0 fresh to 1 spent (#100).</summary>
        public double Fatigue { get; }

        /// <summary>Hurt this match (#109): serious means taken off, and replaceable.</summary>
        public InjuryLevel Injury { get; }

        public bool Keeper { get; }
    }

    /// <summary>
    /// What a side's manager perceives (#110, INV-9): his own footballers who may be replaced, in
    /// ascending id order, his unused substitutes, whether he may substitute now, and how much of
    /// the match is played. Nothing of the other side.
    /// </summary>
    public sealed class ManagerObservation
    {
        public ManagerObservation(bool maySubstitute, double matchShare, IReadOnlyList<ManagedPlayer> players, IReadOnlyList<int> bench)
        {
            MaySubstitute = maySubstitute;
            MatchShare = matchShare;
            Players = players;
            Bench = bench;
        }

        /// <summary>A stoppage or half-time, with a substitution and a window for it left.</summary>
        public bool MaySubstitute { get; }

        /// <summary>0–1: the share of the match's clock played.</summary>
        public double MatchShare { get; }

        /// <summary>His footballers who may be replaced: everyone of his side not sent off.</summary>
        public IReadOnlyList<ManagedPlayer> Players { get; }

        /// <summary>His substitutes who haven't come on, by id, ascending.</summary>
        public IReadOnlyList<int> Bench { get; }
    }

    /// <summary>
    /// Runs each CPU side's manager (#110). A side is the CPU's to manage when no human plays any
    /// of its outfielders. Before a step it adds the manager's substitution, if he asks for one,
    /// to the command of his side's keeper's controller, or of its first footballer's: an owned
    /// command, recorded like any other, so a replay never runs a brain.
    /// </summary>
    public static class ManagerControllers
    {
        /// <summary>What <paramref name="team"/>'s manager perceives in <paramref name="state"/>.</summary>
        public static ManagerObservation Observe(SimState state, SimConfig config, int team)
        {
            int t = Array.FindIndex(state.Teams, x => x.Id == team);
            TeamState side = state.Teams[t];
            MatchPhase phase = state.Match.Phase;
            // Any break is free (#111): half-time, and extra time's two.
            bool halfTime = phase == MatchPhase.HalfTime || phase == MatchPhase.ExtraBreak || phase == MatchPhase.ExtraHalfTime;
            bool playing = phase == MatchPhase.FirstHalf || phase == MatchPhase.SecondHalf || phase == MatchPhase.ExtraFirstHalf || phase == MatchPhase.ExtraSecondHalf;
            bool stopped = halfTime || (playing && state.Restart.On && state.Restart.Phase != RestartPhase.Live);
            bool may = stopped && side.Substitutions < config.Substitutions && (halfTime || side.SubWindowOpen || side.SubWindows < config.SubstitutionWindows);

            double share = phase switch
            {
                MatchPhase.FirstHalf => 0.5 * state.Match.ClockTicks / config.HalfTicks,
                MatchPhase.HalfTime => 0.5,
                MatchPhase.SecondHalf => 0.5 + 0.5 * state.Match.ClockTicks / config.HalfTicks,
                MatchPhase.None => 0,
                _ => 1,
            };

            var players = new List<ManagedPlayer>();
            foreach (PlayerState p in state.Players)
            {
                if (p.TeamId != team || p.SentOff) continue;
                players.Add(new ManagedPlayer(p.Id, p.Fatigue, p.Injury, Array.FindIndex(state.Keepers, k => k.PlayerId == p.Id) >= 0));
            }

            var bench = new List<int>();
            foreach (BenchPlayer b in state.Bench)
            {
                if (b.TeamId == team && !b.Used) bench.Add(b.Id);
            }

            return new ManagerObservation(may, share > 1 ? 1 : share, players, bench);
        }

        /// <summary>Whether <paramref name="team"/> is the CPU's to manage: no human plays any of its outfielders on the pitch.</summary>
        public static bool CpuManages(SimState state, int team)
        {
            foreach (PlayerState p in state.Players)
            {
                if (p.TeamId != team || p.Off || Array.FindIndex(state.Keepers, k => k.PlayerId == p.Id) >= 0) continue;
                if (Array.FindIndex(state.Cpus, c => c.PlayerId == p.Id) < 0) return false;
            }

            return true;
        }

        public static void Append(Simulator simulator, List<Command> commands)
        {
            SimState state = simulator.PeekState();
            if (state.Bench.Length == 0) return;
            foreach (TeamState team in state.Teams)
            {
                if (!CpuManages(state, team.Id)) continue;
                (int off, int on) = AI.ManagerBrain.Decide(Observe(state, simulator.Config, team.Id), simulator.Config);
                if (off == 0) continue;

                // His side's keeper's controller, or its first footballer's on the pitch.
                int controller = 0;
                foreach (PlayerState p in state.Players)
                {
                    if (p.TeamId != team.Id || p.Off || p.OwnerControllerId == 0) continue;
                    bool keeper = Array.FindIndex(state.Keepers, k => k.PlayerId == p.Id) >= 0;
                    if (controller == 0 || keeper) controller = p.OwnerControllerId;
                    if (keeper) break;
                }

                if (controller == 0) continue;
                int at = commands.FindIndex(c => c.ControllerId == controller);
                if (at >= 0) commands[at] = commands[at].WithSubstitution(off, on);
                else commands.Add(new Command(controller, state.Tick, Vec2.Zero, subOff: off, subOn: on));
            }
        }
    }
}
