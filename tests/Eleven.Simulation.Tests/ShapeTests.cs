using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #96: five a side holds a shape. Each CPU outfielder has a place from the back (his slot) and
    /// keeps it relative to the ball: the defenders behind, the forward ahead, the side spread, and
    /// a press that wins the ball back.
    /// </summary>
    public class ShapeTests
    {
        private static readonly SimConfig C = SpikeScenario.Config().With("halfLength", 60);

        private sealed class Measured
        {
            public bool Finished;
            public double Depth, Spacing;
            public int Samples;
            public double LongestBunchedSeconds;
            public readonly Dictionary<int, int> ChallengesWon = new Dictionary<int, int>();
        }

        private static Measured Play(ulong seed)
        {
            var sim = new Simulator(C, Squads.Match(C, seed, 5, bothCpu: true));
            var m = new Measured();
            var commands = new List<Command>();
            var events = new List<SimEvent>();
            long bunchedFrom = -1;
            while (sim.Tick < 4 * C.HalfTicks && sim.PeekState().Match.Phase != MatchPhase.FullTime)
            {
                commands.Clear();
                events.Clear();
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                RestartControllers.Append(sim, commands);
                sim.Step(commands, events);
                SimState s = sim.PeekState();
                // Won back by a standing challenge or, since #98, a slide.
                foreach (SimEvent e in events.Where(e => e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.SlideWon))
                {
                    int team = s.Players.Single(p => p.Id == e.Subject).TeamId;
                    m.ChallengesWon[team] = m.ChallengesWon.TryGetValue(team, out int n) ? n + 1 : 1;
                }

                bool live = s.Restart.Phase == RestartPhase.Live && (s.Match.Phase == MatchPhase.FirstHalf || s.Match.Phase == MatchPhase.SecondHalf);
                if (!live) continue;
                bool bunched = false;
                foreach (TeamState team in s.Teams)
                {
                    PlayerState[] side = s.Players.Where(p => p.TeamId == team.Id && p.Slot > 0).ToArray();
                    double spacing = 0;
                    int pairs = 0;
                    for (int i = 0; i < side.Length; i++)
                    for (int j = i + 1; j < side.Length; j++)
                    {
                        spacing += Math.Sqrt((side[i].Position - side[j].Position).LengthSquared);
                        pairs++;
                    }

                    bunched |= spacing / pairs < 4;
                    if (s.Tick % C.TickRate != 0) continue;
                    double back = side.Where(p => p.Slot <= 2).Average(p => p.Position.X);
                    double front = side.Single(p => p.Slot == 5).Position.X;
                    m.Depth += team.AttackSign * (front - back);
                    m.Spacing += spacing / pairs;
                    m.Samples++;
                }

                if (bunched && bunchedFrom < 0) bunchedFrom = s.Tick;
                if (!bunched && bunchedFrom >= 0)
                {
                    m.LongestBunchedSeconds = Math.Max(m.LongestBunchedSeconds, (s.Tick - bunchedFrom) * C.TickSeconds);
                    bunchedFrom = -1;
                }
            }

            m.Finished = sim.PeekState().Match.Phase == MatchPhase.FullTime;
            return m;
        }

        private static readonly List<Measured> Matches = Enumerable.Range(1, 4).Select(i => Play(0x96000000UL + (ulong)i)).ToList();

        [Fact]
        public void Every_five_a_side_match_finishes_and_no_side_bunches()
        {
            Assert.All(Matches, m => Assert.True(m.Finished));
            Assert.All(Matches, m => Assert.True(m.LongestBunchedSeconds < 6, $"bunched {m.LongestBunchedSeconds:F1} s"));
            Assert.All(Matches, m => Assert.InRange(m.Spacing / m.Samples, 10, 40));
        }

        [Fact]
        public void The_forward_plays_ahead_of_the_defenders()
        {
            Assert.All(Matches, m => Assert.True(m.Depth / m.Samples > 10, $"forward {m.Depth / m.Samples:F1} m ahead of the defenders"));
        }

        [Fact]
        public void Both_sides_press_and_win_the_ball_back()
        {
            // Over the four matches: in a two-minute one a side may try only a few times.
            var bySide = new Dictionary<int, int>();
            foreach (Measured m in Matches)
            {
                foreach (KeyValuePair<int, int> kv in m.ChallengesWon) bySide[kv.Key] = (bySide.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            }

            Assert.True(bySide.Count == 2 && bySide.Values.All(n => n > 2), $"won back: {string.Join(", ", bySide.Select(kv => $"side {kv.Key} {kv.Value}"))}");
        }
    }
}
