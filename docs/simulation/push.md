# Push-and-chase under FREE

Issue #63. **An experiment, not approved feel, and off by default.** It asks whether FREE can also play the way Kick Off 2 does: a deliberate push ahead, a ball that is loose for a moment, and a real chase to get it back. FREE's short touches ([dribble.md](dribble.md)) and ATTACHED ([attached.md](attached.md)) are unchanged.

## The rule

With `pushSpeed` above 0, **a sprint's ordinary touch is a push.** The ball leaves along the footballer's line of travel, `pushSpeed` faster than they will be running by their next touch. Nothing else is added:

- **The ball is free at once.** It rolls ahead, beyond the control reach, and slows only under its own rolling friction.
- **The catch is a chase.** The footballer runs at their own speed, never faster, and touches the ball again on the first step it is back within reach. No magnet, no snap and no speed boost brings it back.
- **A push can be lost.** A ball pushed too hard isn't caught for a long time, and a footballer who stops or turns away leaves it.

**What counts as a sprint:** the stick asks for at least `pushFromSpeed` of full, and the footballer will be running at least `pushFromSpeed` of their max speed by their next touch. The simulation's own movement predicts that (INV-13).

**What never pushes:** a turn's touches (#66), a cut's touch (#75), a jog, and a first touch on a ball they aren't sprinting with.

**The push is a touch,** so it is bounded and has an error like any other: the change of velocity is capped by `touchStrength` scaled by ball control, and its direction error grows as ball control falls. A poor footballer's push is weaker and less true.

**The kick is unchanged.** Its reach is the same, so a kick connects only if the ball is within kick reach when the wind-up ends: just after a push, or as the footballer catches it. In the middle of a chase it misses. `SimEventKind.Pushed` marks each push.

## The knobs

Both are on the bench's Control tab, in config format 13.

| Knob | Reference | Range | What it is |
| --- | --- | --- | --- |
| `pushSpeed` | 0 (off) | 0–5 m/s | How much faster than the footballer a push sends the ball. |
| `pushFromSpeed` | 0.9 | 0.5–1 of max speed | How fast, and how much stick, makes a touch a push. |

To try it: set `pushSpeed` on the Control tab and APPLY / RETRY. 0.5 and 1.0 are the two worth trying first.

## What the physics allows

How long a chase lasts follows from two numbers the experiment doesn't change: the ball's rolling deceleration (`rollingFriction` 0.08 of g, about 0.78 m/s²) and the footballer's top speed (7 m/s). A ball pushed `pushSpeed` faster than a footballer at full speed is back at the same distance after about 2 × `pushSpeed` ÷ 0.78 seconds, and gets `pushSpeed`² ÷ 1.57 metres further away in between.

So **at the reference friction a push that leaves reach is a long one.** That is the main finding: a quick push-and-catch rhythm, as in Kick Off 2, needs either a small push or a ball that slows faster.

## Measured

`eleven-sim push <dir>` writes `push-curves.csv`; [push-reference.csv](push-reference.csv) is the recorded technical reference. Each row is a footballer sprinting after the ball for 12 s, the stick full and straight at it, at one push speed and one ball-control profile (good 0.9, bench 0.7, poor 0.3). The bench profile:

| `pushSpeed` | Catch after | Run per push | Furthest from the footballer | Sprint flicks that connect |
| --- | --- | --- | --- | --- |
| 0 (FREE's short touches) | n/a | n/a | 0.92 m | 100 % |
| 0.5 m/s | 1.06 s | 7.3 m | 1.17 m | 67 % |
| 1.0 m/s | 2.48 s | 17.4 m | 1.64 m | 5 % |
| 1.5 m/s | 3.76 s | 26.3 m | 2.44 m | 3 % |
| 2.0 m/s | 5.03 s | 35.2 m | 3.54 m | 2 % |
| 4.0 m/s | 9.92 s: lost | 69.4 m | 10.55 m | 1 % |

- **A push is lost** when no touch follows it within 6 s.
- **Sprint flicks** are asked for at every step of one chase, the second where there is one, so at a full sprint.
- **The timing window.** At 1.0 m/s a flick connects only if asked for on the push's own step or from 2.43 s after it: 5 % of the chase. At 0.5 m/s the ball stays near the edge of reach, and 67 % connect.
- **Good and poor differ.** At 1.5 m/s a poor footballer's push is caught after 2.92 s, against 3.76 s for a good one: their touch reaches less of the push it asked for.

## What the tests pin

`PushTests` pins the rule's relationships: push off is FREE unchanged; a push runs beyond reach and is caught by running, with the footballer never above their max speed and the ball never sped up between touches; a harder push runs further and takes longer; a jog never pushes; a kick connects right after a push and misses mid-chase; an overpush is lost; a poor footballer's pushes drift more; and a run replays exactly. `PushCurvesTests` pins the reference CSV and the table's relationships.

## Not decided here

- Whether push-and-chase is worth having, and at what speed: that is Erik's, by play.
- Whether a quicker rhythm should come from a ball that slows faster. `rollingFriction` is on the Ball tab, but it changes every roll and pass, so it isn't changed here.
