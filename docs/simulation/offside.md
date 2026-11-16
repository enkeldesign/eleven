# Offside

Issue #107, M4: Law 11's offside offence, and the indirect free kick it gives. This is a simplification of the Law, recorded here with each place it differs.

## When it applies

Offside is a competition rule set in config data (#80): `offsideFrom`, in outfielders a side, default 10. Eleven a side plays with it, and every smaller side without it, as small-sided football does. A match needs a referee and two sides.

## The judgement

At every touch of the ball, `Simulator.Offside` judges the toucher's teammates. A touch is any of:
- a kick, whether it is a header or a volley;
- a touch;
- taking the ball on;
- a challenge or slide won;
- a keeper's catch or parry.

A teammate is **in an offside position** if, in his side's attacking direction, he is:
- in the opponents' half (past the halfway line);
- nearer their goal line than the ball;
- nearer it than the second-last opponent, keeper or outfielder.

**Level is onside,** and so is his own half. The flags go in the state as `RestartState.OffsideFlags`, one bit per footballer by index, so they replay and restore from a snapshot.

- **The offence** is a flagged footballer's next touch: an `Offside` event, then an indirect free kick to the defenders where he stands.
- **Any other touch judges afresh.** A teammate's touch sets new flags; an opponent's clears them.
- **No offence straight from a goal kick, a throw-in or a corner.** The taker's touch sets no flags.
- **Any restart** clears the flags.

### Simplifications

- **Involvement is touching the ball.** "Interfering with an opponent" and "gaining an advantage" from a rebound without a touch are not judged.
- **Every opponent's touch resets,** a deflection included. The Law resets only on deliberate play.
- **The position is judged at the touch's step,** from the footballers' places then, not at the first moment of contact.

## The indirect free kick

`RestartKind.IndirectFreeKick`, placed and taken as a free kick is (opponents 9.15 m off, the taker's own kick).

- **No goal straight from it.** As from a throw-in, a ball into the opponents' goal untouched by anyone else is a goal kick, and into the kicker's own goal a corner (Law 13). Anyone else's touch, the same step included, makes it a goal.
- **The CPU's taker** plays it long up the pitch for a teammate, never at goal.

## Measured

In three CPU 11v11 matches of 2 × 180 s:
- footballers were flagged for 0, 83 and 9 steps;
- none touched the ball while flagged, so no offence was given.

The 12-match 11v11 reference is unchanged in its values. Professional football sees two to four offsides in 90 minutes, about 0.2 in six. The CPU's forwards hold a line tied to the ball and seldom run beyond the last defender. Runs that beat a defensive line, and a trap to catch them, are M5's tactics.

## Tests

`OffsideTests`:
- beyond the second-last defender at a teammate's kick, the next touch is offside, an indirect free kick to the defenders;
- level, behind the ball, or in his own half, never;
- an opponent's touch clears the flag;
- no offence straight from a throw-in;
- five a side plays without offside;
- an indirect free kick straight into the goal is a goal kick;
- the flags survive a snapshot.
