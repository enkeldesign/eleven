# A career session

The human's career ([#167](https://github.com/enkeldesign/eleven/issues/167)) is `CareerSession` (harness). It holds:
- the career's own world, generated from the career's seed (#157);
- the human's club;
- his role;
- the season being played (#164).

M6.06b's hub shows it. Saves are M6.07's, so until then a career lasts as long as the page.

## The roles

#80 gives the human two roles:
- **Player-manager:** he plays his club's matches, on his club's kick-off taker, switching as in any match.
- **Manager-only:** both sides are CPU. He watches the match, or skips it.

## A matchday

- **`Next()`** gives the human's fixture on the next matchday, or none. It is none when his club has a bye (a top club in the cup's preliminary round), is out of the cup, or the season is over.
- **To play it**, `MatchFor(fixture, seed)` builds the kick-off and `MatchConfig(fixture)` its config. A cup tie is a knockout.
  - **His club is always the simulation's home side,** because the human's controller is the home side's (#93). For an away fixture, `Finish` records the result mirrored onto the fixture's own ground. The full simulation has no home advantage, as #163's fit shows (h within its error of 0), so the ground doesn't change the football.
  - **A player-manager's** kick-off is `QuickMatch.Match` (#160): every footballer on his own values, and the human's side on its XI's mean CPU skill until #166.
  - **A manager-only career's** sides are both CPU, on their footballers' own values.
- **`Finish(fixture, result)`** takes the played result from his club's side. It records it on the fixture's ground, quick-sims the rest of the matchday and moves on.
- **`Simulate()`** quick-sims the whole matchday, his fixture included.

## A result from the other ground

`MatchResult.Mirrored()` is the same match seen from the other ground. Every home field becomes the away one's and back:
- the goals, fouls, penalties, cards, injuries, substitutions and shootout;
- the match metrics.

The goals keep their sides' ids, so the winner is the same side. Mirroring twice gives the original, text for text.

## What the hub reads

- **Where things stand:** `Season`, `Division`, and `Table()` with the club's `Position`.
- **Recent form:** `LastResults(n)`, the latest first and from his club's side.
- **The season's end:** `SeasonOver`, then `NextSeason()`, which rolls over (#164). The end's moves and cup winner come from `SeasonEngine.Movement` and `Season.CupWinner`.

## Tests

`CareerSessionTests` check:
- a new career's world, club and first season;
- a season simulated to its end, with the human's 22 league matches, his position and last results, then the next season;
- a top club's bye in the preliminary round;
- fixtures played at home and away, each recorded on its own ground, with the rest of the matchday played;
- mirroring that is exact and its own inverse, on a played knockout;
- the roles: a player-manager owns his footballer and a manager-only career owns none, with his club the simulation's home side on its own values and both sides' plans;
- a manager-only match played by the full simulation and recorded from his side;
- a cup tie's knockout config, and determinism for a career seed.
