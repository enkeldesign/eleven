# Quick match

Home's way in to a match between two of the world's clubs ([#160](https://github.com/enkeldesign/eleven/issues/160)). It is the first time the world ([world.md](world.md)) and its ratings ([ratings.md](ratings.md)) are played by a human.

## Home

The plain address opens on Home, and so does the installed app (the manifest's start address is the plain one). `?home=1` adds Home to any other address. Any other parameter skips it, so every older link and every smoke pass opens where it did.

Home has two ways in:
- **Quick match**, below.
- **Practice**: the practice pitch with the test bench, as before.

Home also shows the last quick match's result and the build. Play waits while Home is open, and its page covers the pitch.

Reloading the page returns to Home from practice.

## Choosing

| Choice | Offered | First visit |
| --- | --- | --- |
| Your club | The world's 36 clubs, by division, then reputation | The top club |
| Opponent | The same list, never your club | The second club |
| Size | 5v5 or 11v11 | 5v5 |
| Each half | 1, 2, 3 or 5 minutes of match clock | 2 minutes |

Each club shows its name, short name, division and kit. The kit's colours are the world's `KitColour` tokens, resolved through the design system's kit tier (`Eleven.Presentation.Kit`). The choices are kept on the device (`eleven.quick`) for the next visit.

The world is one fixed seed (`QuickMatch.WorldSeed`), so a club is the same club on every device and visit. The career's own world (M6.04 on) is separate.

## The match

`QuickMatch.Match` builds the kick-off from the world through `CareerSquads` ([ratings.md](ratings.md)):
- **The human** plays the home club's kick-off taker and switches between his team-mates as in any match.
- **Every footballer** on both sides plays on his own values from his ratings: the human's, his team-mates', the keepers', the CPU's and the benches'. The bench's player knobs don't apply to a quick match. Its other values (ball, kick, control model and the rest) do.
- **Both sides** keep the three stored plans (#126), so the paused dashboard switches them as before.

The seed is the address's `seed=` if it gives one, else the moment of the kick-off. Either way it is logged (`ELEVEN quick started … seed=`). The recording starts from the match's state, so a capture replays it.

In the match:
- the score panel names the clubs by their short names;
- the Reset action reads **Home** and leaves the match for Home;
- Pause stays as it is.

At full time Home shows the result, e.g. "Last match: LIN 2 – 1 ROV". Team colours on the pitch stay the Domain tier's home and away for readability. Club kits on the pitch are a later step.

## Evidence

- **.NET** (`QuickMatchTests`):
  - the address rule;
  - the clubs' order;
  - the human on his club's kick-off taker, with every footballer on his own values;
  - both sides' plans;
  - determinism for a seed.
- **Smoke** (`home-phone`): Home, then Quick match with 36 clubs; the opponent one on and 11v11; Kick off plays those clubs; Home again from the match.

Feel is not approved: the ratings' values are technical references ([ratings.md](ratings.md)).
