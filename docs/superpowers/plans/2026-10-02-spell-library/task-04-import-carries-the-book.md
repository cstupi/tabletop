# Task 4: Import carries the book

**Files:**
- Modify: `server/management/Content/Armies/ArmyForgeList.cs` (`AfUnit.ArmyId`)
- Modify: `server/management/Content/Armies/ArmyForgeImport.cs` (`Section` writes the slot's book)
- Modify: `server/management/Content/Armies/ArmyRepository.cs` (insert and read `army_book`)
- Modify: `server/game/Persistence/PostgresGameRows.cs` (`ReadArmyAsync` reads `army_book`)
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/PostgresFixture.cs` (`SeedArmyAsync` writes `army_book`)
- Modify: `web/src/lib/api/types.ts` (`armyBook?: string` on `ModelSlot`)
- Test: `tests/Tabletop.Management.Tests/ArmyForgeImportTests.cs`, `tests/Tabletop.Management.Tests/ArmyTests.cs`, `tests/Tabletop.GameServer.Tests/GameRowsReadTests.cs`

**Interfaces:**
- Consumes: `ModelSlot.ArmyBook` (Task 1); `model_slot.army_book` (Task 3).
- Produces: `AfUnit(..., bool Combined, string? JoinToUnit, string? ArmyId = null)`.

Spec → *A caster casts its own book's spells*: the TTS export carries `armyId` on every unit entry, and import writes it to each slot built from that entry, a joined entry's slots included. Re-import is how an army imported earlier gets its book; there is no backfill and no call to Army Forge outside import.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Tabletop.Management.Tests/ArmyForgeImportTests.cs`:

```csharp
    [Fact]
    public void Every_slot_carries_the_army_book_its_entry_came_from()
    {
        var army = ArmyForgeImport.ToArmy(Fixture(), "c2-6YsWRH3py", Catalog);
        Assert.All(army.Units.SelectMany(u => u.Models), m => Assert.Equal("prsajf5ot936qznk", m.ArmyBook));
    }

    [Fact]
    public void A_joined_entry_keeps_its_own_army_book()
    {
        var list = Fixture();
        var host = list.Units.First(u => !u.Combined && u.JoinToUnit is null);
        var hero = host with { SelectionId = "hero", Name = "Visiting Hero", Size = 1, JoinToUnit = host.SelectionId, ArmyId = "another-book" };
        var army = ArmyForgeImport.ToArmy(list with { Units = [.. list.Units, hero] }, "c2-6YsWRH3py", Catalog);
        var unit = army.Units.Single(u => u.Models.Any(m => m.Name == "Visiting Hero 1"));
        Assert.Equal("another-book", unit.Models.Single(m => m.Name == "Visiting Hero 1").ArmyBook);
        Assert.All(unit.Models.Where(m => m.Name != "Visiting Hero 1"), m => Assert.Equal("prsajf5ot936qznk", m.ArmyBook));
    }
```

In `tests/Tabletop.Management.Tests/ArmyTests.cs`, `Import_stores_the_whole_tree_and_reads_it_back_identically`, add after the `fetched` line:

```csharp
        Assert.All(fetched.Units.SelectMany(u => u.Models), m => Assert.Equal("prsajf5ot936qznk", m.ArmyBook));
```

Add to `tests/Tabletop.GameServer.Tests/GameRowsReadTests.cs`:

```csharp
    [Fact]
    public async Task A_slots_army_book_reads_back()
    {
        var sample = Tabletop.Content.Tests.ArmyTests.Sample();
        var unit = sample.Units[0];
        var army = sample with { Units = [unit with { Models = [unit.Models[0] with { ArmyBook = "z65fgu0l29i4lnlu" }, .. unit.Models.Skip(1)] }, .. sample.Units.Skip(1)] };
        var seeded = await pg.SeedArmyAsync(await pg.SeedAccountAsync("Owner"), army);
        var read = (await _rows.ReadArmyAsync(seeded.Id, CancellationToken.None))!.Army;
        Assert.Equal("z65fgu0l29i4lnlu", read.Units[0].Models[0].ArmyBook);
        Assert.Equal(Tabletop.Content.ContentJson.Serialize(seeded), Tabletop.Content.ContentJson.Serialize(read));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Management.Tests --filter "FullyQualifiedName~ArmyForgeImportTests|FullyQualifiedName~ArmyTests"`
Expected: compile error (`AfUnit` has no `ArmyId`), then, once it compiles, the book assertions fail with `null`.

- [ ] **Step 3: Read `armyId` and write it to the slots**

In `server/management/Content/Armies/ArmyForgeList.cs`:

```csharp
public sealed record AfUnit(string SelectionId, string Name, int Size, JsonElement Quality, JsonElement Defense,
    List<AfRule>? Rules, List<AfLoadoutItem>? Loadout, AfBases? Bases, bool Combined, string? JoinToUnit, string? ArmyId = null);
```

In `ArmyForgeImport.Section`, the slot constructor's last argument is the entry's book:

```csharp
            into.Add(new ModelSlot(Guid.CreateVersion7(), into.Count + 1, $"{u.Name} {i + 1}", new Dictionary<string, string>(slotStats),
                slotProps[i], body.Base, body.Height, null, slotWeapons[i], u.ArmyId));
```

`Merge` keeps a combined pair's host entry (`u with { ... }`), so its `ArmyId` is the host's, which is the partner's too: a combined pair is two halves of one Army Forge unit.

- [ ] **Step 4: Store and read it in management**

In `ArmyRepository`:

```csharp
    private sealed record SlotRow(Guid Id, Guid UnitId, int Ordinal, string Name, string BaseShape, double BaseA, double BaseB, double Height, Guid? AssetId, string? ArmyBook);
```

the slot insert:

```csharp
                await c.ExecuteAsync("insert into model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height, asset_id, army_book) values (@Id, @unitId, @Ordinal, @Name, @shape, @a, @b, @Height, @AssetId, @ArmyBook)",
                    new { m.Id, unitId = u.Id, m.Ordinal, m.Name, shape = m.Base.Shape.ToString().ToLowerInvariant(), a = m.Base.A, b = m.Base.B, m.Height, m.AssetId, m.ArmyBook }, tx);
```

the slot query in `Get` gains `, s.army_book as ArmyBook` after `s.asset_id as AssetId`, and its `ModelSlot` constructor passes `s.ArmyBook` after the weapons list:

```csharp
                weapons[s.Id].Select(w => new Weapon(w.Id, w.Name, Stats(weaponStats, w.Id), Props(weaponProps, w.Id))).ToList(), s.ArmyBook)).ToList())).ToList());
```

`Patch` writes only `asset_id`, `base_*` and `height`, so a patch never clears the book.

- [ ] **Step 5: Read it in the game server**

In `PostgresGameRows.ReadArmyAsync`, the slot query becomes `select m.id, m.unit_id, m.ordinal, m.name, m.base_shape, m.base_a, m.base_b, m.height, m.asset_id, m.army_book from ...`, and the `ModelSlot` constructor's last argument is `NullableString(r, 9)` after the weapons list.

In `tests/Tabletop.GameServer.Tests/Infrastructure/PostgresFixture.cs`, `SeedArmyAsync`'s slot insert (line 110) gains the column and the value:

```csharp
                await Run("insert into management.model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height, asset_id, army_book) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
                    s.Id, u.Id, s.Ordinal, s.Name, RowMapping.EnumText(s.Base.Shape), s.Base.A, s.Base.B, s.Height, s.AssetId, s.ArmyBook);
```

- [ ] **Step 6: The web type**

In `web/src/lib/api/types.ts`, the `ModelSlot` interface's last line becomes:

```ts
  base: Base; height: number; assetId?: Guid; weapons: Weapon[]; armyBook?: string;
```

The web app reads nothing from it; the field is declared so the type matches what `GET /v1/armies/{id}` returns.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.Management.Tests && dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~GameRowsReadTests && (cd web && npm test && npx svelte-check)`
Expected: PASS; `svelte-check` reports 0 errors.

- [ ] **Step 8: Commit**

```bash
git add server/management/Content/Armies server/game/Persistence/PostgresGameRows.cs tests/Tabletop.Management.Tests tests/Tabletop.GameServer.Tests web/src/lib/api/types.ts
git commit -m "Import each slot's army book from Army Forge and store it"
```
