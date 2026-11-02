# ADR-0003: The UI around the match is UI Toolkit, built from code

- **Status:** Accepted
- **Date:** 2026-11-02
- **Deciders:** Erik decided the direction (the UI is done in Unity; screen-reader support is out of scope). Opus decided the implementation within it.
- **Supersedes / superseded by:** —

## Context

#48 asks for one visual system for the UI around the match: menus, settings, the test bench and HUD chrome. Until then the host had two UI languages:
- IMGUI drawn in the canvas: the build line, Reset, Run scenario, the result panel and the match controls;
- page HTML laid over the canvas: THIS FELT WRONG, SAVE CAPTURE and the status line (#44), and the test bench panel (#45, PR #49).

Opus first proposed page HTML for all of it on the web build, because it is the only way assistive technology can reach the UI there (#48, first design comment).

**Erik's decision (2026-11-01, on #48):** screen-reader support is out of scope, and the UI is done in Unity. That keeps one implementation for every platform ADR-0001 keeps open: web now, native iOS (#6) and the rest later.

## Decision

1. **All UI around the match is drawn in Unity.** Page HTML is used only for browser services with no UI: storage, share and download, and page timing.
2. **The renderer is UI Toolkit, built entirely from code.**
   - `PanelSettings` and an empty theme are created at runtime.
   - There are no UXML or USS assets, and no Editor state.
   - Input reaches the panel through Unity 6's InputForUI from the Input System, with no EventSystem and no uGUI.
3. **Tokens come from one engine-free source**, `src/Eleven.Presentation`:
   - **What it holds:**
     - primitives, semantic roles, and their ordinary and dark mappings;
     - domain colours, kept apart from the roles;
     - the space, shape, type, motion, layer and interaction scales.
   - **How it's checked:** .NET tests cover contrast and the scales.
   - **How Unity reads it:** through `UiColours` only.
4. **Component recipes are C# functions in the host** that read semantic roles only.
   - **Composing states:** the layers (elevation shadow, body, two-colour focus indicator) are separate elements, so states compose.
   - **Where state lives:** in component code, not stylesheet pseudo-classes.
5. **Type uses Unity's built-in runtime font,** with size, weight and tracking per role. A heavier display face is a knob for Erik's device review: one OFL font file under `Resources/`, if the review asks for it.
6. **The match controls** (stick and kick) stay canvas-drawn until #28 decides their final visuals.

## Alternatives considered

- **Page HTML over the canvas.** It gives native VoiceOver and keyboard support on the web.
  - **Why it lost:** screen readers are out of scope (Erik), and every native platform would need a second UI.
- **IMGUI for everything.** It needs no new module.
  - **Why it lost:** it is immediate mode with manual layout, so there is no reflow under enlarged text and no focus model. Unity positions it for editor tools, not runtime UI.
- **uGUI (Canvas).** It needs the `com.unity.ugui` package and an EventSystem, and its layout model is weaker than UI Toolkit's flexbox.
- **UI Toolkit with UXML and USS assets.** It splits the system between assets and code, and USS has no hard shadows or outlines. The recipes would still need code.

## Consequences

**What gets easier:**
- One UI language and one implementation for every platform.
- Tokens are tested in CI without Unity.
- The specimen renders the production components.

**What gets harder:**
- **No screen-reader support.** VoiceOver can't reach canvas UI on the web.
- **Text fields** depend on Unity's mobile keyboard support in WebGL.
- **Browser features that need a user gesture** (share, download) are armed from a Unity press and run in the page's own gesture handler.
- **UI Toolkit adds** to the web build's size and start-up. The first PR measures both.

**Reversing:** the tokens and recipes don't depend on the renderer. Changing the renderer means rewriting the recipe functions, not the system.

## Enforcement

- **.NET tests** (`tests/Eleven.Presentation.Tests`) check:
  - the palette;
  - AA text on every surface and fill, in both mappings;
  - 3:1 outlines and focus indicator, including over the pitch;
  - generic UI kept apart from domain colour;
  - the scales.
- **The CI smoke test** checks that the UI Toolkit panel draws its text in the WebGL build, and that presses on the UI belong to the UI.
- **Review:** AGENTS.md's "colours come from semantic design tokens" now means `Eleven.Presentation` roles.

## Approval

Erik decided the direction on 2026-11-01 (recorded on #48). Opus accepted the implementation as routine architecture within that direction.
