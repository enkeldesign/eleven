# Lens: Accessibility

**Purpose:** keep ELEVEN's surrounding systems accessible from the start. The match itself is not required to be playable non-visually (brief, principle 7), but menus, settings, text, colour use, readable information, remapping, motion options and feedback are.

**Trigger:** issues touching UI, text, colour, input, remapping or motion.

**Applied by:** SOL or a fresh agent — never the author of the change.

## Questions

1. Is any information carried by colour alone?
2. Does text meet contrast and size expectations against its real background, including over the pitch? Do control states and focus indicators meet non-text contrast (WCAG 1.4.11)?
3. Are touch targets large enough and far enough apart for thumbs?
4. Can the action be performed with the planned alternative inputs once their adapters exist, and does the design assume one specific device?
5. Is there motion, flashing or camera shake, and can it be reduced?
6. Do sound cues have a visual equivalent, and vice versa where it matters?
7. Is feedback for actions clear, immediate and consistent with the Enkel Design grammar?

## Blocking criteria

- Information conveyed by colour alone in menus, settings or the HUD.
- Menu or settings text below WCAG AA contrast (4.5:1 for body text, 3:1 for large text).
- Control states or focus indicators below 3:1 non-text contrast (WCAG 1.4.11).
- Interactive targets in menus smaller than 44 × 44 pt.
- A new motion or flashing effect with no way to reduce it.

Findings that meet these criteria are blockers; everything else is a should-fix or a nit.

## Output

A PR review comment headed `Lens: Accessibility`, one finding per line, each marked blocker, should-fix or nit.
