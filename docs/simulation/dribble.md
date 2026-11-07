# Ball control: the FREE model

How a player runs with the ball under `ControlModel.Free`: #59, part 1 of #29. This is a research-backed starting hypothesis, not approved feel. The values are technical references for the test bench.

## The research starting answer

#29 records the sources: the [SWOS manual](https://files.swos.eu/manual/SWOS1.1/SWOS1.1manual.pdf) and the [Kick Off 2 manual](https://wiki.kickoff2.com/index.php?title=Kick_Off_2_competition_version_Game_Manual). SOL's handoff on #59 states the relationship they share: **the ball is free, runs ahead of a running player, and is harder to control at speed and on sharp turns.** No historical pixel constants are copied.

- **The ball stays an independent body**, under the ball model's own physics (#24). Possession is a series of discrete touches. It is never parenting, a per-tick override or a magnet.
- **Control can be lost.** An out-of-reach ball, a poor touch, a sharp turn at speed or a kick leaves the ball free, and nothing reattaches it.
- **The first touch differs from dribbling.** A fast incoming ball is harder to settle than a slow one, and better footballers settle more.

## The model id

`SimConfig.ControlModel` is part of the config's encoding and hash (config format 7):

- **`Free`** is used by play, the practice start and the bench, through `SpikeScenario.Config()`.
- **`Off`** is the default of `new SimConfig()`, and behaves as before #59. The kick, pass, ball and movement characterisations and the older rule tests use it, so they keep measuring their own mechanic. The pass drill's receivers have controllers, so under `Free` they would trap the passes it measures.
- **`Attached`** comes in #60, behind the same id.

## State

- **`PlayerState.TouchCooldown`** counts the ticks until the player may touch the ball again. A touch sets it to `touchInterval`, and it counts down at the start of each step, like `KickCooldown`.
- **`PlayerCapabilities.BallControl`** (0–1) is the smallest technique input. It is not a final attribute.
- **State schema 6.**

## Within a step

The step order is: commands → movement → aftertouch → kicks (wind-up, contact) → **touches** → ball integration → goal line.

**At most one player touches the ball on a step**: the nearest eligible one, the lower id on a tie. A player is eligible when:
- they have an accepted command this step;
- they aren't winding up a kick, and their `KickCooldown` and `TouchCooldown` are 0;
- the ball is within `controlReach` horizontally and no higher than `kickMaxBallHeight`.

Touches run after kicks, and **no one touches the ball on the step a kick connects**, whatever the kick cooldown. The kicker can't touch it through their cooldown either. So a kick always leaves the ball free, and nothing redirects it before it has moved. From the next step, another player within reach can touch it: that is how a pass is received. (Codex on #62: the kicker with no cooldown, or a team-mate within reach, used to redirect a kick on its own step.)

A **standing player** (slower than 0.5 m/s, stick released) with the ball at rest within reach leaves it alone.

## A touch

1. **Where the player will be.** The touch predicts the player's position by their next touch, under this step's stick, with the simulation's own movement (`Accelerate`, shared with `StepPlayer`; INV-13).
2. **Where the ball should be.** The target is the lead ahead of that point, along the player's direction of travel, or their heading when nearly stopped:
   lead = `dribbleLeadSlow` + (`dribbleLeadFast` − `dribbleLeadSlow`) · min(1, speed / maxSpeed).
   The lead is the one the drawn state shows, and it keeps the ball within reach for the next touch.
3. **The velocity that gets it there.** The touch picks the velocity that rolls the ball to the target by then under its own rolling friction, or that stops it there sooner. So one rule:
   - pushes the ball on while running;
   - follows a turn;
   - starts a sprint;
   - settles the ball in front of the feet when the player stops or receives.
4. **Bounded.** Both caps scale by (0.5 + 0.5 · ballControl).
   - For a ball coming at the player, the share of the change that **cushions** it, taking off its speed relative to the player, is capped by `receiveStrength`: the first touch.
   - The rest, which **redirects or pushes**, as on a turn, is capped by `touchStrength`.
   - A touch that needs more only partly reaches what it wanted, so the ball separates.
