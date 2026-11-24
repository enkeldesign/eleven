# Saves

A career as bytes, in five slots ([#170](https://github.com/enkeldesign/eleven/issues/170)). #80 asks for transactional local storage with versioned migration, a checksum and a fallback to the previous save, and no account, server or internet. This page is the format and the slots' logic (`Eleven.Career`, `Saves.cs`), and the host's use of them ([#171](https://github.com/enkeldesign/eleven/issues/171)).

## The format

`SaveCodec.Encode(save)`, canonical and little-endian:

| Field | Bytes |
| --- | --- |
| Magic `ELSV` | 4 |
| Format version (1) | 4 |
| The payload's length, uncompressed | 4 |
| The payload, compressed (`Lz`) | … |
| An FNV-1a 64 checksum over everything before it | 8 |

**The payload:**

| Field | Bytes |
| --- | --- |
| The career's seed | 8 |
| The human's club | 4 |
| His role (0 player-manager, 1 manager-only) | 1 |
| When it was saved, by the host's clock | 8 |
| The world's bytes, length first (`WorldCodec`, #157) | 4 + n |
| The season (`SeasonCodec`) | … |

**The season** holds:
- its number and seed;
- each division's clubs;
- the cup's ranking;
- every fixture;
- every result, in fixture-id order;
- the next matchday;
- the clubs still in the cup;
- the cup's winner.

**Results** go through the result contract's own codec, `MatchResultCodec`, in the simulation's encoding (INV-14). That covers every field, the metrics included, with the result's schema version. Schema 7, the first it wrote, reads as a result with no footballers' lines (#172), so a career saved before them still loads. A result of any other schema is refused. ADR 0005's allowlist names the codec as part of the contract.

**The saved time** is shown in the slot list, never played on: the career is the same whatever the clock says.

## Size

A save lives in the page's local storage as base64. The smallest browser's limit is iOS Safari's, about 2.5 million characters a page: WebKit stores UTF-16 in 5 MB. A WebKit report from iOS 16 says writing past it can clear the page's whole storage. Each slot holds a current and a previous save, and one more is staged while saving, so five slots hold up to 11 saves.

**Compression:** `Lz` is LZ77 in LZ4's block layout, in pure managed code. It gives the same bytes on every runtime, and the browser needs nothing native.
- **Decompression** checks every length and offset against its bounds, and is told the payload's length.
- **Refused:** a stream that isn't whole, or holds any other length. So is a payload over 64 MB, so a damaged length can't ask for more.

| | Uncompressed | Saved | Base64 |
| --- | --- | --- | --- |
| A new career | 83,514 B | 49,332 B | 65,776 chars |
| A season's end, 431 results | 175,680 B | 62,278 B | 83,040 chars |

- **The world** is 72,725 bytes, and LZ77 shrinks it by only a third, because its attributes are near-random values from 1 to 20.
- **A season's results** repeat their layout, and shrink many times over.
- **Eleven saves of a season's end** come to about 913,000 characters. `CareerSaveTests` holds them under half of iOS's limit, leaving the other half for the bench's presets and captures.
- **Later seasons save more:** managers' memories (#191), development (#194) and money (#198) grow the world. After three seasons a save is about 143,000 characters, and eleven come to about 1.58M, within iOS's 2.5M but past that half. That's for M6's gate (M6.16) to weigh. The board (#200) adds 23 bytes a club, 832 in all, contracts (#201) about 3 bytes a footballer and 30 a move, about 5,000, and transfers (#203) 8 bytes a club and about 40 a transfer of the last window, about 2,000, before compression.

## Refusals

`SaveCodec.Decode` gives the save, or a `FormatException` that says why:
- **too short;**
- **a checksum that doesn't match**, which catches any flipped or cut byte;
- **the wrong magic;**
- **a format this build doesn't read;**
- **a payload that doesn't decompress to its length;**
- **a count out of range, or bytes after the end.**

**A later format** is refused, not misread. A test writes a format-2 save with a good checksum and checks the refusal.

## Migration

`SaveCodec.Migrations` maps each older format to a step that turns its bytes into the next format's. Decoding an older save applies the steps in turn. Format 1 is the first, so the table is empty. The world (`WorldCodec`) and the result contract carry their own versions too. The world is at format 2 since #176, its bans and cautions; a format-1 world in a save reads with nobody banned, so the save's own format stays 1.

## Slots

`SaveSlots` keeps five slots over the host's key-value store (`ISaveStore`). Each slot has a current save and a previous one, base-64 in the store.

- **Save:**
  1. The new save is written to the slot's staging key.
  2. It is read back and decoded.
  3. Only if it verifies does the current save become previous and the new one current.
  4. **A damaged current save is never rotated into previous** (Codex on #181). After a load fell back to the previous save, that previous stays the slot's fallback until a good save replaces it.
  5. **The rotation is read back** before current is overwritten. A store that damages it while reporting success leaves current as it was.

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
- **`CareerSlotTests`** (Codex on #183):
  - with no career in play, a slot offers its save or a new career;
  - the career in play resumes from its own slot as it is, said not saved while the device refuses its saves;
  - one whose first save was refused is offered back, not a new career.

## In the host

#171 puts the slots in the page's local storage, through the host's stored-text bridge (#46). There is no network. An empty value counts as no value, so a delete writes an empty one.

- **Career on Home** opens the five slots. Each shows its club, season and matchday, or Empty, and says when a slot's previous save was read.
  - **Continue** loads a slot's career at its hub.
  - **New career** opens New career for that slot.
  - **The career in play** is its own slot's, whatever that slot's save says (Codex on #183). Its line says "in play", and "in play, not saved" when the device refused its last save. **Continue** returns to its hub as it is, without loading, so a refused save never loses it; Save on the hub tries again. `CareerHub.SlotOffer` and `SlotLine` decide, engine-free.
- **Autosave** into the career's slot happens:
  - on Start;
  - after every fixture played or simulated;
  - after a match left before full time;
  - on the next season.
- **Save** on the hub saves at once.
- **The hub says each outcome:** "Saved to slot 2", or why not. If a slot's latest save was damaged, it says its previous one was loaded.
- **The saved time** is the device clock's.

**Smoke** (`career-phone`):
- Career shows five empty slots;
- New career in the first, then Start saves it;
- after #168's matchdays, the simulated fixture is saved;
- with the page's storage refusing writes, Simulate's save fails and is said;
- Home, Career and slot 1 return to the career at matchday 4 without loading;
- with storage back, Save on the hub keeps it;
- the page reloads, and slot 1 shows the career at matchday 4;
- Continue opens its hub there, the table played as before.
