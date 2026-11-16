using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>The match's clock, halves, score and result (#86).</summary>
    public class MatchTests
    {
        // One-minute halves keep the whole-match tests quick; the reference is three minutes.
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        private sealed class Played
        {
            public readonly List<SimEvent> Events = new List<SimEvent>();
            public readonly List<SimState> States = new List<SimState>();
            public readonly List<Command[]> Commands = new List<Command[]>();
            public readonly MatchRecorder Recorder = new MatchRecorder();
            public Simulator Sim = null!;
        }

        private static Played Play(Simulator sim, int maxTicks, bool keepStates = false, Func<Simulator, bool>? stop = null)
        {
            var played = new Played { Sim = sim };
            var step = new List<SimEvent>();
            for (int i = 0; i < maxTicks; i++)
            {
                if (stop != null && stop(sim)) break;
                var commands = new List<Command>();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                played.Commands.Add(commands.ToArray());
                step.Clear();
                sim.Step(commands, step);
                played.Events.AddRange(step);
                played.Recorder.Observe(sim.Config, sim.PeekState(), step);
                if (keepStates) played.States.Add(sim.PeekState().Clone());
            }

            return played;
        }

        private static bool Over(Simulator s) => s.PeekState().Match.Phase == MatchPhase.FullTime;

        private static int Enough => 2 * C.HalfTicks + 60 * 60;

        [Theory]
        [InlineData(MatchPhase.FirstHalf, MatchPhase.HalfTime)]
        [InlineData(MatchPhase.SecondHalf, MatchPhase.FullTime)]
        public void A_kick_winding_up_at_the_whistle_never_connects(MatchPhase half, MatchPhase whistle)
        {
            SimState s = OneVOne.Match(C, seed: 4, bothCpu: true);
            s.Match.Phase = half;
            s.Match.ClockTicks = C.HalfTicks - 1;
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.AwardedTeam = 0;
            s.Restart.TakerId = 0;
            s.Restart.PhaseTicks = 0;
            s.Players[0].Position = new Vec2(10, 0);
            s.Players[0].Heading = 0;
            s.Ball.Position = new Vec3(10.4, 0, C.BallRadius);
            s.Ball.Velocity = Vec3.Zero;
            s.Players[0].WindUpTicks = Math.Max(2, C.KickWindUpTicks);
            s.Players[0].WindUpStrength = 1;
            var sim = new Simulator(C, s);
            var events = new List<SimEvent>();
            for (int i = 0; i < 30; i++) sim.Step(Array.Empty<Command>(), events);
            Assert.Contains(events, e => e.Kind == (whistle == MatchPhase.HalfTime ? SimEventKind.HalfTime : SimEventKind.FullTime));
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Kicked);
            Assert.Equal(whistle, sim.PeekState().Match.Phase);
            Assert.All(sim.PeekState().Players, p => Assert.Equal(0, p.WindUpTicks));
        }

        [Fact]
        public void A_cpu_match_plays_two_halves_changes_ends_and_ends_with_its_result()
        {
            Played p = Play(new Simulator(C, OneVOne.Match(C, seed: 0x86, bothCpu: true)), Enough, keepStates: true);
            SimState end = p.Sim.PeekState();
            Assert.Equal(MatchPhase.FullTime, end.Match.Phase);

            // One of each, in order.
            Assert.Single(p.Events, e => e.Kind == SimEventKind.HalfTime);
            Assert.Single(p.Events, e => e.Kind == SimEventKind.SecondHalf);
            Assert.Single(p.Events, e => e.Kind == SimEventKind.FullTime);
            long half = p.Events.Single(e => e.Kind == SimEventKind.HalfTime).Tick, second = p.Events.Single(e => e.Kind == SimEventKind.SecondHalf).Tick;
            Assert.Equal(half + C.HalfTimeTicks, second);
            Assert.True(p.Events.Single(e => e.Kind == SimEventKind.FullTime).Tick > second + C.HalfTicks);

            // Ends changed at half-time; the second half kicked off by the side that didn't kick off the first.
            Assert.Equal(new[] { -1, 1 }, end.Teams.Select(t => t.AttackSign));
            SimEvent secondKickOff = p.Events.First(e => e.Kind == SimEventKind.RestartPlaced && e.Tick > second);
            Assert.Equal(OneVOne.AwayPlayer, secondKickOff.Subject);
            SimState placed = p.States[(int)secondKickOff.Tick];
            Assert.True(placed.Players[1].Position.X > 40, "home's keeper is at his second-half goal, +x");
            Assert.True(placed.Players[3].Position.X < -40, "away's keeper is at his second-half goal, −x");

            // The score is the goals, credited to the side that scored, once each.
            int[] byGoals = { p.Events.Count(e => e.Kind == SimEventKind.Goal && e.Other == OneVOne.HomeTeam), p.Events.Count(e => e.Kind == SimEventKind.Goal && e.Other == OneVOne.AwayTeam) };
            Assert.Equal(byGoals, end.Teams.Select(t => t.Goals));
            Assert.True(byGoals.Sum() > 0, "no goal in a whole match");

            // The result: its score, its goals and its length.
            MatchResult result = Assert.IsType<MatchResult>(p.Recorder.Result);
            Assert.Equal((byGoals[0], byGoals[1]), (result.HomeGoals, result.AwayGoals));
            Assert.Equal(byGoals.Sum(), result.Goals.Count);
            Assert.Equal(2 * C.HalfLength, result.PlayedSeconds, 9);
            Assert.All(result.Goals, g => Assert.InRange(g.ClockSeconds, 0, C.HalfLength));
            Assert.All(result.Goals, g => Assert.InRange(g.Half, 1, 2));
            Assert.StartsWith("eleven-result/2\n", result.ToText());
        }

        [Fact]
        public void The_clock_runs_only_while_the_ball_is_in_play()
        {
            Played p = Play(new Simulator(C, OneVOne.Match(C, seed: 0x86, bothCpu: true)), Enough, keepStates: true);
            for (int i = 1; i < p.States.Count; i++)
            {
                SimState before = p.States[i - 1], after = p.States[i];
                // A step ending with the ball in play in a half counts, the step a restart is taken included.
                if (after.Match.Phase != before.Match.Phase) continue;
                bool counts = after.Restart.Phase == RestartPhase.Live && (after.Match.Phase == MatchPhase.FirstHalf || after.Match.Phase == MatchPhase.SecondHalf);
                Assert.True(after.Match.ClockTicks == before.Match.ClockTicks + (counts ? 1 : 0), $"step {i}: clock {before.Match.ClockTicks} -> {after.Match.ClockTicks}, {after.Restart.Phase}, {after.Match.Phase}");
            }

            // Restarts happened, so there were dead balls the clock waited out.
            Assert.Contains(p.Events, e => e.Kind == SimEventKind.RestartAwarded);
            Assert.True(p.States.Count > 2 * C.HalfTicks + C.HalfTimeTicks, "the match took no longer than its clock");
        }

        [Fact]
        public void Nothing_is_played_at_half_time_or_after_full_time()
        {
            Played p = Play(new Simulator(C, OneVOne.Match(C, seed: 0x86, bothCpu: true)), Enough + 600, keepStates: true);
            Assert.Single(p.Events, e => e.Kind == SimEventKind.FullTime);
            long half = p.Events.Single(e => e.Kind == SimEventKind.HalfTime).Tick, second = p.Events.Single(e => e.Kind == SimEventKind.SecondHalf).Tick;
            long full = p.Events.Single(e => e.Kind == SimEventKind.FullTime).Tick;
            var play = new[] { SimEventKind.Touched, SimEventKind.Kicked, SimEventKind.ChallengeStarted, SimEventKind.KeeperHeld, SimEventKind.KeeperParried, SimEventKind.Goal, SimEventKind.RestartAwarded };
            Assert.DoesNotContain(p.Events, e => play.Contains(e.Kind) && ((e.Tick > half && e.Tick < second) || e.Tick > full));
            Assert.True(p.Events.Max(e => e.Tick) >= full, "the match didn't run on past full time");
            SimState last = p.States[p.States.Count - 1];
            Assert.Equal(MatchPhase.FullTime, last.Match.Phase);
            Assert.Equal(C.HalfTicks, last.Match.ClockTicks);
        }

        [Fact]
        public void A_match_is_the_same_twice_and_restores_from_half_time()
        {
            // Each played to full time.
            Played a = Play(new Simulator(C, OneVOne.Match(C, seed: 0x86, bothCpu: true)), Enough, stop: Over);
            Played b = Play(new Simulator(C, OneVOne.Match(C, seed: 0x86, bothCpu: true)), Enough, stop: Over);
            Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
            Assert.NotNull(a.Recorder.Result);
            Assert.Equal(a.Recorder.Result!.ToText(), b.Recorder.Result!.ToText());

            // Stopped at the half-time break, snapshotted, restored: the same match from there.
            Played first = Play(new Simulator(C, OneVOne.Match(C, seed: 0x86, bothCpu: true)), Enough, stop: s => s.PeekState().Match.Phase == MatchPhase.HalfTime);
            Simulator restored = Snapshot.Restore(Snapshot.Capture(first.Sim), C);
            Assert.Equal(MatchPhase.HalfTime, restored.PeekState().Match.Phase);
            Play(restored, Enough, stop: Over);
            Assert.Equal(a.Sim.StateHash(), restored.StateHash());

            // And from its recorded commands, with no brain running.
            var recording = CommandRecording.StartFrom(new Simulator(C, OneVOne.Match(C, seed: 0x86, bothCpu: true)));
            foreach (Command[] step in a.Commands) recording.Record(step);
            Assert.Equal(a.Sim.StateHash(), CommandRecording.Decode(recording.Encode()).Replay(C).StateHash());
        }

        [Fact]
        public void A_goal_counts_once_for_the_side_that_scored_it()
        {
            // Home's own goal at −x, its own end in the first half: away's.
            SimState s = OneVOne.Match(C, seed: 4);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.PhaseTicks = 0;
            s.Players[1].Position = new Vec2(-30, 25);
            s.Ball.Position = new Vec3(-(C.GoalLineX - 3), 0, C.BallRadius);
            s.Ball.Velocity = new Vec3(-12, 0, 0);
            s.Ball.LastPlayedBy = OneVOne.HomePlayer;
            var sim = new Simulator(C, s);
            var events = new List<SimEvent>();
            for (int i = 0; i < 300; i++) sim.Step(Array.Empty<Command>(), events);
            Assert.Single(events, e => e.Kind == SimEventKind.Goal);
            Assert.Equal(new[] { 0, 1 }, sim.PeekState().Teams.Select(t => t.Goals));
        }

        [Fact]
        public void The_result_is_plain_values_independent_of_the_simulations_state()
        {
            var allowed = new[] { typeof(int), typeof(double), typeof(IReadOnlyList<ResultGoal>) };
            foreach (PropertyInfo prop in typeof(MatchResult).GetProperties()) Assert.Contains(prop.PropertyType, allowed);
            foreach (PropertyInfo prop in typeof(ResultGoal).GetProperties()) Assert.Contains(prop.PropertyType, new[] { typeof(int), typeof(double) });

            var result = new MatchResult(1, 2, 3, 1, 180, 360, new[] { new ResultGoal(1, 1, 1, 12.5), new ResultGoal(2, 3, 2, 170) }, 9);
            Assert.Equal("eleven-result/2\nhome=1\naway=2\nscore=3-1\nhalf=180\nplayed=360.00\nrestarts=9\nfouls=0-0\npenalties=0-0\ngoal=1,1,1,12.50\ngoal=2,3,2,170.00\n", result.ToText());
        }

        [Fact]
        public void A_dozen_cpu_matches_at_the_reference_halves_all_finish_and_match_their_reference()
        {
            SimConfig reference = SpikeScenario.Config();
            List<MatchTrial> trials = MatchTrials.Run(reference);
            Assert.All(trials, t => Assert.True(t.Finished, $"{t.Seed:x} didn't finish"));
            Assert.All(trials, t => Assert.Equal(t.HomeGoals + t.AwayGoals, t.FirstHalfGoals + t.SecondHalfGoals));
            Assert.All(trials, t => Assert.InRange(t.LengthSeconds, 2 * reference.HalfLength + reference.HalfTimeBreak, 3 * 2 * reference.HalfLength));
            Assert.True(trials.Sum(t => t.FirstHalfGoals) > 0 && trials.Sum(t => t.SecondHalfGoals) > 0);

            var csv = new System.IO.StringWriter();
            MatchTrials.WriteCsv(csv, reference, trials);
            string path = System.IO.Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "match-trials-reference.csv");
            Assert.Equal(System.IO.File.ReadAllText(path), csv.ToString());
        }

        [Fact]
        public void The_hosts_match_start_takes_the_humans_footballer_and_the_opponents_tier()
        {
            PlayerCapabilities quick = PlayerCapabilities.Reference.With("maxSpeed", 8.5);
            SimState s = OneVOne.Match(C, seed: 4, home: quick, awayCpu: CpuSkill.Elite);
            new Simulator(C, s);
            Assert.Equal(8.5, s.Players[0].Capabilities.MaxSpeed);
            Assert.Single(s.Cpus);
            Assert.Equal((OneVOne.AwayPlayer, CpuSkill.Elite.DecisionInterval), (s.Cpus[0].PlayerId, s.Cpus[0].Skill.DecisionInterval));
            Assert.Equal(MatchPhase.FirstHalf, s.Match.Phase);
        }

        [Fact]
        public void A_bad_match_state_is_refused_and_practice_has_no_clock()
        {
            SimState With(Action<SimState> change)
            {
                SimState s = OneVOne.Match(C, seed: 4);
                change(s);
                return s;
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(C, With(s => s.Match.ClockTicks = C.HalfTicks + 1)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Match.FirstKickOff = 9)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Restart = default)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(C, With(s => s.Teams[0].Goals = -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(C, With(s => { s.Match.Phase = MatchPhase.HalfTime; s.Match.PhaseTicks = 99999; })));

            // Practice, and the referee's world without a match, run as before: no clock, no halves.
            var practice = new Simulator(C, SpikeScenario.InitialState(C));
            for (int i = 0; i < 600; i++) practice.Step(new[] { new Command(SpikeScenario.ControllerId, i, new Vec2(1, 0)) }, new List<SimEvent>());
            Assert.Equal(MatchPhase.None, practice.PeekState().Match.Phase);
            Played kickOff = Play(new Simulator(C, OneVOne.KickOff(C, seed: 4, bothCpu: true)), 2 * C.HalfTicks);
            Assert.DoesNotContain(kickOff.Events, e => e.Kind == SimEventKind.HalfTime);
        }
    }
}
