# The transfer market

M6.12b, #203. Clubs buy and sell footballers under contract, for fees, in two windows a season. A buyer improves the weakest place in its XI with the best footballer it can afford from its division or below, weighing quality against price. A club over its wage budget lists its highest-paid outside its XI. Each fee moves from the buyer's balance to the seller's, so the world's money is conserved, and the footballer moves with a new contract. M6.12c gives your club its own market. Until then your club neither buys nor sells.

The parts:
- **`Transfers`** (`Eleven.Career`): a footballer's value and asking price, a club's XI, and the window.
- **`TransferRules`:** the knobs, all technical references, not tuned feel.
- **`CareerTransfers`** (harness): the measurement, `eleven-sim transfers [seasons]`.

## What the research gives

- **Two windows:** FIFA's registration periods, a long one between seasons and a short one mid-season.
- **A fee follows the footballer's value and his contract.** Fee studies of English and European transfers find a fee rises with ability and with the clubs' divisions. It peaks for footballers in their mid-twenties and rises with the contract's years left (Carmichael and Thomas, 1993; Dobson and Gerrard, 1999; Frick, 2007).
- **Money flows down the divisions,** footballers up.
- **Clubs over their wage budget sell** to bring it down.

## A footballer's value

`Transfers.Value`, in k:

`ValueBase` × exp(`ValueSlope` × (level − 10)) × his club's division's share × his age's × his contract's.

| Knob | Reference |
| --- | --- |
| `ValueBase` | 320k at level 10, his peak, in the second division, with three years left |
| `ValueSlope` | 0.25: about ×1.28 a level, as #198's wage |
| `DivisionValue` | ×1.6, ×1.0, ×0.6 for divisions 1, 2, 3 |
| By age | under 21: ×(1 + 0.15 a level of room to his potential), at most ×1.6; 21–27: ×1; from 28: ×0.75; from 30: ×0.5; from 32: ×0.25 |
| By contract | one year left: ×0.5; two: ×0.8; three or more: ×1 |

