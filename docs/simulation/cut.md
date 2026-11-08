# The cut with the ball

How a footballer cuts with the ball under FREE (#75): a change of direction short of a turn, at any speed. It is a research-backed starting hypothesis, not approved feel. Its values are technical references for the test bench, and the FREE-or-ATTACHED choice (#29) stays open. The turn, beyond `turnAngle`, is [turn.md](turn.md). FREE's touches are [dribble.md](dribble.md).

## What the cut fixes

**Under FREE, a cut used to be decided by the touch phase,** which nobody can see. Found during #66 (PR #73):
- **The cause.**
  - At a sprint, the dribble lead (0.85 m) is 5 cm inside the control reach (0.9 m).
  - Any change of direction of more than about 16° takes the ball out of reach within about 3 steps, unless a touch is due.
  - Touches came only on the 15-step cadence, so where the cut fell in it decided the outcome.
- **It wasn't skill.** With the touch's execution error off, a 90° sprint cut was kept from 4, 2 and 0 of the 15 phases at `ballControl` 0.9, 0.7 and 0.3.
- **The dribble characterisation didn't show it.** Its turn cases turn on tick 120, a multiple of the cadence: always on a touch step, the best case.

**The research starting answer.** SOL's hypothesis on #75 draws on classic loose-ball football, which rewards direction-change timing and ball separation:
- Meaningful difficulty must be readable.
- A real inside or outside-foot cut redirects the ball as the player plants, so the touch belongs to the cut, not to a stride clock. The metaphor is for the interaction, not an animation requirement.
- The player's stick stays the authoritative intent, and the footballer executes the touch.
- Better control should still do better. Poor control, or an overloaded cut at a sprint, can lose the ball.

## The rule

1. **A cut is asked** when the stick asks for a direction more than `cutAngle` from where the player is moving, faster than 0.5 m/s (the trap speed).
   - It goes up to `turnAngle`. Beyond that it is a turn, whether or not the turn is on.
   - It applies under FREE only.
2. **The cut touch.**
   - **When:** on the first step of the cut on which the player can play the ball, whatever the touch cooldown.
   - **Like any touch** ([dribble.md](dribble.md#a-touch)):
     - **the target:** the lead ahead of where the player will be by their next touch, under this step's stick;
     - **the caps:** cushioning capped by `receiveStrength`;
     - **the error:** the execution error, `touchError` · (1 − ballControl) · (1 + turn / 90° + overload);
     - **eligibility:** there is no cut touch through a wind-up or a kick cooldown, or on the step a kick connects.
   - **Its own cap:** its redirect is capped by `cutStrength` × (0.5 + 0.5 · ballControl), in place of `touchStrength`.
3. **The follow-up.**
   - The cut touch sets the touch cooldown to `cutInterval`, or to the touch interval if that is shorter.
   - So the next touch, an ordinary one, plays the ball the way the stick asks by then. A thumb sweeps a stick round over a few steps, and the follow-up catches where it ends up.
   - Then the cadence resumes.
4. **One cut a stride.**
   - `PlayerState.CutCooldown` is set to the touch interval by a cut touch, and counts down every step. No cut touch comes while it runs.
   - A turn's touch out sets it too: it stands for a cut's touch while the player comes round. So a turn never gets a cut touch on top of its own two.

Every touch stays one bounded change of the ball's velocity. The ball moves only by its velocity: no parenting, no snap, no change of position.

## Bounds and failure

- **Failure is real, and it belongs to the footballer.** What a cut loses is the touch's execution error, which ball control scales. With the error off, every 45° and 90° cut is kept from every phase, at every control.
- **The rate of touching is bounded.**
  - Apart from the turn's two touches, two touches are never closer than the follow-up interval.
  - A cut is never closer than a stride to the last one.
  - A zig-zag that flips every 4 steps is touched 8 times a second, not every flip.
- **Eligibility, and what wins:** the touch's own. A manual kick, a quick pass, its wind-up and its aftertouch are untouched.
- **ATTACHED doesn't cut.** Its correction acts every step, so it never had a phase to fix.

## State, config and events

- **`PlayerState.CutCooldown`** is an int32 after the turn flag. State schema 11. Restoring refuses one outside 0 to the touch interval.
- **Config format 12** adds `cutAngle`, `cutStrength` and `cutInterval`, on the bench's Control tab.
- **`SimEventKind.Cut`** (17) names the player. It comes with `Touched`. A later player model can animate it.

## Tunables

| Knob | Unit | Valid | Reference |
| --- | --- | --- | --- |
| `cutAngle` | rad | π/12–π/2 | 0.35 (20°) |
| `cutStrength` | m/s | 0–20 | 5, the touch's own |
| `cutInterval` | s | 0.02–1 | 0.07 (4 steps at 60 Hz) |

- **`cutStrength` 0 turns the cut off.** Every change of direction short of a turn then waits for the cadence, as before #75. That is the baseline, for comparing with and without the cut.
- **Why `cutAngle` 0.35 rad.** It catches a snapped 30° change, and a 45° change swept over 0.1 s.
  - The angle is measured after the step's movement, so a snap counts once it is a few degrees over the cut angle.
  - At 0.5 rad, swept 45° cuts at a sprint are back on the lottery.
- **Why `cutStrength` 5.** From 3 to 6 it barely moves 0.9 and 0.7, and lifts 0.3 a little. The cut changes when the footballer touches the ball, not how hard, so the reference is the touch's own strength.
- **Why `cutInterval` 0.07 s.**
  - At 0.05 s, the follow-up comes before a 0.1 s sweep has settled, and good control loses a swept 90° sprint cut for a moment from every phase.
  - At 0.1 s it is as good.
  - At 0.25 s, the cadence with no follow-up, swept cuts fail again.

**The sweep at a sprint.** Kept / retained, of 15 phases, with the other two knobs at their references:

| Knob | 90° snapped, 0.7 | 90° swept, 0.7 | 90° snapped, 0.3 | 90° swept, 0.3 |
| --- | --- | --- | --- | --- |
| `cutStrength` 3 | 14/15 | 14/14 | 1/5 | 1/6 |
| **`cutStrength` 5** | **15/15** | **15/15** | **4/9** | **3/8** |
| `cutStrength` 6 | 15/15 | 15/15 | 5/10 | 4/10 |
| `cutInterval` 0.05 | 15/15 | 9/15 | 2/7 | 1/6 |
| `cutInterval` 0.1 | 15/15 | 15/15 | 3/9 | 2/5 |
| `cutInterval` 0.25 | 5/6 | 0/0 | 0/2 | 0/0 |

## The characterisation

`eleven-sim turns` measures the cut with the turn ([turn.md](turn.md#the-characterisation)):
- 45°, 90°, 135° and 180°;
- the stick snapped, or swept at a steady rate over 0.1 s;
- every case from each of the 15 touch phases;
- "off" means the turn and the cut both off.

`retained` counts the starts in control at the end of the 3 s window, as well as those kept throughout. `out_max_s` and `separation_max_m` are over every start.

**FREE, the turn and cut on.** Kept / retained, of 15 phases, before (main) → after:

| Cut | 0.9 | 0.7 | 0.3 | ATTACHED 0.7 |
| --- | --- | --- | --- | --- |
| 45°, sprint | 6/7 → **15/15** | 3/4 → **15/15** | 0/0 → 4/11 | 15/15 |
| 45°, sprint, swept | 2/4 → **15/15** | 0/0 → **15/15** | 0/0 → 0/8 | 15/15 |
| 90°, sprint | 3/3 → **15/15** | 0/0 → **15/15** | 0/0 → 4/9 | 15/15 |
| 90°, sprint, swept | 0/0 → **15/15** | 0/0 → **15/15** | 0/0 → 3/8 | 0/0 |
| 45°, half stick | 12/13 → **15/15** | 12/12 → **15/15** | 10/10 → **15/15** | 15/15 |
| 90°, half stick | 8/8 → **15/15** | 6/6 → **15/15** | 1/1 → **15/15** | 15/15 |
| 135°, sprint, swept | 5/15 → **15/15** | 5/14 → **15/15** | 1/2 → 4/10 | 15/15 |

**What the references show:**
- **The phase no longer decides a cut.** With the touch error off, every 45° and 90° cut is kept from every phase at every control.
- **The gradients hold.**
  - **By control:** at a 90° sprint cut, 0.3 retains 9 of 15 and 0.9 retains 15.
  - **By angle:**
    - At a sprint, 0.3 retains 11 at 45° and 9 at 90°.
    - At half stick, 45° is kick-ready at once. At 90°, 0.3 takes up to 0.40 s and 0.9 up to 0.10 s.
  - **By speed:** half stick is easier than a sprint.
- **#66's turn is preserved.**
  - Snapped 135° and 180° rows are unchanged.
  - Swept reversals mostly improve, because the stick passes through the cut range first.
- **ATTACHED and every "off" row are unchanged.** The spike scenario has no cut: its digest is the same with the cut off.

## What stays open

- **A gentle bend isn't a cut.**
  - A change of direction the player follows within the cut angle stays on the cadence.
  - At half stick, a 45° bend swept over 0.1 s still loses the ball from 2 of 15 phases at every control.
  - At a sprint, swings slower than about 0.2 s are the same.
  - The root is FREE's sprint lead, 5 cm inside the control reach: a dribbling margin, not a cut. It is for its own issue.
- **ATTACHED at 0.7 loses a swept 90° sprint cut,** from every phase, through its 1.35 m loss reach. Unchanged, and outside #75.
- **A swept 180° reversal at half stick, at 0.3,** goes from 14 to 12 of 15 retained: the cut touch on the way round sends the ball wide of the turn's trap. The other swept reversals improve.
- **A zig-zag every 4 steps,** of ±30°, keeps the ball at 0.9 at 8 touches a second. 0.7 loses it. A real cut-a-stride dribble, a zig-zag every 0.25 s, keeps it from 14 or 15 of 15 phases at 0.9 and 0.7.
- **Feel for Erik.** At the reference, 0.3 keeps every half-stick cut and struggles only at a sprint. `cutStrength` and `touchError` are the knobs.
- **Cross-runtime determinism.**
  - The cut uses only the operations the touch already uses: ADR-0002's maths model.
  - The WebGL smoke test cuts with the ball after its reversal, and the build's recording of that session replays to the live state under .NET.

## The properties tested

`CutTests`:
- the cut touch comes on the cut's first step at every touch phase, at 45° and 90°, half stick and full;
- the follow-up comes a cut interval later, as an ordinary touch, or with the cadence when that is sooner;
- at most one cut a stride, in a zig-zag that asks for a cut at every flip;
- a turn never gets a cut touch, and its trajectory is the same with the cut on and off; its touch out sets the cut cooldown;
- 0.9 and 0.7 keep 45° and 90° sprint cuts from every phase, and 0.3 from at most half;
- with no execution error, every phase keeps every cut, snapped or swept, at every control;
- each cut touch is within its bound, and the ball moves only by its velocity;
- a change short of the cut angle is ordinary, with the same hashes as with the cut off;
- `cutStrength` 0 makes no cut, and the other cut knobs then change nothing;
- ATTACHED never cuts, with the same hashes as with the cut off;
- no cut touch through a kick's wind-up and cooldown;
- determinism, and snapshot and restore mid-cut;
- the cut cooldown round-trips, and one out of range is refused.

`TurnCurvesTests`, across every phase:
- 0.9 and 0.7 keep every 45° and 90° cut;
- poorer control retains fewer sprint cuts, and 0.3 keeps at most half;
- 45° is no harder than 90°, and half stick no harder than a sprint;
- snapped turns and ATTACHED don't change.

#59's dribble gradient tests turn on a touch step, one phase. They pin the cadence's own gradient with the cut off.

## For Erik on device

**The comparison to play.** Under FREE, with `cutStrength` 0 on the Control tab and then 5:
- carry the ball at half stick, and cut about 45° and 90°;
- then the same at a sprint;
- then zig-zag.

**On the bench, try:**
- `cutInterval` 0.05, 0.07 and 0.1;
- `cutAngle` 0.3 to 0.6;
- `ballControl` 0.3 and 0.9 on the Player tab.

None of the defaults is chosen as feel.
