# Lenses

A lens is a written review perspective applied only when an issue calls for it. Lenses advise; they carry no authority. Their value comes from independence: a review lens is applied by an agent that didn't produce the work, preferably a different model or a fresh context.

## Rules

- **The issue names the lenses.** Default is none beyond Codex review; at most two per issue unless Erik asks for more.
- **One pass per lens per PR.** A re-check covers blocking findings only.
- **Findings go in a PR review comment** headed `Lens: <name>`, each marked *blocking* or *non-blocking*.
- **Blocking is narrow:** only when a finding breaks an invariant, an acceptance criterion, a test, or the lens's written blocking criteria.
- **Owners decide.** Erik decides product, Opus implementation, SOL acceptance criteria. Conflicting findings go to the owner once; no lens reviews another lens.
- **Durable outcomes move:** decisions into ADRs, follow-up work into issues, recurring findings into the lens file or AGENTS.md.
- **Lenses earn their place.** Add one when the same kind of problem slips through twice; retire one that goes unused.

## Current lenses

| Lens | Status | Trigger |
| --- | --- | --- |
| [Accessibility](accessibility.md) | Active | Issues touching UI, text, colour, input, remapping or motion |
| [Adversarial architecture review](adversarial-architecture.md) | Active | ADRs that add or supersede an invariant, and ADR-0001 |
| Exploit hunting | Planned | When opponents exist |
| Art direction and Enkel Design consistency | Planned | When visual work starts |
| Copy and editorial | Planned | When menus, tutorials or career text exist |
