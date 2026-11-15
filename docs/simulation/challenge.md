# The standing challenge

Issue #84, the third step of the 1v1 match: an outfield contest for the ball, decided by where the ball is when the challenge reaches it. Sliding tackles, fouls and shielding come with M3.

Every value is an untuned technical reference, and each is a bench knob.

## The rule

A challenge is contextual: there is no tackle button. A footballer **commits** when all of these hold on a step:

- an opponent **controls** the ball;
- the ball is within `challengeRange` (1.0 m) of him;
- his stick is **closing** on it: at least half of it points at the ball;
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
| 0° | won / won / won | won / missed / missed | missed / missed / missed |
| 45° | won / won / won | won / won / missed | none / none / none |
| 90° | won / won / won | won / won / won | none / none / none |
| 135° | won / won / won | won / won / won | none / none / none |
| 180° | won / won / won | won / won / won | none / none / none |

- **A standing dribbler** loses the ball to every challenge.
- **A jogging dribbler** beats a poor tackler coming from in front or at 45°, where the ball moves out of his reach during the commit. The good tackler, sooner and longer, still wins those.
- **A sprinting dribbler** can't be caught from the side or behind by a challenger as fast as he is ("none"), and from in front the challenge commits too early and misses.
- **ATTACHED** gives the same outcomes.
- **Overall:** good 10 won, bench 9, poor 8, of 15 cases each.

## The CPU outfielder

- **He challenges by the same rule,** with no flag of his own: closing on an opponent's ball is enough.
- **He leads his tackle:** within 4 m of an opponent's ball he runs at where it will be a commit's time on.
- **He defends at full speed** to his goal-side spot, which moves with the ball.

**Finding.** In the CPU-against-CPU trials (`cpu-trials-reference.csv`) the challenges are few: 1 to 4 a tier, mostly missed. The cause isn't the challenge. A footballer runs with the ball under FREE as fast as he runs without it, so a defender caught behind the ball never catches the carrier, and the trials, starting with the ball loose, usually begin that way. Before #84 the defender won the ball by getting nearer than its carrier, the steal this issue removes. Whether running with the ball should cost speed is a feel question for the bench, not decided here. Restarts (#85) put defenders goal-side, where challenges will come.

## Tests

`ChallengeTests`: a close challenge commits, wins, knocks the ball and recovers; too far, not closing or a teammate never challenges, and no one steals by being nearer; a loose ball is anyone's; movement avoids a challenge; better tackling reaches further and commits sooner, repeatably; the beaten footballer plays the loose ball after a stride; a kicked ball is no one's, and a recovering footballer starts no kick; under ATTACHED only a challenge frees the ball; no one moves faster than their legs, and the kick-only model has no challenge; snapshots and runs twice; a bad challenge state is refused; a 300-case fuzz; the bench matches its reference.
