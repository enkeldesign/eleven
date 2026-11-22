# The season

The season engine ([#164](https://github.com/enkeldesign/eleven/issues/164)) plays #162's competitions ([competitions.md](competitions.md)). It lives in `Eleven.Career` (`Season.cs`); the harness plays fixtures headless (`CareerSeasons`).

## A season, as plain data

`Season` holds:
- **its number and seed:** the seed comes from the career's seed and the number, on the competition stream, so the same career has the same seasons;
- **each division's clubs,** division 1 first;
- **the cup's ranking,** strongest first;
- **the fixtures:** the league's, then the cup's ties as each round is drawn;
- **every result,** by fixture id: the match-result contract (INV-14), from the full simulation or the quick-sim, never from anywhere else;
- **the next matchday,** its place in the calendar;
- **the clubs still in the cup,** and the winner once there is one.

Saving it is M6.07's.

## Matchdays

`SeasonEngine` turns the calendar's 28 matchdays ([competitions.md](competitions.md#the-calendar)) one at a time:

- **`Next`** gives the next matchday's fixtures: a league round in every division (18 matches), or a cup round. A cup round is drawn the first time it is asked for: the preliminary round from the ranking's eight lowest, every later round from the clubs still in.
- **`Record`** takes a result for one of them. It refuses a fixture of another matchday, and a level cup tie: a tie is always played to a winner.
- **`Advance`** moves on once every fixture of the matchday has its result. After a cup round, its winners go through, with the 28 byes after the preliminary round, and the last one left wins the cup.

## Tables

`Table(season, division)` gives every club's played, won, drawn, lost, for, against and points (3/1/0). The clubs are ordered by:
1. points;
2. goal difference;
3. goals scored;
4. head to head among the clubs still level: points, goal difference and goals in their matches against each other;
5. a lot drawn on the competition stream from the season's seed.

## The end, and rollover

**`Movement`** gives the season's moves: the top two of divisions 2 and 3 go up, and the bottom two of divisions 1 and 2 go down.

**`Rollover`** applies them to the world's clubs and starts the next season:
- its number is one more;
- its seed is new;
- its cup is ranked by the final tables: division 1's, then 2's, then 3's.

Rollover refuses a season that isn't over.

## Headless seasons

`CareerSeasons` (harness) plays a season with the quick-sim for the world's clubs (#163):
- **League matches** are league matches.
- **Cup ties** are knockouts, so extra time and penalties are played when level.
- **The final** is at a neutral ground, without home advantage.
- **Seeds:** each fixture's seed is the season's seed hashed with the fixture's id, so a result can be replayed alone.
- **The hook:** `PlayMatchday` takes one, which plays chosen fixtures another way. M6.06 uses it to play the human's fixture with the full simulation.

`RollForward` plays a new career's seasons in turn and rolls each into the next.
