# Phase 1 — Armies know their edition (design steps 1–2)

Index: `index.md`. Spec: `2026-10-29-rule-set-versions-design.md` → "Army books per edition" (dumps), "Armies know their edition". Group **A**: independent of every other phase; one worktree; tasks sequential (1.1 may run beside 1.2–1.6 in its own worktree, it touches only `scripts/ArmyBooks`).

Decisions this phase fixes (the implementer cannot infer them):

- `Army.Edition` and `ArmySnapshot.Edition` are **init properties** with default `ArmyEditions.V35`, not positional parameters: ~30 positional `new Army(...)`/`new ArmySnapshot(...)` sites in tests stay as they are, and an `army_snapshot` JSON written before this phase deserializes as 3.5.
- The edition an import stamps is the constant `ArmyEditions.Served` (`"3.6"`), set with the dump. Phase 7 (Task 7.3) replaces the constant by the newest embedded units file's edition once two exist.
- The backfill keys on rule names only 3.6 books use **and the 3.5 hand part does not define**. `Armor` and `Shred` are in `grimdark-future-army-rules.part.json` already (3.5 books carry them), so they are excluded. The list: `Bravery Aura`, `Anti-Cover`, `Anti-Regen`, `Unique Hero`, `Crossing Strike`, `Piercing Charge`, `Counter in Melee`, `Spell Resistance`, `Versatile Expert`, `Assault`, `Cumbersome`, `Spell Amplifier`, `Smash`, `Pierce`, `Decimate`, `Scourge`, `Purge`, `Imprecise` (modules diff → renames, new weapon rules; verified absent from both 3.5 parts by grep).

---

### Task 1.1: The 3.5 dump moves to `3.5/`; `fetch` writes the served edition's folder — **Sonnet**

**Files:**
- Modify: `scripts/ArmyBooks/Program.cs:5-33` (dump resolution, `fetch`), `scripts/ArmyBooks/ArmyForgeBook.cs:7` (`AfIndexEntry`)
- Modify: `scripts/ArmyBooks/AGENTS.md` (recipe), `server/game/AGENTS.md:139-140` (commands)
- Test: `tests/Tabletop.ArmyBooks.Tests/ArmyForgeJsonTests.cs` (new)
- Filesystem (not committed): `.superpowers/armyforge/{index.json,books/}` → `.superpowers/armyforge/3.5/{index.json,books/}`; backup already at `/home/cstupi/backups/armyforge-gf-3.5-2026-10-29/` (byte-identical `index.json`, 48 books; verified).

**Interfaces:**
- Produces: `static string ArmyForgeJson.EditionOf(IReadOnlyList<AfIndexEntry> index)` in `ArmyForgeBook.cs` → `"3.6"` for an index whose entries' `VersionString` are all `3.6.x`; throws `InvalidDataException` naming the versions when more than one `major.minor` is present or any entry lacks one. `generate --edition 3.5|3.6` (default `3.5`) selects `<dump>/<edition>/` as its input; its outputs are unchanged in this task (phase 7 moves them).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Edition_is_major_minor_of_the_index_versions()
    => Assert.Equal("3.6", ArmyForgeJson.EditionOf([new("a", "A", "3.6.0"), new("b", "B", "3.6.0")]));
