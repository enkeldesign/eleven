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
| `Players` | Each footballer who played: his line (schema 8, below) |

- **`MatchResult.SchemaVersion`** (8 since #172) is bumped whenever a field is added or changes meaning.
- **`ToText()`** writes a stable text form: `eleven-result/2`, then one `name=value` line each and a `goal=` line per goal. Schema 2 (#99) adds `fouls=` and `penalties=`, home–away.
- **`MatchRecorder`** builds it as the match is played, from each step's events and state, and gives it at full time. It reads the match and is no part of it, so replaying the same commands builds the same result.

### The footballers (schema 8, #172)

The result names who played, so a career can carry each footballer's minutes, cards and injuries into his next match (M6.08b). `Players` holds one `ResultPlayer` per footballer who was on the pitch at any time, in id order. A substitute who never came on has none.

| Field | Text | What it is |
| --- | --- | --- |
| `Player`, `Team` | `player={id},{team},…` | His simulation id and side |
| `Seconds` | `…,{seconds},…` | Match clock he was on the pitch for, two decimals in the text |
| `Cautions` | `…,{cautions},…` | Cautions shown to him |
| `SentOff` | `…,1,…` or `…,0,…` | Whether he was sent off |
| `Injury` | `…,{level}` | His worst injury: 0 none, 1 minor, 2 serious ([injuries.md](injuries.md)) |

- **Time:** `MatchRecorder` credits every footballer on the pitch with each step's advance of the match clock. A starter who plays it all is credited the whole of `PlayedSeconds`, extra time included, the shootout not. A footballer off (sent off, or seriously injured) is credited nothing more. A substitute starts from nothing when he comes on, so he and the man he replaced make a whole match between them.
- **Adds up:** a side's lines hold its cautions and sendings-off exactly, the quick-sim's too ([quicksim.md](quicksim.md)). Its injured lines are at most its injuries, as one footballer can be hurt twice.
- **In the text and the codec:** a `player=` line each, after the goals; `MatchResultCodec` writes the same. The codec reads a schema-7 result, a save's from before #172, as one with no lines (INV-7).
- **Read, never played:** like the rest of the result it comes from the match's events and state (INV-1).

### Match metrics (schema 7, #128)

The brief's "football metrics layer built early": one instrument for AI evaluation, balance, exploit detection and later quick-sim calibration. `MatchRecorder` reads them from the match's events and state, never from inside the football (INV-1), and the result carries a pair (home, away) of each: `HomeMetrics`, `AwayMetrics`.

| Field | Text | Definition |
| --- | --- | --- |
| `Possession` | `possession=` | Share of live play in which the side last played the ball (`Ball.LastPlayedBy`) |
| `Shots` | `shots=` | A kick, header or volley that next reaches the goal line at the end the side attacks, the frame, or the other side's keeper. One that crosses the line or comes off the frame and then scores, or comes off the frame and is saved, is that one shot, on target. Any touch after a kick ends it, a teammate's too: a goal off a touch is a shot from where it was touched. An own goal is none. |
| `OnTarget` | `ontarget=` | Shots that scored, or that the other keeper held or parried |
| `Xg` | `xg=` | Expected goals: the sum of each shot's chance |
| `Passes`, `PassesCompleted` | `passes=`, `passescompleted=` | Quick passes, and those another teammate played next: a touch, a control, a won challenge, a save, a first-time kick, or a first-time pass once its kick connects. Not the passer's own kick that sends it, nor his own recovery of it. Only a pass its kick has sent is completed. A pass whose kick misses counts as played, as #126 counts it, and is never completed. Over the gate's matches, 33 % are completed. |
| `Corners` | `corners=` | Corners awarded |
| `Ppda` | `ppda=` | The other side's passes in its own 60 % per this side's defensive actions there: a challenge or slide won, a foul, or a touch cutting out a pass. 0 with none. |

They count in the halves and extra time, the step that ends one included, not the shootout. They count only with the ball live: a stoppage ends any kick, and what the dead ball does before the restart is no shot. The definitions are #125's and #126's harness measures.

**The xG reference model** (`MetricsModel`) is a logistic of the shape public xG models use. It reads the shot's distance d to the goal's centre and the angle θ the goal mouth subtends from where it was struck: the ball's place before the step, since a kick connects before the ball moves on its step.

xG = 1 / (1 + e^−(a + b·d + c·θ)), with a = −1.0, b = −0.12 per metre, c = 1.0 per radian, and a penalty's kick 0.76 whatever the geometry (its rebound is an ordinary shot).

- **It is a reader's model:** its parameters are metric references, not simulation config, and change no match. Centrally, it gives about 0.35 at 6 m, 0.16 at 11 m and 0.07 at the edge of the box.
- **Checked against the gate's matches,** not fitted to them. Over the 18 matches of `gate11-reference.csv` it gives 36.3 expected goals against 68 scored. A match has 16.6 shots, 14.8 of them on target, and xG 2.01 against 3.78 goals. The CPU's keepers concede more of the chances than a real keeper would, so the model under-reads this game by nearly half (47 %). That is a measure of the game, kept as it is; quick-sim calibration (M5.05) will use it.
- **Recorded and diffed in CI:** the gate's reference carries each match's possession, shots, shots on target, xG, passes and PPDA.
- **Read while a match is played:** `MatchRecorder.Now(side)` gives the metrics so far (0 home, 1 away); at full time they are the result's. The host's dashboard reads them ([match.md](../ui/match.md#the-match-dashboard)).

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
