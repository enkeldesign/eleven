# Substitutions

Issue #110, M4: Law 3's substitutions, a simplification recorded here with each place it differs. Tactical changes are M5; squad selection is M6.

## The bench

`SimState.Bench` holds both sides' substitutes (state schema 25): each has an id, a side, his capabilities, and the CPU skill that plays him where no controller is there to take him. `Squads.Match` gives each side nine at eleven a side (`BenchSize`, from `BenchFrom` 10) and none below, so smaller sides play without substitutions.

## The command

A substitution is an owned command (INV-10): `Command.SubOff` and `SubOn`, the footballer who goes off and the substitute who comes on. Any controller of the side may give it. It is checked with the command and made at the end of the step, so it is recorded and replays (command recording format 7).

It is refused, with the reason as the command's rejection:

| Rejection | When |
| --- | --- |
| `SubIneligible` | The footballer isn't of the side or is sent off; or the substitute isn't on its bench, or has come on already. |
| `SubNoneLeft` | The side has made `substitutions` (5) already. |
| `SubNotStopped` | The ball is in play, or the match is over. |
| `SubNoWindow` | A new stoppage, with `substitutionWindows` (3) used. |

**Windows.** A stoppage in which a side substitutes is one window, however many changes it makes in it. The window closes when play restarts. Half-time is free: a change in the break uses none.

## What a substitution does

- **The substitute takes the slot, the place and the controller** of the man he replaces. He comes on fresh: no fatigue, no cautions, no injury, with his own capabilities.
- **The CPU that played the man replaced plays him,** deciding afresh. A human's own footballer replaced leaves the human controlling the substitute.
- **A keeper replaced hands over his goal;** a restart's taker replaced hands over the kick.
- **The man replaced takes no further part:** he leaves the state, and his substitute's place on the bench is marked used.
- **A footballer taken off injured (#109) is replaced too.** The substitute comes on at the touchline where he left, in a slot behind his side's last, with his own controller and the bench's CPU skill. A footballer sent off is never replaced.
- **The state stays ordered.** Players stay in ascending id order, so the substitute's place in the array moves; the offside flags, kept by place, follow their footballers.

`Substituted` is an event: the substitute, and the man replaced. The match result's schema 5 counts each side's substitutions (INV-14).

### Simplifications

- **Any stoppage is a chance,** with no wait for the referee's permission and no walk off the pitch: the change is made at once.
- **The kick-off before the match counts as a stoppage,** and so as a window.
- **No extra substitution in extra time, and no concussion substitutes.** Extra time is #111.
- **A substitute has no position of his own:** he plays the slot he takes.

## The CPU's manager

`AI.ManagerBrain`, a pure policy on a `ManagerObservation` (INV-9). He perceives only his own side: his footballers who may be replaced, his unused substitutes, whether he may substitute now, and the share of the match played. `ManagerControllers` runs him for each side no human plays, and gives his substitution through his side's keeper's controller.

His policy, in order:
1. a footballer taken off injured is replaced at once;
2. then one hurt and playing on;
3. then, from `cpuSubAfter` (0.67) of the match, the most tired outfielder, if his fatigue is at least `cpuSubFatigue`.

The first unused substitute comes on. It is a simple policy, not tactics.

## The host

At eleven a side the HUD shows **SUBS**, at least 44 × 44 pt, beside SWITCH, while the human's side has a substitution and a substitute left. A press asks for a substitution at the next stoppage; a second press calls it off. The simulation then makes the manager's pick for the human's side, with no wait and no fatigue threshold: an injured footballer first, otherwise the most tired. Choosing who goes off and who comes on is M5's.

The CPU side's manager runs in the host as in the harness.

## Tests

`SubstitutionTests`:
- at a stoppage the substitute takes the slot and the place, fresh, with his own capabilities;
- the CPU, the human, a keeper and a taker each hand over;
- refused in play, after full time, with five made, and in a fourth window; two in one stoppage use one window, and half-time uses none;
- only a footballer of the side goes off, and only an unused substitute of its bench comes on;
- a footballer sent off isn't replaced; one taken off injured is, and his side is whole again;
- the offside flags follow; the result counts; a snapshot and a recording restore.

`ManagerTests`:
- injured first, then hurt, then the most tired late in the match;
- nothing when he may not, or has no one;
- he perceives his own side only;
- his substitution goes through his keeper's controller, and a human's side is left alone;
- a CPU 11v11 match substitutes within the law's limits.
