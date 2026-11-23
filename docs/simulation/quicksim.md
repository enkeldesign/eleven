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
  - **Between tiers** (#163): a side's tier may lie anywhere from 0 to 3, and its rating is the line between the two tiers' ratings. A whole tier rates exactly as before, so no existing result moves.
  - **The capability index** is the mean, over every capability, of its outfielders' difference from the reference as a share of the capability's range. Kick error counts the other way round, since less is better.
- **The scoreline** is one draw against the Dixon–Coles grid, normalised.
  - **The grid's size:** each side's grid holds at least 12 goals, and grows with its rate until the Poisson tail beyond is under 10⁻¹². At 600 s halves an elite side's rate is about 12.5, and the grid holds its goals.
  - **ρ's bounds:** ρ is bounded for the match's rates, max(−1/λ_h, −1/λ_a) ≤ ρ ≤ min(1/(λ_h·λ_a), 1), so no scoreline's chance is negative at long halves. At the reference half the fitted ρ is inside them for every pair of tiers. τ(0,0) = 1 − λ_h·λ_a·ρ, τ(0,1) = 1 + λ_h·ρ, τ(1,0) = 1 + λ_a·ρ and τ(1,1) = 1 − ρ.
- **Each goal** falls at a uniform moment of the two halves, in order. Its scorer is 0: no footballer plays.
- **Fouls, cautions and sendings-off** are Poisson a side at the fitted means.
- **A knockout** (`knockout`, #163) that is level after two halves goes on, as the full simulation's does (#111):
  - **Extra time:** two halves of `extraTimeShare` of a half, at the same rates; its goals fall in halves 3 and 4.
  - **A shootout:** if still level, the home side kicks first, then in turn. Each kick goes in at the model's `ShootoutConversion`. It is over once one side can't be caught in the first `shootoutKicks` each, or after them once both have taken as many and one has scored more. Past 1,000 kicks, a chance of about 10⁻⁹⁹, the home side is given it, so a knockout always ends.
  - **The result** carries `ExtraTime`, the shootout's kicks and goals, and the time played.
  - **Draws:** extra time and the shootout draw on their own counters, so the ninety minutes, fouls and cards are a league match's for the same seed.
  - **Conversion:** 77.3 %, the full simulation's. #122's penalty benchmark at eleven a side, over 160 shootouts, scored 1,450 of 1,875 kicks (± 1.0 %).
- **The result's other fields are 0.** That covers penalties, injuries, substitutions, restarts and the match metrics.

**Deterministic (INV-4, INV-6):**
- every draw is counter-based on its own stream, `RandomStream.QuickSim`, at tick 0, keyed by what it is for;
- the maths is the portable `SimMath.Exp` and basic operations.

**Isolated:** it reads the two sides' data, the config's half and the seed, and nothing of any `Simulator`. A test interleaves it with a full match on the same seed: neither changes the other.

### The reference

`QuickSimModel.Reference`, the fit to the calibration sample, with each parameter's standard error:

| Parameter | Value | Standard error |
| --- | --- | --- |
| μ (log goals a side, sides level) | 0.157396 | 0.040 |
| h (home) | 0.025975 | 0.051 |
| Tier ratings: apprentice, club, pro, elite | 0, 0.230584, 0.243057, 0.706305 | 0, 0.050, 0.050, 0.050 |
| w (capability index) | 9.166028 | 0.472 |
| ρ | −0.014 | 0.064 |
| Fouls, cautions, sendings-off a side | 1.847, 0.438, 0.075 | |

**What the fit reads from the full simulation:**
- **No home advantage:**
  - h is within one standard error of 0.
  - That is what a simulation that treats both sides alike implies: home only kicks off first.
  - The two 60-match fits before #151 had h at +0.18 ± 0.15 and −0.32 ± 0.15: noise.
- **The tiers:**
  - The apprentice tier is clearly below the rest, and the elite tier clearly above.
  - Club (0.231 ± 0.050) and pro (0.243 ± 0.050) play alike: their ratings overlap.
  - The order of those two isn't resolved, and a bigger sample won't separate tiers that play the same. Telling them apart is M7's tuning of the CPU tiers.
- **Capabilities matter:**
  - A side at +0.05 on the capability index rates 0.46 higher: about 1.6 times the goals against a level side.
  - w is resolved to ± 5 %, now that tier and capabilities vary independently.
  - Before #151 it rested on two sides (−2.1 ± 3.7, then 10.9 ± 3.7).
- **ρ is within one standard error of 0:** the full simulation's low scores are about as independent Poisson goals give.

These are measures of the game as it is, kept as they are. Tuning the CPU tiers is M7's.

## Calibration

**The calibration league** (#151): twelve invented sides, each CPU tier at three capability levels, −0.05, 0 and +0.05 on the capability index.
- **How a level is set:** every capability is moved by that share of its range (kick error the other way) and held within the range. For example, ±0.7 m/s of top speed and ±0.05 on each fraction; kick power is already at its maximum.
- **Why:** tier and capabilities vary independently, so the fit can tell them apart.

**The sample:** `eleven-sim quicksim <dir> [rounds] [first]` plays the full simulation over the league, in parallel.
- **Rounds:** each round is every ordered pair once, on fixed seeds. Four rounds make 528 matches, about 28 minutes on four cores.
- **The record:** the matches are written in the gate's CSV form, and `quicksim-calibration-reference.csv` is the record.
- **CI's budget:** CI plays the first 60 fixtures (`quicksim quicksim 4 60`, about three minutes) and diffs them against the reference's first 60 rows.
  - The fixtures go round by round, so the prefix spreads over the pairs.
  - A change to the football moves every match, so the prefix shows any drift.
  - The whole sample is regenerated, and the reference refitted, with the baselines.

**The fit:** `eleven-sim quicksim fit <csv>` refits the reference:
- μ, h, the tier ratings and w by Poisson maximum likelihood, with Newton's method on a concave likelihood;
- ρ by the Dixon–Coles likelihood over those rates, on a grid of 0.001;
- the foul and card means.

It prints each parameter with its standard error and the comparison.
- **μ, h, the tier ratings and w:** the standard errors come from the inverse of the Poisson likelihood's Hessian at the fit.
- **ρ:** its standard error comes from its log-likelihood's curvature.
- **The checks:** `QuickSimTests` checks that the reference equals the fit. It also checks that the errors cover a model the quick-sim draws from, and halve at four times the sample.

**The comparison** (`QuickSimTests`): the quick-sim is drawn 50 times over each of the sample's fixtures.

| Measure | Test | Tolerance |
| --- | --- | --- |
| Home goals, away goals, cautions and fouls a match | Mean against the sample's | Within 2 standard errors of the sample's mean |
| Win/draw/loss | Two-sample χ² | At 1 % (9.21, 2 df) |
| Goals a match, in bins 0 to 6+ | Two-sample χ² | At 1 % (16.81, 6 df) |
| Each side's goal difference a match | Mean against the sample's | Within 3 standard errors of the sample's, 88 matches a side |

At the reference, against the full simulation's 528 matches:

| | Full simulation | Quick-sim (26,400) |
| --- | --- | --- |
| Home goals | 1.48 | 1.48 |
| Away goals | 1.44 | 1.44 |
| Home wins | 41 % | 41 % |
| Draws | 19 % | 19 % |
| Cautions a match | 0.88 | 0.88 |

**The sample's limits:**
- 528 matches resolve h, w and the tiers' ratings to about ± 0.05 on the log scale. They don't resolve scorers, minutes or fine scoreline structure, which the model doesn't have (ADR 0004).
- A league of different sides would rate differently. M6's sides should fall inside the calibration league's tiers and capability levels, where the fit has data.

## Eleven a side

The reference was fitted at 5v5, the calibration league's size. The world's clubs play 11v11 (#163), where the full simulation scores about half as often. `QuickSimModel.ElevenASide` is the model for 11v11 matches; `QuickSimModel.For(perSide)` picks it from ten outfielders a side.

**The sample:** `eleven-sim quicksim career <dir> [count]` plays the full simulation over 480 fixtures between the reference world's clubs (seed `0x157`).
- **Fixtures:** every 17th of all 1,260 ordered pairs, so every club plays 10 to 20 times at home and away, and every gap between divisions is covered both ways round.
- **Conditions:** 180 s halves, every brain running, each fixture on its own seed.
- **Cost:** about 25 minutes on four cores.
- **The record:** `quicksim-career-reference.csv`, with a hash of each result's text, so any change in a match shows. CI plays the first 8 fixtures and diffs them against its first rows.

**The fit:** `eleven-sim quicksim career-fit <csv>` refits it, and `CareerQuickSimTests` checks that the model equals the fit.
- **What stays:** the reference's tier ratings and capability weight, in proportion, so the 5v5 calibration's ordering of sides stands.
- **What is refitted:**
  - μ, h and a scale s on the ratings, by Poisson maximum likelihood with their errors;
  - ρ by the Dixon–Coles likelihood;
  - the foul and card means.

| | Eleven a side | Reference (5v5) |
| --- | --- | --- |
| μ | −0.569 ± 0.061 | 0.157 |
| h | −0.072 ± 0.080 | 0.026 |
| s | 1.005 ± 0.073 | 1 |
| ρ | 0.098 ± 0.102 | −0.014 |
| Fouls, cautions, sendings-off a side | 2.29, 0.54, 0.065 | 1.85, 0.44, 0.075 |

**What the fit says:**
- **Scoring:** the world's clubs score at about half the 5v5 rate (exp(−0.73) ≈ 0.48).
- **Separation:** they separate exactly as the reference predicts, with s within its error of 1.
- **Home advantage:** h is within its error of none, as the full simulation has no home advantage.

**Against the sample**, the quick-sim drawn 100 times per fixture, by the gap between the clubs' divisions (away's less home's):

| Gap | Matches | Full: goals | Full: home W/D | Quick: goals | Quick: home W/D |
| --- | --- | --- | --- | --- | --- |
| −2 | 48 | 0.25–1.48 | 8 %/21 % | 0.21–1.41 | 7 %/25 % |
| −1 | 108 | 0.39–0.98 | 17 %/32 % | 0.35–0.91 | 16 %/36 % |
| 0 | 149 | 0.49–0.49 | 25 %/49 % | 0.56–0.60 | 28 %/41 % |
| +1 | 117 | 0.88–0.44 | 48 %/32 % | 0.83–0.39 | 44 %/37 % |
| +2 | 58 | 1.31–0.19 | 60 %/33 % | 1.33–0.23 | 65 %/27 % |
| All | 480 | 0.64–0.65 | 31 %/36 % | 0.64–0.65 | 32 %/36 % |

**The tests** (`CareerQuickSimTests`):
- mean home and away goals within 2 standard errors of the sample's;
- win, draw and loss by a two-sample χ² at 1 %;
- a fit that recovers a model it draws from, within 3 of its errors.

**Before the fit**, on 240 of these matches, the reference scored 2.80 goals a match against the full simulation's 1.35, with 21 % draws against 35 %.
