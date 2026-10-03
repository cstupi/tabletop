# Task 5: Near terrain: *Grounded Reinforcement*

**Files:**
- Modify: `server/game/Interpreter/Battlefield.cs` (`TerrainUnder` adds `near`)
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (one entry's rows)
- Test: `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`

**Interfaces:**
- Consumes: `Distance.EdgeToEdge(PlacedModel, WorldVolume)`, `Tolerance.Distance` (Geometry); `Battlefield.Terrain`, `Placed`.
- Produces: `Battlefield.TerrainUnder` lists `"near"` beside the declarations when more than half the unit's living models are within 1" of a declared terrain volume.

Spec → *Near terrain*. *"If a unit where all models have this rule has most of them within 1" of terrain, they get +1 to defense rolls."* p.11: *"any rules that affect terrain do not apply to open terrain"*, so a volume declaring no terrain type does not count.

- [ ] **Step 1: Write the failing test**

Add to `ArmyRuleHookTests`:

```csharp
    [Fact]
    public void Grounded_reinforcement_gives_plus_one_to_defense_near_declared_terrain_only()
    {
        Tabletop.Content.TerrainPlacement Block(Tabletop.Geometry.TerrainDeclarations d) => new(Guid.CreateVersion7(), Tabletop.Content.VisualRef.OfBuiltin("block"), new Tabletop.Geometry.Point3(7.5, -18.5, 0), new Tabletop.Geometry.Yaw(0), 1,
            [new Tabletop.Content.TerrainVolume(Guid.CreateVersion7(), new Tabletop.Geometry.Volume(Tabletop.Geometry.VolumeKind.Solid, Tabletop.Geometry.Footprint.Rect(1, 4), new Tabletop.Geometry.Point2(0, 0), new Tabletop.Geometry.Yaw(0), 0, 1, d, null))]);
        foreach (var (declarations, expected) in new[] { (Tabletop.Geometry.TerrainDeclarations.Cover, true), (Tabletop.Geometry.TerrainDeclarations.None, false) })
        {
            var a = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, [P("Grounded Reinforcement")]);
            var sim = new Sim(State([a], [b], x1: -6, x2: 6, terrain: [Block(declarations)]), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5, 3));
            sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
            sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
            Assert.Equal(expected, sim.Steps("roll-to-block").Single().Rolls.Single().Modifiers.Any(m => m.Source == "Grounded Reinforcement"));
        }
    }
```

The defender's model stands at (6, −18.5), its base's edge at 6.63; the block spans x 7 to 8, so its near face is 0.37" away. A solid block 1" tall that declares cover gives no cover here (the shooter is not behind it), so the only defense modifier under test is the rule's.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~Grounded_reinforcement`
Expected: FAIL: no modifier.

- [ ] **Step 3: The row**

In the part file, replace this entry's `"effects": []`:

```json
"Grounded Reinforcement": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": 1, "conditions": [{"kind": "position_terrain", "subject": "self", "comparison": "eq", "values": ["near"]}]}]
```

- [ ] **Step 4: `near`**

In `Battlefield.TerrainUnder`, return the declarations with `near` added:

```csharp
    public static IReadOnlyList<string> TerrainUnder(GameState state, Guid unitId)
    {
        var terrain = Terrain(state);
        var living = Effects.Lookup.LivingModels(state, unitId);
        var flags = living.SelectMany(id => Containment.VolumesContaining(Placed(state, id)!.Value, terrain)).Distinct()
            .Select(id => terrain.First(v => v.Id == id).Volume.Declarations).Aggregate(TerrainDeclarations.None, (acc, d) => acc | d);
        var names = Enum.GetValues<TerrainDeclarations>().Where(f => f != TerrainDeclarations.None && flags.HasFlag(f)).Select(f => Tabletop.Content.ContentJson.Serialize(f).Trim('"')).ToList();
        // Grounded Reinforcement: "most of them within 1" of terrain"; p.11: a volume declaring no terrain type is open terrain, which no rule affects.
        var declared = terrain.Where(v => v.Volume.Declarations != TerrainDeclarations.None).ToList();
        if (living.Count > 0 && living.Count(id => declared.Any(v => Distance.EdgeToEdge(Placed(state, id)!.Value, v) <= 1 + Tolerance.Distance)) * 2 > living.Count)
            names.Add("near");
        return names;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. No core rule's `position_terrain` names `near`, so nothing else changes.

- [ ] **Step 6: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs
git commit -m "Give Grounded Reinforcement +1 to defense with most models within 1 inch of terrain"
```