5. **Execution error.** The direction error is uniform within ±`touchError` · (1 − ballControl) · (1 + turn / 90° + overload). It is drawn from the counter-based stream `RandomStream.Touch` (INV-6).
   - The turn is the angle between the ball's current direction and where the touch sends it, 0–180°.
   - The overload is how far a cap was exceeded, as a fraction of 0–1.
   - **The context multiplier is bounded: 1 to 4.** It is 1 for an easy touch, up to 3 for a full reversal, and up to 4 with a cap overloaded too. So `touchError` is the largest error at zero control on an easy touch, and the largest at all is 4 × `touchError` × (1 − ballControl).
     - At the reference 0.3 rad, ballControl 0.7 reaches ±0.09 rad (5°) on an easy touch and ±0.36 rad (21°) at the most.
     - ballControl 0.3 reaches ±0.21 rad (12°) and ±0.84 rad (48°).
   - The error depends on the capability and the context: there is no universal randomness.
6. **Clean-up.** A touch clears the ball's spin and sets `TouchCooldown`. `SimEventKind.Touched` names the player.

## Tunables

These are in the bench's Control tab, with `ballControl` on the Player tab.

| Knob | Unit | Valid | #29 start | SOL's exploration band | Reference |
| --- | --- | --- | --- | --- | --- |
| `controlReach` | m | 0.3–2 | 0.7–1.2 | — | 0.9, the kick reach |
| `dribbleLeadSlow` | m | 0.2–1.5 | 0.35–0.9 | — | 0.5 |
| `dribbleLeadFast` | m | 0.3–3 | 0.8–2.0 | 0.85; 1.2–2.0 for push-and-chase | 0.85 |
| `touchInterval` | s | 0.05–1 | — | 0.15–0.45 (try 0.20, 0.30, 0.40) | 0.25 |
| `touchStrength` | m/s | 1–20 | — | 3–10 | 5 |
| `receiveStrength` | m/s | 1–30 | — | 6–18 | 12 |
| `touchError` | rad | 0–0.6 | — | 0.10–0.40 | 0.3 |
| `ballControl` | fraction | 0–1 | — | — | 0.7 |

**SOL's bands** (research note on #59) are engineering hypotheses from the mechanics and the genre, not measured SWOS or Kick Off 2 constants. The valid ranges stay wider, so the bench can go past them.

