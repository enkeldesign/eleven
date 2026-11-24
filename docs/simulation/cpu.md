# The CPU outfielder

Issue #83, the second step of the 1v1 match: one CPU footballer who plays readable football through the same commands and the same simulation as a human's footballer.

Every number here is an untuned technical reference. The brain is a basic one: it plays, and gives the later steps (the challenge, restarts, the match) someone to play against.

## Architecture: perception → brain → plan → commands

1. **Perception** (`Perception.OfOutfielder`) turns the state into an `OutfieldObservation`: all the brain ever sees (INV-9).
2. **The brain** (`OutfieldBrain.Decide`, in `Eleven.Simulation.AI`) is pure and stateless. It takes the observation and the config and returns a `CpuPlan`.
3. **The plan** goes to the simulation inside his controller's command. The simulation keeps it in `CpuState` and times it (INV-12): when he may decide, and when a plan takes effect.
4. **His controller** (`CpuControllers.Append`) sends, each tick, the stick of the plan in effect, and that plan's kick or quick pass on the tick it takes effect. These are ordinary owned commands, recorded like a human's (INV-10), so a replay runs no brain.

A plan moves no one. Only the stick, the kick and the quick pass do, and they are the ones a human's adapter sends.

## What he sees

| In the observation | Notes |
| --- | --- |
| His own body: position, velocity, heading, top speed, whether he is kicking, his reaction delay | Exact. The reaction delay since #94, so he can time a ball in the air. |
| The ball: position, velocity, spin, and whether a keeper holds it (his side's or theirs) | The position is where he places it. |
| The other footballers within his sight: position, velocity, teammate or not, keeper or not | Positions are where he places them. Those beyond his `visionRange` are not there at all. |
| Whether there is a goal at each end | So his reading of the ball's path includes both frames. |

- **Not in it:** the state, any command, anyone's plan, a wind-up's intent, the seed. `OutfieldObservation` has no public constructor, and the analyzer keeps the state out of the brain's namespace (ELEVEN003).
- **His own side's frame.** Everything is as seen attacking +x. For a side attacking −x that is the world turned half a turn, so one brain plays either way (as the keeper's does, #82).
- **Perception error.** Each position he looks at is off by up to `perceptionError` × its distance, each way. The error is drawn from the counter-based stream for that tick and that footballer (`RandomStream.Perception`, INV-6): the same state gives the same observation.

## The timers are the simulation's

`CpuState`, one per CPU outfielder, plain data in the state (schema 13). He plays on his footballer's own `CpuSkill`, `PlayerState.Skill`, the whole of difficulty: since #166 (schema 30) it's the footballer's, whoever controls him, not the CPU's.

| Field | What it is |
| --- | --- |
| `NextDecisionTick` | The first tick on which he may decide again. |
| `Plan` | The plan in effect: its mode, stick, kick, quick pass, the tick it was observed on and the tick it took effect. |
| `HasNext`, `Next` | A plan decided but still waiting out his reaction delay. |

- **A plan is accepted** only from a CPU outfielder's controller, on a tick he may decide: his interval has passed and no plan is waiting. Otherwise the whole command is rejected as `PlanRefused`.
- **An accepted plan waits** `reactionDelay` (at least one step), then becomes the plan in effect. He may decide again `decisionInterval` after he last did.
- **Between plans he holds his stick.** A plan's kick or quick pass is made once, on the tick it takes effect.
- **So he always acts on the past:** by his reaction delay at best, by that plus his interval at worst.

## Difficulty

Four tiers (#80). They differ in `CpuSkill` only.

| Tier | `decisionInterval` | `reactionDelay` | `perceptionError` | `visionRange` |
| --- | --- | --- | --- | --- |
| Apprentice | 0.25 s | 0.30 s | 8 % of distance | 30 m |
| Club | 0.20 s | 0.22 s | 5 % | 45 m |
| Pro (reference) | 0.15 s | 0.15 s | 2.5 % | 70 m |
| Elite | 0.10 s | 0.10 s | 1 % | 120 m |

- **No tier changes a body.** Speed, acceleration, kick power, kick error and ball control are the footballer's `PlayerCapabilities`, the same type and the same values a human's footballer has. There is no hidden boost to give.
- **Research start** (#83): a decision interval of 0.10 to 0.25 s, reference 0.15; a reaction delay of 0.10 to 0.30 s.
- The four values are named in `CpuSkill.Parameters` with their valid ranges, ready to become bench knobs.

## The brain

In his own frame, attacking +x. Each decision takes the first that applies.

| Mode | When | What he does |
| --- | --- | --- |
| `Recover` | A keeper holds the ball. | Theirs: drops to 12 m inside his own half. His side's: shows 8 m up the pitch, to one side. Eases in and stands. |
| `Shoot`, `Carry` (in the air) | His kick would meet a ball in the volley or header band within reach (#94, [air.md](air.md)), and no teammate is nearer where it comes down. | A header or volley: at goal from within 22 m, up the pitch otherwise. |
| `Shoot` | The ball is his, within 16 m of the goal, and he can kick. | A full kick at the spot 0.7 m inside the post further from their keeper. |
| `Carry` | The ball is his. | Runs at the goal. An opponent within 6 m ahead: he goes round, on the side the opponent isn't. |
| `SeekBall`, `Approach` | He meets the ball no later than the nearest opponent would, or it is within 4 m. | Runs to where he first meets the ball on its read path. `SeekBall` when that is over 1 s off. |
| `Defend` | Otherwise. | Runs to 3 m goal-side of the ball, on the line from it to his own goal. |

- **"The ball is his"** means it is within 1.6 m, on the ground, and no opponent he sees is closer to it.
- **He reads the ball's path** with `BallFlight.Predict`, the simulation's own flight, both goals' frames included (INV-13).
- **He wins an opponent's ball by a standing challenge** (#84, [challenge.md](challenge.md)): on an opponent's ball within 4 m he runs at where it will be a commit's time on, and the challenge goes the way he runs.
- **Technical constants,** in `OutfieldBrain`, not feel: the distances above, a 4 s reading horizon, a 5 m ease-in.

## Measured: CPU against CPU

The table is from #83, before the standing challenge. Since #84 an opponent can't take the ball by being nearer, and the trials record challenges started and won: see [challenge.md](challenge.md).

`eleven-sim cpu <dir>` plays 12 trials at each tier in the 1v1 world, both outfielders CPU footballers of that tier. The ball starts loose in the middle third, at a spot the seed picks. A trial ends at a goal, in a keeper's hands, with the ball out of play, or after 45 s. `docs/simulation/cpu-trials-reference.csv` is the record.

| Tier | Goals | Held | Out | Timed out | Mean length | Kicks | Ball changed feet | Longest stretch with no one on the ball | Decisions |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Apprentice | 4 | 2 | 6 | 0 | 15.2 s | 15 | 13 | 6.0 s | 1230 |
| Club | 5 | 3 | 4 | 0 | 11.4 s | 13 | 7 | 5.6 s | 1278 |
| Pro | 3 | 7 | 2 | 0 | 11.3 s | 14 | 13 | 5.2 s | 1812 |
| Elite | 2 | 7 | 3 | 0 | 10.7 s | 12 | 29 | 6.0 s | 2582 |

- **No trial times out,** and none has a stretch of 10 s with no one on the ball: no stuck search.
- **The longest stretches** are a shot rolling out of play after it missed.
- **The weaker tiers miss the goal more** (6 and 4 out, against 2 and 3): they place the goal's corner and the ball less well. The stronger tiers shoot on target, where the reference keeper holds most of it.
- **The shooting range decides the score.** From 20 m the reference keeper holds every shot; from 11 m nearly every shot scores. 16 m is a middle, not a tuned value.
- **Decision cost** is one flight prediction of 240 steps per decision, twice when an opponent is in sight.

## Tests

`CpuTests`:
- The observation holds no state, command or seed; the brain takes the observation and the config only.
- He places what he sees within his error, deterministically, and sees no one beyond his range.
- A plan waits out his reaction; he decides again only after his interval. A plan too soon, or from a footballer who is no CPU, is refused whole.
- A plan moves no one by itself.
- He goes to a loose ball, carries it, and shoots at the goal he attacks; out of possession he gets goal-side and presses; while a keeper holds the ball he waits.
- His recorded commands, stripped of their plans and sent to the same world with no CPU footballer, move every body and the ball identically.
- The tiers differ in skill only, never in a body's capabilities. The weakest gets to a loose ball later than the strongest.
- A match against him gives the same hashes twice, resumed from a snapshot, and replayed from its encoded recording with no brain running.
- He plays the same football attacking either way, to 1e-6 m.
- CPU against CPU, every tier: kicks, the ball changing feet, both sides on the ball, and no trial timed out or stuck. The trials match the recorded reference.

## Out of scope

Team play, off-ball support, passing choices, player selection, managers, and winning the ball by a challenge (#84).
