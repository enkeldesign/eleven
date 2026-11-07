# The quick pass

How a controlled player's quick pass works: the owned command only asks for a pass, and the footballer picks the receiver, reads their run and weights the ball, inside the simulation. This page covers the rule, the knobs and the measurements ([#38](https://github.com/enkeldesign/eleven/issues/38), part 3 of [#26](https://github.com/enkeldesign/eleven/issues/26)).

**The values are technical references.** This page describes the model, the knobs and the instruments, not ELEVEN's chosen feel.

## Research basis

**#26's starting rule (SOL)** is a convenience action for obvious fast progression ("clap-clap" football), while the manual kick stays the high-skill way to play into space:
1. consider teammates primarily ahead or advancing, not simply the nearest;
2. prefer an obvious short lane;
3. predict where the receiver will be, not only where they are;
4. stay predictable enough that the player learns what a tap means.

The footballer supplies the intelligence:
- **perception** decides who is noticed and how well a run is read;
- **passing execution** shapes the pass.

**The SWOS port's quick pass** ([`player.cpp`](https://github.com/zlatkok/swos-port/blob/master/src/game/player.cpp): `doPass` and `getClosestNonControlledPlayerInDirection`), read on #38:
- **Receiver:** the teammate closest to the ball whose *own facing* is within ±22.5° of the passer's. In effect it picks the nearest teammate running the same way; it is not a cone of positions in front of the passer.
- **Aim:** the receiver's current position, with no lead. The receiver is then steered onto the ball.
- **Speed:** eight distance steps, plus a bonus from passing skill.
- **Execution:** only CPU passes can be botched by low skill.
- **No receiver:** a free pass along the facing direction.

ELEVEN follows #26's rule, which asks for prediction, and takes SWOS's free pass and distance-weighted speed. Whether the perception gate should instead be SWOS's facing match is a research question on #38.

## The command

`Command.QuickPass` asks for the pass. It is recorded as given, before any assistance (INV-10).
- **A manual `KickIntent` in the same command takes precedence**, so assistance never overrides or redirects a manual kick ([kick.md](kick.md)).
- **When it is accepted:** like a manual kick, only while the player isn't winding up and has no cooldown.

## The rule

Resolved in the kick stage of the step that accepts the intent, after every player has moved (`Simulator.ResolveQuickPass`):

1. **The candidate set** is every teammate (same `TeamId`, not the passer) who is:
   - within `passMaxRange` of the passer;
   - within ± the passer's `passVision` of their heading, as seen from the passer.

   This is the documented set; no pass ever goes outside it.
2. **The choice** is made on what the passer sees now. The cheapest candidate wins, and ties go to the lowest id:

   ```
   cost = distance × (1 + passBackWeight × (1 − cos θ) / 2)
   ```

   - `distance` and θ, the angle between the passer's heading and the line from the ball to the receiver, both come from the receiver's current position.
   - Ahead costs the distance, to the side (1 + w/2) times it, and straight back (1 + w) times it. So an obvious receiver ahead beats a closer one behind, all else equal, and a back pass to someone right beside the passer is still possible.
   - **Why current positions, not predicted ones.** Charging candidates by the distance to their predicted receive point would penalise a runner for running away: the opposite of preferring receivers ahead and advancing. The first design on #38 did that; the drill measured it, and it was changed before the PR.
3. **The aim** leads the chosen receiver:
   - It is their position plus the run the passer reads (`passAnticipation` × their velocity), projected to the first ball step after contact at which a pass weighted for that point can get there.
   - The search rolls the ball with the simulation's own integration (`IntegrateBall`, INV-13), up to 8 s ahead. It scans forward in strides of a tenth of a second for the first reachable step, then bisects inside that stride.
   - Reachability can switch back: with a weak kick, a runner coming back past the ball is reachable only for a while. That's why the search takes the first chance rather than assuming one crossing (Codex on #43).
   - If no step within the horizon is reachable, the pass goes where the receiver is.
   - At anticipation 0 the pass goes where the receiver is; at 1 it meets a runner who keeps their line and speed.
4. **The weight:** a ground pass (loft 0) launched at `√(arrival² + 2 · rollingFriction · g · distance)`, so it arrives at about `arrival = passArrivalSpeed + run`.
   - `run` is the read run's speed along the pass, never below 0. So a standing receiver gets `passArrivalSpeed`, and a runner a ball `passArrivalSpeed` faster than their run, which they can take in stride.
   - It is capped by the passer's kick power × `kickSpeed`.
   - **Why relative to the run.** At a fixed 4 m/s, a pass to a runner going away at 5 m/s is slower than him at the end. The search then met him 51 m out after 7.5 s, and the ball overtook him a metre to the side on the way. The drill measured this, and the rule changed before the PR.
