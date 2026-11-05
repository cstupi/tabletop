# Grimdark Future 3.5.1 → 3.6.0: optional modules, army books, naming

The core and special-rule changes are `2026-10-29-gf-3-6-0-rules-diff.md`; page map and marks are explained there.

## Optional modules the engine builds

### M1. Side Battle deployment — engine (geometry)

- 3.5.1 p.22 diagram: zone A a right triangle in the bottom-left corner, legs *30"* up the short edge and *54"* along the
  long edge; zone B its mirror in the top-right corner.
- 3.6.0 p.21 diagram (6" grid, no labels): legs 36" (6 squares) up the short edge and 48" (8 squares) along the long
  edge; B mirrored. The band between the hypotenuses is still 24".
- Engine: `libs/Tabletop.Geometry/ZoneStyles.cs` `SideBattleLong = 0.75, SideBattleShort = 0.625` (54/72, 30/48) become
  2/3 and 3/4. Every other built style (Frontline, Ground War, Disordered, Spearhead, Opposing Forces) measures the same.
  `ZoneStyles` is shared geometry, so the style needs a per-edition value (design doc). Size **S**.

### M2. Strategic deployment 5 (Tactical Push) — not built

- 3.5.1 p.23: zone A 18" deep with a 6" bump 16" tall, 16" from the long edges' ends (*"18"", "6"", "16"", "16""*).
- 3.6.0 p.22: the bump is 24" tall, 12" from each end. The other five strategic and six asymmetric diagrams are unchanged.

### M3. Terrain generator and built-in terrain — data

- 3.5.1 p.18: *"5 = Ruins or Debris Counts as Cover terrain, as well as Dangerous terrain when using Rush/Charge actions.
  6 = Forest or Plantation Counts as Difficult and Cover terrain, and units may see into and out of them, but not through.
  … 8 = Hill Counts as Cover terrain, and units on top may ignore one unit/terrain for line of sight. … 12 = Steep Hill
  Counts as Cover terrain, as well as Difficult terrain for units moving up, and units on top may ignore one unit/terrain
  for line of sight."*
- 3.6.0 p.30: *"5 = Ruins or Debris Counts as Cover and Unstable terrain. 6 = Forest or Plantation Counts as Difficult,
  Cover, and Dense terrain. … 8 = Hill Counts as Cover and Steep terrain. … 12 = Steep Hill Counts as Cover, Steep, and
  Unstable terrain."*; buildings *"Impassable and Sight Blocking"*.
- Engine: the built-in pieces' `declarations` in `client/content/builtin/catalog.json` (`ruin`, `ruins`, `rubble-field`,
  forests, hills; `libs/Tabletop.Content/PlanetTerrain.cs` picks pieces only). The p.30 wall line *"Wall or Fence: Counts as
  Cover"* is unchanged (cited in `SuiteCatalog.Core.Shooting.cs` as p.18). With two editions a piece's declarations are
  read per edition: `dangerous_on_rush_charge` under 3.5.1, `unstable` under 3.6.0 (design doc). The catalog's `notes`
  quote 3.5.1 (hills: *"Units on top may ignore one unit/terrain for line of sight"*) and need edition text. Size **S**.

### M4. Solid buildings (p.21 → p.33) — no impact

- 3.6.0 capacity reads like Transport: *"up to X friendly models or Heroes with up to Tough(6), and non-Heroes with up to
  Tough(3) which occupy 3 spaces each"*; *"getting into contact with the building to enter, and staying within 6” when
  exiting"*; destruction places survivors *"fully within 6”"*. The engine runs containers with Transport's rules
  (`GfContainers`, decision 9), which already match.

### M5. Unchanged built modules

Hybrid True Line of Sight (p.56 → p.58; "No Tough + Tough(3) heroes" punctuation only), Relic Wargear's Banner of Eternal
Flame (p.36 → p.37), Multiplayer (p.50 → p.52), the core mission (p.6). Only the page numbers move:
`GfOptions.Catalog` ("p.56"), `GfSight`, `GfContainers`, `GfNames` (p.21, p.36), `GfMoves`, `GfRules.MissionLength`
(p.27, p.41, p.59), `client/src/Core/HeaderView.cs` and `viewer/src/lib/play/headerText.ts` (p.26 VP wording, now p.25),
the suite's p.18/p.36/p.56 check strings.

## Army books

### What the 3.6.0 rulebook says about armies

The rulebook names no faction. Its army-facing changes are the special rules above: Assault (48 of 49 books carry it),
Cumbersome (18), Shred and Spell Amplifier promoted to core, Artillery, Counter, Impact, Indirect, Scout, Hero, the
"most models" aggregation (S8), and Caster's casting window (S4), which every spell and caster rule rides.

