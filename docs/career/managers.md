# Managers

M6.09 (#80): managers as entities, with competence, philosophy, temperament and memory, driving the M5 policies. Its parts so far:
- **#180, a manager's philosophy:** his formation and his three plans, and an XI picked for his formation.
- **#182, managers manage:** each by his own profile, in every career match.
- **#191, his memory of opponents:** each meeting remembered, and his plans for the next shaped by it.

## Who he is

Each club has a manager in the world (#157):
- **his preferred formation:** one of the six (#125);
- **his temperament:** 1, cautious, to 20, bold;
- **his reputation**, and **seven competences**, each 1–20: tactics, coaching, development, scouting, recruitment, motivation and adaptability. Coaching and development set how fast his young footballers grow, and development his youth's level (#194, [development.md](development.md));
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

Since #182 each side's manager uses them ([his profile](#his-profile)).

## His profile

M5's CPU manager (#127) decides by knobs that are config, the same for both sides, and off by default (`cpuTactics`). `ManagerProfile` gives each side its own knobs (#182).
- **They're his controller's input, not state or config.** His decisions are recorded commands (INV-10), so a replay needs no profile, and no state hash, config hash or simulation reference moves.
- **Without a profile** a side has the config's, as before.

`CareerManagers.Profile(manager, config)` manages tactics always. Its anchors are #127's two presets, and its values are untuned references:

| Knob | From | Cautious or least | Bold or most |
| --- | --- | --- | --- |
| `ChaseFrom` | temperament | 0.67 | 0.5 |
| `ProtectFrom` | temperament | 0.67 | 0.83 |
| `ChaseMargin` | — | 1 | 1 |
| `Spread` | tactics competence | 0.1 | 0.02 |
| `SubAfter` | adaptability | 0.75 | 0.6 |
| `SubFatigue` | adaptability | 0.25 | 0.15 |

**Every career match is managed:** the human's fixture in the host, a quick match from Home, and every career match the harness plays, the quick-sim's samples included.
- **A player-manager manages his own side,** as before: no CPU manager reads a side a human plays in.
- **Watching,** his club's manager manages it.

### Measured

`eleven-sim managers career 96`: the eleven-a-side sample's first 96 fixtures, each side by its manager, a substitute on his own CPU skill (#166).

| Temperament | Sides | Switches a match | To chase | To protect | Substitutions a match |
| --- | --- | --- | --- | --- | --- |
| cautious, 1–7 | 58 | 0.78 | 14 | 26 | 1.91 |
| middle, 8–13 | 65 | 0.74 | 25 | 19 | 1.66 |
| bold, 14–20 | 69 | 0.84 | 30 | 22 | 1.81 |
| all | 192 | 0.79 | 69 | 67 | 1.79 |

- **The bold chase more and the cautious protect more:** they reach their plans at different moments of the same matches.
- **About one switch a side a match,** fewer than #127's two a match with both presets. Most career matches are close, and level for long spells.
- **Managed, career matches draw more:** 45 % of the eleven-a-side sample, at 1.18 goals a match, against 43 % unmanaged ([quicksim.md](../simulation/quicksim.md#eleven-a-side)). #182's sample, before substitutes played on their own skill, drew 48 %.

## His memory of opponents

#80 asks for managers who remember opponent tendencies between fixtures, within the perception boundary. `Manager.Memory` (#191) holds each manager's meetings, the latest `MemoryRules.Depth` (3) with each opponent.

**What he remembers** is only his own club's matches, and only the result contract's facts (`ManagerMemory.Remember`, after each fixture is recorded):
- the season and matchday;
- goals for and against, and those of the last quarter (the second half's second half, and extra time);
- the formation the opponent lined up in, as anyone watching saw;
- **for a match played in full:** both sides' PPDA and his possession (#128).

Nothing of the opponent's squad, ratings or plans. A quick-simmed match has no metrics, so most memories hold the score and its timing.

**What it does.** Before a meeting, `CareerManagers.Plans(manager, team, opponent)` and `Profile(manager, config, opponent)` shift his own by `ManagerMemory.Shift`, with knobs in `MemoryRules`, all untuned references:

| What he remembers | Shift | Cap |
| --- | --- | --- |
| His goal difference over the meetings, g | His line by 0.5 m × g, deeper when beaten | ±2 m |
| The same | He protects earlier by 0.03 × −g of the match | ±0.1 |
| They outscored him in the last quarters | He protects 0.05 earlier still | |
| In the meetings played in full, their PPDA more than 2 below his | His step-up eases by 1 | |

With no memory of an opponent, his plans and profile are exactly #180's and #182's.

**Where it acts:** a manager's plans act only in the full simulation, so in a career it's the CPU manager facing the human who adapts. The quick-sim's sides have no plans.
- **The human's own side** plays his manager's plans and profile as they are, whether he plays or watches (`CareerSession.MatchFor` and `ProfilesFor`). What he makes of the last meeting is his, from the hub's line. His manager still remembers, for that line.

**The human sees it:** the hub's next-fixture line ends with the last meeting, e.g. "Last met: lost 1–2 (season 1, matchday 7)".

**Saved** with the world at format 5 ([world.md](world.md)), a format-4 world reading with none.

### Measured

`eleven-sim memory 3`: three seasons headless, the human at the first club.
- **Memories:** 1,758 meetings over 36 managers, 17,842 of the world's 95,841 bytes. A save grows from 111,872 to 126,884 characters.
- **The human's division's rivals,** each by his head-to-head with the human's club:

  | Rival | Meetings | Goals for–against | Late | Line | Protects earlier |
  | --- | --- | --- | --- | --- | --- |
  | QUE | 3 | 1–5 | 0–0 | −2.0 m | 0.10 |
  | WYS | 3 | 0–4 | 0–0 | −2.0 m | 0.10 |
  | WYG | 3 | 0–3 | 0–0 | −1.5 m | 0.09 |
  | JOR | 3 | 1–3 | 0–2 | −1.0 m | 0.11 |
  | DRA | 3 | 0–2 | 0–0 | −1.0 m | 0.06 |
  | ROS | 3 | 3–4 | 0–1 | −0.5 m | 0.08 |
  | NES | 3 | 1–2 | 1–0 | −0.5 m | 0.03 |
  | ROM | 2 | 3–4 | 0–0 | −0.5 m | 0.03 |
  | MAR | 3 | 1–1 | 1–1 | 0 | 0 |
  | GAL | 3 | 1–1 | 0–0 | 0 | 0 |
  | QUL | 3 | 2–0 | 2–0 | +1.0 m | −0.06 |

- **No step-up shifts:** every match was quick-simmed, so no meeting has metrics. A played career gives them for the human's own fixtures.
- **The save's size:** 11 three-season saves would come to about 1.40M characters, within iOS Safari's 2.5M but past the half #181 keeps for presets and captures. Without memories it's about 1.23M already. That's for M6's gate (M6.16) to weigh.

## An XI for his formation

The selection fills each slot from its line (#159). At ten outfielders the lines are now the formation's own, read from the back:
- the first line defends;
- the last attacks;
- those between are midfield.

So a 4-3-3 picks three forwards and a 5-3-2 five defenders. The 4-4-2's lines are the old fixed rule's, so a 4-4-2 club picks as before. Below ten outfielders, the old rule stands.

## Tests

`ManagerProfileTests`:
- with no profiles, the managers' commands are as before;
- a profile decides for its side whatever the config;
- a manager's profile by temperament and competences;
- a career match managed on both sides, a player-manager's own side not.

`ManagerMemoryTests` (#191):
- a meeting remembered by both managers from its result, quick-simmed or played in full;
- only his club's meetings, the latest few of each;
- each shift, its cap, and none without memory;
- a career match playing the shifted plans;
- in the human's fixture, only his opponent adapting, whether he plays or watches;
- the hub's last meeting.

`MemoryCodecTests` covers the world's bytes at format 5, and a format-4 world read with no memories.

`ManagerPlansTests`:
- a manager without a philosophy plays the reference, every plan in his formation;
- his depth sets his line, and his pressing his press and step-up;
- chase and protect move toward the high press and the low block, further by temperament;
- a world's managers spread across philosophies and formations;
- a career match stores both managers' plans and formations;
- the slots by each formation's lines;
- an XI for 4-4-2, 4-3-3 and 5-3-2.

`AvailabilityTests` covers the world's bytes at format 4, and a format-3 world read unset.
