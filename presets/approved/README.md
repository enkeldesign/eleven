# Approved feel baselines

A preset in this folder is **approved feel**: Erik promoted it explicitly on its owning issue (#46, #27). Nothing else makes a preset approved. A preset saved on the bench is `saved`, and an agent's is `research-hypothesis`; neither can claim `approved` (`FeelPreset.TryRead` refuses it).

## Promoting a preset

1. **Erik exports the preset** from the test bench and attaches the `.preset` file to the owning issue, saying that it should be promoted.
2. **An agent characterises it:** `eleven-sim characterise <file> <dir>` writes its ball, movement, kick and pass curves and measurements.
3. **In one commit of its own,** with Erik's comment as the reason, the agent:
   - sets `kind: approved`;
   - adds the file here, named after the preset;
   - commits its characterisation beside it.
4. **The build ships it** as a built-in APPROVED FEEL preset next to TECHNICAL REFERENCE.

## Changing an approved baseline

A change to an approved baseline is a separate, reasoned baseline update, as AGENTS.md requires:
- in its own commit;
- with the new characterisation;
- with the reason on the owning issue.

The file's config hash and values change together, or `FeelPreset.TryRead` refuses it.

There are no approved baselines yet.
