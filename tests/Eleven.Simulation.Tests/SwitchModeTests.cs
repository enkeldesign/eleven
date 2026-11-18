using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #136, from Erik's session: "I don't understand the switch system." How automatic switching
    /// is becomes a choice: auto (#93's), on the ball only (a pass's receiver and SWITCH), or
    /// manual (SWITCH only). Each switch says why, and the host can show who SWITCH would take.
    /// </summary>
    public class SwitchModeTests
    {
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        private const int Human = 1, Mate = 2, Opponent = 4;

        // The 2v2 world, live: the human on 1, his teammate 2 by the ball, the opponents away.
        private static Simulator Live()
        {
            SimState s = Squads.Match(C, 5, perSide: 2);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.PhaseTicks = 0;
            s.Players[0].Position = new Vec2(-30, 20);
            s.Players[1].Position = new Vec2(5, 0);
            s.Players[3].Position = new Vec2(9, 0);
            s.Ball.Position = new Vec3(7, 0, C.BallRadius);
            return new Simulator(C, s);
        }

        private static readonly SimEvent Pass = new SimEvent(0, SimEventKind.QuickPassResolved, Human, other: Mate);
        private static readonly SimEvent MateTouched = new SimEvent(0, SimEventKind.Touched, Mate);
        private static readonly SimEvent Turnover = new SimEvent(0, SimEventKind.Touched, Opponent);

        private static (int Target, SwitchCause Cause) Decide(SwitchMode mode, SimEvent? e, bool manual = false)
        {
            var switcher = new SwitchController(Human, mode);
            int target = switcher.Decide(Live(), e.HasValue ? new[] { e.Value } : new SimEvent[0], manual);
            return (target, switcher.LastCause);
        }

        [Fact]
        public void Auto_switches_at_a_pass_a_teammate_on_the_ball_and_a_turnover_and_says_why()
        {
            Assert.Equal((Mate, SwitchCause.Pass), Decide(SwitchMode.Auto, Pass));
            Assert.Equal((Mate, SwitchCause.MateHasBall), Decide(SwitchMode.Auto, MateTouched));
            Assert.Equal((Mate, SwitchCause.Turnover), Decide(SwitchMode.Auto, Turnover));
            Assert.Equal((Mate, SwitchCause.Asked), Decide(SwitchMode.Auto, null, manual: true));
            Assert.Equal((0, SwitchCause.None), Decide(SwitchMode.Auto, null));
        }

        [Fact]
        public void On_the_ball_only_switches_to_a_pass_receiver_and_on_switch()
        {
            Assert.Equal((Mate, SwitchCause.Pass), Decide(SwitchMode.OnTheBall, Pass));
            Assert.Equal((0, SwitchCause.None), Decide(SwitchMode.OnTheBall, MateTouched));
            Assert.Equal((0, SwitchCause.None), Decide(SwitchMode.OnTheBall, Turnover));
            Assert.Equal((Mate, SwitchCause.Asked), Decide(SwitchMode.OnTheBall, null, manual: true));
        }

        [Fact]
        public void Manual_switches_only_on_switch()
        {
            Assert.Equal((0, SwitchCause.None), Decide(SwitchMode.Manual, Pass));
            Assert.Equal((0, SwitchCause.None), Decide(SwitchMode.Manual, MateTouched));
            Assert.Equal((0, SwitchCause.None), Decide(SwitchMode.Manual, Turnover));
            Assert.Equal((Mate, SwitchCause.Asked), Decide(SwitchMode.Manual, null, manual: true));
        }

        [Fact]
        public void The_mode_may_change_between_steps()
        {
            var switcher = new SwitchController(Human, SwitchMode.Manual);
            Simulator sim = Live();
            Assert.Equal(0, switcher.Decide(sim, new[] { Turnover }, false));
            switcher.Mode = SwitchMode.Auto;
            Assert.Equal(Mate, switcher.Decide(sim, new[] { Turnover }, false));
        }

        [Fact]
        public void Next_is_whom_switch_would_take_and_changes_nothing()
        {
            // Eleven a side: several teammates, one of them first to the ball.
            SimState s = Squads.Match(C, 7, perSide: 10);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.PhaseTicks = 0;
            var sim = new Simulator(C, s);
            ulong hash = sim.StateHash();
            var switcher = new SwitchController(Human);
            int next = switcher.Next(sim);
            Assert.NotEqual(0, next);
            Assert.False(switcher.ManualHold);
            Assert.Equal(hash, sim.StateHash());
            Assert.Equal(next, new SwitchController(Human).Decide(sim, new SimEvent[0], manual: true));
        }

        [Fact]
        public void A_seat_trial_counts_a_refused_switch_and_each_switch_by_its_cause()
        {
            // #117: a refusal's reason is in Rejection; Other is 0.
            var trial = new SquadTrial();
            trial.Count(new SimEvent(0, SimEventKind.CommandRejected, Human, CommandRejection.SwitchRefused), Human, SwitchCause.Asked);
            Assert.Equal(1, trial.SwitchesRefused);

            trial.Count(new SimEvent(1, SimEventKind.Switched, Mate, other: Human), Human, SwitchCause.Pass);
            trial.Count(new SimEvent(2, SimEventKind.Switched, Human, other: Mate), Human, SwitchCause.Turnover);
            trial.Count(new SimEvent(3, SimEventKind.Switched, Mate, other: Human), Human, SwitchCause.Turnover);
            Assert.Equal((3, 1, 0, 2, 0), (trial.Switches, trial.SwitchesPass, trial.SwitchesMate, trial.SwitchesTurnover, trial.SwitchesAsked));
        }

        [Fact]
        public void Seat_trials_in_a_match_count_switches_by_cause_that_add_up()
        {
            SquadTrial t = SquadTrials.One(C, 2, 0x136000, seat: true);
            Assert.True(t.Switches > 0);
            Assert.Equal(t.Switches, t.SwitchesPass + t.SwitchesMate + t.SwitchesTurnover + t.SwitchesAsked);
        }
    }
}
