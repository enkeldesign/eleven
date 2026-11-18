# The playable 1v1 match

Issue #87, M1's last step: the 1v1 match of #82 to #86 in the Unity host. The human's outfielder and his keeper against a CPU outfielder and his, from the kick-off to full time. Practice stays as it was, and stays the default.

Nothing here is approved feel, and none of it has been tried on a device.

## Starting a match

- **The bench's Presets tab** has a **Play** switch at its top: Practice or 1v1 match. It acts at once, as the other live switches do. A match starts at its kick-off, under the bench's applied values, with the human's footballer as the bench has him.
- **`?mode=match`** starts straight into a match: a link for a device check, and for the smoke test.
- A full menu is M7's.

| URL parameter | What it does |
| --- | --- |
| `?mode=match` | Start in a 1v1 match. |
| `?half=60` | A half's length in seconds of match clock, 60 to 600. The reference is 180. |
| `?tier=elite` | The CPU opponent's difficulty: `apprentice`, `club`, `pro` (the default) or `elite` (#83). |
| `?seed=135` | The match's seed, for a reproducible match. |

## In a match

- **The HUD's scenario panel shows the match:** SCORE (you – CPU), CLOCK (minutes:seconds of the half's clock) and HALF (1st half, Half-time, 2nd half, Full time). At full time HALF gives the verdict.
- **RUN SCENARIO becomes PAUSE / RESUME.** While paused nothing steps, so the clock stops, and a gesture in progress is cancelled.
- **RESET becomes NEW MATCH:** a fresh match from its kick-off.
- **The status line announces** the kick-off, each goal with the score, half-time, the second half and full time.
- **The view turns with the ends.** At half-time the sides change ends in the simulation (#86). When the human's side attacks −x, the view and the stick's mapping turn 180° (#72), so he attacks up the screen in both halves.
- **The opponents are drawn** in their own colours: a salmon outfielder (3.1:1 against the pitch) and an orchid keeper (3.3:1), far from the human's green, his keeper's blue and the ball's yellow. The far goal is drawn too.
- **The human's restarts:** he takes them with a tap or a kick. If he doesn't, his restart is taken for him after a second (#85), so a match never waits for him.
- **Everything else is as in practice:** the touch candidates, the ball control model, the bench and THIS FELT WRONG. The whole match is one recording, replayed in .NET's simulation at full time.

**The keyboard** (#144): in the bench's Presets tab, Tab reaches the play switch's five segments (Practice, 1v1, 2v2, 5v5, 11v11) before the presets, as on screen. Enter on a segment starts that match, and Enter on Practice ends one. The desktop smoke pass checks the order, tabs through every segment, and starts and ends a 1v1 match from the keyboard.

## What the smoke test checks

`match-phone` (390 × 664 pt at 1 px per pt, one-minute halves) plays a whole match with no input from the human:
- it starts through the bench's play switch;
- the score, clock and half fit their panel, clear of the play;
- PAUSE stops the match and its clock, and RESUME goes on from the same step;
- the first half ends at half-time; the ends change and the view turns;
- the match ends at full time with its result: one half-time, one full time, a score;
- the whole match's recording replays to the live state.

`match-tablet-landscape` (1080 × 810) and `match-phone-landscape` (750 × 342) start from the address and check the match panel. Each pass saves screenshots of the kick-off, half-time and the result.

## The milestone gate

`eleven-sim matches <dir> 50` plays 50 CPU-against-CPU matches under the reference halves in CI. At this step all 50 finish: 344 goals (155 in the first half, 189 in the second), 18 home wins, 22 away, 10 draws, a mean length of 379 s.

## The 2v2 match

Issue #95, M2's exit. Two outfielders and a keeper a side ([squads.md](../simulation/squads.md)), the human switching between his two ([switching.md](../simulation/switching.md)), with headers and volleys ([air.md](../simulation/air.md)), under the same referee, clock and result.

- **Starting it:** the Play switch's third segment, 2v2 match, or `?mode=match2`. NEW MATCH starts a fresh one of the same size.
- **Six footballers are drawn.** His teammate wears his green, as in SWOS. The opponents' two outfielders are salmon and their keeper orchid; every footballer colour is a domain token, 3:1 or more on both grass shades.
- **The controlled footballer is marked** by a white halo under him. The camera follows him.
- **Switching is automatic** at the ball's changes of hands, to the teammate who reaches it first, and to a pass's receiver (#93).
- **SWITCH** (at least 44 × 44 pt) asks for the teammate who reaches the ball first. Its switch holds until the next possession event. It sits in the right-hand area beside the action control: above the capture actions in portrait, left of THIS FELT WRONG in landscape. In a 1v1 match and in practice it has no place.
- **At a switch,** a gesture under way is cancelled: it was for the footballer he left. The footballer he left goes back to his side's CPU.
- **Each switch** is a field of his recorded command, so the match's recording replays with no switcher.

**The smoke test.**
- `match2-phone` (390 × 664) starts a 2v2 match through the bench and checks SWITCH's size. A press on it takes over the teammate. The match then plays to its result, with at least two switches on the way, and the recording replays.
- `match2-phone-landscape` (750 × 342) starts from the address and checks SWITCH in landscape.
- `match-phone` checks a 1v1 match shows no SWITCH.

**The gate.** CI plays 50 2v2 matches CPU against CPU, and 50 with a human seat. The seat is a harness driver that runs at the ball and shoots, its switches decided as the host's are. See [squads.md](../simulation/squads.md).

## The match dashboard

Issue #128, M5: the human manager's view of the match. Nothing here is approved feel, and none of it has been tried on a device.

- **PAUSE opens it.** It shows over the stopped play, under the status line, and RESUME closes it. It runs from the play area's top down to the safe area's foot, over the stick and the action area, which do nothing while paused. A play area made short by the match's controls (SUBS and SWITCH at eleven a side on a phone) still holds it. Research puts a phone's dashboard at a stoppage or a pause, not over live play, so it never covers a running match.
- **It shows:**
  - the score, the clock and the half;
  - each side's plan;
  - SWITCH PLAN, for the human's side;
  - the metrics (possession, shots, shots on target, xG, passes completed of played, corners, PPDA), the two sides in two columns, read from the match so far by the recorder (`MatchRecorder.Now`, [match.md](../simulation/match.md)).

  It is one panel at most 480 pt wide, in the ordinary theme, and it scrolls where it doesn't fit, as in landscape on a phone. The plans come first, above the metrics, because they are the decision.
- **SWITCH PLAN** is a segmented choice of the side's three stored plans: Balanced (the reference), High press and Low block (#126's measured plans, as #127's managers have them). Each segment is at least 44 × 44 pt. The side's plan is marked. While a switch waits for a stoppage, or the cooldown runs, the segments are unavailable and the panel says why.
- **A plan chosen there** goes on the human's next command once play resumes, recorded with it (INV-10). The simulation takes it at the next stoppage, or at once if play is stopped; then it settles and the cooldown runs ([tactics.md](../simulation/tactics.md)). The status line says when it is taken, or why it was refused.
- **Both sides store the three plans from five a side up.** Each starts on Balanced, so a match that never switches plays exactly as before: the same events and result for the smoke's seeds. A 1v1 or 2v2 match has no plans, and its dashboard says so.
- **The keyboard:** Tab reaches the plans' segments after the HUD's controls while the dashboard is shown.

**The smoke test.**
- Every full-match pass checks that PAUSE shows the dashboard with each side's metrics, over the play area's columns and at least 160 pt tall where the screen allows. From five a side up it also checks that SWITCH PLAN offers the three plans at 44 × 44 pt or more; under five a side, that no plans are shown.
- `match11-phone` (390 × 664) pauses after its substitution and picks High press. It resumes, and checks that the switch is sent, taken at a stoppage, and that the recording so far replays to that step.
