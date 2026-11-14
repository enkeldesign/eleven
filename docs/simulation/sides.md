# Two sides and two goals

Issue #82, the first step of the 1v1 match. A match has two sides and a goal at each end. Practice has no sides and keeps its one goal, at +x, exactly as before.

Nothing here is tuned feel. The 1v1 start is a technical start, not a kick-off: restarts get their own sequencer.

## The sides are state

`SimState.Teams` holds a match's sides as plain data (`TeamState`): none for practice, or exactly two.

| Field | What it is |
| --- | --- |
| `Id` | The side's id in this match, positive. Players carry it as `PlayerState.TeamId`. It is not a controller's id: who commands a footballer stays `OwnerControllerId` (INV-10). |
| `AttackSign` | +1 or −1: the sign of world x the side attacks toward. It scores in the goal on that line; its keeper guards the other. |

- **Which way a side attacks is state,** not a property of the team, the screen or the camera. Changing ends at half-time will be a change to `AttackSign`.
- **A state is refused** unless it has no sides or two, with positive ids in ascending order, attacking opposite ways, and every player on one of them.
- **State schema 12** adds the team count and each team to the canonical encoding, after the keepers.

## The far goal is the near goal turned

The −x goal is the +x goal turned half a turn about the centre spot: every coordinate of its frame is the +x frame's with x and y negated.

- **The frame** (`GoalFrame`) takes the end, +1 or −1, and resolves its three parts in the same order at both. A shot at one goal and the same shot turned at the other give the same result to the bit.
- **The posts are named as the attacker sees them.** At +x the right post is at −y; at −x it is at +y.
- **The goal line** judges the ball on its distance toward the end it is at. One `Ball.Line` state serves both lines, since the ball can only be over one.
- **Which end:** the half the ball started the step in. With no sides there is only the +x end, so nothing stands and no line is judged at −x.
- **A `Goal` event names the side it is scored for** in `Other`: the side attacking that end. In practice it is 0.
- **Predictions** (`BallFlight.Predict`) take `bothGoals` to include the −x frame, so a prediction in a match is still the step's own physics (INV-13).

## Each keeper guards his own goal

A keeper guards the end his side doesn't attack (`Perception.GuardedEnd`).

- **He perceives the pitch from his own goal.** `Perception.OfKeeper` gives every keeper an observation with the goal he guards on +x. For the −x keeper that is the world turned half a turn: positions, velocities and the ball's spin with x and y negated.
- **His decision is turned back** (`Perception.InWorld`) before it becomes his command.
- **So one brain keeps either goal** and never knows which. `KeeperBrain` is unchanged.
- **Ownership is unchanged.** Each keeper's command is owned by his own controller and recorded like any other, so a replay runs no brain.
- **His phases, dive, envelope and save** are geometric and don't depend on the end.

## The 1v1 world

`OneVOne.Start(config, seed, homeAttackSign)` in the harness builds the start: four footballers, two sides, two keepers.

| Footballer | Id | Controller | Side |
| --- | --- | --- | --- |
| Home's outfielder (the human's) | 1 | 1 | 1 |
| Home's keeper | 2 | 2 | 1 |
| Away's outfielder | 3 | 3 | 2 |
| Away's keeper | 4 | 4 | 2 |

- The ball is on the centre spot, home's outfielder a stride behind it, and away's 9.15 m off (Law 8's distance).
- Each keeper is 1.5 m off the line he guards, as in practice.
- With `homeAttackSign` −1 the whole start is turned.

## Measured

`eleven-sim sides` plays the same 156 shots, from 11 m, at the +x goal and, with the whole world turned, at the −x goal. Home attacks the end shot at both times, so the same keeper faces every shot with the same random draws.

| End | Shots | Goals | Frame hits | Held | Parried |
| --- | --- | --- | --- | --- | --- |
| +x | 156 | 12 | 24 | 39 | 66 |
| −x | 156 | 12 | 24 | 39 | 66 |

Under the bench's config (`SpikeScenario.Config()`), reference keeper. Every goal is scored for home, the side attacking that end.

## Tests

`TwoSidedTests`:
- 14 shots (through the middle, inside and outside each post, onto each post and the bar, under and over it, in off a post, across the goal) give the same events on the same ticks at both goals, and the ball's path turned, exactly.
- A goal is scored for the side attacking that end, and for the other side when the sides attack the other way.
- With no sides there is one goal, and a ball rolls through where the −x post would be.
- A prediction with both goals equals the step at −x.
- The sides round-trip through a snapshot, and change the state hash.
- The −x keeper's observation is the +x keeper's of the turned world. Four shots are saved alike by the keeper at either end.
- A 1v1 run with four actors gives the same hashes twice, resumed from a snapshot, and replayed from its recorded commands with no brain running.
- The same 1v1 run turned agrees to 1e-6 m. It is not bit-exact with outfielders: a kick's direction and error are angles, and cos(θ + π) is not exactly −cos θ.

## Known limits

- **A quick pass may pick the side's own keeper,** who is a teammate. He catches it and holds it. Keeper distribution comes with the restart sequencer.
- **The view, the host and the CPU outfielder** are later steps of M1.
