# Phase 7 — 3.6 army books and the hand-written army rules (design step 8)

Index: `index.md`. Spec: `2026-10-29-rule-set-versions-design.md` → "Army books per edition"; modules diff → Army books. Group **H**: 7.1–7.3 after phase 2; 7.4–7.7 after phase 5b (S3, S6, S14 add core names the generator must see as core). The long pole is 7.5 (154 changed texts, 132 new names); it is batched like the 3.5 hand part was (`docs/shipped.md` batches 1–9).

Facts: the 3.6 dump is `.superpowers/armyforge/3.6/` (Task 1.1): 49 books at `versionString` 3.6.0, Voidspace Stalkers (`zpaY1LFqVk-ZJdvW`) new. `RuleWriter.Build` reports `NoDescription` (names a book uses that neither the core nor the hand part defines) and refuses two descriptions of one name. `Compose` refuses an entry both the core and a part define.

---

### Task 7.1: Generate the 3.6 books and generated part from the 3.6 dump — **Sonnet**

`./scripts/army-books generate --edition 3.6` with the 3.6 core (phase 5b's entries) and the 3.6 hand part (still the 3.5 copy): expect `wrote 49 books`, the generated part, and a `no description:` list (the 132 new names minus any the core now defines). The generator refuses nothing (text-only entries are `played: false`). **Files:** `server/game/Content/grimdark-future-3.6/army-books/*.book.json` (49; the 48 placeholders replaced, one added), `army-book-rules.part.json`. **Test:** `tests/Tabletop.GameServer.Tests/ContentFoldersTests.cs` gains `The_3_6_books_are_3_6_0` (every 3.6 `BookDocument.VersionString` starts with `3.6`). **Verify:** `dotnet test tests/Tabletop.GameServer.Tests --filter "ContentFolders|SpellLibrary"`; `docker compose restart game` seeds; the viewer's create dialog on 3.6 lists nothing new (books are not UI) but `GET /v1/properties/{hash}/rules` of the 3.6 set lists the 3.6 names. Commit `"3.6 army books generated from the 3.6.0 dump"`.

---

### Task 7.2: The generator test pins 3.5 byte-identity — **Sonnet**

`tests/Tabletop.ArmyBooks.Tests/GeneratedOutputTests.cs` (new, `[Trait("Category","E2E")]`-free but needs the dump: skip with a message when `.superpowers/armyforge/3.5/index.json` is absent): runs the `generate` pipeline in process (`RuleWriter.Build` + `BookWriter.Build/Serialize` + `ReserveUnits.Build`) over the 3.5 dump and compares each output with the committed `grimdark-future-3.5.1/` files byte for byte. Commit.

---

### Task 7.3: Units file per edition; `ArmyBookReserves`/`ArmyBookAdditions` by edition; `Served` from the files — **Sonnet**

**Files:** `scripts/ArmyBooks/ReserveUnits.cs` (`FileName(edition)` → `army-book-units.<edition>.json`), `Program.cs` (`--units` default per edition), `git mv server/management/Content/Armies/army-book-units.json army-book-units.3.5.json`, generate `army-book-units.3.6.json`, `server/management/Tabletop.Management.csproj:23` (`Content/Armies/army-book-units.*.json`, LogicalName `%(Filename)%(Extension)`), `ArmyBookReserves.cs:23-27` (`BooksOf(string edition)`; `Add(..., edition)`), `ArmyBookAdditions.cs` (Takedown weapons: the names are the same in 3.6; takes the edition for symmetry), `ArmyEndpoints.ImportAsync:60-73` (passes `ArmyEditions.Served`), `libs/Tabletop.Content/ArmyEditions.cs` (`Served` stays a constant; management's `ArmyBookReserves.Editions` asserts a units file exists for it at startup — `StartupGuard` or a test).
**Test:** `ArmyBookReservesTests.cs` (3.5 and 3.6 files load; a 3.6 book's Spawn unit resolves), `ArmyForgeImportTests.cs`. Commit.

---

### Task 7.4: The 3.6 hand part: renames and unchanged rules — **Opus**

`server/game/Content/grimdark-future-3.6/army-rules.part.json`: keep the 113 rules whose text is unchanged (compare each entry's `ruleText` with the 3.6 book texts: a script in the scratchpad over the dump lists per name `unchanged | changed | gone`); rename the same-effect renames (`Ignores Regeneration`→`Anti-Regen`, `Ignores Cover`→`Anti-Cover`, `Courage Aura`→`Bravery Aura`, `Strafing`→`Crossing Strike`, `Piercing Assault`→`Piercing Charge`, `Counter-Attack`→`Counter in Melee` (grants Counter: `GfNames.CounterAttack`'s reads in `GfMelee.StrikesFirst:29` must also accept the 3.6 name — a `GfEdition.CounterAttackName`? **decision**: `StrikesFirst` reads both names in both editions, they are names no 3.5 book confuses), `Unique`→`Unique Hero` (*"Counts as having Hero"*: a `grant` of Hero)), delete the gone (Shielded, Re-Position Artillery, Mobile Artillery, Shatter, Crack, Disintegrate, Tear) and Shred (S14). Every kept entry's `ruleText` becomes the 3.6 book text. **Test:** `tests/Tabletop.GameServer.Tests/ArmyRules36Tests.cs` (new): `Every_3_6_hand_entry_text_matches_a_3_6_book` (over the shipped 3.6 books' rule texts) and `No_hand_entry_names_a_rule_no_3_6_book_uses`. Regenerate (7.1's command) after; `Compose` must accept. Commit.

---

### Task 7.5: The 3.6 hand part: changed and new rules, batch by batch — **Opus** (several branches)

Author the 154 changed texts and 132 new names in the effect grammar (`docs/design/interpreter/effect-grammar.md`, `docs/superpowers/specs/2026-10-09-army-book-rules-design.md`'s patterns), in batches of ~30 by mechanism (weapon rules: Smash, Pierce, Decimate, Scourge, Purge, Imprecise first; then auras; then picks; then Enhanced *bound spells; Armor stays (`replace_stat` Defense); Spell Resistance (`most models`: `aggregation majority`); Versatile Expert (no range band)). Each batch: the entries, `generate --edition 3.6`, `Compose` passes, the `no description:` list shrinks, and one suite game per new mechanism in `SuiteCatalog.Army36*.cs` (group `army:3.6:<uid>`, Task 7.6). A rule the grammar cannot express is written text-only and listed in `docs/handoff.md` → Known rule gaps with the sentence. **Verify** per batch: `dotnet test tests/Tabletop.GameServer.Tests --filter "ArmyRules36|SpellLibrary|ContentFolders"`, `dotnet run --project sim -- suite --only army:3.6:<uid>`. Commit per batch.

---

### Task 7.6: `army:3.6:<uid>` and `spells:3.6:<uid>` suite games — **Opus** (with 7.5)

`sim/Suite/SpellGames.cs:39,121` (`Books(set)` over the 3.6 set → `spells:3.6:<uid>` games; `SpellTables` per edition: the 3.6 spells differ in 5–8 of 6 per book, so the 3.6 tables are written from the 3.6 books), `SuiteCatalog.Army36.cs` (the book table for 3.6 uids: 24 books as the 3.5 table, Voidspace Stalkers added), the exact checks reused (`SuiteCatalog.Army.Checks.cs`) with 3.6 unit ids. **Verify:** `dotnet run --project sim -- suite` full: 3.6 coverage complete (every 3.6 own entry covered and fired, wrappers by pattern). Commit per batch.

---

### Task 7.7: Phase 7 docs — **Sonnet**

`server/game/AGENTS.md` (content folder contents per edition, the hand parts), `scripts/ArmyBooks/AGENTS.md` (the per-edition workflow end to end: fetch → generate --edition → tests → suite), `docs/handoff.md` (rules suite counts per edition; 3.6 text-only names). Commit.
