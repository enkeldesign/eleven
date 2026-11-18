using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.AI;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #134, from Erik's session: "the goalie seems to automatically just pass to the opponent".
    /// Holding the ball, the keeper gives it short to an open teammate within reach, weighted by the
    /// quick pass's own speed (INV-13); with no one open he kicks long toward his furthest-forward
    /// teammate on his side.
    /// </summary>
    public class KeeperDistributionTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();
        private const double KickSpeed = 25;

        // In his own frame his goal is at +x: he stands at (50, 0), up-field is −x.
        private static readonly Vec2 Keeper = new Vec2(50, 0);

        private static KeeperObservation Holding(params SeenPlayer[] others) =>
            new KeeperObservation(0, Keeper, Vec2.Zero, Math.PI, KeeperPhase.Position, 0, KeeperCapabilities.Reference, 6,
                new Vec3(Keeper.X - 0.5, 0, 1), Vec3.Zero, Vec3.Zero, true, true, (int)(C.KeeperHoldSeconds * C.TickRate) + 1, true, others, KickSpeed);

        private static SeenPlayer Mate(double x, double y) => new SeenPlayer(new Vec2(x, y), Vec2.Zero, true, false);

        private static SeenPlayer Opponent(double x, double y) => new SeenPlayer(new Vec2(x, y), Vec2.Zero, false, false);

        private static KickIntent Kick(KeeperObservation seen)
        {
            KeeperDecision d = KeeperBrain.Decide(seen, C);
            Assert.True(d.Kick.HasValue, "he kept the ball");
            return d.Kick!.Value;
        }

        private static double AngleTo(KickIntent k, Vec2 target)
        {
            Vec2 to = target - Keeper;
            double a = Math.Atan2(k.Direction.Y, k.Direction.X) - Math.Atan2(to.Y, to.X);
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return Math.Abs(a);
        }

        [Fact]
        public void He_rolls_it_to_an_open_teammate_weighted_as_a_quick_pass()
        {
            var full = new Vec2(36, 18);
            KickIntent k = Kick(Holding(Mate(full.X, full.Y), Opponent(10, -20), Opponent(0, 10)));
            Assert.True(AngleTo(k, full) < 0.02, $"{AngleTo(k, full):F3} rad off him");
            Assert.Equal(0, k.Loft);
            double distance = Math.Sqrt((full - Keeper).LengthSquared);
            Assert.Equal(Simulator.PassSpeedFor(C, distance) / KickSpeed, k.Strength, 9);
        }

        [Fact]
        public void A_marked_teammate_or_one_behind_an_opponent_is_passed_over_for_an_open_one()
        {
            var marked = new Vec2(40, 12);
            var screened = new Vec2(38, -12);
            var open = new Vec2(40, 26);
            KickIntent k = Kick(Holding(
                Mate(marked.X, marked.Y), Opponent(marked.X - 1.5, marked.Y),
                Mate(screened.X, screened.Y), Opponent(44, -6),
                Mate(open.X, open.Y)));
            Assert.True(AngleTo(k, open) < 0.02, $"not to the open one: {AngleTo(k, open):F3} rad off him");
        }

        [Fact]
        public void With_no_one_open_he_kicks_long_toward_his_furthest_forward_teammate()
        {
            var forward = new Vec2(-5, 10);
            KickIntent k = Kick(Holding(
                Mate(40, 12), Opponent(39, 12),
                Mate(forward.X, forward.Y), Opponent(-4, 10)));
            Assert.True(k.Loft > 0, "a long kick goes in the air");
            Assert.True(AngleTo(k, forward) < 0.05, $"{AngleTo(k, forward):F3} rad off the furthest forward");
        }

        [Fact]
        public void A_teammate_beyond_his_throw_range_is_not_given_it_short()
        {
            var far = new Vec2(50 - C.KeeperThrowRange - 5, 0);
            KickIntent k = Kick(Holding(Mate(far.X, far.Y)));
            Assert.True(k.Loft > 0, "beyond his range the ball goes long");
        }

        // Three quarters pooled over five and ten a side, as #134 measured it; each size at least two
        // thirds, above the blind kick's 65 %. Three seeds a size swing the share at five a side by
        // several points (#118 measured 73–78 % over eight), so the size alone is no measure.
        [Fact]
        public void In_cpu_matches_most_of_his_releases_reach_his_own_side()
        {
            (int own5, int all5) = Releases(5);
            (int own10, int all10) = Releases(10);
            Assert.True(own5 >= 2.0 / 3 * all5, $"five a side: {own5} of {all5} releases reached his own side");
            Assert.True(own10 >= 2.0 / 3 * all10, $"ten a side: {own10} of {all10} releases reached his own side");
            Assert.True(own5 + own10 >= 0.75 * (all5 + all10), $"{own5 + own10} of {all5 + all10} releases reached his own side");
        }

        private static (int Own, int Releases) Releases(int perSide)
        {
            int releases = 0, own = 0;
            for (ulong seed = 0x134000; seed < 0x134000 + 3; seed++)
            {
                var sim = new Simulator(C, Squads.Match(C, seed, perSide, bothCpu: true));
                var commands = new List<Command>();
                var events = new List<SimEvent>();
                int pending = 0;
                while (sim.Tick < 2 * C.HalfTicks)
                {
                    commands.Clear();
                    events.Clear();
                    KeeperControllers.Append(sim, commands);
                    CpuControllers.Append(sim, commands);
                    RestartControllers.Append(sim, commands);
                    sim.Step(commands, events);
                    SimState s = sim.PeekState();
                    int TeamOf(int id) => s.Players.FirstOrDefault(p => p.Id == id).TeamId;
                    foreach (SimEvent e in events)
                    {
                        bool keeper = s.Keepers.Any(k => k.PlayerId == e.Subject);
                        if (e.Kind == SimEventKind.Kicked && keeper && s.Restart.Phase == RestartPhase.Live && pending == 0)
                        {
                            releases++;
                            pending = TeamOf(e.Subject);
                        }
                        else if (pending != 0 && (e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.Headed || e.Kind == SimEventKind.Volleyed || e.Kind == SimEventKind.KeeperHeld))
                        {
                            if (TeamOf(e.Subject) == pending) own++;
                            pending = 0;
                        }
                        else if (pending != 0 && e.Kind == SimEventKind.RestartAwarded)
                        {
                            pending = 0;
                        }
                    }
                }
            }

            Assert.True(releases >= 8, $"only {releases} releases at {perSide} a side");
            return (own, releases);
        }
    }
}
