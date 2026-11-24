# Development and ageing

M6.10a, #194. A footballer grows toward his potential while he's young, declines once he's old, and retires; each club's youth take his place. Over seasons a career's squads change, and the world stays at its level.

The parts:
- **`Development`** (`Eleven.Career`): a matchday's growth, and the season's turn.
- **`DevelopmentRules`:** the knobs, all technical references, not tuned feel.
- **`CareerDevelopment`** (harness): the measurement, `eleven-sim development [seasons]`.

## What the research gives

- **Peak:** a footballer peaks in his mid-to-late twenties. Dendir (2016), over Europe's top leagues, places it at about 25–27.
- **Physical first:** pace and stamina fall first, from about 30. Technique holds longer, and reading the game longest.
- **Growth:** fastest in the late teens, slowing toward the peak.
- **Keepers** are generally held to peak and decline later. That's a knob here, `KeeperLater`, not a finding.

## A matchday's growth

`Development.Matchday`, from `CareerSeasons.PlayMatchday` after the matchday's cards and injuries:
1. **Starts:** each club's XI that matchday counts a start each, `Player.Starts`.
2. **Growth:** every footballer short of his peak and his potential grows by a matchday's share of his season's growth, `SeasonGrowth`. It's in level points: his level is the mean of his outfield attributes, or of his keeping for a keeper (`Development.Level`, the selection's level, #179).

