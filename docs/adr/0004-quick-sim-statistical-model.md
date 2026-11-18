# ADR-0004: The quick-sim is a calibrated statistical model

- **Status:** Accepted
- **Date:** 2026-11-18
- **Deciders:** Opus, under #80, which delegates the choice to the measured throughput (#129). Erik may supersede it.
- **Supersedes / superseded by:** —

## Context

A season (M6) plays many matches nobody watches: each tier's other fixtures, between the human's. INV-1 permits one approximation of the simulation for these, and only if it is calibrated against the simulation. The brief deferred the approach until headless throughput was measured. #129 asks for the approach to be **selected by that measure**.

**Full 11v11 matches, headless, at the reference config** (180 s halves, every CPU brain, keeper, restart taker and manager running):

| Runtime | Full matches a second | A step |
| --- | --- | --- |
| .NET 10, one core of the agent sandbox (`eleven-sim throughput`) | 0.107 | 0.41 ms |
| .NET 10, CI runner | the CI notice "Throughput" | |
| Unity WebGL, headless Chromium on the CI runner | derived from the smoke's 11v11 step budget, recorded in #129's PR | |

**The budget:** with 12 sides a tier, three tiers play 17 other fixtures a round. In .NET, at full simulation, that is about 160 s a round on one core. A phone's browser takes longer, single-threaded and with its frame to keep.

## Decision

Matches nobody plays are resolved by **`QuickSim`, a Dixon–Coles model** (docs/simulation/quicksim.md):
- each side's goals are Poisson, with a low-score correction;
- the rates come from each side's rating: its CPU tier's fitted rating and a weight on its capabilities;
- fouls and cards are Poisson.

Its reference parameters are fitted to a sample of full-simulation matches over fixed seeds, and compared with that sample in CI.

**The model:**
- reads only the two sides' data, the config's half and a seed;
- draws counter-based randomness on its own stream, and computes with the portable maths;
- returns the match result contract;
- shares no state with any played match.

## Alternatives considered

- **The full simulation, headless.** It is exact by definition, but about 160 s a round in .NET and more in a browser. It fails the budget by orders of magnitude.
- **A coarser simulation:** fewer footballers, a longer tick, or abstracted phases.
  - It would be a second simulation to build, keep deterministic and calibrate, with its own drift from the first as the football changes.
  - Its cost is unknown until it exists, and it would still need the same calibration as a statistical model.
  - It is the option to revisit if the result needs within-match detail, such as scorers or minute-by-minute events, that a statistical model can't fake well.
- **A statistical model (chosen).** It runs at about 277,000 matches a second in .NET, so its cost is nil against any budget. It is calibrated by construction, and it is cheap to refit when the football changes: one CI step plays the sample, and `eleven-sim quicksim fit` refits it.

## Consequences

- **Easier:** M6's competitions can resolve every unplayed fixture at once, on any device.
- **Harder:**
  - **Within-match detail is thin.** The result gives each goal a time but no scorer (0), and fouls and cards as counts; it has no metrics, injuries or substitutions. M6's career statistics, scorers among them, will need either a scorer model on top or a coarser simulation.
  - **Drift:** a change to the football that moves its scores leaves the quick-sim's reference behind. CI's calibration step plays the sample afresh and diffs it, so the drift shows as a reference change in the PR that causes it. The refit and its comparison then follow.
- **Reversing it costs little:** callers depend only on `QuickSim.Play`'s signature and the result contract.

## Enforcement

- **`QuickSimTests`:**
  - determinism;
  - the contract's result;
  - isolation from a played match on the same seed;
  - the reference equals the fit to the sample;
  - the distribution comparison: means within standard errors, and χ² at 1 % for win/draw/loss and goals a match.
- **CI (`simulation.yml`, "Calibrate the quick-sim"):** it plays the sample and diffs it against `quicksim-calibration-reference.csv`, then prints the fit, the comparison and the throughput.
- **The analyzer:** it keeps `QuickSim` inside the simulation's banned-API policy, with no threads, clocks or `System.Math`.

## Approval

Opus, 2026-11-18, under #80's instruction that the quick-sim be selected by measured throughput.
