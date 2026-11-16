# Injuries

Issue #109, M4: small injuries as bounded event risks at fouls, never continuous random damage, with availability for the rest of the match. Recovery across matches is M6's career.

## When they happen

Injuries are a competition rule in config data (#80): `injuriesFrom`, in outfielders a side, default 10. Eleven a side has them; smaller sides don't, so a footballer taken off never empties a small side. A match needs a referee and two sides.

## At a foul

`Simulator.Injure`, at every foul (#99), after the referee's card (#108), first in the restart step: whatever else stops play in the foul's step (the ball leaving play, offside or the whistle) may take precedence over the free kick, but the injury is still judged.
1. **The chance** is `injuryRisk` (0.01) times the foul's severity, the same severity the cards read, graded as the foul happens (`Simulator.Severity`):
   - 0.25 for a standing challenge, 0.45 for a slide;
   - up to 0.3 for the challenger's pace;
   - 0.2 from behind.

   The draw is on the `Injury` stream, keyed by the fouled footballer (INV-6).
2. **A share `injurySerious` (0.3) are serious.** He is taken off for the rest of the match: he leaves the pitch exactly as a sending-off does (`LeavePitch`, #108), and `PlayerState.Off` covers both. No pass goes to him, and a human's controller takes the CPU teammate nearest where he was.
3. **The rest are minor.** He plays on at `injuredSpeed` (0.85) of his top speed and acceleration, through his body, so every prediction of his run agrees (INV-13).
4. **A second injury is never lighter** than the first.

`Injured` is an event, with its level. The match result's schema 4 counts each side's injuries (INV-14), and the host's status line announces each one.

### Simplifications

- **Only fouls injure.** Collisions without a foul, overuse and the treatment of an injury on the pitch are not modelled.
- **An injury's level is one of two.** Recovery time and the treatment room are M6.
- **There are no substitutes yet:** an injured footballer taken off leaves his side one fewer. Substitutions are M4.05 (#110).

## The rate

Research starting point:
- professional football sees about 8 injuries per 1,000 hours of match play, about 0.26 in a 90-minute match of 22 footballers;
- about half of those come from contact;
- over about 24 fouls, that is roughly 0.5 % of fouls.

`injuryRisk` 0.01 times a typical severity of about 0.5 gives that share. In 6-minute matches with about six fouls, that is roughly three injuries in a hundred matches.

**Measured:** 32 CPU 11v11 matches, 203 fouls, no injury. That fits the rate, about one in 200 fouls. In the 12-match 11v11 reference, two matches (seeds 92000005 and 92000008) play out differently with injuries on: 20 goals, 527 passes, 280 completed.

## Tests

`InjuryTests`:
- with no risk, no foul injures;
- at full risk some fouls do, and a minor injury keeps him on;
- a serious one takes him off, unslotted, his controller's human onto a teammate;
- no quick pass goes to a footballer taken off;
- a foul may injure when the ball leaves play in its step;
- a harder foul injures more often;
- five a side plays without injuries;
- hurt but playing on, he runs at `injuredSpeed`;
- the result counts each side's injuries;
- an injury survives a snapshot.