### What Army Forge serves

- `scripts/ArmyBooks/Program.cs` `fetch`: index `GET /api/army-books?filters=official&gameSystemSlug=grimdark-future`,
  each book `GET /api/army-books/{uid}?gameSystem=2`. No edition parameter.
- Live on 2026-10-29: 49 books, every `versionString` `3.6.0`, all `modifiedAt` 2026-10-29T23:48Z. One new book: Voidspace
  Stalkers (`zpaY1LFqVk-ZJdvW`); none removed.
- 3.5 books are no longer served: `?gameSystem=2&version=3.5.3` and `&versionString=3.5.3` return the 3.6.0 book, and
  `/versions` is the web app's HTML. The only 3.5 source copies are the local dump `.superpowers/armyforge/`
  (git-ignored: 48 books, 43 at 3.5.3, 4 at 3.5.2, one with none) and the generated, tracked
  `server/game/Content/army-books/*.book.json` and `grimdark-future-army-book-rules.part.json`. The dump must be kept; a
  `fetch` now overwrites it with 3.6.0.
- Army import (`ArmyForgeClient`, `GET /api/tts?id=`) returns lists built from the current books, so every army imported
  from now on is a 3.6.0 list. Its rule names (Armor, Bravery Aura, Anti-Cover, Unique Hero, …) are not in the 3.5.1 set;
  `LiveProperties.Gather` skips an absent name, so those rules silently do nothing in today's games. This is live now.

### What changed in the books (3.5.x dump vs 3.6.0, compared by id and name)

| Measure | Result |
| --- | --- |
| Units | 962 of 1,058 unit ids kept across the 48 shared books; 913 of the 962 change cost; rule lists change on ~9 units per book, Quality/Defense/size on ~5; the four Titan Lords Disciples books keep no unit id |
| Upgrade options | 3,859 of 6,965 option `uid`s kept (55%); e.g. Alien Hives 75 of 152 kept, 214 new |
| Army special rules | 361 distinct names in 3.5, 399 in 3.6.0; 267 in both, of which 113 keep their text; 132 new, 94 gone |
| Spells | 5–8 of each book's 6 spells changed (text, value or name) |

