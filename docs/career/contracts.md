# Contracts

M6.12a, #201. Each footballer has a contract, the seasons he has left at his club. At each season's turn every contract runs a season down. A club renews the ending ones it wants, within its board's wage budget, and releases the rest into the free agents. Clubs short of a position sign the best free agents they want and can afford. A free agent still unsigned a season later retires. M6.12b's transfer market will move footballers between clubs for fees, and M6.12c lets you choose for your own club.

The parts:
- **`Contracts`** (`Eleven.Career`): the generator's and a renewal's lengths, whom a club wants, and the turn.
- **`ContractRules`:** the knobs, all technical references, not tuned feel.
- **`CareerContracts`** (harness): the measurement, `eleven-sim contracts [seasons]`.

## What the research gives

- **Contracts run one to five years.** A club usually extends a footballer it wants with a year or two left, rather than let him run down.
- **At a contract's end he's free to leave** without a fee (the Bosman ruling, 1995). An unwanted one is released.
- **Wages follow the market:** a renewal is paid what his level and age command where he plays. Older footballers take shorter contracts.
- **Relegation clauses:** contracts in English football commonly cut wages by a quarter to a half on relegation.

## A contract

`Player.ContractYears`, the seasons left, this one included.

| Who | Length | Reference |
| --- | --- | --- |
| The generator's, under 24 | 2 to `YoungLongest` | 5 |
| The generator's, 24–29 | 1 to `PrimeLongest` | 4 |
| The generator's, 30 and over | 1 to `OldLongest` | 2 |
| A youth's first (#194) | `YouthYears` | 3 |
| A renewal or a free signing (`RenewYears`) | by age: under 24, under 28, under 31, older | 4, 3, 2, 1 |

The generator draws each length on the footballer's own counter, the world stream's fifth draw (`Contracts.FirstContract`).

## Whom a club wants

