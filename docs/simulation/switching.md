# Player switching

Issue #93, M2's second step: with two outfielders a side, the human controls one at a time. Which controller owns which footballer is state (INV-10), and a switch is an owned command, recorded and replayed like any other.

## The switch

`Command.SwitchTo` names a teammate for the controller to take over, or 0.

- **It is accepted** when the target is an outfielder of the controller's side, played by the CPU. A keeper, an opponent, his own footballer or an unknown id is refused, as `SwitchRefused`.
- **It takes effect at the end of the step,** so every command of that step applies to the ownership it was made for. The step after, the human's stick moves the footballer he took over.
- **The footballer he leaves** goes to the CPU controller of the one he took. His side's brain plays him from the next step, with fresh timers (#83) and his own CPU skill (#166).
- **Every footballer keeps his own skill,** whoever controls him (`PlayerState.Skill`, state schema 30). A switch moves a CPU's controller and timers, never a skill. Before #166 the skill was the CPU's, so a switch handed the target's skill to the footballer left, and the human's own footballer had none stored.
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

## Switching modes (#136)

From Erik's session: "I don't understand the switch system." The rules above are sound, but nothing on screen said who he would become or why control had just moved. How automatic switching should be is Erik's call, so it is a choice:

| Mode | Switches to |
| --- | --- |
| **Auto** (#93's, the default) | Every row of the table above |
| **On the ball** | The receiver of his own pass, and SWITCH's choice |
| **Manual** | SWITCH's choice only |

- **The bench's Presets tab** has the switch, at its end, after the presets, and `?switching=auto|ball|manual` sets it from the address. It acts at once and the match goes on.
- **Every mode is recorded commands,** as before (INV-10): the switcher still decides only the target in the human's command, so a replay needs no switcher, whatever the mode was.
- **Each switch has a cause** (`SwitchController.LastCause`): pass, a teammate on the ball, a turnover, or asked.
- **A faint marker** under a teammate shows who SWITCH would take now (`SwitchController.Next`), in every mode. It reads the state and changes nothing.
- **When control moves on its own,** the controlled footballer's marker swells and settles back over half a second (not under reduced motion), and the HUD says why: "Switched: your pass's receiver.", "Switched: your teammate on the ball." or "Switched: first to the lost ball."
- **A capture records the mode,** after its control candidate: `... switching=ball`.

### Measured

`eleven-sim squads <dir> 2 12 seat <mode>`: the scripted seat (#95), which chases the ball and shoots but never passes or asks, over twelve 2v2 matches. Switches by cause, which the squad CSVs now carry (`switches_pass`, `switches_mate`, `switches_turnover`, `switches_asked`):

| Mode | Switches | Pass | Teammate on the ball | Turnover | Asked |
| --- | --- | --- | --- | --- | --- |
| Auto | 530 | 0 | 120 | 410 | 0 |
| On the ball | 0 | 0 | 0 | 0 | 0 |
| Manual | 0 | 0 | 0 | 0 | 0 |

In auto, more than three quarters of the switches are turnovers, about 34 a match. Those are the moments control moved without the player's choice. The refused count reads a refusal's reason now (#117); none of these switches was refused.

### The smoke

- **On the ball:** the 2v2 match on a phone is started through the bench, and its switching switch is pressed (scrolled to) while the bench holds the match. Over the whole match, no automatic switch other than to the human's pass's receiver is made.
- **Manual:** `?mode=match2&switching=manual` plays 20 s with no automatic switch.
- **Auto:** the whole 5v5 and 11v11 matches. Control moves on its own, and each automatic switch is cued.
- **In every mode,** the trace's `switching=` gives the mode, and `next=id@x,y` the teammate SWITCH would take and his marker on screen. At least half of the match's trace lines show the marker.

## Tests

`SwitchingTests`:
- A switch hands the teammate to the human and his footballer to the CPU, and the human's stick then moves the teammate.
- A switch to a keeper, an opponent, himself or no one is refused, and nothing changes.
- A pass hands the human its receiver.
- At a turnover, control goes to the teammate who reaches the ball first.
- A manual switch holds until the next possession event.
- A match with switches replays from its recording and from its snapshots.

`SwitchModeTests`:
- auto switches at a pass, a teammate on the ball and a turnover, and says why;
- on the ball switches only to a pass's receiver and on SWITCH;
- manual switches only on SWITCH;
- the mode may change between steps;
- `Next` is whom SWITCH would take, and changes nothing;
- a seat trial counts a refused switch (#117) and each switch by its cause, and in a match the causes add up to the switches.

`CarryTests`:
- taken over with a neutral stick, he keeps his run;
- under ATTACHED he carries the ball on;
- any stick ends the carry at once;
- the carry ends after `switchCarrySeconds`;
- at 0 he stops as before;
- a snapshot mid-carry restores it.
