# ADR-0000: Baseline — ELEVEN brief v0.3

- **Status:** Accepted
- **Date:** 2026-10-25
- **Deciders:** Erik (product owner), with SOL and Opus
- **Supersedes / superseded by:** —

## Context

ELEVEN's direction was worked out in three brief revisions (v0.1 → v0.3), including a lead developer review by Opus and decisions by Erik and SOL. Erik accepted [brief v0.3](../brief/eleven-brief-v0.3.md) as the working brief on 25 Oct 2026, with two wording corrections that are included in the copy in this repository.

The project needs a single recorded starting point so that later ADRs can supersede specific parts of it explicitly.

## Decision

Adopt brief v0.3 as the project baseline. In particular, the following are decided.

### Architectural invariants

| ID | Invariant | Enforced by |
| --- | --- | --- |
| INV-1 | One authoritative football simulation. Everything else constructs, sequences, observes, measures or presents it. The only permitted approximation is for matches nobody plays, and it must stay calibrated against this simulation. | Dependency rules in CI; no football logic outside the simulation package |
| INV-2 | Engine-free simulation: no engine, rendering, physics, input-device or platform API. Under Unity: no GameObjects, Transform, Rigidbody or PhysX. | The simulation builds and tests outside any engine; in Unity, an assembly that forbids engine references; a test that fails on Unity assembly references |
| INV-3 | Plain-data state, snapshot/restore first-class. Anything that can affect the next tick lives in authoritative state or immutable configured data. | Snapshot round-trip test with per-tick hash comparison |
| INV-4 | Same-build, cross-platform determinism: same state, config, seeds and commands give the same result on every shipped runtime and headless. Cross-version replay is not a goal. | Per-tick state hashes compared between the CI runtime and every shipped runtime |
| INV-5 | Config is separate from state, hashed and versioned. Simulation behaviour is code version plus config hash. | Snapshots and traces record both |
| INV-6 | Independent, counter-based randomness per consumer. | Test that an extra draw in one stream leaves others unchanged |
| INV-7 | States migrate; input traces are version-specific and may expire. | Schema version in every snapshot; migration tests |
| INV-8 | Set pieces are restart → simulation → termination rule, run by a sequencer. Modes contain no football mechanics. | Restarts and termination rules are functions over state; review rule |
| INV-9 | Perception boundary: AI receives an Observation, never human commands or unrestricted internals. | Observation type; AI code can't reach simulation internals |
| INV-10 | Input adapters produce owned commands with a controller id, recorded before assistance; assistance runs inside the simulation. Nothing assumes one human or one input device. | Commands keyed by controller; replay test with assistance on |
| INV-11 | Headless by default: the full simulation runs without rendering or an engine. | CI plays headless matches on every build |
| INV-12 | Gameplay timing lives in the simulation; animation only picks frames. | State carries action phases; review rule |
| INV-13 | AI predicts with the simulation's own functions, never hard-coded physics. | Shared prediction module; review rule |
| INV-14 | Consumers read a stable match-result contract, not internal events. | Contract type; dependency rule |
| INV-15 | No network on the single-player path. | Single-player tests run with networking disabled; offline acceptance test per platform |

### Product and platform principles

Touch-first, not mobile-bound; on-screen touch only (no sensors); input-independent simulation; design for foreseeable extensions (prefer the option that keeps a foreseeable path open at low cost, never pay large costs for hypothetical features); multiplayer preserved but not built; offline-first; accessible by design with a realistic gameplay scope. Full wording in the brief.

### Decision rights

- Erik owns product decisions and player experience.
- Opus owns implementation and routine architecture within the invariants.
- Invariants are superseded only by an ADR setting out the evidence and migration consequences, approved by Erik.
- SOL shapes product, design, acceptance criteria and evaluation.
- Codex independently reviews implementation against the issue, invariants and tests; it is a reviewer, not a competing architect.

### Other decisions

- ELEVEN has its own repository; no production environment yet.
- Erik never routinely operates the engine editor. Engine state is reproducible from code and configuration; GitHub is the source of truth.
- Determinism target: same build, every shipped runtime, headless. The maths model is chosen by ADR-0002.
- No networking in v1.
- Top-down camera, no minimap (current direction). Starting match length: two 3-minute halves (a tuning value).
- A football metrics layer is built early.
- Agents build knobs; Erik turns them.
- Documentation: short AGENTS.md, thin CLAUDE.md, ADRs; architecture docs once there is architecture to describe.
- Collaboration: four persistent roles, Codex review on every PR, and lenses applied by an agent other than the author. Day-one lenses: accessibility and adversarial architecture review.
- Enkel Design: shared grammar, not shared code; no dependency on TURN.

### Explicitly not decided

- **Engine and platform.** Unity 6 (C#, URP, Input System) is the primary hypothesis, tested by the engine spike and decided in ADR-0001. Slice 1 football implementation waits for ADR-0001.
- Everything listed under *Deferred decisions* in the brief, each with the evidence that reopens it.

## Alternatives considered

The alternatives for the engine are compared in the brief's Engine section and will be decided in ADR-0001.

## Consequences

- Later ADRs supersede specific parts of this baseline by reference (for example "supersedes INV-4").
- The invariant IDs above are stable and used in issues, PRs and reviews.
- The brief remains the narrative document; this ADR is the decision record.

## Enforcement

As listed per invariant. At the time of writing, only INV-2's reference test exists; the rest are introduced by the issues that implement the relevant parts.

## Approval

Erik accepted brief v0.3 as the working brief on 25 Oct 2026.
