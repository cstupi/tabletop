---
name: grimdark-future-rules
description: Use when deciding whether a Grimdark Future (One Page Rules) game state, move, shot, charge, morale result, deployment or terrain setup is legal or correctly resolved; when checking an engine sequence, property or test against the rulebook; or when a doc or answer needs a rulebook page cited.
---

# Grimdark Future rules (v3.5.1 and v3.6.0)

The full rulebook text of both editions, one file per printed page, zero-padded: `v3.5.1/pages/p01.md` … `v3.5.1/pages/p71.md` and `v3.6.0/pages/p01.md` … `v3.6.0/pages/p74.md` (PDF page = printed page, so `v3.5.1/pages/p15.md` is "rulebook p.15", the form the docs cite). Image-only content is transcribed into the same files. v3.5.1: deployment diagrams pp.22–24, mission cards pp.68–71; source `GF - Advanced_Rules_v3_5_1.pdf`. v3.6.0 (74 pages): deployment diagrams pp.21–23, mission cards pp.71–74 (their rules pp.69–70); source `GF_-_Advanced_Rulebook_v3_6_0.pdf`.

## Answering a rules question

0. Which edition: the game's `game_rules` — `grimdark-future` is v3.5.1 (`v3.5.1/pages/`), `grimdark-future-3.6` is v3.6.0 (`v3.6.0/pages/`). A bare `p.N` in code or docs means v3.5.1; a v3.6.0 citation is written `3.6.0 p.N`. When the question names no game, answer for both when they differ.
1. Locate: the edition's index below, or `grep -rn -i '<term>' .claude/skills/licensed/skills/grimdark-future-rules/<edition>/pages/` (`v3.5.1` or `v3.6.0`). Grep for the rule's own words too ("within 6\"", "Shaken", "roll-off"), not only its name.
2. Read the whole page, and the page either side when a section continues. Examples and notes on a page change the rule they follow.
3. Apply precedence (p.13): a special rule beats the standard rule; multiple instances of the same rule don't stack unless it has (X) or says so; simultaneous rules affecting one player are ordered by that player, affecting several by roll-off.
4. Check scope. v3.5.1: pages 4–15 are the core game and always apply; pages 16–67 are optional modules that apply only if the game uses them. v3.6.0: core 4–15, modules 16–68, cards 69–74. A verdict that rests on one of them names the module.
5. Answer with a verdict (legal / illegal / correct / wrong, or the resolved number), the page, and the quoted sentence(s) it rests on. When the book is silent or ambiguous, say so and quote the nearest text. p.4's "most important rule" (players decide; roll-off on disagreement) is the book's answer for a real gap. It is not a verdict.

A unit's own special rules that are not in the reference on pp.13–15 come from its army book (the Army Forge snapshot), not from this book. In this repo, authored rule text is in `server/game/Content/*.properties.json` (`ruleText`), and the engine's sequences are in `server/game/Rules/GrimdarkFuture/` and `docs/design/interpreter/opr-sequences.md`. When checking the engine, compare against the page, not against those.

## v3.5.1 page index

| Pages | Section |
|---|---|
| 4 | Most important rule, scale, models & units, unit stats, dice, re-rolls, roll-offs, Quality tests |
| 5 | Modifiers, weapons, measuring distance & movement, line of sight |
| 6 | Battlefield, placing objectives, the mission (seizing and contesting markers), army building, force org, combined units, deployment |
| 7 | Rounds/turns/activations, Hold / Advance / Rush / Charge, unit coherency |
| 8 | Shooting: targets, who can shoot, weapon groups, the shooting sequence |
| 9 | Melee: targets, charge moves, who can strike, melee sequence, return strikes, fatigue |
| 10 | Melee resolution, consolidation, morale tests, Shaken (what it forbids), Routed |
| 11 | Terrain types: open, impassable, blocking, cover, difficult, dangerous, elevated |
| 12 | Terrain setup guidelines and placement |
| 13–15 | Special rules: priority & stacking, the special rules reference |
| 16 | Pre-made rule packs (which optional modules each pack turns on) |
| 17–21 | Advanced terrain: placement styles, generators (18–19), army/battle/relic terrain (20), solid buildings (21) |
| 22–24 | Deployment styles: standard (22), advanced (23), asymmetric (24) |
| 25–29 | Advanced missions: selection, objectives, face-off, progressive, attack & defend, side-missions, categories |
| 30–32 | Advanced actions: extra actions (30), action boosting (31), hero & monster feats (32) |
| 33–36 | Advanced casting: currents of power, living spells, spell types, relic wargear |
| 37–38 | Battlefield conditions; terrain & objective effects |
| 39–40 | Random events |
| 41 | Fog of War |
| 42 | Unit Psychology |
| 43 | Brutal Damage |
| 44–45 | Command Points, stratagems |
| 46–47 | Twists & Gambits |
| 48 | Boarding Actions |
| 49 | Massive Battles |
| 50 | Multiplayer: free-for-all, king of the hill, team games |
| 51 | Kitchen table games |
| 52 | Small scales & multi-basing |
| 53–56 | Tournament guidelines; LOS variants (top-down, hybrid true, volumetric) on 56 |
| 57–58 | Solo & co-op (AI decision trees) |
| 59–65 | Campaign rules |
| 66–71 | Mission cards: rules (66–67), the cards (68–71) |

