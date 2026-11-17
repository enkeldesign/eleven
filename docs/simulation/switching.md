# Player switching

Issue #93, M2's second step: with two outfielders a side, the human controls one at a time. Which controller owns which footballer is state (INV-10), and a switch is an owned command, recorded and replayed like any other.

## The switch

`Command.SwitchTo` names a teammate for the controller to take over, or 0.

- **It is accepted** when the target is an outfielder of the controller's side, played by the CPU. A keeper, an opponent, his own footballer or an unknown id is refused, as `SwitchRefused`.
- **It takes effect at the end of the step,** so every command of that step applies to the ownership it was made for. The step after, the human's stick moves the footballer he took over.
- **The footballer he leaves** goes to the CPU controller of the one he took. His side's brain plays him from the next step, with that controller's skill and fresh timers (#83).
- **Event:** `Switched`. Its subject is the footballer taken over, and `Other` the one left to the CPU.
- **Recording format 6** adds the switch's target to each command.

## The carry (#138)

From Erik's capture `eleven-felt-wrong-223-466fe3c-t7051`: "The receiving player stops when it gets the ball. It should keep its momentum." The capture showed why. Control switched to the pass's receiver while the human's thumb was off the stick, and a neutral stick means stop: he braked from his run (6.7 m/s at one switch) to a standstill and took the ball standing.

- **Taken over, a footballer keeps his run.** For `switchCarrySeconds` (1 s, a technical reference) after a switch, a stick within 0.2 of neutral asks for his own velocity. He runs on, and under ATTACHED the ball goes with him.
- **Any stick ends the carry at once.** It doesn't come back until the next switch. Being held for a restart ends it too.
- **It is assistance inside the simulation:** the recorded command is the raw stick (INV-10), and the carry is in state (`PlayerState.CarryTicks`, state schema 29).
- **`switchCarrySeconds` 0** gives the old behaviour: a neutral stick stops him.

Only a human's controller switches, so CPU-only matches are unchanged.

## Who switches: `SwitchController`

#80's default: automatic at the ball's changes of hands, and on the human's own request. It decides a target before each step, from the step just run, and puts it in the human's command. Its decisions are recorded, so a replay needs no switcher.

| When | Switch to |
| --- | --- |
| He plays a quick pass to a CPU teammate | The pass's receiver, on the next step |
| A CPU teammate touches, takes or wins the ball, or takes his side's restart | That teammate |
| An opponent touches, takes or wins the ball, or his keeper holds it | The teammate who reaches the ball first by its own flight (INV-13), if that isn't the human's own footballer |
| The human asks | The CPU teammate who reaches the ball first |

**A manual switch holds** until the next possession event: a touch, a kick, a take, a challenge won, a save held, or a restart set up. Until then nothing switches automatically.

## Tests

`SwitchingTests`:
- A switch hands the teammate to the human and his footballer to the CPU, and the human's stick then moves the teammate.
- A switch to a keeper, an opponent, himself or no one is refused, and nothing changes.
- A pass hands the human its receiver.
- At a turnover, control goes to the teammate who reaches the ball first.
- A manual switch holds until the next possession event.
- A match with switches replays from its recording and from its snapshots.

`CarryTests`:
- taken over with a neutral stick, he keeps his run;
- under ATTACHED he carries the ball on;
- any stick ends the carry at once;
- the carry ends after `switchCarrySeconds`;
- at 0 he stops as before;
- a snapshot mid-carry restores it.