`Contracts.Wanted`, for the division the club will play in:
- **a prospect:** `ProspectAge` (21) or younger, with `ProspectRoom` (2) or more between his level and his potential, always;
- **a veteran:** from `VeteranFrom` (32), only at the division's level or above;
- **anyone else:** at `KeepMargin` (1) or more above the youth that would replace him (`Development.YouthLevel`, #194): the division's level, less `YouthBelow`, by the manager's development.

## The turn

`Contracts.Turn`, at each season's turn, after its money and the board's verdict, before the off-season and development:
1. **A relegated club's relegation clause:** each wage falls `RelegationCut` (1, all the way) of the way to the new division's rate by #198's rule. The rule's 1.6, 1.0 and 0.6 make it a 37–40 % cut.
2. **Each contract a season down.** Of those ending, the club releases the ones it doesn't want first, so their wages are off the bill (Codex on #204). Then the wanted, its best first, are:
   - **renewed** if the club wants him and the board's budget holds. He's paid #198's rule wage for his level, age and new division, for `RenewYears` by his age;
   - **released** otherwise, into the free agents. He leaves the squad, and a club's picks (#179) go back to the AI's if he was in its XI.
3. **The budget holds:** a renewal must keep the weekly wage bill within `Finance.WageBudget` at the club's next division's share (Codex on #204), or be for no more than he was paid.
4. **The pre-season transfer window** (#203, [transfers.md](transfers.md)), with the room the releases left.
5. **The free agents signed:** the clubs in order of their next division, then reputation. Each club short of the squad's 23 signs the best free agent it wants and can afford, on the rule wage for `RenewYears`. It looks for the position its squad lacks most first, then each other it's short of (Codex on #204). A club doesn't re-sign one it released this turn.
6. **The rest stay free:** `ClubId` 0 and `FreeSince` the season. A free agent doesn't grow (#194), and at the next turn's development, still unsigned, he retires.

Then development's turn (#194) ages and retires, and the youth fill each squad to 23.

## Two changes to the money (#198)

Contracts measured the budget, and two things in it were wrong:
- **The budget's share is by division:** `WageShare` 0.70, 0.85 and 0.95.
  - **Why:** the top's is UEFA's squad-cost cap, and lower divisions run higher wage ratios (Deloitte).
  - **Before:** at 0.65 everywhere, the lower divisions began a third over budget. Then they released their young for want of raises: 87 free agents retired unsigned by the third turn.
- **A ledger counts its days** (`Ledger.Days`), and the budget divides last season's revenue by them.
  - **Why:** a first season has no off-season before it, so it's 157 days, not 217, and the second season's budget read 28 % low.
  - **The effect:** in the first turn's measurement, wage bills went from 1.24–1.32 times their budgets to 0.91–0.97.

## The hub

- **The contract line,** when any contract of yours ends this season: "Out of contract this season: A. Name, B. Name."
- **The season's review** (#194) adds "Released: A. Name. Signed: B. Name."

Choosing whom to renew and sign is M6.12c's. Until then the AI's rule decides for your club too.

## Saved

At world format 10 ([world.md](world.md)):
- each footballer's contract in a byte and the season he went free in two;
- each club's two ledgers' days;
- the last turn's moves, each with the footballer's id, the clubs he left and joined (0 the free agents) and his name.

A format-9 world reads each footballer's contract by the generator's rule at his age, nobody free. Its ledgers' days aren't in it. A ledger with money already booked, a world saved mid-season, has its days unknown (`Ledger.UnknownDays`). It stays so as days pass, and its budget takes a whole season's weeks until a new season counts its own. An empty ledger counts from 0 (Codex on #204).

## Measured

`eleven-sim contracts 3`: a headless career from seed `0x201`, every match quick-simmed, each season turned.

**Each turn's contracts ending,** by the club's next division and the footballer's age:

| Turn | Division | Renewed under 24 | Renewed 24–29 | Renewed 30 and over | Released under 24 | Released 24–29 | Released 30 and over | Signed free |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 | 0 | 14 | 17 | 0 | 3 | 12 | 2 |
| 1 | 2 | 0 | 22 | 22 | 0 | 5 | 9 | 3 |
| 1 | 3 | 0 | 19 | 23 | 0 | 0 | 4 | 3 |
| 2 | 1 | 11 | 22 | 37 | 11 | 6 | 24 | 11 |
| 2 | 2 | 9 | 21 | 34 | 12 | 5 | 6 | 3 |
| 2 | 3 | 17 | 18 | 34 | 5 | 2 | 13 | 1 |
| 3 | 1 | 9 | 13 | 34 | 6 | 6 | 20 | 2 |
| 3 | 2 | 10 | 27 | 28 | 9 | 1 | 15 | 3 |
| 3 | 3 | 10 | 18 | 38 | 4 | 6 | 9 | 7 |

**After each turn:**

| Turn | Free agents | Retired unsigned | Wage bill over budget, division 1: mean, highest | Division 2 | Division 3 | Squads |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 23 | 2 | 0.96, 1.13 | 0.89, 1.19 | 0.86, 1.18 | 23–23 |
| 2 | 64 | 31 | 0.99, 1.17 | 0.96, 1.22 | 1.01, 1.21 | 23–23 |
| 3 | 61 | 68 | 0.96, 1.39 | 0.88, 1.18 | 0.98, 1.27 | 23–23 |

- **Most contracts ending are renewed:** seven to eight in ten. The released are mostly the oldest and the least, and the young without room to grow: 33 at the first turn, then 84 and 76.
- **Squads stay whole:** 23 at every club, the youth filling what releases and retirements leave.
- **Wage bills sit at their budgets:** a division's mean is 0.86–1.01 of its budget, over three seasons and over six.
- **The highest:** before the transfer market (#203), 1.68 at the third turn was club 9's, two seasons after relegation from the top. The relegation clause put its wages at the second division's rate, but its footballers were better than that division's and its revenue was the division's. With the market it sells: the highest is 1.39 over three seasons, and 1.63 over six.
- **Free agents:** 23 after the first turn, then 57–66. Of those unsigned a season, 31, then 63–68, retire. Clubs sign only those above their youth, and the budget holds them back.
- **The world's level holds:** development's drift by the third season is +0.12 (#194, [development.md](development.md)), and training's on Auto is +0.13 ([training.md](training.md)).

## Tests

`ContractTests` (career), 11:
- the generator's lengths by age, a renewal's by age, and a youth's first;
- a contract run down, and one ending renewed or released, its move kept;
- whom a club wants: its prospects, those above their replacement, its veterans only at its level;
- the budget holding: a renewal over it releases him, and one for less is kept;
- free agents signed by a club short of a position within budget, and one unsigned since an earlier turn retiring;
- every footballer at one club, a free agent or retired, every squad whole;
- format 10, and format 9 read by the rules;
- a promoted club's renewals held to its new division's budget (Codex on #204);
- a club short of two places signing for the second when the first has nobody (Codex on #204);
- a world from before the ledgers' days marking a booked ledger's unknown, and budgeting it as a whole season (Codex on #204);
- a club releasing its unwanted before it budgets its renewals (Codex on #204).

`ContractSeasonTests` (harness), 4:
- a career's turn releasing a poor footballer out of contract, every squad whole, and the hub's and review's lines;
- the same career twice, the same contracts;
- the hub's line empty when no contract ends;
- the measurement: squads whole, more renewed than released, and wage bills near their budgets.