### v3.5.1 special rules (pp.13–15)

- p.13: Aircraft, Ambush, AP(X), Artillery, Bane, Blast(X), Caster(X), Counter, Deadly(X), Fast, Fear(X), Fearless, Flying
- p.14: Furious, Hero, Immobile, Impact(X), Indirect, Limited, Regeneration, Relentless, Reliable, Rending, Scout, Slow, Stealth, Strider, Surge, Takedown, Thrust
- p.15: Tough(X), Transport(X), Unstoppable

## v3.6.0 page index

| Pages | Section |
|---|---|
| 4–12 | Core game: rules, units, battlefield and missions, actions, shooting, melee and morale (4–10), terrain placement (11), terrain types (12) |
| 13–15 | Special rules: priority & stacking, the special rules reference |
| 16 | Pre-made rule packs |
| 17 | Fast-Play |
| 18–20 | Advanced actions: extra actions, action boosting, hero & monster feats |
| 21–23 | Deployment styles (diagrams) |
| 24–28 | Advanced missions: extra missions (24–26), side missions (27–28) |
| 29–33 | Advanced terrain: placement (29), generators (30–31), army/battle/relic terrain (32), solid buildings (33) |
| 34–37 | Advanced casting: currents of power (34), living spells (35), relic wargear (36–37) |
| 38–41 | Battlefield effects: special conditions (38), terrain & objective effects (39), random events (40–41) |
| 42–43 | Command Points, stratagems |
| 44–45 | Twists & Gambits |
| 46 | Fog of War |
| 47 | Unit Psychology |
| 48 | Brutal Damage |
| 49 | Political Intrigue |
| 50 | Boarding Actions |
| 51 | Meat Grinder Battles |
| 52 | Multiplayer: free-for-all, king of the hill, team games |
| 53 | Kitchen table games |
| 54 | Small scales & multi-basing |
| 55–58 | Tournament guidelines (55–57); line of sight rules and variants: top-down, hybrid true, volumetric (58) |
| 59–60 | Solo & co-op (AI decision trees) |
| 61–62 | Horde Mode |
| 63–68 | Campaign rules |
| 69–74 | Mission cards: rules (69–70), the cards (71–74) |

### v3.6.0 special rules (pp.13–15)

- p.13: Aircraft, Ambush, AP(X), Artillery, Assault, Bane, Blast(X), Caster(X), Counter
- p.14: Cumbersome, Deadly(X), Fast, Fear(X), Fearless, Flying, Furious, Hero, Immobile, Impact(X), Indirect, Limited, Regeneration, Relentless, Reliable, Rending
- p.15: Scout, Shred, Slow, Spell Amplifier, Stealth, Strider, Surge, Takedown, Thrust, Tough(X), Transport(X), Unstoppable

## Mistakes to avoid

- Answering from memory of other OPR editions or other games (Age of Fantasy, 40k). Only these pages count.
- Reading the rule without its example. The examples settle edge cases (Blast vs model count p.13, Furious/Relentless p.14, Transport spaces p.15).
- Treating an optional module (Advanced Actions, Command Points, Unit Psychology…) as a core rule, or treating a core rule as optional.
- Citing a 3.6.0 page as a bare `p.N`, or reading a 3.6.0 page for a 3.5.1 game.
- Paraphrasing. Quote the sentence. Paraphrase is how invented mechanics enter the code.
- Measuring off a diagram on pp.22–24 at a size other than the drawn one without saying so. The labels are inches on the drawn table.
