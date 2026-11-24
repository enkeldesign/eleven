# A club's money

M6.11a, #198. Each club has a balance. Its footballers earn wages, and it earns broadcasting, matchday takings, merit and cup money. Every k is booked to one line of a ledger, so the world's money is conserved. M6.11b's board sets objectives on it, and M6.12's transfer market spends it.

The parts:
- **`Finance`** (`Eleven.Career`): wages, the days' money, a matchday's takings, the season's end and the wage budget.
- **`FinanceRules`:** the knobs, all technical references, not tuned feel.
- **`CareerFinances`** (harness): the measurement, `eleven-sim finances [seasons]`.

The currency is fictional, in whole thousands, "k", with no exchange rate. Its sums are chosen for their ratios, not any real league's.

## What the research gives

- **Wages are most of a club's spending.** Deloitte's Annual Review of Football Finance puts wages at about 60–70 % of revenue in Europe's top divisions, and above 100 % in some second tiers. UEFA's squad-cost rule caps it at 70 %.
- **Where revenue comes from:** in a top division mostly broadcasting, much of it paid by league position, then commercial income and matchday. Lower divisions earn far less, and more of it on matchdays.

## Wages

`Finance.Wage`, a footballer's weekly wage in k: `WageBase` × exp(`WageSlope` × (level − 10)) × his club's division's share × his age's share, at least 1.

| Knob | Reference |
| --- | --- |
| `WageBase` | 4, at level 10, his peak, in the second division |
| `WageSlope` | 0.25: about ×1.28 a level |
| `DivisionWage` | ×1.6, ×1.0, ×0.6 for divisions 1, 2, 3 |
| `YoungWage`, `NearPeakWage` | ×0.5 under 21, ×0.8 to 23 |

- **Who's paid:** the generator pays every footballer, and a youth (#194) joins on the same rule.
- **A wage holds** until M6.12's contracts change it, whatever his development or his club's division.

## Revenue and costs

| Line | When | Reference, by division 1, 2, 3 |
| --- | --- | --- |
| Wages | each day between matchdays, and the off-season's | each footballer's weekly wage over 7 |
| Broadcasting | each day | 48, 10 and 3k a day |
| Matchday | each home match, league or cup; none at the final's neutral ground | 250, 130 and 50k, 6 % more a point of the club's reputation above 10, which moves at each season's turn (#200) |
| Merit | the season's end | 3,000, 900 and 300k for the champion, a twelfth less a place below |
| Cup | the season's end | 40k a round reached, and 200k more for the winner |

- **Booked once:** every k goes to the club's balance and to one line of its season's `Ledger`.
- **The season's turn:** at it, the season's ledger becomes `LastSeason`.
- **Conserved:** a season's lines sum to its change in the balance. Then the off-season's wages and broadcasting start the next one.

## The wage budget

`Finance.WageBudget`, the board's weekly wage budget: `WageShare` of last season's revenue, by division 0.70, 0.85 and 0.95 (#201; 0.65 everywhere in #198).
- **By division:** the top's is UEFA's squad-cost cap, and lower divisions run higher wage ratios. At 0.65 everywhere the lower divisions began a third over budget ([contracts.md](contracts.md#two-changes-to-the-money-198)).
- **The first season:** a season's expected revenue, its broadcasting, eleven home matches, a mid-table merit and two cup rounds.
- **Over the weeks it was earned in:** `Ledger.Days` counts the days a ledger's wages and broadcasting were paid for, and the budget divides by them over 7. A season is 217 days, 31 weeks (`Finance.SeasonDays`), not a year's 52. A first season has no off-season before it, so it's 157 days. #198 divided it by 217, and the second season's budget read 28 % low.
- **Enforced by contracts (#201):** a renewal or a free signing must keep the wage bill within it.

## The hub

- **The finance line:** "Balance 1,240k. Wages 41k a week of a 45k budget."
- **The season's review** (#194) adds last season's revenue and wages.

## Saved

At world format 8 ([world.md](world.md)):
- each club in order, its balance and this season's and last season's ledgers;
- each footballer's weekly wage in two bytes.

A format-7 world reads each club's opening balance and each footballer's wage by the rule.

## Measured

`eleven-sim finances 3`: a headless career from seed `0x198`, every match quick-simmed, each season turned so its merit is paid. Since #200 a club's reputation moves at each turn, and its takings with it; since #201 contracts renew and release at each turn, and a relegated club's wages fall. Seasons 2 and 3 are theirs. Balances are at each season's start, the ledger the season's own, by the division each club played it in.

| Season | Division | Balance at its start | In deficit | Wages over revenue | Broadcasting | Matchday | Merit | Cup |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 | 5,000k ± 0k | 0 | 0.67 | 58 % | 28 % | 12 % | 1 % |
| 1 | 2 | 1,500k ± 0k | 0 | 0.80 | 42 % | 42 % | 13 % | 3 % |
| 1 | 3 | 400k ± 0k | 0 | 0.89 | 36 % | 44 % | 13 % | 7 % |
| 2 | 1 | 7,534k ± 3,583k | 0 | 0.71 | 65 % | 23 % | 11 % | 1 % |
| 2 | 2 | 3,079k ± 3,526k | 1 | 0.85 | 52 % | 35 % | 10 % | 2 % |
| 2 | 3 | 588k ± 728k | 2 | 1.12 | 47 % | 37 % | 11 % | 5 % |
| 3 | 1 | 11,316k ± 3,368k | 0 | 0.69 | 65 % | 24 % | 10 % | 1 % |
| 3 | 2 | 4,218k ± 5,995k | 2 | 0.73 | 53 % | 34 % | 10 % | 3 % |
| 3 | 3 | 958k ± 1,267k | 2 | 1.06 | 48 % | 37 % | 11 % | 5 % |

- **Near the research:** the top division's wages are 0.67–0.76 of its revenue, its broadcasting 58–65 %. The second division's are 0.78–0.98, and the third's 0.89–1.25.
- **The lower divisions' high ratios come from movement:** a relegated club keeps its higher division's wages and earns its new division's revenue. M6.12's contracts and sales are its way out.
- **Most clubs stay solvent:** at most 3 of a division's 12 are in deficit by the third season.
- **The top division banks its surplus,** about 2,500k a club a season. M6.12's transfers will spend it.
- **The references were chosen by it.** With 35k a day and 400k a home match, the top division earned 38–45 % on matchdays, against the research's broadcasting-led revenue.
- **The save grows:** after three seasons the world is 112,853 bytes and a save about 143,000 characters, from 138,000 (#194). Eleven saves would come to about 1.58M.

## Tests

`FinanceTests` (career), 6:
- a wage by level, age and division, and the generator paying everyone;
- the days' wages and broadcasting, each on its line;
- a home match's takings by division and reputation, the away side's none;
- the season's merit by place and cup by round, its ledger becoming last season's;
- the wage budget over the season's weeks;
- the world's bytes at format 8, and a format-7 world read by the rules.

`FinanceSeasonTests` (harness), 4:
- a season and its turn conserving every k;
- the same career twice, the same money;
- the hub's finance line;
- the measurement's ratios by division, and most clubs solvent.