5. **No candidate:** a free pass along the heading, weighted the same way for `passFreeDistance`, with the passer as the runner (#65).
   - `run` is the passer's own velocity along the pass line, read when the tap is accepted, never below 0.
   - So a sprinting passer's tap goes ahead of them, at about `passArrivalSpeed` faster than their run at `passFreeDistance`, instead of being caught.
   - A standing passer's free pass is unchanged.
   - **The heading is the direction of travel,** set every movement step, and the free pass goes along it. So the passer's run along the line is their speed, and the clamp at 0 is only a safeguard: braking, reversing or turning when tapping shrinks the boost, never below a standing passer's.

**Then it is an ordinary kick.** The resolved line, strength and loft go into the same wind-up and launch as a manual kick, so the passer's `kickError` shapes the execution of assisted passes too, and aftertouch can shape them.

**State and events.** `PlayerState.WindUpTarget` holds the chosen receiver's id through the wind-up, so presentation can show who the pass is for (INV-12). The `QuickPassResolved` event (subject: the passer; `Other`: the receiver, or 0 for a free pass) reports the choice even when there is no wind-up and the pass connects on its own step. It is 0 for a manual kick, a free pass, or no wind-up. Player ids are therefore positive, and `Simulator` refuses a target outside a wind-up or off the passer's team.

**Lanes.** With no opponents yet, an obvious lane is simply the shortest; lane blocking comes with opponents.

**Not modelled here:**
- receivers don't react to the pass: there is no steering onto it, and no trapping;
- the pass is always a ground pass;
- there is no SWOS facing-match gate.

## Knobs and capabilities

The config's values are in `SimConfig.Tunables`:

| Name | Unit | Valid | Starting | Reference |
| --- | --- | --- | --- | --- |
| `passMaxRange` | m | 5–60 | — | 30 |
| `passArrivalSpeed` (over the receiver's run) | m/s | 0–15 | — | 4 |
| `passBackWeight` | factor | 0–10 | — | 2 |
| `passFreeDistance` | m | 1–40 | — | 10 |

The passer's own values are in `PlayerCapabilities.Parameters`:

| Name | Unit | Valid | Starting | Reference |
| --- | --- | --- | --- | --- |
| `passVision` | rad, half-angle | 0–π | — | 1.05 (60°) |
| `passAnticipation` | fraction | 0–1 | — | 0.75 |

None has a research range; the ranges are open questions for SOL on #38. They are not the final attribute taxonomy either: a vision- or anticipation-type attribute may shape them later.

## Measurements

`eleven-sim passes <dir> [name=value …]` runs the practice drills under the real simulator (`PassDrill`).
- **The drill:** one controlled player at the origin, facing +x, with the ball at rest at (0.5, 0) and inert practice receivers on their team. A receiver's own controller holds a fixed stick throughout, so a runner keeps running. There is no team AI (INV-9). The passer asks for a quick pass on tick 0.
- **Overrides:** a name can be a config tunable or a capability. Capabilities change the first profile.
- **Output:** `pass-curves.csv` and `pass-curves.txt` (build, config, every tunable and the profile written exactly, the drills, the key numbers). CI publishes both as the `pass-curves` artifact.

**Drills:**
- `standard`:
  - `ahead-advancing` at (14, 3), running +x at 5 m/s;
  - `behind-closer` at (−7, −1), standing;
  - `wide` at (3, 6), standing about 63° off the heading: just outside the reference view, and the cheapest pass for anyone who sees it.
- `crossing-run`: one runner at (14, 8), crossing the passer's line at 5 m/s.

**Profiles,** all with the reference's `kickError` of 0.05 rad:

| Profile | Vision | Anticipation |
| --- | --- | --- |
| `profile` (the reference) | 60° | 0.75 |
| `literal-narrow` | 34° | 0 |
| `reader-wide` | 120° | 1 |
| `all-round` | 180° | 0.75 |

**Columns:**

| Column | Meaning |
| --- | --- |
| `target` | The receiver chosen, or `free`. |
| `line_deg` | The resolved pass line. |
| `lead_m` | How far the line runs from the receiver's position at the intent: 0 aims at where they are. |
| `launch_speed_mps` | The ball's speed after the contact step. |
| `closest_m`, `closest_s`, `arrival_speed_mps` | The ball's closest approach to the receiver's centre, when, and its speed then. Empty for a free pass. |

[`pass-reference.csv`](pass-reference.csv) is the default output. `PassCurvesTests` fails if the code produces different curves. It is golden data and a technical reference, not approved feel.

### The free pass (#65)

`eleven-sim passes` also writes `free-pass-curves.csv`: a quick pass with no team-mate in view.
- **The cases:** tapped while standing, walking, jogging, sprinting, and turning (a sprint, then the stick at 90°, tapped 3 steps on).
- **Under:** FREE and ATTACHED, by the reference player and a weak kicker (`kickPower` 0.5).
- **The columns:**
  - the passer's speed;
  - the launch speed;
  - the ball's lead over the passer along the line at contact and at 0.25, 0.5 and 1 s after it;
  - any touch or take of the ball in that second, which is always 0: the pass is released.

[`free-pass-reference.csv`](free-pass-reference.csv) is its reference, checked by `FreePassCurvesTests`.

| Passer (reference) | Speed | Launch | Lead at contact → 1 s, FREE | ATTACHED |
| --- | --- | --- | --- | --- |
| standing | 0 m/s | 5.6 m/s | 0.69 → 5.9 m | 0.69 → 5.9 m |
| walking | 2.1 | 7.3 | 0.69 → 5.5 | 0.65 → 5.4 |
| jogging | 3.5 | 8.5 | 0.76 → 5.3 | 0.61 → 5.2 |
| sprinting | 7.0 | 11.7 | 0.92 → 5.2 | 0.79 → 5.1 |
| turning | 5.9 | 10.7 | 0.79 → 9.6 | 0.89 → 9.7 |

The weak kicker is held to 9.0 m/s, so a sprinting tap still pulls away, from 0.88 m to 2.5 m in a second. Before #65, every free pass launched at 5.6 m/s, and a sprinting passer caught it within half a second.

### Findings for the test bench

| Drill / profile | Target | Lead | Closest approach | When | Ball speed then |
| --- | --- | --- | --- | --- | --- |
| standard / reference | ahead-advancing | 1.3 m | 1.3 m | 4.3 s | 6.6 m/s |
| standard / literal-narrow | ahead-advancing | 0 m | 13.4 m | 1.5 s | 5.0 m/s |
| standard / reader-wide | wide | 0 m | 0.02 m | 1.5 s | 4.0 m/s |
| standard / all-round | wide | 0 m | 0.02 m | 1.5 s | 4.0 m/s |
| crossing-run / reference | crossing-run | 9.6 m | 2.3 m | 2.3 s | 4.8 m/s |
| crossing-run / literal-narrow | crossing-run | 0 m | 7.4 m | 1.5 s | 5.3 m/s |
| crossing-run / reader-wide | crossing-run | 11.3 m | 0.1 m | 2.4 s | 5.4 m/s |

For a pass aimed behind its receiver, the closest approach is when the ball passes them by, not a reception.

- **Perception decides the target.** Only a passer who sees the near wide receiver picks them. No profile picks the closer receiver behind.
- **Anticipation decides the lead.** Reading none, three quarters and all of a crossing run comes within 7.4 m, 2.3 m and 0.1 m of the runner.
- **The reference reads three quarters of a run, so it underleads by design.**
  - It misses a runner going straight away at 5 m/s by 1.3 m, and a crossing one by 2.3 m.
  - Its pass to the runner going away still takes 4.3 s to reach him, because he is running away from the ball.
  - If the bench finds quick passes to runners unhelpful, the knobs are `passAnticipation` and `passArrivalSpeed`.

## Tests and properties

`PassTests`:
- **Choice:**
  - the receiver ahead beats a closer one behind, and only `passBackWeight` makes the difference;
  - the headless drill shows the same;
  - only teammates in view and in range are candidates, with a free pass when there is none;
  - perception decides who is noticed;
  - a manual kick takes precedence.
- **Lead:**
  - a full reader leads a crossing runner to under 0.5 m, and a literal passer misses by over 3 m;
  - the lead changes deterministically with anticipation;
  - in the drill, a full reader meets a runner going away, with the ball arriving faster than he runs.
- **Weight and execution:** a standing receiver gets the configured arrival speed, and the passer's execution error shapes assisted passes.
- **Replay, snapshots and validation:**
  - commands recorded before assistance replay tick for tick;
  - snapshot and restore during the wind-up continue identically;
  - the simulator refuses a target that isn't a winding-up player's teammate.

`KickPropertyTests` adds teams, quick passes and quick passes overridden by manual kicks to the fuzz. Its new property is that every quick pass picks a documented candidate, or plays a free pass only when there is none, and is a ground pass. The mutant hands passes to someone outside the set (an opponent, someone out of view, or the passer) and is caught. The existing properties (a manual kick's intent kept, wind-up timing, the window, expiry, bounds) hold with quick passes in the mix.
