# The fictional world

Issue #157, M6.01 (#80). The career's world: every club, footballer and manager. It lives in `Eleven.Career` ([ADR 0005](../adr/0005-career-layer.md)). Every value here is a technical reference, not approved feel.

The world is **wholly fictional** (#80): names are invented from syllable tables. Every club, footballer and manager has a stable id apart from its display name, ready for M7's editing and world packs.

## The model

- **A club:**
  - id, name and short name (2–4 letters);
  - home kit, shirt and shorts, as design tokens (`KitColour`). The design system's kit tier (`Eleven.Presentation.Kit`) holds the colours, so a kit follows the palette (AGENTS.md);
  - reputation 1–20;
  - division (1 is the top);
  - manager and squad, as ids.
- **A footballer:**
  - id, first and last name, age, club;
  - positions, best first: goalkeeper, centre back, full back, defensive, central, wide or attacking midfield, striker;
  - his stronger foot (right, left or both).
  - **Attributes:** #80's 26 football attributes, each 1–20.
  - **Separate properties:** potential (1–20), fitness (0–100), morale (1–20) and days injured.
  - **Who's out** (#176): his bans to serve and this season's cautions, in the league and the cup ([squads.md](squads.md)).
  - **Development** (#194): his progress toward his next point, starts this season, points this season and last, and the season he joined from his club's youth ([development.md](development.md)).
- **A club's picks** (#179): the human's XI and bench, empty for every club the AI picks for.
- **The world's next footballer id** (#194), for a youth, and those who retired at the last season's turn.
- **A manager:**
  - id, name, age, club;
  - #80's seven competences, each 1–20: tactics, coaching, development, scouting, recruitment, motivation, adaptability;
  - a preferred formation, temperament (1 cautious to 20 bold) and reputation;
  - a philosophy (#180): how far up he presses and how high his line stands, 1–20 each ([managers.md](managers.md));
  - his memory of opponents (#191): his latest meetings with each club, from their results ([managers.md](managers.md#his-memory-of-opponents)).

  Memory and decisions come in M6.09.

**The attributes are data.** `PlayerAttributes.Ids` lists them in their stored order: pace (acceleration, top speed, agility), body (balance, stamina, strength), ball (first touch, dribbling), passing (short, long, crossing, vision), shooting (power, finishing, curve), air (heading, jumping), defending (tackling, marking, positioning), mind (anticipation, decisions, composure) and keeping (reflexes, handling, positioning). A footballer stores one value an entry, so an attribute is added in one place.

## The generator

`WorldGenerator.Generate(seed)` builds the world. Every draw is counter-based on `RandomStream.World` (INV-6), keyed by what is drawn, for whom and which draw, so the same seed gives the same world byte for byte.

- **3 divisions of 12 clubs:** 36 clubs, the cup's size.
- **Each division** has a mean level: 13, 11 and 9 on 1–20. Each club spreads up to ±1.5 around it.
- **A squad of 23** in a fixed template: 3 keepers, 4 centre backs, 4 full backs, 2 defensive, 3 central and 3 wide midfielders, an attacking midfielder and 3 strikers.
- **Each footballer** sits at his club's level, spread by the sum of three draws (about ±3, most near the middle).
  - **His position's profile** shifts each group of attributes: for example, a centre back's defending and air are up 3 and his shooting is down 3. Keepers' keeping is up 4, and outfielders' keeping is down 9.
  - **A little noise** is added per attribute (±2), and every value is held within 1–20.
  - **Potential** sits above his level, more so the younger he is.
  - **A second position** is added, next to his first, four times in ten.
- **Each manager's** competences sit at his club's level, ±3.

**The reference world** (seed `0x157`):

| Division | Outfield level, mean | 10th–90th percentile | Clubs' levels | Keepers' keeping | Reputation |
| --- | --- | --- | --- | --- | --- |
| 1 | 13.5 | 11.8–15.3 | 12.0–14.7 | 17.3 | 12–15 |
| 2 | 11.0 | 9.0–12.9 | 9.3–12.2 | 15.1 | 9–12 |
| 3 | 9.3 | 7.3–11.0 | 7.5–10.9 | 13.0 | 7–11 |

- **The divisions overlap at the edges.** The best clubs below are as good as the worst above, as promotion needs.
- **Speed:** it generates in about 1.8 ms and encodes to about 73 KB.

## The codec

`WorldCodec` encodes a world to canonical, versioned bytes and back.
- **The layout:** magic `ELWD`, format 6, the seed, then the clubs, footballers and managers in order, then each footballer with a ban or caution (#176), then each club's picks (#179), then each manager's philosophy (#180), then each manager's memories (#191), grouped by opponent, then the next footballer id, each footballer with development and those who retired at the last turn (#194). An older world reads with what it lacks empty: nobody banned or cautioned, no picks, no philosophy, no memories, no development, and its next id past its highest.
- **Migration:** the attribute and competence counts are written, so a later format can migrate a world (INV-7).
- **Refusals:** a cut, padded or foreign buffer is a `FormatException`.
- **Saves:** M6.07's saves build on it.

## The tests

`WorldTests` (`tests/Eleven.Career.Tests`, CI "Run career tests"):
- determinism by seed;
- the counts: divisions, squads, keepers, managers;
- every rating within 1–20;
- unique ids and names;
- the divisions ordered by strength, and keepers clearly keepers;
- the codec's round trip;
- ADR 0005's dependency rule.
