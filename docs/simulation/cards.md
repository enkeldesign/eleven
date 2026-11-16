# Cards

Issue #108, M4: Law 12's cautions and sendings-off, a simplification recorded here with each place it differs.

## When they are shown

Cards are a competition rule in config data (#80): `cardsFrom`, in outfielders a side, default 10. Eleven a side is carded; smaller sides are not, so a sending-off never empties a small side. A match needs a referee and two sides.

## The referee's call

`Simulator.Discipline`, at every foul (#99), before the restart is given.

1. **Denying an obvious goal-scoring opportunity** (DOGSO), simplified, has three conditions:
   - the fouled footballer was carrying the ball;
   - he was within `dogsoRange` (25 m) of the goal he attacks;
   - none of the offender's side stood nearer that goal line than him, apart from its keeper, the offender and anyone sent off.

   **Outside the penalty area, a sending-off. In the area, a penalty and a caution:** every challenge here plays for the ball, which Law 12 cautions rather than sends off.
2. **Otherwise the foul's severity, as the referee reads it.** It is the sum of four parts:
   - 0.25 for a standing challenge, 0.45 for a slide;
   - up to 0.3 for the challenger's pace, as a share of his top speed;
   - 0.2 from behind the fouled footballer's facing;
   - give or take `cardNoise` (0.3), drawn on the `Card` stream.

   From `cautionFrom` (0.8), reckless: a caution. From `sendOffFrom` (1.15), excessive force: a sending-off.
3. **A second caution sends off.**

`Cautioned` (with his cautions now) and `SentOff` are events. The match result's schema 3 counts each side's cautions and sendings-off (INV-14).

## Sent off

`PlayerState.SentOff`, for the rest of the match:
- **He goes beyond the touchline,** 10 m off the pitch, out of every touch, challenge and contact. His wind-up, challenge and control end.
- **No one plays him.** A CPU footballer's controller is removed. A human's controller takes over the nearest CPU teammate who isn't the keeper, emitting `Switched`, and the sent-off footballer answers to no controller.
- **No one sees him.** CPU perception leaves him out (INV-9), and so do offside's positions.
- **No restart places him,** and he is never a taker.
- **His side's shape closes up:** the slots above his move down one, so ten a side plays as nine in `OutfieldBrain.Lines(9)`, a 3-4-2.

The host draws him out of view, and the status line announces each card.

### Simplifications

- **Severity is the challenge's kind, pace and angle,** not its contact. Holding, handball and misconduct other than fouls are not judged.
- **DOGSO's "general direction of play" and "likelihood of keeping control"** are left out, beyond the carrier having the ball.
- **A card during advantage is shown at once,** not at the next stoppage.

## Measured

Sixteen CPU 11v11 matches, four per tier, both sides at the tier, chose the reference thresholds.

| Thresholds | Cautions | Sendings-off |
| --- | --- | --- |
| 0.6 and 0.95 | 51 of 105 fouls (49 %) | 21, about 1.3 a match |
| **0.8 and 1.15** | 23 of 109 fouls (21 %) | 2 in 16 matches |

Professional football cautions about one foul in six (17 %) and sees about 0.15 sendings-off a match. DOGSO never arose in these matches: with both thresholds out of reach, no card was shown in 16 more.

The 12-match 11v11 reference moves with cards: 21 goals (19 before), 538 passes, 285 completed, never bunched.

## Tests

`CardTests`:
- a careless foul is a free kick only;
- reckless, a caution; excessive force, a sending-off at once; a second caution sends off;
- DOGSO outside the area sends off, in it gives a penalty and a caution, and a defender between makes it none;
- five a side plays without cards;
- sent off, he leaves the pitch: unslotted, unseen, unplaced, his side reslotted;
- the human's controller takes a teammate;
- cards survive a snapshot.
