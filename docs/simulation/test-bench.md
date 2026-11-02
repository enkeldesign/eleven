# Feel test bench

The test bench (#45, part of #27) is how values are tuned by playing. Research supplies the model and credible ranges, the bench exposes them as knobs, and Erik plays and varies them. Its values are technical references and hypotheses, never approved feel. Promoting a preset to an approved feel baseline is a separate, explicit step (#46).

## Knobs

`BenchCandidate.Knobs` (harness) is every `SimConfig.Tunables` entry (ball, kick and pass), then every `PlayerCapabilities.Parameters` entry (the controlled player). Nothing else lists them, so a new tunable appears on the bench by being added to the metadata.

Each knob shows what its metadata holds:
- its unit and valid range;
- its research starting range, where docs/simulation/ball.md, kick.md, movement.md or pass.md gives one, or "technical reference" otherwise;
- its group (`TunableParameter.Group`: Ball, Kick, Pass or Player).

Each knob also has:
- **a label** derived from its name (`kickSpeed` becomes "Kick speed");
- **a step for − and +**, which is presentation only. It is the largest 1, 2 or 5 × 10ⁿ that fits twenty times into the starting range, or into the valid range where there is no starting range. The slider reaches any value on the same grid.

`eleven-sim candidate` prints them all.

## Candidates

A `BenchCandidate` is a complete set of values: one config and one player.
- **Built only through the supported surface**, `SimConfig.With` and `PlayerCapabilities.With`, so it is an ordinary immutable, validated and hashed config (INV-5). It is never a change to a running simulation.
- **The practice start** is the spike scenario's start state with the candidate's player (`SpikeScenario.InitialState(config, capabilities)`).
- **The same values always give the same config hash and the same practice start hash**, in any order and in any runtime. `eleven-sim candidate name=value ...` prints both.
- **Refused whole.** A candidate with any value out of its valid range, unknown, missing, given twice or not finite is refused, with the knob and its range named. Nothing is ever partly applied.

The bench sheet builds candidates directly, with `TryWith`. `Describe`, `Values` and `TryParse` carry values as IEEE-754 bits (16 hex digits each), for tools outside .NET, so they get exactly the doubles the host uses. Decimal is for display only.

## The sheet

**TEST BENCH** is a Unity action from the visual system (`Hud`, `BenchSheet`; ADR-0003). It sits in the top row with Reset and Run scenario, away from the thumbs. On a narrow phone it starts the row below. It opens **Test bench**, a sheet over a scrim, so presses don't reach play. Play goes on under it.

**Group tabs:** Ball, Kick, Pass and Player. Each tab lists its knobs, and the list scrolls, so the sheet's actions stay in reach.

**Each knob** is the recipes' value control:
- its name and unit;
- what is applied, and its ranges, in words;
- − and + in one row with the value between them;
- a full-width slider on its own row, so a 320 pt phone needs no horizontal scrolling.

Precise tuning never needs a drag. Every target is at least 44 × 44 pt. The controls hold the valid range, so a value outside it can't be made, let alone applied.

**State is text, never colour alone:**
- a changed knob says "Changed, applied 18";
- the sheet says what is running: the technical reference, or unsaved values and how many differ from it;
- it counts the pending changes;
- a refused candidate says why.

**Actions:**
- **APPLY / RETRY** turns the pending edits on the applied values into a candidate, refused whole with the reason or handed to the host. The host replaces the applied values and starts a fresh run under the new config, from the practice start, with a new recording and a new capture window. The sheet closes, and the status line announces the result and its time. With nothing pending, it restarts the practice.
- **Revert** returns every pending edit to the applied values.
- **Close**, or Escape. Pending edits stay for the next opening.

**The keyboard:** Tab moves through the sheet's controls only while it is open, with the focus ring.

**Opening and closing are presentation only.** The run steps exactly as it would without the sheet.

**What isn't kept yet:** the applied values last for the page's life, and a reload returns to the technical reference. Named presets that persist come with #46. Until then a capture records the active preset as `technical-reference` for the reference values and `unsaved` for anything else.

## Measured

**What is measured:**
- **The host's time** to build the config and the fresh run: `ELEVEN bench applied ... hostMs=`, which the status line announces. The press and the fresh run happen in the same frame.

**What the smoke test checks** in headless Chromium, in both passes:
- the knob count against the host's description;
- the size of every target, and that nothing is wider than the screen;
- that opening and closing leave the run alone (no reset, no game input, ticks continue);
- that no value goes past its valid range (Gravity's slider at its end, then +), and that Revert puts it back;
- that two edits are counted in words;
- that APPLY / RETRY starts a fresh run whose config and practice start hash exactly as `eleven-sim candidate kickSpeed=18.5 maxSpeed=7.5` says in .NET.

**A capture under the candidate.** The smoke test then presses THIS FELT WRONG, and CI replays that capture under .NET with the rest.
