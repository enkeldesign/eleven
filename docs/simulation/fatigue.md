# Fatigue

Issue #100. Sprinting is the full stick (#80); fatigue is what limits it. Every value is an untuned technical reference, not approved feel.

## The rule

**Only in a match world** (two sides). Practice and the benches measure the body as it is.

Each footballer carries **fatigue**, 0 fresh to 1 spent (state schema 21). Every new state has it at 0.

| Running | Change per second |
| --- | --- |
| Above `fatigueFrom` (0.7) of his fresh top speed | + `fatigueDrain` (0.012) × how far above, up to his top speed, × (1.6 − stamina) |
| Below it | − `fatigueRecover` (0.02) × how far below, to standing, × (0.4 + stamina) |

- **`stamina`** is a new capability: 0–1, reference 0.6. At 0.6 the knobs' rates apply. At 1 he tires at 0.6 of them and rests at 1.4.
- **Tired, his top speed and acceleration** fall toward `fatigueFloor` (0.85) of themselves. They go through the same body that his running and every prediction of it use (INV-13), the dribble's cost included.
- **Half-time** takes `halfTimeRecovery` (0.5) of every footballer's fatigue away.

The five knobs are in the bench's Control tab (config format 22).

## The CPU

- He reads his own fatigue, and his teammates'. A teammate's tiredness can be read; an opponent's only as the speed he sees.
- A tired CPU footballer reckons his own runs, and his teammates', at the top speed fatigue leaves them, by the body's own formula (#118, INV-13). So a spent footballer is later to the ball than a fresher teammate, and the fresher one presses. The 0.6 s delay he once added was dropped: over the gate's 18 matches it changed nothing beyond their noise.
- At two a side the pair agree on it.
- In a shape of three or more, the shape's own first to the ball goes.

## Measured

**12 CPU 5v5 matches,** mean outfield fatigue at each minute of play:

| Minute | 1 | 2 | 3 (half-time) | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| Fatigue | 0.125 | 0.189 | | 0.193 | 0.225 |

- The second half starts fresher than the first half ended, after half-time's rest, and ends more tired. At 0.2 fatigue a footballer keeps 97 % of his speed.
- Goals: 17 in the first half, 18 in the second.

**2v2, 50 matches:** 291 goals; 318 passes, 134 completed; longest bunched 3.9 s.

**Finding.** At 2 × 180 s fatigue barely bites. It is built for M4's longer 11-a-side matches, where substitutions will matter.

## Tests

`FatigueTests`:
- Practice is timeless.
- Sprinting tires him and standing rests him.
- Spent, he keeps only the floor of his top speed.
- More stamina tires slower.
- A tired CPU footballer leaves the press to a fresh teammate.
- Half-time gives back its share.
