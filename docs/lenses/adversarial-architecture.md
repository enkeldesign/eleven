# Lens: Adversarial architecture review

**Purpose:** attack a significant architectural decision before it is accepted, so that it rests on evidence rather than on its author's confidence.

**Trigger:** ADRs that add or supersede an architectural invariant, and ADR-0001 (engine and platform).

**Applied by:** Codex or a fresh agent — never the ADR's author. The reviewer critiques; it does not become a competing architect. The decision stays with the owner under the decision rights in AGENTS.md.

## Questions

1. What evidence supports the decision, and is it measured or assumed?
2. Which alternatives were dismissed, and was the strongest one stated fairly?
3. Which invariants does it touch, and does it supersede them explicitly?
4. What does it make expensive later, and what would reversing it cost?
5. What are the migration consequences for existing state, snapshots, tests and tools?
6. Where would it break first: a new platform, a new input device, multiplayer, 10× more simulation load?
7. Does it pay a large cost today for a hypothetical future (brief, principle 4)?

## Blocking criteria

- The ADR contradicts an invariant without explicitly superseding it.
- It supersedes an invariant without evidence or without migration consequences.
- A key claim rests on an assumption that the spike or a test could cheaply measure but didn't.

Everything else is non-blocking.

## Output

A PR review comment headed `Lens: Adversarial architecture`, one finding per line, each marked blocking or non-blocking, ending with the single strongest argument against the decision.
