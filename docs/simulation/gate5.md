# M5's gate

Issue #130, M5's exit (#80). Tactics, the managers, the match metrics and the quick-sim each have their own measure in CI. This page lists them as one gate, with the checks M5 adds: the CPU's predictions against the simulation, and match-result isolation. Every figure is a technical reference, not approved feel.

## The gate

| Check | Where | Reference |
| --- | --- | --- |
| Tactic contrast: the three plans play differently, and a switch costs its wait | `eleven-sim plans`, CI "Contrast the tactical plans" | `tactic-reference.csv`, [tactics.md](tactics.md) |
| The managers' decision distributions under each preset, and against the scripted seat | `eleven-sim managers`, CI | `manager-reference.csv`, [managers.md](managers.md) |
| The match metrics of the 11v11 gate's matches | `eleven-sim gate11`, CI "Play the 11v11 gate" | `gate11-reference.csv`, [match.md](match.md) |
| The quick-sim against full matches: its calibration sample, fit and comparison | `eleven-sim quicksim`, CI "Calibrate the quick-sim" | `quicksim-calibration-reference.csv`, [quicksim.md](quicksim.md) |
| **Same-sim prediction (INV-13)** | `eleven-sim predictions`, CI "Check the CPU's predictions against the simulation" | Any difference fails |
| **Match-result isolation** | `QuickSimTests` | Any shared state fails |
| An 11v11 match in the host, with plans and the dashboard; the smoke switches a plan, and the recording replays | The smoke's `match11-phone` pass | [ui/match.md](../ui/match.md#the-match-dashboard) |

Every reference is diffed in CI, so a change to the football shows as a reference change in the PR that makes it.

## Same-sim prediction

INV-13: the CPU's predictions equal the simulation.
- **What is checked:** `PredictionTrials` plays CPU matches with every brain running. At each tick the ball is free, it predicts the ball's flight as the CPU's brains do: `BallFlight.Predict`, from the state they read, with both goals. It compares every predicted position with where the simulation puts the ball, step by step, until something plays it or play stops.
- **"Free" means:**
  - live in a half;
  - at no footballer's feet and in no keeper's hands;
  - no one shaping it with aftertouch.
- **"Played" means** a kick, touch, control, header, volley, save, won challenge or slide, a restart awarded, or a goal.

`eleven-sim predictions` runs two 5v5 and two 11v11 matches of the reference halves:

| Predictions | Predicted positions checked | Differed |
| --- | --- | --- |
| 77,338 | 1,896,770 | 0 |

Every prediction is the simulation's ball, bit for bit, over up to 2 s ahead. That covers the frame, both goals, bounces and spin, through whole matches. `PredictionTests` checks the same over a minute of a 5v5 and an 11v11 match.

**The CPU's own runs:** #118 made them use the body's formula. A tired or hurt footballer reckons his top speed as his body runs it (`FatigueTests`, `InjuryTests`).

## Match-result isolation

A quick-simmed match and a played one never share state. `QuickSimTests.A_quick_simmed_match_shares_no_state_with_a_played_one` runs the quick-sim before, during and after a full match on the same seed:
- the quick-sim's result is the same each time;
- the full match ends on the state it reaches alone.

The quick-sim reads only its sides' data, the config's half and a seed, on its own random stream ([quicksim.md](quicksim.md)).

## Open

- **The quick-sim's calibration sample** was small for its parameters. #151 grew it to 528 matches over a calibration league, and the fit now gives each parameter's standard error ([quicksim.md](quicksim.md)).
- **The CPU tiers:** club and pro play alike (#151). Telling them apart is M7's tuning.