Renames with the same or near-same effect (3.5 → 3.6.0): Ignores Regeneration → Anti-Regen, Ignores Cover → Anti-Cover,
Courage Aura → Bravery Aura, Strafing → Crossing Strike, Piercing Assault → Piercing Charge, Counter-Attack → Counter in
Melee (now grants Counter, so S5's optional strike-first applies), Unique → Unique Hero (*"Counts as having Hero"*).
Changed effect under a new name: Resistance → Spell Resistance (*"most models"*), Versatile Attack (*"over 9” away"*)
→ Versatile Expert (no range band), the *bound Boost rules → Enhanced *bound. Gone (22 books lose Shielded, *"+1 to
defense rolls against hits that are not from spells"*; 37 gain Armor, *"Counts as having Defense X+"*, not a rename):
Shielded, Re-Position Artillery, Mobile Artillery, Shatter, Crack, Disintegrate, Tear. New weapon rules: Smash, Pierce,
Decimate, Scourge, Purge, Imprecise. Book copies of core rules carry 3.6.0's text (Regeneration, Stealth "most"; Unstoppable; Caster; Scout;
Indirect).

Effect on the code: `grimdark-future-army-rules.part.json` (hand-authored, 128 KB) is written against the 3.5 names and
texts; for 3.6.0 it needs a second hand part covering the 132 new names and the 154 changed texts, with the generated
part and books regenerated from a 3.6.0 dump. `server/management/Content/Armies/army-book-units.json` (generated) and
`ArmyBookAdditions`/`ArmyBookReserves` key on 3.5 unit and rule names. Size **L** (the largest item in the move).

## Optional modules not built

One line each; none is built, none is raised.

- Rule packs (p.16): each pack lists the same modules in a new order.
- Fast-Play (p.17, new; replaces Massive Battles p.49): leader-based movement, melee, shooting; simplified terrain.
- Extra actions (p.18): the Assault action is removed; Hunker Down and Defensive Stance give +1 Defense instead of -2 to
  enemy hits; Stealth Move only against shooting from over 12".
- Action boosting (p.19), hero & monster feats (p.20): boost and feat values retuned.
- Advanced missions (pp.24–26): objective setup moves to deployment-zone edges; progressive scoring is X VP per marker per
  round (X = round number) in place of 1 VP plus a final bonus; Ambush trap 2D3.
- Side-missions (pp.27–28): scoring moved to round-numbered VPs; Tough(12+) thresholds become Tough(9+); Slaughter three
  units, not four.
- Army terrain (p.32), living spells (p.35), battlefield conditions and objective effects (pp.38–39), random events
  (pp.40–41), command points (pp.42–43), twists & gambits (pp.44–45), unit psychology (p.47), boarding actions (p.50):
  damage numbers move from D6/hits-with-AP to D3/2D3 wounds, "immediately" and "automatic" dropped.
- Advanced casting (p.34): Psychic Pool → Magic Pools, new 2D6 Casting and Heavy Backlash; miscast table replaced.
- Fog of War (p.46) adds Escalating Battles; Brutal Damage (p.48) adds Death Zone and Heavy Damage, drops System Damage.
- Political Intrigue (p.49), Meat Grinder (p.51), Horde Mode (pp.61–62): new modules.
- Tournament (pp.55–58), solo & co-op (pp.59–60), campaign (pp.63–68): rewritten AI decision trees, campaign skills,
  events and roster sheet.
- Mission cards (pp.69–74): card images identical; rule pages wording only.

## Wording, layout and typos only

- Pages 3–5: version, credits, copyright 2026, contents; "Whenever" → "When"; "Quality tests" → "Quality Tests".
- Throughout: "take/taking an action" → "use/using"; "battlefield" → "table"; "morale test" → "Morale Test"; terrain
  type names capitalised; "at least 9”" → "over 9”" in terrain placement (p.29).
- p.7: "GAME STRUCTURE" heading moved; first round wording ("gets the first turn").
- p.10: "Shaken units must stay idle, but may strike back counting as Fatigued" (capital only).
- p.11: terrain placement moved before terrain rules; the Forest example adds *"count as Dangerous Terrain the first time a
  unit enters it"* as an illustration.
- p.16: "Relic Wargeat" (typo in 3.6.0).
- pp.21–23: deployment diagrams redrawn on a 6" grid without inch labels (M1, M2 are the only geometry changes).

## Where the docs and code name the version

The two editions coexist, so these are places to make edition-specific, not a blind bump. 35 tracked files outside the
skill's pages (plus `pages/p01–p71.md`, each with a "v3.5.1" footer):

- Rules data: `server/game/Content/grimdark-future-core.properties.json` (`"name": "Grimdark Future v3.5.1"`), pinned by
  `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs`.
- Code comments stating 3.5.1 readings: `GfAttack.cs` (Regeneration `all`), `GfCasting.cs`, `GfHooks.cs`, `GfMelee.cs`
  (Shaken strike-back); tests `GfAttackTests.cs`, `GfDealWoundsTests.cs`, `GfShootingTests.cs`, `LivePropertiesTests.cs`
  (4), `RollEffectsTests.cs`.
- Skill: `.claude/skills/licensed/skills/grimdark-future-rules/SKILL.md` (title, source PDF), `pages/`.
- Repo docs: `AGENTS.md`, `server/game/AGENTS.md` (spec list), `docs/handoff.md` (skill and PDF lines 431–433),
  `docs/design/README.md`, `docs/design/client/rulings-and-references.md`, `docs/design/contracts/design.md` (2),
  `docs/design/game-server/design.md`, `docs/design/geometry/design.md` (3: elevation, Side Battle diagram, p.6),
  `docs/design/interpreter/{decisions,design,effect-grammar,opr-sequences,properties}.md`,
  `docs/design/management-api/design.md`, `docs/design/scheduler/design.md` (2), `docs/design/simulation/design.md`,
  `docs/design/viewer/design.md`.
- Specs (history of 3.5.1 decisions; they stay as written): `2026-08-29-effect-grammar`, `2026-09-03-action-sequences`,
  `2026-09-04-scheduler`, `2026-09-04-setup-phase`, `2026-09-06-ruleset-3-5-1`, `2026-09-09-open-questions`,
  `2026-10-02-spell-library`.

No client, viewer or web UI string names a version; the client's create dialog shows the property set's `name`, which
will.

## Owner rulings these touch

- "If a rule FIX is released it shouldnt publish a new version. everything should move to it" (`docs/shipped.md`, v0.1.22;
  memory "rule fixes move every game"): holds within an edition. The edition choice is the exception the owner made on
  2026-10-29.
- Activation notes (`docs/shipped.md` F, `GfRules.ActivationChoices`: "Immobile/Artillery → Hold"): under 3.6.0 Artillery
  has no Hold-only note (S2); Cumbersome gets one.
- Charge contact grace (`GfNames.ChargeContactGrace`), "your own units DO screen", optional rules, team activation: no
  3.6.0 text contradicts them.
- Decision 4 (aggregation `all`) and the Counter/Impact order are engine readings of 3.5.1 text, not owner rulings; S5,
  S8, S11 replace them for 3.6.0.
