# The restart sequencer

Issue #85, the fourth step of the 1v1 match: out of play, goals and restarts, decided by rules over the state and run by the simulation on each step (INV-8). There is no restart mechanic in a mode. A restart is awarded, set up, and taken by an ordinary kick on the ordinary ball.

The timings are untuned technical references, and each is a bench knob.

## A match has a referee; practice doesn't

`SimState.Restart.On` turns the sequencer on. A match's start turns it on (`OneVOne.KickOff`). Practice, the test bench and the worlds of #82 to #84 leave it off. Then the ball is never out of play, as before.

## Out of play and the award

Judged after the ball's step and the goal line, only while the ball is live:

| What happens | Restart | To |
| --- | --- | --- |
| A goal | Kick-off, from the centre spot | The side that conceded |
| The whole ball over a touchline (`touchlineY`, 34 m: Law 1's 68 m width) | Throw-in, where it crossed | The side that didn't touch it last |
| The whole ball over a goal line, wide or over the bar, an attacker last | Goal kick, from the goal area's corner on that side | The defenders |
| The same, a defender last (a keeper's save included) | Corner, from that corner | The attackers |

- **Exactly one award per crossing.** From the award until the restart is taken, the ball is dead: no line is judged, so a ball lying beyond a line can't be awarded twice.
- **Who touched it last** is the side of `Ball.LastPlayedBy` (#84): the last to touch, kick, take, win a challenge or save.
- **A goal straight from a throw-in doesn't count** (Law 15). While the ball is live from a throw no one else has played, a ball through the mouth is out of play over the goal line: a goal kick or a corner.

## The phases (INV-12)

| Phase | Steps | What happens |
| --- | --- | --- |
| `Live` | | The ball is in play. |
| `Stopped` | `restartStop` (0.5 s) | The ball is dead and keeps rolling. No one can touch, kick, challenge or save it. |
| Set-up | | The ball at the restart's point, at rest and free. Its taker, the awarded side's outfielder nearest the point, 0.6 m behind it, facing the play. Every footballer stopped, out of any kick, challenge or dive. Opponents moved out to the law's distance: 9.15 m, or 2 m from a throw-in. At a kick-off, everyone in his own half and the keepers on their lines. |
| `Ready` | `restartReady` (0.4 s), then the wait | Only the taker moves; everyone else is held still. Once it may be taken, the taker's kick puts the ball in play. |

- **The restart is taken by an ordinary kick:** the taker's kick intent or quick pass, on the ordinary ball, with the kick's own wind-up and error. A goal kick and the same kick in open play fly alike.
- **A throw-in is a kick intent released by the hands:** it leaves at its strength × `throwSpeed` (12 m/s), whatever the thrower's kick power.
- **Not modelled yet:** a second touch by the taker before anyone else, and the ceremony of walking to the ball. The set-up places the footballers, as a referee would.

## Taking a restart: no match stalls

`RestartControllers.Append` takes restarts as owned commands, appended before a step and recorded like any other (INV-10), so a replay runs nothing.

- **A CPU taker** stands at the ball through the set-up and kicks as soon as he may.
- **A human taker** is left to take it himself, by his own kick or quick pass. If he hasn't once it has waited `restartAuto` (1 s), it is taken for him, and his own command for that step gives way.
- **The basic taker's kick,** in his side's frame: a short kick forward at a kick-off, a throw down the line, a long goal kick up the pitch, a cross at the penalty spot from a corner. Technical references, not tactics.

## Keepers distribute

In a match, a keeper who holds the ball for `keeperHoldSeconds` (1.2 s) gives it away (#134). Without this a caught ball stopped a match. In practice he holds it, as before (#69).

He sees the pitch through `KeeperObservation.Others`, in his own frame, and decides:

1. **Short.** He looks at each teammate (keepers aside) from 3 m to `keeperThrowRange` (30 m) away. A teammate is open when no opponent is within `keeperOpenDistance` (6 m) of him, and none is within half of that of the line from the keeper to him. He rolls it to the most open (the nearer on a tie) on the ground, at the quick pass's own weight: `BallFlight.PassSpeed`, the formula the simulation weights its passes with (INV-13).
2. **Long.** With nobody open, he kicks it in the air (strength 0.6, loft 0.35) toward his furthest-forward teammate.
3. With no teammate at all, he kicks up the pitch toward the side he is on, as before.

Erik's playtest found he "just passes to the opponent": the blind kick up the pitch reached his own side on 41 of 63 releases at 5 and 10 a side over four seeds. `KeeperDistributionTests` now requires three quarters, and the release chooses no randomness.

| Knob | Range | Default |
| --- | --- | --- |
| `keeperHoldSeconds` | 0.2–6 s | 1.2 s |
| `keeperThrowRange` | 5–60 m | 30 m |
| `keeperOpenDistance` | 1–20 m | 6 m |

These are technical references, not tuned feel.

## Measured

`eleven-sim restarts <dir>` plays 12 CPU-against-CPU matches of 180 s from the kick-off, every brain running. `docs/simulation/restart-trials-reference.csv` is the record.

| | Total |
| --- | --- |
| Goals | 41 |
| Kick-offs | 41, one per goal |
| Throw-ins | 9 |
| Goal kicks | 14 |
| Corners | 30 |
| Dead ball, award to taken | 1.02 s each time: the stop, the set-up's wait, and the kick's wind-up |
| Longest stretch with the ball live and no one on it | 6.0 s |

No match stalled. CPU takers take at once, so every dead ball lasts the same.

## Tests

`RestartTests`:
- Twelve crossings, for goals, touchlines and goal lines under each last touch and both ends, each award exactly one restart, of the right kind, to the right side, set up after the stop time.
- Each restart places the ball and the footballers as the laws do.
- A dead ball is no one's; only the taker moves once it is set up; it is taken by his kick.
- A throw leaves at the throw's speed, and straight into a goal it scores nothing.
- A restart's kick flies as the same kick in open play.
- A CPU taker takes at once; a human's restart is taken for him after the wait; a human who kicks takes it himself.
- Six minutes of CPU football run on, are the same twice, from a snapshot and from the encoded recording.
- The restart state round-trips mid-restart, and a bad one is refused.
- Without a referee nothing is ever out of play.
- Over a dozen CPU matches every kind of restart comes, none stalls, and the trials match the reference.

## Reuse

Free kicks and penalties (M3) are further restart kinds with their own award and point. Offside (M4) is a further award. The phases, the set-up, the taking and the controllers stay as they are.
