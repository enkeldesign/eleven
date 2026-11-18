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
| μ (log goals a side, sides level) | 0.185773 |
| h (home) | 0.182322 |
| Tier ratings: apprentice, club, pro, elite | 0, 0.378836, 0.253575, 0.951047 |
| w (capability index) | −2.076960 |
| ρ | 0.199 |
| Fouls, cautions, sendings-off a side | 2.85, 0.725, 0.1 |

**What the fit reads from the full simulation:**
- **The tiers aren't linear:** over this sample the club sides do better than the pro ones, and the elite side is well clear of all. So each tier has its own rating rather than one slope.
- **The capability weight is weakly identified:** only two sides differ from the reference in capabilities.
  - Stonegate, the strong club side, did worse than its tier-mate.
  - Northfield, the quick pro side, did better than its tier-mate, but its index is small.
  - The fit weighs Stonegate's larger index, so w comes out negative.
- **ρ is positive:** the full simulation draws less often than independent Poisson goals would (12 % of the sample).

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
| Home goals | 1.60 | 1.61 |
| Away goals | 1.33 | 1.35 |
| Home wins | 50 % | 46 % |
| Draws | 12 % | 17 % |
| Cautions a match | 1.45 | 1.47 |

**The sample's limits:** 60 matches resolve means and the gross shape, not fine scoreline structure. M6, which uses it at scale, can grow the sample: CI plays it on four cores in about four minutes.
