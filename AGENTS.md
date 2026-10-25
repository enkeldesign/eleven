# AGENTS.md

ELEVEN is a retro-style football game by Enkel Design: touch-first, not mobile-bound, initially targeting iPhone and iPad. This file is the project constitution for every agent and contributor. It holds durable rules and navigation only; details live in `docs/`.

## Status

Project startup. The engine and platform are undecided until **ADR-0001**; Unity 6 is the primary hypothesis being tested by the engine spike. **Do not implement football (Slice 1) before ADR-0001 is accepted.**

## Where things are

| Path | What it holds |
| --- | --- |
| `docs/brief/` | The current project brief: vision, principles, reasoning |
| `docs/adr/` | Decisions. When an accepted ADR and the brief disagree, the newer accepted ADR wins |
| `docs/lenses/` | Review perspectives and when to apply them |
| `src/Eleven.Simulation/` | The engine-free football simulation |
| `tests/` | .NET tests for the simulation |

## Architectural invariants

Full wording and enforcement are in [ADR-0000](docs/adr/0000-baseline-brief-v0.3.md). In short:

1. One authoritative football simulation; everything else constructs, sequences, observes, measures or presents it.
2. The simulation is engine-free: no engine, rendering, physics, input-device or platform API.
3. Plain-data state; snapshot/restore is first-class. Anything that affects the next tick lives in state or immutable config.
4. Same-build determinism across every shipped runtime and headless.
5. Config is separate from state, hashed and versioned.
6. Randomness comes from independent, counter-based streams.
7. States migrate; input traces are version-specific and may expire.
8. Set pieces are restart → simulation → termination rule, run by a sequencer. Modes contain no football mechanics.
9. AI receives an Observation, never human commands or unrestricted internals.
10. Input adapters produce owned commands (controller id), recorded before assistance; assistance runs in the simulation.
11. The full simulation runs headless.
12. Gameplay timing lives in the simulation; animation only picks frames.
13. AI predicts with the simulation's own functions.
14. Consumers read a stable match-result contract, not internal events.
15. No network on the single-player path.

An invariant is superseded only by an ADR that sets out the evidence and migration consequences, approved by Erik.

## Decision rights

- **Erik** owns product decisions and player experience, and approves invariant changes.
- **Opus** (lead developer) owns implementation and routine architecture within the invariants.
- **SOL** shapes product, design, acceptance criteria and evaluation.
- **Codex** reviews implementation against the issue, the invariants and the tests. Codex is a reviewer, not a competing architect.

## Working rules

- **Issues first.** Every change starts from an issue with acceptance criteria, written before implementation. If they are missing or unclear, ask; don't invent product decisions.
- **One issue, one branch, one PR.** The PR links the issue and CI must be green.
- **Review.** Codex reviews every PR and marks each finding *blocking* (breaks an invariant, an acceptance criterion, a test, or a lens's blocking criteria) or *non-blocking*. Only blocking findings send a PR back.
- **Lenses.** The issue names any lenses (see `docs/lenses/`). A review lens is applied by an agent that didn't author the work, once per PR; re-checks cover blocking findings only.
- **Tests.** Rules are tested first, and a new test must first fail on an assertion, not on an error. Prefer properties that always hold over exact-position examples.
- **Baselines.** Updates to golden data, recorded curves or hash baselines are separate commits with a stated reason. Feel-related baselines need Erik's sign-off.
- **Feel.** Agents build knobs; Erik turns them. Never change tuned feel values on your own judgement.
- **The repo is the shared memory.** Decisions live in ADRs and issues. A decision that exists only in a chat hasn't been made.
- **Engine state is code.** No undocumented manual editor state. If a change truly needs an editor's interface, the PR says what and why.

## Simulation code rules

Until ADR-0002 defines the maths model:

- Target `netstandard2.1` and C# 9 so the same source compiles in Unity and in plain .NET.
- No references to UnityEngine or any engine, rendering, physics, input or platform API.
- No `System.Random`, wall-clock time (`DateTime.Now`, `Stopwatch`, `Environment.TickCount`), threads or tasks in simulation code.
- No static mutable state. State is plain data with ids, not object references.
- Transcendental maths (`sin`, `cos`, `atan2`, `exp`, `pow`) goes through the simulation's own maths entry point, never `System.Math` or `MathF` directly.

## Carried over from TURN

Not done yet. Before this section is filled, review `turn/AGENTS.md` in webb for rules added after incidents or repeated mistakes, and copy the ones that apply here.
