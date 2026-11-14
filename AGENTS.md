# AGENTS.md

ELEVEN is a retro-style football game by Enkel Design: touch-first, not mobile-bound, initially targeting iPhone and iPad. This file is the project constitution for every agent and contributor. It holds durable rules and navigation only; details live in `docs/`.

## Status

Slice 1. The engine spike is done and its decisions are accepted:
- **[ADR-0001](docs/adr/0001-engine-and-platform.md):** Unity 6 hosts the engine-free C# simulation, and the PWA Web build is the channel. Native iOS follows when there is an Apple Developer membership.
- **[ADR-0002](docs/adr/0002-simulation-maths-model.md):** the `Portable` maths model.

Football work (Slice 1) can start, issue by issue.

**The one-shot run is under way** (#80): from Slice 1's end to a complete offline v1, without device tests. Its rules are in the next section.

## The one-shot run (#80)

From 13 November 2026 Erik asked for ELEVEN to be finished in one continuous run: "One-shot, no device tests, merge on green." [#80](https://github.com/enkeldesign/eleven/issues/80) is the roadmap (M0–M8) and the register of product defaults. While the run lasts, the rules in this section replace the ones they name. Everything else in this file stands, and no invariant changes.

- **Keep going.** Work through #80's milestones in dependency order. Don't wait for a device test, a play session, an approval or an answer.
- **Opus writes the specs SOL hasn't.** SOL wrote M0's and M1's issues and has handed the rest to Opus (Erik, 13 November). For each later step Opus writes a short issue first, from #80's defaults and the research: what is built, its acceptance criteria as tests, and the starting values. This replaces SOL's part in *Issues first*.
- **One PR per playable step.** A PR may close several issues when they make one behaviour that is tested together. It stays small enough for Codex to review whole. This replaces *One issue, one branch, one PR*. PRs are made one at a time: each merges before the next starts.
- **Less paperwork.** No design comment before a change, and a PR description that says what changed, what was checked and what is left. Decisions that later work depends on still go in the repo: the docs, an ADR or the issue.
- **Decide routine detail.** Where an issue or #80 is silent on an implementation or football detail, take the research answer, expose a knob where feel is open, and record the choice and its evidence on the PR. This replaces "ask" in *Issues first* and "surface it to Erik" in *Don't broaden scope* for everything except the stop conditions below.
- **Product defaults come from #80.** Its register marks each as Erik's decision (E), a research-informed working default (D) or a retained experiment (H). A working default is built behind config, a profile or a switch, and is never reported as approved feel.
- **Merge on green.** Opus squash-merges a PR when all of these hold at its current head:
  - the .NET tests pass;
  - the Unity Web build and the Chromium smoke test pass;
  - Codex's review is complete with no unresolved blocker or should-fix;
  - the lenses the issue names have been applied.

  A red check is fixed and rerun, never merged. This replaces Erik's approval in *Merging* for product defaults recorded in #80.
- **No device gate.** *Device verification* doesn't hold a PR or a milestone during the run. Device-facing work is reported as not device-validated, and Erik's device findings afterwards become issues.
- **Baselines.** Recorded curves, golden data and hash baselines for untuned technical references may move without Erik's sign-off, still in their own commits with a stated reason. A value Erik has tuned is never changed.
- **Stop only for:**
  - a change one of the 15 invariants needs, which takes an ADR Erik approves;
  - a true contradiction with an explicit earlier decision of Erik's;
  - research or evidence that can't be reconciled.

  Known limits and deferred choices are written down, not waited on.
- **At each milestone's end:** run the automated regression, the seed-based AI evaluation, the cross-runtime replay, screenshots at phone and tablet sizes and the offline checks, as far as each exists. Then review current `main` as one product and file each finding as its own issue.
- **The final report** says plainly that v1 was not device-validated and that its feel is not approved.
- **What doesn't change:** rules are tested first, baselines move in their own commits, Codex reviews every PR, every build shows its build id, and a milestone that passes its automated checks is not Erik's approval of its feel.

When the run ends, this section is removed in its own PR and the rules it replaced apply again.

## Where things are

| Path | What it holds |
| --- | --- |
| `docs/brief/` | The current project brief: vision, principles, reasoning |
| `docs/adr/` | Decisions. When an accepted ADR and the brief disagree, the newer accepted ADR wins |
| `docs/lenses/` | Review perspectives and when to apply them |
| `docs/ui/` | The visual system: tokens, roles, scales, component recipes and the specimen (ADR-0003) |
| `src/Eleven.Presentation/` | The engine-free visual system tokens: palette, semantic roles and mappings, domain colours, scales |
| `src/Eleven.Simulation/` | The engine-free football simulation |
| `src/Eleven.Simulation.Harness/`, `src/Eleven.Simulation.Cli/` | Scripted scenarios and the headless harness (hash logs, tick cost) |
| `unity/` | The Unity 6 spike host and its build script; see `unity/README.md` |
| `docs/simulation/` | The simulation's determinism contract: ticks, commands, encoding, hashes, maths |
| `tests/` | .NET tests for the simulation and the visual system tokens |

Current `main` is the source of truth. Issues, comments, mockups and earlier discussions explain intent and history but don't override the repository's current state.

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

## Roles and decision rights

- **Erik** owns product decisions, player experience, design and accessibility interpretation, final acceptance, and approves invariant changes.
- **Opus** is the sole implementation developer and owns implementation and routine architecture within the invariants: code changes, fixes, refactors, implementation branches and PRs.
- **SOL** shapes product, design, acceptance criteria, issue writing and triage, and evaluation. SOL doesn't implement code, create implementation branches or PRs, or merge unless Erik asks.
- **Codex** independently reviews implementation against the issue, the invariants and the tests. Codex reviews rather than rewrites: findings go back to Opus unless Erik asks Codex to make a fix. Codex is a reviewer, not a competing architect.

## Working rules

- **Issues first.** Every change starts from an issue with acceptance criteria, written before implementation. If they're missing or unclear, ask; don't invent product decisions.
- **One issue, one branch, one PR.** Keep each PR small enough to implement, review and validate without exhausting usage in one pass; split the work or ask Erik when it grows disproportionately.
- **Don't broaden scope.** Prefer minimal, local changes and read only the files relevant to the task. Add CI, dependencies, refactors or documentation only when the issue calls for them. If the work exposes a product or design question that the brief, established patterns and football-game research don't settle, surface it to Erik.
- **Settle what's settled.** When an established ELEVEN rule or pattern already answers a question, apply it and report it. When established football-game research gives a strong starting answer (see *Research first*), that answer is the starting point too. Bring Erik only genuine product decisions, not implementation, detail choices or questions the research answers.
- **Name the state.** When a spec phrase touches a mechanic with more than one state or timer, the issue or PR says which one it means before implementation.
- **One problem, one issue.** A distinct problem found mid-work gets its own focused issue rather than reopening a large one.
- **Validate small.** Run the smallest relevant validation. Maximum two attempts for a failing check, then report. Stop once the requested behaviour is implemented and verified.
- **Report concisely:** changed files, exact checks run, unresolved issues.
- **Mockups show intent.** Names, lengths and counts in them may be worst-case placeholders; ask which parts are literal when unsure. Mockups aren't required: without one, make a common-sense implementation from established ELEVEN patterns, the design system and recognised best practice. A mockup never overrides system consistency, responsive behaviour, accessibility or sound engineering.

## Review and merge

- **Codex reviews every PR** and marks each finding:
  - **blocker** — unsafe to merge or release; breaks an invariant, an acceptance criterion, a test, or a lens's blocking criteria.
  - **should-fix** — a genuine defect or material risk that normally returns to Opus before merge.
  - **nit** — optional; never blocks.
- **Lenses.** The issue names any lenses (see `docs/lenses/`). A review lens is applied by an agent that didn't author the work, once per PR; re-checks cover blockers and should-fixes only.
- **Merging.** Opus may merge a Codex-reviewed, green, noncontroversial PR. Erik's approval is needed for unresolved review findings, product or design decisions that materially change the brief, scope expansion, and anything touching an invariant. Squash-merge.
- **Device verification.** Device-facing work isn't finished until Erik (or the reporter) confirms it on a device, not when it merges.
- **Process changes are announced.** When a change alters how others work — release steps, sources of truth, ownership — say so in its PR and in the owning issue.
- **Notes live with their issue.** Use the owning issue and PR for implementation notes and device follow-up, never an unrelated umbrella issue.
- **Review the whole.** After a substantial batch of work, review current `main` as one product, focusing on interactions between recently changed systems, and record findings in an issue or PR.

## Tests and baselines

- Rules are tested first, and a new test must first fail on an assertion, not on an error. Prefer properties that always hold over exact-position examples.
- Updates to golden data, recorded curves or hash baselines are separate commits with a stated reason. Feel-related baselines need Erik's sign-off.
- **Feel:**
  1. Research sets the starting model, defaults and plausible ranges.
  2. Agents build the knobs, presets, measurements and test benches.
  3. Erik plays credible candidates, then chooses or tunes.

  Untuned values are technical references, not approved feel. Never change tuned feel values on your own judgement. "Erik turns the knobs" doesn't mean asking him to invent defaults or answer football-design or physics questions from first principles.

## Standing rules

- **Trade-off order:** 1. architectural invariants and correctness, 2. feel — responsiveness, latency and smooth frames, 3. accessibility of everything around the match, 4. product and UI coherence, 5. regression risk.
- **Research first.** Decades of successful football games, especially SWOS / Sensible Soccer, Kick Off and modern descendants such as Super Soccer Champs, supply hypotheses, defaults, candidate mechanics and plausible ranges. They come before any question to Erik, whose part is genuine unresolved product choices.
- **Feel over realism.** Real football and simulation give direction, constraints and plausible relationships. Feel decides how closely ELEVEN follows reality in each case. Realism alone never justifies a mechanic or a value: prefer football credibility, responsiveness, readability, skill expression and fun over physical fidelity. The simulation gives coherent, reproducible behaviour and starting points, not final feel.
- **Assistance comes from the footballer.**
  - Input expresses intent. The controlled player's attributes shape perception, judgement and execution; there is no universal UI helper.
  - Assisted actions may draw on attributes, context and AI reasoning. Manual actions keep the player's intent rather than secretly redirecting it.
  - Attribute effects are perceptible, and deterministic under the simulation rules.
- **Small controls, deep expression.** Prefer few controls with expressive behaviour. Test candidate control models rather than assuming conventional PASS/SHOOT buttons. Learn from retro football's interaction patterns while keeping ELEVEN's identity.
- **Accessibility is a constraint, not a feature,** for everything around the match: menus, settings, text, colour, remapping, motion and feedback. Sweeps include non-text contrast for control states and focus indicators (WCAG 1.4.11), not only text contrast.
- **Performance and smooth frames come before richer sound or visuals.**
- **Colours come from semantic design tokens, never hard-coded.**
- **Pause means stop.** Anything that runs during a match stops on pause, leave and background, and the simulation never advances while paused.
- **The repo is the shared memory.** Decisions live in ADRs and issues. A decision that exists only in a chat hasn't been made.
- **Engine state is code.** No undocumented manual editor state. If a change truly needs an editor's interface, the PR says what and why.
- **Every build shows its build id.** Once ELEVEN has releases, every release-affecting merge increments the build in that same merge.

## Simulation code rules

Per ADR-0001 and ADR-0002:

- Target `netstandard2.1` and C# 9 so the same source compiles in Unity and in plain .NET.
- No references to UnityEngine or any engine, rendering, physics, input or platform API.
- No `System.Random`, wall-clock time (`DateTime.Now`, `Stopwatch`, `Environment.TickCount`), threads or tasks in simulation code.
- No static mutable state. State is plain data with ids, not object references.
- Transcendental maths (`sin`, `cos`, `atan2`, `exp`, `pow`) goes through the simulation's own maths entry point, never `System.Math` or `MathF` directly.

Rules under *Roles and decision rights*, *Working rules*, *Review and merge* and *Standing rules* are adapted from TURN's AGENTS.md (25 Oct 2026): the ones added after real incidents or repeated mistakes that still apply to ELEVEN. Four standing rules are ELEVEN's own, from Slice 1's start (#32): *Research first*, *Feel over realism*, *Assistance comes from the footballer* and *Small controls, deep expression*. So is the *Feel* rule's research-first wording.
