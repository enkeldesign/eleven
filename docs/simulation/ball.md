# The ball model

How the simulation moves the ball, which values tools may vary, where their starting ranges come from, and how the ball's behaviour is measured and guarded ([#24](https://github.com/enkeldesign/eleven/issues/24)).

**The values are spike placeholders.** This page describes the knobs and the instruments, not ELEVEN's chosen feel.

## Research basis

The retro-football lineage ELEVEN draws from treats the ball as **free, readable and player-shaped**, not as a realistic aerodynamic simulation:

- **SWOS / Sensible Soccer:** power from input duration, and drives, lobs and bends shaped just after the kick.
- **Kick Off:** the ball runs ahead of the player, with aftertouch to bend or chip it.
- **Super Soccer Champs:** manual aim, shot power and curve, and weighted crosses on touch.

The recurring pattern is **predictable base trajectories plus expressive player control over power, height and curve**. Sources and the full summary are on #24 (SOL).

The starting model follows from that:

- **No air drag.** Gravity, bounce, rolling friction and spin-induced curve only. Drag returns only if playable evidence shows a concrete problem the model can't solve cleanly, and then through a focused issue.
- **Launch is three independent quantities:** speed, loft (elevation) and spin.
  - A kick sets the ball's velocity directly and launches it without spin; aftertouch then turns it, adds sidespin or lifts it during a short window ([kick.md](kick.md), #37).
  - Magnus curve acts on whatever spin the ball carries in flight, and only in flight: a rolling ball keeps its line (#33).
  - **Two separate things limit shaping.** The aftertouch window controls when the player's input may shape the kick (#37); it does not end physical spin, which keeps curving the ball in flight after the window closes. A ground bounce removes most of that spin (#40), so the curve of repeated hops dies within a few bounces.
  - So control work implements researched pass, shot and aftertouch candidates through commands, without rewriting the ball physics.

## Integration

Every tick (`Simulator.StepBall`), with `dt` = 1 / tick rate:

- **In the air:**
  1. Velocity gains gravity and the Magnus acceleration `m = magnusCoefficient × (spin × velocity)`.
  2. Position moves by the new velocity.
  3. A ball that reaches the ground while falling bounces:
     - upward speed × `bounceRestitution`, or 0 below `settleSpeed`;
     - horizontal speed × `bounceTangentialRetention`;
     - spin × `bounceSpinRetention`, exactly once per bounce (#40);
     - the ball is put back at ground height.
  4. Spin decays by `airSpinDecay`.
- **On the ground:**
  1. Rolling friction (`rollingFriction × g`, constant deceleration) slows it, and below `stopSpeed` it stops.
  2. Spin decays by `groundSpinDecay` but doesn't curve the ball. It stays in state for later bounce, presentation and aftertouch work.

  Why no curve on the ground (#33, SOL on #24): the retro lineage treats shaping as a short post-kick effect. That is Kick Off 2's aftertouch window and SWOS's ten-update spin sequence, not a ball that keeps carving sideways while it rolls.

## Tunable values and starting ranges

`SimConfig.Tunables` is the supported surface for tools, presets and the later tuning panel:

- `SimConfig.Get(name)` reads a value.
- `SimConfig.With(name, value)` builds a new config with one value changed, and `With(overrides)` changes several. Every variant is validated and hashed like any other config.

Each tunable has two ranges:

- **The valid range** keeps the ball physical and the integration well behaved. The constructor refuses values outside it.
- **The starting range**, where the research gives one, is what a feel test bench should explore first. Playable evidence narrows it. It is not a target.

Every default lies inside its starting range.

| Name | Unit | Valid | Starting | Default |
| --- | --- | --- | --- | --- |
| `kickSpeed` (maximum kick strength) | m/s | 0–60 | 15–32 | 18 |
| `kickLoft` (maximum loft) | rad | 0–1.5 | 0–1.05 (0–60°) | 1.05 (60.2°) |
| `kickSideSpin` (maximum sidespin) | rad/s | 0–100 | 0–40 | 12 |
| `magnusCoefficient` (curve strength) | 1/rad | 0–0.05 | 0.004–0.008 | 0.006 |
| `bounceRestitution` | fraction | 0–1 | 0.5–0.75 | 0.55 |
| `bounceTangentialRetention` | fraction | 0–1 | 0.45–0.85 | 0.8 |
| `bounceSpinRetention` | fraction | 0–1 | 0.15–0.35 | 0.25 |
| `rollingFriction` | fraction of g | 0–2 | 0.05–0.3 | 0.08 |
| `gravity` | m/s² | 0.1–50 | — | 9.81 |
| `ballRadius` | m | 0.05–0.5 | — | 0.11 |
| `settleSpeed` | m/s | 0–5 | — | 0.3 |
| `stopSpeed` | m/s | 0–2 | — | 0.05 |
| `airSpinDecay` | 1/s | 0–60 | — | 0.5 |
| `groundSpinDecay` | 1/s | 0–60 | — | 3.0 |

### Why these starting ranges

- **`kickSpeed`, 15–32 m/s:** from a firm pass up to the hardest real shots.
  - Elite shots average about 22 m/s ([Frontiers, 2026](https://public-pages-files-2025.frontiersin.org/journals/sports-and-active-living/articles/10.3389/fspor.2026.1766063/pdf)).
  - Lab peak ball speeds are 25–26 m/s ([UNE](https://rune.une.edu.au/web/handle/1959.11/15855)).
  - Powerful men's shots exceed 30 m/s.
  - The retro lineage rewards decisive shots, so the range reaches the top. The spike's 18 m/s sits at the soft end, below an average elite shot.
- **`kickLoft`, 0–60°:** the full span the research names, from passes along the ground and low drives to lobs and chips. Since #37 it is the maximum: a kick's loft (0–1) is a fraction of it, so the reference sits at the top of the span.
- **The kick's own knobs** (`kickWindUpSeconds`, `aftertouchSeconds`, `aftertouchBend`, `aftertouchLift`) and the kicker's capabilities are in [kick.md](kick.md). Since #37, `kickSpeed` is the hardest kick, reached by a player with full kick power, and `kickSideSpin` is the sidespin that full sideways aftertouch gives.
- **`kickSideSpin` × `magnusCoefficient`, curve:** curve strength is their product, so the two ranges are set together.
  - Their upper ends give about 6 m/s² sideways at 20 m/s, about 4–5 m of bend in flight on a 25 m cross.
  - That spans real bending free kicks, where the research uses about 600 rpm ≈ 63 rad/s of sidespin ([Craig, 2006](https://hal-amu.archives-ouvertes.fr/hal-01635484/file/Craig%20-%20Die%20Naturwissenschaften%20-%202006.pdf)), up to SWOS-like exaggerated bending.
  - At 0 the ball doesn't curve.
- **`bounceRestitution`, 0.5–0.75:** FIFA's vertical rebound test drops the ball from 2 m.
  - Quality-level turf returns it to 0.6–1.0 m (a [FIFA Quality test report](https://storage.mtender.gov.md/get/74b2fd4a-cc88-4840-90e0-9dfd2303d69f-1716468883631); method in the [FIFA Test Manual](https://www.sporteimpianti.it/wp-content/uploads/2024/04/FIFA-TTM-I-Layout-v8.pdf)), a restitution of about 0.55–0.71.
  - Turfgrass measurements give 0.56–0.69 ([University of Tennessee, 2024](https://trace.tennessee.edu/utk_gradthes/11814)).
  - In this integrator a 2 m drop rebounds 0.47 m at 0.5 and 1.07 m at 0.75, so the range brackets the band.
- **`bounceTangentialRetention`, 0.45–0.85:** FIFA's angle-rebound test band is 45–80 % (same test report).
- **`bounceSpinRetention`, 0.15–0.35:** SOL's research answer on #33, set out on #40.
  - In SWOS, aftertouch is a ten-update shaping sequence, and no persistent spin state lets later bounces curve the ball again. Kick Off 2 limits aftertouch to a very short period after the kick.
  - Real oblique ground contact changes rotation through tangential friction, so spin shouldn't simply become zero.
  - So ELEVEN keeps its physical spin state and has a bounce remove most of it. The band is a game-design range, not a real-football measurement: at 0.25, 12 rad/s becomes 3 after one bounce, 0.75 after two and 0.19 after three.
- **`rollingFriction`, 0.05–0.3 g:**
  - **The real-pitch band, 0.06–0.15 g.** FIFA's ball-roll test releases the ball from 1 m on a 45° ramp, so it leaves at about 3.4 m/s. Real pitches stop it in 4–10 m ([FIFA natural pitch rating system](https://digitalhub.fifa.com/m/58aa765dd3e85f26/original/FIFA-natural-pitch-rating-system_EN.pdf); [Labosport](https://labosport.com/ball-roll-ramp/)). That is a deceleration of about 0.06–0.15 g.
  - **The extension to 0.3 g.** With no drag and constant friction, the real band lets a firm 16 m/s ground pass roll 85–220 m. Readable retro ground passes need the upper part of the range. See *Findings*.

## Curves

`eleven-sim curves <dir> [name=value ...]` characterises the ball under the default config with any tunables overridden. It writes `ball-curves.csv` and `ball-curves.txt`, and CI publishes both on every run (the `ball-curves` artifact).

**Named football actions** (`kind=action`), the actions that recur across the retro lineage:

| Case | Launch | Meaning |
| --- | --- | --- |
| `ground-pass-short` | 10 m/s, 0° | a short pass along the ground |
| `ground-pass-medium` | 16 m/s, 0° | a firm pass along the ground |
| `driven-shot` | `kickSpeed`, 8° | a full-power drive, low over the grass |
| `lofted-pass` | 20 m/s, 35° | a long ball over the midfield |
| `chip` | 12 m/s, 55° | a chip over a keeper or a wall |
| `bending-shot` | 0.85 × `kickSpeed`, 8°, `kickSideSpin` | a shot bent with full sidespin |
| `bending-cross` | 20 m/s, 20°, `kickSideSpin` | a cross curled with full sidespin |
| `bounce-and-roll` | 16 m/s, 15° | a low ball that lands and rolls on |

- **Fixed launches:** most actions are fixed, so presets compare like for like.
- **Config-defined launches:** the full-power and bending actions take the config's maximum kick speed and sidespin, because those knobs define them.
- These actions characterise behaviour; they are not targets.

**The rest of the suite:**
- **The grid** (`kind=grid`): speed 8/12/16/20/25/30 m/s × loft 0/5/10/20/30/45° × sidespin 0/10/20 rad/s.
- **The config's own kick** (`config-kick`): its hardest kick at its maximum loft and largest sidespin.
- **Drops** from 0.5, 1, 2, 5 and 10 m.

Every launch starts on the ground along +x, with no players on the pitch, and runs until rest or for 120 s.

**Columns in `ball-curves.csv`:**

| Column | Meaning |
| --- | --- |
| `carry_m` | Horizontal distance to the first bounce |
| `peak_m` | Highest clearance of the ball's underside |
| `flight_s` | Time to the first bounce |
| `bounces` | Number of bounces |
| `rollout_m` | Distance from the first bounce to rest |
| `total_m` | Distance from release to rest |
| `lateral_flight_m` | Sideways bend at the first bounce, + left |
| `lateral_m` | Sideways distance at rest: bend in flight carried along the exit line |
| `curve_after_landing_m` | How far the rest point lies off the line the ball was travelling when it first landed, + left |
| `rebound_m` | Highest clearance after the first bounce |
| `rest_s` | Time to rest |

**Traceability:**
- Every CSV row carries the config hash.
- `ball-curves.txt` records the build, the config hash and format, every tunable's value with the overrides marked, the actions, the grid and the key numbers.

### The technical reference

[`ball-curves-reference.csv`](ball-curves-reference.csv) holds the default config's curves. `BallCurvesTests` fails if the code produces different ones.

- **It is golden data.** It changes only in a separate commit that states why (AGENTS.md, *Tests and baselines*).
- **It is a technical reference of the placeholder values, not approved feel.** Nothing in it is a target or "correct" ball behaviour.
- **Promotion:** when Erik promotes a feel preset from the playable test bench, that preset's curves become the guarded feel baseline. Changing them then also needs his sign-off.

The workflow, from #24:
1. Research sets the starting model and ranges.
2. Agents build the knobs and the measurements.
3. Erik varies them in a playable test bench.
4. A promising preset is saved.
5. Its curves are characterised.
6. Erik may promote it.

The test bench changes config only through `SimConfig.With`.

### Findings for the test bench

These are measured with the curve tool.

| Configuration | Short pass total | Firm pass total | Chip carry / peak | Bend in flight (shot / cross) | 2 m drop rebound |
| --- | --- | --- | --- | --- | --- |
| Defaults (placeholders) | 63.6 m | 163.0 m | 13.8 / 4.8 m | 0.1 / 1.1 m | 0.57 m |
| Starting-range ends, firm and dead* | 16.9 m | 43.4 m | 13.8 / 4.8 m | 0.0 / 0.7 m | 0.47 m |
| Starting-range ends, slick and lively** | 101.9 m | 260.8 m | 13.8 / 4.8 m | 2.3 / 4.7 m | 1.07 m |

\* friction 0.3, restitution 0.5, retention 0.45, kick 15 m/s, Magnus 0.004.
\** friction 0.05, restitution 0.75, retention 0.85, kick 32 m/s, sidespin 40, Magnus 0.008.

- **Ground passes need firm ground.** With no drag, constant rolling friction in the real-pitch band lets firm passes roll 85–220 m, which isn't readable. The upper part of the starting range (0.2–0.3 g) gives passes of about 15–65 m. If the test bench can't make passes readable without distorting other values, that is the kind of concrete problem that would justify a focused issue on speed-dependent rolling resistance or drag.
- **Where the sideways distance after landing comes from.** #24 reported 34–39 m of sideways distance at rest at the slick end, against 2–5 m of bend in flight, and attributed it to curve on the ground. Measuring it properly (#33) showed it was mostly two other things:
  1. **The exit line.** Bend in flight leaves the ball travelling at an angle, and on slick ground it rolls about 100 m along that line. That is `lateral_m`, a consequence of the ground-pass finding above.
  2. **Bounce hops.** A lively ball is airborne between bounces, and its spin kept curving each hop. Since #40 a bounce keeps only `bounceSpinRetention` of it.

  Removing rolling curve (#33) changed `curve_after_landing_m` only from 20.0 to 18.5 m (bending shot) and from 11.4 to 11.2 m (bending cross) at the slick, lively end, and from 1.3 to 0.8 m and 1.4 to 1.3 m at the defaults.

  **Bounce spin retention (#40)** then took out the hop curve. Bend in flight is unchanged in every case:

  | `curve_after_landing_m` | Before #40 (spin kept) | 0.25 (reference) | 0.15–0.35 (starting range) |
  | --- | --- | --- | --- |
  | Defaults: bending shot / cross | 0.75 / 1.26 m | 0.15 / 0.23 m | — |
  | Slick, lively end*: bending shot / cross | 18.5 / 11.2 m | 2.5 / 1.9 m | 1.4–3.7 / 1.1–2.7 m |

  \* as in the findings table above.

  The lateral distance at rest still includes the exit line: at the slick, lively end the shot rests 22.5 m and the cross 25.7 m off the launch line (from 37.7 and 34.1 m), almost all of it bend in flight carried about 100 m along slick ground.

## Properties

`BallPropertyTests` runs 400 seeded random balls for 600 steps each (10 s at 60 Hz).

**What the fuzz varies:**
- **Configs:** every tunable is drawn uniformly from its whole valid range.
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
ΔE ≤ dt² · |m|²  [ + g · dt · (|vz| + (|m| + g) · dt)  on a step that bounces ]  + 1e-9 · (1 + E)
```

Each term has a source:

- **Magnus:** the acceleration is perpendicular to the velocity, so it does no work in continuous time. A discrete step in the air still adds `½ dt² |m|²`. Allowing `dt² |m|²` is conservative now that a rolling ball gets no Magnus (#33).
- **Gravity:** the semi-implicit step loses `½ g² dt²` per step, so gravity never adds energy.
- **The bounce:** putting the ball back at ground height raises it by at most `|vz| · dt`.

Over the fuzz, energy rose on 1,807 of 240,000 steps. The largest rise was 83 % of its allowance, and a ball that gains 1 % speed per step fails the check.

**Kicks** are the one modelled energy source, so the fuzz has no players.
