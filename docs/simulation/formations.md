# Formations

Issue #125, M5 (#80): a side's formation as data. Before it, a side's shape was code: #96's rows below eight a side, and #106's technical-reference 4-4-2 at ten outfielders. Every figure is a technical reference, not approved feel.

## The data

- **`Formation`** names a shape for ten outfielders: 4-4-2 (the default), 4-3-3, 4-2-3-1, 3-5-2, 5-3-2 and 3-4-3.
- **Each formation is a list of lines from the back.** A line has a count, a depth and a half-width, in the side's own frame with the ball at the centre spot (m; the side attacks +x).
- **A place's home** is its line's depth, spread evenly across the line's width. Places are the slots of #96, numbered from the back and across.
- **`TeamState.Formation`** holds each side's (state schema 27). It is state, not config, because M5.02's plans change it during a match. A snapshot keeps it, and a state with an unknown formation is refused.
- **The CPU reads it through its observation** (INV-9): `OutfieldObservation.Formation`, his own side's.

| Formation | Lines (count at depth m, half-width m) |
| --- | --- |
| **4-4-2** | 4 at −24, 22 · 4 at −10, 22 · 2 at +4, 10 |
| 4-3-3 | 4 at −24, 22 · 3 at −10, 14 · 3 at +4, 20 |
| 4-2-3-1 | 4 at −24, 22 · 2 at −15, 8 · 3 at −4, 20 · 1 at +4 |
| 3-5-2 | 3 at −24, 14 · 5 at −10, 24 · 2 at +4, 10 |
| 5-3-2 | 5 at −25, 24 · 3 at −11, 14 · 2 at +4, 10 |
| 3-4-3 | 3 at −24, 14 · 4 at −10, 22 · 3 at +4, 20 |

The 4-4-2 is #106's shape exactly, so a side at full strength in it plays as before. The shape follows the ball, steps up with it and stays inside the lines as #96 describes; only the homes change.

## A side short of men

- **From eight to ten outfielders a side plays its formation.** Each man missing (sent off, or taken off injured before his substitute is on) comes out of the furthest-forward line of more than one: a 4-4-2 down to nine is a 4-4-1, to eight a 4-3-1.
- **Below eight,** every formation plays #96's small-side rows.

Before formations, a 4-4-2 down to nine played the derived 3-4-2. Only matches in which a man left the pitch moved: the gate's first semi-final and three 10v10 trials.

## The kick-off

The kick-off's places are the homes inside the side's own half, as before. A home in the middle near the halfway line (the centre forward of a front three, a 4-2-3-1's central attacking midfielder) stands 5 m to one side, clear of the taker and of the opponent facing him.

## The contrast

`eleven-sim formations <dir>`, in CI against `formation-reference.csv`: home in each formation against the 4-4-2, CPU against CPU at the reference config, six seeds each.

**Possession** is the side that last played the ball, by the simulation's own record: a touch, kick, header, volley, challenge or keeper's save. A **shot** here is a kick, header or volley that next reaches the goal line at the end the kicker's side attacks, the frame, or the other side's keeper. **Above last man** is how far the side's mean x stands above its deepest outfielder's: the shape, apart from where the ball is.

| Home | Goals | Shots | Possession | Depth | Above last man | Wins the ball at |
| --- | --- | --- | --- | --- | --- | --- |
| **4-4-2** | 1.0–1.0 | 5.7–6.3 | 51 % | −9.3 m | 13.2 m | −2.8 m |
| 4-3-3 | 0.7–1.2 | 8.7–5.0 | 50 % | −6.0 m | 14.7 m | −2.5 m |
| 4-2-3-1 | 1.2–0.5 | 8.0–5.2 | 49 % | −8.0 m | 13.0 m | −2.0 m |
| 3-5-2 | 0.7–1.3 | 6.0–6.3 | 50 % | −8.0 m | 14.2 m | −4.4 m |
| 5-3-2 | 0.5–1.2 | 4.5–5.5 | 49 % | −13.2 m | 12.3 m | −7.2 m |
| 3-4-3 | 0.7–1.2 | 4.7–8.3 | 52 % | −8.5 m | 15.3 m | −6.0 m |

**What it shows:**
- **The shape changes where a side plays.** A 5-3-2 stands 4 m deeper than the 4-4-2 and wins the ball 4.4 m deeper. A 4-3-3 stands 3 m higher and shoots most.
- **Possession doesn't move.** The CPU's passing doesn't depend on the shape yet.
- **Goals at six seeds are noise:** a 10v10 match has about two. Shots and depths are the contrast's measures.
- **The 3-4-3 concedes more shots than it takes** (4.7 to 8.3). Why isn't measured yet; M5.02's plans and M5.03's measures look at it.

Pressing, line height and width as instructions are M5.02's plans; who chooses a formation and when is M5.03's.

## Tests

`FormationTests`:
- each formation's lines match its name, ten in all, each line ahead of the last;
- every side lines up on the pitch in its own half, no two within 4 m, in each formation;
- every home lies inside the pitch and no two places share one;
- the 4-4-2 at full strength is the shape before formations;
- a side short of men closes up from the front; below eight a side, every formation plays the small sides' shape;
- a CPU side in a 5-3-2 stands nearer its last man than in a 3-4-3;
- a snapshot keeps each side's formation, and an unknown formation is refused.
