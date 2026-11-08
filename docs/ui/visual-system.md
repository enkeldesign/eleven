# Visual system

ELEVEN's visual system (#48, ADR-0003) is one code-controlled source of truth for the UI around the match. It should be clearly Enkel Design and clearly football. The family resemblance to TURN comes from construction and behaviour, not shared colours: substantial outlines, rounded geometry, hard offset shadows with no blur, controls that press into their shadow, heavy labels and calm prose, and one dominant forward action per view.

**The palette is a hypothesis** until Erik reviews the specimen on an iPhone or iPad. Changing it after that review is product and design tuning, Erik's call.

**Buttons (Erik, 2026-11-02, after the first specimen review):**
- **Text:** buttons never mix white and black text. Every button's text is white, on darker, desaturated fills at 5:1 or better. The tests check both.
- **Borders:** borders carry button contrast against whatever the button sits on. Over the pitch, the pitch context gives them chalk borders.
- **Everything else:** content that isn't a button stays dark on light in the ordinary mapping, including state banners and badges in their light colours with ink text.

## Layers

1. **Primitives** (`Eleven.Presentation.Primitives`) are the #48 starting palette (chalk, paper, ink, slate, royal, grass, violet, sky, gold, green, red) plus dark-context neutrals.
2. **Semantic roles** (`Role`) are what components read. There is no generic "football" role.
   - **Surfaces and text:** surfaces, text, outline, shadow, inner edge, the focus ring and its gap, and the scrim.
   - **Button fills:** `ActionPrimary`, `ActionSupporting`, `ActionInformation`, `ActionNeutral`, `ActionDestructive` and `Selection`, all with white `OnAction` text.
   - **State fills that aren't buttons:** success, warning and danger, with ink `OnState` text.
3. **Context mappings** map every role. A context replaces the mapping, never a component.
   - **`Theme.Ordinary`:** light pages, with ink text and borders.
   - **`Theme.Dark`:** navy-black pages, with chalk text and borders.
   - **`Theme.Pitch`:** controls over the pitch, with chalk borders and a chalk focus ring.
4. **Component-state recipes** (`unity/Assets/Eleven.Host/UiKit.cs`) are C# functions that read roles only.
5. **Screen composition** happens in the host: placed from `HudLayout`, or laid out by flexbox.

**Domain colours** (`Domain`: pitch, lines, ball, players, keeper) are a separate set. No role uses them, and no theme redefines them.

**The pitch is vertical** (#72): goal to goal runs bottom to top, and the controlled team always attacks up the screen, in portrait and landscape alike.
- **One orientation, everywhere.** `PitchOrientation` (harness) says which way along the goal-to-goal axis, world +x (0°) or −x (180°), is up.
  - **Drawing.** The camera rolls by its `CameraRoll`, so the lines, the mown pattern, the goal, the players and the shadow turn with the view.
  - **Framing and the height cue.** The framing works on screen through the same orientation. The ball's height cue lifts it up the screen, toward the attacked goal.
  - **Input.** The touch adapter maps the stick, a flick's line and aftertouch from screen to world through it before a command is made, so recorded commands are in world coordinates whatever the view (INV-10).
  - **The HUD**, an overlay, stays upright and in place.
- **The match decides the end; the view only shows it.** The view follows the controlled team's authoritative attack direction: +x in practice, and in a match whatever its half-time end switch says (INV-8).
  - A flip is a half turn of the view and of the input mapping, with no simulation state, config or hash.
  - It happens between frames, with no step and no command, and cancels gestures in progress, so a held stick can't reverse the player.
  - The bench's **Pitch view, 0° / 180°**, tests it ([test-bench.md](../simulation/test-bench.md)).
- **The mapping is exact:** a quarter turn, by swaps and subtraction from 0, with no scaling or mirroring. Screen left and right stay left and right, and it round-trips bit for bit.
- **The action is framed below the status line**, which sits over the top of the play area, where the play runs up-field. A message never covers the ball, and the pitch under it is more to see ahead.
- **Look-ahead follows the play area's height.**
  - A wide play area shows less up-field than the horizontal pitch did across it: iPad portrait, iPhone landscape.
  - A tall one shows more: iPhone portrait, iPad landscape.
  - `?ahead=10` sets the least up-field view in metres, zooming out to give it, for comparison. The trace's `ELEVEN view` line reports `ahead` and `across`.

**The pitch is mown** (#58) in two shades, `Pitch` and `PitchMown`, so movement reads under the following camera wherever it is.
- **Cross-cut is the default:** squares mown both ways, so movement reads in every direction.
- **`?pitch=stripes`** draws bands parallel to the goal lines, as in SWOS and Kick Off 2. **`?pitch=plain`** draws one shade. Both are there for comparison.
- **Bands are 5.5 m**, a technical reference.
- **The shades differ quietly**, by 1.10–1.35:1, so the pattern never competes with the play. The player, the keeper (#69), the ball, the lines and the HUD's pitch-context borders and focus pair read at 3:1 on both shades.

## Scales, defined once

| Scale | Values |
| --- | --- |
| Spacing (`Spacing`) | 3, 6, 9, 12, 18, 24, 36 pt |
| Shape (`Shapes`): radius / border / hard shadow | control 12/3/3, primary action 12/3/4, card 18/4/6, sheet 24/4/8 |
| Type (`TypeScale`) | micro 11, small 13, body 15, title 19, heading 26, display 36; labels bold uppercase, data tabular |
| Motion (`MotionScale`) | 120 and 180 ms, easing (0.2, 0, 0, 1); reduced motion removes movement and keeps a static cue |
| Layers (`Layer`) | match, HUD, controls, overlay, sheet, dialog, status |
| Interaction | 44 pt minimum target, a 2 pt focus gap, a 3 pt focus ring, a 2 pt inner edge |

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

**Layers compose instead of erasing one another.** A raised component is up to four elements:
1. the hard shadow (its elevation);
2. the body (fill, outline and content);
3. on controls, the inner edge: a light band inside the top border, from the `InnerEdge` role;
4. the two-colour focus indicator.

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
- reduced motion: the device's `prefers-reduced-motion` setting, so pressed controls lose their shadow instead of moving into it;
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
- **The specimen** (`?specimen`, `?specimen=dark`) is built only from these recipes.
  - It shows the pitch context on the pitch's own colour.
  - CI renders it at phone and tablet sizes in both mappings, and on a 320 pt phone with text at 200 % (WCAG 1.4.4). Each must keep every target at 44 × 44 pt or more, with nothing wider than the page. CI keeps the screenshots as artifacts.
- **The HUD at 200 % text:** CI boots the game on a 320 pt phone with `?textscale=2`. Every placed action must hold its label on one line, and the build line must fit its panel. The HUD measures its elements and lays out around them: actions widen as far as the row allows, Reset goes below Run where both no longer fit side by side, SAVE CAPTURE goes above THIS FELT WRONG, and data slots put their value below their name.
- **The HUD in play** (#53): the build line keeps one size while its values change; THIS FELT WRONG, the note sheet and SAVE CAPTURE are driven by touch, the mouse and the keyboard (Tab with the focus ring shown, Enter, Escape); and no page controls remain over the canvas.
