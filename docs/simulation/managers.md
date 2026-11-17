# The CPU manager's tactics

Issue #127, M5 (#80): manager decision policies. The CPU manager of #110 replaces the injured and the tired. With `cpuTactics` on, he also switches between his side's three plans (#126) and makes tactical substitutions, by the score, the clock and the men on the pitch. Every value here is a technical reference, not approved feel.

## What he sees (INV-9)

`ManagerObservation` has #110's fields, and adds:
- the score: his side's goals and the other side's;
- each side's footballers on the pitch: a sending-off is plain to see;
- his current plan, and whether he may switch (plans stored, no cooldown, none waiting);
- each of his footballers' **line** in his formation: back, middle or forward;
- his **spread**: one draw a side a match from the counter-based `Manager` stream (INV-6), up to `cpuTacticSpread` of the match either way. It shifts the clock he reads, so two managers don't react on the same tick.

Nothing of the human's commands or the other side's footballers.

## His policy

His plans are, by convention, **balanced** (the first), **chase** and **protect**.

1. **Behind** by `cpuChaseMargin` or more, from `cpuChaseFrom` of the match: chase.
2. **Ahead,** from `cpuProtectFrom`: protect.
3. **Fewer men than the other side,** and not behind: protect.
4. **Otherwise** balanced. He asks only for a plan he hasn't got, and only when he may switch.

**Substitutions,** in order:
1. the injured, as in #110;
2. **chasing, his most tired forward; protecting, his most tired midfielder.** Only from `cpuSubAfter`, and only if that footballer is at least half as tired as `cpuSubFatigue`. Fresh legs never replace fresh legs.
3. then #110's most tired outfielder.

| Knob | Range | Default | Cautious | Bold |
| --- | --- | --- | --- | --- |
| `cpuTactics` | 0–1 | **0 (off)** | 1 | 1 |
| `cpuChaseFrom` | 0–1 | 0.67 | 0.67 | 0.5 |
| `cpuChaseMargin` | 1–5 goals | 1 | 1 | 1 |
| `cpuProtectFrom` | 0–1 | 0.67 | 0.67 | 0.83 |
| `cpuTacticSpread` | 0–0.25 | 0.05 | 0.05 | 0.05 |

**Off by default.** With it on, his tactical substitutions change matches even when a side's three plans are the same. Every reference stays as it was with it off. Whether CPU sides manage by default, and with which preset, is for M5's gate (#130).

## Measured

`eleven-sim managers`, in CI against `manager-reference.csv`: at the reference config, each side with a balanced (reference), a chase (#126's high press) and a protect (#126's low block) plan. It fails if a side switches more often than its cooldown allows.

### CPU against CPU, eight seeds a preset

| Preset | Goals | Switches a match | To chase, protect, balanced (all matches) | At (share of the match) | Most by one side | Substitutions a match |
| --- | --- | --- | --- | --- | --- | --- |
| Off | 0.8–0.9 | 0 | — | — | 0 | 2.8 |
| Cautious | 0.9–1.0 | 1.9 | 5, 8, 2 | 0.37–0.92, mean 0.76 | 2 | 3.8 |
| Bold | 0.6–0.9 | 1.9 | 6, 8, 1 | 0.37–0.99, mean 0.77 | 2 | 4.8 |

- **About two switches a match**, most of them in the last quarter: a side behind chases, the side ahead protects.
- **The early switches (from 0.37 of the match)** are a side down to ten protecting.
- **The cooldown allows 13 a side; no side made more than 2.** There's no spam.
- **Tactical substitutions add one or two a match.** The first run, before the fresh-legs rule, made 7.9 with the cautious preset: chasing, the manager replaced a forward however fresh, a substitute just on included.

### The scripted seat against a managed CPU side, four seeds a preset

The M4 review's direct runner (finding 5): a harness seat that chases the ball and shoots.

| Preset | The seat's goals–conceded | The CPU side's switches | When |
| --- | --- | --- | --- |
| Off | 2.8–0 | 0 | — |
| Cautious | 2.0–0 | 1 a match, all to chase | 0.65–0.73 |
| Bold | 1.8–0 | 1 a match, all to chase | 0.47–0.65 |

- **The managed side still concedes and doesn't score.** Its chase plan comes after it is already behind.
- **The direct runner's weakness is the CPU's defending,** not its manager. Four seeds are too few to say whether chasing changes the seat's goals.

## Tests

`ManagerPolicyTests`:
- off by default he never switches;
- behind late he chases and not early;
- ahead late he protects and not early;
- level again he goes back to balanced;
- short of men and not behind he protects;
- he asks for nothing he already has or may not have;
- his spread moves his thresholds;
- chasing he freshens his forwards, protecting his midfield, and he never replaces fresh legs with fresh legs;
- each side draws its own spread, the same every time;
- he reads the score, the men and the lines;
- a CPU side behind late switches to its chase plan in the match.
