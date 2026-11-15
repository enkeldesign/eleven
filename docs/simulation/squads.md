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

## Tests

`SquadTests`:
- The 2v2 world has three footballers a side, each with his own controller.
- Only one of a side goes for the ball, and the other supports.
- The CPU passes to an open teammate who meets the pass; teammates spread rather than bunch; the trials match the reference.
- A kick-off places six footballers legally.
- A 2v2 match is the same twice, from a snapshot and from its recording.
- One a side is the 1v1 match.