**Why the references sit where they do:**
- **`dribbleLeadFast` 0.85 m: the safe, playable reference, not a preferred feel** (SOL on #59). A kick connects only within the 0.9 m kick reach, so the reference keeps the loop SOL names playable: control, run, turn, lose and recover, flick a kick. Push-and-chase, 1.2–2.0 m, is the second experiment; see [The sprint lead](#the-sprint-lead).
- **`touchStrength` 5 m/s.** The reference player keeps 45° and 90° turns at sprint. A poorer one loses a 45° turn at sprint and a 90° turn even at half speed.
- **`receiveStrength` 12 m/s.** A good player settles an 8 m/s head-on pass at once, a poor one in half a second. A 16 m/s ball beats both.

## The characterisation

`eleven-sim dribble <dir> [name=value ...]` writes `dribble-curves.csv`, and CI publishes it. `docs/simulation/dribble-reference.csv` is the recorded technical reference, checked by `DribbleCurvesTests`.

There are seven cases, each under a good profile (`ballControl` 0.9) and a poor one (0.3):

| Case | What happens | Measured |
| --- | --- | --- |
| `straight-jog` | half stick for 5 s from the ball at the feet | mean and max lead over the last 3 s; time beyond reach |
| `sprint` | the same at full stick | the same |
| `turn-45`, `turn-90` | full stick for 2 s, then the stick turned | max separation and time beyond reach over the 2 s after the turn |
| `receive-and-go` | an 8 m/s ball head-on at a player jogging on to it | settle time and distance: from the first touch until the ball is within reach and moving with the player |
| `difficult-first-touch` | the same at 16 m/s | the same, or "not settled" with the final separation |
| `kick-release` | a 0.6-strength kick along the heading while dribbling | the release speed, and touches through the kick cooldown (always 0) |
| `sprint-flick` | a 0.6-strength flick after 2 s of sprinting with the ball, started once at each step of a touch interval (15 starts) | the share that connect: the ball rolls on, untouched, through the wind-up |

**At the references:**
- **Leads:** 0.68 m at a jog and 0.85 m at a sprint.
- **Every flick at a sprint connects**, at both profiles.
- **The good profile** keeps 45° and 90° turns, settles the 8 m/s pass at once, and loses the 16 m/s ball.
- **The poor profile** loses a 45° turn for 1.75 s and a 90° turn entirely, settles the 8 m/s pass in 0.5 s over 1.6 m, and loses the 16 m/s ball.

**The properties tested** (`DribbleTests`):
- finite state;
- the ball moves only by its velocity, so there's no teleport;
- touches happen only within reach, and at most one per step;
- no touch during a wind-up or kick cooldown, so a kick releases the ball;
- under `Off`, nobody touches;
- determinism, and exact snapshot/restore.

The tests pin relationships, not values:
- the lead grows with speed;
- 90° loses more than 45° at the poor profile;
- the same turn is harder at speed;
- better control keeps a sharp turn tighter;
- a slow ball is settled, and a fast one beats the first touch;
- a sprint flick connects at the reference lead, and less often with the lead beyond the kick reach.

## The sprint lead

SOL asked (#59) for evidence across sprint leads of 0.85, 1.2, 1.5 and 2.0 m, all from `eleven-sim dribble <dir> dribbleLeadFast=… [controlReach=…]`.

**Beyond the control reach, the dribble is lost; it doesn't become push-and-chase.**
- With `controlReach` at 0.9 m, a lead of 1.2 m or more sends the ball beyond reach at every touch.
- It rolls on faster than the sprinting player, and rolling friction (0.78 m/s²) takes seconds to bring it back.
- At 1.2 m the sprint is beyond reach for the whole 3 s measured, with the ball 5.1 m away at most for good control and 8.7 m for poor. At 2.0 m it is 11.6 m and 14.2 m.

**With `controlReach` raised to match, the ball is held at the longer lead, and no sprint flick connects.**
- At 1.2 m (reach 1.3), 1.5 m (reach 1.6) and 2.0 m (reach 2.0), the ball runs that far ahead, and 0% of sprint flicks connect against the 0.9 m kick reach.
- At 1.2 and 1.5 m, turns behave as at the reference: good control keeps them, poor control loses them. At 2.0 m the ball sits at the edge of a 2.0 m reach and is counted beyond it for much of the run, even straight ahead.

**Sprint flicks fall off a cliff at the kick reach**, measured with `controlReach` 1.1 or 1.25 so the dribble holds:

| `dribbleLeadFast` | 0.85–0.95 m | 1.0 m | 1.05 m and more |
| --- | --- | --- | --- |
| good (0.9), flicks connecting | 100% | 100% | 0% |
| poor (0.3) | 100% | 93% | 0% |

So in this model a flick at a sprint is all or nothing by lead. Kick Off 2's timed push-and-chase, where the ball runs out of reach and the player catches it, isn't a setting of these knobs. It would take a different touch target: one that pushes the ball out and lets it come back into reach.

**This is a known limitation of FREE v1.** SOL's decision on #62: the sweep shows a missing push-and-recovery rule, not a lead to tune. #63 is a separate push-and-chase experiment, with catch-up physics and timing-dependent kick windows. The 0.85 m lead and its 100% sprint flicks remain a technical reference, not approved feel.

## For Erik on device

**The loop to play:** run onto the ball, carry it, change direction, lose and recover it, and flick a kick with Candidate A.

On the bench, try:
- `ballControl` 0.3 and 0.9;
- `dribbleLeadFast` towards 2.0, with `controlReach` raised to match or the ball is lost: the ball runs further ahead, and sprint flicks stop connecting (see [The sprint lead](#the-sprint-lead));
- `touchStrength` and `receiveStrength`.

None of the defaults is chosen as feel.
