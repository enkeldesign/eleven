# The goal: goal line, posts and crossbar

The goal (#68, part 1 of #30): its geometry, how its frame returns the ball, and when a ball is a goal. The keeper is #69. The goal's sizes are Law 1's. The frame's restitution is a technical reference for the test bench, not approved feel.

## The research starting answer

#30's research, from IFAB [Law 1](https://www.theifab.com/laws/latest/the-field-of-play/) and [Law 10](https://www.thefa.com/football-rules-governance/lawsandrules/laws/football-11-11/law-10---determining-the-outcome-of-the-match):
- **The mouth** is 7.32 m between the posts' inner faces, and 2.44 m from the ground to the crossbar's underside.
- **The posts and crossbar** share one width, at most 12 cm, which is also the goal line's width. They are white.
- **A goal** is scored when the whole ball crosses the goal line between the posts and under the crossbar.

The research gives the real size as the start. If top-down readability or touch aiming later makes another size feel better, Erik tunes it with evidence. So the sizes are bench knobs.

## Geometry

One goal, at the +x end, on the goal line, centred on y = 0. The far end gets its goal when two-sided play needs one.

| Value | Reference | What it is |
| --- | --- | --- |
| `goalLineX` | 52.5 m | x of the goal line's outer edge. Not a bench knob |
| `goalWidth` | 7.32 m | Between the posts' inner faces |
| `goalHeight` | 2.44 m | From the ground to the crossbar's underside |
| `postWidth` | 0.12 m | The posts' and crossbar's width |

The posts and crossbar are round. Their back faces stand on the line's outer edge, so:
- the posts' axes are at x = `goalLineX` − `postWidth`/2 = 52.44 m, y = ±(`goalWidth` + `postWidth`)/2 = ±3.72 m, from the ground up;
- the crossbar's axis joins the posts' tops, at z = `goalHeight` + `postWidth`/2 = 2.50 m.

`SimConfig.FrameAxisX`, `PostAxisY` and `CrossbarAxisZ` give them.

## The frame

**The shape.** The frame is three capsules, two posts and the crossbar. Each is the set of points within `ballRadius` + `postWidth`/2 of its axis, for the ball's centre. The crossbar's ends are the posts' tops, so the corners are round.

**Each step,** after the ball integrates, the frame resolves along the step's path. The path is the straight segment the integration moved the ball's centre along, from where it started the step to where it ends.
1. **A ball touching the frame at the step's start and moving into it** is turned at once. Then it moves the whole step along its new velocity, from where it started. Touching means within 1 nm of a capsule.
2. **The first contact along the path** stops the ball there, at the contact point, and turns it. The rest of that step's motion is lost: at most 1/60 s.

A ball's path stays one straight segment, with at most two contacts in a step (a corner). Without step 1, a ball pressed onto the crossbar by gravity would stop at its contact on every step and freeze there.

**A turn** reverses the velocity's component along the contact normal, scaled by `frameRestitution`. The tangential component and the spin are kept. So:
- a contact never adds speed;
- a ball struck straight at a post comes back along its line at `frameRestitution` times its speed;
- the normal is the direction from the axis to the contact point. It is never shorter than `ballRadius` + `postWidth`/2, which is above 0, so it is always finite.

**Players** don't collide with the frame. They are still dots. A keeper's envelope is swept along the same step's path as the frame, and the earliest contact wins ([keeper.md](keeper.md)).

**The quick pass's weighting** rolls a pass on open grass, without the frame. A pass is never weighted for a post.

### Energy

On a step the ball hits the frame, the energy check in `GoalPropertyTests` allows the integrator's known error, as [ball.md](ball.md#the-energy-allowance) derives it, plus what a contact can add:

```
ΔE ≤ dt² · |m|²  +  g · dt · (|vz| + (|m| + g) · dt)  [ twice on a step that also bounces ]
                 +  2 · g · dt · (|v| + (|m| + g) · dt)  +  1e-9 · (1 + E)
```

Each term has a source:
- **Stopping at the contact.** The step's gravity was applied to the velocity for the whole step, but the ball only moved part of it. So it can end up to `|vz| · dt` higher than the step's full fall would put it, as at a bounce.
- **Moving the whole step along a turned velocity.** For a ball that starts touching, the step's gravity acted along the old velocity, and the turn changes the vertical speed by at most twice the ball's speed.

Over the fuzz, energy rose on 2 of the 110 steps with a contact, by at most 12 % of the allowance.

## The goal line

**"Crosses the line"** means the whole ball is beyond the line's outer edge: x − `ballRadius` > `goalLineX`.

**"Inside the mouth"** is judged where the ball's centre crossed x = `goalLineX` on the step's path: between the posts' axes, |y| < `PostAxisY`, and below the crossbar's, z < `CrossbarAxisZ`. On that plane, a ball not touching the frame is always at least √(r² + r · `postWidth`) from both thresholds: 0.16 m at the references. So the judgement is never ambiguous. The ball is inside exactly when it passed between the posts and under the crossbar.

**`BallState.Line`** keeps where the ball stood after the previous step (state schema 8):

| State | Meaning |
| --- | --- |
| `InPlay` | The ball's centre is in front of the line |
| `CrossingMouth` | Its centre crossed inside the mouth; part of the ball is still on the line |
| `CrossingWide` | Its centre crossed outside the mouth or over the bar; part of the ball is still on the line |
| `Goal` | The whole ball is beyond, having crossed inside the mouth |
| `Out` | The whole ball is beyond, having crossed outside it |

**Events.**
- `GoalLineCrossed` fires on the step the whole ball crosses, whether it is a goal or not.
- `Goal` fires on the same step when it crossed inside the mouth.
- A ball that stays beyond emits nothing more.
- `HitFrame` fires on each step the ball meets the frame, with the part in `Other`: 1 the right post (−y), 2 the left (+y), 3 the crossbar, as the attacker sees them facing the goal.

**Back in play.** The state returns to `InPlay` only when the ball's centre is back in front of the line. Then a new crossing can score again. So a scored ball that comes half back over the line can't score twice, and nor can jitter on the line.

**A placed state.** A ball whose centre started the step in front of the line was in play, whatever its state says. A ball placed over the line in play is judged where it was placed.

## Ordering within a step

The step's order is in [determinism.md](determinism.md#ticks): … ball control → **integrate the ball, then resolve the frame along the step's path → judge the goal line** → advance the tick.

So the goal line always sees the ball after its contacts:
- **A ball that hits a post and comes back out** never scores. The contact stops it in front of the line on the step it hits, even when, unobstructed, that step would have taken it wholly over.
- **A ball that goes in off a post** scores on the step its whole ball is over.

No step both scores and rebounds ambiguously.

After a goal the ball rolls on, since there is no net, and RESET starts again. A net, and the restart after a goal (INV-8's sequencer), are later work.

## Tunables

The **Goal** tab of the test bench ([test-bench.md](test-bench.md)):

| Knob | Valid | Starting range | Reference |
| --- | --- | --- | --- |
| `goalWidth` | 3–12 m | none: Law 1's 7.32 m is the start | 7.32 m |
| `goalHeight` | 1.5–4 m | none: Law 1's 2.44 m is the start | 2.44 m |
| `postWidth` | 0.05–0.3 m | 0.10–0.12 m, real posts | 0.12 m |
| `frameRestitution` | 0–1 | 0.55–0.85 | 0.70 |

**Why the restitution's range.** FIFA Quality Pro's rebound test drops a ball from 2 m onto steel and needs 135–155 cm back. That is e ≈ 0.82–0.88 at about 6 m/s, and e falls at shot speeds, as the ball deforms more. The range spans both.

The pitch draws the posts and crossbar from these values, 2.5 times their width so 12 cm reads on a phone. A goal size changed on the bench is drawn on APPLY / RETRY.

## Edge cases, measured

`eleven-sim goal <dir> [name=value ...]` writes `goal-edges.csv`. CI publishes it as the `goal-edges` artifact, and the reference is [goal-edges-reference.csv](goal-edges-reference.csv).

**The shots:**
- ground shots rolled straight along +x at the left post, at 8 and 25 m/s;
- flat 20 m/s shots at the crossbar, at y = 0;
- each from 0.30 m inside the frame's inner face to 0.50 m outside it, in 5 cm steps.

**The columns:** each shot's outcome (goal, post, bar, wide or over), the part it hit first, the speed it came back at, and how far the contact turned it.

**At the references,** on both edges:

| Offset from the inner face | Ground, 8 and 25 m/s | High, 20 m/s |
| --- | --- | --- |
| −0.30 to −0.15 m | goal, clean | goal, clean |
| −0.10 m | in off the post, turned 34° | in off the bar, turned 21° |
| −0.05 m | off the post and out, turned 89° | in off the bar, turned 84° |
| 0 to +0.20 m | off the post | off the bar |
| +0.25 to +0.50 m | wide, clean | over, clean |

Straight at the axis (+0.05 m), a shot comes back turned 172° off a post and 167° off the bar, at about 0.7 times its speed. A shot grazing the post's outside face keeps more of its speed than one straight at it, since the frame takes off only the normal part.

At −0.05 m the two edges part ways. The ground shot is turned almost square across the mouth and doesn't cross the line within the 3 s measured. The high shot comes down off the bar, and gravity carries it on under the bar and over the line. The high shots also lose a few millimetres to gravity before they reach the line.

## Properties

`GoalPropertyTests` runs 300 seeded random shots for 240 steps each (4 s at 60 Hz).

**What the fuzz varies:**
- the goal's four values, each across its whole valid range, and the ball's radius from 0.08 to 0.2 m;
- the shots: from 0.5 to 12 m out, on the ground or up to 3.5 m high, at 3–45 m/s, with spin up to ±40 rad/s;
- a third of the shots aimed at a post's or the bar's axis, the rest anywhere around the mouth.

**Properties checked:**
- the ball never passes into the frame: at the end of every step, its centre is at least `ballRadius` + `postWidth`/2 from every axis;
- a contact adds no energy beyond the allowance above;
- finite shots stay finite;
- a goal is scored exactly when the whole ball crosses having crossed with its centre inside the mouth, read off each step's straight path;
- every shot is deterministic.

The fuzz hits both posts and the bar at least 20 times each, and scores and misses at least 40 times each. Each property has a test that feeds its check a broken trajectory and shows the check catches it.

`GoalTests` covers the rules one by one:
- a ball through the middle scores once, as its whole ball crosses;
- just inside a post scores and just outside it doesn't;
- a fast ball at a post or the bar comes back off it by the restitution;
- a ball off the inside of a post goes in;
- just under the bar scores and just over it doesn't;
- a scored ball half back over the line can't score again, and one back in play can;
- a ball placed over the line is judged where it is;
- a shot by the controlled player scores, runs the same twice, and replays from a snapshot taken in flight.
