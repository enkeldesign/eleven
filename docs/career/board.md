# The board

M6.11b, #200. Each club's board sets objectives at each season's start and judges its manager against them. Its confidence moves match by match with points won against the expected. At the season's turn, the clubs' and managers' reputations move with what they achieved. M6.15's job market will sack and appoint by confidence and reputation.

The parts:
- **`Board`** (`Eleven.Career`): the objectives, confidence by match and at the season's end, reputation's moves and the words.
- **`BoardRules`:** the knobs, all technical references, not tuned feel.
- **`CareerBoard`** (harness): the measurement, `eleven-sim board [seasons]`.

## What the research gives

- **Spending predicts the table.** Across seasons, a club's wage bill explains most of where it finishes (Szymanski and Kuypers, *Winners and Losers*, 1999, and the studies since). So a board expects the club's wage rank.
- **Boards judge results against their expectations.** A head coach's dismissal follows the gap between his side's results and what was expected of it before the season, as bookmakers' odds put it (Pieper, Nüesch and Franck, 2014; Bryson, Buraimo, Farnell and Simmons, 2023). No betting market is modelled, so here the expectation is the wage rank, and confidence moves with points against it.
- **Reputation follows achievement slowly,** over seasons, with a club's division and finishes.

## Objectives

`Board.Objectives`, at each season's start for every club, after the turn's movement, development and youth:

| Objective | Set by | Reference |
| --- | --- | --- |
| The expected place | the club's weekly wage-bill rank in its division, ties by reputation, then id | |
| The league | the expected place, `Leeway` places lower, never below safety | `Leeway` 2; safety 10th, above the two relegated |
| The cup | a round to reach by division (`CupTarget`) | the quarter-finals, the round of 16, the round of 32 |
| The money | end the season in the black | |

**In words** (`Board.League`, `Board.Cup`):

| League target | Says |
| --- | --- |
| 1st | "Win the league" |
| 2nd, below the top division | "Win promotion" |
| 10th, above the bottom division | "Avoid relegation" |
| Any other | "Finish 6th or higher" |

The cup's says "reach the round of 16", or "win the cup" for a target past the final.

## Confidence

The board's confidence in its manager, 0–100, `BoardState.Confidence`. A new world's starts at `StartConfidence`, 60.

