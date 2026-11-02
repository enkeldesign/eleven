# Visual system

ELEVEN's visual system (#48, ADR-0003) is one code-controlled source of truth for the UI around the match. It should be clearly Enkel Design and clearly football. The family resemblance to TURN comes from construction and behaviour, not shared colours: substantial outlines, rounded geometry, hard offset shadows with no blur, controls that press into their shadow, heavy labels and calm prose, and one dominant forward action per view.

**The palette is a hypothesis** until Erik reviews the specimen on an iPhone or iPad. Changing it after that review is product and design tuning, Erik's call.

## Layers

1. **Primitives** (`Eleven.Presentation.Primitives`) are the #48 starting palette (chalk, paper, ink, slate, royal, grass, violet, sky, gold, green, red) plus dark-context neutrals.
2. **Semantic roles** (`Role`) are what components read: surfaces, text, outline, shadow, the focus ring and its gap, scrim, and action and state fills. There is no generic "football" role.
3. **Context mappings** (`Theme.Ordinary`, `Theme.Dark`) map every role. A dark or other context replaces the mapping, never a component. Coloured fills keep Ink text in both.
4. **Component-state recipes** (`unity/Assets/Eleven.Host/UiKit.cs`) are C# functions that read roles only.
5. **Screen composition** happens in the host: placed from `HudLayout`, or laid out by flexbox.

**Domain colours** (`Domain`: pitch, lines, ball, players) are a separate set. No role uses them, and no theme redefines them.

## Scales, defined once

| Scale | Values |
| --- | --- |
| Spacing (`Spacing`) | 3, 6, 9, 12, 18, 24, 36 pt |
| Shape (`Shapes`): radius / border / hard shadow | control 12/3/3, primary action 12/3/4, card 18/4/6, sheet 24/4/8 |
| Type (`TypeScale`) | micro 11, small 13, body 15, title 19, heading 26, display 36; labels bold uppercase, data tabular |
| Motion (`MotionScale`) | 120 and 180 ms, easing (0.2, 0, 0, 1); reduced motion removes movement and keeps a static cue |
| Layers (`Layer`) | match, HUD, controls, overlay, sheet, dialog, status |
| Interaction | 44 pt minimum target, a 2 pt focus gap, a 3 pt focus ring |

A point is a CSS pixel on the web: the host sets the UI's pixels per point from the canvas.

## Components

| Recipe | Notes |
| --- | --- |
| `MakeAction` | Primary, supporting, utility, navigation and destructive. States: pressed (into the shadow), focused, disabled, busy. |
| `MakeSegmented` | One frame. The selected segment has the selection fill, a drawn mark and bold text. Segments wrap under enlarged text. |
| `MakeValue` | A labelled value: −, the value and + in one row, with a full-width slider below. Precise changes never need a drag, and the row fits 320 pt. |
| `MakeCard`, `MakeSheet` | Framed surfaces with title, content and actions regions. |
| `MakeData` | A stable label and a tabular value in a fixed slot, so the layout doesn't jitter. |
| `MakeStatus` | The status line. |

**Layers compose instead of erasing one another.** A raised component is three elements:
1. the hard shadow (its elevation);
2. the body (fill, outline and content);
3. the two-colour focus indicator.

How states use them:
- **Pressed** moves the body into the shadow.
- **Disabled** drops the elevation and takes no input. The caller states the reason in text.
- **Focus** shows only for keyboard focus.

**The focus indicator is two colours** (WCAG technique C40): a ring on a gap. The tests found that a single Ink ring is 2.86:1 against the pitch. The pair reads at 3:1 on every surface and on the pitch.

## Type

Type uses Unity's built-in runtime font, with size, weight and tracking per role, and needs no font asset. A heavier OFL display face is a knob for Erik's device review: one file under `Resources/`, and only if the review shows the hierarchy is too weak.

## Accessibility

Screen-reader support is out of scope (Erik, ADR-0003). Everything else stands:
- AA text contrast;
- 3:1 for outlines and focus;
- 44 pt targets;
- state that isn't colour alone;
- a visible keyboard focus;
- reduced motion;
- reflow under enlarged text (`?textscale=` until settings exist).

## Checks

- **`tests/Eleven.Presentation.Tests`** checks:
  - the palette;
  - every role mapped;
  - AA text on every surface and fill, in both mappings;
  - 3:1 outlines;
  - the focus pair on every surface and on the pitch;
  - generic roles kept apart from domain colours;
  - the scales.
- **The CI smoke test** checks:
  - UI Toolkit lays out text and takes clicks in the WebGL build;
  - presses on the UI never reach play;
  - the UI's frame cost, as idle fps with and without the UI.
- **The specimen** (`?specimen`, `?specimen=dark`) is built only from these recipes. CI renders it at phone and tablet sizes in both mappings, and on a 320 pt phone with text at 150 %. Each must keep every target at 44 × 44 pt or more, with nothing wider than the page. CI keeps the screenshots as artifacts.