| Factor | Rule | Knob, reference |
| --- | --- | --- |
| Age | Full to 19, falling straight to none at 24 | `YoungTo` 19, `PeakFrom` 24 |
| Rate | 0.85 level a season at full, at Normal training | `GrowthYoung` 0.85 |
| Training (#196) | His club's intensity: ×0.8 Light, ×1.25 Hard | `TrainingRules.Growth` |
| His club | His manager's mean of `coaching` and `development`: ±30 % from 1 to 20 | `CompetenceSpread` 0.3 |
| Playing | +50 % for a footballer who starts every match his club has played, in proportion | `StartsBonus` 0.5 |

**Kept exactly:** growth accrues as attribute points in `Player.Progress`.
- **Spent whole:** each whole point goes to one of the attributes his level counts, below 20, drawn by his first position's profile from the generator (#157). A striker's go most to his shooting, a centre back's to his defending and in the air; a keeper's to his keeping.
- **Never past his potential:** a point is spent only while his level stays at or under his `Potential`. At his potential, growth stops and the fraction is dropped.
- **Counted:** `SeasonPoints` counts his points this season.

**Starts, not minutes:** the issue asked for minutes. A quick-simmed result names only the footballers it gives a card or an injury, so a whole XI's minutes aren't known for most matches. Each fixture's XI is, from its lineup.

## The season's turn

`Development.Rollover`, from `SeasonEngine.Rollover` after the off-season's days:
1. **Decline,** by the age the season was played at, and keepers `KeeperLater` (3) years later:

   | Kind | Attributes | From | Points an attribute a season |
   | --- | --- | --- | --- |
   | Physical | acceleration, top speed, agility, balance, stamina, strength, jumping, keeper's reflexes | 30, `DeclineFrom` | 1.0, `PhysicalDecline` |
   | Technical | the rest | 32, `TechnicalFrom` | half: 0.5 |
   | Mental | positioning, anticipation, vision, decisions, composure, keeper's positioning | 34, `MentalFrom` | a quarter: 0.25 |

   A fraction of a point is lost by a draw, never below 1.
2. **Ageing:** every footballer is a year older. His season's points become `LastSeasonPoints`, and his starts start again.
3. **Retirement,** by his new age: from `RetireFrom` 34, or `KeeperRetireFrom` 37 for a keeper.
   - **The chance:** `RetireChance` (25 %) at the first age, rising as much each year, so certain by 37, or 40 for a keeper.
   - **Gone:** a retired footballer leaves the world, his squad and his club's picks. If he was in your picked XI, your picks go back to the AI's, so nobody from the bench slides into the XI in his place (Codex on #195). A retired bench pick just leaves the bench.
   - **Remembered:** `World.LastRetired` keeps who retired at the last turn, for its review.
4. **Youth:** each club fills its squad back to 23.
   - **Position:** each youth takes the position its squad lacks most against the generator's template, so a keeper replaces a keeper.
   - **Age:** 16–18, `YouthMin`, `YouthMax`.
   - **Level:** drawn like the generator's footballers, `WorldGenerator.Youth`, at the club's division's level less `YouthBelow` (3), ±1 by his manager's `development`.
   - **Potential:** as the generator gives the young, 1 to 5 above his level.
   - **Ids:** each takes `World.NextPlayerId` and moves it on, so no retired footballer's id is used again.
   - **Marked:** `Joined` is the season he joined.

**Deterministic:** every draw is on the development stream's counters (INV-6), keyed by the world's seed and the season:
- a point, by the footballer and his points that season;
- a decline, by the footballer and the attribute;
- a retirement, by the footballer;
- a youth's age, by his id.

## Saved

The world's bytes at format 6 ([world.md](world.md)):
- the next footballer's id;
- each footballer with any development, his progress, starts, this season's and last season's points, and the season he joined;
- those who retired at the last turn.

A format-5 world reads with none accrued, and its next id past its highest.

## The hub

- **Squad's rows** show his age: "J. Smith, 23 · CB, DM · 14.2 · 96" ([squads.md](squads.md#your-squad)).
- **The season's review** (`CareerHub.ReviewLine`), from the season's turn until your club's first match of the new one: "Last season: most improved A. Name +1.3, B. Name +0.9. Retired: C. Name, 35. From the youth: D. Name, 17. Released: E. Name. Signed: F. Name. Revenue 9,800k, wages 6,900k. 4th, 6th expected: objective met. Confidence 74. Reputation 13, up 1." The releases and signings are #201's ([contracts.md](contracts.md)), the money #198's ([finances.md](finances.md)), the board's verdict #200's ([board.md](board.md)).
  - **Most improved** are your three with the most points last season, decline included, as levels.

## Measured

`eleven-sim development 3`, a headless career from seed `0x194`, the human at the first club, every match quick-simmed, every club training on Auto (#196, [training.md](training.md)), its contracts renewed and released at each turn (#201, [contracts.md](contracts.md)). The level and the bands are the clubs' footballers', the free agents left out; "Retired" is the clubs'.

| Season | Mean level | Its drift from the start | Retired at its turn | Youth | Squads |
| --- | --- | --- | --- | --- | --- |
| 1 | 11.56 | 0.00 | 0 | 0 | 23–23 |
| 2 | 11.73 | +0.17 | 6 | 32 | 23–23 |
| 3 | 11.71 | +0.15 | 14 | 74 | 23–23 |

By age band at each season's start, count, then mean level ± spread:

| Age | Season 1 | Season 2 | Season 3 |
| --- | --- | --- | --- |
| 16–18 | 88: 11.6 ± 2.6 | 71: 10.6 ± 3.0 | 101: 8.9 ± 2.4 |
| 19–21 | 141: 11.7 ± 2.7 | 147: 12.9 ± 2.5 | 142: 13.3 ± 2.8 |
| 22–24 | 140: 11.3 ± 2.5 | 145: 11.7 ± 2.5 | 143: 12.4 ± 2.7 |
| 25–27 | 148: 10.9 ± 2.4 | 130: 11.0 ± 2.6 | 131: 11.5 ± 2.6 |
| 28–30 | 148: 11.8 ± 2.4 | 144: 11.5 ± 2.2 | 142: 11.2 ± 2.2 |
| 31–33 | 143: 11.5 ± 2.5 | 138: 11.4 ± 2.4 | 111: 11.8 ± 2.5 |
| 34+ | 20: 15.6 ± 2.9 | 53: 13.0 ± 3.4 | 58: 12.7 ± 2.8 |

Five footballers followed, age then level:

| Followed | Season 1 | Season 2 | Season 3 |
| --- | --- | --- | --- |
| W. Ulvström, wide midfield | 17: 12.74 | 18: 13.70 | 19: 14.70 |
| D. Ostard, centre back | 23: 13.96 | 24: 14.22 | 25: 14.22 |
| W. Froquist, centre back | 29: 13.17 | 30: 13.17 | 31: 12.87 |
| P. Dalenko, full back | 33: 12.22 | 34: 11.65 | retired |
| Z. Wesström, keeper | 34: 18.67 | 35: 18.33 | 36: 17.67 |

- **The world holds its level.** Over six seasons its mean drifts at most +0.17 (+0.17, +0.15, +0.07, −0.01, −0.04). The generator's world is young-heavy, so it rises first.
  - **Before contracts (#201),** it drifted +0.18, +0.20, +0.11, +0.03, −0.03, and retirements built to about 50 a season, the youth coming in at 9 or so.
  - **With contracts,** the clubs release about 30 at the first turn and 80–90 at the next, mostly the old and the least ([contracts.md](contracts.md)). So fewer retire from a club, 14–30 a turn after the first, and the youth replace both: 63–77 a turn after the first, and the places mid-season sales left (#203). The released are replaced by youth below them, so the level eases.
- **The references were chosen by it.**
  - **In #194,** without training, growth 1.2 and decline 0.8 drifted +0.32 by the third season, past the issue's ±0.3, and growth 1.0 and decline 1.0 stayed within it.
  - **With training (#196),** CPU managers train Hard in most full weeks, and growth 1.0 drifted +0.34. At 0.85 the world stays within it, as above.
- **The generator's world isn't in a steady state:** ages are drawn evenly from 17 to 33, so few retire after the first season and more after the second.
- **The save grows:** after three seasons the world is 109,185 bytes, from 95,841 without development (#191), and a save is about 138,000 characters.
  - **Eleven saves** would come to about 1.52M, within iOS Safari's 2.5M but past the half `CareerSaveTests` keeps for presets and captures at a season's end.
  - **For M6's gate** (M6.16) to weigh, with the memories' (#191).

## Tests

`DevelopmentTests` (career), 9:
- growth by age, his manager's competences and his starts;
- none past his potential, and none from the peak;
- a season's growth kept to the fraction, and spent by his position's profile;
- a matchday's starts;
- at the season's turn every footballer older, the older declined by kind, keepers 3 years later;
- the oldest retired, each squad filled again from the youth with fresh ids, keepers for keepers;
- retirement's chance at its first age;
- the same career twice, the same footballers;
- the world's bytes at format 6, and a format-5 world read with none accrued.

`DevelopmentSeasonTests` (harness), 4:
- a career matchday counting each XI's starts and growing the young, and none past 24;
- three seasons rolled forward within ±0.3 of the start, retirements replaced, every squad 23;
- the hub's review naming your risers, retirements and youth until your first match;
- a squad row's age.
