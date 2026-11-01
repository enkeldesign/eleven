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
- **a step for − and +**, which is presentation only. It is the largest 1, 2 or 5 × 10ⁿ that fits twenty times into the starting range, or into the valid range where there is no starting range. Any valid value can be typed.

`eleven-sim candidate` prints them all.

## Candidates

A `BenchCandidate` is a complete set of values: one config and one player.
- **Built only through the supported surface**, `SimConfig.With` and `PlayerCapabilities.With`, so it is an ordinary immutable, validated and hashed config (INV-5). It is never a change to a running simulation.
- **The practice start** is the spike scenario's start state with the candidate's player (`SpikeScenario.InitialState(config, capabilities)`).
- **The same values always give the same config hash and the same practice start hash**, in any order and in any runtime. `eleven-sim candidate name=value ...` prints both.
- **Refused whole.** A candidate with any value out of its valid range, unknown, missing, given twice or not finite is refused, with the knob and its range named. Nothing is ever partly applied.

The page and the host exchange values as IEEE-754 bits (16 hex digits each), so the host gets exactly the doubles the page shows. Decimal is for display only.

## The panel

**TEST BENCH** is a real page button (`unity/Assets/Eleven.Host/Plugins/ElevenPage.jslib`). It sits in the top row, left of Reset, away from the thumbs. It opens **FEEL / TEST BENCH**, a page panel: a sheet at the side in landscape and at the bottom in portrait. Touch, VoiceOver and the keyboard all work natively.

**Each knob** has:
- a slider;
- − and + buttons, labelled with the step ("Increase Kick speed by 0.5");
- a text field with a decimal keypad, which takes "," or "." as the decimal separator.

Precise tuning never needs a drag. Every control is at least 44 × 44 px.

**State is text, never colour alone:**
- a changed knob says "Changed, was 18";
- an invalid one says "Out of range: valid 1–15 m/s";
- the status line counts the pending changes;
- APPLY / RETRY is disabled while any value is invalid, and the status line says why.

**Buttons:**
- **APPLY / RETRY** sends the whole candidate. The host either refuses it with the reason, shown in the panel, or replaces the applied values. It then starts a fresh run under the new config, from the practice start, with a new recording and a new capture window. The panel closes, and the result and its time are announced. With nothing pending, it restarts the practice.
- **DISCARD** returns the candidate to the applied values.
- **CLOSE**, or Escape.

**Opening and closing are presentation only.** The run steps exactly as it would without the panel.

**What isn't kept yet:** the applied values last for the page's life, and a reload returns to the technical reference. Named presets that persist come with #46. Until then a capture records the active preset as `technical-reference` for the reference values and `unsaved` for anything else.

## Measured

**What is measured:**
- **The host's time** to build the config and the fresh run: `ELEVEN bench applied ... hostMs=`.
- **The page's time** from the press to the fresh run, including the wait for the next frame: `ELEVEN bench page applied pageMs=`. The panel announces the page's time.

**What the smoke test checks** in headless Chromium, in both passes:
- the knob count against the host's description;
- the size of every control;
- that opening and closing leave the run alone (no reset, no game input, ticks continue);
- that an out-of-range value can't be applied;
- that APPLY / RETRY starts a fresh run whose config and practice start hash exactly as `eleven-sim candidate kickSpeed=18.5 maxSpeed=8.5` says in .NET.

**A capture under the candidate.** The smoke test then presses THIS FELT WRONG, and CI replays that capture under .NET with the rest.
