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
