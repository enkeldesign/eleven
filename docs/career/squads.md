# Who's out

Injuries and bans carried from one match to the next ([#176](https://github.com/enkeldesign/eleven/issues/176), M6.08b). Each result names its footballers (#172). What happened to them in a match keeps them out of the next ones, and nobody out is picked.

The state is the footballer's own, in the world (`Player`), so it is saved with him:
- `InjuredDays`, days out injured;
- `LeagueBan` and `CupBan`, his club's matches he must still sit out in each competition;
- `LeagueCautions` and `CupCautions`, this season's cautions toward a ban.

The logic is `Availability` (`Eleven.Career`, engine-free). The knobs are `SquadRules`, all untuned technical references.

## Injuries

A footballer's worst injury in a match (#109) puts him out for days. The draw is on `RandomStream.Squad`, keyed by the fixture and the footballer (INV-6):
1. **A band**, by the injury's level, from UEFA's elite-club injury study, which grades a layoff by days out:

   | Band | Days | Minor (he played on) | Serious (he left the pitch) |
   | --- | --- | --- | --- |
   | Slight | 1–3 | 50 % | 0 % |
   | Minor | 4–7 | 35 % | 20 % |
   | Moderate | 8–28 | 15 % | 55 % |
   | Severe | 29–90 | 0 % | 25 % |

2. **A day within the band,** uniformly.

The bands are the study's. The shares by level are references: the full simulation's minor injury is a knock he plays on with, its serious one takes him off.

- **A second injury** never shortens the first's layoff.
- **The days pass** by `DaysPerMatchday` (7) at the end of each matchday, his injury's own included, whether or not his club plays. He is out while any are left.
- **So a knock never costs a match.** A layoff of up to a week is over by the next matchday, so only a moderate or severe one is: 15 % of minor injuries, 80 % of serious ones.

## Bans

SvFF's rule for Allsvenskan, counted per competition. The league is one competition whatever the division, and the cup is the other.

- **A sending-off** bans him from his club's next `SendingOffBan` (1) match in that competition.
- **Cautions** add up over a competition's season. The `CautionsForBan`th (3rd) brings a one-match ban, then every `CautionsPerFurtherBan` (2) more: the 5th, 7th and 9th.
- **A second yellow's two cautions** don't count toward the total; its sending-off bans him. A caution before a straight red does count.
- **Served** only in his club's matches of that competition. A bye, or a match in the other competition, doesn't serve it.

## A matchday

`CareerSeasons.PlayMatchday` in order:
1. **Who's out** of each club's match is read before any fixture is played, for the matchday's competition.
2. **Each XI** is picked without them (`CareerSquads.Selection`), for the full simulation and the quick-sim alike. With no fit keeper, the best of the rest at keeping goes in goal. The bench is the best of the rest, up to nine; with footballers out it can be shorter, and the match has only those places (Codex on #185).
3. **The fixtures are played** and recorded, each with its lineup: simulation id to the world's footballer. The human's fixture brings its own lineup (`CareerSession.LineupFor`). His club is the simulation's home side whatever the ground (#167), so the lineup is his club's first.
4. **Bans are served** by those who sat it out (`Availability.Serve`).
5. **The matchday's injuries and cards** are applied (`Availability.Apply`), so a ban it brings is served from the next.
6. **The week passes** for the injured (`Availability.Pass`), so an injury's days count from its own matchday.

**At the season's turn** (`SeasonEngine.Rollover`), cautions start again. Bans still to serve carry into the same competition. The off-season's `OffSeasonDays` (60) pass for the injured.

## Saved

The world's bytes are format 2 (`WorldCodec`): after the managers, each footballer with a ban or caution. Days injured were in format 1 already. A format-1 world reads as format 2 with nobody banned or cautioned, so a career saved before #176 loads (INV-7).

## Measured

`eleven-sim seasons 3`, every fixture quick-simmed. CI runs it.

| Season | Injuries (serious) | Sendings-off | Footballer-matches missed: injured | Banned | A club |
| --- | --- | --- | --- | --- | --- |
| 1 | 19 (4) | 40 | 15 | 68 | 2.3 |
| 2 | 11 (1) | 58 | 1 | 102 | 2.9 |
| 3 | 18 (1) | 59 | 2 | 90 | 2.6 |

- **Few injuries.** The full simulation injures about 0.02 footballers a side a match at 3-minute halves (#172), most of them knocks, so a club loses well under one footballer-match a season to injury. Whether the career should see more is a feel question, not settled here.
- **Bans** cost a club two or three.
- **Training injuries** are M6.10's.

## Tests

- **`AvailabilityTests`** (career):
  - the layoff's bands and their shares;
  - an injury's days, never shortened;
  - a ban for a sending-off, in its competition;
  - the third caution, then every second;
  - the second yellow;
  - serving, only in its competition's matches of his club;
  - the days passing, and a layoff within the week that misses no matchday;
  - the season's turn, in `Rollover` too;
  - the world's bytes, and a format-1 world.
- **`AvailabilitySeasonTests`** (harness):
  - selection without who's out, the keeper's fallback included;
  - a match with outfielders out, its bench only the substitutes picked;
  - a headless season that never picks anyone out, plays the quick-sim on the XI it picked, and bans and injures from its lines;
  - the human's lineup, home and away, and his played fixture carried;
  - the hub's line.
