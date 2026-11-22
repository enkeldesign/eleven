# Knockout: extra time and the shootout

Issue #111, M4: a knockout needs a winner. IFAB's "Procedures to determine the outcome of a match", simplified, with each difference recorded here. Competitions and their draws are M6.

## League or knockout

A competition rule in config data (#80): `knockout`, 0 or 1, default 0.

- **0, a league match:** a draw stands. Nothing here applies, and every match plays as before.
- **1, a knockout:** a match level after two halves plays extra time, and level still, a shootout.

## Extra time

1. **The second half ends level:** `ExtraTime` (0) is raised, and the match rests for a break as long as half-time (`MatchPhase.ExtraBreak`).
2. **The first extra half:** the ends change, the clock starts from nought, and the side that kicked off the match kicks off. `ExtraTime` (1).
3. **A break between,** then the ends change again and the other side kicks off. `ExtraTime` (2).
4. **The second extra half ends:** a side ahead wins; level, the shootout.

Each extra half is `extraTimeShare` (0.3333) of a half on the match clock: 15 minutes of 45.

Every break is free for substitutions (#110), as half-time is.

## The shootout

`MatchPhase.Shootout`. Each kick is a penalty through the restart sequencer (INV-8): it is awarded, set up, taken by its taker's own command, and judged.

- **Who kicks:** the side that kicked off the match first, then the sides in turn.
- **The taker:** the side's next outfielder on the pitch, by id, round again when all have taken. Its keeper takes only if it has no outfielder.
- **Where everyone stands:** the taker at the mark, the keeper he faces on his line, and everyone else in the centre circle's rows, each side in its own half.
- **Who moves and plays the ball:** only the taker until his kick, and from the kick only the keeper he faces. No one else may move, kick or touch the ball, the taker included, so no one follows up a rebound.
- **Scored:** the ball in the goal the kicking side attacks, off the keeper or the frame included.
- **Missed:** the ball out of play elsewhere, held by the keeper, or still in play when `shootoutKickTime` (4 s) runs out.
- **Over:** in the first `shootoutKicks` (5) each, as soon as one side can't be caught in the kicks it has left. After those, on sudden death: when the sides have taken as many and one has scored more.

`ShootoutStarted` and `ShootoutKick` (its taker, and whether it scored) are events. A kick scored is not a goal of the match: no `Goal` is raised, and the match's score stays level.

**The CPU and the human take theirs** as any penalty (#99): a CPU taker kicks as soon as he may; a human taker is left alone until the restart's wait runs out, then the basic taker's kick is made for him.

### Simplifications

- **Each side kicks at the goal it attacked in the last period,** not both at one end chosen by a toss. The sequencer's penalty is by the kicking side's end.
- **No toss:** the side that kicked off the match kicks first.
- **Keepers don't take turns** as takers unless a side has no outfielder, and a side reduced by sendings-off isn't matched in number by the other.
- **No substitutions in a shootout,** and no extra substitution in extra time.
- **Fatigue isn't rested** at extra time's breaks; half-time's rest is half-time's alone.

## Measured

**Twenty-four CPU knockouts at each size,** under the reference config, the managers substituting (#122's re-measure, seeds 0x111000000 onwards):

| Side | Finished with a winner | Went to extra time | Went to a shootout | Kicks in each shootout |
| --- | --- | --- | --- | --- |
| 5v5 | 24 of 24 | 6 | 4 | 9, 16, 8 and 8 |
| 11v11 | 24 of 24 | 6 | 4 | 9, 10, 16 and 9 |

**How a shootout's kicks end,** from six level shootouts at each size (`PenaltyTrials`):

| Side | Kicks | Scored | Saved, held or parried | Missed, all off the frame |
| --- | --- | --- | --- | --- |
| 5v5 | 67 | 51 (76 %) | 13 | 3 |
| 11v11 | 60 | 48 (80 %) | 7 | 5 |

- **Before #122,** the CPU taker scored about one kick in five (15 of 72 at 5v5, 11 of 71 at 11v11), and a shootout ran 9 to 16 kicks.
- **Now most shootouts end in their first five each,** as in football.
- These are untuned references.

## The penalty

The CPU taker's penalty (`RestartControllers.Take`) goes into a corner:
- **`penaltyInside` (0.5 m):** how far inside the post he aims;
- **`penaltyPower` (0.9):** the share of his kick power he uses;
- **`penaltyLoft` (0.135):** the share of the maximum loft he gives it.

The side he favours is set by his id. The simulation's kick error is applied to his kick, as to any (INV-12).

**The benchmark** (#122, `PenaltyTrials`) plays knockouts level at the last step of extra time. Their shootouts' kicks are the CPU taker's, against the reference keeper's brain. `PenaltyTests` checks that the reference scores 65–85 %, is saved 8–27 % of the time and misses at most 15 %, over at least 300 kicks. Football's split is about 75 % scored, 17 % saved and 8 % missed.

**The sweep that chose the values,** at five a side:

| Inside, power, loft | Kicks | Scored | Saved | Missed |
| --- | --- | --- | --- | --- |
| 0.6 m, 0.9, 0.04 (before) | 499 | 18 % | 82 % | 0 % |
| 0.6 m, 0.9, 0.12 | 410 | 52 % | 48 % | 0 % |
| 0.6 m, 0.9, 0.14 | 502 | 81 % | 19 % | 0 % |
| 0.5 m, 0.9, 0.13 | 438 | 65 % | 30 % | 6 % |
| **0.5 m, 0.9, 0.135 (the reference)** | 891 | 76 % | 18 % | 6 % |
| 0.5 m, 0.9, 0.14 | 926 | 82 % | 12 % | 6 % |
| 0.3 m, 0.9, 0.2 | 197 | 79 % | 2 % | 19 % |

- **Loft decides the saves.** The keeper's reach is a sharp cliff: about half are saved at 0.12, about a tenth at 0.14. Low and hard, the reference keeper reaches most kicks.
- **The aim inside the post decides the misses.** At 0.5 m his kick error puts about 6 % against the frame; at 0.6 m and more, none; at 0.3 m, about one in five.
- **Feel is open:** a taker who varies his corner and height, or reads the keeper, is M7's. These values are technical references.

## The result

Match result schema 6 (INV-14):

| Field | Meaning |
| --- | --- |
| `extratime` | 1 when extra time was played; `played` then includes it. |
| `shootout` | Kicks each side scored. |
| `shootoutkicks` | Kicks each side took; 0-0 when there was no shootout. |
| `winner` | The winning side's id, by goals and then by the shootout; 0 for a draw. |

A goal in extra time has half 3 or 4.

## The host

`?knockout=1` in the address plays a knockout. The status line announces extra time, each of its halves, the shootout and each kick with the kicks' score; the match panel names the period and shows the penalties' score; and the verdict at full time is the shootout's when the goals are level.

## Tests

`KnockoutTests`:
- a league draw stands; a knockout with a winner after two halves is over;
- level, it goes to extra time: the break, two halves of `extraTimeShare`, the ends changed before each, the other side kicking off the second;
- a winner after extra time ends it, and the result records extra time;
- level again, a shootout starts with a penalty for the side that kicked off;
- the sides kick in turn, a side's takers take turns, and a kick isn't a match goal;
- it ends as soon as one side can't be caught; level after five each, sudden death;
- a kick no one ends is a miss after `shootoutKickTime`;
- only the taker and the keeper he faces move;
- after his kick, the taker can't play the ball again, under FREE and ATTACHED;
- with every controller running, a level knockout finds a winner;
- a shootout survives a snapshot.
