# Tactical plans

Issue #126, M5 (#80), and the brief's hypothesis: "saved tactical setups, switchable mid-match at a cost". Each side stores three plans; its manager switches between them in a match. Every value here is a technical reference, not approved feel.

## A plan

`TeamTactic`, three a side in `SimState.Tactics` (state schema 28). A plan is a formation (#125) and four team instructions:

| Field | Meaning | Reference | Range |
| --- | --- | --- | --- |
| `Formation` | Where each place has its home | the side's | six formations |
| `StepUp` | m the shape pushes up when the side has the ball | 6 | 0–20 |
| `Line` | m the whole shape moves up (+) or down (−) from its homes | 0 | −15–15 |
| `Width` | A scale on every line's half-width | 1 | 0.5–1.5 |
| `Press` | m in the side's own frame: with the opponents on the ball beyond it, the first and second to the ball hold their places instead of going at it | 60 (anywhere) | −50–60 |

- **Every side's three plans default to the reference plan in its formation.** So a match with no switch plays exactly as before: every reference is unchanged in value.
- **Sides below three a side store no plans:** they have no shape (#96).
- **The CPU reads its side's plan through its observation** (`OutfieldObservation.Tactic`, INV-9). His place is his home in the plan, with its line and width, shifted with the ball and up by its step-up when his side has it.

## The switch

`Command.TacticTo`, 1–3 (0 none), on a command from a controller that owns one of the side's footballers, as SUBS is (INV-10; recording format 8). A restart taker's command keeps it.

1. **Checked as it is accepted, and again at the start of the step.** It is refused, with `TacticRefused`, for no stored plan, or for the plan the side already plays or awaits. It is refused with `TacticCooldown` inside the cooldown.
2. **Taken at once at a stoppage or a break.** In play it waits for the next stoppage, unless `planSwitchAt` is 1.
3. **Taken, the side settles.** Its homes and instructions blend from the old plan to the new over `planSettleSeconds`. Its formation is the new plan's at once. `TacticSwitched` is raised.
4. **Then a cooldown,** `planSwitchCooldown` of the match clock, before it may switch again.

| Knob | Range | Default | Meaning |
| --- | --- | --- | --- |
| `planSwitchAt` | 0–1 | 0 | 0: at the next stoppage, or at once in a break. 1: at once. |
| `planSettleSeconds` | 0–30 | 8 | The blend's length. 8 s of a 180 s half is about 2 match minutes of reorganising. |
| `planSwitchCooldown` | 0–120 | 30 | s of the match clock between switches. |

A switch made while the side still settles starts from its last plan, not from the blend. With the defaults that can't happen: the cooldown is longer than the settling.

## Measured

`eleven-sim plans`, in CI against `tactic-reference.csv`: CPU against CPU at the reference config.

### The plans' contrast

Home on each plan, all in a 4-4-2, against the reference plan, six seeds:
- **low block:** line −8, step-up 2, width 0.85, press −10;
- **high press:** line +6, step-up 10, width 1.1, press anywhere.

| Home | Goals | Shots | Possession | Depth | PPDA |
| --- | --- | --- | --- | --- | --- |
| **Reference** | 1.7–0.7 | 6.3–4.7 | 50 % | −7.8 m | 1.2 |
| Low block | 1.0–1.2 | 3.2–5.3 | 48 % | −24.3 m | 3.7 |
| High press | 1.5–1.7 | 9.2–8.3 | 49 % | −2.8 m | 1.4 |

**PPDA** here is away's passes in its own 60 % of the pitch per home defensive action there: a challenge or slide won, a foul, or a touch that cuts out a pass. **Depth** is home's outfielders' mean x in its own frame.

- **The plans play differently.** The low block stands 16 m deeper than the reference and lets away pass three times as long before it acts. It takes half the shots and concedes a little more.
- **The high press stands 5 m higher** and makes the game open: both sides shoot more.
- **PPDA is low throughout** (professional sides run about 8–15). The CPU passes rarely, so few passes come between defensive actions; the contrast between plans is the measure here, not the level.
- **Possession doesn't move.**

### The switch's cost

Home asks to switch from the reference to the high press at a third of the match, under each preset, four seeds each. Away's shots and goals are counted in the 20 s after the switch is taken, against the same seeds unswitched from the same tick.

| Preset | Waited for the switch | Away's shots in the window | Away's goals |
| --- | --- | --- | --- |
| No switch | — | 0 | 0 |
| **At the next stoppage, 8 s settle** | 22–57 s (34 s mean) | 0 | 0 |
| At once, 8 s settle | 0 s | 0.25 | 0 |
| At the next stoppage, no settle | 34 s mean | 0 | 0 |
| At the next stoppage, 20 s settle | 34 s mean | 0 | 0 |

- **The measurable cost is the wait.** At the next stoppage, a switch asked for at 120 s took effect 22 to 57 s later, about a tenth of the match. A plan chosen to chase a game arrives late.
- **Conceded in the window is too rare to resolve at this sample.** About 0.3 away shots fall in any 20 s window, so four seeds can't separate the presets. Whether the settling is a felt cost is for M7's sessions with a player. M5.03's decision distributions measure how often managers switch.

## Tests

`TacticTests`:
- a side stores three plans and starts on its first;
- at a stoppage a switch is taken at once and the side settles; in play it waits for the next stoppage, or not with `planSwitchAt` 1;
- inside the cooldown a switch is refused, as is one to the plan played, awaited or not stored;
- a side without plans can't switch;
- the side settles over `planSettleSeconds` and the cooldown runs down;
- a restart taker's command keeps the switch;
- a CPU side on a low block stands deeper than on a high press in the same formation;
- a recorded switch replays, and a snapshot mid-settle restores.
