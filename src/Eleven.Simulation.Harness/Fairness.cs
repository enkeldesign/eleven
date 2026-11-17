// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The human's camera view (#102), as the host frames it on a phone held upright: centred
    /// between his footballer and the ball, zoomed out until both fit with a margin, between the
    /// closest and the widest views. The status band and the widest view's shift toward the
    /// footballer are left out. Technical references from the host's framing (#31, #72).
    /// </summary>
    public static class HumanView
    {
        /// <summary>m. Half the view across the pitch at the closest and the widest zoom.</summary>
        public const double Closest = 7, Widest = 17.5;

        /// <summary>m. The margin the framing keeps around the footballer and the ball.</summary>
        public const double Margin = 3;

        /// <summary>The play area's height over its width on the reference phone in portrait (390 × 560 pt).</summary>
        public const double Aspect = 560.0 / 390.0;

        /// <summary>Whether <paramref name="at"/> is within <paramref name="beyond"/> of the view that frames <paramref name="player"/> and <paramref name="ball"/>.</summary>
        public static bool Shows(Vec2 player, Vec2 ball, Vec2 at, double beyond = 0)
        {
            // Upright, the pitch's length runs up the screen: x along, y across.
            double needAcross = Math.Abs(ball.Y - player.Y) / 2 + Margin;
            double needAlong = (Math.Abs(ball.X - player.X) / 2 + Margin) / Aspect;
            double across = Math.Max(Closest, Math.Min(Widest, Math.Max(needAcross, needAlong)));
            double along = across * Aspect;
            var centre = new Vec2((player.X + ball.X) / 2, (player.Y + ball.Y) / 2);
            return Math.Abs(at.X - centre.X) <= along + beyond && Math.Abs(at.Y - centre.Y) <= across + beyond;
        }
    }

    /// <summary>How often one tier's CPU decisions depended on footballers the human couldn't see (#102).</summary>
    public sealed class FairnessTrial
    {
        public string Tier = "";
        public int Matches;

        /// <summary>Decisions in play by the human's opponents, and by his CPU teammates.</summary>
        public int OpponentDecisions, MateDecisions;

        /// <summary>The opponents' decisions that came out otherwise with the human's side's footballers outside his view out of sight.</summary>
        public int OpponentOnHidden;

        /// <summary>Of those, the ones that changed in kind: another mode, a kick or a pass, not only the run.</summary>
        public int OpponentOnHiddenInKind;

        /// <summary>The same, with the footballers the edge indicators show counted as seen.</summary>
        public int OpponentOnUnindicated;

        /// <summary>The opponents' decisions that came out otherwise with their own hidden teammates out of sight: their coordination, reported only.</summary>
        public int OpponentOnHiddenMates;

        /// <summary>The symmetric yardstick: the human's CPU teammates' decisions that came out otherwise with the opponents outside his view out of sight.</summary>
        public int MateOnHidden;

        public double OpponentShare => Share(OpponentOnHidden, OpponentDecisions);
        public double OpponentKindShare => Share(OpponentOnHiddenInKind, OpponentDecisions);
        public double UnindicatedShare => Share(OpponentOnUnindicated, OpponentDecisions);
        public double MateShare => Share(MateOnHidden, MateDecisions);

        private static double Share(int part, int whole) => whole > 0 ? (double)part / whole : 0;
    }

    /// <summary>
    /// Off-screen threat fairness (#102): in 5v5 matches with a human seat (<see cref="SquadTrials.Seat"/>)
    /// against each CPU tier, every CPU decision in play is made again on the same observation with
    /// footballers outside the human's view moved out of sight. A decision that comes out otherwise
    /// depended on what the human couldn't see. Perception draws two errors for every footballer,
    /// seen or not, so nothing else in the observation changes. A measurement, not a change: the
    /// CPU's own perception (#83) is what decides, and he sees off the human's screen as a
    /// footballer on the pitch would.
    /// </summary>
    public static class FairnessTrials
    {
        public const int Seeds = 3;

        /// <summary>m. How far beyond the view the edge indicators show a footballer: the host's reference (#102).</summary>
        public const double IndicatorReach = 15;

        /// <summary>
        /// The recorded bounds: at no tier may the opponents depend on the human's hidden
        /// footballers in more than <see cref="Bound"/> of their decisions, nor in a larger share
        /// than his own CPU teammates depend on hidden opponents plus <see cref="Margin"/>: the CPU
        /// side then knows no more off the screen than the human's side does. Set from the first
        /// measurement (at most 0.4 %) with headroom. A rise past them is a regression to look at,
        /// not a tuning target.
        /// </summary>
        public const double Bound = 0.02, Margin = 0.01;

        private static readonly Vec2 OutOfSight = new Vec2(10000, 10000);

        public static List<FairnessTrial> Run(SimConfig config, int seeds = Seeds)
        {
            var tiers = new[] { ("apprentice", CpuSkill.Apprentice), ("club", CpuSkill.Club), ("pro", CpuSkill.Pro), ("elite", CpuSkill.Elite) };
            return tiers.Select(t => One(config, t.Item1, t.Item2, seeds)).ToList();
        }

        /// <summary>Whether a tier is within the bound.</summary>
        public static bool Within(FairnessTrial t) => t.OpponentShare <= Bound && t.OpponentShare <= t.MateShare + Margin;

        public static FairnessTrial One(SimConfig config, string tier, CpuSkill skill, int seeds)
        {
            var trial = new FairnessTrial { Tier = tier, Matches = seeds };
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            for (int i = 1; i <= seeds; i++)
            {
                var sim = new Simulator(config, Squads.Match(config, 0x102000000UL + (ulong)i, League.PerSide, awayCpu: skill, homeCpu: skill));
                var switcher = new SwitchController(SquadTrials.SeatController);
                var recorder = new MatchRecorder();
                int limit = 3 * 2 * config.HalfTicks;
                while (sim.Tick < limit && recorder.Result == null)
                {
                    Measure(sim, trial);
                    commands.Clear();
                    commands.Add(SquadTrials.Seat(sim, SquadTrials.SeatController, switcher.Decide(sim, events, manual: false)));
                    events.Clear();
                    KeeperControllers.Append(sim, commands);
                    CpuControllers.Append(sim, commands);
                    RestartControllers.Append(sim, commands);
                    ManagerControllers.Append(sim, commands);
                    sim.Step(commands, events);
                    recorder.Observe(config, sim.PeekState(), events);
                }
            }

            return trial;
        }

        // Every CPU decision this tick, made again without footballers the human can't see.
        private static void Measure(Simulator sim, FairnessTrial trial)
        {
            SimState s = sim.PeekState();
            if (s.Restart.Phase != RestartPhase.Live) return;
            int me = Array.FindIndex(s.Players, p => p.OwnerControllerId == SquadTrials.SeatController);
            if (me < 0) return;
            PlayerState human = s.Players[me];
            Vec2 ball = s.Ball.Position.Horizontal;
            for (int i = 0; i < s.Cpus.Length; i++)
            {
                CpuState cpu = s.Cpus[i];
                if (!cpu.MayDecide(s.Tick)) continue;
                PlayerState body = Array.Find(s.Players, p => p.Id == cpu.PlayerId);
                int sign = Perception.AttackSign(s, body.TeamId);
                CpuPlan Plan(SimState state) => Perception.InWorld(AI.OutfieldBrain.Decide(Perception.OfOutfielder(state, i), sim.Config), sign);
                CpuPlan full = Plan(s);

                // The other side's footballers, or his own side's, beyond the view by more than `beyond`, out of sight.
                bool Depends(bool theirs, double beyond, out bool inKind)
                {
                    CpuPlan blind = Plan(Hide(s, human.Position, ball, body, theirs, beyond));
                    inKind = !SameKind(full, blind);
                    return !Same(full, blind);
                }

                if (body.TeamId != human.TeamId)
                {
                    trial.OpponentDecisions++;
                    if (Depends(theirs: true, 0, out bool inKind))
                    {
                        trial.OpponentOnHidden++;
                        if (inKind) trial.OpponentOnHiddenInKind++;
                        if (Depends(theirs: true, IndicatorReach, out _)) trial.OpponentOnUnindicated++;
                    }

                    if (Depends(theirs: false, 0, out _)) trial.OpponentOnHiddenMates++;
                }
                else
                {
                    trial.MateDecisions++;
                    if (Depends(theirs: true, 0, out _)) trial.MateOnHidden++;
                }
            }
        }

        // The state with the footballers of the decider's other side (or, not `theirs`, of his own)
        // more than `beyond` outside the human's view out of sight; never the decider, whose own body
        // he always knows. Only positions move, so the observation's draws are the same.
        private static SimState Hide(SimState s, Vec2 human, Vec2 ball, PlayerState decider, bool theirs, double beyond)
        {
            SimState copy = s.Clone();
            for (int i = 0; i < copy.Players.Length; i++)
            {
                PlayerState p = copy.Players[i];
                if (p.Id == decider.Id || (p.TeamId != decider.TeamId) != theirs) continue;
                if (!HumanView.Shows(human, ball, p.Position, beyond)) copy.Players[i].Position = OutOfSight;
            }

            return copy;
        }

        private static bool SameKind(CpuPlan a, CpuPlan b) =>
            a.Mode == b.Mode && a.QuickPass == b.QuickPass && a.Kick.HasValue == b.Kick.HasValue;

        private static bool Same(CpuPlan a, CpuPlan b)
        {
            if (!SameKind(a, b) || a.Move.X != b.Move.X || a.Move.Y != b.Move.Y) return false;
            if (!a.Kick.HasValue) return true;
            KickIntent x = a.Kick.GetValueOrDefault(), y = b.Kick.GetValueOrDefault();
            return x.Direction.X == y.Direction.X && x.Direction.Y == y.Direction.Y && x.Strength == y.Strength && x.Loft == y.Loft;
        }

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<FairnessTrial> trials)
        {
            w.WriteLine("config_hash,tier,matches,opponent_decisions,on_hidden,on_hidden_in_kind,on_unindicated,on_hidden_mates,mate_decisions,mate_on_hidden,opponent_share,unindicated_share,mate_share,within");
            foreach (FairnessTrial t in trials)
            {
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:x16},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10:F4},{11:F4},{12:F4},{13}",
                    config.Hash, t.Tier, t.Matches, t.OpponentDecisions, t.OpponentOnHidden, t.OpponentOnHiddenInKind, t.OpponentOnUnindicated, t.OpponentOnHiddenMates,
                    t.MateDecisions, t.MateOnHidden, t.OpponentShare, t.UnindicatedShare, t.MateShare, Within(t) ? "yes" : "no"));
            }
        }

        public static string KeyNumbers(IReadOnlyList<FairnessTrial> trials) =>
            "opponents deciding on the human's hidden footballers (with the indicators; his teammates on hidden opponents): " + string.Join("; ", trials.Select(t => string.Format(CultureInfo.InvariantCulture,
                "{0} {1:P1} ({2:P1}; {3:P1})", t.Tier, t.OpponentShare, t.UnindicatedShare, t.MateShare)))
            + string.Format(CultureInfo.InvariantCulture, "; bound {0:P0}, margin {1:P0}", Bound, Margin);
    }
}
