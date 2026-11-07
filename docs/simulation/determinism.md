# Simulation determinism contract (spike)

The rules the engine-free simulation follows so that the same code, config, seed and commands give the same per-tick hashes. Written for issue [#2](https://github.com/enkeldesign/eleven/issues/2). The maths model is [ADR-0002](../adr/0002-simulation-maths-model.md): one implementation, `Portable`, in every build. The cross-runtime evidence behind it is on [#5](https://github.com/enkeldesign/eleven/issues/5).

## What must match

- **Within one runtime:** same code, config, seed and commands give identical hashes on every tick, and identical events. Run-twice and snapshot/restore tests check exactly this.
- **Between runtimes:** the same, required (INV-4, ADR-0002). Runtimes are compared on the complete per-tick hash sequence, through its digest, not only the final state. CI fails a build whose WebGL digest for the scenario differs from .NET's.

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

Inside a step: resolve commands → count down kick and touch cooldowns, move players, update headings → aftertouch (open windows shape the ball, then count down) → kicks (accept intents into wind-ups, count them down, connect or miss) → ball-control touches under the FREE model, at most one a step ([dribble.md](dribble.md)) → integrate the ball → check the goal line → advance the tick. The kick's timers are described in [kick.md](kick.md).

The tick rate lives in config (default 60 Hz); the step length is `1 / TickRate`.

## Commands

A command is `(controller id, tick, move vector, kick intent or none, aftertouch vector, quick pass)`, recorded before any assistance (INV-10). A kick intent is a direction, a strength and a loft ([kick.md](kick.md)). A quick pass asks the footballer to pick, lead and weight a pass inside the simulation ([pass.md](pass.md)). A manual kick intent in the same command takes precedence.

- A command controls the player whose `OwnerControllerId` equals its controller id. Ownership is part of state. A command for a controller that owns no player is rejected as `Unowned`.
- A command applies only at its own tick. Passed to a step after its tick, it is `Stale`; before, `Early`.
- Two or more commands for the same controller at the same tick are all rejected as `Duplicate`. Resolving them in submission order would make the result depend on the order an adapter happened to use.
- A non-finite value anywhere (move, aftertouch, or any part of the kick intent) is `Invalid`. A move or aftertouch vector longer than 1 is scaled to length 1, and a kick's strength and loft are clamped to 0–1.
- Accepted commands are applied in ascending controller id. Submission order never matters.
- A player with no accepted command in a tick gets a neutral command: no move, no kick, no aftertouch.
- Every rejection emits a `CommandRejected` event naming the controller and the reason.

## Events

`Kicked` (subject: player), `Bounced` (ball), `GoalLineCrossed` (ball), `CommandRejected` (controller), `KickMissed` (player: a wind-up ended with the ball out of reach), `QuickPassResolved` (player; `Other` is the chosen receiver, or 0 for a free pass). They're the simulation's internal events, not the match-result contract (INV-14).

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

No generator state survives a draw, so an extra draw in one stream can't shift another stream's values. Streams in use: `KickDirection = 1`, the kicker's execution error. `KickSpin = 2` is reserved: the spike's random kick sidespin, no longer drawn since #37, and not reused. The seed is part of state.

## Config

`SimConfig` is immutable and versioned (`FormatVersion = 7`; version 2 dropped the maths backend, version 3 moved player movement out to each player's capabilities, version 4 added the kick's wind-up and aftertouch and moved its direction error to the player, version 5 added bounce spin retention, version 6 added the quick pass's range and weight, and version 7 added the ball control model and its touch values, #59). Its hash is FNV-1a 64 over its canonical encoding: the format version, then every field in declaration order (`SimConfig.Encode`). The default physics values are placeholders that exercise the code, not tuned feel. The ball-feel values tools may vary, with their valid and research-derived starting ranges, are `SimConfig.Tunables`, described with the ball model in [ball.md](ball.md). Per-player capabilities are state, not config: see [movement.md](movement.md), [kick.md](kick.md) and [pass.md](pass.md).

## Canonical encoding

Used for state hashes, the config hash and snapshots (`CanonicalWriter`).

- Integers: little-endian two's complement, 4 bytes (`int32`) or 8 bytes (`int64`, `uint64`).
- Doubles: the IEEE-754 binary64 bit pattern, little-endian. The exact bits are kept, so −0 and +0 encode differently. They can behave differently later (in `atan2`, for example), so they count as different state.
- Booleans: one byte, 0 or 1.
- Strings (snapshot header only): an int32 byte count, then UTF-8.
- Arrays: an int32 count, then the elements.
- No padding, field names or tags. The order is fixed and documented in `SimState.Encode`: tick, seed, ball (id, position, velocity, spin, beyond-goal-line), player count, then each player (id, owner controller id, team id, capabilities (max speed, acceleration, kick power, kick error, pass vision, pass anticipation), position, velocity, heading, kick cooldown, wind-up ticks, wind-up direction, strength, loft and target, aftertouch ticks, aftertouch direction).
- **NaN and ±Infinity are refused.** Encoding throws, so a non-finite value in state can't be hashed or snapshotted silently. A simulator validates its initial state the same way.

All future-affecting state is in `SimState`; anything else that affects the next tick is in `SimConfig`. Per-step scratch (the accepted-command list, random draw counters) is rebuilt every step.

## Hashes

- **State hash:** FNV-1a 64 over the canonical state encoding. Offset basis `0xCBF29CE484222325`, prime `0x100000001B3`, one byte at a time: `h = (h ^ byte) × prime`. Every step is a bijection on the 64-bit state, so two equal-length encodings that differ in one byte always hash differently. FNV-1a is a divergence detector, not a cryptographic hash, and it is a few lines any runtime computes identically.
- **Sequence digest:** FNV-1a 64 over every tick hash in order, each as 8 little-endian bytes. Two runs agree on the digest only if they agree on every tick, so a divergence that later converges still shows.

## Snapshots

Magic `ELSN`, state schema version (int32; version 2 dropped the maths backend, version 3 added player capabilities, version 4 added kick capabilities and the kick's wind-up and aftertouch phases, and version 5 added each player's team, pass capabilities and quick-pass target), build id (string), tick convention (string), config hash (uint64), seed (uint64), tick (int64), then the canonical state.

Restoring checks the schema version, tick convention and config hash, then builds a fresh `Simulator`, which refuses kick state the config couldn't have produced: a wind-up, window or cooldown longer than the config's, or a pass target outside a wind-up or off the team. Player ids are positive, because a target of 0 means none. The build id is recorded but not enforced: states are meant to migrate (INV-7), and there is no migration yet beyond the schema version. The build id is the simulation assembly's informational version, which the .NET SDK suffixes with the source commit.

## Command recordings

A `CommandRecording` (harness) is a session's owned commands exactly as they were passed to the simulation, before any assistance (INV-10), with the snapshot the session started from. Replaying it reproduces the session tick for tick under the same build and config, and `Replay` can report every tick's hash.

Encoding, canonical:
1. Magic `ELRC`, format version (int32, now 3), snapshot length (int32) and bytes, step count (int32).
2. Then, per step, a command count (int32), and per command:
   - controller id (int32) and tick (int64);
   - move x and y;
   - whether there is a kick intent (bool), and if so its direction x and y, strength and loft;
   - aftertouch x and y;
   - quick pass (bool).

Every double is its raw IEEE-754 bits (uint64), so even invalid input replays as it was. Version 2 replaced version 1's kick flag with the kick intent and added aftertouch (#37); version 3 added the quick pass (#38). Recordings are version-specific and may expire (INV-7).

## Feel captures

The "this felt wrong" capture (#44, part of #27) is evidence, not telemetry: it is made and kept on the device, with no service (INV-15). It holds the last ten seconds of a run and everything needed to replay them exactly.

**The recorder.** `FeelRecorder` (harness) is fed after every step: the owned commands exactly as passed (INV-10), then the step's hash and events.
- It takes a snapshot every second and drops whatever the window no longer needs.
- So a capture covers 10–11 s, or the whole run if it is younger.
- It only reads the simulator, so recording and capturing never change a run. A test checks that runs with and without captures hash identically.

**Encoding** (`FeelCapture`), canonical:
1. Magic `ELFC` and the capture format (int32, now 1).
2. The host build and simulation build (strings), and the tick convention (string).
3. The state schema, config format and recording format (int32 each).
4. The config hash and seed (uint64), the window's start tick (int64) and its step count (int32).
5. The active preset, control candidate and note (strings).
   - **The preset** is `technical-reference` for the reference values. It is `unsaved` for any other test bench candidate ([test-bench.md](test-bench.md)), until #46 names presets.
   - **The control candidate** is `none` until #10.
6. The config payload (`SimConfig.Encode`) and the window's `CommandRecording`, each as an int32 length, then bytes.
7. The live run's tick hashes (int32 count, uint64 each): the hash at the window's start and after every step.
8. Its events (int32 count, then tick int64, and kind, subject, rejection and other as int32 each).

**Replaying** (`FeelCapture.Replay`, `eleven-sim replay <file>`):
1. **Versions first.** The versions come before the payloads, so a capture from another build is read far enough to say, by name, which version differs ("state schema 4, this build reads 5"). It is never replayed under the wrong rules.
2. **The config.** It is rebuilt from the payload with `SimConfig.Decode`, which checks its format, and must hash to the recorded hash. A capture made under a tuned candidate therefore replays under that candidate.
3. **Every tick and event.** The window replays, and every tick's hash and every event is compared. A divergence names the first tick that differs. The CLI exits 1 on any mismatch.

**In CI:**
- **The WebGL build:** the smoke test presses THIS FELT WRONG in it, and CI replays each capture with the .NET CLI. These are the bytes a player keeps: those in `localStorage` and, on desktop, SAVE CAPTURE's download, which must be identical to them.
- **Failures fail the build:** the replay step names any capture that doesn't decode or replay, and fails (#50).
- **The fixture:** `FeelCaptureTests` also replays a committed fixture, `tests/Eleven.Simulation.Tests/Fixtures/spike-felt-wrong.elfc`, made by `eleven-sim capture <file>` from the scripted spike session.

Like recordings, captures are version-specific (INV-7). A schema or format change makes the fixture test report the mismatch by name, and the fixture is then regenerated in its own commit.

## Hash logs

`eleven-sim hashlog <dir>` writes the scenario's log (`eleven-hashlog v2`). The header records the scenario name and version, build, state schema, tick convention, config hash and seed. Then come `<tick> <hash>` lines for every tick, then `# digest: …`. The format is text with LF line endings, so logs from different runtimes diff directly. CI publishes them as the `hashlogs` artifact.

## Maths boundary

`sin`, `cos`, `atan2` and `sqrt` go through `SimMath`, the only entry point, backed by `PortableMath`. Simulation code never calls `System.Math` or `MathF` for anything but exact helpers (`Abs`, `Min`, `Max`, `Floor`, `Ceiling`, `Round`, `Truncate`, `Sign`, `Clamp`, `CopySign` and the constants).

`SimMath` checks domains in front of the implementation. An out-of-domain argument throws, because a NaN would enter state silently.

| Function | Supported domain | `Portable` method | Asserted bound vs System.Math | Measured worst |
| --- | --- | --- | --- | --- |
| sin, cos | finite, abs(x) ≤ 1e5 | Cody–Waite reduction by π/2 in three parts (fdlibm constants; exact for abs(n) < 2²⁰), then Taylor series to x¹⁵ / x¹⁶ on abs(r) ≤ π/4 | 9e-16 absolute (4 ulp of 1) | 2.2e-16 |
| atan2 | finite; (0, 0) → 0 | Octant reduction to t in [0, 1], shift by π/4 above tan(π/8), one half-angle step to abs(v) ≤ 0.199, alternating series to v²³ | 1.8e-15 absolute (4 ulp of π) | 4.4e-16 |
| sqrt | finite, x ≥ 0 | Exponent-halving bit guess (within about 6 %), six Newton steps; tiny inputs pre-scaled by 2¹⁰⁰⁰ | 4.5e-16 relative (2 ulp) | 1 ulp |

The truncation errors of the series are below 5e-17 for sin, 3e-18 for cos and 1e-19 for atan, so the measured errors are rounding in the evaluation. The bounds leave a factor of 2 to 4 over the measured worst case. Measurements: 4 million arguments per function on .NET 10, x64.

`Portable` uses only IEEE-754 basic operations (+, −, ×, ÷), `Math.Round`, `Math.Abs` and bit casts, which every conforming runtime computes identically. One caveat for native builds: a compiler that contracts `a × b + c` into a fused multiply-add changes rounding. RyuJIT doesn't contract, and WebAssembly has no implicit fused multiply-add. IL2CPP's C++ compiler settings need checking when the native lane ([#6](https://github.com/enkeldesign/eleven/issues/6)) lands (ADR-0002).

## Banned APIs

The `BannedApiAnalyzer` runs as a compile-time analyzer on the simulation and harness projects, so CI fails on any violation:

- **ELEVEN001:** `System.Random`; `DateTime.Now`, `UtcNow` and `Today`; `DateTimeOffset.Now` and `UtcNow`; `Stopwatch`; `Environment.TickCount` and `TickCount64`; anything in `System.Threading` and its sub-namespaces (threads, tasks, timers, locks).
- **ELEVEN002:** any other `System.Math` or `MathF` member, anywhere in simulation code.

`BannedApiAnalyzerTests` runs it against a fixture that must be caught on every marked line, and one that must pass (exact helpers anywhere). A third test shows that no type is exempt, not even one named like the removed System.Math backend.

## Tick cost

Measured by `eleven-sim bench` in Release: 20 warm-up runs of the scenario, then 30 timed runs. Only `Simulator.Step` is timed, with commands prepared beforehand. State hashing is timed separately. CI reports the result as a check annotation. It is evidence, not a budget.
