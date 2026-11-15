# Headers and volleys

Issue #94, M2's third step. A ball in the air can be played. The same kick intent and quick pass decide what the contact is from the ball's height: there is no new button (SWOS). Every value here is an untuned technical reference, not approved feel.

## The contact

When a wind-up ends, `Simulator.Connect` takes the ball's centre height `z`:

| Height | Contact | Reach | Error off the intended line, each way | Speed |
| --- | --- | --- | --- | --- |
| `z ≤ kickMaxBallHeight` (0.5 m) | A kick, as before | `kickReach` (0.9 m) | `kickError` | `strength × kickPower × kickSpeed` |
| Up to `volleyMaxHeight` (1.1 m) | A volley: `Kicked`, then `Volleyed` | `kickReach` | `kickError + volleyError × incoming` | As a kick |
| `headerMinHeight` to `headerMaxHeight` (1.4–2.4 m) | A header: `Kicked`, then `Headed` | `headerReach` (0.6 m) | `kickError + headerError × (1 − heading)` | `strength × headerSpeed × (0.6 + 0.4 × heading) × kickSpeed` |
| Between the bands, or above them | A miss: `KickMissed` | | | |

- **incoming** is the ball's speed as it arrives, in m/s.
- **A header is flatter.** Its loft is half a kick's.
- **The header's band wins** if a tuning makes the two bands overlap.
- **Every draw** is the kick's own counter-based draw (`RandomStream.KickDirection`, INV-6). A header or volley widens its bound and draws nothing extra.
- **`heading`** is a new footballer capability, 0–1, reference 0.6 (state schema 17).

The config's seven new values sit in the Kick group (config format 17), each with a valid range and #94's research starting range where it gives one:

| Value | Reference | Valid | Research start |
| --- | --- | --- | --- |
| `volleyMaxHeight` | 1.1 m | 0.6–1.6 | 0.9–1.2 |
| `headerMinHeight` | 1.4 m | 1.0–2.0 | 1.4–1.7 |
| `headerMaxHeight` | 2.4 m | 1.8–3.0 | 2.1–2.5 |
| `headerSpeed` | 0.55 of `kickSpeed` | 0.2–1 | 0.4–0.7 |
| `headerReach` | 0.6 m | 0.2–1.2 | |
| `volleyError` | 0.02 rad per m/s | 0–0.1 | |
| `headerError` | 0.25 rad | 0–0.8 | |

**Not modelled yet:**
- a volley's error growing with the ball's height;
- a header's line or speed taking the ball's incoming velocity;
- jumping (the research's extra 0.4 m);
- diving headers and bicycle kicks (out of #94's scope).

Each would be a knob over this rule.

## The CPU outfielder

He plays the ball in the air by the same rule and the same intents (see [cpu.md](cpu.md)).

- **His observation carries his own reaction delay.** He knows his own body; the simulation still times his plan (INV-12).
- **He reads the ball's flight** to the step his kick would connect: his reaction, then the wind-up.
- **He goes for it** if the ball will then be in the volley or the header band, within 0.8 of that reach of where his run takes him.
- **Where it goes:** at goal from within 22 m (the shooting range plus 6 m); up the pitch otherwise.
- **A teammate nearer** where the ball comes down leaves it to him.

## Measured

`eleven-sim air <dir>` writes `air-curves.csv` and `air-trials.csv`; `docs/simulation/air-curves-reference.csv` and `air-trials-reference.csv` are the record.

**The curves.** A full kick along +x, the ball 0.4 m in front, at 14 heights, 5 incoming speeds and headings 0, 0.6 and 1, over 32 seeds. At the reference heading:

| Height | Contact | Speed | Largest error, ball at rest | at 10 m/s | at 20 m/s |
| --- | --- | --- | --- | --- | --- |
| 0.11–0.50 m | kick | 18.0 m/s | 0.049 rad | 0.049 | 0.049 |
| 0.70–1.10 m | volley | 18.0 m/s | 0.049 | 0.243 | 0.437 |
| 1.30 m | miss | | | | |
| 1.50–2.30 m | header | 8.3 m/s, 46 % of a kick | 0.146 | 0.146 | 0.146 |
| 2.50 m and up | miss | | | | |

**In play.** 12 CPU-against-CPU matches at each side size, 2 × 180 s:

| | Kicks | Volleys | Headers | Goals | From volleys | From headers |
| --- | --- | --- | --- | --- | --- | --- |
| 1v1 | 853 | 1 | 1 | 86 | 0 | 0 |
| 2v2 | 888 | 6 | 0 | 79 | 0 | 0 |

**Finding: air contact is rare in play.** The CPU shoots and passes low, and only a corner is crossed in the air. Headers and volleys will matter once there are crosses and lofted passes to meet. Until then the rule is measured, not exercised.

**Finding: a volley at 20 m/s goes up to 25° wide.** That is `volleyError` 0.02 rad per m/s, a value the research doesn't fix, and a knob.

## Tests

`AirTests`:
- A ball in the head's band is headed, slower than it would be kicked. A better header of the ball heads it harder.
- A ball above the feet and below the head's band is volleyed.
- A ball between the bands or over the head is missed. A header reaches less far than a foot.
- A volley errs more the faster the ball comes, and a header by the player's heading, each within its bound.
- A kick on the ground is what it was.
- A CPU outfielder heads and volleys a ball that comes to him in the air.

`AirCurvesTests`:
- Each height band makes its contact.
- A header is 30–75 % of a kick.
- The curves and the in-play counts match their references.

`KickPropertyTests` allows a header's or a volley's error on top of the kicker's own.
