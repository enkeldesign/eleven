# Training

M6.10b, #196. Each club trains at an intensity, Light, Normal or Hard, between its matches. Harder training develops the young faster (#194), recovers condition slower (#177) and injures more. On Auto, the manager chooses.

The parts:
- **`Club.Training`:** Auto, Light, Normal or Hard.
- **`TrainingRules`:** the knobs, all technical references, not tuned feel.
- **`Training`** (`Eleven.Career`): the manager's choice and the days between matches.
- **`CareerTraining`** (harness): the measurement, `eleven-sim training [seasons]`.

## What the research gives

- **Training injures too.** UEFA's Elite Club Injury Study (Ekstrand, Hägglund and Waldén 2011) measures about 3–4 injuries per 1,000 hours of training, against about 27 in matches, most of them minor. At about an hour and a quarter of training a day, that's about 0.004 a footballer a training day.
- **Load cuts both ways.** Gabbett (2016), the "training–injury prevention paradox": heavier training builds players, and sharp rises in load bring injuries.

## The intensities

| | Development (#194) | Recovery (#177) | Training injuries |
| --- | --- | --- | --- |
| Light | ×0.8 | ×1.2 | ×0.6 |
| Normal | ×1.0 | ×1.0 | ×1.0, `InjuryPerDay` 0.004 |
| Hard | ×1.25 | ×0.8 | ×1.5 |

The rows are `TrainingRules.Growth`, `Recovery` and `Injury`.

## The days between matches

`CareerSeasons.PlayMatchday`, after the matchday's results:
1. **Each club's intensity:** its own, or on Auto its manager's choice (`Training.Choose`):
   - Light when his XI's mean condition at this kick-off is below `LightBelow` (85);
   - Hard when it's at or above `HardFrom` (97) and the days to the next matchday are `HardGapDays` (7) or more;
   - else Normal.

   Every CPU club trains on Auto.
2. **Development** (#194) by the intensity's pace.
3. **`Training.Pass`** over the days to the next matchday:
   - the injured heal a day a day;
   - everyone recovers at the intensity's pace, `RecoveryDays` (3.5) for a match's cost at Normal;
   - each footballer fit at the start may be injured in training, by the intensity's chance each day. His layoff is drawn from the minor bands (#176: slight, minor, moderate), and he heals over the rest of the days.
   - Each injury is on the training stream's counters, keyed by the season, the matchday, the footballer and the day (INV-6).

The off-season's days stay as they were (#177): everyone heals and recovers, and nobody trains.

## The hub

- **The line:** your club's training and what it does, e.g. "Training: Hard. Faster development, slower recovery, more injuries." On Auto: "Training: Auto, your manager's choice by your XI's condition and the days to your next match."
- **Training** cycles it: Auto, Light, Normal, Hard and round again (`CareerSession.CycleTraining`).
- **An injury in training** shows on the hub's out line like any other.

## Saved

Each club not on Auto, with its training, at world format 7 ([world.md](world.md)). A format-6 world reads every club on Auto.

## Measured

`eleven-sim training 3`: a headless career from seed `0x196`, every match quick-simmed, three seasons with every club at one intensity, for each in turn, then all on Auto.

| Training | Under-24s' level a season | XI's condition at kick-off | Training injuries a club a season | Days lost a club a season | Drift by the last season |
| --- | --- | --- | --- | --- | --- |
| Light | +0.50 | 100.0 | 8.6 | 47 | −0.02 |
| Normal | +0.60 | 99.7 | 14.1 | 77 | +0.05 |
| Hard | +0.70 | 98.9 | 20.9 | 118 | +0.17 |
| Auto | +0.66 | 99.7 | 19.1 | 108 | +0.11 |

- **The trade-off holds:** Hard develops the young a third faster than Light, at two and a half times the training injuries.
- **Condition hardly separates them at kick-off.** A match's cost is recovered in 3.5 days, and most gaps are longer. The cost shows in a cup week's three days, and in a squad thinned by injuries.
- **Training injuries at Normal,** about 14 a club a season, are near UEFA's elite clubs, whose training injuries are about two in five of 50 a season.
- **Auto trains Hard in most full weeks,** since a rested XI is rarely below 97. So #194's reference growth of 1.0 drifted +0.34 by the third season, past ±0.3.
  - **The fix:** `GrowthYoung` is 0.85.
  - **The result:** Auto drifted +0.26 by the third season, and Hard everywhere +0.30.
  - **With contracts (#201),** the released are replaced by youth below them: Auto drifts +0.11 and Hard +0.17, as in the table. The table counts the clubs' footballers, the free agents left out.

## Tests

`TrainingTests` (career), 5:
- each intensity's development, recovery and injury paces, and three days' recovery by a club's;
- a training injury likelier the harder, its layoff in the minor bands, never for the injured;
- Auto's choice at each boundary;
- the same days twice, the same injuries;
- the world's bytes at format 7, and a format-6 world read on Auto.

`TrainingSeasonTests` (harness), 4:
- a club's intensity shaping its matchday's growth and recovery;
- on Auto, every club as its manager chooses;
- the hub's cycle and line;
- the measurement's order of the intensities, and Auto within ±0.3.
