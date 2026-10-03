# Task 8: Every granted rule works, and the `army-rules` scenario

**Files:**
- Modify: `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs` (the every-spell theory checks the fifteen rules)
- Modify: `sim/Scenarios/ScenarioCatalog.cs` (`army-rules`)
- Modify: `tests/Tabletop.Sim.Tests/ScenarioTests.cs`, `tests/Tabletop.Sim.Tests/DriverAgreementTests.cs`

**Interfaces:**
- Consumes: `SpellLibraryTests.Every_spell_is_cast_and_does_what_its_family_promises` (library Task 13); every hook of Tasks 2 to 7; `ScenarioBuilder.Army(..., weapons, ...)`; `Expectation.AskAsked`.
- Produces: catalog scenario `army-rules`.

Spec → *Testing*: the library's every-spell theory gains, for the fifteen grants, an assertion that the granted rule is live where its grant puts it; the simulator gains `army-rules`, run on both drivers, under the seven invariants.

- [ ] **Step 1: Extend the theory**

In `SpellLibraryTests.Every_spell_is_cast_and_does_what_its_family_promises`, inside the `default:` branch after the grant assertion:

```csharp
                    // The fifteen army-rule hooks (army-rule hooks spec): a granted rule is live on the side its row names.
                    if (row.Kind == EffectKind.GrantProperty && HookRules.Contains(row.TargetEntry!))
                    {
                        var other = targetUnit == wizard.Id ? enemy.Id : wizard.Id;
                        var (attacker, defender) = row.Subject == EffectSubject.Self ? (targetUnit, other) : (other, targetUnit);
                        var live = LiveProperties.Settle(new Interaction(sim.State, props, Party.Whole(sim.State, attacker), Party.Whole(sim.State, defender),
                            row.Conditions.Any(c => c.Kind == ConditionKind.AttackKind && c.Values.Contains("melee")) ? AttackKind.Melee : AttackKind.Shooting, GfNames.Advance, 12, []));
                        Assert.True(live.Has(Side.Attacker, row.TargetEntry!), $"{name}: {row.TargetEntry} is not live on the side its grant names");
                    }
```

and the set beside `EverySpell`:

```csharp
    static readonly HashSet<string> HookRules = ["Hit & Run Fighter", "Hit & Run Shooter", "Harassing", "Harassing Boost", "Quick Shot", "Grounded Reinforcement", "Teleport",
        "Rapid Blink", "Rapid Blink Boost", "Unpredictable Fighter", "Unpredictable Shooter", "Primal", "Primal Boost", "Clan Warrior", "Clan Warrior Boost"];
```

(add `using Tabletop.GameServer.Effects;`). A self grant is live on its holder as the attacker; an *against* grant (*Combat Ecstasy*'s *Quick Shot*, *Terror Seeker*'s *Unpredictable Fighter*, *Mob Frenzy*'s *Unpredictable Shooter*) is live on the caster's side attacking the marked unit. The grants were written by the cast just made, so the theory's one test per spell covers every way a spell reaches these rules.

- [ ] **Step 2: The scenario**

In `ScenarioCatalog`, beside `casting`:

```csharp
        // Every army-rule hook in one game (army-rule hooks spec → Testing): moves after melee and shooting, a placement before attacking and at
        // activation, shooting after Rush, a die at the attack's start, extra attacks, and the near-terrain bonus, on the terrain board.
        ["army-rules"] = () => new ScenarioBuilder("army-rules", seed: 1)
            .Army("Red", 4, 3,
                modelRules: [new ScenarioRule(1, "Hit & Run Fighter"), new ScenarioRule(2, "Teleport"), new ScenarioRule(3, "Quick Shot"), new ScenarioRule(4, "Rapid Blink")],
                weapons: [Rifle, HandWeapon])
            .Army("Blue", 4, 3,
                modelRules: [new ScenarioRule(1, "Hit & Run Shooter"), new ScenarioRule(2, "Unpredictable Fighter"), new ScenarioRule(3, "Primal"), new ScenarioRule(4, "Grounded Reinforcement")],
                weapons: [Rifle, HandWeapon])
            .Terrain("ruin", new Point3(36, 24, 0), volumes:
                [new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, null)])
            .Expect(new Expectation.AskAsked("react"))
            .Build(),
```

with the two weapons as fields of the class:

```csharp
    static readonly ScenarioWeapon Rifle = new("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, []);
    static readonly ScenarioWeapon HandWeapon = new("Hand Weapon", new Dictionary<string, string> { ["Attacks"] = "1" }, []);
```

- [ ] **Step 3: Run it on both drivers**

In `ScenarioTests`, add:

```csharp
    [Fact]
    public void The_army_rules_scenario_fields_every_hook_rule()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("army-rules"));
        var rules = c.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models).SelectMany(m => m.Properties).Select(p => p.Name).ToHashSet();
        Assert.Superset(rules, new HashSet<string> { "Hit & Run Fighter", "Hit & Run Shooter", "Teleport", "Rapid Blink", "Quick Shot", "Unpredictable Fighter", "Primal", "Grounded Reinforcement" });
    }
```

In `DriverAgreementTests`, add `[InlineData("army-rules", 5UL)]` to the agreement theory (library Task 14 gave it the scenario parameter).

Run: `docker compose up -d && dotnet test tests/Tabletop.Sim.Tests && dotnet run --project sim -- army-rules --seeds 1..20`
Expected: PASS; the console run passes all twenty seeds (exit code 0), each holding the seven invariants. A `stuck` seed is a hook that never closes its window: read the dumped frames for the last Ask.

- [ ] **Step 4: Run every suite**

Run: `dotnet test && (cd viewer && npm run check)`
Expected: PASS; `wire.generated.ts` unchanged.

- [ ] **Step 5: Commit**

```bash
git add tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs sim/Scenarios/ScenarioCatalog.cs tests/Tabletop.Sim.Tests
git commit -m "Check every granted hook rule in the spell theory and play them all in the army-rules scenario"
```
