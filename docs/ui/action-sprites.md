# The first action sprites

Issue #101, M3. The footballers were discs. They are now top-down pixel sprites, one for each action, chosen from the simulation's state each frame and never written back (INV-2).

This is a first set, not final art (M7). Every size and timing here is an untuned technical reference.

## The set

`ActionSprites` (presentation, engine-free) draws each pose at start from a 16 × 16 character grid. There are no external assets.

| Pose | When it's drawn |
| --- | --- |
| Stand | slower than 0.5 m/s |
| Run A, Run B | moving; the stride changes every 0.9 m he covers |
| Receive | for 0.25 s after a touch or after taking the ball on |
| Kick | through a wind-up, and for 0.25 s after a kick |
| Shield | carrying, slower than `shieldSpeed`, with an opponent within `shieldRange` (#97) |
| Slide | in a slide tackle, along its line (#98) |
| Grounded | after the slide |
| Header, Volley | for 0.25 s after the contact, in place of the kick it also is (#94) |
| Keeper dive | in a dive, gloves first along it |
| Keeper hold | with the ball in his gloves |

`ActionPoses` (host, engine-free) makes the choice from the state and the steps' events, in the order above from the bottom up: a keeper's dive and hold first, then slide and fall, air contact, kick, shield, receive, and run or stand.

## Readability

- **Silhouette before colour.** Each pose has its own shape: every two differ in at least 6 of their pixels (`ActionSpriteTests`).
- **The kit is the side's domain token,** unchanged: teammate, keeper, opponent, opponent keeper. Each is 3:1 against both grass shades.
- **An ink outline edges the kit,** at 3:1 against every kit, so neighbours and the shorts read apart. The shorts are the kit at 60 %.
- **The controlled marker,** the white halo, stays under the controlled footballer's sprite.
- **Eight facings.** Each sprite faces up its grid and is turned to the footballer's facing, to the nearest eighth of a turn. Point filtering keeps the pixels crisp.
- **Size.** A sprite is 16 pixels across, its body about 10 of them, drawn at a footballer's 1.8 m diameter. The ball keeps its oversized disc.

## Reduced motion

With reduced motion a run is one frame: Run A, whatever the stride. Every other pose changes only when the state does, so nothing animates that isn't a change of state.

## Screenshot regression

The regression has two parts.

1. **The art is pinned in .NET.** `docs/ui/action-sprites-reference.txt` holds a digest of every pose's pixels in each kit, and `ActionSpriteTests` checks it. A change to a sprite is a baseline update: regenerate it with `ELEVEN_WRITE_REFERENCE=1`, in its own commit with its reason.
2. **The drawing must reproduce it.** `?sheet=1` shows every pose in every kit, in a grid over the play area. The host logs:
   - the grids;
   - the palette;
   - each cell's place on screen.

   The smoke's `sheet-phone` pass checks that all 48 sprites are shown. It screenshots them and samples every opaque sprite pixel at its centre. 95 % must be within 40 a channel of the colour the generator gives it.

A wrong filter, turn, colour space or scale fails the second part; any change to the art fails the first.

`?sprites=0` draws the discs as before, for comparison.