[Fact] public void Mixed_or_missing_versions_are_refused()
{
    Assert.Throws<InvalidDataException>(() => ArmyForgeJson.EditionOf([new("a", "A", "3.5.3"), new("b", "B", "3.6.0")]));
    Assert.Throws<InvalidDataException>(() => ArmyForgeJson.EditionOf([new("a", "A", null)]));
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.ArmyBooks.Tests --filter "FullyQualifiedName~ArmyForgeJsonTests"` — FAIL (no `EditionOf`).

- [ ] **Step 3: Implement `EditionOf`** in `ArmyForgeBook.cs`; in `Program.cs`: `fetch` downloads the index to memory, computes `edition = EditionOf(index)`, writes to `<dump>/<edition>/index.json` and `<dump>/<edition>/books/<uid>.json`; if `<dump>/<edition>/index.json` exists and `EditionOf` of it differs from `edition`, print `refusing: <dir> holds edition X, Army Forge serves Y` and return 1. Every other verb reads `Arg("--edition", "3.5")` and uses `<dump>/<edition>/` (replace the `dump` variable's use in `generate`, lines 35-50). `FindDump` is unchanged.

- [ ] **Step 4: Move the dump** (shell, not code): `mkdir .superpowers/armyforge/3.5 && git mv`-free `mv .superpowers/armyforge/index.json .superpowers/armyforge/books .superpowers/armyforge/3.5/`. Then `./scripts/army-books fetch` → prints `fetched 49 books into .../3.6`. `EditionOf` on `3.5/index.json` must be `3.5` (43 at 3.5.3, 4 at 3.5.2 → one `major.minor`; the one entry with no version: `EditionOf` must tolerate a null `VersionString` when at least one other entry fixes the edition — amend the test accordingly: `[new("a","A","3.5.3"), new("b","B",null)]` → `"3.5"`; only an index with *no* version at all throws).

- [ ] **Step 5: Verify the 3.5 output is byte-identical**: `./scripts/army-books generate --edition 3.5 && git status --porcelain server/game/Content server/management/Content` prints nothing. Run the tests: PASS.

- [ ] **Step 6: Recipe.** `scripts/ArmyBooks/AGENTS.md`: "Dumps live at `.superpowers/armyforge/<edition>/`; `fetch` writes the edition Army Forge serves and refuses another edition's folder; the 3.5 dump cannot be fetched again (backup `/home/cstupi/backups/armyforge-gf-3.5-2026-10-29/`); `generate --edition 3.5|3.6`." Update the two command lines in `server/game/AGENTS.md`.

- [ ] **Step 7: Commit** `git add scripts/ArmyBooks tests/Tabletop.ArmyBooks.Tests server/game/AGENTS.md && git commit -m "Army books: dumps per edition; fetch writes the served edition"`.

---

### Task 1.2: Migration `023_army_edition.sql` with the rule-name backfill — **Opus**

**Files:**
- Create: `server/management/Persistence/Migrations/023_army_edition.sql`
- Modify: `scripts/postgres/init/001_schema.sql` is **not** touched (init is 001 only; `Migrator.Baseline` records 001 and runs the rest).
- Test: `tests/Tabletop.Management.Tests/MigrationTests.cs` (E2E, `[Trait("Category","E2E")]`, re-runs an embedded script as the 022 test does)

**Interfaces:**
- Produces: column `management.army.edition text not null`, constraint `army_edition_check check (edition in ('3.5','3.6'))`, no default after the migration (an insert must name it).

- [ ] **Step 1: Write the failing test** (append to `MigrationTests`):

```csharp
[Fact] public async Task Army_edition_is_checked_and_the_backfill_marks_3_6_lists_by_their_rule_names()
{
    await using var c = new NpgsqlConnection(_db.ConnectionString);
    var owner = await SeedAccount(c);                        // helper already used by the 022 test, or insert into management.account
    var a35 = Guid.CreateVersion7(); var a36 = Guid.CreateVersion7();
    await c.ExecuteAsync("insert into management.army (id, owner_id, name, game_system, source, edition) values (@a35, @owner, 'old', 'grimdark-future', 's', '3.5'), (@a36, @owner, 'new', 'grimdark-future', 's', '3.5')", new { a35, a36, owner });
    var unit = Guid.CreateVersion7();
    await c.ExecuteAsync("insert into management.unit (id, army_id, ordinal, name) values (@unit, @a36, 0, 'u'); insert into management.unit_property (unit_id, name) values (@unit, 'Bravery Aura')", new { unit, a36 });
    await c.ExecuteAsync(await Script("023_army_edition.sql"));   // the embedded resource, as the 022 test reads it
    Assert.Equal("3.5", await c.ExecuteScalarAsync<string>("select edition from management.army where id=@a35", new { a35 }));
    Assert.Equal("3.6", await c.ExecuteScalarAsync<string>("select edition from management.army where id=@a36", new { a36 }));
    var e = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync("insert into management.army (id, owner_id, name, game_system, source, edition) values (@id, @owner, 'x', 'grimdark-future', 's', '3.7')", new { id = Guid.CreateVersion7(), owner }));
    Assert.Equal("23514", e.SqlState);
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Management.Tests --settings tests/e2e.runsettings --filter "FullyQualifiedName~MigrationTests"` (stack up) — FAIL: `023_army_edition.sql` resource not found.

- [ ] **Step 3: Write the script**, idempotent so the test can re-run it:

```sql
-- 023_army_edition.sql: an army is a 3.5 or a 3.6 Army Forge list (rule-set versions spec). Existing rows are 3.5; a list whose
-- rules carry a name only 3.6.0 books use was imported after Army Forge switched (2026-10-29T23:48Z) and is 3.6.
alter table management.army add column if not exists edition text not null default '3.5';
update management.army a set edition = '3.6' where edition = '3.5' and exists (
  select 1 from management.unit u
  left join management.unit_property up on up.unit_id = u.id
  left join management.model_slot s on s.unit_id = u.id
  left join management.slot_property sp on sp.slot_id = s.id
  left join management.weapon w on w.slot_id = s.id
  left join management.weapon_property wp on wp.weapon_id = w.id
  where u.army_id = a.id and coalesce(up.name, sp.name, wp.name) in (
    'Bravery Aura','Anti-Cover','Anti-Regen','Unique Hero','Crossing Strike','Piercing Charge','Counter in Melee',
    'Spell Resistance','Versatile Expert','Assault','Cumbersome','Spell Amplifier','Smash','Pierce','Decimate','Scourge','Purge','Imprecise'));
alter table management.army drop constraint if exists army_edition_check;
alter table management.army add constraint army_edition_check check (edition in ('3.5','3.6'));
alter table management.army alter column edition drop default;
```

Because the default is dropped, the test's first insert (before the script runs again) already names `edition`; on a fresh database `Migrator.Run` applies 023 once and the column exists for that insert.

- [ ] **Step 4: Run the test** — PASS. Also `Schema_has_every_table_...` still passes (no new table; 38 stays).

- [ ] **Step 5: Commit** `git add server/management/Persistence/Migrations/023_army_edition.sql tests/Tabletop.Management.Tests/MigrationTests.cs && git commit -m "Management: army.edition with the 3.6 rule-name backfill"`.

---

### Task 1.3: `Army.Edition`, repository, summary and import stamping — **Sonnet**

**Files:**
- Create: `libs/Tabletop.Content/ArmyEditions.cs`
- Modify: `libs/Tabletop.Content/Army.cs:29` (`Army`), `server/management/Content/Armies/ArmyRepository.cs:10` (`ArmySummary`), `:30` (insert), `:48-75` (`Get`), `:84` (`List`), `server/management/Content/Armies/ArmyForgeImport.cs:40`, `server/game/Persistence/PostgresGameRows.cs:151` (`ReadArmyAsync` reads `edition`), `tests/Tabletop.Management.Tests/Infrastructure/InMemoryArmyRepository.cs:50`
- Test: `tests/Tabletop.Content.Tests/ArmyTests.cs`, `tests/Tabletop.Management.Tests/ArmyForgeImportTests.cs`, `tests/Tabletop.Management.Tests/Contracts/RepositoryContract.Content.cs:19`, `tests/Tabletop.GameServer.Tests/GameRowsContract.cs` (army round trip)

**Interfaces:**
- Produces: `public static class ArmyEditions { public const string V35 = "3.5"; public const string V36 = "3.6"; public const string Served = V36; public static readonly IReadOnlyList<string> All = [V35, V36]; }` in `Tabletop.Content`.
- `record Army(Guid Id, string Name, string GameSystem, string Source, IReadOnlyList<Unit> Units) { public string Edition { get; init; } = ArmyEditions.V35; }`.
- `record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models, string Edition = ArmyEditions.V35)` (appended, defaulted: the client and viewer DTOs read it by name).
- `ArmyForgeImport.ToArmy` returns `new Army(...) { Edition = ArmyEditions.Served }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// ArmyTests (Content)
[Fact] public void An_army_is_3_5_unless_stamped() { Assert.Equal("3.5", Sample().Edition); Assert.Equal("3.6", (Sample() with { Edition = ArmyEditions.V36 }).Edition); }
// ArmyForgeImportTests
[Fact] public void Import_stamps_the_edition_army_forge_serves() => Assert.Equal(ArmyEditions.Served, ArmyForgeImport.ToArmy(FixtureList(), "src", Catalog).Edition);
// RepositoryContract.Content (both repositories)
[Fact] public async Task An_army_keeps_its_edition_and_the_summary_shows_it()
{
    var army = ArmyTests.Sample() with { Edition = "3.6" };
    await Armies.Insert(Owner, army);
    Assert.Equal("3.6", (await Armies.Get(Owner, army.Id))!.Edition);
    Assert.Equal("3.6", (await Armies.List(Owner, Keyset.First(10))).Single().Edition);
}
// GameRowsContract: the seeded army read by ReadArmyAsync carries its edition (seed one with '3.6' through the fixture's insert)
```

- [ ] **Step 2: Run** the four test classes — FAIL (no `Edition`).

- [ ] **Step 3: Implement.** SQL: insert adds `edition`; `Get` selects `edition` into the head tuple; `List` selects `a.edition as Edition`; `PostgresGameRows.ReadArmyAsync` selects `edition` and sets it. The in-memory repository copies `Army.Edition` into the summary. `InMemoryRows`'s seed of armies (if it builds `Army` rows) keeps the default.

- [ ] **Step 4: Run** `dotnet test tests/Tabletop.Content.Tests tests/Tabletop.Management.Tests tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Army|FullyQualifiedName~RepositoryContract|FullyQualifiedName~GameRowsContract"` — PASS. E2E (`PostgresRepositoryContractTests`, `PostgresGameRowsTests`) with the stack up — PASS.

