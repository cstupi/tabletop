# Phase 4: docs

Both Sonnet, group K, after 3.4, in parallel. State the current decision; no change history, no plan task ids in prose (memory: no change history in docs; name artifacts, not process). Grep for every superseded fact before closing.

---

### Task 4.1: design docs and schema — **Sonnet**

Files: `docs/design/interpreter/effect-grammar.md` (the kinds table: twenty kinds, `count_wounds` and `remove_spell_tokens` rows with their pass and reader; the predicates: thirteen, `weapon_range` and `remaining_size`; `RollEffects.Plan`'s fourth suppression `positive_defense`; the grants store: once grants released at the attack's end, `one_marker` beside `marker` and `spent_marker`, `targeting.oncePerRound` and `countFromParameter`, `optional` on `add_hit`; the name-checks table gains Quick Throw, Brace, Parry; the Worked-against-the-book table gains Pierce and Scourge rows), `docs/design/interpreter/opr-sequences.md` (`attack-sequence`: `defender-choice` and `attack-end`; `resolve-hits`: `parry`; `spell-hits` and `rule-hits` close with `attack-end`; the `attacked` point), `docs/design/interpreter/properties.md` and `decisions.md` where they list kinds or durations, `docs/schema.md` (027's columns and values), `docs/design/game-server/design.md` and `docs/design/simulation/design.md` where they count games, points or Ask questions (`extra_attacks`, `choose_effect` for the defender), `docs/superpowers/specs/2026-10-29-gf-3-6-0-rules-diff-modules.md` if it lists the text-only names.

- [ ] `grep -rn "eighteen kinds\|eleven predicates\|text-only\|not automated" docs/` shows only current facts.

---

### Task 4.2: AGENTS recipes and the handoff — **Sonnet**

Files: `server/game/AGENTS.md` (one line per new read where phase 1 left none; Layout lists the `GfAttack` partials, `GfParry.cs`; Conventions: a name read by C# is a hand entry with `effects: []`, as Ravage, Brace, Parry, Quick Throw), `sim/AGENTS.md` (the 3.6 game files gain `Army36M.cs`; the new checks; a recipe "a rule that needs a defender's answer is scripted with `.Match("choose_effect", …)`"), `scripts/ArmyBooks/AGENTS.md` (the common-rules recipe from 2.1 checked), `docs/handoff.md` (→ The rules suite numbers from 3.4; → Known rule gaps: the 3.6.0 text-only paragraphs replaced by one sentence that every 3.6.0 entry plays, the rulings of the spec that a player meets (Tags spend automatically, Predator asks, Versatile Defense per attack, Brace "-X total", Parry's pick) kept as standing rulings; → Next: this plan's entry removed; the 3.5.1 latent AP-floor note stays), `docs/shipped.md` untouched until a release.

- [ ] `grep -rn "played: false\|text-only\|not automated\|Brace, Parry: no description" docs/ server/game/AGENTS.md sim/AGENTS.md` shows only current facts (the coverage line's `0 not automated` and the generator's report format are current).
- [ ] `docs/handoff.md` stays under 500 lines.
