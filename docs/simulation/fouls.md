# Fouls, free kicks, penalties and advantage

Issue #99. A simplified Law 12–14, through the one restart sequencer (INV-8, [restarts.md](restarts.md)). Every value is an untuned technical reference, not approved feel.

## A foul

A challenge that meets the man before the ball:
- **a slide** ([slide.md](slide.md)) whose first contact is the man: `SlideFouled`;
- **a standing challenge** ([challenge.md](challenge.md)) that misses the ball and ends within `challengeFoulReach` (0.45 m) of the footballer who has it.

Either emits `Foul` (the offender, the footballer fouled), with a referee on. The first foul of a step counts.

## The call

The referee judges after the goal line, so a goal scored on the same step stands.

| Where, and who has the ball | The call |
| --- | --- |
| In the offender's penalty area (Law 1: 16.5 m deep, 20.16 m either side of the middle) | **Penalty** from the mark, 11 m out, at once |
| Elsewhere, the fouled side has the ball | **Advantage** (`AdvantagePlayed`): play on. If the fouled side loses the ball within `advantageTime` (3 s), the free kick is given where the foul was. |
| Elsewhere, the fouled side hasn't the ball | **Direct free kick** where the foul was, at once |

The advantage is in the restart's state: the side, the steps left, the place (state schema 20). Any stoppage ends it.

## The set pieces

- **A free kick** is set up as other restarts are, the opponents 9.15 m off.
- **A penalty** is set up by Law 14:
  - the taker at the mark;
  - the defending keeper on his line, between the posts;
  - everyone else outside the area and 9.15 m from the mark.
- **The automatic take** (`RestartControllers`):
  - a penalty is shot low into a corner;
  - a free kick within 25 m of the goal it attacks is shot at goal; otherwise it is a long ball up the pitch.

  The human takes his own through the ACTION surface, or has it taken for him after the wait (#85).

## The result

The match result's schema 2 (INV-14) counts each side's fouls and the penalties it was awarded, with `fouls=` and `penalties=` lines.

## Measured

**Restart trials:** 12 matches of 180 s, 0 free kicks and 6 penalties. Every foul outside an area was played as advantage there.

**12 CPU 5v5 matches:**

| Goals | Fouls | Advantage played | Free kicks | Penalties | Goals within 2 s of a penalty | Of a free kick |
| --- | --- | --- | --- | --- | --- | --- |
| 35 | 95 | 85 | 20 | 7 | 2 | 0 |

**2v2 passing:** a pass broken up by a foul counts as missed. Over 50 matches 38 % of passes reach a teammate, against 42.5 % without fouls.

**Not modelled yet:** walls, indirect free kicks (with offside, M4), cards (M4), a foul's severity.

## Tests

`FoulTests`:
- A challenge that meets the man and misses the ball is a foul, and the fouled side keeps advantage.
- Advantage kept through its window gives nothing.
- Advantage lost within its window gives the free kick where the foul was, with the opponents 9.15 m off.
- A foul in the offender's penalty area is a penalty from the mark, set up by Law 14.
- The match result counts each side's fouls and penalties.
