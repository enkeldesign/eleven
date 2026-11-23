using System;
using System.Collections.Generic;

namespace Eleven.Simulation
{
    /// <summary>Which line of his side's formation a footballer plays in (#127), as his manager reads it.</summary>
    public enum ManagedLine : byte
    {
        /// <summary>A keeper, or a side with no shape.</summary>
        None = 0,
        Back = 1,
        Middle = 2,
        Forward = 3,
    }

    /// <summary>One of a manager's own footballers, as he reads him (#110).</summary>
    public readonly struct ManagedPlayer
    {
        public ManagedPlayer(int id, double fatigue, InjuryLevel injury, bool keeper, ManagedLine line = ManagedLine.None)
        {
            Line = line;
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

        /// <summary>His line in his side's formation (#127): the back line, the last line forward, or in between.</summary>
        public ManagedLine Line { get; }
    }

    /// <summary>
    /// What a side's manager perceives (#110, INV-9): his own footballers who may be replaced, in
    /// ascending id order, his unused substitutes, whether he may substitute now, and how much of
    /// the match is played. Nothing of the other side.
    /// </summary>
    public sealed class ManagerObservation
    {
        public ManagerObservation(bool maySubstitute, double matchShare, IReadOnlyList<ManagedPlayer> players, IReadOnlyList<int> bench,
            int goalsFor = 0, int goalsAgainst = 0, int onPitch = 0, int theirsOnPitch = 0, int plan = 0, bool maySwitch = false, double spread = 0)
        {
            GoalsFor = goalsFor;
            GoalsAgainst = goalsAgainst;
            OnPitch = onPitch;
            TheirsOnPitch = theirsOnPitch;
            Plan = plan;
            MaySwitch = maySwitch;
            Spread = spread;
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

        /// <summary>The score (#127): his side's goals and the other side's.</summary>
        public int GoalsFor { get; }

        public int GoalsAgainst { get; }

        /// <summary>Footballers on the pitch (#127), his side's and the other's: a sending-off is plain to see.</summary>
        public int OnPitch { get; }

        public int TheirsOnPitch { get; }

        /// <summary>His side's current tactical plan (#126), 0 to 2: balanced, chase, protect by convention (#127).</summary>
        public int Plan { get; }

        /// <summary>Whether he may switch plan now (#126): plans stored, no cooldown, none waiting.</summary>
        public bool MaySwitch { get; }

        /// <summary>His own spread (#127): a share of the match by which his thresholds shift, from his side's draw.</summary>
        public double Spread { get; }
    }

    /// <summary>What a manager asks for (#110, #127): a substitution, a switch of plan, both or neither.</summary>
    public readonly struct ManagerDecision
    {
        public ManagerDecision(int off, int on, byte tacticTo = 0)
        {
            Off = off;
            On = on;
            TacticTo = tacticTo;
        }

        public int Off { get; }

        public int On { get; }

        /// <summary>1 to 3 for a plan to switch to; 0 for none.</summary>
        public byte TacticTo { get; }

        public void Deconstruct(out int off, out int on)
        {
            off = Off;
            on = On;
        }
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
                players.Add(new ManagedPlayer(p.Id, p.Fatigue, p.Injury, Array.FindIndex(state.Keepers, k => k.PlayerId == p.Id) >= 0, LineOf(state, side, p)));
            }

            var bench = new List<int>();
            foreach (BenchPlayer b in state.Bench)
            {
                if (b.TeamId == team && !b.Used) bench.Add(b.Id);
            }

            int other = Array.FindIndex(state.Teams, x => x.Id != team), onPitch = 0, theirs = 0;
            foreach (PlayerState p in state.Players)
            {
                if (p.Off) continue;
                if (p.TeamId == team) onPitch++;
                else theirs++;
            }

            bool maySwitch = state.Tactics.Length > 0 && side.TacticCooldown == 0 && side.TacticPending == 0;
            var draws = new Randomness.StreamDraws(state.Seed, 0, Randomness.RandomStream.Manager, team);
            double spread = (2 * draws.NextUniform() - 1) * config.CpuTacticSpread;
            return new ManagerObservation(may, share > 1 ? 1 : share, players, bench, side.Goals, other >= 0 ? state.Teams[other].Goals : 0, onPitch, theirs, side.Tactic, maySwitch, spread);
        }

        // A footballer's line in his side's formation (#127), by his place in its shape; None for a keeper or no shape.
        private static ManagedLine LineOf(SimState state, TeamState side, PlayerState p)
        {
            if (p.Slot <= 0) return ManagedLine.None;
            int n = 0;
            foreach (PlayerState q in state.Players)
            {
                if (q.TeamId == p.TeamId && q.Slot > 0) n++;
            }

            FormationLine[] lines = Formations.Lines(side.Formation, n);
            int at = p.Slot;
            for (int i = 0; i < lines.Length; i++)
            {
                if (at <= lines[i].Count) return i == 0 ? ManagedLine.Back : i == lines.Length - 1 ? ManagedLine.Forward : ManagedLine.Middle;
                at -= lines[i].Count;
            }

            return ManagedLine.Forward;
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

        /// <summary>
        /// The CPU managers' commands (#182), each side by its profile in <paramref name="profiles"/>
        /// by team id, or by the config's without one.
        /// </summary>
        public static void Append(Simulator simulator, List<Command> commands, IReadOnlyDictionary<int, ManagerProfile>? profiles) => Append(simulator, commands);

        public static void Append(Simulator simulator, List<Command> commands)
        {
            SimState state = simulator.PeekState();
            if (state.Bench.Length == 0 && state.Tactics.Length == 0) return;
            foreach (TeamState team in state.Teams)
            {
                if (!CpuManages(state, team.Id)) continue;
                ManagerDecision d = AI.ManagerBrain.Decide(Observe(state, simulator.Config, team.Id), simulator.Config);
                int off = d.Off, on = d.On;
                if (off == 0 && d.TacticTo == 0) continue;

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
                if (at >= 0) commands[at] = commands[at].WithSubstitution(off, on).WithTactic(d.TacticTo);
                else commands.Add(new Command(controller, state.Tick, Vec2.Zero, subOff: off, subOn: on, tacticTo: d.TacticTo));
            }
        }
    }
}
