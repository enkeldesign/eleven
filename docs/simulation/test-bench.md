# Feel test bench

The test bench (#45, part of #27) is how values are tuned by playing. Research supplies the model and credible ranges, the bench exposes them as knobs, and Erik plays and varies them. Its values are technical references and hypotheses, never approved feel. Promoting a preset to an approved feel baseline is a separate, explicit step (#46).

## Knobs

`BenchCandidate.Knobs` (harness) is every `SimConfig.Tunables` entry (ball, kick, pass and ball control), then every `PlayerCapabilities.Parameters` entry (the controlled player), then every `ActionSurfaceSettings.Parameters` entry (Candidate A's touch gestures, #28), then every `SplitSettings.Parameters` entry (Candidate B's, #76). Nothing else lists them, so a new tunable appears on the bench by being added to the metadata.

Each knob shows what its metadata holds:
- its unit and valid range;
- its research starting range, where docs/simulation/ball.md, kick.md, movement.md or pass.md gives one, or "technical reference" otherwise;
- its group (`TunableParameter.Group`: Ball, Kick, Pass, Control, Attached, Goal, Keeper, Player or Touch).

Each knob also has:
- **a label** derived from its name (`kickSpeed` becomes "Kick speed");
- **a step for − and +**, which is presentation only. It is the largest 1, 2 or 5 × 10ⁿ that fits twenty times into the starting range, or into the valid range where there is no starting range. The slider reaches any value on the same grid.

`eleven-sim candidate` prints them all.

## Candidates

A `BenchCandidate` is a complete set of values: one config, one player and one set of touch gesture settings.
- **Built only through the supported surface**, `SimConfig.With`, `PlayerCapabilities.With` and `ActionSurfaceSettings.With`, so it is an ordinary immutable, validated and hashed config (INV-5). It is never a change to a running simulation.
- **Touch values are not simulation values.** They decide which owned command a gesture becomes, never how the football behaves, so they are in no config or state hash. A capture records them as its control candidate (`action-surface/v1 name=value ...`).
- **The practice start** is the spike scenario's start state with the candidate's player (`SpikeScenario.InitialState(config, capabilities)`).
- **The same values always give the same config hash and the same practice start hash**, in any order and in any runtime. `eleven-sim candidate name=value ...` prints both.
- **Refused whole.** A candidate with any value out of its valid range, unknown, missing, given twice or not finite is refused, with the knob and its range named. Nothing is ever partly applied.

The bench sheet builds candidates directly, with `TryWith`. `Describe`, `Values` and `TryParse` carry values as IEEE-754 bits (16 hex digits each), for tools outside .NET, so they get exactly the doubles the host uses. Decimal is for display only.

## The sheet

**TEST BENCH** is a Unity action from the visual system (`Hud`, `BenchSheet`; ADR-0003). It sits in the top row with Reset and Run scenario, away from the thumbs. On a narrow phone it starts the row below. It opens **Test bench**, a sheet over a scrim, so presses don't reach play. Play goes on under it.

**Ball control (#60):**
- "Free / Attached" sits at the top of the sheet, above the tabs, and switches the model at once ([attached.md](attached.md#the-live-switch)).
- The run goes on from the same state, so it isn't a pending edit.
- Only the model changes in the applied values; pending edits stay pending.

**Pitch view (#72):**
- "0° / 180°" sits below the ball-control switch. 180° shows a second half, with the ends switched: the controlled team attacks −x, still up the screen, and the practice goal is at the bottom.
- It flips the view and the stick's mapping at once, between frames, and the run goes on. Nothing in the simulation changes, and gestures in progress are cancelled.
- It is a presentation setting, not a knob: in no config, candidate or capture. Reset and APPLY / RETRY keep it. [visual-system.md](../ui/visual-system.md) has the contract.

**Touch controls (#76):**
- "A · Action / B · Pass + Kick" sits below the Pitch view. It switches the touch candidate at once, between frames, and the run goes on: no step, no command, and the config, the state and possession untouched. The tick and the state hash are logged either side.
- A gesture in progress is cancelled, and fingers already down are ignored until they lift.
- It is the non-gesture way to switch, in Tab's order with the other choices. Reset and APPLY / RETRY keep it.
- A capture records the active candidate and every one of its values.

**Group tabs:** Ball, Kick, Pass, Control, Attached, Goal, Keeper, Player, Touch (Candidate A's gestures) and Split (Candidate B's).
- Control holds the FREE ball's touches (#59, [dribble.md](dribble.md)), the capture reach both models share, the turn both models share, `turnAngle` and `turnStrength` (#66, [turn.md](turn.md)), and FREE's cut, `cutAngle`, `cutStrength` and `cutInterval` (#75, [cut.md](cut.md)). `turnStrength` 0 turns the turn off and `cutStrength` 0 the cut, for comparing with and without each.
- Attached holds the ATTACHED comparator's correction (#60, [attached.md](attached.md)).
- Goal holds the goal's size, its posts' width and how its frame returns the ball (#68, [goal.md](goal.md)). The pitch draws the goal a candidate asks for.
- Keeper holds the practice keeper's four capabilities and the config's five keeper values (#69, [keeper.md](keeper.md)). The capabilities change the practice keeper, never the config.
- Their player value, `ballControl`, is on the Player tab.
- Each tab lists its knobs, and the list scrolls, so the sheet's actions stay in reach.
- The sheet keeps its height whichever tab is shown, so the tabs stay put (#59).

**Each knob** is the recipes' value control:
- its name and unit;
- what is applied, and its ranges, in words;
- − and + in one row with the value between them;
- a full-width slider on its own row, so a 320 pt phone needs no horizontal scrolling.

Precise tuning never needs a drag. Every target is at least 44 × 44 pt. The controls hold the valid range, so a value outside it can't be made, let alone applied.

**State is text, never colour alone:**
- a changed knob says "Changed, applied 18";
- the sheet says what is running: the technical reference, or unsaved values and how many differ from it;
- it counts the pending changes;
- a refused candidate says why.

**Actions:**
- **APPLY / RETRY** turns the pending edits on the applied values into a candidate, refused whole with the reason or handed to the host. The host replaces the applied values and starts a fresh run under the new config, from the practice start, with a new recording and a new capture window. The sheet closes, and the status line announces the result and its time. With nothing pending, it restarts the practice.
- **Revert** returns every pending edit to the applied values.
- **Close**, or Escape. Pending edits stay for the next opening.

**The keyboard:** Tab moves through the sheet's controls only while it is open, with the focus ring.

**Opening and closing are presentation only.** The run steps exactly as it would without the sheet.

**What isn't kept yet:** the applied values last for the page's life, and a reload returns to the technical reference. Named presets that persist come with #46. Until then a capture records the active preset as `technical-reference` for the reference values and `unsaved` for anything else.

## Touch: Candidate A, ACTION SURFACE

Candidate A from #28, playable and tunable here, and switchable live with B (#76); Candidate C is not built yet (#77). It is an input adapter over the existing intents and physics. `ActionSurfaceAdapter` (harness, engine-free, so .NET tests it) reads raw pointer samples, each with its event time, into owned commands for one controller (INV-10). The host's `TouchAdapter` only feeds it Input System events.

**Where a finger lands decides its role, fixed for that finger's life:**
- **Stick zone, left:** the floating stick. It always moves the player, whatever the right thumb does.
- **ACTION surface, right:** the whole right-hand control area (`HudLayout.ActionZone`), the stick zone's mirror. The disc marks where the thumb rests. A press on a UI action inside it, such as THIS FELT WRONG, belongs to the UI.
  - It starts a **gesture**,
  - or, while the controlled player's kick winds up, its aftertouch window is open, or a gesture waits for its tick, it starts **shaping**.

Each control belongs to its first finger, and a second finger on it is ignored. Roles depend only on where each finger began, so the order fingers land in never changes the commands.
- **Owners last from press to lift.** Unity reports a held finger as pressed again whenever its phase changes. Such a repeat changes nothing:
  - a stick finger that moves over a UI action stays the stick's;
  - a finger that began on the UI never becomes a gesture;
  - a finger held across a cancel stays ignored until it lifts.

**A gesture**, from press p₀ at t₀ to release p₁ at t₁. Distances are in points, d = |p₁ − p₀|.

| Gesture | When | Command |
| --- | --- | --- |
| **Flick** | d ≥ `actionFlickDistance`, or d ≥ `actionFastFlickDistance` with release speed ≥ `actionFastFlickSpeed` | a manual kick along p₁ − p₀, kept exactly: screen right is pitch +x and screen up is pitch +y. The loft is `actionLoft`; there is no receiver selection and no snapping. |
| **Tap** | not a flick, released before `actionHoldSeconds` | the assisted quick pass (#26, #38), at release, with no waiting |
| **Let go** | not a flick, held longer | nothing |

- **Release speed** is measured from the newest sample at least 30 ms before the release, or from the press for a shorter gesture.
- **Hold → flick strength:**
  - The hold is the time from the press until the travel first reaches `actionFastFlickDistance`.
  - A hold shorter than `actionHoldSeconds` is an immediate flick, at `actionFlickStrength`.
  - A longer one is a hold → flick. Its strength rises linearly from `actionFlickStrength` to `actionChargedStrength` over `actionChargeSeconds` beyond the threshold, then stays there.
  - The disc shows the charge while the thumb holds.
- **The kick itself is unchanged:** the simulation's wind-up, the kicker's bounded execution error and the manual kick's precedence over the quick pass.

**Shaping:**
- The drag from where the re-touch began is the aftertouch, at full at `actionAftertouchRange` points and capped there. It is sent every tick while the finger is down.
- The window is the simulation's `aftertouchSeconds`. Outside it, the simulation ignores aftertouch, and there is no second duration.
- Lifting a re-touch sends nothing, so it is never a tap. The stick no longer doubles as aftertouch.

**Cancelled:** a reset, APPLY / RETRY, a sheet opening, a view flip or a switch of candidate drops any gesture in progress. Nothing pending is delivered, and fingers already down are ignored until they lift, so no phantom kick, pass or aftertouch follows.

**The disc says what the surface does:** ACTION, HOLD with the charge, SHAPE, then for a moment what the last gesture became (PASS, or KICK with its strength), and the milliseconds from the finger lifting to the command.

**Starting values** are technical references, not approved feel. The hold threshold and the flick distance sit inside #28's research ranges.

| Knob | Unit | Valid | Research start | Reference |
| --- | --- | --- | --- | --- |
| `actionHoldSeconds` | s | 0.05–0.6 | 0.12–0.25 | 0.18 |
| `actionFlickDistance` | pt | 4–80 | 12–32 | 20 |
| `actionFastFlickDistance` | pt | 2–40 | — | 8 |
| `actionFastFlickSpeed` | pt/s | 50–3000 | — | 400 |
| `actionFlickStrength` | fraction of kick power | 0–1 | — | 0.6 |
| `actionChargeSeconds` | s | 0.05–2 | — | 0.5 |
| `actionChargedStrength` | fraction of kick power | 0–1 | — | 1.0 |
| `actionLoft` | fraction of `kickLoft` | 0–1 | — | 0.43 (0.45 rad, the spike's kick elevation) |
| `actionAftertouchRange` | pt | 10–200 | — | 40 |

## Touch: Candidate B, SPLIT

Candidate B from #28 and #76, after Super Soccer Champs: the convenience pass and the manual kick on separate controls, so the thumb never has to tell a tap from a flick on one surface. It is a comparator, not a replacement. `SplitAdapter` (harness, engine-free) shares A's stick, shaping, cancelling and screen-to-world mapping (`GestureAdapter`) and A's reading of a press, a hold and a flick (`FlickReader`).

**Where a finger lands decides its role, fixed for that finger's life:**
- **Stick zone, left:** the floating stick, as in A.
- **PASS:** a bounded disc at the right thumb's rest, A's ACTION disc's place and size (`HudLayout.PassCentre`, `PassRadius`).
- **KICK:** the rest of the right-hand control area. It is marked by a smaller disc beside PASS: to its left, up and left, or above, wherever it fits clear of PASS and the capture actions (`HudLayout.KickMarker`). A press on a UI action inside the area belongs to the UI.

| Control | Gesture | Command |
| --- | --- | --- |
| **PASS** | a press | the assisted quick pass, delivered on the next tick, at the press: no hold threshold, no wait for the release. Lifting or dragging PASS asks for nothing more. It never replaces a kick waiting for its tick. |
| **KICK** | a flick | a manual kick along the gesture, kept exactly, as A's flick (`splitFlickDistance`, `splitFastFlickDistance`, `splitFastFlickSpeed`). The flick may end anywhere, even over PASS: only its press decides. |
| | hold → flick | a stronger kick, as A's (`splitHoldSeconds`, `splitChargeSeconds`, `splitChargedStrength`) |
| | released without a flick | nothing: a tap or hold on KICK isn't a pass, and a kick along the facing is Candidate C's (#77) |
| | a re-touch while a kick winds up, its window is open, or a gesture waits for its tick | shaping, as A's (`splitAftertouchRange`) |

**The discs say what the controls do:** PASS, and for a moment the milliseconds from the press to the command; KICK, HOLD with the charge, SHAPE, then the last kick's strength.

**Starting values** are Candidate A's, as #76 asks, so the two compare on the same reading. They are technical references, not approved feel: `splitHoldSeconds` 0.18, `splitFlickDistance` 20, `splitFastFlickDistance` 8, `splitFastFlickSpeed` 400, `splitFlickStrength` 0.6, `splitChargeSeconds` 0.5, `splitChargedStrength` 1.0, `splitLoft` 0.43 and `splitAftertouchRange` 40, with A's valid and research ranges.

## Measured

**What is measured:**
- **The host's time** to build the config and the fresh run: `ELEVEN bench applied ... hostMs=`, which the status line announces. The press and the fresh run happen in the same frame.

**What the smoke test checks** in headless Chromium, in both passes:
- the knob count against the host's description;
- the size of every target, and that nothing is wider than the screen;
- that opening and closing leave the run alone (no reset, no game input, ticks continue);
- that no value goes past its valid range (Gravity's slider at its end, then +), and that Revert puts it back;
- that two edits are counted in words;
- that APPLY / RETRY starts a fresh run whose config and practice start hash exactly as `eleven-sim candidate kickSpeed=18.5 maxSpeed=7.5` says in .NET.

**What the smoke test checks for Candidate A**, in both passes:
- the ACTION surface is on the right and at least 44 × 44 pt;
- a tap asks for the quick pass (`QuickPassResolved`), with its latency in `ELEVEN action ... latency=`;
- an immediate flick is a manual kick along the gesture;
- in the phone pass, the stick and ACTION work at the same time.

The .NET tests (`ActionSurfaceTests`) cover the rest of the gesture reading: hold → flick, fast short flicks, unsnapped kicks into space, shaping, finger order, cancellation and determinism.

**What the smoke test checks for Candidate B**, on an iPhone 13 in portrait and landscape:
- PASS's disc and KICK's marker each hold a 44 × 44 pt square, in a right-hand area larger still;
- switching to B through the bench changes no state and makes no step;
- a press on PASS asks for the quick pass (`QuickPassResolved`), with the press-to-command time in `ELEVEN action gesture=pass ... latency=`;
- an immediate flick on KICK is a manual kick along the gesture, and a hold → flick a stronger one;
- a re-touch on KICK during a kick shapes it and kicks nothing more;
- the stick and KICK work at the same time;
- a KICK finger held while the controls switch to A and back, and the view flips, makes nothing when lifted;
- at 180°, a flick up the screen on KICK kicks up-field;
- the session's recording, across the switches, replays to the live state.

`SplitTests` covers the reading in .NET, with the live switch: a finger held across it makes nothing on either candidate, and a command waiting for its tick is dropped.

**A capture under the candidate.** The smoke test then presses THIS FELT WRONG, and CI replays that capture under .NET with the rest.
