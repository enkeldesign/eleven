using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A match between two of the world's clubs (#159): each side's XI and bench chosen from its
    /// squad, every footballer, keeper and CPU footballer given his own values by
    /// <see cref="RatingMap"/>. The adapter outside the career that ADR 0005 calls for: the career
    /// holds the world, the simulation plays the match.
    /// </summary>
    public static class CareerSquads
    {
        /// <summary>
        /// The match's state: <see cref="Squads.Match"/>'s line-up and plans, with each side's
        /// footballers, keeper, CPU footballers and bench given their own values from
        /// <see cref="RatingMap"/>.
        /// </summary>
        public static SimState Match(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed, bool bothCpu = true,
            TeamTactic[]? homeTactics = null, TeamTactic[]? awayTactics = null, ISet<int>? homeOut = null, ISet<int>? awayOut = null)
        {
            SimState s = Squads.Match(config, seed, perSide, bothCpu: bothCpu, homeTactics: homeTactics, awayTactics: awayTactics);
            Apply(s, Selection(world, homeClub, perSide, homeOut), perSide, Squads.HomeTeam, Squads.HomeOutfielder, Squads.HomeKeeper(perSide), i => Squads.HomeBench(perSide, i));
            Apply(s, Selection(world, awayClub, perSide, awayOut), perSide, Squads.AwayTeam, i => Squads.AwayOutfielder(perSide, i), Squads.AwayKeeper(perSide), i => Squads.AwayBench(perSide, i));
            return s;
        }

        /// <summary>
        /// A match's simulation ids for its world footballers (#172): each side's starters (by
        /// <see cref="Squads.HomeOutfielder"/> and its keeper) and bench, simulation id to the
        /// world's player id. A result's lines (#172) name footballers by these ids.
        /// </summary>
        public static Dictionary<int, int> Lineup(World world, int homeClub, int awayClub, int perSide, ISet<int>? homeOut = null, ISet<int>? awayOut = null)
        {
            var lineup = new Dictionary<int, int>();
            SquadSelection home = Selection(world, homeClub, perSide, homeOut), away = Selection(world, awayClub, perSide, awayOut);
            for (int i = 0; i < perSide; i++)
            {
                lineup[Squads.HomeOutfielder(i)] = home.Starters[i].Id;
                lineup[Squads.AwayOutfielder(perSide, i)] = away.Starters[i].Id;
            }

            lineup[Squads.HomeKeeper(perSide)] = home.Starters[perSide].Id;
            lineup[Squads.AwayKeeper(perSide)] = away.Starters[perSide].Id;
            for (int i = 0; i < home.Bench.Count; i++) lineup[Squads.HomeBench(perSide, i)] = home.Bench[i].Id;
            for (int i = 0; i < away.Bench.Count; i++) lineup[Squads.AwayBench(perSide, i)] = away.Bench[i].Id;
            return lineup;
        }

        private static void Apply(SimState s, SquadSelection side, int perSide, int team, Func<int, int> outfielder, int keeperId, Func<int, int> benchId)
        {
            var byId = new Dictionary<int, Player>();
            for (int i = 0; i < perSide; i++) byId[outfielder(i)] = side.Starters[i];
            Player keeper = side.Starters[perSide];
            byId[keeperId] = keeper;
            for (int i = 0; i < s.Players.Length; i++)
            {
                if (byId.TryGetValue(s.Players[i].Id, out Player p)) s.Players[i].Capabilities = RatingMap.Capabilities(p);
            }

            for (int i = 0; i < s.Keepers.Length; i++)
            {
                if (s.Keepers[i].PlayerId == keeperId) s.Keepers[i].Capabilities = RatingMap.Keeper(keeper);
            }

            for (int i = 0; i < s.Cpus.Length; i++)
            {
                if (byId.TryGetValue(s.Cpus[i].PlayerId, out Player p)) s.Cpus[i].Skill = RatingMap.Skill(p);
            }

            for (int b = 0; b < s.Bench.Length; b++)
            {
                for (int i = 0; i < side.Bench.Count; i++)
                {
                    if (s.Bench[b].Id != benchId(i) || s.Bench[b].TeamId != team) continue;
                    s.Bench[b].Capabilities = RatingMap.Capabilities(side.Bench[i]);
                    s.Bench[b].Skill = RatingMap.Skill(side.Bench[i]);
                }
            }
        }

        // A footballer's lines, for the basic selection: a slot's from its share of the side.
        private enum Line
        {
            Defence,
            Midfield,
            Attack,
        }

        private static Line LineOf(Position p) => p switch
        {
            Position.CentreBack or Position.FullBack => Line.Defence,
            Position.Striker => Line.Attack,
            _ => Line.Midfield,
        };

        // Outfielder i's slot (Squads.SlotOf: the kick-off taker plays the last) and its line,
        // the first two fifths of the side defending and the last fifth attacking.
        private static Line SlotLine(int perSide, int i)
        {
            int slot = i == 0 ? perSide : i;
            double share = (double)(slot - 1) / perSide;
            return share < 0.4 ? Line.Defence : share < 0.8 ? Line.Midfield : Line.Attack;
        }

        private static readonly int[] Keeping =
        {
            PlayerAttributes.Index("keeperReflexes"), PlayerAttributes.Index("keeperHandling"), PlayerAttributes.Index("keeperPositioning"),
        };

        /// <summary>His keeping: the mean of his three keeper attributes.</summary>
        public static double KeeperLevel(Player p)
        {
            double sum = 0;
            foreach (int i in Keeping) sum += p.Attributes[i];
            return sum / Keeping.Length;
        }

        // An outfielder's level for the basic selection: the mean of his attributes but the keepers'.
        private static double OutfieldLevel(Player p)
        {
            double sum = 0;
            int n = 0;
            for (int i = 0; i < p.Attributes.Length; i++)
            {
                if (Array.IndexOf(Keeping, i) >= 0) continue;
                sum += p.Attributes[i];
                n++;
            }

            return sum / n;
        }

        /// <summary>
        /// A basic selection (#159; the real one is M6.08): the best keeper by his keeping, then
        /// for each outfield slot from the back the best unchosen footballer whose first position
        /// is in the slot's line, else whose second is, else the best left; the bench the best of
        /// the rest. Ties go to the lower id, so it is deterministic. Footballers in
        /// <paramref name="unavailable"/> (#176: injured or banned) are never picked; with no fit
        /// keeper, the best of the rest at keeping goes in goal.
        /// </summary>
        public static SquadSelection Selection(World world, int clubId, int perSide, ISet<int>? unavailable = null)
        {
            var squad = new List<Player>();
            foreach (Player p in world.Players)
            {
                if (p.ClubId == clubId) squad.Add(p);
            }

            var chosen = new HashSet<int>();
            Player? keeper = null;
            foreach (Player p in squad)
            {
                if (p.Positions.Count == 0 || p.Positions[0] != Position.Goalkeeper) continue;
                if (keeper == null || KeeperLevel(p) > KeeperLevel(keeper) || (KeeperLevel(p) == KeeperLevel(keeper) && p.Id < keeper.Id)) keeper = p;
            }

            if (keeper == null) throw new InvalidOperationException($"Club {clubId} has no keeper.");
            chosen.Add(keeper.Id);

            Player? Best(Func<Player, bool> fits)
            {
                Player? best = null;
                foreach (Player p in squad)
                {
                    if (chosen.Contains(p.Id) || p.Positions.Count == 0 || p.Positions[0] == Position.Goalkeeper || !fits(p)) continue;
                    if (best == null || OutfieldLevel(p) > OutfieldLevel(best) || (OutfieldLevel(p) == OutfieldLevel(best) && p.Id < best.Id)) best = p;
                }

                return best;
            }

            var bySlot = new Player[perSide];
            // Fill slots from the back, in slot order, so each line takes its best.
            var order = new List<int>();
            for (int slot = 1; slot <= perSide; slot++) order.Add(slot == perSide ? 0 : slot);
            foreach (int i in order)
            {
                Line line = SlotLine(perSide, i);
                Player p = Best(x => LineOf(x.Positions[0]) == line)
                    ?? Best(x => x.Positions.Count > 1 && LineOf(x.Positions[1]) == line)
                    ?? Best(_ => true)
                    ?? throw new InvalidOperationException($"Club {clubId} hasn't {perSide} outfielders.");
                bySlot[i] = p;
                chosen.Add(p.Id);
            }

            var selection = new SquadSelection();
            selection.Starters.AddRange(bySlot);
            selection.Starters.Add(keeper);
            for (int i = 0; i < Squads.BenchOf(perSide); i++)
            {
                Player? p = Best(_ => true);
                if (p == null) break;
                selection.Bench.Add(p);
                chosen.Add(p.Id);
            }

            return selection;
        }

        /// <summary>
        /// A CPU-against-CPU match between two clubs, played to its result by the full simulation
        /// with every brain running: the keepers', the CPU's, the restarts' and the managers'.
        /// </summary>
        public static MatchResult Play(SimConfig config, World world, int homeClub, int awayClub, int perSide, ulong seed)
        {
            var sim = new Simulator(config, Match(config, world, homeClub, awayClub, perSide, seed));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            long limit = 8L * config.HalfTicks + 600L * config.TickRate;
            while (sim.Tick < limit && recorder.Result == null)
            {
                commands.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                ManagerControllers.Append(sim, commands);
                events.Clear();
                sim.Step(commands, events);
                recorder.Observe(config, sim.PeekState(), events);
            }

            return recorder.Result ?? throw new InvalidOperationException($"{homeClub} against {awayClub} (seed {seed}) didn't finish.");
        }
    }

    /// <summary>A side's footballers for a match: the starters in the side's slot order, the keeper last, then the bench.</summary>
    public sealed class SquadSelection
    {
        public List<Player> Starters = new List<Player>();
        public List<Player> Bench = new List<Player>();
    }
}
