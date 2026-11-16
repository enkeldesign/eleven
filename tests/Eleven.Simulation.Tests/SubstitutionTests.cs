using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #110: substitutions (Law 3). A side brings on up to five substitutes from its bench, at a
    /// stoppage, in at most three stoppages in play, half-time apart. A substitute takes the slot
    /// and the place of the man he replaces, fresh; the man replaced takes no further part.
    /// </summary>
    public class SubstitutionTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private const int Side = 10;
        private const int Seat = SquadTrials.SeatController;
        private static readonly int Mate = Squads.HomeOutfielder(3);
        private static readonly int Sub = Squads.HomeBench(Side, 0), Sub2 = Squads.HomeBench(Side, 1);

        // Eleven a side at the kick-off, the ball dead: a stoppage. Home's first outfielder is the human's.
        private static SimState World() => Squads.Match(C, 5, Side);

        private static int Index(SimState s, int id) => Array.FindIndex(s.Players, p => p.Id == id);

        private static TeamState Home(Simulator sim) => sim.PeekState().Teams.Single(t => t.Id == Squads.HomeTeam);

        // One step with one command from `controller`: `off` for `on`.
        private static List<SimEvent> Substitute(Simulator sim, int off, int on, int controller = Seat)
        {
            var events = new List<SimEvent>();
            sim.Step(new[] { new Command(controller, sim.Tick, Vec2.Zero, subOff: off, subOn: on) }, events);
            return events;
        }

        private static void Refused(List<SimEvent> events, CommandRejection why, int controller = Seat)
        {
            Assert.Contains(events, e => e.Kind == SimEventKind.CommandRejected && e.Subject == controller && e.Rejection == why);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Substituted);
        }

        [Fact]
        public void At_a_stoppage_a_substitute_takes_the_slot_and_the_place_of_the_man_he_replaces()
        {
            SimState s = World();
            PlayerState before = s.Players[Index(s, Mate)];
            var sim = new Simulator(C, s);
            List<SimEvent> e = Substitute(sim, Mate, Sub);

            SimEvent made = Assert.Single(e, x => x.Kind == SimEventKind.Substituted);
            Assert.Equal((Sub, Mate), (made.Subject, made.Other));
            SimState after = sim.PeekState();
            Assert.DoesNotContain(after.Players, p => p.Id == Mate);
            PlayerState on = after.Players[Index(after, Sub)];
            Assert.Equal((before.Slot, before.TeamId, before.Position), (on.Slot, on.TeamId, on.Position));
            Assert.Equal(s.Players.Length, after.Players.Length);
            Assert.True(after.Bench.Single(b => b.Id == Sub).Used);
            Assert.Equal((1, 1), (Home(sim).Substitutions, Home(sim).SubWindows));
        }

        [Fact]
        public void He_comes_on_fresh_with_his_own_capabilities_and_a_clean_record()
        {
            SimState s = World();
            int m = Index(s, Mate);
            s.Players[m].Fatigue = 0.8;
            s.Players[m].Cautions = 1;
            s.Players[m].Injury = InjuryLevel.Minor;
            int b = Array.FindIndex(s.Bench, x => x.Id == Sub);
            s.Bench[b].Capabilities.MaxSpeed = 9.5;
            var sim = new Simulator(C, s);
            Assert.Contains(Substitute(sim, Mate, Sub), x => x.Kind == SimEventKind.Substituted);
            PlayerState on = sim.PeekState().Players[Index(sim.PeekState(), Sub)];
            Assert.Equal((0.0, 0, InjuryLevel.None, 9.5), (on.Fatigue, (int)on.Cautions, on.Injury, on.Capabilities.MaxSpeed));
        }

        [Fact]
        public void The_cpu_that_played_the_man_replaced_plays_the_substitute()
        {
            var sim = new Simulator(C, World());
            int controller = sim.PeekState().Players[Index(sim.PeekState(), Mate)].OwnerControllerId;
            Assert.Contains(Substitute(sim, Mate, Sub), x => x.Kind == SimEventKind.Substituted);
            SimState after = sim.PeekState();
            Assert.Equal(controller, after.Players[Index(after, Sub)].OwnerControllerId);
            Assert.Contains(after.Cpus, c => c.PlayerId == Sub);
            Assert.DoesNotContain(after.Cpus, c => c.PlayerId == Mate);
            Assert.Equal(after.Cpus.Select(c => c.PlayerId).OrderBy(x => x), after.Cpus.Select(c => c.PlayerId));
        }

        [Fact]
        public void The_human_replacing_his_own_footballer_controls_the_substitute()
        {
            var sim = new Simulator(C, World());
            int own = Squads.HomeOutfielder(0);
            Assert.Contains(Substitute(sim, own, Sub), x => x.Kind == SimEventKind.Substituted);
            SimState after = sim.PeekState();
            Assert.Equal(Seat, after.Players[Index(after, Sub)].OwnerControllerId);
            Assert.DoesNotContain(after.Cpus, c => c.PlayerId == Sub);
            // He was the kick-off's taker: the substitute takes it.
            Assert.Equal(Sub, after.Restart.TakerId);
        }

        [Fact]
        public void A_cpu_sides_own_controller_substitutes_for_it()
        {
            var sim = new Simulator(C, World());
            int keeper = Squads.AwayKeeper(Side), off = Squads.AwayOutfielder(Side, 4), on = Squads.AwayBench(Side, 0);
            List<SimEvent> e = Substitute(sim, off, on, controller: keeper);
            Assert.Contains(e, x => x.Kind == SimEventKind.Substituted && x.Subject == on && x.Other == off);
            Assert.Equal(0, Home(sim).Substitutions);
        }

        [Fact]
        public void A_keeper_replaced_hands_his_goal_to_the_substitute()
        {
            var sim = new Simulator(C, World());
            Substitute(sim, Squads.HomeKeeper(Side), Sub);
            Assert.Contains(sim.PeekState().Keepers, k => k.PlayerId == Sub);
            Assert.DoesNotContain(sim.PeekState().Keepers, k => k.PlayerId == Squads.HomeKeeper(Side));
        }

        [Fact]
        public void In_play_a_substitution_is_refused()
        {
            SimState s = World();
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.TakerId = 0;
            var sim = new Simulator(C, s);
            Refused(Substitute(sim, Mate, Sub), CommandRejection.SubNotStopped);
            Assert.Contains(sim.PeekState().Players, p => p.Id == Mate);
        }

        [Fact]
        public void After_full_time_a_substitution_is_refused()
        {
            SimState s = World();
            s.Match.Phase = MatchPhase.FullTime;
            Refused(Substitute(new Simulator(C, s), Mate, Sub), CommandRejection.SubNotStopped);
        }

        [Fact]
        public void With_five_made_a_sixth_is_refused()
        {
            SimState s = World();
            s.Teams[Array.FindIndex(s.Teams, t => t.Id == Squads.HomeTeam)].Substitutions = 5;
            Refused(Substitute(new Simulator(C, s), Mate, Sub), CommandRejection.SubNoneLeft);
        }

        [Fact]
        public void Two_in_one_stoppage_use_one_window_and_the_window_closes_when_play_restarts()
        {
            var sim = new Simulator(C, World());
            Substitute(sim, Mate, Sub);
            Substitute(sim, Squads.HomeOutfielder(4), Sub2);
            Assert.Equal((2, 1), (Home(sim).Substitutions, Home(sim).SubWindows));
            Assert.True(Home(sim).SubWindowOpen);

            // The kick-off is taken: play is live, and the window is closed.
            var commands = new List<Command>();
            for (int t = 0; t < 10 * C.TickRate && sim.PeekState().Restart.Phase != RestartPhase.Live; t++)
            {
                commands.Clear();
                RestartControllers.Append(sim, commands);
                sim.Step(commands, new List<SimEvent>());
            }

            Assert.Equal(RestartPhase.Live, sim.PeekState().Restart.Phase);
            Assert.False(Home(sim).SubWindowOpen);
        }

        [Fact]
        public void With_three_windows_used_a_new_stoppage_gives_none_but_the_open_one_still_does()
        {
            SimState s = World();
            int home = Array.FindIndex(s.Teams, t => t.Id == Squads.HomeTeam);
            s.Teams[home].SubWindows = 3;
            Refused(Substitute(new Simulator(C, s.Clone()), Mate, Sub), CommandRejection.SubNoWindow);

            s.Teams[home].SubWindowOpen = true;
            var sim = new Simulator(C, s);
            Assert.Contains(Substitute(sim, Mate, Sub), x => x.Kind == SimEventKind.Substituted);
            Assert.Equal(3, Home(sim).SubWindows);
        }

        [Fact]
        public void At_half_time_a_substitution_uses_no_window()
        {
            SimState s = World();
            s.Match.Phase = MatchPhase.HalfTime;
            s.Match.PhaseTicks = C.HalfTimeTicks;
            int home = Array.FindIndex(s.Teams, t => t.Id == Squads.HomeTeam);
            s.Teams[home].SubWindows = 3;
            var sim = new Simulator(C, s);
            Assert.Contains(Substitute(sim, Mate, Sub), x => x.Kind == SimEventKind.Substituted);
            Assert.Equal((1, 3), (Home(sim).Substitutions, Home(sim).SubWindows));
            Assert.False(Home(sim).SubWindowOpen);
        }

        [Fact]
        public void Only_a_footballer_of_the_side_goes_off_and_only_an_unused_substitute_of_its_bench_comes_on()
        {
            // An opponent off; an opponent's substitute on; nobody; half a pair; someone unknown.
            Refused(Substitute(new Simulator(C, World()), Squads.AwayOutfielder(Side, 2), Sub), CommandRejection.SubIneligible);
            Refused(Substitute(new Simulator(C, World()), Mate, Squads.AwayBench(Side, 0)), CommandRejection.SubIneligible);
            Refused(Substitute(new Simulator(C, World()), Mate, 0), CommandRejection.SubIneligible);
            Refused(Substitute(new Simulator(C, World()), 0, Sub), CommandRejection.SubIneligible);
            Refused(Substitute(new Simulator(C, World()), Mate, 999), CommandRejection.SubIneligible);

            // A substitute comes on once, and the man replaced never comes back.
            var sim = new Simulator(C, World());
            Substitute(sim, Mate, Sub);
            Refused(Substitute(sim, Squads.HomeOutfielder(4), Sub), CommandRejection.SubIneligible);
            Refused(Substitute(sim, Squads.HomeOutfielder(4), Mate), CommandRejection.SubIneligible);
        }

        [Fact]
        public void A_footballer_sent_off_is_not_replaced()
        {
            SimState s = World();
            s.Players[Index(s, Mate)].SentOff = true;
            Refused(Substitute(new Simulator(C, s), Mate, Sub), CommandRejection.SubIneligible);
        }

        [Fact]
        public void A_footballer_taken_off_injured_is_replaced_and_his_side_is_whole_again()
        {
            // A serious injury takes him off (#109): unslotted, beyond the touchline, no CPU his.
            SimConfig c = C.With("shieldSpeed", 0).With("cautionFrom", 2).With("sendOffFrom", 2).With("injuryRisk", 1).With("injurySerious", 1);
            SimState s = null!;
            int hurt = Squads.AwayOutfielder(Side, 0), fouler = Squads.HomeOutfielder(2);
            for (ulong seed = 1; seed <= 40 && s == null; seed++)
            {
                SimState w = Squads.Match(c, seed, Side);
                w.Restart.Phase = RestartPhase.Live;
                w.Restart.Kind = RestartKind.None;
                w.Restart.TakerId = 0;
                w.Restart.LastTouchedTeam = Squads.AwayTeam;
                var at = new Vec2(10, 0);
                int h = Index(w, hurt), f = Index(w, fouler);
                w.Players[h].Position = at;
                w.Ball.Position = new Vec3(at.X - 0.7, at.Y, c.BallRadius);
                w.Ball.LastPlayedBy = hurt;
                w.Players[f].Position = at + new Vec2(0.3, 0);
                w.Players[f].Velocity = new Vec2(-5, 0);
                w.Players[f].Challenge = ChallengePhase.Commit;
                w.Players[f].ChallengeTicks = 1;
                w.Players[f].ChallengeLine = new Vec2(-1, 0);
                var one = new Simulator(c, w);
                var events = new List<SimEvent>();
                one.Step(Array.Empty<Command>(), events);
                if (events.Any(x => x.Kind == SimEventKind.Injured)) s = one.PeekState().Clone();
            }

            Assert.NotNull(s);
            Assert.True(s.Players[Index(s, hurt)].Off);
            Assert.NotEqual(RestartPhase.Live, s.Restart.Phase);
            int slots = s.Players.Count(p => p.TeamId == Squads.AwayTeam && p.Slot > 0);

            var sim = new Simulator(c, s);
            int on = Squads.AwayBench(Side, 0);
            List<SimEvent> e = Substitute(sim, hurt, on, controller: Squads.AwayKeeper(Side));
            Assert.Contains(e, x => x.Kind == SimEventKind.Substituted && x.Subject == on && x.Other == hurt);
            SimState after = sim.PeekState();
            PlayerState sub = after.Players[Index(after, on)];
            Assert.False(sub.Off);
            Assert.Equal(slots + 1, sub.Slot);
            Assert.True(Math.Abs(sub.Position.Y) <= c.TouchlineY, $"at {sub.Position}");
            Assert.Contains(after.Cpus, x => x.PlayerId == on);
            Assert.NotEqual(0, sub.OwnerControllerId);
            Assert.Equal(1, after.Players.Count(p => p.OwnerControllerId == sub.OwnerControllerId));
        }

        [Fact]
        public void A_substitute_carries_no_offside_flag_from_the_man_he_replaces()
        {
            SimState s = World();
            s.Restart.OffsideFlags = 1UL << Index(s, Mate);
            var sim = new Simulator(C, s);
            Substitute(sim, Mate, Sub);
            Assert.Equal(0UL, sim.PeekState().Restart.OffsideFlags);
        }

        [Fact]
        public void The_result_counts_each_sides_substitutions()
        {
            SimState s = World();
            s.Teams[0].Substitutions = 2;
            s.Teams[1].Substitutions = 3;
            var recorder = new MatchRecorder();
            recorder.Observe(C, s, new[] { new SimEvent(40, SimEventKind.FullTime, 0) });
            Assert.NotNull(recorder.Result);
            Assert.Equal((2, 3), (recorder.Result!.HomeSubstitutions, recorder.Result.AwaySubstitutions));
            Assert.Contains("substitutions=2-3\n", recorder.Result.ToText());
        }

        [Fact]
        public void The_bench_and_a_substitution_survive_a_snapshot_and_a_recording()
        {
            var sim = new Simulator(C, World());
            var recording = new CommandRecording(Snapshot.Capture(sim));
            var step = new[] { new Command(Seat, sim.Tick, Vec2.Zero, subOff: Mate, subOn: Sub) };
            recording.Record(step);
            sim.Step(step, new List<SimEvent>());

            Simulator back = Snapshot.Restore(Snapshot.Capture(sim), C);
            Assert.Equal(sim.StateHash(), back.StateHash());
            Assert.True(back.PeekState().Bench.Single(b => b.Id == Sub).Used);

            CommandRecording read = CommandRecording.Decode(recording.Encode());
            Simulator replayed = read.Replay(C);
            Assert.Equal(sim.StateHash(), replayed.StateHash());
        }
    }
}
