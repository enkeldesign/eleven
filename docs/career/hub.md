# The career hub

The career's screens ([#168](https://github.com/enkeldesign/eleven/issues/168)) over #167's session ([session.md](session.md)). After this, a career is playable from the start screen. What the hub says is `CareerHub` (harness), tested in .NET; the host shows it (`HomeSheet.Career.cs`).

## New career

**Career** on Home opens the five save slots ([saves.md](saves.md)): **Continue** a slot's career, or **New career** in an empty one. The career in play continues from its own slot as it is, saved or not.
- **The world:** a new one of 36 clubs, from a seed taken at that moment.
- **Your club:** any of the 36, shown with name, short name, division and kit, as in Quick match.
- **Your role:** **Player-manager** or **Manager only** (#80).
- **Start** begins the first season. Its matches have 3-minute halves, #80's default.

The career is saved into its slot on Start, after every fixture and on the next season. **Save** on the hub saves at once (#171).

## The hub

- **The header:** the season and matchday, "Season 1 · Matchday 4 of 28".
- **The standing:** your division, place and points, "Division 1 · 5th · 12 points".
- **Your next fixture:** "League round 4: LIN v ROV, at home", or "Cup, Round of 16: ROV v LIN, away". If there is none, why: "No match for you: a bye into the round of 32", or "out of the cup". If your manager remembers the opponent, the line ends with your last meeting, "Last met: lost 1–2 (season 1, matchday 7)" (#191, [managers.md](managers.md#his-memory-of-opponents)).
- **Your picks out** (#179), when any is: "Picked but out: A. Name; the AI's pick takes his place."
- **Who's out** (#176), when anyone is: "Out: A. Name (injured, 12 days), B. Name (banned, 1 league match)" ([squads.md](squads.md)).
- **Your money** (#198): "Balance 1,240k. Wages 41k a week of a 45k budget." ([finances.md](finances.md)).
- **Your contracts ending** (#201), when any do: "Out of contract this season: A. Name, B. Name." ([contracts.md](contracts.md)).
- **Your board** (#200): "The board: finish 6th or higher, reach the round of 16 and end the season in the black. Confidence 62: pleased." ([board.md](board.md)).
- **Your training** (#196): "Training: Hard. Faster development, slower recovery, more injuries." ([training.md](training.md)).
- **The season's review** (#194), from the season's turn until your club's first match of the new one: "Last season: most improved A. Name +1.3, B. Name +0.9. Retired: C. Name, 35. From the youth: D. Name, 17." ([development.md](development.md#the-hub)).
- **The table:** your division's place, club, played, goal difference and points. Your row is bold and marked by a dot, not only coloured.
- **Your last results:** "W 2–1 v ROV (home)", or "L 1–1 v ROT (away, cup, on penalties)".
- **At the season's end:** your finish, who went up and down, and the cup's winner.

The actions:

| Action | When | What it does |
| --- | --- | --- |
| **Play** | You have a fixture, as player-manager | Plays it on the full simulation, on your club's kick-off taker |
| **Watch** | You have a fixture, as manager only | Plays it on the full simulation, both sides CPU. The host sends no command of yours (`CareerSession.HumanPlays`), which would double the CPU's for your kick-off taker, so in the match both sides' CPU managers make the changes. |
| **Simulate** | You have a fixture | Quick-sims the matchday, your fixture too |
| **Next matchday** | No fixture of yours | Quick-sims the matchday |
| **Next season** | The season is over | Rolls over (#164) |
| **Save** | Always | Saves into the career's slot at once (#171) |
| **Squad** | Always | Your squad: pick your XI and bench ([squads.md](squads.md#your-squad)) |
| **Training** | Always | Cycles your club's training: Auto, Light, Normal, Hard (#196) |
| **Home** | Always | Back to Home; Career's slots return here |

## A career match

- **The score panel** names the clubs, yours first: your club is the simulation's home side (#167).
- **Reset reads Hub.**
  - **At full time,** the result is recorded on the fixture's own ground, the rest of the matchday is played, and Hub shows the new table.
  - **Before full time,** leaving simulates your fixture instead.

## Evidence

- **`CareerHubTests`:**
  - ordinals;
  - the first matchday's lines and action;
  - the table and results after a matchday;
  - a bye;
  - a cup tie's round;
  - the season's end, with its summary and the action to roll over.
- **Smoke** (`career-phone`):
  - Home, Career and Start;
  - the hub at matchday 1;
  - Simulate, and matchday 2 with the table played once;
  - Play starts your fixture at eleven a side, and it plays;
  - Hub leaves it, simulated, at matchday 3.
