# The ball model

How the simulation moves the ball, which values tools may vary, and how its behaviour is measured and guarded ([#24](https://github.com/enkeldesign/eleven/issues/24)). The values are spike placeholders: this page describes the knobs and the instruments, not ELEVEN's chosen feel. Erik chooses that by playing.

## Integration

Every tick (`Simulator.StepBall`), with `dt` = 1 / tick rate:

- **In the air:**
  1. Velocity gains gravity and the Magnus acceleration `m = magnusCoefficient × (spin × velocity)`.
  2. Air drag scales the velocity.
  3. Position moves by the new velocity.
  4. A ball that reaches the ground while falling bounces:
     - upward speed × `bounceRestitution` (or 0 below `settleSpeed`);
     - horizontal speed × `bounceTangentialRetention`;
     - the ball is put back at ground height.
  5. Spin decays by `airSpinDecay`.
- **On the ground:**
  1. Horizontal velocity gains the horizontal Magnus acceleration.
  2. Air drag scales the velocity.
  3. Rolling friction (`rollingFriction × g`) slows it, and below `stopSpeed` it stops.
  4. Spin decays by `groundSpinDecay`.

### Air drag

Air drag is quadratic, `dv/dt = −airDrag · |v| · v`, taken semi-implicitly:

```
v ← v / (1 + airDrag · |v| · dt)
```

It is the smallest deterministic formulation that suits a game ball:

- **Cost:** one square root through `SimMath` per step (ADR-0002).
- **Stability:** the factor is in (0, 1] for any speed and step, so drag only slows the ball. Even the largest valid value can't make it oscillate or reverse.
- **Zero means off:** at `airDrag = 0` the factor is exactly 1 and skipped, so trajectories are bit-identical to the spike's. The scenario digest is still `e9e470a94550ecb7`.

A real football is roughly 0.01–0.02 /m. Whether ELEVEN's ball has drag at all is Erik's to decide in play (SOL on #24). The default stays 0.

## Tunable values

`SimConfig.Tunables` is the supported surface for tools, presets and the later tuning panel. Each value has a valid range:

- `SimConfig.Get(name)` reads a value.
- `SimConfig.With(name, value)` builds a new config with one value changed, and `With(overrides)` changes several. Every variant is validated and hashed like any other config.
- The constructor refuses out-of-range values. Non-finite values are refused by the encoding, as before.

| Name | Unit | Range | Default |
| --- | --- | --- | --- |
| `gravity` | m/s² | 0.1–50 | 9.81 |
| `ballRadius` | m | 0.05–0.5 | 0.11 |
| `bounceRestitution` | fraction | 0–1 | 0.55 |
| `bounceTangentialRetention` | fraction | 0–1 | 0.8 |
| `settleSpeed` | m/s | 0–5 | 0.3 |
| `rollingFriction` | fraction of g | 0–2 | 0.08 |
| `stopSpeed` | m/s | 0–2 | 0.05 |
| `magnusCoefficient` | 1/rad | 0–0.05 | 0.006 |
| `airSpinDecay` | 1/s | 0–60 | 0.5 |
| `groundSpinDecay` | 1/s | 0–60 | 3.0 |
| `airDrag` | 1/m | 0–1 | 0 |
| `kickSpeed` | m/s | 0–60 | 18 |
| `kickLoft` | rad | 0–1.5 | 0.45 |
| `kickSideSpin` | rad/s | 0–100 | 12 |

The ranges keep the ball physical and the integration well behaved:
- restitution and retention never add speed;
- decay never exceeds one tick's worth;
- drag stays stable.

They are not feel targets.

## Curves

`eleven-sim curves <dir> [name=value ...]` characterises the ball under the default config with any tunables overridden. It writes `ball-curves.csv` and `ball-curves.txt`, and CI publishes both on every run (the `ball-curves` artifact).

**Inputs:**
- **The grid:** launch speed 8/12/16/20/25/30 m/s × loft 0/5/10/20/30/45° × sidespin 0/10/20 rad/s about +z. Each launch starts on the ground along +x with no players on the pitch.
- **The config's own kick:** at its largest sidespin; real kicks draw 0.5–1 × that.
- **Drops:** from 0.5, 1, 2, 5 and 10 m.

Each runs until the ball rests, or for 120 s.

**Columns in `ball-curves.csv`:**

| Column | Meaning |
| --- | --- |
| `carry_m` | Horizontal distance to the first bounce |
| `peak_m` | Highest clearance of the ball's underside |
| `flight_s` | Time to the first bounce |
| `bounces` | Number of bounces |
| `rollout_m` | Distance from the first bounce to rest |
| `total_m` | Distance from release to rest |
| `lateral_m` | Sideways distance from the launch line, + left |
| `rebound_m` | Highest clearance after the first bounce |
| `rest_s` | Time to rest |

**Traceability:**
- Every CSV row carries the config hash.
- `ball-curves.txt` records the build, the config hash and format, every tunable's value with the overrides marked, the grid, and the key numbers.

So any plotted result traces back to the exact simulation that produced it.

### The technical reference

[`ball-curves-reference.csv`](ball-curves-reference.csv) holds the default config's curves. `BallCurvesTests` fails if the code produces different ones.

- **It is golden data.** It changes only in a separate commit that states why (AGENTS.md, *Tests and baselines*).
- **It is a technical reference of the placeholder values, not approved feel.** Nothing in it is a target or "correct" ball behaviour.
- **Promotion:** when Erik promotes a feel preset from the playable test bench, that preset's curves become the guarded feel baseline. Changing them then also needs his sign-off.

The intended workflow, from #24:
1. Agents build the knobs and the measurements.
2. Erik varies them in a playable test bench.
3. A promising preset is saved.
4. Its curves are characterised.
5. Erik may promote it.

The test bench must change config only through `SimConfig.With`.

## Properties

`BallPropertyTests` runs 400 seeded random balls for 600 steps each (10 s at 60 Hz).

**What the fuzz varies:**
- **Configs:** every tunable is drawn uniformly from its range.
- **Air drag:** half the cases have none, a quarter 0.01–0.03, and a quarter anywhere up to 1.
- **Balls:** in the air (up to 30 m), rolling or at rest, anywhere on the pitch (|x| ≤ 60 m, |y| ≤ 40 m). Each velocity component is up to ±40 m/s, and each spin component up to ±100 rad/s.

**Properties checked:**
- the ball never sinks below the ground;
- a bounce creates no energy;
- without a kick, energy never grows beyond the integration allowance (below);
- spin never grows;
- a ball at rest stays at rest;
- finite inputs stay finite;
- every run is deterministic.

Each property also has a test that feeds its check a deliberately broken trajectory and shows the check catches it.

### The energy allowance

Energy per unit mass is `E = ½|v|² + g · (z − r)`. The explicit integrator isn't exactly energy-conserving, so the check allows its known numerical error per step:

```
ΔE ≤ dt² · |m|²  +  g² · dt²  [ + g · dt · (|vz| + (|m| + g) · dt)  on a step that bounces ]  + 1e-9 · (1 + E)
```

Each term has a source:

- **Magnus:** the acceleration is perpendicular to the velocity, so it does no work in continuous time. A discrete step still adds `½ dt² |m|²` to the kinetic energy, up to `dt² |m|²` when a rolling ball still has some vertical speed.
- **Gravity:** the semi-implicit step loses `½ g² dt²` per step on its own. With drag at very low speed, the terms can rearrange by up to `g² dt²`.
- **The bounce:** putting the ball back at ground height raises it by at most `|vz| · dt`.

Over the fuzz, energy rose on 1,089 of 240,000 steps. The largest rise was 58 % of its allowance, and a ball that gains 1 % speed per step fails the check.

**Kicks** are the one modelled energy source, so the fuzz has no players.
