# The standing challenge

Issue #84, the third step of the 1v1 match: an outfield contest for the ball, decided by where the ball is when the challenge reaches it. Sliding tackles, fouls and shielding come with M3.

Every value is an untuned technical reference, and each is a bench knob.

## The rule

A challenge is contextual: there is no tackle button. A footballer **commits** when all of these hold on a step:

- an opponent **controls** the ball;
- the ball is within `challengeRange` (1.0 m) of him;
- his stick is **closing** on it: at least half of it points at the ball;
- the commit **will reach the ball** (#96). He runs along his stick's line through the commit, with the simulation's own movement (INV-13). The ball is carried on with whoever controls it. It must end within his reach. A defender running alongside a dribbler, closing on him slowly, waits until he can reach it, rather than committing at 1 m and ending short;
- he is ready: not in a challenge, not winding up a kick, and not a keeper.

Then the simulation runs his phases (INV-12):

| Phase | Steps | What happens |
| --- | --- | --- |
| `Commit` | `challengeCommit` (0.2 s), scaled by tackling: 0.8 of it at full tackling, 1.25 of it at none | He keeps the line his stick asked for as he committed, at full stick, and can't steer. He starts no kick. |
| End of the commit | | A ball within his reach is **won**: knocked away at `challengeImpulse` (5 m/s) along his line. If that line runs into the dribbler's feet, it comes off them: the line mirrored about the way to him. The direction errs by up to 0.6 rad × (1 − tackling). A ball beyond his reach is **missed**. |
| `Recover` | `challengeRecover` (0.27 s) | Won or missed, he brakes, and can't touch, take, kick or challenge. |
| `Ready` | | He can challenge again. |

- **His reach** is `challengeReach` (0.9 m) × (0.75 + 0.25 × tackling).
- **He goes the way his stick asks,** so a human or the CPU can lead a tackle into where the ball will be.
- **No chance decides it.** The ball is won if it is still there, and lost if the dribbler took it away. Nothing moves anyone to the ball; his body runs on his own capabilities, as any footballer's does.
- **Events:** `ChallengeStarted`, `ChallengeWon` (its `Other` is the opponent who had the ball) and `ChallengeMissed`.

## Who controls the ball

The simulation now keeps `Ball.LastPlayedBy`: the last footballer to touch it, kick it, take it under ATTACHED, win it in a challenge, or save it. A footballer **controls** the ball when it is attached to him, or when he played it last and it is on the ground within 1.25 × the control reach of him. Except:

- **A ball in a kick's flight** (anyone's aftertouch window open) is no one's. A challenge never redirects a manual kick, and a kick that connects first takes the ball away from a challenge.
- **A keeper's ball** is no one's to challenge.
- **A ball knocked away by a challenger still recovering** is no one's, so the beaten footballer may play it.
- **Under the kick-only model** no one controls the ball, so no one challenges.

A ball an opponent controls is not there for an ordinary touch, or for an ATTACHED take by being nearer: **only a challenge wins it.** Before #84 an opponent took the ball by getting nearer to it than its dribbler. A loose ball is anyone's to touch.

## After a won challenge

- **The beaten footballer** gets a stride's touch cooldown (`touchInterval`), so he can play the loose ball, but not on the next step.
- **Every aftertouch window and turn** closes, as for any new touch.
- **Under ATTACHED** the ball is released.

## Capability

`tackling` (0–1, reference 0.7) is a new player capability: the smallest input, not the final attribute taxonomy. It shortens the commit, lengthens the reach and narrows the knock-away's error. Anticipation, agility, strength and composure, which #84's research names, wait for the attribute model (M6).

## Measured: the challenge bench

`eleven-sim challenge <dir>` runs a dribbler along +x with the ball under FREE and under ATTACHED, standing, at a jog (3.5 m/s) and at a sprint (7 m/s), and a challenger who starts 4 m from the ball and runs straight at it, from five angles against the dribbler's run (0° is from in front), at three tackling profiles: good 0.9, bench 0.7, poor 0.3. `docs/simulation/challenge-reference.csv` is the record. Under FREE (good / bench / poor):

| From | standing | 3.5 m/s | 7 m/s |
| --- | --- | --- | --- |
| 0° | won / won / won | won / won / won | none / none / none |
| 45° | won / won / won | won / won / won | won / won / missed |
| 90° | won / won / won | won / won / won | won / missed / won |
| 135° | won / won / won | won / won / won | none / none / none |
| 180° | won / won / won | won / won / won | none / none / none |

Since #96 a commit must reach the ball, and the dribbler at 7 m/s runs without it: he has pushed it ahead.
- **A standing or jogging dribbler** loses the ball to every challenge.
- **A sprinting dribbler** escapes a challenger from in front or behind ("none"). From the side, a well-timed challenge now wins: 4 of 6.
- **ATTACHED** gives similar outcomes, and every timed challenge wins there.
- **Overall under FREE:** good 12 won, bench 11, poor 11, of 15 cases each. Before #96: 10, 9 and 8, with 18 commits missed at a sprint that are now never started.

## The CPU outfielder

- **He challenges by the same rule,** with no flag of his own: closing on an opponent's ball is enough.
- **He leads his tackle:** within 4 m of an opponent's ball he runs at where it will be a commit's time on.
- **He defends at full speed** to his goal-side spot, which moves with the ball.

