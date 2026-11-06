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

Touches run after kicks. So a kick's own step and its cooldown always leave the ball free, and nothing redirects a kicked ball.

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
   - The turn is the angle between the ball's current direction and where the touch sends it.
   - The overload is how far a cap was exceeded, as a fraction of 0–1.
   - The error depends on the capability and the context: there is no universal randomness.
6. **Clean-up.** A touch clears the ball's spin and sets `TouchCooldown`. `SimEventKind.Touched` names the player.

## Tunables

These are in the bench's Control tab, with `ballControl` on the Player tab.

| Knob | Unit | Valid | #29 start | Reference |
| --- | --- | --- | --- | --- |
| `controlReach` | m | 0.3–2 | 0.7–1.2 | 0.9, the kick reach |
| `dribbleLeadSlow` | m | 0.2–1.5 | 0.35–0.9 | 0.5 |
| `dribbleLeadFast` | m | 0.3–3 | 0.8–2.0 | 0.85 |
| `touchInterval` | s | 0.05–1 | — | 0.25 |
| `touchStrength` | m/s | 1–20 | — | 5 |
| `receiveStrength` | m/s | 1–30 | — | 12 |
| `touchError` | rad | 0–0.6 | — | 0.3 |
| `ballControl` | fraction | 0–1 | — | 0.7 |

**Why the references sit where they do:**
- **`dribbleLeadFast` 0.85 m.** A kick connects only within the 0.9 m kick reach. A sprint lead beyond reach makes a sprinting player's kick miss unless it is timed, which is Kick Off 2's push-and-chase. The reference keeps the playable loop SOL names (control, run, turn, lose and recover, flick a kick), and the bench reaches 2.0 m for the push-ahead style.
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

**At the references:**
- **Leads:** 0.68 m at a jog and 0.85 m at a sprint.
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
- a slow ball is settled, and a fast one beats the first touch.

## For Erik on device

**The loop to play:** run onto the ball, carry it, change direction, lose and recover it, and flick a kick with Candidate A.

On the bench, try:
- `ballControl` 0.3 and 0.9;
- `dribbleLeadFast` towards 2.0, the push-and-chase style;
- `touchStrength` and `receiveStrength`.

None of the defaults is chosen as feel.
