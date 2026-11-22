# ADR-0005: The career layer is an engine-free package that reads only the match-result contract

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** Opus, a routine architecture decision under #80. No invariant changes; Erik may supersede it.
- **Supersedes / superseded by:** —

## Context

M6 (#80) is the deep career:
- a fictional world;
- competitions and seasons;
- squads and managers, transfers, finances and saves.

It is large, and it lives for years of game time. The invariants constrain it:
- **INV-1:** it must not contain football mechanics.
- **INV-14:** it must read matches only through the match-result contract.
- **INV-15:** it must never need a network.

M5 built the pieces it consumes:
- the full simulation's `MatchResult`;
- the quick-sim for matches nobody plays (ADR 0004).

Where the career's code lives is costly to change once seasons, saves and the host's screens depend on it (#157).

## Decision

The career is its own package, **`Eleven.Career`** (`src/Eleven.Career`).
- **Engine-free:** netstandard2.1, a Unity package and an asmdef with no engine references, like the simulation (INV-2).
- **Deterministic:** its randomness is counter-based on its own streams in the shared registry (`RandomStream.World` and later ones; INV-6). The simulation's banned-API analyzer applies to it: no `System.Random`, clocks, threads or `System.Math`.
- **Plain data:** its state is classes of plain fields and lists (INV-3), encoded canonically and versioned (`WorldCodec`, then M6.07's saves; INV-7).
- **Narrow dependency:** it references the simulation only for:
  - the match-result contract (`MatchResult`, `ResultGoal`);
  - the randomness (`CounterRng`, `RandomStream`);
  - the canonical encoding;
  - the portable maths;
  - formation names.

  It never touches `Simulator`, `SimState`, `SimConfig` or any football mechanic.
- **Who plays matches:** matches are played by the harness and host (the full simulation) or by the quick-sim, and their results are handed to the career as `MatchResult`. Turning the world's footballers into a match's squads is an adapter outside the career (M6.02, M6.03).

## Alternatives considered

- **The career inside the harness.** The harness already plays leagues and gates. But it references every simulation type, so nothing would stop the career reaching football internals. It would also mix test tooling with shipped game logic.
- **The career inside the simulation.** This breaks INV-1's separation and puts career state into the hashed football domain.
- **The career in the host (Unity).** It couldn't be tested or rolled forward headless (INV-11), and saves would depend on the engine.

## Consequences

- **Easier:**
  - seasons and careers roll forward headless in .NET tests and CI;
  - saves are the career's own bytes;
  - the host shows the career but owns none of it.
- **Harder:** every piece of football information the career needs must come through the result contract or an explicit adapter. That is the point.
- **Reversing it costs** moving files between packages; no format depends on the package boundary.

## Enforcement

- **`WorldTests.The_career_uses_only_the_simulations_contract_randomness_encoding_and_maths`** reads the career assembly's type references into `Eleven.Simulation`. It fails on anything outside the allowed list.
- **The banned-API analyzer** runs on the career's build.
- **CI** runs "Run career tests" on every build.

## Approval

Opus, 2026-11-22, under #80's instruction to decompose and decide routine architecture.
