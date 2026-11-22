# The 11v11 gate

Issue #112, M4's exit (#80): an eleven-a-side match in the host with every law of M4, a fixed-seed evaluation in CI, repeated replays, a stable result contract and the tick budget. #80's three open defaults (3+3 minutes, 60 Hz and no minimap) are re-evaluated here by evidence. Every figure is a technical reference, not approved feel.

## The gate

`eleven-sim gate11 <dir>` (`Gate11`), in CI after the tick budget:

1. **A league:** the six fictional sides of #103's league play a single round robin, 15 matches under league rules, every footballer a CPU footballer at his side's tier and each side's manager substituting (#110).
2. **A cup:** the league's top four play a knockout (#111): first against fourth, second against third, then the final.
3. **Replays:** each cup tie is replayed twice from its recorded commands, no brain run. A replay must give the same events, in order, and end on the same state.

It fails if a match is stuck (unfinished; an outfielder still, or the ball untouched, for more than 30 s of play), if a replay differs, or if the cup has no winner. Its matches are diffed against `gate11-reference.csv`.

### The reference

| | |
| --- | --- |
| Matches | 18: 15 in the league, 3 in the cup. All finished, none stuck. |
| Goals a match | 3.8 |
| Laws a match | 5.4 fouls, 0.06 offsides, 1.0 cautions, 0.11 sendings-off, no injuries, 4.5 substitutions |
| Longest still, untouched | 4.5 s, 5.2 s |
| League | One draw in 15, Millbrook–Northfield 0–0. Kestrel, the elite side, won all five. |
| Cup | No tie to extra time; Kestrel winning the final 2–1 against Riverside. |
| Replays | 6 of 6 equal. |

This is the reference after #122, which sets the CPU taker's penalty: a match with a penalty plays on differently once it scores where it was saved before. The league and cup results are as after #118, which reckoned tired CPU runs at their tired speed.

**What it shows:**
- **Laws reached at eleven a side:** fouls, offside, cautions, a sending-off and substitutions.
  - In this reference no foul injures and no tie goes to extra time or penalties. `InjuryTests` and `KnockoutTests` cover those, and earlier references reached them.
  - Offside is rare in CPU play (once in 18 matches): the CPU's forwards seldom run beyond the second-last defender before a pass.
- **The result contract** (schema 7, with the match metrics) is stable across replays.

## The host

- **The play switch** has a fifth segment, 11v11, and `?mode=match11` starts one from the address: twenty-two footballers, the controlled one marked, SWITCH and SUBS (#110).
- **The smoke** plays a whole 11v11 match through the bench on seed 11 (21 after #138's carry, 10 before it; each change to the CPU's play changes every untouched-human match), the human untouched, at 90 s halves. In .NET that match gives a caution and the CPU managers' substitutions; the smoke checks both, counts offsides and the off-screen cues, and replays the recording at full time.
- **No seed** among the first 120 gives offside, a card and a substitution together in an untouched 90 s match, so offside is counted, not required, in the browser. It is required in the .NET gate and in `OffsideTests`.

## 3+3 minutes

Eight 11v11 matches at each half length, with the harness's human seat (it chases the ball and shoots, switching as the host does):

| Halves | Goals a match | Possessions a live minute | The human's touches a live minute | The human's footballer his side's nearest to the ball |
| --- | --- | --- | --- | --- |
| 1.5 + 1.5 min | 0.6 | 17.0 | 85 | 83 % |
| **3 + 3 min** | 2.6 | 18.1 | 76 | 81 % |
| 4.5 + 4.5 min | 3.0 | 17.8 | 71 | 81 % |

- **The rate of play doesn't change with length:** possessions and the human's share are the same at each, so a half's length only sets how much football there is.
- **At 1.5 minutes most matches end goalless**; at 3 a match has two or three goals, and 4.5 adds little.
- **Kept: 3 + 3 minutes.** An untuned reference, re-evaluated with a real player in M7.

The seat is a harness driver, not a human: it scored every goal here, so its numbers describe the match's pace, not a player's chances.

## 60 against 120 Hz

`eleven-sim budget` (#106), three 11v11 matches at each rate, on this container:

| Rate | Step, mean | Step, 99th percentile | CPU a second of play |
| --- | --- | --- | --- |
| **60 Hz** | 0.45 ms | 3.7 ms | 27 ms |
| 120 Hz | 0.44 ms | 6.7 ms | 52 ms |

- 120 Hz doubles the simulation's cost a second for no measured gain: the step is already well inside a quarter of a 60 Hz frame, and no feel evidence asks for finer steps.
- **Kept: 60 Hz.** The browser's own budget is measured by the smoke's `match11-phone` pass.

## No minimap

The off-screen cues (#102) mark footballers just outside the view at its edge. The smoke now measures them at eleven a side as at five, in the whole-match pass, and checks every cue sits inside the play area.

- **Kept: no minimap.** Opponents have no off-screen omniscience (#102's fairness trial), and the cues show who is just out of view.
- **Revisit** if the measured cue rate at eleven a side crowds the screen; the figure is in the smoke's notes for `match11-whole`.

## Tests

`Gate11Tests`:
- a cup tie finishes with a winner, unstuck, and replays to the same events and state;
- the gate fails on a stuck match, a differing replay or a cup without a winner;
- the matches' CSV has one line a match under its header.
