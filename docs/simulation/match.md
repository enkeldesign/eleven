# The match

Issue #86, the fifth step of the 1v1 match: a kick-off, two halves, a change of ends, the score and the result. The clock, the halves and the score are rules over the state, after the restart sequencer (#85) on each step.

The half's length is the brief's starting reference, 2 × 3 minutes. Every value is a bench knob, not approved feel.

## The state

`SimState.Match` (`MatchState`, schema 16) and each side's `TeamState.Goals`:

| Field | What it is |
| --- | --- |
| `Phase` | `None` (practice: no clock), `FirstHalf`, `HalfTime`, `SecondHalf`, `FullTime` |
| `ClockTicks` | Steps of match clock played in this half |
| `PhaseTicks` | Steps left of the half-time break |
| `FirstKickOff` | The side that kicked off the first half |
| `TeamState.Goals` | Goals the side has scored |

A match needs its referee: a state with a match phase and the restart sequencer off is refused. `OneVOne.Match(config, seed)` starts one: the kick-off set up, the clock at nought.

## The clock

- **It runs only while the ball is in play.** A step that ends with the ball live in a half adds one step to it. The step a restart is taken counts, since the ball is in play by its end.
- **A dead ball, the half-time break and full time never advance it.** So there is no stoppage time to add: a 3-minute half is 3 minutes of play, however many restarts it has.
- **At the half's length** (`halfLength`, 180 s) the ball goes dead wherever it is.

## The halves

| Moment | What happens | Event |
| --- | --- | --- |
| The first half's clock runs out | The ball is dead, and no one can play it. | `HalfTime` |
| After the break (`halfTimeBreak`, 2 s) | **The sides change ends**: each side's attack sign flips. The clock starts again from nought. The second half's kick-off goes to the side that didn't kick off the first, set up as any restart. | `SecondHalf`, then the kick-off's `RestartAwarded` |
| The second half's clock runs out | **Full time**: the ball is dead for good. | `FullTime` |

- **The ends change in the state, not on screen.** The keepers go to their new goals as the kick-off is set up, since a keeper guards the end his side doesn't attack (#82).
- **The view follows the attack sign.** The host's pitch view turns 180° when the controlled side attacks −x, so the human still attacks up the screen (#72). That is the host's job (#87).
- **Exactly one of each event.** A match can't have a second half-time or full time.

## The score

A goal is credited to the side attacking the end it went into (#82), once: the goal line judges a crossing once (#68), and the ball is dead until the kick-off. An own goal counts for the other side.

## The result (INV-14)

`MatchResult` is the contract a consumer reads: a results screen, later a career. It holds plain values, and no reference to the simulation's state or events:

| Field | What it is |
| --- | --- |
| `HomeTeam`, `AwayTeam` | The sides' ids |
| `HomeGoals`, `AwayGoals` | The score |
| `HalfSeconds`, `PlayedSeconds` | A half's length, and the match clock played |
| `Goals` | Each goal: its side, the footballer who last played the ball, its half, its time on the half's clock |
| `Restarts` | Restarts taken, kick-offs included |

- **`MatchResult.SchemaVersion`** (1) is bumped whenever a field is added or changes meaning.
- **`ToText()`** writes a stable text form: `eleven-result/1`, then one `name=value` line each and a `goal=` line per goal.
- **`MatchRecorder`** builds it as the match is played, from each step's events and state, and gives it at full time. It reads the match and is no part of it, so replaying the same commands builds the same result.

## Measured

`eleven-sim matches <dir>` plays 12 CPU-against-CPU matches under the reference halves, from the kick-off to full time, every brain running. `docs/simulation/match-trials-reference.csv` is the record, and `first-result.txt` the first match's result text.

| | |
| --- | --- |
| Finished | 12 of 12 |
| Goals | 81: 43 in the first half, 38 in the second |
| Results | home 3 wins, away 6, 3 draws |
| Length | 374 to 381 s: 360 s of clock, the dead balls and the break |

The goals come at about 7 a match, high for football: the CPU outfielder shoots from 16 m whenever he can, and the reference keeper holds only some of it. Both are untuned technical references.

## Tests

`MatchTests`:
- A CPU match plays two halves, changes ends, kicks off the second half to the other side with the keepers at their new goals, and ends with its result, whose score is the goals.
- The clock runs only while the ball is in play.
- Nothing is played at half-time or after full time.
- A match is the same twice, from a half-time snapshot, and from its encoded recording.
- A goal counts once, for the side that scored it.
- The result is plain values, with a stable text form.
- A bad match state is refused; practice, and the referee's world without a match, have no clock.
- A dozen CPU matches at the reference halves all finish and match their reference.
