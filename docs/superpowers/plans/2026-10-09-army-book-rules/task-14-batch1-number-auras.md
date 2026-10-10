# Task 14: Batch 1: written-out Auras with a number

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (nine entries)
- Modify: `sim/Suite/SuiteCatalog.Army.Auras.cs` (`ArmyGames.NumberAuras`)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (the `Army()` list)
- Modify: `tests/Tabletop.GameServer.Tests/Batch1AurasTests.cs`; the suite games

**Interfaces:**
- Consumes: `Batch1AurasTests.Holds` (Task 13); `Game`, `Morale`, `Allowance`, `ApGrant`, `Carrying`, `Carriers`,
  `GrantedModifier`, `RangeBonus`, `Each`, `Melee`, `Shooting`, `BattleBrothers`, `Guerrillas` (Tasks 8–13).
- Produces: nine Aura entries; `ArmyGames.NumberAuras`. With Task 13's twelve, the spec's 21 written-out Auras.

| Entry | Army Forge text | Rows | Book |
| --- | --- | --- | --- |
| Courage Aura | "This model and its unit get +1 to morale test rolls." | morale +1 | Battle Brothers |
| Fast Aura | "This model and its unit moves +2" when using Advance actions and +4" when using Rush/Charge actions." | Fast's rows | Blessed Sisters |
| Increased Shooting Range Aura | "This model and its unit get +6" range when shooting." | weapon Range +6, shooting | Alien Hives |
| Piercing Fighter Aura | "This model and its unit get AP(+1) in melee." | grant AP 1, melee | Orc Marauders |
| Piercing Shooter Aura | "This model and its unit get AP(+1) when shooting." | grant AP 1, shooting | Wormhole Daemons of War |
| Precision Charge Aura | "This model and its unit get +1 to hit rolls when charging." | hit +1, Charge, melee (Thrust's to-hit row) | Orc Marauders |
| Precision Fighter Aura | "This model and its unit get +1 to hit rolls in melee." | hit +1, melee | Eternal Dynasty |
| Precision Shooter Aura | "This model and its unit get +1 to hit rolls when shooting." | hit +1, shooting | Human Inquisition |
| Rapid Charge Aura | "This model and its unit moves +4" when using Charge actions." | Charge +4 | Alien Hives |

Courage, Fast and Rapid Charge Aura are read where a unit tests or moves, not per attack, so their games put the Aura
on every model (the check then holds while any model stands); the content test pins *"this model and its unit"* with
one carrier.

- [ ] **Step 1: Write the failing content tests**

In `tests/Tabletop.GameServer.Tests/Batch1AurasTests.cs`, add inside the class (and `using
Tabletop.GameServer.Rules.GrimdarkFuture;` at the top):

```csharp
    static int Mod(LiveSet l, string roll) => l.Effects(roll == "defense" ? Side.Defender : Side.Attacker, EffectKind.ModifyRoll)
        .Where(x => x.Effect.TargetRoll == roll).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Courage_aura_is_plus_one_to_morale_and_the_move_auras_lengthen_moves()
    {
        Assert.Equal(1, Mod(Holds("Courage Aura", AttackKind.Shooting), "morale"));
        var fast = Holds("Fast Aura", AttackKind.Shooting);
        Assert.Equal((8.0, 16.0, 16.0), (StatReader.ActionDistance(fast, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(fast, Side.Attacker, GfNames.Rush), StatReader.ActionDistance(fast, Side.Attacker, GfNames.Charge)));
        Assert.Equal(16.0, StatReader.ActionDistance(Holds("Rapid Charge Aura", AttackKind.Melee), Side.Attacker, GfNames.Charge));
        Assert.Equal(6.0, StatReader.ActionDistance(Holds("Fast Aura", AttackKind.Shooting, carrierAttacks: false), Side.Attacker, GfNames.Advance));
    }

    [Fact]
    public void Increased_shooting_range_aura_adds_six_inches_when_shooting()
    {
        var u = Unit("A", "4+", "4+", [], 3, [], Gun("Gun", "24\"", 1));
        var a = u with { Models = [u.Models[0] with { Properties = [P("Increased Shooting Range Aura")] }, .. u.Models.Skip(1)] };
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        var gun = s.Projection.Models.Values.First(m => m.UnitId == a.Id && m.Slot.Ordinal == 2).Slot.Weapons.Single().Id;   // a model without the Aura
        var live = LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), Party.Whole(s, b.Id), AttackKind.Shooting, null, 20, []));
        Assert.Equal(30.0, StatReader.WeaponRange(live, Side.Attacker, gun));
    }

    [Fact]
    public void Piercing_and_precision_auras_act_in_their_attack_kind()
    {
        Assert.Equal(((int?)1, (int?)null), (Holds("Piercing Fighter Aura", AttackKind.Melee).Parameter(Side.Attacker, "AP"), Holds("Piercing Fighter Aura", AttackKind.Shooting).Parameter(Side.Attacker, "AP")));
        Assert.Equal(((int?)1, (int?)null), (Holds("Piercing Shooter Aura", AttackKind.Shooting).Parameter(Side.Attacker, "AP"), Holds("Piercing Shooter Aura", AttackKind.Melee).Parameter(Side.Attacker, "AP")));
        Assert.Equal((1, 0), (Mod(Holds("Precision Charge Aura", AttackKind.Melee, action: GfNames.Charge), "hit"), Mod(Holds("Precision Charge Aura", AttackKind.Melee), "hit")));
        Assert.Equal((1, 0), (Mod(Holds("Precision Fighter Aura", AttackKind.Melee), "hit"), Mod(Holds("Precision Fighter Aura", AttackKind.Shooting), "hit")));
        Assert.Equal((1, 0), (Mod(Holds("Precision Shooter Aura", AttackKind.Shooting), "hit"), Mod(Holds("Precision Shooter Aura", AttackKind.Melee), "hit")));
        Assert.Equal(0, Mod(Holds("Precision Shooter Aura", AttackKind.Shooting, carrierAttacks: false), "hit"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1AurasTests"`
Expected: the three new tests FAIL (text-only); Task 13's theory passes.

- [ ] **Step 3: The entries**

In `server/game/Content/grimdark-future-army-rules.part.json`, add in name order:

```json
    {"name": "Courage Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get +1 to morale test rolls.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "morale", "amount": 1}]},
    {"name": "Fast Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit moves +2\" when using Advance actions and +4\" when using Rush/Charge actions.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 2}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 4}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4}]},
    {"name": "Increased Shooting Range Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get +6\" range when shooting.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetStat": {"level": "weapon", "name": "Range"}, "amount": 6, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Piercing Fighter Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get AP(+1) in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Piercing Shooter Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get AP(+1) when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Precision Charge Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get +1 to hit rolls when charging.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Precision Fighter Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get +1 to hit rolls in melee.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Precision Shooter Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get +1 to hit rolls when shooting.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Rapid Charge Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit moves +4\" when using Charge actions.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4}]},
```

- [ ] **Step 4: Regenerate and run the content tests**

Run: `./scripts/army-books generate && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1AurasTests|FullyQualifiedName~ArmyRulesContentTests"`
Expected: PASS; the report's "Aura, effect written out" reads 0.

- [ ] **Step 5: The games**

In `sim/Suite/SuiteCatalog.Army.Auras.cs`, add inside `ArmyGames` after `GrantAuras`:

```csharp
        static (string Uid, string Army) WarDaemons => ("prsajf5ot936qznk", "Wormhole Daemons of War");

        public static IReadOnlyList<TestGame> NumberAuras =>
        [
            // p.10: the fixed hits leave the unit at half or less, so it tests morale; every model carries the Aura, so a casualty cannot take it away.
            Morale(BattleBrothers, "battle-brothers-courage-aura", "Courage Aura", [R(1, "Courage Aura")], 1, "\"This model and its unit get +1 to morale test rolls.\""),
            Game(Sisters, "blessed-sisters-fast-aura", Play.Moves, ["Fast Aura"], Each(4, "Fast Aura"), [],
                [Allowance("Fast Aura", null, "\"This model and its unit moves +2\" when using Advance actions and +4\" when using Rush/Charge actions\"", ("advance", 8), ("rush", 16), ("charge", 16))]),
            Game(Hives, "alien-hives-increased-shooting-range-aura", Play.Shoots, ["Increased Shooting Range Aura"], [], [],
                [RangeBonus("Increased Shooting Range Aura", Carrying("Increased Shooting Range Aura", When.Shooting), 12, 6, "\"This model and its unit get +6\" range when shooting.\"")],
                carriers: Carriers("Increased Shooting Range Aura", 2), units: 2, gunRange: 12),
            Melee(Orcs, "orc-marauders-piercing-fighter-aura", "Piercing Fighter Aura",
                ApGrant("Piercing Fighter Aura", Carrying("Piercing Fighter Aura", When.Charging), 1, "\"This model and its unit get AP(+1) in melee.\"")),
            Shooting(WarDaemons, "wormhole-daemons-of-war-piercing-shooter-aura", "Piercing Shooter Aura",
                ApGrant("Piercing Shooter Aura", Carrying("Piercing Shooter Aura", When.Shooting), 1, "\"This model and its unit get AP(+1) when shooting.\"")),
            Melee(Orcs, "orc-marauders-precision-charge-aura", "Precision Charge Aura",
                GrantedModifier("Precision Charge Aura", Carrying("Precision Charge Aura", When.Charging), "Precision Charge Aura", "hit", 1, "\"This model and its unit get +1 to hit rolls when charging.\"")),
            Melee(Dynasty, "eternal-dynasty-precision-fighter-aura", "Precision Fighter Aura",
                GrantedModifier("Precision Fighter Aura", Carrying("Precision Fighter Aura", When.Charging), "Precision Fighter Aura", "hit", 1, "\"This model and its unit get +1 to hit rolls in melee.\"")),
            Shooting(Inquisition, "human-inquisition-precision-shooter-aura", "Precision Shooter Aura",
                GrantedModifier("Precision Shooter Aura", Carrying("Precision Shooter Aura", When.Shooting), "Precision Shooter Aura", "hit", 1, "\"This model and its unit get +1 to hit rolls when shooting.\"")),
            Game(Hives, "alien-hives-rapid-charge-aura", Play.Charges, ["Rapid Charge Aura"], Each(4, "Rapid Charge Aura"), [],
                [Allowance("Rapid Charge Aura", null, "\"This model and its unit moves +4\" when using Charge actions\"", ("charge", 16))]),
        ];
```

`Inquisition` is Task 11's. In `sim/Suite/SuiteCatalog.Army.cs`, extend the list with `.. ArmyGames.NumberAuras`.

- [ ] **Step 6: Run the games, the army tests and the full suite**

Run: `dotnet build sim && dotnet run --project sim -- suite --only battle-brothers-courage-aura,blessed-sisters-fast-aura,alien-hives-increased-shooting-range-aura,orc-marauders-piercing-fighter-aura,wormhole-daemons-of-war-piercing-shooter-aura,orc-marauders-precision-charge-aura,eternal-dynasty-precision-fighter-aura,human-inquisition-precision-shooter-aura,alien-hives-rapid-charge-aura`
Expected: nine `pass` lines.

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite`
Expected: PASS; the suite's last line starts `PASSED:`.

- [ ] **Step 7: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.Auras.cs tests/Tabletop.GameServer.Tests/Batch1AurasTests.cs
git commit -m "Play the written-out Auras with a number: Courage, Fast, range, Piercing, Precision, Rapid Charge"
```
