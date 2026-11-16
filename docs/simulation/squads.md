# Two outfielders a side

Issue #92, M2's first step: N outfielders and a keeper a side, and the CPU outfielder playing as a side. One goes for the ball and the other supports; in defence one presses and the other marks; and the carrier passes. The ball, the challenge, restarts and the match need no change, since each was already per side.

Every distance here is an untuned technical reference.

## The world

`Squads.Match(config, seed, perSide)` starts an N-a-side match: the referee on, the clock at nought, home kicking off.

| Ids, ascending | Who |
| --- | --- |
| `1` to `N` | Home's outfielders: the first is the human's unless `bothCpu` |
| `N + 1` | Home's keeper |
| `N + 2` to `2N + 1` | Away's outfielders |
| `2N + 2` | Away's keeper |

- Each footballer's controller is his id (INV-10).
- **At one a side it is M1's 1v1 match:** the same state, the same hash.
- **Restarts place every footballer legally,** for any N (#85).

## Playing as a side

The brain (`OutfieldBrain`) still decides on the footballer's own observation alone (INV-9), in his side's frame. What a teammate does, he reads from where he sees him.

| Rule | What it does |
| --- | --- |
| **One at the ball** | Of two teammates, the one who reaches the ball first by its own flight (INV-13) goes for it. So a pass's receiver goes to meet it while the passer lets it go. On a tie, the one further toward +y. Only the nearer of the two carries. |
| **Support in attack** | With a teammate on the ball: a passing lane 6 m ahead of him and 10 m across, toward the middle, inside the touchlines. Alongside him (within 5 m), he breaks wide on a diagonal first, aiming 4 m ahead of where he is: making for a place ahead of a carrier who runs at his own speed, he would stay at his shoulder (#94). |
| **Marking in defence** | Second to the ball when the opponents have it: 2.5 m goal-side of the other opponent; with none in sight, 10 m goal-side of the ball. |
| **Passing** | Pressed (an opponent within 6 m), with an open teammate (no opponent within 4 m of him) inside 0.9 rad of his heading: a quick pass. It goes through the footballer's own quick pass (#38), with his own perception and weight. |
| **Waiting** | While a keeper holds the ball, two teammates take the waiting spot's two sides. |

At one a side none of these applies, so the 1v1 references don't move.

## Measured

`eleven-sim squads <dir> 2` plays 12 CPU-against-CPU 2v2 matches of 2 × 180 s. `docs/simulation/squad-2v2-reference.csv` is the record.

| | |
| --- | --- |
| Finished | 12 of 12 |
| Goals | 89 |
| Passes | 59, 29 reaching a teammate |
| The ball changing sides | 580 |
| Mean spacing between teammates | 19.8 m |
| Longest stretch with a side's outfielders within 2 m | 2.6 s |

Since #94: headers and volleys, and the supporter's diagonal break. Before, 77 goals; 49 passes, 22 reaching a teammate; 563 changes of side; 19.9 m; 3.5 s.

Before the side rules, both teammates chased the ball, and stood bunched for up to 29 s at a time.

## Five a side: a shape (#96)

From three a side (`Squads.ShapeFrom`), each outfielder has a place in his side's shape: `PlayerState.Slot` (state schema 18).
- **Slots run from the back.** The kick-off's taker is the furthest forward. A footballer keeps his slot whoever controls him.
- **His observation carries it,** with his side's size. It is his own, as his reaction delay is.

| Role | Who | Where |
| --- | --- | --- |
| **Presser** | The first of the side to the ball, by its own flight (INV-13) | At the ball, as before |
| **Cover** | The second, when the opponents have it | 6 m goal-side of the ball |
| **Shape** | Everyone else | His slot's place |

**The slot's place.**
- **Rows:** a back row of half the rest, a middle row of the others, and one forward. At five a side that's 2-2-1, on lines at −24, −10 and +4 m.
- **Spread:** each row is spread 14 m either side of the middle.
- **Follow:** the rows move with 0.55 of the ball's x and 0.3 of its y.
- **Step up:** they push 6 m further up when his side has the ball.
- **Bounds:** they stay 6 m inside the lines.

Technical constants in `OutfieldBrain`, not tactics: formations and instructions are M5's. One and two a side play as before.

`eleven-sim squads <dir> 5` plays 12 CPU 5v5 matches of 2 × 180 s. `docs/simulation/squad-5v5-reference.csv` is the record, and CI diffs the run against it.

| | |
| --- | --- |
| Finished | 12 of 12 |
| Goals | 42, 3.5 a match |
| Passes | 438, 195 reaching a teammate |
| The ball changing sides | 1309 |
| Mean spacing between teammates | 23.1 m |
| Longest stretch bunched | 0 s |

`ShapeTests` checks four seeds:
- every 5v5 match finishes, with no side bunched for 6 s;
- the forward plays more than 10 m ahead of the two defenders;
- both sides win the ball back.

## Fifty matches, and a human seat (#95)

`eleven-sim squads <dir> 2 50` plays M2's gate: 50 2v2 matches CPU against CPU. `eleven-sim squads <dir> 2 50 seat` plays them with a **human seat** on home's first outfielder.
- The seat is a harness driver, not an AI: it reads the state, as a test does. It runs at the ball, and within reach shoots at the goal its side attacks.
- Its switches are `SwitchController`'s, as the host's are.
- CI runs both and publishes `squad-evaluation`.

| | CPU against CPU | With the seat |
| --- | --- | --- |
| Finished | 50 of 50 | 50 of 50 |
| Goals, home – away | 181 – 194 | 19 – 123 |
| Passes, completed | 187, 86 | 273, 62 |
| Changes of side | 2300 | 2321 |
| Switches | | 2111, none refused: 42 a match |
| Longest bunched | 4.2 s | 9.3 s; over 6 s in 18 matches |

- **The seat loses heavily.** It never defends, and it kicks at the goal from anywhere in reach. It is there to exercise switching, not to play well.
- **Finding: a naive human crowds his CPU teammate.** The teammate reads the seat as he reads a CPU teammate, but the seat always chases the ball, so both go for it. A human who chases every ball will see the same.

## Tests

`SquadTests`:
- The 2v2 world has three footballers a side, each with his own controller.
- Only one of a side goes for the ball, and the other supports.
- The CPU passes to an open teammate who meets the pass; teammates spread rather than bunch; the trials match the reference.
- A kick-off places six footballers legally.
- A 2v2 match is the same twice, from a snapshot and from its recording.
- One a side is the 1v1 match.

## Eleven a side

Issue #106, M4's first step: ten outfielders and a keeper a side.

### The lines

`OutfieldBrain.Lines(n)` gives how many of a side's outfielders play at the back, in the middle and up front, numbered by slot from the back.

| Outfielders | Back | Middle | Forward |
| --- | --- | --- | --- |
| 3 to 7 | half | the rest but one | 1 |
| 8 | 3 | 3 | 2 |
| 10 | 4 | 4 | 2: a technical-reference 4-4-2 |

- **Rows of four or more spread 22 m either side** of the middle, not 14. Two forwards stand 10 m either side of it.
- **The lines are M3's:** −24, −10 and +4 m in a side's own frame with the ball at the centre spot. They follow the ball as before (#96).
- **The kick-off** places every outfielder at his home in his own half, from six a side up. The taker stays at the ball. Smaller sides keep M2's rows, so no 5v5 or 2v2 reference moves.

`OutfieldBrain.ShapeHome(n, slot)` is a footballer's home with the ball at the centre spot. Formations as data are M5.

### Measured

`eleven-sim squads <dir> 10` plays 12 CPU matches of 2 × 180 s; `squad-10v10-reference.csv` is the record.

| | |
| --- | --- |
| Finished | 12 of 12 |
| Goals | 19 |
| Passes | 539, 288 reaching a teammate |
| The ball changing sides | 1,242 |
| Mean spacing between teammates | 25.1 m |
| Longest stretch bunched | none |

Goals are fewer than at five a side (1.6 a match against 2.9): more defenders between the ball and the goal.

### The tick budget

`eleven-sim budget <dir> [seeds]` plays CPU 11v11 matches at 60 and at 120 Hz, the same seeds and knobs (`SimConfig.AtTickRate`), and times each step. It fails if a 60 Hz step averages over a quarter of its 16.7 ms frame, 4.2 ms. A step is the whole football tick: the keepers' and the CPU's decisions, then the simulation's step. The kick's cooldown, the one length set in ticks, keeps its seconds at either rate. The timing is the runner's, so CI publishes it rather than diffing it.

Measured locally, three seeds each:

| | 60 Hz | 120 Hz |
| --- | --- | --- |
| Mean step | 0.58 ms | 0.56 ms |
| 99th percentile step | 5.3 ms | 8.4 ms |
| Simulation time per second of play | 35 ms | 68 ms |
| Goals | 5 | 9 |

- **60 Hz stays.** 120 Hz costs twice as much per second of play, and three seeds show no difference worth it.
- **The spikes are not yet explained.** The likeliest cause is the CPU footballers' decisions landing on the same steps. If the browser's frames need it, measuring that comes first, then spreading the decision ticks.

In the browser, `?mode=match11` starts an eleven-a-side match from the address. In trace, the host reports the step's mean and longest every five seconds of match clock, timed over the same work as the CLI's. The smoke's `match11-phone` pass checks the mean against the same 4.2 ms.
