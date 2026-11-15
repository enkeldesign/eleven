# The 5v5 league

Issue #103, M3's gate. Six fictional sides play a double round robin at five a side. It tests that whole 5v5 matches finish, that nothing gets stuck, what the referee gives, and that results spread by tier. It doesn't test the standings.

Every side's values are untuned technical references, and so are the bounds below.

## The sides

`League.Sides()`, weakest tier first. Every footballer is a CPU footballer at his side's tier (#83); a side's outfielders share its capabilities, and the keepers are the reference's.

| Side | Tier | Capabilities |
| --- | --- | --- |
| Harbour | apprentice | the reference |
| Millbrook | club | the reference |
| Stonegate | club | strong: strength, tackling and stamina 0.8 |
| Riverside | pro | the reference |
| Northfield | pro | quick: top speed 7.4 m/s, ball control 0.8 |
| Kestrel | elite | the reference |

Each side is at home to every other once: 30 matches at the reference halves, each on its own seed (`0x103000000` plus home × 6 plus away). Home kicks off.

## What a match records

- The score, and whether the match reached full time.
- Fouls, advantages played, and the restarts awarded: penalties, free kicks, corners, throw-ins and goal kicks.
- The longest stretch in play in which:
  - an outfielder stayed within 1 m of one place ("still");
  - a side's outfielders were all within 2 m of each other ("bunched");
  - nobody touched the ball ("untouched").

A match is **stuck** if it doesn't finish, or an outfielder is still or the ball untouched for over 30 s in play.

## The gate

`eleven-sim league <dir>` writes `league-matches.csv` and `league-table.csv`. It fails if any match is stuck, or if the elite tier takes no more points a match than the apprentice tier. CI runs it and diffs the matches against `docs/simulation/league-reference.csv`.

## Measured

At config `806d80eae53da2e8`, all 30 matches finished and none was stuck.

| Per match | |
| --- | --- |
| Goals | 3.70 |
| Fouls | 6.70 |
| Free kicks | 1.13 |
| Penalties | 0.47 |
| Corners | 4.37 |

The longest stretches over the league:
- still, 4.1 s;
- bunched, none;
- untouched, 6.5 s.

| Tier | Points a match |
| --- | --- |
| apprentice | 0.40 |
| club | 1.50 |
| pro | 1.45 |
| elite | 2.50 |

The table runs Kestrel 25, Stonegate 18, Northfield 16, Riverside 13, Millbrook 12, Harbour 4.

- **The strong club side finished second,** above both pro sides. Body and tier both count; neither is tuned.
- **Fewer free kicks than fouls:** most fouls are played on as advantage (#99), and fouls in the area are penalties.

## The human's restarts at five a side

The switcher (#93) takes the human over his side's taker when a restart is placed. A free kick or a penalty then waits for his own kick through the ACTION surface, and is taken for him only when the wait runs out (#85). The CPU's are taken as soon as the set-up allows. `HumanRestartTests` has both.
