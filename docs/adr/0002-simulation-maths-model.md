# ADR-0002: Simulation maths model

- **Status:** Accepted
- **Date:** 2026-11-01
- **Deciders:** Opus proposes; Erik approves (issue #8 groups it with ADR-0001)
- **Supersedes / superseded by:** —

## Context

INV-4 requires same-build determinism across every shipped runtime and headless. The brief left the maths model open: `System.Math`, our own trig, or fixed point. Its condition was that the spike measures how much is needed and what it costs (brief v0.3, "Where Opus disagrees", point 3).

The spike built a maths boundary (`SimMath`, [docs/simulation/determinism.md](../simulation/determinism.md)) with two interchangeable backends:

- **`SystemMath`:** `System.Math.Sin`, `Cos`, `Atan2` and `Sqrt`.
- **`Portable`:** our own `sin`, `cos` and `atan2` built only from IEEE-754 basic operations (+, −, ×, ÷), `Math.Round` and bit casts, with documented domains and error bounds.

The issue 1 scenario runs for 60 s of simulated time (3601 ticks) and is hashed every tick. Its sequence digest on each runtime, from issues #3 and #5:

| Runtime | `Portable` | `SystemMath` |
| --- | --- | --- |
| .NET 10, x64 (CI) | `e9e470a94550ecb7` | `34daafc472806d33` |
| Unity 6000.3 WebGL, headless Chromium (CI, every build) | `e9e470a94550ecb7` | `5d8b6374860e3a0e` |
| Unity 6000.3 WebGL, Safari, iPhone 16, iOS 26.6.2 (builds 7, 16 and 24) | `e9e470a94550ecb7` | `5d8b6374860e3a0e` |

- `Portable` is identical everywhere it has run.
- `SystemMath` diverges between .NET and WebGL. The two browser engines agree with each other only because the Web build ships its own maths library.

**Cost:**

| Measurement | `Portable` | `SystemMath` |
| --- | --- | --- |
| `Simulator.Step`, .NET x64 in CI (`eleven-sim bench`) | 125 ns/tick | 99 ns/tick |
| Whole scenario on the iPhone, hashing included | 9 ms for 3601 ticks (≈2.5 µs/tick) | 6 ms |

Both are negligible against a 16.7 ms frame.

## Decision

The simulation uses the **`Portable`** maths backend: our own transcendental functions built from IEEE-754 basic operations.

- **One entry point:** every `sin`, `cos`, `atan2` and `sqrt` in simulation code goes through `SimMath`, and `SimMath` uses `Portable` in every shipped build. New transcendental functions are added to `Portable`, with a stated domain, an error bound against `System.Math` and a test, before simulation code may use them.
- **Doubles, not fixed point:** state stays in `double`, because `Portable` doubles already agree across the runtimes we ship.
- **`SystemMath` is removed** from the simulation once this ADR is accepted, together with the maths switch in the spike host. The `MathBackend` config field goes with it. That changes the config hash, so the hash baselines change in a separate commit with that reason.
- **Cross-runtime hashes are a gate, not a report.** CI compares the WebGL build's per-tick hash digest with .NET's on every build, and a mismatch fails the build. In the spike it was only a warning. The gate is implemented by #22, together with removing `SystemMath`. Until #22 merges, a mismatch is still only a warning.

## Alternatives considered

- **`System.Math` (the platform's maths library).**
  - Lost on evidence: it already diverges between .NET and WebGL on a 60-second scenario.
  - Every new runtime (native iOS, another browser engine, a future console) would be another library to trust.
  - Its only gain is about 25 ns a tick.
- **Fixed point.**
  - Not needed: `Portable` doubles agree across every runtime measured.
  - It would cost a numeric type through the whole simulation, careful overflow and precision work on ball physics, and slower, harder-to-read code.
  - It is the fallback if a future runtime breaks IEEE-754 double behaviour, for example by contracting `a × b + c` into a fused multiply-add.

## Consequences

**Easier:**
- One maths model, and determinism is checked on every build.
- Replays, "this felt wrong" captures (Slice 1, issue 13) and hash-based tests mean the same thing in the browser and in CI.

**Harder:**
- Every transcendental function the simulation needs is ours to write, bound and test, e.g. `exp` and `pow` for drag or spin decay.
- The banned-API analyzer already prevents bypassing `SimMath`.

**Open risk, native builds:**
- A native compiler may contract `a × b + c` into a fused multiply-add, which changes rounding. Apple's clang does this by default for C and C++, and IL2CPP compiles to C++.
- The native iOS lane (#6) is deferred, so this is unverified.
- When it lands, its determinism row must match before it ships. If it doesn't, the fix is a compiler flag (`-ffp-contract=off`) or, failing that, fixed point.
- WebAssembly has no implicit fused multiply-add, so the Web build is not affected.

**Cost to reverse:** low. The backend sits behind one entry point, and switching back is a config change plus new hash baselines.

## Enforcement

- **The banned-API analyzer:**
  - **ELEVEN001:** banned non-deterministic APIs.
  - **ELEVEN002:** `System.Math` or `MathF` outside the maths backend.
  - Once `SystemMathBackend` is removed, no simulation type may call them except for exact helpers.
- **`Portable`'s tests:** domains, error bounds and special values.
- **CI cross-runtime gate (from #22):** the WebGL digest must equal the .NET digest, on every PR and on `main`.
- **On devices:** each new shipped runtime gets a determinism-matrix row on #5, or its successor, before release.

## Approval

Approved by Erik on 1 Nov 2026, together with ADR-0001 (PR #20).
