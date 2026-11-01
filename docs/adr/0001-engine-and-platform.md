# ADR-0001: Engine and platform

- **Status:** Proposed
- **Date:** 2026-11-01
- **Deciders:** Opus recommends; Erik approves (an engine and platform decision changes the product)
- **Supersedes / superseded by:** —

## Context

Brief v0.3 made Unity 6 the hypothesis to test, not the decision. The brief named its main risk as the iteration loop: build times, CI licensing and dependence on the Editor. Determinism and performance were lower risks. The engine spike (#2–#7) tested Unity against five kill criteria. Their thresholds were proposals for Erik to confirm, and Erik ran the device session against them.

As the brief points out, this is largely a language decision: **a C# simulation hosted in Unity** versus a TypeScript simulation in the browser. The simulation is engine-free (INV-2), so the host stays replaceable at a contained cost. The simulation's language does not.

### Evidence

| # | Unity fails if | Result | Source |
| --- | --- | --- | --- |
| K1 | Push to playable link over 20 min median, or any manual Editor step | **Median 367 s (6.1 min)** over 13 pushes, range 256–514 s. No Editor step: settings, scene, service worker and icons all come from code at build time | #4, `unity/ci/loop-metrics.sh` |
| K2 | CI can't build reliably with our licence | **20 of 20** Unity build jobs succeeded, 26 Oct–1 Nov. Licence activation from repository secrets never failed. Three runs went red in our own smoke test, and five were cancelled by newer pushes; neither is a build failure | #4, Actions history |
| K3 | Web build unreliable on iPhone or iPad, over 10 s to load, or more than a frame slower than TURN LAB | **iPhone 16, Safari:** cold load 1.1–2.1 s, 0 dropped frames in 60 s of play, touch "at least as responsive" as TURN by Erik's assessment. Installed to the home screen, it cold-launches and plays in flight mode | #5 |
| K4 | Hashes diverge and neither own trig nor fixed point fixes it at acceptable cost | **Own trig fixes it:** `Portable` maths is identical on .NET x64, WebGL in Chromium and WebGL in Safari on iOS, at about 2.5 µs a tick on the phone. See ADR-0002 | #3, #5, ADR-0002 |
| K5 | Opus can't make and verify a visible change without the Editor | Pending: Erik's unannounced change ("make the player twice as big and green") was made through code in 1 minute and pushed as PR #21; CI verification to follow | #7, PR #21 |

**Beyond the kill criteria**, the spike's Unity work all went through code and CI without the Editor:

- a code-built host and HUD;
- a responsive layout, checked over 54 viewport cases;
- a service worker and icons;
- in-build frame-pacing and load-time instruments.

Unity's settings live in `ProjectSetup` as code, and the build runs in batch mode. Agents verify through a headless Chromium smoke test on every PR (38 checks), and the .NET harness runs locally: 65 of 65 tests pass in this agent environment.

### Not measured

- **The iPad.** The device session measured the iPhone only; iPad rows stay open on #5.
- **Touch latency was not measured.** It is Erik's side-by-side assessment, not the 240 fps video method the brief specified.
- **Native iOS (#6)** is deferred: Erik is not enrolling in the Apple Developer Program for now. The likely trigger is a TURN App Store launch, whose membership would cover ELEVEN. ADR-0002 records the open floating-point risk for native builds.

## Decision

1. **Engine:** Unity 6 (6000.3 LTS) hosts ELEVEN. The simulation stays an engine-free C# library (INV-2), compiled both as a Unity assembly that forbids engine references and as plain .NET for tests and tools.
2. **Simulation language:** C# (`netstandard2.1`, C# 9) until a later ADR raises it.
3. **Platform for now:** the Unity Web build, installed as a PWA, is ELEVEN's channel on iPhone and iPad. Every merge to `main` updates the stable link (ELEVEN LAB) and every PR gets a preview.
4. **Native iOS:** added when there is an Apple Developer membership. It must then pass its determinism row (ADR-0002) and the latency comparison before it ships.
5. **Engine state is code:** no Editor-made state, and the batch-mode build is the only build. A change that truly needs the Editor's interface says what and why in its PR.

## Alternatives considered

- **TypeScript web stack, as in TURN.**
  - It is the strongest alternative, with the fastest loop, already proven by TURN.
  - It lost because Unity cleared every kill criterion it was meant to fail on. The loop is about 6 minutes, well inside the limit.
  - It would also need its own deterministic transcendental maths.
  - It closes the console path and needs wrappers for native iOS and Steam.
  - It stays the control: if Unity's loop or licensing degrades, K1 and K2 point here.
- **Defold (Lua).**
  - Smaller web builds and command-line builds.
  - It lost on language: Lua for a large simulation, with no JIT on iOS or the web.
  - The fallback if Unity's CI licensing breaks.
- **Godot 4 with C#.** It can't export C# to the web, which breaks the web loop. It stays a possible future host behind the engine-free boundary if its C# web export ships.
- **Unreal.** Too heavy for this game.

## Consequences

**Easier:**
- Slice 1 can start (AGENTS.md: "Do not implement football before ADR-0001 is accepted").
- C# suits agents.
- The engine-free simulation runs headless in CI and in agent environments.
- Unity keeps iOS, Android, desktop, Steam and console routes open behind one build pipeline.

**Harder, or costs:**
- **Web build size:** about 6.5 MiB compressed. That is larger than a TypeScript bundle but loaded in under 2.1 s on the phone, and the service worker keeps it offline-capable.
- **CI time:** each build takes about 4–5 minutes of GameCI time, mostly image pull, licence activation and the Library cache.
- **Licensing:** Unity's licensing has changed abruptly before. The engine-free simulation contains that risk, since a host swap would not touch the football.
- **Editor-free discipline** is a standing cost: every scene, setting and asset pipeline has to be code.

**Cost to reverse:** contained, not small.
- Swapping the host keeps the simulation, its tests, hash logs and replays.
- It means rewriting the presentation, input adapters, tooling and build pipeline.
- Swapping the simulation language would be a rewrite.

**Spike code:**
- **Kept:** the Unity host, pipeline and instruments, which become Slice 1's foundation.
- **Deleted:** `SystemMath` and the host's maths switch, per ADR-0002.
- **The spike scenario stays** as the determinism fixture until Slice 1 replaces it.

## Enforcement

- **Engine-free simulation:**
  - The simulation's asmdef sets `noEngineReferences`.
  - The .NET build compiles the same sources without Unity.
  - The banned-API analyzer (ELEVEN001 and ELEVEN002) runs on every build.
- **CI on every PR and on `main`:**
  - the Unity Web build in batch mode;
  - the smoke test, including the cross-runtime digest gate from ADR-0002;
  - the playable link with its push-to-link time.
- **Measuring the loop:** `unity/ci/loop-metrics.sh` re-measures K1 and K2 at any time. A sustained median over 20 minutes, or a run of licence failures, reopens this decision.

## Approval

Pending: Erik.
