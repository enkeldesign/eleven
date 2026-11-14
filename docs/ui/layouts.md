# Layouts: portrait and landscape

Issue #31. ELEVEN is laid out for a phone and a tablet in both orientations. Neither orientation is preferred: that is Erik's to judge by play. Orientation is a matter of presentation and input layout only. The simulation, its config, the camera's rules and every control candidate's gestures are the same in both.

## What is laid out, and from what

`HudLayout` (host) places everything from four inputs: the screen's size, its safe area, the number of result lines, and the sizes the HUD's labels need. One unit is a ninth of the safe area's shorter side.

- **The safe area.** Every control, HUD action and panel is inside it, so none sits under a notch or a home indicator. The sheets (the test bench and the note sheet) add the screen's unsafe edges to their margins.
- **The stick zone** is on the left and **the right-hand control area** on the right, in both orientations. In portrait they share the band under the play area. In landscape they are the columns either side of it.
- **The play area** is what the controls and the HUD's actions leave uncovered. The camera frames the play inside it, not inside the whole screen.
- **Bounded controls** are at least 44 × 44 pt: the stick, A's ACTION disc, B's PASS disc and KICK marker, C's FIRE disc, and the HUD's actions. The gesture areas are larger still.

## The camera in the play area

`PitchView.Frame` centres the view between the footballer and the ball, and zooms out just enough to hold both with a 3 m margin.

| Bound | Value | What it does |
| --- | --- | --- |
| Closest view | 7 m either side of the centre, on the play area's shorter side | The view at rest. |
| Look-ahead | at least 7 m up-field (`?ahead=` asks for more) | Room to aim a manual kick into space. |
| Widest view | 17.5 m either side (`PitchView.MaxHalfExtent`, `?maxview=`) | The view never zooms out past this, so the footballer and the ball keep a useful size. |

- **The widest view is a technical reference,** not approved feel: a 35 m view, about a third of the pitch's length, as Sensible Soccer shows.
- **A ball further off than the widest view leaves the frame.** The footballer stays in it, with the margin, toward the edge the ball left by. Before #31 the view zoomed out without limit.
- **The view line** (`ELEVEN view`, with `?trace`) gives the bounds in use: `ahead=`, `across=`, `ppm=` (pixels per metre), `playerPx=`, `ballPx=` and `maxview=`.

## When the screen turns

- **The layout follows before the next frame is drawn.** `HostLoop.UpdateLayout` runs first in every frame's update.
- **The run goes on.** Nothing in the simulation changes: no reset, no step, no command.
- **Gestures in progress are cancelled** (#76), since a finger held across the turn was pressed in the old screen's coordinates. The host logs `ELEVEN gestures cancelled reason=resize`.
- **The build id, the active control candidate and the test bench** are available in both orientations.

## The test bench in each orientation

The bench sheet (`BenchSheet.Arrange`) has two arrangements, and takes the one the screen calls for when it opens and whenever the screen changes while it is open.

- **Stacked,** where there is height for it: the state, the live switches, the tabs, the knob list and the actions, one over the other. The list takes what is left.
- **Wide,** on a screen under 480 pt high and wider than it is tall (a landscape phone): three columns.
  - The live switches (ball control, pitch view) over the actions.
  - The tabs.
  - The knob list, at the sheet's full height.

  The title goes and the switches' labels shorten, so every switch, tab and action is in reach without scrolling.
- **Before #31** a landscape phone got the stacked arrangement, whose fixed rows took the sheet's whole height. The knob list had no room, and the Touch tab's A / B / C choice couldn't be reached.
- **The run goes on while the bench is open,** so a live switch can be judged at once. No play input can happen by accident: opening the bench cancels gestures in progress, its scrim covers the whole screen, and a press on the UI never reaches the match controls. The note sheet is different: play waits while it is open.

## Trying a layout

| URL parameter | What it does |
| --- | --- |
| `?safe=47,0,34,0` | Safe-area insets to try, in points: top, right, bottom, left. A desktop browser has none, so this stands in for a notch and a home indicator. |
| `?maxview=25` | The widest view, in metres either side of the play area's centre. |
| `?ahead=10` | The least look-ahead, in metres. |

## What the smoke test checks

The layout matrix runs these viewports, in points, each from boot:

| Pass | Viewport | Stands for |
| --- | --- | --- |
| `layout-phone-portrait` | 390 × 664 | a modern iPhone in Safari, portrait |
| `layout-phone-landscape` | 750 × 342 | the same, landscape |
| `layout-phone-compact` | 320 × 568 | a compact, narrow phone |
| `layout-phone-portrait-notch` | 390 × 844, insets 47 top and 34 bottom | a home-screen web app under a notch and a home indicator |
| `layout-phone-landscape-notch` | 844 × 390, insets 47 left and right, 21 bottom | the same, landscape |
| `layout-tablet-portrait` | 810 × 1080 | a standard iPad, portrait |
| `layout-tablet-landscape` | 1080 × 810 | the same, landscape |
| `layout-tablet-large-portrait` | 1024 × 1366 | a large iPad, portrait |
| `layout-tablet-large-landscape` | 1366 × 1024 | the same, landscape |

For each one:
- every control is inside the safe area;
- the play area is clear of the stick zone, the right-hand area and the HUD's actions, and at least 150 pt on each side;
- bounded controls are at least 44 × 44 pt;
- the footballer and the ball are in the play area, at least 12 pt and 4 pt across, with at least 7 m ahead;
- the bench takes the arrangement the screen calls for, with no switch, tab or action clipped and at least 100 pt of knob list;
- through the bench: the Touch tab opens, its first knob is on screen, and the controls switch to B and back with no step;
- the screen turns: the layout follows, every control is inside the new safe area, and the run goes on from the tick it was at.

It saves a screenshot of each viewport at rest, with the bench open, and after the turn, and prints each viewport's measurements as a notice.

The emulated viewports are evidence, not a device check: nothing here has been tried on a physical iPhone or iPad.
