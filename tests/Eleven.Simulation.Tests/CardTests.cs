using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #108: cards (Law 12). A foul's severity, as the referee reads it, cautions a reckless foul and
    /// sends off one of excessive force; a second caution sends off; denying an obvious
    /// goal-scoring opportunity sends off, or cautions in the area. A footballer sent off leaves
    /// play for the rest of the match. Only at eleven a side (cardsFrom).
    /// </summary>
    public class CardTests
    {
        // The challenge's own mechanics, shielding off (#97); the referee's reading exact.
        private static readonly SimConfig Base = SpikeScenario.Config().With("shieldSpeed", 0).With("cardNoise", 0);

        private const int Side = 10;
        private static readonly int Carrier = Squads.HomeOutfielder(0), Mate = Squads.HomeOutfielder(1);
        private static readonly int Defender = Squads.AwayOutfielder(Side, 0);

        // Eleven a side in play, home attacking +x: the carrier at `at` with the ball ahead of his
        // feet, the defender a step from the end of a standing challenge along +x from just behind
        // him, the ball beyond the defender's reach. Everyone else far off in their own half.
        private static SimState Fouling(SimConfig config, Vec2 at, int perSide = Side, bool homeFouls = false)
        {
            SimState s = Squads.Match(config, 3, perSide);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.TakerId = 0;
            s.Restart.LastTouchedTeam = homeFouls ? Squads.AwayTeam : Squads.HomeTeam;
            for (int i = 0; i < s.Players.Length; i++)
            {
                int id = s.Players[i].Id;
                bool keeper = s.Keepers.Any(k => k.PlayerId == id);
                bool home = s.Players[i].TeamId == Squads.HomeTeam;
                s.Players[i].Position = keeper ? new Vec2(home ? -51 : 51, 0) : new Vec2(home ? -45 : -30, -30 + 2.5 * i);
                s.Players[i].Velocity = Vec2.Zero;
            }

            int carrier = homeFouls ? Squads.AwayOutfielder(perSide, 0) : Squads.HomeOutfielder(0);
            int fouler = homeFouls ? Squads.HomeOutfielder(0) : Squads.AwayOutfielder(perSide, 0);
            int c = Index(s, carrier), f = Index(s, fouler);
            s.Players[c].Position = at;
            s.Ball.Position = new Vec3(at.X + 0.7, at.Y, config.BallRadius);
            s.Ball.LastPlayedBy = carrier;
            s.Players[f].Position = at + new Vec2(-0.3, 0);
            s.Players[f].Challenge = ChallengePhase.Commit;
            s.Players[f].ChallengeTicks = 1;
            s.Players[f].ChallengeLine = new Vec2(1, 0);
            return s;
        }

        private static int Index(SimState s, int id) => Array.FindIndex(s.Players, p => p.Id == id);

        private static PlayerState Of(Simulator sim, int id) => sim.PeekState().Players[Index(sim.PeekState(), id)];

        private static List<SimEvent> Step(Simulator sim)
        {
            var events = new List<SimEvent>();
            sim.Step(Array.Empty<Command>(), events);
            return events;
        }

        [Fact]
        public void A_careless_foul_is_a_free_kick_only()
        {
            SimConfig c = Base.With("cautionFrom", 2).With("sendOffFrom", 2);
            var sim = new Simulator(c, Fouling(c, new Vec2(-10, 0)));
            List<SimEvent> e = Step(sim);
            Assert.Contains(e, x => x.Kind == SimEventKind.Foul && x.Subject == Defender);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.Cautioned || x.Kind == SimEventKind.SentOff);
            Assert.Equal(0, Of(sim, Defender).Cautions);
        }

        [Fact]
        public void A_reckless_foul_is_a_caution()
        {
            SimConfig c = Base.With("cautionFrom", 0).With("sendOffFrom", 2);
            var sim = new Simulator(c, Fouling(c, new Vec2(-10, 0)));
            List<SimEvent> e = Step(sim);
            SimEvent caution = Assert.Single(e, x => x.Kind == SimEventKind.Cautioned);
            Assert.Equal((Defender, 1), (caution.Subject, caution.Other));
            Assert.Equal(1, Of(sim, Defender).Cautions);
            Assert.False(Of(sim, Defender).SentOff);
        }

        [Fact]
        public void A_second_caution_sends_him_off()
        {
            SimConfig c = Base.With("cautionFrom", 0).With("sendOffFrom", 2);
            SimState s = Fouling(c, new Vec2(-10, 0));
            s.Players[Index(s, Defender)].Cautions = 1;
            var sim = new Simulator(c, s);
            List<SimEvent> e = Step(sim);
            Assert.Contains(e, x => x.Kind == SimEventKind.Cautioned && x.Subject == Defender && x.Other == 2);
            Assert.Contains(e, x => x.Kind == SimEventKind.SentOff && x.Subject == Defender);
            Assert.True(Of(sim, Defender).SentOff);
        }

        [Fact]
        public void Excessive_force_sends_him_off_at_once()
        {
            SimConfig c = Base.With("cautionFrom", 0).With("sendOffFrom", 0);
            var sim = new Simulator(c, Fouling(c, new Vec2(-10, 0)));
            List<SimEvent> e = Step(sim);
            Assert.Contains(e, x => x.Kind == SimEventKind.SentOff && x.Subject == Defender);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.Cautioned);
            Assert.True(Of(sim, Defender).SentOff);
        }

        [Fact]
        public void Denying_an_obvious_goal_scoring_opportunity_outside_the_area_sends_him_off()
        {
            // 22 m from goal, outside the area, nobody but the keeper between him and the goal.
            SimConfig c = Base.With("cautionFrom", 2).With("sendOffFrom", 2);
            var sim = new Simulator(c, Fouling(c, new Vec2(30, 0)));
            List<SimEvent> e = Step(sim);
            Assert.Contains(e, x => x.Kind == SimEventKind.SentOff && x.Subject == Defender);
        }

        [Fact]
        public void Denying_one_in_the_area_while_playing_for_the_ball_is_a_penalty_and_a_caution()
        {
            SimConfig c = Base.With("cautionFrom", 2).With("sendOffFrom", 2);
            var sim = new Simulator(c, Fouling(c, new Vec2(42, 0)));
            List<SimEvent> e = Step(sim);
            Assert.Contains(e, x => x.Kind == SimEventKind.RestartAwarded && x.Other == (int)RestartKind.Penalty);
            Assert.Contains(e, x => x.Kind == SimEventKind.Cautioned && x.Subject == Defender);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.SentOff);
        }

        [Fact]
        public void A_defender_between_him_and_the_goal_makes_it_no_obvious_opportunity()
        {
            SimConfig c = Base.With("cautionFrom", 2).With("sendOffFrom", 2);
            SimState s = Fouling(c, new Vec2(30, 0));
            s.Players[Index(s, Squads.AwayOutfielder(Side, 1))].Position = new Vec2(40, 3);
            var sim = new Simulator(c, s);
            Assert.DoesNotContain(Step(sim), x => x.Kind == SimEventKind.SentOff || x.Kind == SimEventKind.Cautioned);
        }

        [Fact]
        public void Small_sides_play_without_cards()
        {
            SimConfig c = Base.With("cautionFrom", 0).With("sendOffFrom", 0);
            var sim = new Simulator(c, Fouling(c, new Vec2(-10, 0), perSide: 5));
            List<SimEvent> e = Step(sim);
            Assert.Contains(e, x => x.Kind == SimEventKind.Foul);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.Cautioned || x.Kind == SimEventKind.SentOff);
        }

        [Fact]
        public void Sent_off_he_leaves_the_pitch_and_play_his_side_reslotted_and_unseen()
        {
            SimConfig c = Base.With("cautionFrom", 0).With("sendOffFrom", 0);
            var sim = new Simulator(c, Fouling(c, new Vec2(-10, 0)));
            Step(sim);
            SimState s = sim.PeekState();
            PlayerState off = Of(sim, Defender);
            Assert.True(Math.Abs(off.Position.Y) > c.TouchlineY + 5, $"at {off.Position}");
            Assert.Equal(0, off.Slot);
            Assert.DoesNotContain(s.Cpus, x => x.PlayerId == Defender);
            Assert.Equal(Enumerable.Range(1, Side - 1), s.Players.Where(p => p.TeamId == Squads.AwayTeam && p.Slot > 0).Select(p => p.Slot).OrderBy(x => x));

            // No CPU footballer sees anyone off the pitch, and the free kick's set-up leaves him there.
            for (int i = 0; i < s.Cpus.Length; i++)
            {
                OutfieldObservation seen = Perception.OfOutfielder(s, i);
                Assert.All(seen.Others, o => Assert.True(Math.Abs(o.Position.Y) < c.TouchlineY + 3, $"someone seen at {o.Position}"));
            }

            for (int t = 0; t < 4 * c.TickRate; t++) Step(sim);
            Assert.True(Math.Abs(Of(sim, Defender).Position.Y) > c.TouchlineY + 5);
        }

        [Fact]
        public void When_the_humans_footballer_is_sent_off_his_controller_takes_a_teammate()
        {
            SimConfig c = Base.With("cautionFrom", 0).With("sendOffFrom", 0);
            var sim = new Simulator(c, Fouling(c, new Vec2(-10, 0), homeFouls: true));
            List<SimEvent> e = Step(sim);
            Assert.Contains(e, x => x.Kind == SimEventKind.SentOff && x.Subject == Carrier);
            SimState s = sim.PeekState();
            PlayerState now = s.Players.Single(p => p.OwnerControllerId == SquadTrials.SeatController);
            Assert.NotEqual(Carrier, now.Id);
            Assert.Equal(Squads.HomeTeam, now.TeamId);
            Assert.False(now.SentOff);
            Assert.DoesNotContain(s.Keepers, k => k.PlayerId == now.Id);
            Assert.DoesNotContain(s.Cpus, x => x.PlayerId == now.Id);
        }

        [Fact]
        public void Cards_survive_a_snapshot()
        {
            SimConfig c = Base.With("cautionFrom", 0).With("sendOffFrom", 2);
            var sim = new Simulator(c, Fouling(c, new Vec2(-10, 0)));
            Step(sim);
            Simulator back = Snapshot.Restore(Snapshot.Capture(sim), c);
            Assert.Equal(1, Of(back, Defender).Cautions);
            Assert.Equal(sim.StateHash(), back.StateHash());
        }
    }
}
