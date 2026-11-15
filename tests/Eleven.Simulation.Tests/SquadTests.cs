using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>The 2v2 world (#92): two outfielders a side, support, marking and passing.</summary>
    public class SquadTests
    {
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        private static List<SimEvent> Run(Simulator sim, int ticks, List<Command[]>? recorded = null, List<SimState>? states = null, List<ulong>? hashes = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var commands = new List<Command>();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                recorded?.Add(commands.ToArray());
                sim.Step(commands, events);
                states?.Add(sim.PeekState().Clone());
                hashes?.Add(sim.StateHash());
            }

            return events;
        }

        [Fact]
        public void The_2v2_world_has_three_footballers_a_side_each_with_his_own_controller()
        {
            SimState s = Squads.Match(C, seed: 1, perSide: 2);
            new Simulator(C, s);
            Assert.Equal(6, s.Players.Length);
            Assert.Equal(6, s.Players.Select(p => p.OwnerControllerId).Distinct().Count());
            Assert.Equal(new[] { 1, 1, 1, 2, 2, 2 }, s.Players.Select(p => p.TeamId));
            Assert.Equal(new[] { Squads.HomeKeeper(2), Squads.AwayKeeper(2) }, s.Keepers.Select(k => k.PlayerId));

            // The human has home's first outfielder; every other outfielder is a CPU footballer.
            Assert.Equal(new[] { 1, 3, 4 }.Select(i => s.Players[i].Id), s.Cpus.Select(c => c.PlayerId));
            Assert.Equal(4, Squads.Match(C, seed: 1, perSide: 2, bothCpu: true).Cpus.Length);
        }

        [Fact]
        public void Only_one_of_a_side_goes_for_the_ball_and_the_other_supports()
        {
            var recorded = new List<Command[]>();
            var states = new List<SimState>();
            Run(new Simulator(C, Squads.Match(C, seed: 3, perSide: 2, bothCpu: true)), 60 * 60, recorded, states);
            var chasing = new[] { CpuMode.SeekBall, CpuMode.Approach, CpuMode.Carry, CpuMode.Shoot };
            int both = 0, plans = 0, supports = 0;
            for (int i = 0; i < states.Count; i++)
            {
                SimState s = states[i];
                foreach (int team in new[] { Squads.HomeTeam, Squads.AwayTeam })
                {
                    CpuState[] side = s.Cpus.Where(c => s.Players.First(p => p.Id == c.PlayerId).TeamId == team).ToArray();
                    plans++;
                    if (side.All(c => chasing.Contains(c.Plan.Mode))) both++;
                    if (side.Any(c => c.Plan.Mode == CpuMode.Support)) supports++;
                }
            }

            // Both at the ball only in passing, as one plan gives way to the next.
            Assert.True(both < plans / 20, $"both of a side were at the ball {both} of {plans} times");
            Assert.True(supports > plans / 4, $"a side had a supporter only {supports} of {plans} times");
        }

        [Fact]
        public void The_cpu_passes_to_an_open_teammate_who_meets_the_pass()
        {
            List<SquadTrial> trials = SquadTrials.Run(SpikeScenario.Config(), 2);
            Assert.All(trials, t => Assert.True(t.Finished, $"{t.Seed:x} didn't finish"));
            Assert.True(trials.Sum(t => t.PassesCompleted) > 10, $"{trials.Sum(t => t.PassesCompleted)} passes completed");
            Assert.True(trials.Sum(t => t.PassesCompleted) * 3 > trials.Sum(t => t.Passes), "fewer than a third of the passes reached a teammate");

            // Spread, not bunched: teammates about 20 m apart, and never stuck together for long.
            Assert.All(trials, t => Assert.InRange(t.MeanSpacing, 8, 35));
            Assert.All(trials, t => Assert.True(t.LongestBunchedSeconds < 6, $"{t.Seed:x}: bunched {t.LongestBunchedSeconds} s"));

            var csv = new System.IO.StringWriter();
            SquadTrials.WriteCsv(csv, SpikeScenario.Config(), trials);
            string reference = System.IO.Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "squad-2v2-reference.csv");
            Assert.Equal(System.IO.File.ReadAllText(reference), csv.ToString());
        }

        [Fact]
        public void A_human_seat_plays_and_switches_and_every_match_finishes()
        {
            List<SquadTrial> trials = SquadTrials.Run(SpikeScenario.Config(), 2, seeds: 4, seat: true);
            Assert.All(trials, t => Assert.True(t.Finished, $"{t.Seed:x} didn't finish"));
            Assert.All(trials, t => Assert.Equal(0, t.SwitchesRefused));

            // Every match he switches, and his side scores: the seat plays the ball, not stands by it.
            Assert.All(trials, t => Assert.True(t.Switches >= 5, $"{t.Seed:x}: {t.Switches} switches"));
            Assert.True(trials.Sum(t => t.HomeGoals) > 0, "the seat's side never scored");
        }

        [Fact]
        public void A_kick_off_places_six_footballers_legally()
        {
            // A goal, then the kick-off's set-up.
            SimState s = Squads.Match(C, seed: 3, perSide: 2, bothCpu: true);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.PhaseTicks = 0;
            s.Players[2].Position = new Vec2(-30, 25);
            s.Ball.Position = new Vec3(-(C.GoalLineX - 3), 0, C.BallRadius);
            s.Ball.Velocity = new Vec3(-12, 0, 0);
            var sim = new Simulator(C, s);
            var states = new List<SimState>();
            for (int i = 0; i < 120; i++)
            {
                sim.Step(Array.Empty<Command>(), new List<SimEvent>());
                states.Add(sim.PeekState().Clone());
            }

            SimState placed = states.First(st => st.Restart.Phase == RestartPhase.Ready);
            foreach (PlayerState p in placed.Players)
            {
                int own = placed.Teams.Single(t => t.Id == p.TeamId).AttackSign;
                Assert.True(p.Position.X * own <= 1e-9, $"player {p.Id} at {p.Position.X} is in the other half");
                if (p.TeamId != placed.Restart.AwardedTeam) Assert.True(Math.Sqrt(p.Position.LengthSquared) >= Simulator.RestartDistance - 1e-9, $"player {p.Id} is in the centre circle");
            }
        }

        [Fact]
        public void A_2v2_match_is_the_same_twice_from_a_snapshot_and_from_its_recording()
        {
            const int Ticks = 50 * 60;
            var first = new List<ulong>();
            var recorded = new List<Command[]>();
            var sim = new Simulator(C, Squads.Match(C, seed: 0x92, perSide: 2, bothCpu: true));
            Run(sim, 1200, recorded, hashes: first);
            byte[] snapshot = Snapshot.Capture(sim);
            Run(sim, Ticks - 1200, recorded, hashes: first);

            var second = new List<ulong>();
            Run(new Simulator(C, Squads.Match(C, seed: 0x92, perSide: 2, bothCpu: true)), Ticks, hashes: second);
            Assert.Equal(first, second);

            var resumed = new List<ulong>();
            Run(Snapshot.Restore(snapshot, C), Ticks - 1200, hashes: resumed);
            Assert.Equal(first.Skip(1200), resumed);

            var recording = CommandRecording.StartFrom(new Simulator(C, Squads.Match(C, seed: 0x92, perSide: 2, bothCpu: true)));
            foreach (Command[] step in recorded) recording.Record(step);
            var replayed = new List<ulong>();
            CommandRecording.Decode(recording.Encode()).Replay(C, tickHashes: replayed);
            replayed.RemoveAt(0);
            Assert.Equal(first, replayed);
        }

        [Fact]
        public void One_a_side_is_the_1v1_match()
        {
            // The N-a-side world at one a side plays as M1's match: the same result for the same seed.
            SimState squads = Squads.Match(C, seed: 7, perSide: 1, bothCpu: true);
            SimState oneVOne = OneVOne.Match(C, seed: 7, bothCpu: true);
            Assert.Equal(new Simulator(C, oneVOne).StateHash(), new Simulator(C, squads).StateHash());
        }
    }
}
