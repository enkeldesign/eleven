# Awareness cues and off-screen fairness

Issue #102, part of M3's exit. The human sees only what the camera shows. A footballer just off the screen gets a cue at its edge. A measurement shows how often the CPU's decisions depend on footballers he can't see.

Every value here is an untuned technical reference.

## The cues

`EdgeCues.Place` (presentation, engine-free) places a cue for each of the match's footballers drawn outside the play area and within the reach. `PitchView` draws them.

| | Reference |
| --- | --- |
| Reach | 15 m outside the play area, `?cues=` (0 turns them off) |
| Size | 16 pt just off the edge, down to 9 pt at the reach |
| Place | the play area's edge nearest him, 9 pt in, pointing at him |
| Colour | his side's domain token: teammate, opponent, keepers. Each is 3:1 against both grass shades (`ContrastTests`) |

- **Shape before colour.** A cue is a pointer, not a disc, so it reads apart from the footballers.
- **No animation.** A cue moves only with the footballer it points at, so reduced motion needs nothing more.
- **No touch.** Cues are pitch sprites, which take no touch. They sit inside the play area's edge, never over the HUD's controls.
- **No minimap** (out of scope, #102).

In trace, the host logs each second of match clock: the cues shown and where. The 5v5 smoke checks that cues appear and that every one is inside the play area, and records how many a second.

## The human's view

`HumanView` models the host's framing of a phone held upright (#31, #72):
- centred between the human's footballer and the ball;
- zoomed out until both fit with a 3 m margin;
- from 7 m to 17.5 m either side across the pitch, and the play area's aspect (560 ÷ 390) along it.

The status band and the widest view's shift toward the footballer are left out.

## The measurement

`eleven-sim fairness <dir> [seeds]` plays 5v5 matches with the human seat (#95) against each CPU tier, three seeds each. Both sides are at the tier.

Each CPU decision in play is made again on the same observation with some footballers moved out of sight. A decision that comes out otherwise depended on them. Perception draws two errors for every footballer, seen or not (#83), so nothing else in the observation changes.

| Count | Footballers moved out of sight |
| --- | --- |
| The opponents on the human's hidden footballers | the human's side's, outside his view |
| The same, with the cues | the human's side's, outside his view and beyond the cues' reach |
| The opponents on their own hidden teammates | their side's, outside the view: their coordination, reported only |
| The human's CPU teammates on hidden opponents | the CPU side's, outside the view: the yardstick |

A CPU footballer sees off the human's screen as a footballer on the pitch would, within his own vision (INV-9). So the question is whether the CPU side knows more off the screen than the human's side does.

## The bound

At every tier, the opponents may depend on the human's hidden footballers in at most 2 % of their decisions. That share may also not exceed the human's CPU teammates' dependence on hidden opponents by more than 1 point. The command fails past either, and CI diffs `fairness.csv` against `docs/simulation/fairness-reference.csv`.

## Measured

At config `7f5f8868896123d5`, with #134's keeper distribution:

| Tier | Opponents' decisions | On the human's hidden footballers | Not shown by a cue | The human's teammates on hidden opponents |
| --- | --- | --- | --- | --- |
| apprentice | 18,122 | 0.39 % | 0.06 % | 1.28 % |
| club | 25,080 | 0.12 % | 0 | 0.67 % |
| pro | 36,207 | 0.10 % | 0 | 0.60 % |
| elite | 54,322 | 0.20 % | 0 | 0.55 % |

- **Rare, and nearly always cued.** The CPU's decisions depend on a hidden footballer of the human's side about once in 250 to 1,000. Within the cues' reach, at club tier and above none is left unshown.
- **The CPU side knows less off the screen than the human's side does.** The human's teammates depend on hidden opponents two to seven times as often.
- **Coordination dominates.** About 29 % to 39 % of the opponents' decisions depend on their own teammates off the human's screen, such as who presses and who covers (#96). This is a side playing as a side, not knowledge of the human's threat.
