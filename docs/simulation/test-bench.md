# Feel test bench

The test bench (#45, part of #27) is how values are tuned by playing. Research supplies the model and credible ranges, the bench exposes them as knobs, and Erik plays and varies them. Its values are technical references and hypotheses, never approved feel. Promoting a preset to an approved feel baseline is a separate, explicit step (#46).

## Knobs

`BenchCandidate.Knobs` (harness) is every `SimConfig.Tunables` entry (ball, kick, pass and ball control), then every `PlayerCapabilities.Parameters` entry (the controlled player), then every `ActionSurfaceSettings.Parameters` entry (Candidate A's touch gestures, #28), then every `SplitSettings.Parameters` entry (Candidate B's, #76), then every `OneFireSettings.Parameters` entry (Candidate C's, #77). Nothing else lists them, so a new tunable appears on the bench by being added to the metadata.

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
- "A · Action / B · Pass + Kick / C · One fire" sits at the top of the Touch tab, above every candidate's knobs. It switches the touch candidate at once, between frames, and the run goes on: no step, no command, and the config, the state and possession untouched. The tick and the state hash are logged either side.
- It is in the tab rather than above the tabs, so a phone's knob list keeps its room.
- A gesture in progress is cancelled, and fingers already down are ignored until they lift.
- **A new screen size cancels gestures the same way**, for every candidate: when the device turns, a finger held across the turn was pressed in the old screen's coordinates, and its lift would read as a flick. The host logs `ELEVEN gestures cancelled reason=resize`. A change of the result panel's size alone cancels nothing.
- It is the non-gesture way to switch, in Tab's order with the other choices. Reset and APPLY / RETRY keep it.
- A capture records the active candidate and every one of its values.
- `?controls=split` starts on B and `?controls=one-fire` on C, before the first step: links for a device comparison.
- **On a landscape phone the bench's knob list gets almost no room**, on every tab. The sheet's fixed rows (ball control, view, two rows of tabs) take its height, so the A / B choice can't be reached there. The bench in landscape is #31's. Until then, switch in portrait and turn the phone, or open with `?controls=split` or `?controls=one-fire`.

**Group tabs:** Ball, Kick, Pass, Control, Attached, Goal, Keeper, Player and Touch, then Presets ([below](#named-presets)). Touch holds the A / B / C choice, then Candidate A's gesture knobs, then B's (the `Split` group), then C's (the `Fire` group).
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
- **PASS:** a bounded disc, 0.9 units across its radius, in the right-hand control area's bottom corner, under the resting thumb (`HudLayout.PassCentre`, `PassRadius`).
- **KICK:** the rest of the right-hand control area. It is marked by a disc the same size beside PASS, wherever it fits whole clear of PASS and the capture actions: to its left, up and left, or above, a little smaller if it must (`HudLayout.KickMarker`). On an iPhone 13 that is to the left in portrait and up and left in landscape, each holding a square of about 50 pt. A press on a UI action inside the area belongs to the UI.

| Control | Gesture | Command |
| --- | --- | --- |
| **PASS** | a press | the assisted quick pass, delivered on the next tick, at the press: no hold threshold, no wait for the release. Lifting or dragging PASS asks for nothing more. It never replaces a kick waiting for its tick. |
| **KICK** | a flick | a manual kick along the gesture, kept exactly, as A's flick (`splitFlickDistance`, `splitFastFlickDistance`, `splitFastFlickSpeed`). The flick may end anywhere, even over PASS: only its press decides. |
| | hold → flick | a stronger kick, as A's (`splitHoldSeconds`, `splitChargeSeconds`, `splitChargedStrength`) |
| | released without a flick | nothing: a tap or hold on KICK isn't a pass, and a kick along the facing is Candidate C's (#77) |
| | a re-touch while a kick winds up, its window is open, or a gesture waits for its tick | shaping, as A's (`splitAftertouchRange`) |

**The discs say what the controls do:** PASS, and for a moment the milliseconds from the press to the command; KICK, HOLD with the charge, SHAPE, then the last kick's strength.

**Starting values** are Candidate A's, as #76 asks, so the two compare on the same reading. They are technical references, not approved feel: `splitHoldSeconds` 0.18, `splitFlickDistance` 20, `splitFastFlickDistance` 8, `splitFastFlickSpeed` 400, `splitFlickStrength` 0.6, `splitChargeSeconds` 0.5, `splitChargedStrength` 1.0, `splitLoft` 0.43 and `splitAftertouchRange` 40, with A's valid and research ranges.

## Touch: Candidate C, ONE FIRE

Candidate C from #28 and #77, after Sensible Soccer and Kick Off: one fire control and nothing to aim but the footballer. It tests whether a flick's explicit aim is needed at all. It is a comparator, not a replacement. `OneFireAdapter` (harness, engine-free) shares the stick, shaping, cancelling and screen-to-world mapping with A and B (`GestureAdapter`).

**Where a finger lands decides its role, fixed for that finger's life:**
- **Stick zone, left:** the floating stick, as in A. It always moves the footballer, also while a kick is shaped.
- **FIRE:** the whole right-hand control area, marked by the disc at the thumb's rest. A press on a UI action inside the area belongs to the UI.

| Gesture on FIRE | Command |
| --- | --- |
| a tap: released before `fireHoldSeconds` | the assisted quick pass, at the release. It doesn't wait for the hold threshold to pass. |
| a hold, released | a manual kick along the footballer's heading: `fireHoldStrength` at the threshold, rising to `fireChargedStrength` over `fireChargeSeconds`, with `fireLoft`. Never a pass, and never aimed at anyone. |
| a drag, in a tap or a hold | nothing of its own: only the time held counts |
| a re-touch while a kick winds up, its window is open, or a gesture waits for its tick | shaping, as A's (`fireAftertouchRange`). Outside that, a touch is a fire gesture. |

**Which heading the kick takes.** The command is `KickIntent.AlongHeading`: it carries no direction. The simulation reads the footballer's heading on the tick it accepts the command and holds that line through the wind-up, so a turn during the wind-up doesn't move it. The adapter never samples a heading, so the view (0° or 180°) can't change the line, and a recording replays it exactly. On screen the kick goes where the footballer faces.

**The disc says what the control does:** FIRE, HOLD with the charge past the threshold, SHAPE, then PASS or the last kick's strength with the milliseconds from the release to the command.

**Starting values** are Candidate A's where the two share a meaning, so the three compare on the same numbers. They are technical references, not approved feel: `fireHoldSeconds` 0.18 (#28's range 0.12–0.25), `fireHoldStrength` 0.6 (A's immediate flick), `fireChargeSeconds` 0.5, `fireChargedStrength` 1.0, `fireLoft` 0.43 and `fireAftertouchRange` 40.

## Named presets

Issue #46. A preset is a bench candidate saved under a short name: every value the bench owns, with what a later build needs to tell whether it can still read it. `BenchPreset` and `PresetShelf` (harness, engine-free) hold the format and the rules; the bench's Presets tab is the way in.

**A preset is not approved feel.** Saving one says nothing about feel, and there is no approved flag to set. Promoting a preset to an approved feel baseline is Erik's explicit act, recorded in the owning issue. Later changes to an approved baseline are separate, reasoned baseline updates under AGENTS.md.

**On the Presets tab:**
- **Save** keeps the values shown, pending changes included, under the name typed, with the active touch candidate and the build. A name already saved is replaced.
- **Load** sets a preset's values as the pending changes, whole, and makes its touch candidate the active one. Nothing runs under them until APPLY / RETRY, which starts a fresh run as for any edit.
- **TECHNICAL REFERENCE** is built in: it loads the current untuned reference values. It can't be deleted or replaced.
- **Delete** removes a saved preset.
- The sheet says in words which preset is running, and a felt-wrong capture records its name: a saved preset's name, `technical-reference`, or `unsaved`.

**A name** is 1 to 24 characters: letters, digits, spaces, hyphens and underscores. Case doesn't tell two names apart.

**What a preset records** (`BenchPreset.Write`):

| Line | What it is |
| --- | --- |
| `eleven-preset/1` | The preset format. |
| `name=`, `build=` | Its name, and the build that saved it. |
| `controls=` | The touch candidate that was active: `action-surface`, `split` or `one-fire`. |
| `configFormat=`, `configHash=` | The config's format, and the hash of the config its values make. |
| `knobs=` | How many knobs it holds. |
| `values:` then one `name=bits` line per knob | Every value, as its IEEE-754 bits. |

**A preset loads whole or not at all.** Each of these is refused with the reason, and nothing is applied:
- another preset format, or another config format;
- a touch candidate this build doesn't have;
- a knob missing, unknown, given twice or outside its valid range;
- values that don't make the config hash the preset recorded.

A stored preset this build can't read stays stored, and the Presets tab lists it with the reason. So it fails visibly, and a build that can read it still finds it.

**Where they are kept:** in the browser's `localStorage` under `eleven.presets`, on the device, for every build served from the same origin. No network.

**From a preset to its curves.** `eleven-sim preset <file>` reads a preset's text, says what it is and lists what differs from the technical reference. `eleven-sim preset <file> <dir>` also runs every characterisation tool under the preset's own config and footballer: ball, movement, kicks, passes, dribble, turns, goal and keeper. The curves describe the preset; they are no baseline until Erik promotes it.

**An example preset** is `tests/Eleven.Simulation.Tests/Fixtures/example-preset.txt`: `kickSpeed` 18.5, `maxSpeed` 7.5 and `fireHoldSeconds` 0.2 under Candidate C, config `20d50d46865126bf`.

**Known limit:** there is no way yet to export a preset's text from a phone. A felt-wrong capture taken under the preset carries its name and its whole config, and SAVE CAPTURE exports that.

**What the smoke test checks,** on the desktop pass:
- a name that can't be used is refused, and the running candidate is saved under a good one, with the config hash .NET gives;
- TECHNICAL REFERENCE loads as two pending changes and applies to the boot's config and start;
- the saved preset loads and applies to the config and practice start .NET gives, and the run is named after it;
- after a reload the preset is still there, and can be deleted.

`BenchPresetTests` covers the format, every refusal and the shelf in .NET.

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
- in portrait, switching to B through the bench changes no state and makes no step. Landscape starts on B with `?controls=split`, since its bench has almost no room for the Touch tab's list;
- a press on PASS asks for the quick pass (`QuickPassResolved`), with the press-to-command time in `ELEVEN action gesture=pass ... latency=`;
- an immediate flick on KICK is a manual kick along the gesture, and a hold → flick a stronger one;
- a re-touch on KICK during a kick shapes it and kicks nothing more;
- the stick and KICK work at the same time;
- a KICK finger held while the controls switch to A and back (portrait) and the view flips makes nothing when lifted;
- at 180°, a flick up the screen on KICK kicks up-field;
- the session's recording, across the switches, replays to the live state;
- in portrait, a KICK finger held while the screen turns, then moved and lifted, kicks nothing.

The test sends each flick in one go. At the few frames a second a 3× phone draws on the runner, the UI then sees the press where the flick ends, and a flick ending on THIS FELT WRONG opens the note sheet. So KICK is pressed at its marker unless a flick from there would end on a HUD action; otherwise at the right-hand area's lower left, which is KICK too (in landscape). A finger takes many frames, so on a device the UI sees the press where it lands.

`SplitTests` covers the reading in .NET, with the live switch: a finger held across it makes nothing on either candidate, and a command waiting for its tick is dropped.

**What the smoke test checks for Candidate C**, on an iPhone 13 in portrait:
- switching to C through the bench changes no state and makes no step;
- a tap on FIRE asks for the quick pass (`QuickPassResolved`), with the release-to-command time in `ELEVEN action gesture=tap ... latency=`;
- a hold released on FIRE asks for a kick (`gesture=holdkick`), harder after a longer hold;
- a re-touch on FIRE during a kick shapes it and kicks nothing more;
- with the stick held up the screen the footballer runs onto the ball, and a hold on FIRE kicks it on ahead of him, along his heading;
- a FIRE finger held while the controls switch to A and back, and the view flips, makes nothing when lifted long after;
- at 180° a hold is still the kick along the heading;
- the session's recording, across the switches, replays to the live state.

`OneFireTests` covers the reading in .NET, with the three-way switch: a finger held across any of the six switches makes nothing, and a lift reaches every inactive candidate.

**A capture under the candidate.** The smoke test then presses THIS FELT WRONG, and CI replays that capture under .NET with the rest.
