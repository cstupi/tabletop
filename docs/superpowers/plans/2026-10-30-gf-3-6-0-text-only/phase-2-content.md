# Phase 2: the generator and the hand part

All Sonnet, group I. Sources: the 3.6 dump in `.superpowers/armyforge/3.6/` (texts byte for byte), `common-rules.json` for Brace and Parry, the spec's effect shapes. After every content edit: `dotnet run --project scripts/ArmyBooks -- generate --edition 3.6` (the hand part overrides a generated name; the generated report's text-only counts fall), `generate --edition 3.5` byte-identical, `dotnet test tests/Tabletop.ArmyBooks.Tests tests/Tabletop.Content.Tests tests/Tabletop.GameServer.Tests` (the content tests compose the set: two sources of one name must agree). Never edit a `*.book.json` or `army-book-rules.part.json`.

---

### Task 2.1: common rules in `fetch` and `generate` — **Sonnet**, wave 1

Files: `scripts/ArmyBooks/Program.cs` (`fetch` also GETs `https://army-forge.onepagerules.com/api/rules/common/<gameSystemId>` — 2 for Grimdark Future — into `<dump>/common-rules.json`; the only other network call, refused when the dump is another edition's as the books are), `RuleWriter.cs` (`descriptions` falls back to `common-rules.json` for a name no book describes; the report lists a name under "described by the common rules" and `NoDescription` empties), `Generation.cs` (the note), `tests/Tabletop.ArmyBooks.Tests/RuleWriterTests.cs` (a fixture common-rules file with Brace and Parry; a name described there is written with that text), `tests/Tabletop.ArmyBooks.Tests/ArmyRules36Tests.cs` (`Every_3_6_hand_entry_text_matches_a_3_6_book` accepts a common-rules text), `scripts/ArmyBooks/AGENTS.md` (the recipe: fetch saves the common rules; a name the books carry without text takes it from there), `docs/handoff.md` → The rulebooks paragraph gains the endpoint.

- [ ] Run `./scripts/army-books fetch` once against the live endpoint to save `.superpowers/armyforge/3.6/common-rules.json` (the dump folder is git-ignored; the test fixture is the tracked copy of the two rules).
- [ ] `generate --edition 3.6` reports no description for no name; `generate --edition 3.5` byte-identical; `GeneratedOutputTests` pass.

---

### Task 2.2: the translator — **Sonnet**, after 1.5 merges

Files: `scripts/ArmyBooks/SpellTranslator.cs` (effect `Urj6dU6Tua8B` → `remove_spell_tokens` with `numberDigit`, on the assignment's target; an empty `attackType` on effect `54xO9EKTj-ix` (a weapon-rule grant) writes the grant with no `attack_kind` condition; `IsTextOnly` no longer names either case), `tests/Tabletop.ArmyBooks.Tests/SpellTranslatorTests.cs` (Shifting Form's effects; Triangulation Bots' grant of Indirect with no attack condition), `scripts/ArmyBooks/AGENTS.md` (one line).

- [ ] `generate --edition 3.6`: the "text-only spells" note loses Burn the Heretic, Shifting Form ×3 and Triangulation Bots; `BookWriter.Check` passes (Indirect is a core entry).

---

### Task 2.3: hand entries, the weapon rules — **Sonnet**, after 1.1, 1.2, 1.3, 1.12, 1.13

`server/game/Content/grimdark-future-3.6/army-rules.part.json`: replace the `played: false` stubs with rows (drop `played`); add Brace and Parry (kind `weapon`, `takesParameter: true`, `effects: []`, text from `common-rules.json`).

| Entry | Rows |
| --- | --- |
| Pierce | `suppress_modifiers positive_defense` (opponent); `grant_property AP 1` (self) with `weapon_range gt 50` |
| Smash | `force_reroll defense` (opponent) on `roll_result defense raw eq 6` with `weapon_range gt 50` (Bane's row shape) |
| Destructive | `grant_property AP 1` (self) with `remaining_size opponent gt 50 [Tough]` |
| Decimate | `force_reroll defense` (opponent) on raw 6 with `remaining_size opponent gt 50 [Tough]` |
| Scourge | `count_wounds 2` (opponent) on `roll_result defense raw eq 1` |
| Brace, Parry | none (read by name) |

- [ ] `Tabletop.ArmyBooks.Tests/ArmyRules36Tests` (texts match the dump or the common rules), `Tabletop.Content.Tests`, `Tabletop.GameServer.Tests` `ArmyRules36Tests`/`AppliedKindsTests` (every row kind is read somewhere) pass; `generate --edition 3.6` moves these names to the hand part's count and demotes no spell for them.

---

### Task 2.4: hand entries, the rest — **Sonnet**, after 1.5–1.11

| Entry | Rows |
| --- | --- |
| Mind Control | `targeting {enemy, unit, count 1, range 18, oncePerRound: true}`; `take_test` with child `move 6`, both at `at_point before-attacking` |
| Piercing Tag | `targeting {enemy, unit, count 1, countFromParameter: true, range 24}`; `grant_property AP 1` (opponent), `duration one_marker`, `at_point before-attacking`, `usage_count lt 1 [game]`, `attack_kind melee, shooting` |
| Precision Tag | the same with `modify_roll hit +1` (opponent) |
| Versatile Defense | aggregation `majority`; `set_condition "Versatile Defense: +1 to Defense"` and `"Versatile Defense: -1 to hit"` at `at_point attacked`, each twice (shooting `range gt 9`; `action_taken opponent Charge`, melee, `range gt 6`); `modify_roll defense +1` (self) on `actor_state self eq` the first; `modify_roll hit -1` (opponent) on `actor_state self eq` the second |
| Enhanced Wolfborn | `modify_parameter Wolfborn +1` (self), `holds_property self Wolfborn` |
| Enhanced Rapid Blink | the same over Rapid Blink |
| Spell Drain | `targeting {enemy, model, count 1, range 18}`; `take_test` with child `remove_spell_tokens 2`, at `before-attacking` |
| Quick Throw | kind `model`, aggregation `none`, `effects: []` |
| Ravage Aura | only if the generator's Aura template declines `(+1)`: `grant_property Ravage 1` (self), aggregation `any` |

- [ ] The same test set as 2.3; `generate --edition 3.6` reports the Auras (Enhanced Rapid Blink Aura, Enhanced Wolfborn Aura, Versatile Defense Aura, Ravage Aura) as played wrappers and the 24 spells as translated.

---

### Task 2.5: Predator and Primal, Thrust in Melee Buff, regenerate, zero text-only — **Sonnet**, last of phase 2

Files: the 3.6 hand part (Predator's and Primal's `add_hit attacks` rows gain `optional: true`; Thrust in Melee Buff keeps its one `grant_property Thrust once` row, now whole through 1.7: no content change unless the review finds one), `server/game/Content/grimdark-future-3.6/army-book-rules.part.json` and `army-books/*.book.json` (regenerated), `army-book-units.3.6.json` if the generator rewrites it.

- [ ] `generate --edition 3.6` prints `0 text-only` for rules and no text-only spells; `generate --edition 3.5` byte-identical; `git diff --stat server/game/Content/grimdark-future-3.5.1` empty.
- [ ] `dotnet test tests/Tabletop.ArmyBooks.Tests tests/Tabletop.Content.Tests tests/Tabletop.GameServer.Tests tests/Tabletop.Sim.Tests` pass (the Sim coverage test will fail on uncovered entries until 3.4: note it in the report, do not pin).
