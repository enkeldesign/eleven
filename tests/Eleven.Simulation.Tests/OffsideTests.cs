using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #107: offside (Law 11). A footballer is in an offside position when a teammate plays the ball
    /// if he is in the opponents' half and nearer their goal line than both the ball and the
    /// second-last opponent; the offence is his next touch, given as an indirect free kick to the
    /// defenders. No offence straight from a goal kick, a throw-in or a corner; none at small sides.
    /// </summary>
    public class OffsideTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private const int Side = 10;
        private static readonly int Passer = Squads.HomeOutfielder(0), Runner = Squads.HomeOutfielder(1);
        private static readonly int Defender = Squads.AwayOutfielder(Side, 0);

        // Eleven a side in play, home attacking +x: the passer at x with the ball at his feet, the
        // runner at runnerX, every away outfielder level at defendersX and their keeper on his line.
        // Everyone else stands deep in home's half. Only the commands a test sends move anyone.
        private static SimState World(double x, double runnerX, double defendersX, int perSide = Side)
        {
            SimState s = Squads.Match(C, 1, perSide);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            for (int i = 0; i < s.Players.Length; i++)
            {
                ref PlayerState p = ref s.Players[i];
                int id = p.Id;
                bool keeper = s.Keepers.Any(k => k.PlayerId == id);
                if (p.TeamId == Squads.HomeTeam) p.Position = keeper ? new Vec2(-50, 0) : new Vec2(-40, -30 + 3 * i);
                else p.Position = keeper ? new Vec2(50, 0) : new Vec2(defendersX, -25 + 5 * (i % 10));
                p.Velocity = Vec2.Zero;
            }

            Put(s, Squads.HomeOutfielder(0), new Vec2(x, 0));
            Put(s, Squads.HomeOutfielder(1), new Vec2(runnerX, 10));
            s.Ball.Position = new Vec3(x + 0.5, 0, C.BallRadius);
            s.Ball.Velocity = Vec3.Zero;
            s.Ball.LastPlayedBy = 0;
            return s;
        }

        private static void Put(SimState s, int id, Vec2 at)
        {
            int i = Array.FindIndex(s.Players, p => p.Id == id);
            s.Players[i].Position = at;
            s.Players[i].Heading = 0;
        }

        private static int IndexOf(SimState s, int id) => Array.FindIndex(s.Players, p => p.Id == id);

        private static bool Flagged(SimState s, int id) => (s.Restart.OffsideFlags & (1UL << IndexOf(s, id))) != 0;

        // The footballer kicks the ball along +x until the kick connects: the events of every step.
        private static List<SimEvent> KickBy(Simulator sim, int id)
        {
            var all = new List<SimEvent>();
            for (int t = 0; t < C.TickRate && !all.Any(e => e.Kind == SimEventKind.Kicked); t++)
            {
                var events = new List<SimEvent>();
                sim.Step(new[] { new Command(id, sim.Tick, Vec2.Zero, new KickIntent(new Vec2(1, 0), 0.5, 0)) }, events);
                all.AddRange(events);
            }

            Assert.Contains(all, e => e.Kind == SimEventKind.Kicked && e.Subject == id);
            return all;
        }

        // The state after the passer's kick, with the ball at the runner's feet, at rest.
        private static Simulator BallTo(Simulator sim, int id)
        {
            SimState s = sim.PeekState().Clone();
            PlayerState p = s.Players[IndexOf(s, id)];
            s.Ball.Position = new Vec3(p.Position.X + 0.5, p.Position.Y, C.BallRadius);
            s.Ball.Velocity = Vec3.Zero;
            s.Ball.Spin = Vec3.Zero;
            return new Simulator(C, s);
        }

        [Fact]
        public void Beyond_the_second_last_defender_when_his_teammate_plays_it_his_next_touch_is_offside()
        {
            var sim = new Simulator(C, World(x: 10, runnerX: 30, defendersX: 20));
            KickBy(sim, Passer);
            Assert.True(Flagged(sim.PeekState(), Runner), "the runner was beyond the defenders and the ball");

            Simulator then = BallTo(sim, Runner);
            List<SimEvent> events = KickBy(then, Runner);
            SimEvent offside = Assert.Single(events, e => e.Kind == SimEventKind.Offside);
            Assert.Equal(Runner, offside.Subject);
            Assert.Equal(Squads.AwayTeam, offside.Other);
            Assert.Contains(events, e => e.Kind == SimEventKind.RestartAwarded && e.Subject == Squads.AwayTeam && e.Other == (int)RestartKind.IndirectFreeKick);
            Assert.Equal(RestartKind.IndirectFreeKick, then.PeekState().Restart.Kind);
        }

        [Theory]
        [InlineData(10, 20, 20)] // level with the second-last defender
        [InlineData(10, 8, 20)] // behind the ball
        [InlineData(-30, -5, -10)] // beyond the defenders, but in his own half
        public void Level_behind_the_ball_or_in_his_own_half_he_is_not_offside(double x, double runnerX, double defendersX)
        {
            var sim = new Simulator(C, World(x, runnerX, defendersX));
            KickBy(sim, Passer);
            Assert.False(Flagged(sim.PeekState(), Runner));
            Assert.DoesNotContain(KickBy(BallTo(sim, Runner), Runner), e => e.Kind == SimEventKind.Offside);
        }

        [Fact]
        public void An_opponents_touch_clears_the_flag()
        {
            var sim = new Simulator(C, World(x: 10, runnerX: 30, defendersX: 20));
            KickBy(sim, Passer);
            Simulator then = BallTo(sim, Defender);
            var events = new List<SimEvent>();
            for (int t = 0; t < C.TickRate && !events.Any(e => e.Kind == SimEventKind.Kicked); t++)
            {
                then.Step(new[] { new Command(Defender, then.Tick, Vec2.Zero, new KickIntent(new Vec2(-1, 0), 0.5, 0)) }, events);
            }

            Assert.Equal(0UL, then.PeekState().Restart.OffsideFlags);
        }

        [Fact]
        public void There_is_no_offence_straight_from_a_throw_in()
        {
            SimState s = World(x: 10, runnerX: 30, defendersX: 20);
            s.Restart.Phase = RestartPhase.Ready;
            s.Restart.Kind = RestartKind.ThrowIn;
            s.Restart.TakerId = Passer;
            s.Restart.AwardedTeam = Squads.HomeTeam;
            var sim = new Simulator(C, s);
            KickBy(sim, Passer);
            Assert.False(Flagged(sim.PeekState(), Runner));
        }

        [Fact]
        public void Small_sides_play_without_offside()
        {
            var sim = new Simulator(C, World(x: 10, runnerX: 30, defendersX: 20, perSide: 5));
            KickBy(sim, Passer);
            Assert.Equal(0UL, sim.PeekState().Restart.OffsideFlags);
        }

        [Fact]
        public void An_indirect_free_kick_straight_into_the_goal_scores_nothing()
        {
            SimState s = World(x: 38, runnerX: -10, defendersX: 0);
            s.Restart.Phase = RestartPhase.Ready;
            s.Restart.Kind = RestartKind.IndirectFreeKick;
            s.Restart.TakerId = Passer;
            s.Restart.AwardedTeam = Squads.HomeTeam;
            // The keeper well off his line, so the kick goes in.
            Put(s, Squads.AwayKeeper(Side), new Vec2(40, 25));
            var sim = new Simulator(C, s);
            var events = new List<SimEvent>();
            for (int t = 0; t < 4 * C.TickRate && !events.Any(e => e.Kind == SimEventKind.GoalLineCrossed); t++)
            {
                sim.Step(new[] { new Command(Passer, sim.Tick, Vec2.Zero, new KickIntent(new Vec2(1, 0), 1, 0.05)) }, events);
            }

            Assert.Contains(events, e => e.Kind == SimEventKind.GoalLineCrossed);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Goal);
            Assert.Contains(events, e => e.Kind == SimEventKind.RestartAwarded && e.Other == (int)RestartKind.GoalKick && e.Subject == Squads.AwayTeam);
        }

        [Fact]
        public void The_flags_survive_a_snapshot()
        {
            var sim = new Simulator(C, World(x: 10, runnerX: 30, defendersX: 20));
            KickBy(sim, Passer);
            SimState s = sim.PeekState();
            Assert.NotEqual(0UL, s.Restart.OffsideFlags);
            Simulator back = Snapshot.Restore(Snapshot.Capture(sim), C);
            Assert.Equal(s.Restart.OffsideFlags, back.PeekState().Restart.OffsideFlags);
            Assert.Equal(sim.StateHash(), back.StateHash());
        }
    }
}
