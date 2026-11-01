# Player movement

How a controlled player moves, which capabilities set it, where their starting ranges come from, and how movement is measured and guarded ([#36](https://github.com/enkeldesign/eleven/issues/36), part 1 of [#26](https://github.com/enkeldesign/eleven/issues/26)).

**The values are technical references.** This page describes the model, the knobs and the instruments, not ELEVEN's chosen feel.

## Research basis

From #26 (SOL), where the sources are:

- **SWOS and Kick Off 2:** direct movement, eight-way in Kick Off 2. The ball isn't glued to the feet.
- **Real football gives scale only:** professional players' theoretical top speed is around 9 m/s, and measured peak acceleration is around 6 m/s².
- **Feel over realism:** ELEVEN may be more responsive than literal human acceleration if that feels better. So the starting acceleration range is deliberately two to five times the real figure.

## The player

`PlayerState` is plain state, in this order:
- id and owner controller id;
- capabilities;
- position, velocity and heading;
- kick cooldown.

`PlayerCapabilities` is what one footballer can do. It is the smallest per-player surface the current mechanics need, not the final attribute taxonomy. #36 adds movement; later mechanics add only what they need.

Capabilities live in state, not in `SimConfig`:
- every player can differ, and no universal value or UI helper owns them;
- snapshots, hashes and replays carry them with the rest of the player;
- the simulation reads them and never changes them;
- `Simulator` refuses values outside the valid ranges, including a player built without capabilities, who otherwise couldn't move.

## Movement

Every tick (`Simulator.StepPlayer`), with `dt` = 1 / tick rate:

1. The owned command's move vector (length at most 1; no command is neutral) asks for the velocity `move × maxSpeed`.
2. The velocity moves towards it by at most `acceleration × dt`.
3. The position moves by the new velocity.
4. The heading follows the direction of travel. Below 1 mm/s the player counts as stopped and keeps their heading.

The move vector is the intent. The touch adapter is one source of it; nothing in the simulation knows about input devices.

**One acceleration bounds every change of velocity:** speeding up, braking and turning alike. So a change Δv takes Δv / a seconds, and leaves the player Δv² / 2a metres from where an instant change would have put them. A turn through θ at top speed v is a change of 2v sin(θ/2), so:

| Movement | Change Δv | Time | Lag behind an instant change |
| --- | --- | --- | --- |
| Standing start, or stop | v | v / a | v² / 2a |
| 90° turn | √2 · v | 1.41 v / a | v² / a |
| Reversal | 2v | 2v / a | 2v² / a |

A reversal costs twice the time of a standing start and four times the distance.

## Capabilities and starting ranges

`PlayerCapabilities.Parameters` lists them, in the same form as the ball's `SimConfig.Tunables`. `Get(name)` reads a value, and `With(name, value)` returns a copy with one changed.

| Name | Unit | Valid | Starting | Reference |
| --- | --- | --- | --- | --- |
| `maxSpeed` | m/s | 1–15 | 7–10 | 7 |
| `acceleration` | m/s² | 1–100 | 12–30 | 25 |

- **The starting ranges** are from #26. 7–10 m/s brackets the spike's 7 m/s and elite sprint scale. 12–30 m/s² explores immediate retro responsiveness.
- **The valid ranges** are wider, so the test bench can explore past the starting ranges. At 100 m/s² the player reaches 10 m/s in six ticks.
- **The reference profile** (`PlayerCapabilities.Reference`) is the spike's 7 m/s and 25 m/s², inside both starting ranges. It is a technical reference, not approved feel.

## Curves

`eleven-sim movement <dir> [name=value …]` characterises one profile (the reference, or the reference with the named capabilities changed) and a grid of four top speeds by four accelerations, evenly spaced across the starting ranges and including their ends. Each run uses the real simulator with one player:

- from rest with full stick along +x;
- from top speed along +x, with the stick released, or with full stick turned 45°, 90° or 180°.

`movement-curves.csv` has one row per profile:

| Column | Meaning |
| --- | --- |
| `config_hash`, `kind` | The config that made the row; `profile` or `grid`. |
| `max_speed_mps`, `acceleration_mps2` | The capabilities. |
| `full_speed_s`, `full_speed_m` | From rest: time to top speed, and the distance covered by then. |
| `split_5m_s`, `split_10m_s` | From rest: time to cover 5 m and 10 m. |
| `stop_s`, `stop_m` | From top speed, stick released: time to a standstill, and the distance covered. |
| `turn45_s`, `turn90_s`, `turn180_s` | From top speed: time until running at top speed on the new line. |
| `turn45_lag_m`, `turn90_lag_m`, `turn180_lag_m` | How far the player then is from where an instant turn would have put them: what the turn costs in metres. |
| `turn45_min_speed_mps`, `turn90_min_speed_mps` | The lowest speed through the turn. A reversal passes through a standstill. |

`movement-curves.txt` records:
- the build, the config hash and format, and the tick rate;
- the profile's capabilities, written exactly (shortest round-trip form), so the summary rebuilds the profile;
- the grid, and the key numbers.

CI publishes both as the `movement-curves` artifact.

### The technical reference

[`movement-reference.csv`](movement-reference.csv) holds the default output. `MovementCurvesTests` fails if the code produces different curves.

- **It is golden data.** It changes only in a separate commit that states why (AGENTS.md, *Tests and baselines*).
- **It is a technical reference of the current model, not approved feel.** It changes when the movement model, the tick rate or the grid does.
- **The tests check it against the closed form above**, to within a tick: every time is Δv / a and every lag is Δv² / 2a. The tests also check that quicker players respond sooner and faster players cover 10 m sooner.

### Findings for the test bench

The reference profile and the grid's corners:

| Profile | Top speed in | 10 m in | Stop | 90° turn: time, lag | Reversal: time, lag |
| --- | --- | --- | --- | --- | --- |
| Reference: 7 m/s, 25 m/s² | 0.28 s | 1.56 s | 0.9 m | 0.40 s, 1.9 m | 0.57 s, 3.8 m |
| 7 m/s, 12 m/s² | 0.58 s | 1.71 s | 2.0 m | 0.83 s, 4.0 m | 1.17 s, 8.1 m |
| 7 m/s, 30 m/s² | 0.23 s | 1.54 s | 0.8 m | 0.33 s, 1.6 m | 0.47 s, 3.2 m |
| 10 m/s, 12 m/s² | 0.83 s | 1.41 s | 4.1 m | 1.18 s, 8.2 m | 1.67 s, 16.5 m |
| 10 m/s, 30 m/s² | 0.33 s | 1.16 s | 1.6 m | 0.48 s, 3.2 m | 0.67 s, 6.5 m |

- **Straight-line pace hides most of the difference.** Across the whole grid the 10 m split only ranges from 1.16 s to 1.71 s, but a reversal from top speed ranges from 0.47 s and 3.2 m to 1.67 s and 16.5 m. How a player turns is where the starting ranges differ most.
- **Turning shares the acceleration capability.** The research on #26 gives ranges for top speed and acceleration, not for braking or turning separately. If the test bench finds turns sluggish at a top speed and acceleration that feel right in a straight line, separate braking or turning capabilities are the next knob to research. That would come through a focused issue, not a change here.

## Properties

`MovementPropertyTests` runs 300 seeded cases for 300 steps each (5 s at 60 Hz).

**What the fuzz varies:**
- **Players:** one to three per case, each with capabilities drawn from the whole valid range, starting anywhere from rest to top speed.
- **Sticks:** neutral, full, partial, over-long and tiny, each held for a while as a thumb would, so players reach top speed and turn at it.
- **Commands:** besides the owned ones, unowned, stale, early, non-finite and duplicate commands.

**Properties checked:**
- no player exceeds their top speed;
- no velocity changes faster than the player's acceleration;
- capabilities never change;
- the heading follows the direction of travel;
- player state stays finite;
- **owned control** (INV-10): changing every other controller's commands, including rejected junk sent in the owner's name, leaves a player's run identical, bit for bit.

Each property also has a test that feeds its check a deliberately broken run and shows the check catches it.

`MovementTests` covers the rest:
- each player's own top speed, acceleration, braking and reversal;
- an observable, deterministic difference from a one-unit capability change;
- range refusal;
- snapshot and restore while accelerating, mid-turn, through a reversal and while slowing;
- tick-for-tick replay of recorded commands (`CommandRecording.Replay` can report every tick's hash).
