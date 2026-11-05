# Phase 8 — Docs (design step 9)

Index: `index.md`. Spec: modules diff → "Where the docs and code name the version" (35 files). Group **I**, last. All **Sonnet**. State the current decision; no change history (`docs/handoff.md` → How to work here).

### Task 8.1: Repo and module AGENTS.md

`AGENTS.md` (line 3: "One Page Rules (Grimdark Future v3.5.1 and v3.6.0, chosen per game)"), `server/game/AGENTS.md` (spec list gains the three 2026-10-29 specs; the Layout `Content/` line; `GfEdition` in "How it works": "Grimdark Future reads what differs between editions through `GfEdition.Of(state)`; a step whose logic changes has a 3.6 copy beside the 3.5.1 one, chosen in the sequence catalog"), `sim/AGENTS.md`, `tests/AGENTS.md` (the 3.6 fixtures), `client/AGENTS.md`, `viewer/AGENTS.md`, `web/AGENTS.md`, `server/management/AGENTS.md` (`army.edition`, `Served`). Verify each recipe added in phases 1–7 is present once. Commit.

### Task 8.2: Design docs

`docs/design/README.md` (vocabulary: edition, rule set), `docs/design/game-server/design.md` (editions as ids, `GfEdition`, lobby set change), `docs/design/interpreter/{decisions,design,effect-grammar,opr-sequences,properties}.md` (decision 4's two readings, `dangerous-terrain-36`, `foreign_to_hit_penalties`, per-edition content), `docs/design/geometry/design.md` (Steep, Side Battle per edition, eight declarations), `docs/design/management-api/design.md` (`PropertiesSummary.Default`, `GameSummary.RuleSet`, `army.edition`), `docs/design/scheduler/design.md` (Cumbersome note, Artillery per edition), `docs/design/simulation/design.md`, `docs/design/viewer/design.md`, `docs/design/client/rulings-and-references.md`, `docs/design/contracts/design.md`, `docs/data-model.md`, `docs/schema.md` (`army.edition`, `steep` columns). Every bare `p.N` already there means 3.5.1 and stays; a 3.6.0 fact is cited `3.6.0 p.N`. `grep -rn "v3.5.1" docs/design` → each hit either names both editions or is a 3.5.1-only statement. Commit.

### Task 8.3: Code comments and tests naming 3.5.1

`GfAttack.cs:183`, `GfCasting.cs`, `GfHooks.cs`, `GfMelee.cs:293-297`, `GfAttackTests.cs:95`, `GfDealWoundsTests.cs`, `GfShootingTests.cs:83`, `LivePropertiesTests.cs` (4), `RollEffectsTests.cs:29`: each comment stays true as a 3.5.1 reading and adds the 3.6.0 one where the phase-5 task changed it. Commit.

### Task 8.4: Handoff and shipped

`docs/handoff.md`: the first paragraph names both editions; "The rulebooks" names both PDFs and the skill's two folders; the suite and checks numbers from phase 6/7; `docs/shipped.md` gains the batch with the owner's 2026-10-29 ruling quoted from the design spec's first paragraph. Commit.
