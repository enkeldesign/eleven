# The quick-sim

Issue #129, M5 (#80): a calibrated result for matches nobody plays, such as a league's other fixtures. INV-1 permits this one approximation, and only calibrated against the simulation. The approach was chosen by measured throughput ([ADR 0004](../adr/0004-quick-sim-statistical-model.md)). Every value here is a technical reference fitted to the full simulation, not approved feel.

## The measure that chose it

`eleven-sim throughput` plays full 11v11 matches headless on one core at the reference config, every brain running, and then 100,000 quick-sim matches.

| | Full simulation | Quick-sim |
| --- | --- | --- |
| .NET 10, agent sandbox | 0.107 matches/s (0.41 ms a step) | about 277,000 matches/s |
| .NET 10, CI runner | in the CI notice "Throughput" | in the same notice |
| Unity WebGL, headless Chromium on the CI runner | about 0.033 matches/s (1.32 ms a step, from the smoke's 11v11 budget) | |

M6's three tiers play their other fixtures between the human's matches: 17 a round with 12 sides a tier. The full simulation takes about 160 s a round in .NET and about 9 minutes in the CI runner's browser. The quick-sim takes well under a millisecond.

## The model

`QuickSim.Play(config, home, away, seed)` returns a `MatchResult` (INV-14). It is Dixon–Coles: each side's goals are Poisson, with a low-score correction ρ.

- **Rates**, for a match of reference halves (180 s), scaled by the half's length:
  - home λ = exp(μ + h + r_home − r_away);
  - away λ = exp(μ − (r_home − r_away)).
  - **Bounded:** each rate is at most `QuickSim.MaxRate`, 30 goals a match, and its exponent lies within ±40. The calibrated rates are a few goals a match. With the fitted weight, though, valid sides at the capability extremes would extrapolate to tens of thousands. The bound keeps the sampler's maths in range for any valid sides (Codex's finding on #150, `QuickSimTests`).
- **A side's rating** r = its CPU tier's rating (apprentice 0, club, pro, elite) + w × its capability index.
  - **The capability index** is the mean, over every capability, of its outfielders' difference from the reference as a share of the capability's range. Kick error counts the other way round, since less is better.
- **The scoreline** is one draw against the Dixon–Coles grid, normalised.
  - **The grid's size:** each side's grid holds at least 12 goals, and grows with its rate until the Poisson tail beyond is under 10⁻¹². At 600 s halves an elite side's rate is about 12.5, and the grid holds its goals.
  - **ρ's bounds:** ρ is bounded for the match's rates, max(−1/λ_h, −1/λ_a) ≤ ρ ≤ min(1/(λ_h·λ_a), 1), so no scoreline's chance is negative at long halves. At the reference half the fitted ρ is inside them for every pair of tiers. τ(0,0) = 1 − λ_h·λ_a·ρ, τ(0,1) = 1 + λ_h·ρ, τ(1,0) = 1 + λ_a·ρ and τ(1,1) = 1 − ρ.
- **Each goal** falls at a uniform moment of the two halves, in order. Its scorer is 0: no footballer plays.
- **Fouls, cautions and sendings-off** are Poisson a side at the fitted means.
- **The result's other fields are 0.** That covers penalties, injuries, substitutions, restarts, extra time, the shootout and the match metrics. A quick-simmed knockout tie that ends level stays level; its competition (M6) resolves it.

**Deterministic (INV-4, INV-6):**
- every draw is counter-based on its own stream, `RandomStream.QuickSim`, at tick 0, keyed by what it is for;
- the maths is the portable `SimMath.Exp` and basic operations.

**Isolated:** it reads the two sides' data, the config's half and the seed, and nothing of any `Simulator`. A test interleaves it with a full match on the same seed: neither changes the other.

### The reference

`QuickSimModel.Reference`, the fit to the calibration sample:

| Parameter | Value |
| --- | --- |
| μ (log goals a side, sides level) | 0.439635 |
| h (home) | −0.319634 |
| Tier ratings: apprentice, club, pro, elite | 0, 0.157840, 0.341332, 1.013932 |
| w (capability index) | 10.929432 |
| ρ | −0.134 |
| Fouls, cautions, sendings-off a side | 2.525, 0.608, 0.083 |

**What the fit reads from the full simulation:**
- **The tiers rise with the tier,** and the elite side is well clear of all. Each tier has its own rating rather than one slope.
- **The sample is small for these parameters.** #129's fit, before #118, had home at +0.18, w at −2.08 and ρ at +0.199. After #118 changed the CPU's runs, the refit to a fresh sample of the same size has home at −0.32, w at 10.9 and ρ at −0.134. Some of that is the football; most of it is the noise of 60 matches.
  - **Home's advantage:** its sign flips between two samples, so it isn't resolved.
  - **The capability weight:** only two sides differ from the reference in capabilities, so w is barely identified.
  - **The comparison:** each fit still matches its own sample within the stated tolerances.
  - **What's next:** growing the sample is a follow-up for M6, which leans on the quick-sim.

These are measures of the game as it is, kept as they are. Tuning the CPU tiers is M7's.

## Calibration

**The sample:** `eleven-sim quicksim <dir>` plays the full simulation over M3's six sides, each ordered pair twice on fixed seeds: 60 matches, in parallel. They are written in the gate's CSV form. `quicksim-calibration-reference.csv` is the record, diffed in CI.

**The fit:** `eleven-sim quicksim fit <csv>` refits the reference:
- μ, h, the tier ratings and w by Poisson maximum likelihood, with Newton's method on a concave likelihood;
- ρ by the Dixon–Coles likelihood over those rates, on a grid of 0.001;
- the foul and card means.

It also prints the comparison. `QuickSimTests` checks that the reference equals the fit.

**The comparison** (`QuickSimTests`): the quick-sim is drawn 50 times over each of the sample's fixtures.

| Measure | Test | Tolerance |
| --- | --- | --- |
| Home goals, away goals, cautions and fouls a match | Mean against the sample's | Within 2 standard errors of the sample's mean |
| Win/draw/loss | Two-sample χ² | At 1 % (9.21, 2 df) |
| Goals a match, in bins 0 to 6+ | Two-sample χ² | At 1 % (16.81, 6 df) |
| Each side's goal difference a match | Mean against the sample's | Within 3 standard errors of the sample's, 20 matches a side |

At the reference, against the full simulation's 60 matches:

| | Full simulation | Quick-sim (3,000) |
| --- | --- | --- |
| Home goals | 1.28 | 1.30 |
| Away goals | 1.77 | 1.76 |
| Home wins | 33 % | 31 % |
| Draws | 22 % | 23 % |
| Cautions a match | 1.22 | 1.23 |

**The sample's limits:** 60 matches resolve means and the gross shape, not fine scoreline structure. M6, which uses it at scale, can grow the sample: CI plays it on four cores in about four minutes.
