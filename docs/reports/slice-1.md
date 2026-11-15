# Slice 1: technical report

M0's closing report (#80): what Slice 1 built, what is measured, what stays open, and what is known to be missing. Slice 1 is the single footballer's game: running, the ball, kicks, passes, ball control, the goal, the keeper, the vertical pitch, three touch candidates, the test bench and its presets.

**Slice 1 has not been tried on a physical device, and none of its feel is approved.** Every value below is an untuned technical reference unless Erik has tuned it, and none has been.

## What it is

| Area | What exists | Where it is documented |
| --- | --- | --- |
| Determinism | A fixed-step simulation with counter-based randomness, canonical state hashes, snapshots, recordings and cross-runtime replay (INV-1 to INV-7) | `docs/simulation/determinism.md` |
| Movement | Each footballer's top speed and acceleration, the stick as a velocity | `movement.md` |
| The ball | Gravity, drag, bounce, rolling friction, spin and its curve | `ball.md` |
| Kicks | A manual kick's line, strength and loft, its wind-up and error, and aftertouch | `kick.md` |
| Passes | The assisted quick pass, its receiver, line and weight, from the footballer's own perception | `pass.md` |
| Ball control | FREE's discrete touches (the working default), ATTACHED as a live comparator, the turn, the cut, and push-and-chase as an experiment | `dribble.md`, `attached.md`, `turn.md`, `cut.md`, `push.md` |
| The goal | The goal line and a round frame of posts and crossbar | `goal.md` |
| The keeper | Phases, dive, reach and save from an observation-only brain | `keeper.md` |
| The view | The vertical pitch, attacking up, with a half-time flip | #72 |
| Touch | Candidate A, ACTION SURFACE (Erik's choice), and B and C for comparison in the bench | `docs/simulation/test-bench.md` |
| Layouts | Phone and tablet, portrait and landscape, safe areas, and the bench's three arrangements | `docs/ui/layouts.md` |
| The bench | Every knob, live switches, APPLY / RETRY, felt-wrong captures, and named presets | `test-bench.md` |

## What is measured

Each mechanic has a characterisation run by `eleven-sim` in CI, with its recorded reference under `docs/simulation/*-reference.csv`. A change to the simulation that moves one fails a test until the reference is regenerated in its own commit.

| Reference | What it measures |
| --- | --- |
| `ball-curves` | Flight, bounce and roll under every ball knob |
| `movement` | Acceleration and stopping by profile |
| `kick`, `pass`, `free-pass` | A kick's speed and line, the quick pass's choice and weight |
| `dribble`, `turn`, `push` | FREE and ATTACHED's touches, the turn and cut, push-and-chase |
| `goal-edges`, `keeper-shots` | The frame's edge cases, and the keeper against a lattice of shots |

## What stays open

These are hypotheses or working defaults, not decisions. Each is a knob or a switch in the bench.

- **Ball control:** FREE is the working default; ATTACHED stays switchable; push-and-chase is off (`pushSpeed` 0).
- **Touch:** Candidate A is chosen. B and C stay in the bench for comparison only.
- **Orientation:** portrait is the starting layout; landscape is complete. Neither is declared the preferred one.
- **Every feel value:** speeds, touches, the kick's power and error, the keeper's reactions. None has been played by Erik on a device.

## Known limits

- **A preset can't be exported from a phone.** A felt-wrong capture carries the preset's name and whole config, and SAVE CAPTURE exports it.
- **One gesture in one frame can reach both the game and the UI.**
- **PASS reports "delivered" when the simulation drops it.**
- **The emulated viewports are evidence, not a device check.**
- **Running with the ball costs no speed.** A defender caught behind a carrier can't catch him (found in #84). Whether it should cost speed is a feel question for the bench.

## After Slice 1

M1 (#82 to #87) builds on Slice 1 without changing it: two sides and two goals, the CPU outfielder, the standing challenge, restarts, the match's clock and result, and the playable 1v1 match in the host. Practice and the bench run as before.
