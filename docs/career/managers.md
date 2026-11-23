# Managers

M6.09 (#80): managers as entities, with competence, philosophy, temperament and memory, driving the M5 policies. Its parts so far:
- **#180, a manager's philosophy:** his formation and his three plans, and an XI picked for his formation.

## Who he is

Each club has a manager in the world (#157):
- **his preferred formation:** one of the six (#125);
- **his temperament:** 1, cautious, to 20, bold;
- **his reputation**, and **seven competences**, each 1–20: tactics, coaching, development, scouting, recruitment, motivation and adaptability;
- **his philosophy** (#180), two values of 1–20:
  - `Pressing`, how far up his side presses;
  - `Depth`, how high its line stands.

  They're drawn by the generator on counters of their own, so no other draw moves. A world from before #180 reads them as 0, and a manager without a philosophy plays the reference plan.

## His plans

`CareerManagers.Plans(manager, team)` gives his three, in #127's order: **balanced**, **chase** and **protect**. All three are in his preferred formation, in `TeamTactic`'s fields (#126). Every value is an untuned technical reference.

**Balanced:** his philosophy on the reference plan.

| Field | From | Range |
| --- | --- | --- |
| `Line` | depth: −6 m at 1, +6 m at 20 | −6 to 6 |
| `Press` | below the middle of his pressing, from the low block's −10 up to anywhere (60); above it, anywhere | −10 to 60 |
| `StepUp` | above the middle of his pressing, from the reference's 6 up to the high press's 10 | 6 to 10 |
| `Width` | the reference's | 1 |

At the middle of both he plays the reference, so most managers are near it.

**Chase** moves every field of his balanced plan toward #126's high press, and **protect** toward its low block. His temperament sets how far:
- **chase:** half way for the most cautious, all the way for the boldest;
- **protect:** all the way for the most cautious, half way for the boldest.

Each field stays within `TeamTactic`'s ranges.

## A career match

`CareerSquads.Match` stores each side's plans and formation from its manager, unless a caller gives others. That holds for the human's fixture, played or watched, and for a quick match from Home. The quick-sim plays its sample's model, now fitted to matches played this way ([quicksim.md](../simulation/quicksim.md#eleven-a-side)).

M5's CPU managers switch plans only with `cpuTactics` on, which is off by default. So in a career match today, each side plays its balanced plan, and the chase and protect plans wait for M6.09b's managers to use them.

## An XI for his formation

The selection fills each slot from its line (#159). At ten outfielders the lines are now the formation's own, read from the back:
- the first line defends;
- the last attacks;
- those between are midfield.

So a 4-3-3 picks three forwards and a 5-3-2 five defenders. The 4-4-2's lines are the old fixed rule's, so a 4-4-2 club picks as before. Below ten outfielders, the old rule stands.

## Tests

`ManagerPlansTests`:
- a manager without a philosophy plays the reference, every plan in his formation;
- his depth sets his line, and his pressing his press and step-up;
- chase and protect move toward the high press and the low block, further by temperament;
- a world's managers spread across philosophies and formations;
- a career match stores both managers' plans and formations;
- the slots by each formation's lines;
- an XI for 4-4-2, 4-3-3 and 5-3-2.

`AvailabilityTests` covers the world's bytes at format 4, and a format-3 world read unset.
