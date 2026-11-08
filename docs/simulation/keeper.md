# The basic keeper

The keeper (#69, part 2 of #30): how he perceives, decides, moves and saves. He is an autonomous practice opponent, with no outfield opponents or team AI. His values are technical references for the test bench, not approved feel.

## The research starting answer

#30's research:
- **Precedent.** Kick Off 2's keeper is computer controlled: the game handles his positioning and saves rather than asking the player to micromanage him.
- **Real keepers supply scale, not a mandate.**
  - Elite goalkeeper-specific visual reaction is about 0.21 s.
  - A full penalty-style movement to the side takes about 0.9 s, reaction included.
  - Horizontal push-off velocity in a dive is about 2.7 m/s.
- **The starting envelope for the bench:**
  - reaction 0.15–0.30 s;
  - a committed dive of 0.45–0.90 s, depending on the reach needed;
  - dive speed 2.5–5.0 m/s, deliberately above the measured biomechanics so retro responsiveness can be tested.
- **Four explicit phases:** POSITION, REACT, COMMIT/DIVE and RECOVER. There is no teleporting, no hidden catch radius that follows the ball, and no reading the ball's future.

## Architecture: perception → brain → commands

This follows the brief's architecture: the simulation is observed, perception gives the AI an Observation, and the AI's decisions enter as commands owned by an AI controller.

- **His body.** The keeper is a player on the other team, owned by an AI controller. `SimState.Keepers` adds his `KeeperState`: his phase, its timer, his dive and his `KeeperCapabilities` (state schema 9).
- **Perception (INV-9).** `Perception.OfKeeper` builds his `KeeperObservation`:
  - his own body, phase and capabilities, and his top speed on his feet;
  - the ball as seen now: position, velocity, spin, and whether it is held;
  - the tick.

  It holds no command, no other controller's input, no wind-up intent, no seed and no reference to state.
- **The brain.** `Eleven.Simulation.AI.KeeperBrain.Decide(observation, config)` is pure and stateless, and returns a `KeeperDecision`: a move, and a dive target if he dives.
- **The boundary is a compile error.** ELEVEN003, in the banned-API analyzer, refuses any code in `Eleven.Simulation.AI` that names `SimState`, `Simulator`, `PlayerState`, `BallState`, `KeeperState` or `Snapshot`, or any of their members.
- **Prediction (INV-13).** `BallFlight` is the simulation's own ball step, its integration and the goal's frame ([goal.md](goal.md)). The step moves a ball no keeper meets with it, and the brain predicts with it. `KeeperBrainTests` shows the two agree bit for bit on a spinning, lofted shot that meets the post.
- **The driver.** `KeeperControllers.Append` runs each keeper's brain before a step and appends his command, owned by his controller, beside the human's. The host records his commands with the player's (recording format 4 adds `Command.Dive`). So replays, the recording check and feel captures never run a brain.

## Phases (INV-12)

The phases are authoritative state, timed by the step after kicks; the brain can ask but never skip one.

| Phase | Starts | Lasts | His body |
| --- | --- | --- | --- |
| `Position` | after REACT or RECOVER | until a kick or a dive | moves where his brain steers him, with his own movement capabilities |
| `React` | on the step any kick connects, from POSITION | `reactionTime`, in whole steps | sets: brakes to a stop, whatever is asked |
| `Dive` | a dive command, from POSITION | the steps the distance needs at `diveSpeed`, at most `keeperDiveSeconds` | flies at the dive's velocity |
| `Recover` | the end of a dive | `keeperRecoverSeconds` | still, on the ground |

**Within a step**, after kicks and ball control, each keeper in turn:
1. counts down a phase set on an earlier step, and ends it when it reaches zero;
2. starts REACT if a kick connected on this step and he is on his feet;
3. takes a dive his command asks for, if he is on his feet and isn't holding the ball.

So a dive starts no sooner than his reaction after the kick, and one asked on the kick's own step is refused. His body moves by his phase in the next step's movement.

**He reacts to strikes.** Other changes in the ball's path, such as off the frame or off his own parry, he follows as he sees them.

## Body and reach

**His envelope** is a capsule round a vertical axis at his position. Its radius is his `reach`, plus the ball's radius for the ball's centre. Its top depends on his phase:
- 2.0 m on his feet, in POSITION and REACT;
- the dive's height when diving, between 0.3 and 2.0 m;
- 0.3 m on the ground, in RECOVER.

These heights are technical constants of the body. His reach extends the envelope beyond both.

**It is bounded.** It moves only with his body, at his bounded speeds; nothing grows toward the ball. His furthest lateral reach from a dive is `diveSpeed` × `keeperDiveSeconds` + `reach`:
- the reference keeper's is 2.9 m;
- the slow keeper's is 2.1 m.

A well-struck corner beats them, and a ball beyond his reach passes untouched: `KeeperTests` runs one 2 cm past it, bit for bit as with no keeper.

**Contact.** Each step, the ball's path is swept against his envelope as against the goal's frame, and the earliest contact wins. Keepers take no FREE touches and no ATTACHED takes: they play the ball with their hands.

## The save: held or parried

**Held.** Relative to him, he holds a ball no faster than:

```
keeperHoldSpeed × (0.5 + 0.5 · handling) × (1 − stretch)
```

Stretch is how far out toward his reach the ball's line passes his body: 0 at the body, 1 at the fingertips. At the references, a shot up to 18.7 m/s is held at the body, half that at half stretch, and nothing at the fingertips.

A held ball stays where he caught it and moves with him (`BallState.HeldBy`). No kick or touch reaches it. Distribution is out of scope, so in practice RESET starts again.

**Parried.** Anything faster he parries:
- the velocity's component along the contact normal, relative to him, reverses, scaled by `keeperParryRestitution`;
- its direction then turns by a seeded error within ±`keeperParryError` × (1 − `handling`), drawn from the counter-based `RandomStream.Keeper` (INV-6).

A parried ball can still go in, and the goal line judges it.

**No random save chance.** The outcome comes from the shot's speed, where he meets it and his handling. Only a parry's direction varies, and it is seeded.

## The brain

On each step he isn't diving, on the ground or holding the ball, the brain finds where the ball's own flight first reaches his x, and when, up to 3 s ahead:

| The shot | He |
| --- | --- |
| crosses more than 0.5 m outside the posts or over the bar | lets it go, and keeps his position |
| crosses within half his reach | squares up to it |
| can be reached on his feet in time, shuffling at 0.4 of his top speed | shuffles across |
| can't | dives for the crossing point, once its arrival is within the dive's time; until then he shuffles toward it |
| no shot | stands on the line from the goal's centre toward the ball, 1.5 m off the goal line, inside the posts, easing in within a metre |

These constants are the basic keeper's technical ones, in the brain. A brain may ask for a dive at any time; the simulation's phases decide whether he takes it.

## Capabilities and values

**`KeeperCapabilities`**, the keeper's alone. On his feet he moves with his player's movement capabilities, the reference player's in practice.

| Capability | Valid | Starting range | Reference | Slow |
| --- | --- | --- | --- | --- |
| `reactionTime` | 0.05–0.6 s | 0.15–0.30 s | 0.20 s | 0.30 s |
| `diveSpeed` | 1–8 m/s | 2.5–5.0 m/s | 3.5 m/s | 2.5 m/s |
| `reach` | 0.3–1.5 m | none | 0.8 m | 0.6 m |
| `handling` | 0–1 | none | 0.7 | 0.4 |

**Config** (format 10):

| Value | Valid | Starting range | Reference |
| --- | --- | --- | --- |
| `keeperDiveSeconds` | 0.1–1.5 s | 0.45–0.90 s | 0.6 s |
| `keeperRecoverSeconds` | 0.1–3 s | none | 0.8 s |
| `keeperHoldSpeed` | 0–50 m/s | none | 22 m/s |
| `keeperParryRestitution` | 0–1 | none | 0.4 |
| `keeperParryError` | 0–1 rad | none | 0.3 rad |

All nine are on the bench's **Keeper** tab ([test-bench.md](test-bench.md)). The capabilities change the practice keeper, never the config.

## Practice

The practice start is the spike's with a keeper in goal:
- player 2, on the other team, owned by AI controller 2;
- 1.5 m off the line, facing the play;
- with the bench's keeper capabilities.

The host runs his brain each step and draws him as a sky-blue disc the player's size: `Domain.Keeper`, 3.8:1 against the pitch and 4.7:1 against its mown shade. The trace line ends `keeper=x,y,phase`, and the smoke test checks he stands in goal on his feet.

The spike scenario keeps no keeper, so its WebGL-equals-.NET digest gate is unchanged.

## Measured

`eleven-sim keeper <dir> [name=value ...]` writes `keeper-shots.csv`. CI publishes it as the `keeper-shots` artifact, and the reference is [keeper-shots-reference.csv](keeper-shots-reference.csv).

**The shots** are struck by a shooter with no kick error, at 12, 15 and 18 m/s, from the penalty spot and from 16 m:
- at the centre, y = 0.4 m;
- at the low corner, y = 3.2 m;
- at the high corner, y = 3.0 m, lofted to cross the line at 2.0 m;
- lofted, at loft 0.55.

**Each row records:**
- the outcome: held, parried (and kept out), parried in, goal or miss;
- his reaction;
- when he dived and when he met the ball, from the kick;
- the furthest he went across;
- his phase at contact.

**At the references,** with the time from the kick to his contact:

The reference keeper:

| Shot | 12 m/s | 15 m/s | 18 m/s |
| --- | --- | --- | --- |
| 11 m, centre | held 0.73 s | held 0.57 s | held 0.47 s |
| 11 m, low corner | parried 0.78 s | **goal** | **goal** |
| 11 m, high corner | parried 1.02 s | parried 0.68 s | **goal** |
| 11 m, lofted | held 0.85 s | miss | miss |
| 16 m, centre | held 1.17 s | held 0.92 s | held 0.77 s |
| 16 m, low corner | held 1.20 s | parried 0.95 s | parried 0.80 s |
| 16 m, high corner | held 2.37 s | parried 1.10 s | parried 0.83 s |
| 16 m, lofted | held 1.35 s | parried 1.15 s | miss |

The slow keeper:

| Shot | 12 m/s | 15 m/s | 18 m/s |
| --- | --- | --- | --- |
| 11 m, centre | held 0.75 s | held 0.58 s | parried 0.48 s |
| 11 m, low corner | **goal** | **goal** | **goal** |
| 11 m, high corner | **goal** | **goal** | **goal** |
| 11 m, lofted | held 0.87 s | miss | miss |
| 16 m, centre | held 1.18 s | held 0.93 s | parried 0.77 s |
| 16 m, low corner | parried 1.23 s | **goal** | **goal** |
| 16 m, high corner | held 2.40 s | **goal** | **goal** |
| 16 m, lofted | held 1.37 s | miss | miss |

**What the table shows:**
- **Reaction to contact.** The reference keeper reacts in 0.20 s and the slow one in 0.30 s. Contact comes 0.47–0.92 s after a penalty, inside the research's 0.9 s full movement.
- **Reach.** A penalty to the low corner at 15 m/s or more beats even the reference keeper: he dives as soon as his reaction lets him, and runs out of dive 2.1 m across. From 16 m he has time, and gets there.
- **The two profiles differ.** The reference keeper saves 17 of 24; the slow one saves 11.
- **The furthest across** either went was 2.7 m, on his feet before a 12 m/s high corner from 16 m.

No playtest yet suggests a deliberate departure from the real scale: that is Erik's to find on the bench.

## Properties and tests

`KeeperTests`:
- a kick sets him reacting for his reaction time, and a dive he asks for from the kick's own step comes when it ends;
- he sets while reacting, whatever his brain asks;
- a dive carries him at his dive speed for the time it needs, at most the longest dive, then he recovers, still, then stands;
- a ball beyond his reach passes untouched;
- a shot at his body is held and moves with him;
- a fast one at his fingertips is parried away;
- his handling decides between holding and parrying the same shot;
- a parry's direction error is seeded, bounded and reproducible;
- he plays with his hands, never a touch;
- a ball he holds can't be kicked;
- snapshots in REACT, DIVE and RECOVER each replay the run.

`KeeperBrainTests`:
- his observation carries no command and no state;
- on his feet he steps onto the line toward the ball;
- he dives for where a corner shot will cross;
- no dive for a shot well wide or straight at him;
- he predicts with the simulation's own ball;
- the reference keeper saves a central penalty, and a slow one dives and is beaten in the corner;
- a save runs the same twice, and its recording replays without the brain.

`KeeperShotsTests`:
- the reference matches;
- he never dives before his reaction, nor goes further across than his shuffle and dive allow;
- the reference keeper saves every central shot, and more than the slow one.

`BannedApiAnalyzerTests` shows ELEVEN003 catches AI code naming the simulation's state, and allows the same names elsewhere.

## Out of scope

As #30:
- outfield opponents and team AI;
- distribution, sweeping, set pieces and tactics;
- the final keeper attribute taxonomy;
- animation;
- final feel values.

A held ball stays held until RESET.
