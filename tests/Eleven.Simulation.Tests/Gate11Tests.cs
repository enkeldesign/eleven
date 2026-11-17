using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #112: M4's gate at eleven a side. One cup tie here, with every controller running and its
    /// recording replayed; the whole gate, a round robin and a cup, runs in CI against its reference.
    /// </summary>
    public class Gate11Tests
    {
        [Fact]
        public void A_cup_tie_finishes_with_a_winner_unstuck_and_replays_to_the_same_events_and_state()
        {
            SimConfig c = SpikeScenario.Config().With("knockout", 1);
            IReadOnlyList<LeagueSide> sides = League.Sides();
            GateMatch m = Gate11.Play(c, sides, 5, 2, 0x112100001UL, "semi-final", replays: 1);
            Assert.True(m.Finished);
            Assert.False(m.Stuck, $"still {m.LongestStillSeconds:F1} s, untouched {m.LongestUntouchedSeconds:F1} s");
            Assert.Contains(m.Winner, new[] { 5, 2 });
            Assert.Equal((1, 1), (m.Replays, m.ReplaysEqual));
            Assert.StartsWith($"eleven-result/{MatchResult.SchemaVersion}\n", m.ResultText);
            Assert.True(Gate11.Passes(new[] { m, new GateMatch { Stage = "final", Finished = true, Winner = m.Winner } }));
        }

        [Fact]
        public void The_gate_fails_on_a_stuck_match_a_differing_replay_or_a_cup_without_a_winner()
        {
            GateMatch Fine(string stage) => new GateMatch { Stage = stage, Finished = true, Winner = 1 };
            Assert.True(Gate11.Passes(new[] { Fine("league"), Fine("semi-final"), Fine("final") }));
            Assert.False(Gate11.Passes(new[] { Fine("league"), Fine("semi-final") }));
            Assert.False(Gate11.Passes(new[] { Fine("league"), new GateMatch { Stage = "final", Finished = false } }));
            Assert.False(Gate11.Passes(new[] { Fine("league"), new GateMatch { Stage = "final", Finished = true, Winner = -1 } }));
            Assert.False(Gate11.Passes(new[] { Fine("final"), new GateMatch { Stage = "league", Finished = true, LongestStillSeconds = League.StuckSeconds + 1 } }));
            Assert.False(Gate11.Passes(new[] { new GateMatch { Stage = "final", Finished = true, Winner = 1, Replays = 2, ReplaysEqual = 1 } }));
        }

        [Fact]
        public void The_matches_csv_has_a_line_a_match_under_its_header()
        {
            IReadOnlyList<LeagueSide> sides = League.Sides();
            var w = new StringWriter();
            Gate11.WriteMatches(w, SpikeScenario.Config(), sides, new[] { new GateMatch { Stage = "league", Home = 0, Away = 1, Finished = true, Winner = 0, HomeGoals = 2 } });
            string[] lines = w.ToString().Split('\n').Where(l => l.Length > 0).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.Equal(lines[0].Split(',').Length, lines[1].Split(',').Length);
            Assert.Contains(",league,", lines[1]);
            Assert.Contains(",Harbour,Millbrook,yes,2,0,no,0-0,Harbour,", lines[1]);
        }
    }
}