| When | Moves by | Reference |
| --- | --- | --- |
| Each league match | `PerPoint` × (points won − the match's expected) | 2.5 |
| Out of the cup before its target round | −`CupMissed` | 6 |
| Each cup round reached beyond it | +`CupBeyond` | 3 |
| The season's end, the league objective met | +`LeagueMet`, then `PerPlace` a place above the target | 5, 2 |
| The season's end, missed | −`LeagueMissed`, then `PerPlace` a place below it | 5, 2 |
| The season's end in deficit | −`Deficit` | 8 |
| The new season | keeps `Carry` of its distance from the start | 0.5 |

- **A match's expected points:** `Board.ExpectedPoints`, from `TopPoints` for the best paid falling evenly to `BottomPoints` for the least, measured below: 1.50 and 1.02.
- **Bounded:** each move stays within 0–100.
- **In words** (`Board.Words`): delighted from 80, pleased from 60, satisfied from 40, concerned from 20, then losing patience.
- **Last season's verdict:** at the turn each club keeps its place, expected place and target, its confidence before the carry and its reputation before the move, for the review.

## Reputation

At each season's turn, before its movement (`Board.SeasonEnd`):
- **A club's target** (`Board.ReputationTarget`) is what the generator would give a club of the division it played in and its place. It's the division's level (13, 11, 9), from `ClubSpread` (1.5) above for the champion to as far below for the last. The cup's winner gets `CupWinnerReputation` (1) more.
- **A manager's target** is his club's, plus `ManagerPerPlace` (0.5) a place above his expected, or less a place below.
- **Each moves** (`Board.Toward`) `ReputationRate` (0.35) of the way, rounded, at most `ReputationStep` (2) a season, within 1–20.
- **What reads it:** a club's reputation sets its home takings (#198) and the first season's cup ranking. Neither touches a match (INV-1).

## The hub

- **The board line:** "The board: finish 6th or higher, reach the round of 16 and end the season in the black. Confidence 62: pleased."
- **The season's review** (#194) adds the verdict: "4th, 6th expected: objective met. Confidence 74. Reputation 13, up 1."

## Saved

At world format 9 ([world.md](world.md)), each club in order:
- its expected place and its league and cup targets, in a byte each, and its confidence;
- last season's place, expected place and target, its confidence then, and its reputation before the turn.

A format-8 world reads its objectives by the rules, with confidence at its start and no verdict.

## Measured

`eleven-sim board 3`: a headless career from seed `0x200`, every match quick-simmed, each season turned, its contracts renewed and released at each turn (#201) and its transfer windows open (#203).

**Points a match by expected place**, every division's clubs together:

| Season | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1.45 | 1.53 | 1.29 | 1.39 | 1.18 | 1.30 | 1.17 | 0.95 | 1.05 | 1.44 | 1.02 | 1.02 |
| 2 | 1.23 | 1.64 | 1.26 | 1.27 | 1.23 | 1.23 | 1.06 | 1.20 | 1.36 | 1.30 | 1.08 | 1.30 |
| 3 | 1.53 | 1.64 | 1.30 | 1.08 | 1.38 | 1.20 | 1.38 | 1.52 | 1.20 | 0.98 | 1.02 | 0.97 |
| All | 1.40 | 1.60 | 1.28 | 1.25 | 1.26 | 1.24 | 1.20 | 1.22 | 1.20 | 1.24 | 1.04 | 1.10 |

**The board's verdicts,** and confidence at each season's end:

| Season | Met their league objective | Division 1 | Division 2 | Division 3 | Confidence | Below 20 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 25 of 36 | 7 | 8 | 10 | 61 ± 24 | 6 % |
| 2 | 23 of 36 | 8 | 8 | 7 | 62 ± 29 | 8 % |
| 3 | 24 of 36 | 8 | 8 | 8 | 62 ± 28 | 11 % |

**Mean reputation by division** at each season's start:

| Season | Clubs 1 | Clubs 2 | Clubs 3 | Managers 1 | Managers 2 | Managers 3 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 12.75 | 10.67 | 9.08 | 12.67 | 10.92 | 8.42 |
| 2 | 12.83 | 10.75 | 9.08 | 13.08 | 10.58 | 8.67 |
| 3 | 12.83 | 10.75 | 9.25 | 13.00 | 11.00 | 8.33 |
| After 3 | 12.83 | 10.67 | 9.17 | 13.08 | 11.08 | 8.25 |
| Drift | 0.08 | 0.00 | 0.08 | 0.42 | 0.17 | −0.17 |

- **The table is flatter than football's.** The best paid take about 1.5 points a match and the least about 1.0; in football it's more like 2.2 and 0.8. The career's matches score 1.18 goals and draw 45 % ([managers.md](managers.md)), so places are closer. `TopPoints` and `BottomPoints` are this measurement's line, from six seasons (`eleven-sim board 6`), not football's. The first references, 2.0 and 0.9, judged every top club short and every bottom club ahead: confidence ended at 50 ± 27, with up to 31 % below 20 (before contracts).
- **Neither easy nor impossible:** two-thirds to three-quarters of clubs meet their league objective each season, a quarter don't. Confidence ends near 63 ± 27, and 6–11 % of clubs end below 20 over six seasons, M6.15's reference for a board that has lost patience.
- **Clubs' reputations hold:** their drift by division is within half a point over three seasons, and within 0.33 over six.
- **Managers' reputations drift up in the top division:** 0.42 over three seasons and 0.58 over six. In the others they hold within a third of a point. It's selection: the managers of promoted clubs beat their expectation and rose, and the relegated clubs' fell and left. Until M6.15 moves managers between clubs, a manager rises and falls with his club.
- **Contracts (#201) leave the expected points where they were:** six seasons with them give the same line, 1.50 and 1.02. With transfers (#203) the table flattens a little, as squads move: 1.44 and 1.07. The references stay at 1.50 and 1.02, within 0.06, for M6's gate (M6.16) to measure again with the whole career.

## Tests

`BoardTests` (career), 6:
- the expected place by wage rank, its targets, and a club paid ten times more expected to win;
- each objective's and confidence's words;
- confidence by a league match's points against the expected, its bounds, and by the cup's rounds;
- the season's end: the league objective and the money judged, the verdict kept, reputations moved and confidence carried;
- reputation's target and its pace, its step and its bounds;
- the world's bytes at format 9, and a format-8 world read by the rules.

`BoardSeasonTests` (harness), 4:
- a career's confidence moving match by match, its turn judging the season and setting new objectives, and the review's verdict;
- the same career twice, the same board;
- the hub's board line;
- the measurement: most clubs meeting their objective and some not, few below 20, the best paid taking more points, and clubs' reputations steady.
