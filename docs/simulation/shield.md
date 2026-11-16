# Shielding

Issue #97. A carrier puts his body between the ball and a challenger. There is no button: the body position follows from moving slowly under pressure. Every value here is an untuned technical reference, not approved feel.

## The rule

A footballer **shields** when all of these hold on a step:
- he has the ball at his feet, as [the dribble's cost](dribble.md#running-with-the-ball) defines it;
- he moves slower than `shieldSpeed` (0.45) of his top speed with it;
- an opponent is within `shieldRange` (2 m).

**Where the ball goes.** It is kept just off his feet, at the slow lead, on the far side from the nearest opponent:
- under FREE, by his touches, standing or moving;
- under ATTACHED, by the steering.

**A challenge through him.** A standing challenge ([challenge.md](challenge.md)) can reach a ball that lies beyond him along the challenge's line. Then it is a **contest**, drawn from its own counter-based stream (`RandomStream.Shield`, INV-6):

```
hold = shieldHold + 0.5 × (carrier's strength − challenger's strength) − 0.3 × (challenger's tackling − 0.5)
```

- `hold` is kept within 0.05–0.95; `shieldHold` is 0.6.
- **Held:** `ShieldHeld` (the carrier, the challenger) and `ChallengeMissed` are emitted, and the challenger recovers as after any miss.
- **Not held:** the challenge wins as before.

**No bodies.** Footballers have no bodies to collide, so a challenger may end his commit past the carrier. "Beyond him" is judged along the line the challenger came on.

**Strength** is a new capability: 0–1, reference 0.6, state schema 19. The three knobs are in the bench's Control tab (config format 19). `shieldSpeed` 0 turns shielding off.

## Measured

- **The challenge bench** (`challenge-reference.csv`): a standing dribbler now shields. 9 of 30 challenges win and 21 are held. A jogging dribbler, too fast to shield, loses all 30.
- **In play:** in 12 CPU 5v5 matches, 4 of 542 challenges meet a held shield.

**Finding.** CPU carriers seldom slow down under pressure: they carry, pass or shoot. A brain that holds the ball up when pressed with no pass on, waiting for support, is M5's tactics.

## Tests

`ShieldTests`:
- A slow carrier with an opponent near keeps the ball on his far side, under FREE and ATTACHED.
- A challenge through a shielding carrier is a contest: neither always wins, and a stronger challenger wins more.

`ChallengeTests` runs the challenge's own mechanics with shielding off, and checks the bench's contest against a standing dribbler.
