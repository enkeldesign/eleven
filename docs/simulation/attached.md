# Ball control: the ATTACHED comparator and the live switch

How a player keeps the ball under `ControlModel.Attached` (#60, part 2 of #29), and how play switches between FREE and ATTACHED without a restart. This is a research-backed comparator for an A/B test against FREE ([dribble.md](dribble.md)), not approved feel. Its values are technical references for the test bench. No winner is chosen here: Erik tries both on device.

## The research starting answer

SOL's handoff on #60, from #29's sources ([SWOS](https://files.swos.eu/manual/SWOS1.1/SWOS1.1manual.pdf), [Kick Off 2](https://wiki.kickoff2.com/index.php?title=Kick_Off_2_competition_version_Game_Manual)), asks for **a credible, breakable attached comparator, not a straw man.**
- **Same ball, same physics.** It uses the same ball and #24's physics. The controller changes the ball's velocity, never its position, and nothing is parented.
- **Bounded correction.** The correction is damped and capped every step, with a deadband. A ball 1–2 m out can't snap back in one step.
- **Failure is real.** Control fails credibly under overload: on a fast turn, against a hard incoming ball, or when the player stops dead from a sprint.
- **Ball control matters.** Ball control reduces drift and recovery time.
- **Hysteresis.** The ball is gained within a smaller reach than the one it is lost beyond. At the references that is 0.9 m against 1.35 m (see [Tunables](#tunables) for when it holds).

## State

- **`BallState.AttachedTo`** is the holder's player id, or 0 when the ball is free.
  - Only ATTACHED sets it. Any other model clears it on its first step.
  - State schema 7.
- **`SimEventKind.Attached`** and **`Released`** name the player who took or lost the ball. Presentation can show them.

## Within a step

The step order is the same as FREE's:

commands → movement → aftertouch → kicks → **ball control** → ball integration → goal line.

Under ATTACHED, the ball-control slot does this:

1. **A kick released it.** If a kick or quick pass connected this step, a held ball is released. Nothing takes it on the kick's own step, and the kicker stays excluded through their wind-up and kick cooldown.
2. **The holder loses it** when any of these holds:
   - the ball is beyond the **loss reach**, the larger of `attachedReleaseReach` and `controlReach`;
   - the ball is above `kickMaxBallHeight`;
   - the holder has no accepted command.

   A released stick is still a command, so letting go of the stick alone doesn't drop a settled ball. The holder keeps the ball through their own wind-up, until contact.
3. **The nearest eligible player takes a loose ball.** Eligible means:
   - an accepted command;
   - no wind-up and no kick cooldown;
   - the ball within `controlReach` and no higher than `kickMaxBallHeight`.

   A player strictly nearer than the holder takes it over. The lower id wins a tie. FREE's touch cooldown plays no part.
4. **Taking control is a first touch,** as under FREE.
   - It cushions the ball's velocity relative to the taker by at most `receiveStrength` · s, where s = 0.5 + 0.5 · ballControl. That is FREE's knob, so reception compares like for like.
   - It clears the ball's spin and closes every aftertouch window.
5. **The holder steers the ball** toward a lead point:
   - **The target:** lead = `attachedLeadSlow` + (`attachedLeadFast` − `attachedLeadSlow`) · min(1, speed / maxSpeed), along the holder's direction of travel, or their heading when nearly stopped.
   - **The closing speed:** min(`attachedSpeedCap` · s, √(2 · `attachedAcceleration` · s · (*d* − `attachedDeadband`))), where *d* is the ball's distance from the target. It is the speed the capped acceleration could stop from at the deadband's edge, so the ball doesn't overshoot.
   - **The wanted velocity** is the holder's velocity plus the closing speed toward the target.
   - **Inside the deadband** the ball only keeps pace with the holder, so a held ball sits up to a deadband from its target.
   - **The cap.** The change is capped at `attachedAcceleration` · s · dt per step: 0.35 m/s at the reference and ballControl 0.7.

ATTACHED draws no random numbers. Its failures come from the caps.

## Tunables

These are on the bench's Attached tab. The capture radius is `controlReach` on the Control tab, shared with FREE, and `ballControl` is on the Player tab.

| Knob | Unit | Valid | SOL's band | Reference |
| --- | --- | --- | --- | --- |
| `attachedLeadSlow` | m | 0.2–1.5 | 0.4–0.7 | 0.5 |
| `attachedLeadFast` | m | 0.3–2 | 0.7–1.1 | 0.8 |
| `attachedAcceleration` | m/s² | 1–60 | 8–30 | 25 |
| `attachedSpeedCap` | m/s | 0.5–10 | 2–4 | 3 |
| `attachedDeadband` | m | 0–0.6 | 0.1–0.3 | 0.2 |
| `attachedReleaseReach` | m | 0.5–3 | 1.1–1.7 | 1.35 |

Every value in each range is valid on its own. The ball is lost beyond the larger of `attachedReleaseReach` and `controlReach` (`SimConfig.AttachedLossReach`).
- **Strict hysteresis holds only while `attachedReleaseReach` is greater than `controlReach`.** At or below it, the ball is gained and lost at the same reach, with none.

**`attachedAcceleration`'s reference is 25 m/s².** SOL's review on #64 moved it from the first reference, 15, which is kept for comparison.
- At 15, even good control lost a dead stop and a 90° turn from a sprint, where FREE keeps both. That made ATTACHED less forgiving than FREE in the very cases it is meant to soften.
- At 25, good control keeps every turn and stop, and poor control still loses the 90° turn.
- It is a more informative comparator, not approved feel.

## The live switch

`ControlSwitch.To(simulator, model)` returns a simulator from **the same state** under the same config with only the model changed. The host calls it between steps, from the bench's "Ball control" switch.

- **Nothing in the state changes.** Positions, velocities, spin, the touch cooldown, a kick's wind-up and an aftertouch window carry over, and the state hash is the same either side.
- **Nothing is backdated.** The new model acts on its first step, under its own eligibility.
  - Switching to ATTACHED never treats a distant loose ball as held.
  - Switching to FREE ends the attachment at once. FREE's touch cooldown is untouched, so FREE may touch on its first step.
- **Nothing is lost or duplicated.** The host leaves the touch adapter alone, so a gesture in progress, or a kick queued for the next tick, is delivered once, to the new simulator.
- **Identity.** The applied bench values take the new model, and so its config hash. Pending edits stay pending.
- **Recording.** The run recording and the felt-wrong recorder start a new segment at the switch, since each holds one config. A capture taken soon after a switch covers only the seconds since it, and every capture replays as before. The trace line names the model.

Candidate A is the only touch candidate (#28). The switch depends on nothing in its gesture internals.

## The characterisation

`eleven-sim dribble` runs every case under both models ([dribble.md](dribble.md#the-characterisation)).

**ATTACHED's own measures:**
- **`acquired`** and **`released`**: the takes and losses.
- **`max_correction_mps2`**: the largest change in ball velocity per second on a held step, rolling friction included.
- **`lost_s`**: time not attached.

**The cases added for #60:**
- **`sprint-stop`**: full stick for 2 s, then the stick released.
- **`switch-in`**: a sprint dribble switched into the model at 2 s.

**At the references** (good 0.9, poor 0.3; ATTACHED at 25 m/s²):

| Case | FREE good | FREE poor | ATTACHED good | ATTACHED poor |
| --- | --- | --- | --- | --- |
| sprint lead | 0.85 m | 0.82 m | 0.72 m | 0.72 m |
| sprint-stop | kept | lost 1.7 s | kept, 0.75 m at most | kept, 1.17 m at most |
| turn-45 at sprint | kept | lost 1.75 s | kept, 0.90 m at most | kept, 1.00 m at most |
| turn-90 at sprint | kept | lost | kept, 1.04 m at most | lost 1.62 s |
| receive 8 m/s | settled at once | 0.50 s | settled at once | 0.12 s |
| first touch 16 m/s | not settled | not settled | settled in 0.27 s | lost |
| kick-release | released, 0 touches after | the same | released, 0 after | the same |
| sprint flicks | 100% | 100% | 100% | 100% |
| switch-in | control on the first step | the same | the same | the same |

**What the references show:**
- **Good control.** It keeps every turn and stop under both models.
- **Poor control.** It is more forgiving under ATTACHED, which loses only the 90° turn. FREE loses the stop and both turns.
- **Reception.** ATTACHED is stronger at taking a hard ball: the first touch plus the continuous correction holds a 16 m/s ball at good control.

**Settled is model-specific.** FREE settles within its control reach, and ATTACHED once the ball is held, possibly out to the loss reach. So equal `settle_s` values are not equal kick-ready control (SOL on #64). On device, watch the separation, and whether a kick connects after a reception.

**The correction's effect, by `attachedAcceleration`** (time lost; – means kept):

| `attachedAcceleration` | sprint-stop good / poor | turn-45 good / poor | turn-90 good / poor |
| --- | --- | --- | --- |
| 15 (the first reference) | 1.57 s / 1.70 s | – / 1.53 s | 1.65 s / 1.70 s |
| 20 | – / 1.65 s | – / – | 1.57 s / 1.67 s |
| 25 (reference) | – / – | – / – | – / 1.62 s |
| 30 | – / – | – / – | – / 1.50 s |

The good-and-poor contrast at 25 comes from these turn and stop cases only. It doesn't show that the controller is never over-forgiving elsewhere in play.

**The properties tested** (`AttachedTests`, `ControlSwitchTests`):
- finite state;
- the ball moves only by its velocity, and every held step's correction is within its cap;
- no snap back from 1.2 m;
- a kick releases the ball, and nothing takes it on its step;
- a standing player stops and holds the ball;
- a released stick keeps a settled ball;
- the hysteresis;
- ownership, and a nearer player taking over;
- other models clearing the attachment;
- determinism, and exact snapshot/restore;
- a switch keeping the state and its hash;
- no teleport across a switch;
- control taken on the next eligible step, but never of a distant ball;
- a wind-up and an aftertouch window carried across a switch;
- a session with switches replaying exactly, segment by segment.

**The relationships tested, not the values:**
- the lead grows with speed;
- a sharper turn, more speed and poorer control separate more;
- better control drifts less;
- an 8 m/s ball is settled and a 16 m/s ball beats the poor profile.

## For Erik on device

**The comparison to play.** Play the same loop under each model, and switch on the bench, at the top of the sheet:
- run onto the ball;
- carry it, change direction, and stop;
- lose it and win it back;
- flick a kick with Candidate A.

**On the bench, try:**
- `attachedAcceleration` 25 against 15, the first reference;
- `ballControl` 0.3 against 0.9;
- `attachedDeadband` and `attachedLeadFast`.

None of the defaults is chosen as feel.
