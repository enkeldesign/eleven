# The career hub

The career's screens ([#168](https://github.com/enkeldesign/eleven/issues/168)) over #167's session ([session.md](session.md)). After this, a career is playable from the start screen. What the hub says is `CareerHub` (harness), tested in .NET; the host shows it (`HomeSheet.Career.cs`).

## New career

**Career** on Home opens **New career**, or the hub of the career already under way.
- **The world:** a new one of 36 clubs, from a seed taken at that moment.
- **Your club:** any of the 36, shown with name, short name, division and kit, as in Quick match.
- **Your role:** **Player-manager** or **Manager only** (#80).
- **Start** begins the first season. Its matches have 3-minute halves, #80's default.

Until saves arrive (M6.07), a career lasts as long as the page, and both pages say so.

## The hub

- **The header:** the season and matchday, "Season 1 · Matchday 4 of 28".
- **The standing:** your division, place and points, "Division 1 · 5th · 12 points".
- **Your next fixture:** "League round 4: LIN v ROV, at home", or "Cup, Round of 16: ROV v LIN, away". If there is none, why: "No match for you: a bye into the round of 32", or "out of the cup".
- **The table:** your division's place, club, played, goal difference and points. Your row is bold and marked by a dot, not only coloured.
- **Your last results:** "W 2–1 v ROV (home)", or "L 1–1 v ROT (away, cup, on penalties)".
- **At the season's end:** your finish, who went up and down, and the cup's winner.

The actions:

| Action | When | What it does |
| --- | --- | --- |
| **Play** | You have a fixture, as player-manager | Plays it on the full simulation, on your club's kick-off taker |
| **Watch** | You have a fixture, as manager only | Plays it on the full simulation, both sides CPU |
| **Simulate** | You have a fixture | Quick-sims the matchday, your fixture too |
| **Next matchday** | No fixture of yours | Quick-sims the matchday |
| **Next season** | The season is over | Rolls over (#164) |
| **Home** | Always | Back to Home; Career returns here |

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
