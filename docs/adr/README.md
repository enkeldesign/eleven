# Architecture decision records

ADRs record significant decisions and why they were made. They describe current decisions, not immutable truth: an ADR can be superseded when the evidence changes.

## Rules

- **Numbering:** `NNNN-short-title.md`, numbered in order. Numbers are never reused.
- **Status:** `Proposed`, `Accepted`, `Superseded by ADR-NNNN` or `Rejected`.
- **Who decides:** Opus may accept routine architecture ADRs. ADRs that add or supersede an architectural invariant, or that change the product or player experience, need Erik's approval. The approval is recorded in the ADR.
- **Superseding an invariant** requires the evidence and the migration consequences, and the adversarial architecture lens (see `docs/lenses/`) applied by an agent other than the author.
- **Precedence:** when an accepted ADR and the brief disagree, the newer accepted ADR wins, and the brief is updated.
- **Editing:** accepted ADRs are not rewritten. Corrections of fact or typos are fine; changes of decision get a new ADR.

Use [template.md](template.md) for new ADRs.

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0000](0000-baseline-brief-v0.3.md) | Baseline: ELEVEN brief v0.3 | Accepted |
