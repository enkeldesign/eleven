# The manual kick

How a controlled player kicks: the intent, the wind-up, the launch and aftertouch; which values tools may vary; and how kicks are measured and guarded ([#37](https://github.com/enkeldesign/eleven/issues/37), part 2 of [#26](https://github.com/enkeldesign/eleven/issues/26)).

**The values are technical references.** This page describes the model, the knobs and the instruments, not ELEVEN's chosen feel.

## Research basis

From #26 (SOL), where the sources are:

- **One generic kick, not PASS and SHOOT.** The retro lineage gets depth from a tiny vocabulary: direct movement, a generic kick, narrow convenience assistance. Whether a kick is a pass, a cross or a shot is a consequence of its direction, strength and loft and of the situation.
- **SWOS:** hold for a harder kick; immediately after it, direction input shapes it into a drive, lob or bend.
- **Kick Off 2:** aftertouch is available only for a very short period immediately after the kick, and the players' skills affect passing and shooting.
- **Super Soccer Champs:** manual shots need aim, power and curve.
- **Starting ranges:** the aftertouch window is roughly 0.15–0.30 s (#26), and the hardest kick 15–32 m/s (#24, [ball.md](ball.md)).
- **Manual intent is preserved.** A manual kick aimed into space is never redirected to a teammate. The kicker's capability may add a bounded execution error, but it keeps the intended line.

Aftertouch follows the SWOS port that #26 cites ([`ball.cpp`](https://github.com/zlatkok/swos-port/blob/master/src/game/ball/ball.cpp)), read on #37:
- The window is ten updates after a kick.
- Sideways input, relative to the kick's direction, shifts the ball's destination directly, with decaying weights `{5,4,3,2,2,2,2,1,1,1}`.
- Back input gives a high kick, forward a low drive.

It agrees with SOL's research answer on #33: shaping is a short post-kick effect.

**SOL's clarification on #37:** the aftertouch window gates input; it does not define the lifetime of physical spin. Spin already on the ball keeps curving it in flight after the window closes. A ground bounce then keeps only `bounceSpinRetention` of it (#40, [ball.md](ball.md)).

## The intent

A `Command` carries, besides its move vector:

- **`Kick`**, an optional `KickIntent`:
  - `Direction` on the pitch plane. Only its direction counts; exactly zero means the player's heading when the intent arrives.
  - `Strength`, 0–1, a fraction of the player's kick power.
  - `Loft`, 0–1, a fraction of the config's maximum loft.
  It names no action.
- **`Aftertouch`**, a vector on the pitch plane. It is separate from the move vector, so the gesture mapping (#10) decides whether the stick doubles as aftertouch.

Non-finite values anywhere in the command make it `Invalid`. Strength and loft are clamped to 0–1, and an aftertouch vector longer than 1 is scaled to length 1, as the move vector is.

## The timers

Each is its own counter in `PlayerState` (AGENTS.md, *Name the state*):

| Timer | State | Knob | Runs from → to |
| --- | --- | --- | --- |
| Wind-up | `WindUpTicks` | `kickWindUpSeconds` | an accepted intent → contact |
| Aftertouch input window | `AftertouchTicks` | `aftertouchSeconds` | the step after contact → the window's end |
| Kick cooldown | `KickCooldown` | `kickCooldownTicks` | contact → the next intent the player can start, alongside the window |

Seconds become whole steps, nearest (`SimConfig.KickWindUpTicks`, `AftertouchTicks`). At 60 Hz the reference wind-up is 6 steps and the window 12; the starting window range is 9–18 steps.

Ball spin is physical state, not a timer: it decays by `airSpinDecay` and `groundSpinDecay` whatever the window does.

## Step by step

In `Simulator.Step`, after the players move:

1. **Aftertouch.** Each player with an open window shapes the ball by their command's aftertouch input (none without a command), then the window counts down.
2. **Kicks.** For each player, in id order:
   1. An intent is accepted only if the player isn't winding up and has no cooldown. It starts the wind-up and stores the intended line, the clamped strength and the loft. Intents at other times are ignored.
   2. The wind-up counts down. A wind-up of W steps started on step N ends on step N + W, and with no wind-up the kick connects on the intent's own step.
   3. When the wind-up ends:
      - **The ball is out of reach** (further than `kickReach` horizontally, or higher than `kickMaxBallHeight`): the kick misses. `KickMissed` is emitted, and no cooldown is set.
      - **Otherwise the kick connects** and `Kicked` is emitted.
3. **The ball integrates**, so a window opened by a contact first acts on the next step.

**The launch:**

```
direction = intended line + (2u − 1) × kickError      u from the KickDirection stream
speed     = strength × kickPower × kickSpeed
elevation = loft × kickLoft
velocity  = speed × (cos elevation cos direction, cos elevation sin direction, sin elevation)
spin      = 0
```

On connecting:
- every player's aftertouch window closes, and the kicker's opens at its full length, measured against the intended line;
- the kicker's cooldown starts.

**Aftertouch.** On a window step with n steps left, of A in all, the weight is `w = n / (1 + 2 + … + A)`, so the weights decay linearly and sum to 1. With the input split into its parts *along* the intended line (forward +) and *across* it (left +):

- **Bend** (direct, SWOS-style): the horizontal velocity turns by `w × across × aftertouchBend`, its speed unchanged.
- **Spin:** sidespin about +z grows by `w × across × kickSideSpin`. Magnus then curves the ball in flight, during the window and after it.
- **Lift:** vertical speed changes by `−w × along × aftertouchLift`, so back input lifts and forward input drives.

Full input held through the whole window therefore turns the ball by exactly `aftertouchBend`, adds exactly `kickSideSpin`, or changes vertical speed by exactly `aftertouchLift`. The window's length changes when and for how long input acts, not how much it can do, so the knobs stay separable.

## Knobs and capabilities

The config's kick values are in `SimConfig.Tunables`, with the ball's ([ball.md](ball.md)):

| Name | Unit | Valid | Starting | Reference |
| --- | --- | --- | --- | --- |
| `kickSpeed` (the hardest kick) | m/s | 0–60 | 15–32 (#24) | 18 |
| `kickLoft` (the maximum loft) | rad | 0–1.5 | 0–1.05 (#24) | 1.05 (60.2°) |
| `kickSideSpin` (full sideways aftertouch's spin) | rad/s | 0–100 | 0–40 (#24) | 12 |
| `aftertouchSeconds` | s | 0–1 | 0.15–0.30 (#26) | 0.2 |
| `kickWindUpSeconds` | s | 0–0.5 | — | 0.1 |
| `aftertouchBend` | rad | 0–0.5 | — | 0.1 (5.7°) |
| `aftertouchLift` | m/s | 0–10 | — | 2 |

The kicker's own values are in `PlayerCapabilities.Parameters`, with movement ([movement.md](movement.md)):

| Name | Unit | Valid | Starting | Reference |
| --- | --- | --- | --- | --- |
| `kickPower` | fraction of `kickSpeed` | 0.1–1 | — | 1 |
| `kickError` | rad | 0–0.5 | — | 0.05 (2.9°) |

Values without a starting range have no research range: they are technical references only.

**Why this shape** (decided on #37):

- **Kick power is relative to the world's hardest kick**, not absolute m/s.
  - `kickSpeed` keeps its #24 meaning and research range.
  - The retro lineage scales a base action by relative player skill.
  - Erik tunes the world's ceiling for feel, and player cards stay relative, so retuning one doesn't mean retuning every player.
  - Movement is absolute (#36) because it had no researched world ceiling.
- **`kickLoft` is the maximum loft.** Its reference moved from 0.45 to 1.05 rad, the top of the research span, so an intent's loft reaches lofted passes and chips.
- **The spike's random sidespin is gone.** It always curved the ball left, a bias unrelated to intent or capability. Curve now comes from the kicker's aftertouch, and `kickSideSpin` keeps its research range as the spin that full sideways aftertouch gives.
- **Bend and spin are separate knobs**, so the test bench can compare SWOS-style direct shaping (`kickSideSpin` 0), physical curve that develops through the flight (`aftertouchBend` 0), and mixes, without code changes.
- **`kickDirectionError` left the config.** It is now the kicker's `kickError`.

**Simplifications against SWOS:**
- **The latch:** SWOS latches left or right on the first sideways input; ELEVEN has no latch.
- **The high kick:** SWOS's high kick also adds speed; here back input only lifts.
- **The weights:** SWOS's weights are `{5,4,3,2,2,2,2,1,1,1}`; here they decay linearly.

Each is a candidate for the test bench, not a decision.

## Curves

`eleven-sim kicks <dir> [name=value …]` characterises the kick under the default config and the reference profile. A name can be a config tunable or a capability, for example `kicks out kickPower=0.8 aftertouchBend=0`.

Each run uses the real simulator. One player at rest, facing +x, gives the intent on tick 0, with a ball at rest 0.5 m ahead. Aftertouch is held from the next step to the end.

- **Launches** (`kind=launch`): strength 0.25/0.5/0.75/1 × loft 0/0.25/0.5/0.75/1, with no aftertouch.
- **Aftertouch** (`kind=aftertouch`): none, left, back and forward at full stick. They shape a `ground-pass` (strength 0.6, loft 0) and a `lofted` kick (strength 1, loft 0.4), across windows of 0.15/0.20/0.25/0.30 s, the starting range, plus the config's own window if it isn't one of them. Each window is its own config, so these rows carry its hash.

`kick-curves.csv` has one row per kick:

| Column | Meaning |
| --- | --- |
| `config_hash`, `kind`, `case` | The config that made the row; `launch` or `aftertouch`; the kick shaped. |
| `strength`, `loft`, `input`, `window_s` | The intent, the aftertouch input and the window. |
| `launch_speed_mps`, `launch_elevation_deg` | The launch the model gives the intent. `KickTests` checks the simulated ball leaves at exactly this. |
| `error_deg` | Measured: the launch's deviation from the intended line, the kicker's execution error. Every row shares a seed and contact tick, so it is the same draw throughout. |
| `turn_deg`, `spin_radps`, `lift_mps` | At the window's end: how far the horizontal velocity turned since the launch (+ left), the sidespin, and the vertical speed relative to the same kick with no input. |
| `carry_m`, `peak_m`, `flight_s` | To the first bounce: distance, highest clearance and time. |
| `total_m`, `lateral_m`, `rest_s` | At rest: distance from the start, distance off the launch line (+ left), and time. A ball still moving after 120 s is measured where it is then, with `rest_s` empty, as in the ball curves. |

`kick-curves.txt` records:
- the build, the config hash and format, and the tick rate;
- every tunable (ball and kick values, overrides marked) and the profile's capabilities, written exactly, so the summary rebuilds both;
- the grids, and the key numbers.

CI publishes both as the `kick-curves` artifact.

### The technical reference

[`kick-reference.csv`](kick-reference.csv) holds the default output. `KickCurvesTests` fails if the code produces different curves.

- **It is golden data.** It changes only in a separate commit that states why (AGENTS.md, *Tests and baselines*).
- **It is a technical reference of the current model, not approved feel.**
- **The tests check it against closed forms:**
  - Launches without aftertouch fly as projectiles, to within a tick.
  - Full aftertouch turns a ground pass by exactly `aftertouchBend`, and changes an airborne kick's vertical speed by exactly `aftertouchLift`, at every window.
  - Harder kicks carry further, more loft peaks higher, and less kick power softens every launch.

### Findings for the test bench

The reference profile's full-strength kicks, under the default config:

| Loft | Elevation | Carry | Peak | Total |
| --- | --- | --- | --- | --- |
| 0 | 0° | — | — | 206.3 m |
| 0.25 | 15.0° | 16.5 m | 1.1 m | 49.0 m |
| 0.5 | 30.1° | 28.6 m | 4.1 m | 60.5 m |
| 0.75 | 45.1° | 33.0 m | 8.2 m | 64.9 m |
| 1 | 60.2° | 28.4 m | 12.3 m | 52.0 m |

Aftertouch at full stick, across the starting window:

| Kick | Input | 0.15 s | 0.20 s | 0.25 s | 0.30 s |
| --- | --- | --- | --- | --- | --- |
| Ground pass, rolls 74.2 m | left: turn, rests left by | 5.73°, 7.34 m | 5.73°, 7.33 m | 5.73°, 7.31 m | 5.73°, 7.29 m |
| | back: peak, total | 0.10 m, 25.1 m | 0.07 m, 34.9 m | 0.03 m, 34.3 m | 0.01 m, 11.1 m |
| Lofted, carries 24.4 m, peaks 2.7 m | left: turn, rests left by | 6.15°, 9.75 m | 6.28°, 9.68 m | 6.41°, 9.62 m | 6.53°, 9.55 m |
| | back: carry, peak | 31.0 m, 4.25 m | 31.0 m, 4.21 m | 31.0 m, 4.18 m | 30.7 m, 4.15 m |
| | forward: carry, peak | 18.1 m, 1.53 m | 18.4 m, 1.57 m | 18.4 m, 1.60 m | 18.4 m, 1.63 m |

- **The window sets timing, not amount, where nothing competes with the input.**
  - Sideways input turns a ground pass by exactly 5.73° at every window, and its rest point moves only 7.29–7.34 m.
  - On a lofted kick, back and forward input change vertical speed by exactly ±2 m/s at every window. A longer window delivers the lift later, so the peak moves by about 0.1 m.
- **Spin keeps curving after the window**, as SOL's clarification says.
  - A lofted kick bent left leaves the window turned 6.2–6.5°, more than the bend alone, and spinning at 10.8–11.4 rad/s.
  - It comes to rest 9.6–9.8 m left, against 7.3 m for a ground pass, whose spin can't curve it while it rolls (#33). Its first bounce keeps only a quarter of the spin (#40), so the curve after landing is small.
- **Back input can't chip a ground pass at the reference lift.**
  - The 2 m/s spread over the window competes with gravity, so the ball only hops: 0.01–0.10 m, depending on the window.
  - Each hop's bounce costs it most of its roll: 11–35 m instead of 74 m.
  - Here the window does change the outcome. If the bench wants chips from the ground, `aftertouchLift` is the knob to raise.
- **Forward input can't drive a ball into the ground.** On a ground pass it does nothing.
- **Ground passes roll far at the default friction:** 74 m at 10.8 m/s, and 206 m at full strength. This is [ball.md](ball.md)'s finding: readable ground passes need `rollingFriction` in the upper part of its range.

## Properties

`KickPropertyTests` runs 200 seeded cases for 240 steps each (4 s at 60 Hz).

**What the fuzz varies:**
- **Configs:** every kick and aftertouch tunable across its whole valid range. Some configs have no wind-up, and some no window.
- **Players:** one to three per case, near the ball, with capabilities across their whole valid ranges.
- **Commands:**
  - Players chase the ball or hold random sticks.
  - Kick intents: no direction, tiny or over-long directions, and strength and loft beyond 0–1.
  - Random aftertouch.
  - Unowned, stale, early, duplicate and non-finite commands.

**Properties checked:**
- **Manual intent is kept:**
  - an intent accepted while the player is ready starts a kick that stores its line (or the heading), strength and loft as given, only clamped;
  - the line never changes during the wind-up, and the kick shapes along it;
  - the ball leaves within ± the kicker's `kickError` of it.
- **Timing:**
  - the wind-up counts down one step at a time;
  - the kick connects or misses exactly when it runs out;
  - a contact opens the kicker's window and closes everyone else's;
  - otherwise windows count down.
- **Aftertouch expiry:** changing every aftertouch input on every step where no window is open leaves the run identical, bit for bit.
- Kick state stays within what the config can produce, and finite.

Each property also has a test that feeds its check a deliberately broken run and shows the check catches it:
- the intent check catches a line retargeted at the intent;
- the launch check catches every launch turned towards the nearest teammate;
- the expiry check catches input changed on a window's last open step, the boundary itself.

`KickTests` covers the rest:
- **Commands:** rejection and clamping.
- **The wind-up:** its timing, the state it holds, no wind-up at all, and misses (a ball out of reach, or one rolling out of reach during the wind-up).
- **Intents and the line:** ignored intents, and a line fixed while the player turns.
- **The launch:** its formula, its zero spin, an explicit direction, and the bounded execution error.
- **Capabilities:** observable, deterministic differences from kick power and from error.
- **No retargeting:** a teammate anywhere never changes a launch.
- **Aftertouch:**
  - the bend preserves speed;
  - the weights decay linearly and sum to 1;
  - the spin and the lift;
  - expiry;
  - only the kicker shapes the ball.
- **Snapshots and validation:**
  - snapshot and restore at every tick through a wind-up and a window;
  - refusing kick state the config couldn't produce.

## The spike host

Until the touch candidates (#10) replace it, the host's mapping is provisional:
- A kick tap is a full-strength kick along the heading at the spike's old elevation, 0.45 rad.
- The stick doubles as aftertouch, as the joystick does in SWOS and Kick Off 2.

The simulation ignores aftertouch outside a window, so running after the ball shapes the kick only in the 0.2 s after contact.