**Asked** (`Transfers.Asking`): his value, ×1.25 (`WantedMarkup`) if his club wants him (#201).

## A club's XI

`Transfers.Eleven`: the best by level at each place of a 4-4-2:
- a keeper;
- two centre backs and two full backs;
- two central midfielders (defensive, central or attacking);
- two wide midfielders and two strikers.

A place short of its positions is filled by the club's best left.

## The windows

- **Pre-season,** at the season's turn: after contracts' renewals and releases (#201), before their free signings, so a club has the room its releases left.
- **Mid-season,** after the league round halfway through (`MidSeason`, 0.5: round 11 of 22).

In each window:
1. **The over-budget list:** a club whose wage bill is over its budget lists its `Listed` (2) highest-paid footballers outside its XI, at their value.
2. **The buyers,** by division, then reputation, each up to `BuysPerWindow` (2):
   - **its weakest place:** its XI's lowest-level footballer;
   - **the candidates:** footballers of his position at least `UpgradeMargin` (1) level better, at a club in the buyer's division or below, or listed;
   - **what it can afford:** a fee within `SpendShare` (half) of its balance, and his new wage within its budget;
   - **what the seller lets go:** it keeps `SquadMinimum` (20) and another of his position, two keepers;
   - **its choice:** the best level less `LevelsPerK` (0.001) a k of fee, a level a 1,000k.
3. **Room:** a squad below 23, or one footballer of the place in his contract's last season whom the club doesn't want (#201), let go.
4. **The transfer:** the fee off the buyer's balance and onto the seller's, each on the ledger's `Transfers` line. The footballer joins on #198's rule wage at the buyer's division, for #201's renewal length.

**The budgets are the next division's** at the pre-season window, as contracts' (Codex on #204).

**Transfers aren't revenue:** the `Transfers` line is kept apart from revenue, as football's accounts keep player trading, so the board's wage budget (#198) doesn't grow on sales.

## The hub

- **The window's transfers,** the dearest first: "Transfers: A. Name, ROV to WYS, 1,240k; B. Name, QUE to DRA, 380k."

## Saved

At world format 11 ([world.md](world.md)):
- each club's two ledgers' transfers lines;
- the last window's transfers, each with the footballer's id, the clubs from and to, the season, the fee and his value then, the window and his name.

A format-10 world reads with no transfers.

## Measured

`eleven-sim transfers 3`: a headless career from seed `0x203`, every match quick-simmed, each season turned.

**Transfers each window,** and their fees, by the divisions from and to:

| Season | Window | 1→1 | 2→1 | 3→1 | 2→2 | 3→2 | 1→2 | 3→3 | 1→3 | 2→3 | Fees | Fee over value |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | mid-season | 0 | 1 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 2,490k | 1.25 |
| 2 | pre-season | 6 | 0 | 0 | 3 | 1 | 0 | 6 | 1 | 0 | 7,871k | 1.24 |
| 2 | mid-season | 5 | 0 | 0 | 4 | 0 | 0 | 1 | 0 | 0 | 8,405k | 1.25 |
| 3 | pre-season | 8 | 2 | 2 | 8 | 2 | 0 | 7 | 0 | 0 | 23,680k | 1.25 |
| 3 | mid-season | 0 | 1 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 2,934k | 1.25 |
| 4 | pre-season | 9 | 2 | 1 | 6 | 3 | 0 | 10 | 0 | 0 | 23,082k | 1.25 |

**Each division's mean wage bill over budget,** before the window's matchday or turn, and after:

| Season | Window | Division 1 | Division 2 | Division 3 |
| --- | --- | --- | --- | --- |
| 1 | mid-season | 0.97 → 0.97 | 1.22 → 1.21 | 1.18 → 1.18 |
| 2 | pre-season | 0.97 → 0.91 | 1.21 → 0.86 | 1.18 → 0.85 |
| 2 | mid-season | 0.91 → 0.90 | 0.86 → 0.86 | 0.85 → 0.85 |
| 3 | pre-season | 0.90 → 0.94 | 0.86 → 0.90 | 0.85 → 1.02 |
| 3 | mid-season | 0.94 → 0.94 | 0.90 → 0.90 | 1.02 → 1.01 |
| 4 | pre-season | 0.94 → 0.93 | 0.90 → 0.92 | 1.01 → 0.97 |

- **Money is conserved:** every fee paid is received, 0k over three seasons and over six.
- **The pre-season windows carry the trade:** 17, 29 and 31 transfers before seasons 2, 3 and 4, against 3, 10 and 3 mid-season. A club has room at the turn, where its releases left it. Mid-season it has room only by letting go a footballer it doesn't want in his contract's last season.
- **Fees are mostly asked at the markup:** 1.24–1.25 of value. A club rarely sells a footballer it doesn't want, since the buyer wants an upgrade.
- **Footballers go up, money comes down:**
  - **over three seasons:** 17 transfers to a higher division and 1 to a lower;
  - **the top division's footballers bought from below:** 3 % after three seasons, 7 % after six;
  - **within a division:** most trade, the top division's dearest.
- **The price weight was chosen by it** (`LevelsPerK`). The table is three seasons, measured before the windows' order changed (below): the second season's windows' fees, and the top division's footballers bought from below:

  | `LevelsPerK` | Fees, the second season's windows | From below by the third season |
  | --- | --- | --- |
  | 0 | 14,319k and 35,391k | 1 % |
  | 0.0005 | 3,234k and 24,182k | 2 % |
  | 0.001 | 2,584k and 11,416k | 2 % |
  | 0.0015 | 3,313k and 3,430k | 3 % |

  At 0, a top club takes the best whatever the price, so its money stays in the top division. At 0.001 a level costs it about 1,000k.
  - **The windows' order came from the same measurement.** The pre-season window first ran after contracts' free signings, which had already filled the room, so it was smaller than the mid-season one. Run between the releases and the free signings, it carries most of the trade.
- **The wage bills:** the before and after of a pre-season window include contracts' turn, so the table can't separate the two. A mid-season window moves a division's mean by 0.01 at most.

## Tests

`TransferTests` (career), 8:
- a value by level, age, contract and division, and the asking price;
- a club's XI by its shape;
- a window buying for a club's weakest place from below, the fee on both ledgers, not revenue;
- a club not buying from a higher division unless it's listed over its budget;
- a buyer without the balance, the budget or the room buying none, and the human's club out of it;
- a window conserving money and footballers, every squad within its bounds;
- the mid-season window after the league round halfway through, once;
- format 11, and format 10 read with none.

`TransferSeasonTests` (harness), 4:
- a season and its turn trading, money conserved, every squad whole and the human's club out of it;
- the same career twice, the same transfers;
- the hub's line, the dearest first;
- the measurement: trading in every pre-season window and some mid-season, fees at or above values, more footballers up than down.
