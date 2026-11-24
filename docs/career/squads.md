# Who's out

Injuries, bans and condition carried from one match to the next ([#176](https://github.com/enkeldesign/eleven/issues/176) and [#177](https://github.com/enkeldesign/eleven/issues/177), M6.08b and c). Each result names its footballers (#172). What happened to them in a match keeps them out of the next ones, or tired for them, and nobody out is picked.

The state is the footballer's own, in the world (`Player`), so it is saved with him:
- `InjuredDays`, days out injured;
- `Fitness`, his condition, 0–100 (#177);
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
- **The days pass** at the end of each matchday, his injury's own included, whether or not his club plays: the calendar's days to the next matchday, 7 to a league round and 3 to a midweek cup round ([competitions.md](competitions.md#the-calendar)). He is out while any are left.
- **So a knock rarely costs a match.** A layoff of up to a week is over by the next league round, so mostly a moderate or severe one does: 15 % of minor injuries, 80 % of serious ones. A midweek tie three days on comes sooner.

## Bans

SvFF's rule for Allsvenskan, counted per competition. The league is one competition whatever the division, and the cup is the other.

- **A sending-off** bans him from his club's next `SendingOffBan` (1) match in that competition.
- **Cautions** add up over a competition's season. The `CautionsForBan`th (3rd) brings a one-match ban, then every `CautionsPerFurtherBan` (2) more: the 5th, 7th and 9th.
- **A second yellow's two cautions** don't count toward the total; its sending-off bans him. A caution before a straight red does count.
- **Served** only in his club's matches of that competition. A bye, or a match in the other competition, doesn't serve it.

## Condition

`Player.Fitness`, 0–100 (#177). Recovery from a match takes 72–96 hours (Nédélec et al. 2012, *Recovery in soccer, part I*), so a week's gap recovers it and a midweek tie's three days nearly do.
- **A match's cost** is `ConditionCostPerMatch` (30) for a whole match at reference stamina. It scales by the share of the match he played (his #172 line's seconds over two halves, so extra time costs more) and by his stamina: ± `StaminaWeight` (0.4) of it at stamina 1 and 20.
- **Recovery** is the cost of a whole match over `RecoveryDays` (3.5) days, per day passed, to 100, at the pace of his club's training (#196, [training.md](training.md)).
- **A tired start:** in a career match on the full simulation, each footballer starts at fatigue (100 − condition) / 100 × `ConditionFatigue` (1.0) (#100). A fresh side plays exactly as before. Fatigue is already state, so no schema changes. A substitute still comes on fresh.
- **Measured, it costs nothing.** In-match fatigue clears within a minute or so, so a side starting at fatigue 0.6 scores as a fresh one does ([quicksim.md](../simulation/quicksim.md#condition)). The quick-sim's weight for it is fitted at 0. For condition to tell in a match it would have to act on something that lasts the match, such as his stamina for it. That is open.
- **Rotation:** a starter below `RestBelow` (70) is rested for a footballer of the slot's line at or above it, within `RotationMargin` (1.5) of his selection level. With none, he plays tired. Every club is picked this way, the human's too until M6.08d. Keepers aren't rotated.
- **The season's turn:** the off-season's days leave everyone fresh.

## Your squad

**Squad** on the hub (#179) shows every footballer of your club in three groups:
- **XI**, 11, the keeper among them;
- **Bench**, up to 9;
- **Not picked**, by level.

A row: "J. Smith, 23 · CB, DM · 14.2 · 96", his name and age (#194), positions, selection level (the 1–20 mean the AI picks by), condition, and why he's out if he is.

- **Picking is a swap:** choose a footballer, then one of those who can take his place, best first.
  - **Who can't:** one who's out can't come into the XI or onto the bench.
  - **Keepers:** in the XI, a keeper's place is a keeper's and an outfielder's an outfielder's. No keeper sits on the bench, as substitutes are outfielders (#110).
  - **The first swap** starts from the AI's pick. **Auto** returns to it.
- **By keyboard,** Tab moves through the rows, then Back and Auto. The list scrolls to keep the focused row in view, as every list of the host's does (Codex on #187).
- **Your picks** are `Club.Picks` in the world: your eleven, then your bench. They're saved with it, at world format 3.
- **They play your fixture,** whether played, watched or simulated:
  - the AI's rule places your eleven in the formation's slots;
  - a pick out by kick-off is replaced from the rest, in his line, and the hub says so. A reserve of the slot's first-position line comes in before any pick is moved to his second position, and only then is anyone moved out of position, so every other pick keeps his place (Codex on #187);
  - nobody of yours is rotated (#177), as you decide who rests.

## A matchday

`CareerSeasons.PlayMatchday` in order:
1. **Who's out** of each club's match is read before any fixture is played, for the matchday's competition.
2. **Each XI** is picked without them (`CareerSquads.Selection`), for the full simulation and the quick-sim alike. With no fit keeper, the best of the rest at keeping goes in goal. The bench is the best of the rest, up to nine; with footballers out it can be shorter, and the match has only those places (Codex on #185).
3. **The fixtures are played** and recorded, each with its lineup: simulation id to the world's footballer. The human's fixture brings its own lineup (`CareerSession.LineupFor`). His club is the simulation's home side whatever the ground (#167), so the lineup is his club's first.
4. **Bans are served** by those who sat it out (`Availability.Serve`).
5. **The matchday's injuries and cards** are applied (`Availability.Apply`), so a ban it brings is served from the next.
6. **The days to the next matchday pass** (`Training.Pass`, #196): the injured heal, everyone recovers at his club's training pace, and the fit may be injured in training, so an injury's days count from its own matchday.

**At the season's turn** (`SeasonEngine.Rollover`), cautions start again. Bans still to serve carry into the same competition. The off-season's `OffSeasonDays` (60) pass for the injured.

## Saved

The world's bytes are format 2 (`WorldCodec`): after the managers, each footballer with a ban or caution. Days injured were in format 1 already. A format-1 world reads as format 2 with nobody banned or cautioned, so a career saved before #176 loads (INV-7).

## Measured

`eleven-sim seasons 3`, every fixture quick-simmed. CI runs it.

| Season | Injuries (serious) | Sendings-off | Footballer-matches missed: injured | Banned | A club |
| --- | --- | --- | --- | --- | --- |
| 1 | 19 (4) | 40 | 20 | 68 | 2.4 |
| 2 | 11 (1) | 58 | 3 | 102 | 2.9 |
| 3 | 18 (1) | 59 | 4 | 90 | 2.6 |

| Season | XI's condition at kick-off: league | Midweek cup | Lowest starter | Starters rested |
| --- | --- | --- | --- | --- |
| 1 | 99.8 | 97.9 | 80 | 0 |
| 2 | 99.8 | 97.8 | 80 | 0 |
| 3 | 99.7 | 97.6 | 80 | 0 |

- **Few injuries.** The full simulation injures about 0.02 footballers a side a match at 3-minute halves (#172), most of them knocks, so a club loses well under one footballer-match a season to injury. Whether the career should see more is a feel question, not settled here.
- **Bans** cost a club two or three.
- **Condition hardly bites at the references.** A week recovers a whole match, and three days nearly do, as the recovery research says. So no XI starts below 80 and rotation never rests anyone. That's the references' answer, not a tuned one: a later `RecoveryDays`, a higher `RestBelow` or a busier calendar would make it bite, and which is a feel question.
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
- **`ConditionTests`** (career): the calendar's days; a match's cost by minutes and stamina; recovery by the days; a tired start; the season's turn;
  - the season's turn, in `Rollover` too;
  - the world's bytes, and a format-1 world.
- **`AvailabilitySeasonTests`** (harness):
  - selection without who's out, the keeper's fallback included;
  - a match with outfielders out, its bench only the substitutes picked;
  - a headless season that never picks anyone out, plays the quick-sim on the XI it picked, and bans and injures from its lines;
  - the human's lineup, home and away, and his played fixture carried;
  - the hub's line.
- **`ConditionSeasonTests`** (harness): a career match's tired start; rotation's rule; the quick-sim's tired side and its rating; a headless matchday's cost and recovery before a midweek tie; the human's tired start; the condition sample and its fit; the hub's line.
