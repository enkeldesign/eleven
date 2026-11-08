# The turn with the ball

How a footballer turns sharply with the ball (#66), under FREE ([dribble.md](dribble.md)) and ATTACHED ([attached.md](attached.md)) alike. A change of direction short of the turn angle is a cut under FREE ([cut.md](cut.md), #75). It is a research-backed starting hypothesis, not approved feel. Its values are technical references for the test bench, and the FREE-or-ATTACHED choice (#29) stays open.

## The research starting answer

SOL's research on #66 cites coaching guides on turning: [Special Olympics](https://read.nxtbook.com/special_olympics/guide_books/football_coaching_guide_2021/fundamentals_turning.html) and [England Football](https://learn.northeurope.englandfootball.com/articles-and-resources/coaching/resources/2023/How-to-coach-turning-in-football). They support a relationship, not game timing:
- **Real players arrest and redirect the ball near the feet.** The drag-back, the inside or outside hook and the Cruyff turn stop the ball close to the planted foot while the body pivots, then play it the new way.
- **The ball never chases its old front position through the player.**
- **Capability decides execution.** There is no universal perfect turn, and no extra gesture: movement expresses the intent.

## What the turn fixes

Before the turn, a reversal left the ball trailing (Erik on build 128).
- **Why it trailed.** Each model's control kept steering the ball toward a lead ahead of the player's direction of travel. On a reversal that point swings through the player, so the ball had to stop, reverse and pass them: a change of about 14 m/s from a sprint.
- **The size of it.** At good control (0.9) and a sprint, the ball took 0.68 s under FREE and 0.78 s under ATTACHED to get 0.3 m ahead again, timed from when the player first moved the new way. It trailed by up to 0.64 m and 0.89 m.
- **FREE's turns also depended on the touch phase.**
  - Between touches the ball, pushed for the sprint, runs on while the player brakes. Unless a touch is due as the stick turns, the ball is out of reach before the next one.
  - Started at each of the 15 steps of a touch interval, good control kept a sprint reversal from only 2 of them, and the bench's player (0.7) from none.
  - The dribble characterisation's turn cases always turn on a touch step, the best case.

## The rule

1. **A turn starts** when the stick asks for a direction more than `turnAngle` from where the player is moving, at more than 0.5 m/s, the trap speed.
2. **The trap, as it starts.**
   - **The pivot** is where the player's motion along their direction of travel ends under this stick, predicted with the simulation's own movement (`Accelerate`, INV-13).
   - **The target** is 0.3 m beside the pivot, on the side the turn goes, at the planted foot. On an exact reversal it is the player's left.
   - **The velocity** is the one that rolls the ball to the target by the time the player reaches the pivot, or stops it there sooner. That is the touch's own rule, with this target and horizon.
3. **The touch out, as the player comes round:** on the first step they move within 90° of the stick.
   - It plays the ball to the lead ahead of where they will be by their next touch, the touch's own target (`TouchTarget`).
   - FREE uses its dribble lead, ATTACHED its own lead.
4. **Between the two touches,** neither model's own control acts.
   - FREE makes no ordinary touch. A trap on the cadence refines the first.
   - ATTACHED makes no correction.

   After the touch out, each model resumes.

`PlayerState.Turning` is set by the trap. It is cleared by:
- the touch out, or an ordinary touch;
- a kick's wind-up;
- losing the ball: beyond the control reach under FREE, released under ATTACHED. A take under ATTACHED always starts with the ordinary correction (Codex on #73).

So a player's next possession after losing the ball mid-turn is an ordinary one. A turn whose stick is let go mid-way holds until the stick asks for a direction again.

## Bounds and failure

- **The strength.** Each of the two touches changes the ball's velocity by at most `turnStrength` × (0.5 + 0.5 · ballControl): 5.1 m/s at the bench's 0.7.
  - A trap that needs more only partly arrests the ball, which rolls on past the pivot.
  - Under FREE, the touch out's cushioning part is still capped by `receiveStrength`, as on any touch.
- **The cadence.** The touch interval is a stride's cadence on the run, and a turn breaks it. Under FREE the trap and the touch out come when the turn starts and ends, whatever the touch cooldown, at most one of each per turn. Both set the cooldown.
- **The error.**
  - Under FREE both touches carry the touch's execution error: `touchError` · (1 − ballControl) · (1 + turn / 90° + overload), from `RandomStream.Touch` (INV-6).
  - Under ATTACHED, like its take, neither does, so ATTACHED still draws no random numbers.
- **Eligibility is the touch's.**
  - The ball must be within `controlReach` under FREE, or held under ATTACHED, and no higher than `kickMaxBallHeight`.
  - There is no turn touch during a wind-up or a kick cooldown, or on the step a kick connects, and a wind-up ends the turn.
  - So nothing overrides a manual kick, a quick pass, its wind-up or its aftertouch.
- **The ball stays an independent body.** It moves only by its velocity, and each turn touch is one bounded change, like a touch or ATTACHED's take. There is no parenting, no snap and no change of position.
- **Failure is real.** A fast ball, poor control or the error at speed leaves the ball rolling past the pivot. Under FREE it can go out of reach, and under ATTACHED beyond the loss reach.

## The shared contract

The trap and the touch out are the same in both models: the same moments, the same targets apart from each model's lead, and the same strength. Only the execution error differs. Outside a turn, FREE's cadence and ATTACHED's capped correction run unchanged. `TurnTests` tests the contract under both models.

## State, config and events

- **`PlayerState.Turning`** is a byte, 0 or 1, after the touch cooldown. State schema 10.
- **Config format 11** adds `turnAngle` and `turnStrength`, on the bench's Control tab.
- **`SimEventKind.Turned`** names the player, with `Other` 1 for the trap and 2 for the touch out. Under FREE it comes with `Touched`. A later player model can animate it.

## Tunables

| Knob | Unit | Valid | Reference |
| --- | --- | --- | --- |
| `turnAngle` | rad | π/2–π | 2.0 (115°): 135° and 180° are turns; a 90° cut isn't |
| `turnStrength` | m/s | 0–20 | 6 |

- **`turnStrength` 0 turns the turn off**, and every touch is ordinary. That is the baseline, for comparing with and without the turn.
- **The foot offset**, 0.3 m, is a technical constant, not a knob.
- **Why a strength of its own.**
  - `receiveStrength` (12) would let poor control trap a sprint ball every time.
  - `touchStrength` (5) can't arrest one at the bench's 0.7.
  - A knob of its own keeps the turn's feel apart from reception and dribbling.
- **Why 6.** It keeps a clear gradient across 0.9, 0.7 and 0.3. The sweep, a 180° sprint reversal, worst over the starts kept:

| `turnStrength` | 0.9, FREE / ATTACHED | 0.7, FREE / ATTACHED | 0.3, FREE / ATTACHED |
| --- | --- | --- | --- |
| 5 | 0.30 s / 0.35 s | 0.48 s / 0.58 s | lost / lost |
| **6** | 0.12 s / 0.17 s | 0.27 s / 0.32 s | 1 of 15 kept / 1.10 s |
| 7 | 0.05 s / 0.07 s | 0.10 s / 0.15 s | 3 of 15 kept / 0.72 s |
| 8 | 0.05 s / 0.05 s | 0.05 s / 0.07 s | 3 of 15 kept / 0.52 s |

At 7–8, 0.7 turns as tightly as 0.9, and the gradient flattens. At 5, ATTACHED at 0.3 loses the reversal too, but good control turns three times slower.

## The characterisation

`eleven-sim turns <dir> [name=value ...]` writes `turn-curves.csv`, and CI publishes it. `docs/simulation/turn-reference.csv` is the recorded technical reference, checked by `TurnCurvesTests`. It measures the cut too ([cut.md](cut.md#the-characterisation)).

**The cases:**
- 45°, 90°, 135° and 180° changes of direction from the ball at the feet, after 2 s at half stick or full;
- the stick snapped to the new direction, or swept there at a steady rate over 0.1 s (`sweep_s`), as a thumb sweeps a stick;
- under FREE and ATTACHED, with the turn and the cut off (`turnStrength` and `cutStrength` 0) and on;
- at ballControl 0.9 (good), 0.7 (bench, the bench's player) and 0.3 (poor);
- each case started at each of the 15 steps of a touch interval.

**The columns:**

| Column | What it is |
| --- | --- |
| `entry_mps` | the player's speed as the stick turns |
| `kept` | the starts with control throughout: within reach under FREE, attached under ATTACHED, and the ball ahead again within 3 s |
| `retained` | the starts in control at the end of the 3 s, whatever happened on the way: within reach and touched in the last touch interval under FREE, attached under ATTACHED |
| `forward_mean_s`, `forward_min_s`, `forward_max_s` | from the step the player first moves the new way until the ball is 0.3 m ahead along it; the min and max give the spread across phases |
| `trail_max_m`, `behind_max_s` | over that time, the furthest the ball is behind the player, and for how long |
| `kick_ready_max_s` | until the ball is ahead, within kick reach and low enough, under either model |
| `exit_mps` | the player's speed along the new direction when the ball is first ahead |
| `pass_beside_min_m` | the closest the ball passes the player's centre, across the new direction |
| `max_step_change_mps` | the largest change of ball velocity in one step, over every start |
| `out_max_s` | the longest spell without control, over every start; a ball never regained counts to the window's end |
| `separation_max_m` | the furthest the ball gets from the player's centre, over every start |

The columns from `forward_mean_s` to `pass_beside_min_m` are over the starts kept.

**180° at the references**, snapped, worst over the starts kept:

| 180° | Off: kept, ahead in, trails | On: kept, ahead in, trails |
| --- | --- | --- |
| FREE 0.9, half | 5/15, 0.57 s, 0.78 m | 15/15, 0.10 s, 0.14 m |
| FREE 0.9, sprint | 2/15, 0.68 s, 0.64 m | 15/15, 0.12 s, 0.17 m |
| FREE 0.7, half | 4/15, 0.58 s, 0.79 m | 15/15, 0.20 s, 0.25 m |
| FREE 0.7, sprint | 0/15 | 15/15, 0.27 s, 0.33 m |
| FREE 0.3, half | 1/15, 0.75 s, 0.83 m | 15/15, 0.63 s, 0.46 m |
| FREE 0.3, sprint | 0/15 | 1/15, 0.83 s, 0.62 m |
| ATTACHED 0.9, half | 15/15, 0.52 s, 0.59 m | 15/15, 0.12 s, 0.02 m |
| ATTACHED 0.9, sprint | 15/15, 0.78 s, 0.89 m | 15/15, 0.17 s, 0.10 m |
| ATTACHED 0.7, half | 15/15, 0.63 s, 0.68 m | 15/15, 0.15 s, 0.07 m |
| ATTACHED 0.7, sprint | 0/15 | 15/15, 0.32 s, 0.27 m |
| ATTACHED 0.3, half | 15/15, 1.03 s, 1.03 m | 15/15, 0.32 s, 0.23 m |
| ATTACHED 0.3, sprint | 0/15 | 15/15, 1.10 s, 0.92 m |

**What the references show:**
- **135° moves the same way.** At a sprint, 0.9 goes from 1/15 to 15/15 under FREE, and from 0.80 s to 0.05 s under ATTACHED.
- **Kick-ready comes with the ball ahead** in every row.
- **The ball passes the player beside them.** At 0.9 it passes at least 0.16 m from their centre. Off, a 180° reversal passes within 0.05 m, through them. Poorer control passes closer, because its trap only partly arrests the ball.
- **The largest change in one step** with the turn on is 5.85 m/s at 0.9 under FREE, against a cap of 5.7: the rest is the touch's error. Under ATTACHED, with no error, it is 5.71, the cap plus a step's rolling friction.
- **90° rows:** under ATTACHED they are the same off and on. Under FREE the cut acts ([cut.md](cut.md)).

## What stays open

- **A 90° cut isn't a turn.** Under FREE it was decided by the touch phase: at a sprint, 0.9 kept it from 3 of 15 starts and 0.7 from none. The cut (#75, [cut.md](cut.md)) now touches the ball as it starts, and a turn's touch out stands for a cut's touch, so the two never stack.
- **ATTACHED at 0.3 keeps a sprint reversal** at the reference, but as slowly as at half speed with the turn off: 1.10 s and 0.92 m.
  - Meanwhile it still loses a 90° sprint turn.
  - The cause is ATTACHED's 1.35 m loss reach: the player brakes onto a ball the trap only slowed.
  - FREE at 0.3 loses the sprint reversal from 14 of 15 starts.
  - At `turnStrength` 5, ATTACHED loses it too, at a cost to good control. Erik judges that with the knob.
- **Cross-runtime determinism.** It rests on ADR-0002's maths model, and the turn uses only the operations the touch already uses. The spike scenario has no turn at speed, so its WebGL digest gate doesn't exercise one. The WebGL smoke test does reverse with the ball, and the build's recording of that session replays to the live state.

## The properties tested

`TurnTests`, under both models where it applies:
- the trap comes on the turn's first step at every touch phase (FREE);
- the touch out comes once, on the first step the player moves the stick's way, and the player is turning in between;
- with the turn the ball trails less, and for less time, than without it;
- good control keeps a sprint reversal from every phase, and poor control from at most half (FREE);
- better control trails less;
- with no execution error, each turn touch is within its bound, and the ball moves only by its velocity;
- a good player's turn takes the ball past them at least 0.12 m from their centre;
- 45° and 90° changes of direction make no turn touch, with the same per-tick hashes as with the turn off (the cut, the same in both, is [cut.md](cut.md)'s);
- `turnStrength` 0 makes no turn touch;
- no turn touch through a kick's wind-up and cooldown, and a wind-up ends the turn;
- determinism, and snapshot and restore mid-turn;
- a turn ends when the player loses the ball, so a ball rolled at them afterwards is taken as if they had never turned;
- the turn flag round-trips through the encoding, and a bad one is refused.

`TurnCurvesTests` checks the reference and the relationships in it.

## For Erik on device

**The comparison to play.** Under each model, switched at the top of the sheet:
- carry the ball and reverse the stick, slowly at half stick and abruptly at full speed;
- then turn about 135°;
- compare with `turnStrength` 0 on the Control tab, the turn off.

**On the bench, try:**
- `turnStrength` 5, 6 and 8;
- `turnAngle`, to decide which turns count;
- `ballControl` 0.3 and 0.9 on the Player tab.

None of the defaults is chosen as feel.
