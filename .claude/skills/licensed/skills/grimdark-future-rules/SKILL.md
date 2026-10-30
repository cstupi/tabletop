---
name: grimdark-future-rules
description: Use when deciding whether a Grimdark Future (One Page Rules) game state, move, shot, charge, morale result, deployment or terrain setup is legal or correctly resolved; when checking an engine sequence, property or test against the rulebook; or when a doc or answer needs a rulebook page cited.
---

# Grimdark Future rules (v3.5.1 and v3.6.0)

The full rulebook text, one file per printed page, zero-padded: `v3.5.1/pages/p01.md` … `v3.5.1/pages/p71.md` (PDF page = printed page, so `v3.5.1/pages/p15.md` is "rulebook p.15", the form the docs cite). Image-only content (the deployment diagrams on pp.22–24, the mission cards on pp.68–71) is transcribed into the same files. Source: `GF - Advanced_Rules_v3_5_1.pdf`.

## Answering a rules question

0. Which edition: the game's `game_rules` — `grimdark-future` is v3.5.1 (`v3.5.1/pages/`), `grimdark-future-3.6` is v3.6.0 (`v3.6.0/pages/`). A bare `p.N` in code or docs means v3.5.1; a v3.6.0 citation is written `3.6.0 p.N`. When the question names no game, answer for both when they differ.
1. Locate: the index below, or `grep -rn -i '<term>' .claude/skills/licensed/skills/grimdark-future-rules/<edition>/pages/` (`v3.5.1` or `v3.6.0`). Grep for the rule's own words too ("within 6\"", "Shaken", "roll-off"), not only its name.
2. Read the whole page, and the page either side when a section continues. Examples and notes on a page change the rule they follow.
3. Apply precedence (p.13): a special rule beats the standard rule; multiple instances of the same rule don't stack unless it has (X) or says so; simultaneous rules affecting one player are ordered by that player, affecting several by roll-off.
4. Check scope. Pages 4–15 are the core game and always apply. Pages 16–67 are optional modules that apply only if the game uses them. A verdict that rests on one of them names the module.
5. Answer with a verdict (legal / illegal / correct / wrong, or the resolved number), the page, and the quoted sentence(s) it rests on. When the book is silent or ambiguous, say so and quote the nearest text. p.4's "most important rule" (players decide; roll-off on disagreement) is the book's answer for a real gap. It is not a verdict.

A unit's own special rules that are not in the reference on pp.13–15 come from its army book (the Army Forge snapshot), not from this book. In this repo, authored rule text is in `server/game/Content/<edition>/` (`ruleText`), and the engine's sequences are in `server/game/Rules/GrimdarkFuture/` and `docs/design/interpreter/opr-sequences.md`. When checking the engine, compare against the page, not against those.

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

## Mistakes to avoid

- Answering from memory of other OPR editions or other games (Age of Fantasy, 40k). Only these pages count.
- Reading the rule without its example. The examples settle edge cases (Blast vs model count p.13, Furious/Relentless p.14, Transport spaces p.15).
- Treating an optional module (Advanced Actions, Command Points, Unit Psychology…) as a core rule, or treating a core rule as optional.
- Citing a 3.6.0 page as a bare `p.N`, or reading a 3.6.0 page for a 3.5.1 game.
- Paraphrasing. Quote the sentence. Paraphrase is how invented mechanics enter the code.
- Measuring off a diagram on pp.22–24 at a size other than the drawn one without saying so. The labels are inches on the drawn table.
