# Simulation determinism contract (spike)

The rules the engine-free simulation follows so that the same code, config, seed and commands give the same per-tick hashes. Written for issue [#2](https://github.com/enkeldesign/eleven/issues/2). It covers the spike skeleton, not ELEVEN's final maths model (ADR-0002, [#8](https://github.com/enkeldesign/eleven/issues/8)) or cross-runtime acceptance of INV-4 ([#5](https://github.com/enkeldesign/eleven/issues/5)).

## What must match

- **Within one maths backend:** same code, config (so same backend), seed and commands give identical hashes on every tick, and identical events. Run-twice and snapshot/restore tests check exactly this.
- **Between backends:** nothing is required. `SystemMath` and `Portable` may hash differently. The test suite only checks that they stay within rounding of each other, as evidence that the portable backend isn't broken.
- **Between runtimes:** compare like with like. The same backend and config on two runtimes, comparing the complete per-tick hash sequence rather than only the final state.

## Projects

| Project | Target | Role |
| --- | --- | --- |
| `src/Eleven.Simulation` | netstandard2.1, C# 9 | The simulation. Banned-API analyzer applied |
| `src/Eleven.Simulation.Harness` | netstandard2.1, C# 9 | Scripted scenario, runner, hash log. Reusable by the Unity host ([#3](https://github.com/enkeldesign/eleven/issues/3)). Analyzer applied |
| `src/Eleven.Simulation.Analyzers` | netstandard2.0 | The banned-API Roslyn analyzer (Microsoft.CodeAnalysis 3.8, for Unity compatibility) |
| `src/Eleven.Simulation.Cli` | net10.0 | Headless harness: writes hash logs, measures tick cost. The only place `Stopwatch` is used |
| `unity/` | Unity 6000.3 | The spike host. Compiles the simulation and harness from the same sources as local packages; `noEngineReferences` on the simulation's assembly definition ([unity/README.md](../../unity/README.md)) |

## Ticks

`tick-v1`: state N is the state after N steps. Step N applies the commands whose tick is N and turns state N into state N+1. Hash N is the hash of state N, so a 60-second run at 60 Hz has 3601 hashes, ticks 0 to 3600.

Inside a step: resolve commands → count down kick cooldowns, move dots, update headings → apply kicks → integrate the ball → check the goal line → advance the tick.

The tick rate lives in config (default 60 Hz); the step length is `1 / TickRate`.

## Commands

A command is `(controller id, tick, move vector, kick)`, recorded before any assistance (INV-10).

- A command controls the dot whose `OwnerControllerId` equals its controller id. Ownership is part of state. A command for a controller that owns no dot is rejected as `Unowned`.
- A command applies only at its own tick. Passed to a step after its tick, it is `Stale`; before, `Early`.
- Two or more commands for the same controller at the same tick are all rejected as `Duplicate`. Resolving them in submission order would make the result depend on the order an adapter happened to use.
- A non-finite move vector is `Invalid`. A move vector longer than 1 is scaled to length 1.
- Accepted commands are applied in ascending controller id. Submission order never matters.
- A dot with no accepted command in a tick gets a neutral command: no move, no kick.
- Every rejection emits a `CommandRejected` event naming the controller and the reason.

## Events

`Kicked` (subject: dot), `Bounced` (ball), `GoalLineCrossed` (ball), `CommandRejected` (controller). They're the simulation's internal events, not the match-result contract (INV-14).

**Goal-line crossing** is geometry only. The ball is beyond the line when it is wholly past it: `x − radius > GoalLineX`. `BallState.BeyondGoalLine` records whether that held after the previous step. The event fires only on the step where it goes from false to true, so a ball resting beyond the line emits one event, not one per tick. A ball that comes back behind the line clears the flag and can cross again.

## Randomness

Counter-based (INV-6). Each draw is a pure function:

```
h = mix(seed ^ 0x9E3779B97F4A7C15)
h = mix(h ^ tick)
h = mix(h ^ (stream << 32 | entity))
h = mix(h ^ draw)
uniform = (h >> 11) × 2⁻⁵³        // in [0, 1)
```

`mix` is the SplitMix64 finaliser: `z = (z ^ z>>30) × 0xBF58476D1CE4E5B9; z = (z ^ z>>27) × 0x94D049BB133111EB; z ^ z>>31`, on unsigned 64-bit integers with wrap-around. `stream` and `entity` are 32-bit, `draw` is the draw's index within that stream, entity and tick, starting at 0.

No generator state survives a draw, so an extra draw in one stream can't shift another stream's values. Streams in use: `KickDirection = 1`, `KickSpin = 2`. The seed is part of state.

## Config

`SimConfig` is immutable and versioned (`FormatVersion = 1`). Its hash is FNV-1a 64 over its canonical encoding: the format version, then every field in declaration order (`SimConfig.Encode`). The maths backend is a config field, so it is part of the hash. The default physics values are placeholders that exercise the code, not tuned feel.

## Canonical encoding

Used for state hashes, the config hash and snapshots (`CanonicalWriter`).

- Integers: little-endian two's complement, 4 bytes (`int32`) or 8 bytes (`int64`, `uint64`).
- Doubles: the IEEE-754 binary64 bit pattern, little-endian. The exact bits are kept, so −0 and +0 encode differently. They can behave differently later (in `atan2`, for example), so they count as different state.
- Booleans: one byte, 0 or 1.
- Strings (snapshot header only): an int32 byte count, then UTF-8.
- Arrays: an int32 count, then the elements.
- No padding, field names or tags. The order is fixed and documented in `SimState.Encode`: tick, seed, ball (id, position, velocity, spin, beyond-goal-line), dot count, then each dot (id, owner controller id, position, velocity, heading, kick cooldown).
- **NaN and ±Infinity are refused.** Encoding throws, so a non-finite value in state can't be hashed or snapshotted silently. A simulator validates its initial state the same way.

All future-affecting state is in `SimState`; anything else that affects the next tick is in `SimConfig`. Per-step scratch (the accepted-command list, random draw counters) is rebuilt every step.

## Hashes

- **State hash:** FNV-1a 64 over the canonical state encoding. Offset basis `0xCBF29CE484222325`, prime `0x100000001B3`, one byte at a time: `h = (h ^ byte) × prime`. Every step is a bijection on the 64-bit state, so two equal-length encodings that differ in one byte always hash differently. FNV-1a is a divergence detector, not a cryptographic hash, and it is a few lines any runtime computes identically.
- **Sequence digest:** FNV-1a 64 over every tick hash in order, each as 8 little-endian bytes. Two runs agree on the digest only if they agree on every tick, so a divergence that later converges still shows.

## Snapshots

Magic `ELSN`, state schema version (int32), build id (string), tick convention (string), config hash (uint64), maths backend (int32), seed (uint64), tick (int64), then the canonical state.

Restoring checks the schema version, tick convention, config hash and backend, then builds a fresh `Simulator`. The build id is recorded but not enforced: states are meant to migrate (INV-7), and there is no migration yet beyond the schema version. The build id is the simulation assembly's informational version, which the .NET SDK suffixes with the source commit.

## Hash logs

`eleven-sim hashlog <dir>` writes one log per backend. The header records the scenario name and version, build, state schema, tick convention, config hash, maths backend and seed. Then come `<tick> <hash>` lines for every tick, then `# digest: …`. The format is text with LF line endings, so logs from different runtimes diff directly. CI publishes them as the `hashlogs` artifact.

## Maths boundary

`sin`, `cos`, `atan2` and `sqrt` go through `SimMath`, the only entry point. Simulation code never calls `System.Math` or `MathF` for anything but exact helpers (`Abs`, `Min`, `Max`, `Floor`, `Ceiling`, `Round`, `Truncate`, `Sign`, `Clamp`, `CopySign` and the constants). `SystemMathBackend` is the one type allowed to call the rest.

`SimMath` checks domains itself, so both backends fail identically. An out-of-domain argument throws, because a NaN would enter state silently.

| Function | Supported domain | `Portable` method | Asserted bound vs System.Math | Measured worst |
| --- | --- | --- | --- | --- |
| sin, cos | finite, abs(x) ≤ 1e5 | Cody–Waite reduction by π/2 in three parts (fdlibm constants; exact for abs(n) < 2²⁰), then Taylor series to x¹⁵ / x¹⁶ on abs(r) ≤ π/4 | 9e-16 absolute (4 ulp of 1) | 2.2e-16 |
| atan2 | finite; (0, 0) → 0 | Octant reduction to t in [0, 1], shift by π/4 above tan(π/8), one half-angle step to abs(v) ≤ 0.199, alternating series to v²³ | 1.8e-15 absolute (4 ulp of π) | 4.4e-16 |
| sqrt | finite, x ≥ 0 | Exponent-halving bit guess (within about 6 %), six Newton steps; tiny inputs pre-scaled by 2¹⁰⁰⁰ | 4.5e-16 relative (2 ulp) | 1 ulp |

The truncation errors of the series are below 5e-17 for sin, 3e-18 for cos and 1e-19 for atan, so the measured errors are rounding in the evaluation. The bounds leave a factor of 2 to 4 over the measured worst case. Measurements: 4 million arguments per function on .NET 10, x64.

`Portable` uses only IEEE-754 basic operations (+, −, ×, ÷), `Math.Round`, `Math.Abs` and bit casts, which every conforming runtime computes identically. One caveat for [#5](https://github.com/enkeldesign/eleven/issues/5): a native compiler that contracts `a × b + c` into a fused multiply-add changes rounding. RyuJIT doesn't contract. IL2CPP's C++ compiler settings need checking.

## Banned APIs

The `BannedApiAnalyzer` runs as a compile-time analyzer on the simulation and harness projects, so CI fails on any violation:

- **ELEVEN001:** `System.Random`; `DateTime.Now`, `UtcNow` and `Today`; `DateTimeOffset.Now` and `UtcNow`; `Stopwatch`; `Environment.TickCount` and `TickCount64`; anything in `System.Threading` and its sub-namespaces (threads, tasks, timers, locks).
- **ELEVEN002:** any other `System.Math` or `MathF` member outside `Eleven.Simulation.Maths.SystemMathBackend`.

`BannedApiAnalyzerTests` runs it against a fixture that must be caught on every marked line, and one that must pass: System.Math inside the backend and exact helpers anywhere. A third test shows the exception doesn't extend to other types.

## Tick cost

Measured by `eleven-sim bench` in Release: both backends warmed up for 20 full runs, then 30 timed runs each, alternating. Only `Simulator.Step` is timed, with commands prepared beforehand. State hashing is timed separately. CI reports the result as a check annotation. It is evidence, not a budget.
