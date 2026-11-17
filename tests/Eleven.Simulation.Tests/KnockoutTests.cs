using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #111: a knockout needs a winner. Level after two halves, it plays extra time: two shorter
    /// halves, the ends changing. Level still, kicks from the penalty mark: five each, alternating,
    /// then sudden death, ending as soon as one side can't be caught. A league match's draw stands.
    /// </summary>
    public class KnockoutTests
    {
        private static readonly SimConfig League = SpikeScenario.Config();
        private static readonly SimConfig Knockout = League.With("knockout", 1);

        private const int Side = 2;
        private const int Home = Squads.HomeTeam, Away = Squads.AwayTeam;

        // Two a side, CPU both, in play with the ball at rest in midfield, one step from the end of `phase`.
        private static SimState AtTheEndOf(SimConfig c, MatchPhase phase, int home = 0, int away = 0)
        {
            SimState s = Squads.Match(c, 3, Side, bothCpu: true);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.AwardedTeam = 0;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            s.Ball.Position = new Vec3(0, 20, c.BallRadius);
            s.Match.Phase = phase;
            bool extra = phase == MatchPhase.ExtraFirstHalf || phase == MatchPhase.ExtraSecondHalf;
            s.Match.ClockTicks = (extra ? c.ExtraTicks : c.HalfTicks) - 1;
            s.Teams[0].Goals = home;
            s.Teams[1].Goals = away;
            return s;
        }

        private static List<SimEvent> Step(Simulator sim, int steps = 1)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < steps; i++) sim.Step(Array.Empty<Command>(), events);
            return events;
        }

        private static TeamState Team(Simulator sim, int id) => sim.PeekState().Teams.Single(t => t.Id == id);

        [Fact]
        public void In_a_league_match_a_draw_stands()
        {
            var sim = new Simulator(League, AtTheEndOf(League, MatchPhase.SecondHalf, 1, 1));
            List<SimEvent> e = Step(sim);
            Assert.Equal(MatchPhase.FullTime, sim.PeekState().Match.Phase);
            Assert.Contains(e, x => x.Kind == SimEventKind.FullTime);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.ExtraTime);
        }

        [Fact]
        public void A_knockout_with_a_winner_after_two_halves_is_over()
        {
            var sim = new Simulator(Knockout, AtTheEndOf(Knockout, MatchPhase.SecondHalf, 2, 1));
            Assert.Contains(Step(sim), x => x.Kind == SimEventKind.FullTime);
            Assert.Equal(MatchPhase.FullTime, sim.PeekState().Match.Phase);
        }

        [Fact]
        public void A_knockout_level_after_two_halves_goes_to_extra_time_with_the_ends_changed()
        {
            SimState s = AtTheEndOf(Knockout, MatchPhase.SecondHalf, 1, 1);
            int homeSign = s.Teams[0].AttackSign;
            var sim = new Simulator(Knockout, s);
            List<SimEvent> e = Step(sim);
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.FullTime);
            Assert.Contains(e, x => x.Kind == SimEventKind.ExtraTime && x.Other == 0);
            Assert.Equal(MatchPhase.ExtraBreak, sim.PeekState().Match.Phase);

            // After the break: the first extra half, the clock at nought, a kick-off, the ends changed.
            e = Step(sim, Knockout.HalfTimeTicks);
            SimState after = sim.PeekState();
            Assert.Contains(e, x => x.Kind == SimEventKind.ExtraTime && x.Other == 1);
            Assert.Equal((MatchPhase.ExtraFirstHalf, 0), (after.Match.Phase, after.Match.ClockTicks));
            Assert.Equal(RestartKind.KickOff, after.Restart.Kind);
            Assert.Equal(-homeSign, after.Teams[0].AttackSign);
        }

        [Fact]
        public void Extra_time_is_two_halves_of_extraTimeShare_of_a_half_with_the_ends_changing_between()
        {
            Assert.Equal((int)Math.Round(Knockout.HalfTicks * Knockout.ExtraTimeShare), Knockout.ExtraTicks);
            SimState s = AtTheEndOf(Knockout, MatchPhase.ExtraFirstHalf, 1, 1);
            int homeSign = s.Teams[0].AttackSign;
            var sim = new Simulator(Knockout, s);
            Step(sim);
            Assert.Equal(MatchPhase.ExtraHalfTime, sim.PeekState().Match.Phase);
            List<SimEvent> e = Step(sim, Knockout.HalfTimeTicks);
            SimState after = sim.PeekState();
            Assert.Contains(e, x => x.Kind == SimEventKind.ExtraTime && x.Other == 2);
            Assert.Equal((MatchPhase.ExtraSecondHalf, 0), (after.Match.Phase, after.Match.ClockTicks));
            Assert.Equal(-homeSign, after.Teams[0].AttackSign);

            // The side that didn't kick off extra time's first half kicks off its second.
            Assert.Equal(RestartKind.KickOff, after.Restart.Kind);
            Assert.NotEqual(after.Match.FirstKickOff, after.Restart.AwardedTeam);
        }

        [Fact]
        public void A_winner_after_extra_time_ends_the_match_and_the_result_records_extra_time()
        {
            var sim = new Simulator(Knockout, AtTheEndOf(Knockout, MatchPhase.ExtraSecondHalf, 2, 1));
            var recorder = new MatchRecorder();
            recorder.Observe(Knockout, sim.PeekState(), new[] { new SimEvent(1, SimEventKind.ExtraTime, 0) });
            var events = new List<SimEvent>();
            sim.Step(Array.Empty<Command>(), events);
            recorder.Observe(Knockout, sim.PeekState(), events);
            Assert.Equal(MatchPhase.FullTime, sim.PeekState().Match.Phase);
            Assert.NotNull(recorder.Result);
            Assert.Equal(1, recorder.Result!.ExtraTime);
            Assert.Equal(Home, recorder.Result.Winner);
            Assert.Equal(2 * (Knockout.HalfTicks + Knockout.ExtraTicks) * Knockout.TickSeconds, recorder.Result.PlayedSeconds, 6);
            Assert.Contains("extratime=1\n", recorder.Result.ToText());
        }

        [Fact]
        public void Level_after_extra_time_a_shootout_starts_with_a_penalty_for_the_side_that_kicked_off()
        {
            var sim = new Simulator(Knockout, AtTheEndOf(Knockout, MatchPhase.ExtraSecondHalf, 1, 1));
            List<SimEvent> e = Step(sim);
            SimState s = sim.PeekState();
            Assert.DoesNotContain(e, x => x.Kind == SimEventKind.FullTime);
            Assert.Contains(e, x => x.Kind == SimEventKind.ShootoutStarted);
            Assert.Equal(MatchPhase.Shootout, s.Match.Phase);
            Assert.Equal((RestartKind.Penalty, s.Match.FirstKickOff), (s.Restart.Kind, s.Restart.AwardedTeam));
        }

        // The shootout's next kick, by the taker's own command: at a corner of the goal past a keeper
        // no one moves, or wide of it. Returns the kick's event.
        private static SimEvent Kick(Simulator sim, bool score)
        {
            SimConfig c = sim.Config;
            var events = new List<SimEvent>();
            for (int t = 0; t < 30 * c.TickRate; t++)
            {
                SimState s = sim.PeekState();
                var commands = new List<Command>();
                RestartState r = s.Restart;
                if (r.Phase == RestartPhase.Ready && r.PhaseTicks == 0)
                {
                    PlayerState taker = s.Players.Single(p => p.Id == r.TakerId);
                    int sign = s.Teams.Single(x => x.Id == taker.TeamId).AttackSign;
                    Vec2 at = score ? new Vec2(sign * c.GoalLineX, c.GoalWidth / 2 - 0.6) : new Vec2(sign * c.GoalLineX, c.GoalWidth / 2 + 6);
                    commands.Add(new Command(taker.OwnerControllerId, s.Tick, Vec2.Zero, new KickIntent(at - s.Ball.Position.Horizontal, 0.9, 0.04)));
                }

                events.Clear();
                sim.Step(commands, events);
                int kick = events.FindIndex(x => x.Kind == SimEventKind.ShootoutKick);
                if (kick >= 0) return events[kick];
            }

            throw new Xunit.Sdk.XunitException("no shootout kick ended in 30 s");
        }

        private static Simulator InAShootout()
        {
            var sim = new Simulator(Knockout, AtTheEndOf(Knockout, MatchPhase.ExtraSecondHalf, 1, 1));
            Step(sim);
            Assert.Equal(MatchPhase.Shootout, sim.PeekState().Match.Phase);
            return sim;
        }

        [Fact]
        public void The_sides_kick_in_turn_and_a_kick_scores_or_not_without_counting_as_a_match_goal()
        {
            Simulator sim = InAShootout();
            int first = sim.PeekState().Match.FirstKickOff, second = first == Home ? Away : Home;
            SimEvent a = Kick(sim, score: true);
            Assert.Equal(1, a.Other);
            Assert.Equal(first, sim.PeekState().Players.Single(p => p.Id == a.Subject).TeamId);
            Assert.Equal((1, 1), (Team(sim, first).ShootoutKicks, Team(sim, first).ShootoutGoals));

            SimEvent b = Kick(sim, score: false);
            Assert.Equal(0, b.Other);
            Assert.Equal(second, sim.PeekState().Players.Single(p => p.Id == b.Subject).TeamId);
            Assert.Equal((1, 0), (Team(sim, second).ShootoutKicks, Team(sim, second).ShootoutGoals));

            // The match's score is still level.
            Assert.Equal((1, 1), (Team(sim, Home).Goals, Team(sim, Away).Goals));
            Assert.Equal(MatchPhase.Shootout, sim.PeekState().Match.Phase);
        }

        [Fact]
        public void A_sides_takers_take_turns()
        {
            Simulator sim = InAShootout();
            var takers = new List<int>();
            for (int i = 0; i < 4; i++) takers.Add(Kick(sim, score: true).Subject);

            // Two a side: each side's two outfielders, one after the other.
            Assert.Equal(4, takers.Distinct().Count());
        }

        [Fact]
        public void The_shootout_ends_as_soon_as_one_side_cant_be_caught()
        {
            // Three each, the first side scoring all and the second none: 3-0 with two kicks left each.
            Simulator sim = InAShootout();
            int first = sim.PeekState().Match.FirstKickOff;
            var recorder = new MatchRecorder();
            for (int i = 0; i < 3; i++)
            {
                Kick(sim, score: true);
                Assert.Equal(MatchPhase.Shootout, sim.PeekState().Match.Phase);
                Kick(sim, score: false);
            }

            Assert.Equal(MatchPhase.FullTime, sim.PeekState().Match.Phase);
            recorder.Observe(Knockout, sim.PeekState(), new[] { new SimEvent(sim.Tick, SimEventKind.FullTime, 0) });
            Assert.Equal(first, recorder.Result!.Winner);
            Assert.Equal((1, 1), (recorder.Result.HomeGoals, recorder.Result.AwayGoals));
            Assert.Equal(3, recorder.Result.HomeShootoutKicks + recorder.Result.AwayShootoutKicks - 3);
            Assert.Contains($"winner={first}\n", recorder.Result.ToText());
        }

        [Fact]
        public void Level_after_five_each_it_is_sudden_death()
        {
            Simulator sim = InAShootout();
            for (int i = 0; i < 5; i++)
            {
                Kick(sim, score: true);
                Kick(sim, score: true);
            }

            // 5-5: on it goes. A pair both scored changes nothing; then one scores and one doesn't.
            Assert.Equal(MatchPhase.Shootout, sim.PeekState().Match.Phase);
            Kick(sim, score: true);
            Kick(sim, score: true);
            Assert.Equal(MatchPhase.Shootout, sim.PeekState().Match.Phase);
            Kick(sim, score: false);
            Assert.Equal(MatchPhase.Shootout, sim.PeekState().Match.Phase);
            Kick(sim, score: true);
            Assert.Equal(MatchPhase.FullTime, sim.PeekState().Match.Phase);
            int first = sim.PeekState().Match.FirstKickOff, second = first == Home ? Away : Home;
            Assert.Equal((7, 6), (Team(sim, first).ShootoutKicks, Team(sim, first).ShootoutGoals));
            Assert.Equal((7, 7), (Team(sim, second).ShootoutKicks, Team(sim, second).ShootoutGoals));
        }

        [Fact]
        public void A_kick_no_one_ends_is_a_miss_after_shootoutKickTime()
        {
            // A kick so soft the ball stops short of the goal: called a miss when the time is up.
            SimConfig c = Knockout.With("shootoutKickTime", 2);
            var sim = new Simulator(c, AtTheEndOf(c, MatchPhase.ExtraSecondHalf, 1, 1));
            Step(sim);
            var events = new List<SimEvent>();
            long kicked = -1;
            for (int t = 0; t < 30 * c.TickRate && !events.Any(x => x.Kind == SimEventKind.ShootoutKick); t++)
            {
                SimState s = sim.PeekState();
                var commands = new List<Command>();
                if (s.Restart.Phase == RestartPhase.Ready && s.Restart.PhaseTicks == 0)
                {
                    PlayerState taker = s.Players.Single(p => p.Id == s.Restart.TakerId);
                    int sign = s.Teams.Single(x => x.Id == taker.TeamId).AttackSign;
                    commands.Add(new Command(taker.OwnerControllerId, s.Tick, Vec2.Zero, new KickIntent(new Vec2(sign, 0), 0.05, 0)));
                }

                sim.Step(commands, events);
                if (kicked < 0 && events.Any(x => x.Kind == SimEventKind.RestartTaken && x.Other == (int)RestartKind.Penalty)) kicked = sim.Tick;
            }

            SimEvent kick = Assert.Single(events, x => x.Kind == SimEventKind.ShootoutKick);
            Assert.Equal(0, kick.Other);
            Assert.InRange(kick.Tick - kicked, c.ShootoutKickTicks - 2, c.ShootoutKickTicks + 2);
        }

        [Fact]
        public void In_a_shootout_only_the_taker_and_the_keeper_he_faces_move()
        {
            Simulator sim = InAShootout();
            SimConfig c = sim.Config;
            var events = new List<SimEvent>();
            var start = new Dictionary<int, Vec2>();
            int taker = 0, keeper = 0;
            for (int t = 0; t < 30 * c.TickRate && !events.Any(x => x.Kind == SimEventKind.ShootoutKick); t++)
            {
                SimState s = sim.PeekState();
                if (s.Restart.Phase == RestartPhase.Ready && start.Count == 0)
                {
                    taker = s.Restart.TakerId;
                    int kicking = s.Restart.AwardedTeam;
                    keeper = s.Keepers.Select(k => k.PlayerId).Single(id => s.Players.Single(p => p.Id == id).TeamId != kicking);
                    foreach (PlayerState p in s.Players) start[p.Id] = p.Position;
                }

                // Every controller but the taker's pushes his stick; the taker's own command kicks when he may.
                var commands = new List<Command>();
                foreach (PlayerState p in s.Players)
                {
                    KickIntent? kick = p.Id == s.Restart.TakerId && s.Restart.Phase == RestartPhase.Ready && s.Restart.PhaseTicks == 0
                        ? new KickIntent(new Vec2(s.Teams.Single(x => x.Id == p.TeamId).AttackSign, 0.2), 0.9, 0.04)
                        : (KickIntent?)null;
                    commands.Add(new Command(p.OwnerControllerId, s.Tick, p.Id == s.Restart.TakerId ? Vec2.Zero : new Vec2(0, 1), kick));
                }

                sim.Step(commands, events);
                if (start.Count > 0 && !events.Any(x => x.Kind == SimEventKind.ShootoutKick))
                {
                    foreach (PlayerState p in sim.PeekState().Players)
                    {
                        if (p.Id == taker || p.Id == keeper) continue;
                        Assert.True((p.Position - start[p.Id]).LengthSquared < 1e-12, $"player {p.Id} moved from {start[p.Id]} to {p.Position}");
                    }
                }
            }

            Assert.Contains(events, x => x.Kind == SimEventKind.ShootoutKick);
            Assert.NotEmpty(start);
        }

        [Theory]
        [InlineData(ControlModel.Free)]
        [InlineData(ControlModel.Attached)]
        public void After_a_shootout_kick_the_taker_cannot_play_the_ball_again(ControlModel model)
        {
            // A kick so soft it stays at his feet: he asks to touch it and to kick it again, at the
            // goal, every step. Only the keeper he faces may play it now; the kick is a miss.
            SimConfig c = Knockout.WithControlModel(model);
            var sim = new Simulator(c, AtTheEndOf(c, MatchPhase.ExtraSecondHalf, 1, 1));
            Step(sim);
            var events = new List<SimEvent>();
            int taker = 0, kicks = 0;
            for (int t = 0; t < 30 * c.TickRate && !events.Any(x => x.Kind == SimEventKind.ShootoutKick); t++)
            {
                SimState s = sim.PeekState();
                RestartState r = s.Restart;
                if (r.Phase == RestartPhase.Ready && taker == 0) taker = r.TakerId;
                var commands = new List<Command>();
                if (taker != 0)
                {
                    PlayerState me = s.Players.Single(p => p.Id == taker);
                    int sign = s.Teams.Single(x => x.Id == me.TeamId).AttackSign;
                    Vec2 toGoal = new Vec2(sign * c.GoalLineX, 0) - s.Ball.Position.Horizontal;
                    bool first = r.Phase == RestartPhase.Ready && r.PhaseTicks == 0;
                    KickIntent? kick = first ? new KickIntent(toGoal, 0.02, 0) : r.Phase == RestartPhase.Live ? new KickIntent(toGoal, 1, 0.05) : (KickIntent?)null;
                    commands.Add(new Command(me.OwnerControllerId, s.Tick, Vec2.Zero, kick));
                }

                int before = events.Count;
                sim.Step(commands, events);
                kicks += events.Skip(before).Count(x => x.Kind == SimEventKind.Kicked && x.Subject == taker);
            }

            Assert.Equal(1, kicks);
            Assert.DoesNotContain(events, x => x.Kind == SimEventKind.Touched && x.Subject == taker);
            Assert.Equal(0, Assert.Single(events, x => x.Kind == SimEventKind.ShootoutKick).Other);
        }

        [Fact]
        public void A_whole_knockout_level_at_the_end_finds_a_winner_with_every_controller_running()
        {
            // From the end of extra time, level: the CPU takers, the keepers' brains, to the end.
            var sim = new Simulator(Knockout, AtTheEndOf(Knockout, MatchPhase.ExtraSecondHalf, 0, 0));
            var recorder = new MatchRecorder();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            var teams = new List<int>();
            for (int t = 0; t < 600 * Knockout.TickRate && recorder.Result == null; t++)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                recorder.Observe(Knockout, sim.PeekState(), events);
                foreach (SimEvent e in events)
                {
                    Assert.NotEqual(SimEventKind.Goal, e.Kind);
                    if (e.Kind == SimEventKind.ShootoutKick) teams.Add(sim.PeekState().Players.Single(p => p.Id == e.Subject).TeamId);
                }
            }

            Assert.NotNull(recorder.Result);
            MatchResult r = recorder.Result!;
            Assert.NotEqual(0, r.Winner);
            Assert.Equal((0, 0), (r.HomeGoals, r.AwayGoals));
            Assert.NotEqual(r.HomeShootoutGoals, r.AwayShootoutGoals);
            Assert.InRange(Math.Abs(r.HomeShootoutKicks - r.AwayShootoutKicks), 0, 1);
            Assert.Equal(r.HomeShootoutKicks + r.AwayShootoutKicks, teams.Count);
            for (int i = 1; i < teams.Count; i++) Assert.NotEqual(teams[i - 1], teams[i]);
            Assert.Contains($"shootout={r.HomeShootoutGoals}-{r.AwayShootoutGoals}\n", r.ToText());
        }

        [Fact]
        public void A_shootout_survives_a_snapshot()
        {
            Simulator sim = InAShootout();
            Kick(sim, score: true);
            Simulator back = Snapshot.Restore(Snapshot.Capture(sim), Knockout);
            Assert.Equal(sim.StateHash(), back.StateHash());
            Assert.Equal(MatchPhase.Shootout, back.PeekState().Match.Phase);
            SimEvent a = Kick(sim, score: false), b = Kick(back, score: false);
            Assert.Equal((a.Subject, a.Other), (b.Subject, b.Other));
            Assert.Equal(sim.StateHash(), back.StateHash());
        }
    }
}
