# Saves

A career as bytes, in five slots ([#170](https://github.com/enkeldesign/eleven/issues/170)). #80 asks for transactional local storage with versioned migration, a checksum and a fallback to the previous save, and no account, server or internet. This page is the format and the slots' logic (`Eleven.Career`, `Saves.cs`). M6.07b's host gives them the page's local storage, autosaves after every fixture and offers a manual save.

## The format

`SaveCodec.Encode(save)`, canonical and little-endian:

| Field | Bytes |
| --- | --- |
| Magic `ELSV` | 4 |
| Format version (1) | 4 |
| The career's seed | 8 |
| The human's club | 4 |
| His role (0 player-manager, 1 manager-only) | 1 |
| When it was saved, by the host's clock | 8 |
| The world's bytes, length first (`WorldCodec`, #157) | 4 + n |
| The season (`SeasonCodec`) | … |
| An FNV-1a 64 checksum over everything before it | 8 |

**The season** holds:
- its number and seed;
- each division's clubs;
- the cup's ranking;
- every fixture;
- every result, in fixture-id order;
- the next matchday;
- the clubs still in the cup;
- the cup's winner.

**Results** go through the result contract's own codec, `MatchResultCodec`, in the simulation's encoding (INV-14). That covers every field, the metrics included, with the result's schema version; a result of another schema is refused. ADR 0005's allowlist names the codec as part of the contract.

**The saved time** is shown in the slot list, never played on: the career is the same whatever the clock says.

A career ten matchdays in is about 80 KB, and the world is most of it.

## Refusals

`SaveCodec.Decode` gives the save, or a `FormatException` that says why:
- **too short;**
- **a checksum that doesn't match**, which catches any flipped or cut byte;
- **the wrong magic;**
- **a format this build doesn't read;**
- **a count out of range, or bytes after the end.**

**A later format** is refused, not misread. A test writes a format-2 save with a good checksum and checks the refusal.

## Migration

`SaveCodec.Migrations` maps each older format to a step that turns its bytes into the next format's. Decoding an older save applies the steps in turn. Format 1 is the first, so the table is empty. The world (`WorldCodec`) and the result contract carry their own versions too.

## Slots

`SaveSlots` keeps five slots over the host's key-value store (`ISaveStore`). Each slot has a current save and a previous one, base-64 in the store.

- **Save:**
  1. The new save is written to the slot's staging key.
  2. It is read back and decoded.
  3. Only if it verifies does the current save become previous and the new one current.

  If the store refuses or damages any write, `Save` returns false, and a load still finds the last good save.
- **Load** takes the current save if it verifies, else the previous one, and says which and why.
- **Summary** shows a slot in the list: empty, or the club, season, matchday and saved time, and whether the previous save was read.
- **Delete** empties a slot.

## A career to and from its save

`CareerSession.ToSave(savedAt)` and `CareerSession.FromSave(save, config)` (harness) convert the session (#167). The loaded career's matches use the config given: the host's.

## Tests

- **`SaveTests`** (career):
  - a career ten matchdays in, round-tripped byte for byte, with its season going on the same;
  - every refusal, the later format included;
  - five slots kept apart, with fallback to the previous save when the current one is damaged;
  - writes the store refuses or damages, leaving the slot as it was;
  - the season's codec.
- **`CareerSaveTests`** (simulation):
  - a played knockout's result reading back text for text, metrics and goals included;
  - a career saved after nine matchdays and loaded, playing eight more exactly as the one that never stopped.
