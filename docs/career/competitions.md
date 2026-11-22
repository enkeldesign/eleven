# Competitions

The career's competitions as data ([#162](https://github.com/enkeldesign/eleven/issues/162)), in `Eleven.Career` (`Competitions.cs`). This page covers the rules, the league fixtures, the cup and the season's calendar. Playing them, with tables, promotion and relegation and rollover, is M6.05's season engine.

## The rules

`CompetitionRules.Reference` holds #80's shape:

| | |
| --- | --- |
| Divisions | 3 of 12 clubs (`CompetitionId.Division1`–`3`) |
| League | Each pair meets twice, once at each ground: 22 rounds |
| Points | 3 for a win, 1 for a draw, 0 for a loss |
| Movement | 2 up, 2 down |
| Cup | 36 entrants, single-leg ties, extra time and penalties when level (the simulation's knockout), a neutral final |
| Cup rounds | Preliminary round, Round of 32, Round of 16, Quarter-finals, Semi-finals, Final |

Every value is data, ready for M7's world editing.

## League fixtures

`LeagueFixtures.Generate(clubs, seed, season, division)` makes a double round robin by the circle method:

- **Rounds:** every club plays once a round. With 12 clubs, that's 22 rounds of 6 matches.
- **Grounds:**
  - every club meets every other once at home and once away, so 11 home matches each;
  - the second half is the first with the grounds swapped: round r + 11 is round r reversed.
- **Never three in a row at one ground:**
  - in the first half, each club plays at most one pair of matches at the same ground;
  - the half's rounds are turned until no club plays three in a row at home or away across the swap into the second half. For 12 clubs that's a turn of two rounds.
  - The reference season has 30 such pairs, the fewest a mirrored double round robin of 12 clubs can have (3n − 6).
- **Variety:** the seed, the season and the division shuffle which club takes which place, so seasons differ. The shuffle is on the competition stream.
- **Size:** any even league works. Four clubs can't avoid three at one ground; their schedule is still complete and balanced.
- **Speed:** a season's fixtures take 0.05 ms.

A club's grounds over the reference season (division 1 of world `0x157`, seed `0x162`):

```
ROS HAHAHAHAHHAAHAHAHAHAAH
ZOR HAHAHAHAHAHAHAHAHAHAHA
PRY AHHAHAHAHAHHAAHAHAHAHA
```

## Fixture ids

A fixture's id is `((season × 16 + competition) × 64 + round) × 64 + place`:
- stable: the same match always has the same id;
- unique: no two matches share one, for rounds and places below 64.

Saves (M6.07) and the season's records key on it.

## The cup

**`Cup.FirstRound(ranked)`** takes the entrants ranked strongest first. As many of the lowest-ranked as it takes to leave a power of two play a preliminary round. With 36 entrants, the eight lowest play four ties for the last places in the round of 32, and the other 28 have byes. The season engine supplies the ranking: last season's league positions.

**`Cup.Draw(clubs, seed, season, round)`** pairs a round's clubs at random:
- **Home:** the first drawn is at home.
- **The final:** when two clubs are left, the tie is the final, at a neutral ground.
- **Its own randomness:** each round, season and seed draws on its own counter, so drawing one round never moves another (INV-6).

From 36 clubs that makes six rounds and 35 ties: 4, 16, 8, 4, 2 and 1.

**`Cup.Winner(tie, result)`** reads a tie's winner from the match-result contract (INV-14): by goals, then by the shootout. A level result is refused, since a cup tie is always played to a winner.

## The calendar

`SeasonCalendar.Of(rules)` lists the season's 28 matchdays in order. A league matchday plays that round in every division. Cup round c follows league round ⌊c × 22 / 6⌋, and the final follows the last league round:

```
L1 L2 L3 C1 L4 L5 L6 L7 C2 L8 L9 L10 L11 C3 L12 L13 L14 C4 L15 L16 L17 L18 C5 L19 L20 L21 L22 C6
```

## Randomness

All of it is on `RandomStream.Competition` (13), counter-based:
- **A league's places:** the season is the tick and the division is the entity.
- **A cup round's draw:** season × 64 + round is the tick, with the cup as the entity.

No other stream's values move.

## Tests

`CompetitionTests` cover:
- the rules;
- the rounds, the meetings and grounds, the three-in-a-row rule, the mirror, the seed's shuffle and stable ids;
- the preliminary round and byes, each round's draw, the cup's six rounds to a neutral final, a tie's winner with its shootout;
- the calendar.