- [ ] **Step 5: Commit** `git commit -am "Armies carry their edition; import stamps the served one"`.

---

### Task 1.4: `ArmySnapshot.Edition` — **Sonnet**

**Files:**
- Modify: `libs/Tabletop.Content/Army.cs:37-39`, `sim/Scenarios/ScenarioCompiler.cs:117`
- Regenerate: `viewer/src/lib/protocol/wire.generated.ts`, `viewer/tests/fixtures/` (`tests/AGENTS.md` → Regenerating)
- Test: `tests/Tabletop.Content.Tests/ArmyTests.cs`

**Interfaces:**
- Produces: `record ArmySnapshot(Guid Id, string Name, string GameSystem, IReadOnlyList<Unit> Units) { public string Edition { get; init; } = ArmyEditions.V35; }`; `Of(army, id)` copies `army.Edition`. Phase 6 (Task 6.1) makes `ScenarioCompiler.BuildArmy` stamp the game's edition; here it stays the default.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void A_snapshot_keeps_the_armys_edition() => Assert.Equal("3.6", ArmySnapshot.Of(Sample() with { Edition = "3.6" }, Guid.NewGuid()).Edition);
[Fact] public void A_snapshot_written_before_editions_reads_as_3_5()
    => Assert.Equal("3.5", ContentJson.Deserialize<ArmySnapshot>("""{"id":"00000000-0000-0000-0000-000000000001","name":"a","gameSystem":"grimdark-future","units":[]}""")!.Edition);
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Content.Tests --filter "FullyQualifiedName~ArmyTests"` — FAIL.
- [ ] **Step 3: Implement**; regenerate the viewer types: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check && npm test`.
- [ ] **Step 4: Run** Content, Protocol, GameServer (`GameRowsContract`), Viewer tests — PASS.
- [ ] **Step 5: Commit** `git commit -am "ArmySnapshot carries the army's edition"`.

