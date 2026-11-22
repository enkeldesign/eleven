# A footballer's ratings in the simulation

Issue #159, M6.02 (#80). How a footballer's 1–20 attributes ([world.md](world.md)) become the simulation's own inputs:
- his outfield capabilities;
- his keeping;
- his skill as a CPU footballer.

Every value here is a technical reference, not approved feel.

## The map

`RatingMap` (harness) holds each simulation value as data:
- **the attributes it reads:** his rating for the value is their mean;
- **three points:** its value at 1, at the reference rating 10.5, and at 20.

It is linear between them.

- **10.5 gives the simulation's reference exactly.** A side of average footballers plays as the technical reference, so nothing tuned moves.
- **Every value depends only on the footballer's own attributes.** There is no overall rating and no bonus (#80).

**Outfield** (`PlayerCapabilities`):

| Value | Attributes | At 1 | At 10.5 | At 20 |
| --- | --- | --- | --- | --- |
| maxSpeed (m/s) | top speed | 6.0 | 7.0 | 8.5 |
| acceleration (m/s²) | acceleration | 15 | 25 | 35 |
| kickPower | shooting power | 0.75 | 1.0 | 1.0 |
| kickError (rad) | finishing, short and long passing, curve | 0.12 | 0.05 | 0.02 |
| passVision (rad) | vision | 0.7 | 1.05 | 1.4 |
| passAnticipation | anticipation | 0.55 | 0.75 | 0.9 |
| ballControl | first touch, dribbling | 0.45 | 0.7 | 0.9 |
| tackling | tackling, marking | 0.45 | 0.7 | 0.9 |
| heading | heading, jumping | 0.35 | 0.6 | 0.85 |
| strength | strength | 0.35 | 0.6 | 0.85 |
| stamina | stamina | 0.35 | 0.6 | 0.85 |

Kick power's reference is already the whole of `kickSpeed`, so above 10.5 it stays there. A better finisher gains through a smaller kick error instead.

**Keeping** (`KeeperCapabilities`):

| Value | Attributes | At 1 | At 10.5 | At 20 |
| --- | --- | --- | --- | --- |
| reactionTime (s) | reflexes | 0.32 | 0.20 | 0.14 |
| diveSpeed (m/s) | reflexes, agility | 2.3 | 3.5 | 4.5 |
| reach (m) | keeper positioning, jumping | 0.6 | 0.8 | 1.0 |
| handling | handling | 0.4 | 0.7 | 0.9 |

**CPU skill** (`CpuSkill`): the Apprentice tier at 1, Pro at 10.5 and Elite at 20. A CPU footballer's thinking is his own mental attributes, not a difficulty bonus (#80).

| Value | Attributes | At 1 | At 10.5 | At 20 |
| --- | --- | --- | --- | --- |
| decisionInterval (s) | decisions | 0.25 | 0.15 | 0.10 |
| reactionDelay (s) | anticipation | 0.30 | 0.15 | 0.10 |
| perceptionError | vision, anticipation | 0.08 | 0.025 | 0.01 |
| visionRange (m) | vision | 30 | 70 | 120 |

**Over the reference world** (seed `0x157`):
- outfielders' top speed runs 6.3–8.5 m/s, kick error 0.029–0.103 rad and tackling 0.49–0.88;
- keepers' reaction runs 0.140–0.206 s and their dive 3.2–4.3 m/s;
- CPU decision intervals run 0.105–0.218 s and vision 43–115 m.

## A career match

`CareerSquads` (harness) is the adapter [ADR 0005](../adr/0005-career-layer.md) calls for: the career holds the world, and the simulation plays the match.

- **`Selection(world, club, perSide)`** is a basic selection until M6.08's:
  - **the keeper:** the best by his keeping;
  - **the outfield slots,** from the back: each takes the best unchosen footballer of its line (defence, midfield or attack, from the slot's share of the side). His first position counts first, then his second; failing both, the best left.
  - **the bench:** the best of the rest.
  - **Ties** go to the lower id.
- **`Match`** builds `Squads.Match`'s line-up and plans. Each side's footballers, keeper, CPU footballers and bench then get their mapped values.
- **`Play`** plays it to its result with every brain running.

**The world's divisions apart** (CPU against CPU at eleven a side, 90 s halves, 12 matches each, home and away):

| | Wins | Draws | Losses | Goals |
| --- | --- | --- | --- | --- |
| Division 1 against 3 | 7 | 4 | 1 | 8–2 |
| Division 2 against 3 | 5 | 7 | 0 | 8–1 |
| Division 1 against 2 | 4 | 4 | 4 | 5–5 |

Adjacent divisions are about 2½ rating points apart, and 12 low-scoring matches don't separate the top two. M6.05's seasons will measure it over whole leagues.

## The tests

`RatingMapTests`:
- each value meets the reference at 10.5, moves one way from 1 to 20, and stays within its tunable's range;
- a footballer's values are his ratings mapped;
- a career match gives each footballer, keeper and CPU footballer his own values;
- a top-division club beats a bottom-division one more often than not.