**Finding, and its fix (#96).** In M1's trials the CPU started challenges and won none. A carrier ran as fast as a defender, so a defender caught behind never got closer. Committing at 1 m alongside, he ended a step's closing short of his 0.83 m reach. #96 fixes both:
- running with the ball costs speed ([dribble.md](dribble.md#running-with-the-ball));
- a commit must reach the ball.

The CPU trials now see challenges won by every tier: Apprentice 3, Club 4, Pro 8, Elite 10 (`cpu-trials-reference.csv`).

## Why a challenge didn't start (#135)

From Erik's session: "I can't reliably tackle or get the ball back." A standing challenge has no button: it starts when the rule above holds, and nothing said why it didn't.

**The report.** For each human footballer pressing toward a ball an opponent controls (stick at least 0.2, the ball within 3 m), `Simulator.ChallengeReports` says what became of his challenge on the step:

| Hint | When |
| --- | --- |
| Started | It started. |
| TooFar | The ball is beyond `challengeRange`. |
| NotClosing | The stick doesn't point at least half toward the ball. |
| WontReach | The commit would end short of the ball carried on with its dribbler. `Short` says by how much. |
| NotReady | He is committing, recovering, sliding, down, or winding up a kick. |

- **It drives nothing (INV-12).** It is in no state or hash and no step reads it. A replay or a snapshot gives the same reports, so a capture can be read for them.
- **A CPU footballer is never reported,** nor a keeper.

**In the host.**
- The controlled footballer's `challengeRange` shows, faint, in the ball's yellow, while an opponent's ball is in it.
- His marker swells when a challenge starts (not under reduced motion).
- When a reason holds for 0.3 s, the HUD says it in words: "Tackle: run at the ball.", "Tackle: you won't reach it yet. Get ahead of the ball." or "Tackle: still recovering.". It says so at most once every 2.5 s, and never for too far, the commonest state while chasing.
- The trace logs `ELEVEN match challenge hint=` at each change.

**Erik's captures.** Both captures he shared (builds 223 and 227) replay exactly on their own builds with the report added. His footballer was within 3 m of a ball an opponent controlled for 1 and 4 steps, and too far each time. Neither 10 s clip holds a challenge that didn't start: he never got close.

### The scripted seat's ball-winning

`eleven-sim squads <dir> <perSide> <count> seat`: the seat (#95) runs straight at the ball, as a human holding the stick at it does. An attempt is a run of steps pressing with the ball within `challengeRange`, by the report's distance. Eight seeds a size, measured with `humanChallengeLeeway`:

| Size | Leeway | Attempts | Started | Won | Goals for–against |
| --- | --- | --- | --- | --- | --- |
| 5v5 | 0 (reference) | 546 | 206 (38 %) | 104 (50 % of starts, 19 % of attempts) | 21–0 |
| 5v5 | 0.25 m | 569 | 246 (43 %) | 65 (26 %, 11 %) | 17–2 |
| 5v5 | 0.5 m | 588 | 374 (64 %) | 88 (24 %, 15 %) | 15–11 |
| 5v5 | 1.0 m | 604 | 476 (79 %) | 84 (18 %, 14 %) | 8–13 |
| 10v10 | 0 (reference) | 699 | 179 (26 %) | 102 (57 %, 15 %) | 34–0 |
| 10v10 | 0.25 m | 668 | 252 (38 %) | 107 (42 %, 16 %) | 33–0 |
| 10v10 | 0.5 m | 767 | 389 (51 %) | 94 (24 %, 12 %) | 13–0 |
| 10v10 | 1.0 m | 770 | 586 (76 %) | 84 (14 %, 11 %) | 9–9 |

- **Running straight at the ball, a challenge starts on a quarter to a third of the presses in range,** and wins about half of those. That is what "nothing happens" measures: the won't-reach rule holds him back while he chases alongside.
- **Loosening the rule starts more challenges and wins fewer of them.** A commit that ends short misses, and the seat's side concedes more while he recovers. The rule protects him.
- So **the readable hint is the change this issue makes**, and the loosened rule is a preset to compare it with.

**`humanChallengeLeeway`** (m, 0–1.5, 0 by default) adds to a human's reach in the won't-reach check, never the CPU's. A CPU-only match is unchanged at any value. The bench's suggested preset **LOOSE TACKLE** is the technical reference with 0.5 m. Like the reference, it can be loaded but not deleted or saved over, and a capture records its name. Both are technical references, not approved feel.

## Tests

`ChallengeTests`: a close challenge commits, wins, knocks the ball and recovers; too far, not closing or a teammate never challenges, and no one steals by being nearer; a loose ball is anyone's; movement avoids a challenge; better tackling reaches further and commits sooner, repeatably; the beaten footballer plays the loose ball after a stride; a kicked ball is no one's, and a recovering footballer starts no kick; under ATTACHED only a challenge frees the ball; no one moves faster than their legs, and the kick-only model has no challenge; snapshots and runs twice; a bad challenge state is refused; a 300-case fuzz; the bench matches its reference.

`ChallengeHintTests` (#135):
- a challenge that starts is reported, with its distance;
- beyond `challengeRange` he is too far, and further than 3 m off he isn't reported;
- with the stick not toward the ball he isn't closing, and a neutral stick isn't reported;
- running alongside a dribbler he won't reach, by how much, until he can;
- committing or recovering he isn't ready;
- a CPU footballer is never reported;
- a snapshot gives the same reports;
- the defending seat counts its attempts, starts and wins;
- `humanChallengeLeeway` lets a human's commit end that much short, and never changes a CPU match.

`SuggestedPresetTests`: LOOSE TACKLE is the reference with a human leeway, can't be saved over, and is recognised by its values.