---

### Task 1.5: The web app shows an army's edition — **Sonnet**

**Files:**
- Modify: `web/src/lib/api/types.ts:31,87` (`Army`, `ArmySummary` gain `edition: string`), `web/src/routes/armies/+page.svelte:77,82` (a column `Edition` after `System`, cell `{a.edition}`), `web/src/routes/armies/[id]/+page.svelte:60` (`{army.gameSystem} · {army.edition} · imported from …`)
- Test: `web/tests/armies.test.ts` (fixtures gain `edition: '3.6'`; `reimport` keeps the body unchanged — assert the request body still has only `source`), `web/tests/api.test.ts` (an `ArmySummary` with `edition` parses)

- [ ] **Step 1: Failing test**: in `api.test.ts`, a listed army's `edition` is `'3.6'` from a response fixture that carries it (TypeScript: the field must exist on the type, so the test fails to type-check until Step 3).
- [ ] **Step 2: Run** `cd web && npm test` — FAIL (type error).
- [ ] **Step 3: Implement** the three files.
- [ ] **Step 4: Run** `cd web && npm test` — PASS. Open `http://127.0.0.1:5173/armies` against the stack (phase 1.3 deployed to the dev stack via `dotnet watch`): the column shows `3.5` for the owner's armies, `3.6` for a fresh import.
- [ ] **Step 5: Commit** `git commit -am "Web: army pages show the edition"`.

