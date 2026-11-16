# The slide tackle

Issue #98. A committed action with a long recovery: a gamble to win the ball from further than a standing challenge reaches. Every value is an untuned technical reference, not approved feel.

## The intent

There is no slide button. On ELEVEN's ACTION surface (Candidate A, #80) it is the flick without the ball. In the simulation, a **kick intent** is a slide when:
- the footballer can't kick the ball: it is beyond his `kickReach`;
- an opponent controls it on the ground;
- it is within `slideRange` (3 m);
- he isn't a keeper.

Anywhere else the same intent is a kick, as ever.

## The slide

- **Committed** along the intent's line, or his heading if it has none: `Slide` phase, and `SlideStarted`.
- **Its run:** at `slideSpeed` (8 m/s) easing to half of it over `slideTime` (0.4 s). That is about 2.4 m: #98's research puts a slide at 2–3 m in about 0.4 s.
- **Each step it meets** the ball within `slideReach` (0.9 m), or the man who has it within half of that, whichever comes first:
  - the ball first: `SlideWon`. The ball is knocked on along the line at the challenge's impulse, and the man may not touch it for a stride;
  - the man first: `SlideFouled`. It is recorded for #99 to adjudicate;
  - neither by the end: `SlideMissed`.
- **Then on the ground,** still, for `slideRecover` (1 s): `Grounded`. He can't move, play the ball or challenge.

The five knobs are in the bench's Control tab (config format 20).

## The CPU outfielder

He slides by the same intent:
- beyond 1.5 standing challenge ranges and within 0.7 of the slide's range of an opponent's ball;
- only with the ball 0.4 m nearer him than the man;
- aimed at where the ball will be partway through.

So he slides from the side or in front, never through the man.

**Measured** in 12 CPU 5v5 matches: 166 slides, 60 won, 25 fouled and 81 missed, beside 312 standing challenges won.

**Finding.** Half of the CPU's slides miss: the ball moves on while he reacts. Slides that foul will cost free kicks and penalties once #99 lands. Its trials will show whether the CPU should slide less.

## Tests

`SlideTests`:
- A kick intent without the ball near an opponent's slides along its line and ends on the ground.
- Far from an opponent's ball the same intent is a kick.
- Meeting the ball first wins it; meeting the man first is a foul.
- The CPU slides, winning more than it fouls.