---

### Task 1.6: The Godot and browser lobbies label armies with their edition — **Sonnet**

**Files:**
- Modify: `client/src/Core/ManagementDtos.cs:21` (`ArmySummary(..., string Edition = "3.5")`), `client/src/Core/LobbyView.cs` (add `public static string ArmyLabel(ArmySummary a) => $"{a.Name} ({a.Edition})"`), `client/src/Panels/LobbyPanel.cs:51`, `client/src/Panels/WaitingPanel.cs:~56`, `client/src/Screens/HomeScreen.cs:252` (item text `$"{LobbyView.ArmyLabel(a)}, {a.Units} units"`)
- Modify: `viewer/src/lib/api/types.ts:16` (`edition: string`), `viewer/src/lib/lobby/lobbyView.ts` (`armyLabel(a: ArmySummary): string` → `` `${a.name} (${a.edition})` ``), `viewer/src/lib/lobby/TeamCards.svelte:57`, `viewer/src/lib/games/CreateGameDialog.svelte:~88`, `viewer/src/lib/play/WaitingPanel.svelte` (same label)
- Test: `tests/Tabletop.Client.Tests/LobbyViewTests.cs`, `viewer/tests/lobbyView.test.ts`

- [ ] **Step 1: Failing tests**: `Army_label_is_name_and_edition` → `"Orcs (3.6)"` in both suites.
- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Client.Tests --filter LobbyView` and `cd viewer && npm test -- lobbyView` — FAIL.
- [ ] **Step 3: Implement**; the nodes and components call the label function (no other behaviour change).
- [ ] **Step 4: Verify** `dotnet test tests/Tabletop.Client.Tests`, `dotnet build client/Tabletop.csproj && ./scripts/check-client`, `cd viewer && npm run check && npm test` — PASS. `CallsViewerFixtureTests`/`calls.agreement` are unaffected (labels are not sent).
- [ ] **Step 5: Recipe**: none new (labels are Core/`lobbyView.ts` helpers, the existing rule in `client/AGENTS.md` and `viewer/AGENTS.md`).
- [ ] **Step 6: Commit** `git commit -am "Lobbies label armies with their edition"`.
