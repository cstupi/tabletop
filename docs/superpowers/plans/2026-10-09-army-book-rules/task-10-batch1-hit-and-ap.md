# Task 10: Batch 1: hit and AP rules

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (nine entries)
- Create: `sim/Suite/SuiteCatalog.Army.HitAndAp.cs` (`ArmyGames.HitAndAp`)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (the `Army()` list)
- Test: `tests/Tabletop.GameServer.Tests/Batch1HitAndApTests.cs` (new); the suite games

**Interfaces:**
- Consumes: `Game`, `Modifier`, `ExtraHits`, `ExtraAttacks`, `ApGrant`, `By`, `Within` (Task 8), `All3`, `Each`
  (Task 9).
- Produces: entries Good Shot, Precise, Point-Blank Surge, Sergeant, Predator Fighter, Predator Shooter, Melee Slayer,
  Ranged Slayer, Point-Blank Piercing; `ArmyGames.HitAndAp`.

| Entry | Army Forge text | Rows (the shape it copies) | Book |
| --- | --- | --- | --- |
| Good Shot | "This model gets +1 to hit rolls when shooting." | hit +1, shooting (Targeting Visor without range) | DAO Union |
| Precise | "Gets +1 to hit when attacking." | hit +1, melee or shooting (Primal's "when attacking") | Alien Hives |
| Point-Blank Surge | "When this model shoots at enemies within 12", unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules)." | add_hit 1 on raw 6, shooting, range ≤ 12 (Relentless) | Blessed Sisters |
| Sergeant | "When this model attacks, unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules)." | add_hit 1 on raw 6, melee or shooting | Wormhole Daemons of Change |
| Predator Fighter | "For each unmodified roll of 6 to hit in melee, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks." | add_hit attacks 1 on raw 6, melee (Primal) | Alien Hives |
| Predator Shooter | "For each unmodified roll of 6 to hit when shooting, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks." | add_hit attacks 1 on raw 6, shooting | Ratmen Clans (Predator Shooter Aura) |
| Melee Slayer | "When this model charges, its weapons get AP(+2) if most models in the target have Tough(3) or higher." | Slayer's charge row | Blood Prime Brothers |
| Ranged Slayer | "When this model shoots at enemies over 9" away, its weapons get AP(+2) if most models in the target have Tough(3) or higher." | Slayer's shooting row | DAO Union (Ranged Slayer Aura) |
| Point-Blank Piercing | "This model gets AP(+1) when shooting enemies within 12"." | grant AP 1, shooting, range ≤ 12 (Piercing Hunter) | Blessed Sisters (Point-Blank Piercing Aura) |

Predator Shooter, Ranged Slayer and Point-Blank Piercing are printed only through their Aura wrappers; their game is
grouped under the book that prints the wrapper. Sergeant follows the house reading of *"this model"* (aggregation `all`,
the scenario gives it to every model of the unit). The suite games read the *"within 12""* rules only on attacks made
within 12" (`Within`); the content test pins the range edge.

- [ ] **Step 1: Write the failing content test**

Create `tests/Tabletop.GameServer.Tests/Batch1HitAndApTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1HitAndApTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Attack(string rule, AttackKind kind, double range, string? action = null, int tough = 3)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule)], Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 3, [P("Tough", tough)]);
        var s = State([a], [b]);
        return LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), Party.Whole(s, b.Id), kind, action, range, []));
    }

    static int Hit(LiveSet l) => l.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
    static int OnSix(LiveSet l, TargetQuantity quantity) => l.Effects(Side.Attacker, EffectKind.AddHit, new DieContext("hit", 6, 6, true))
        .Where(x => (x.Effect.TargetQuantity ?? TargetQuantity.Hits) == quantity).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Good_shot_is_plus_one_shooting_and_precise_plus_one_attacking()
    {
        Assert.Equal((1, 0), (Hit(Attack("Good Shot", AttackKind.Shooting, 20)), Hit(Attack("Good Shot", AttackKind.Melee, 1))));
        Assert.Equal((1, 1), (Hit(Attack("Precise", AttackKind.Shooting, 20)), Hit(Attack("Precise", AttackKind.Melee, 1))));
    }

    [Fact]
    public void Point_blank_surge_adds_a_hit_on_a_six_within_twelve_inches_and_sergeant_on_any_attack()
    {
        Assert.Equal((1, 0), (OnSix(Attack("Point-Blank Surge", AttackKind.Shooting, 12), TargetQuantity.Hits), OnSix(Attack("Point-Blank Surge", AttackKind.Shooting, 12.5), TargetQuantity.Hits)));
        Assert.Equal((1, 1), (OnSix(Attack("Sergeant", AttackKind.Shooting, 20), TargetQuantity.Hits), OnSix(Attack("Sergeant", AttackKind.Melee, 1), TargetQuantity.Hits)));
    }

    [Fact]
    public void Predator_rules_add_an_attack_on_a_six_in_their_attack_kind()
    {
        Assert.Equal((1, 0), (OnSix(Attack("Predator Fighter", AttackKind.Melee, 1), TargetQuantity.Attacks), OnSix(Attack("Predator Fighter", AttackKind.Shooting, 20), TargetQuantity.Attacks)));
        Assert.Equal((1, 0), (OnSix(Attack("Predator Shooter", AttackKind.Shooting, 20), TargetQuantity.Attacks), OnSix(Attack("Predator Shooter", AttackKind.Melee, 1), TargetQuantity.Attacks)));
    }

    [Fact]
    public void Slayers_and_point_blank_piercing_grant_ap_on_their_occasions_only()
    {
        Assert.Equal(2, Attack("Melee Slayer", AttackKind.Melee, 1, GfNames.Charge).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack("Melee Slayer", AttackKind.Melee, 1, GfNames.Charge, tough: 1).Parameter(Side.Attacker, "AP"));
        Assert.Equal(2, Attack("Ranged Slayer", AttackKind.Shooting, 9.5).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack("Ranged Slayer", AttackKind.Shooting, 9).Parameter(Side.Attacker, "AP"));
        Assert.Equal(1, Attack("Point-Blank Piercing", AttackKind.Shooting, 12).Parameter(Side.Attacker, "AP"));
        Assert.Null(Attack("Point-Blank Piercing", AttackKind.Shooting, 12.5).Parameter(Side.Attacker, "AP"));
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1HitAndApTests"`
Expected: FAIL in every test: the names are text-only, so nothing applies (`Assert.Equal() Failure`).

- [ ] **Step 3: The entries**

In `server/game/Content/grimdark-future-army-rules.part.json`, add in name order:

```json
    {"name": "Good Shot", "kind": "model", "aggregation": "all", "ruleText": "This model gets +1 to hit rolls when shooting.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Melee Slayer", "kind": "model", "aggregation": "all", "ruleText": "When this model charges, its weapons get AP(+2) if most models in the target have Tough(3) or higher.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 2, "conditions": [{"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "most_models", "subject": "opponent", "comparison": "gte", "number": 3, "values": ["Tough"]}]}]},
    {"name": "Point-Blank Piercing", "kind": "model", "aggregation": "all", "ruleText": "This model gets AP(+1) when shooting enemies within 12\".", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "lte", "number": 12}]}]},
    {"name": "Point-Blank Surge", "kind": "model", "aggregation": "all", "ruleText": "When this model shoots at enemies within 12\", unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "lte", "number": 12}, {"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Precise", "kind": "model", "aggregation": "all", "ruleText": "Gets +1 to hit when attacking.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee", "shooting"]}]}]},
    {"name": "Predator Fighter", "kind": "model", "aggregation": "all", "ruleText": "For each unmodified roll of 6 to hit in melee, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks.", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "attacks", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Predator Shooter", "kind": "model", "aggregation": "all", "ruleText": "For each unmodified roll of 6 to hit when shooting, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks.", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "attacks", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Ranged Slayer", "kind": "model", "aggregation": "all", "ruleText": "When this model shoots at enemies over 9\" away, its weapons get AP(+2) if most models in the target have Tough(3) or higher.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 2, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "most_models", "subject": "opponent", "comparison": "gte", "number": 3, "values": ["Tough"]}]}]},
    {"name": "Sergeant", "kind": "model", "aggregation": "all", "ruleText": "When this model attacks, unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee", "shooting"]}, {"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
```

The apostrophe in the two Predator texts is U+2019, as Army Forge writes it (and as `Primal`'s text in this file).

- [ ] **Step 4: Regenerate and run the content test**

Run: `./scripts/army-books generate && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1HitAndApTests|FullyQualifiedName~ArmyRulesContentTests"`
Expected: PASS. The report now counts Predator Shooter Aura, Ranged Slayer Aura and Point-Blank Piercing Aura under
"Aura, template text, base in catalog" (three more generated wrappers), and Melee Slayer Aura too.

- [ ] **Step 5: The games**

Create `sim/Suite/SuiteCatalog.Army.HitAndAp.cs`:

```csharp
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: hit modifiers, extra hits and attacks on 6s, and AP grants. The point-blank games stand the armies 2" apart (a 26" table).</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) BloodPrime => ("7ex2x15bpkmy1alv", "Blood Prime Brothers");
        static IReadOnlyList<ScenarioRule> Both(string rule) => [R(1, rule), R(2, rule)];

        public static IReadOnlyList<TestGame> HitAndAp =>
        [
            Game(Dao, "dao-union-good-shot", Play.Shoots, ["Good Shot"], Both("Good Shot"), [],
                [Modifier("Good Shot", "hit", 1, "\"This model gets +1 to hit rolls when shooting.\"")]),
            Game(Hives, "alien-hives-precise", Play.Shoots, ["Precise"], Both("Precise"), [],
                [Modifier("Precise", "hit", 1, "\"Gets +1 to hit when attacking.\"")]),
            Game(Sisters, "blessed-sisters-point-blank-surge", Play.Shoots, ["Point-Blank Surge"], Both("Point-Blank Surge"), [],
                [ExtraHits("Point-Blank Surge", Within(By("Point-Blank Surge", when: When.Shooting), 12), [6],
                    "\"When this model shoots at enemies within 12\", unmodified results of 6 to hit deal 1 extra hit\"")], length: 26),
            Game(ChangeDaemons, "wormhole-daemons-of-change-sergeant", Play.Shoots, ["Sergeant"], Both("Sergeant"), [],
                [ExtraHits("Sergeant", By("Sergeant"), [6], "\"When this model attacks, unmodified results of 6 to hit deal 1 extra hit\"")]),
            Game(Hives, "alien-hives-predator-fighter", Play.Charges, ["Predator Fighter"], All3("Predator Fighter"), [],
                [ExtraAttacks("Predator Fighter", By("Predator Fighter", when: When.Charging), [6], "\"For each unmodified roll of 6 to hit in melee, this model may roll +1 attack with that weapon\"")]),
            Game(Ratmen, "ratmen-clans-predator-shooter", Play.Shoots, ["Predator Shooter"], Both("Predator Shooter"), [],
                [ExtraAttacks("Predator Shooter", By("Predator Shooter", when: When.Shooting), [6], "\"For each unmodified roll of 6 to hit when shooting, this model may roll +1 attack with that weapon\"")]),
            Game(BloodPrime, "blood-prime-brothers-melee-slayer", Play.Charges, ["Melee Slayer"], All3("Melee Slayer"), [],
                [ApGrant("Melee Slayer", By("Melee Slayer", when: When.Charging), 2, "\"When this model charges, its weapons get AP(+2) if most models in the target have Tough(3) or higher\"")],
                enemy: [R(1, "Tough", 3), R(2, "Tough", 3)]),
            Game(Dao, "dao-union-ranged-slayer", Play.Shoots, ["Ranged Slayer"], Both("Ranged Slayer"), [],
                [ApGrant("Ranged Slayer", By("Ranged Slayer", when: When.Shooting), 2, "\"When this model shoots at enemies over 9\" away, its weapons get AP(+2) if most models in the target have Tough(3) or higher\"")]),
            Game(Sisters, "blessed-sisters-point-blank-piercing", Play.Shoots, ["Point-Blank Piercing"], Both("Point-Blank Piercing"), [],
                [ApGrant("Point-Blank Piercing", Within(By("Point-Blank Piercing", when: When.Shooting), 12), 1, "\"This model gets AP(+1) when shooting enemies within 12\"\"")], length: 26),
        ];
    }
}
```

In `sim/Suite/SuiteCatalog.Army.cs`, extend the list with `.. ArmyGames.HitAndAp`.

`Play.Shoots` gives the opponents Tough(3) (the volley default), which Ranged Slayer's *"most models in the target have
Tough(3)"* reads; its shots cross at least 12", over the 9".

- [ ] **Step 6: Run the games**

Run: `dotnet build sim && dotnet run --project sim -- suite --only dao-union-good-shot,alien-hives-precise,blessed-sisters-point-blank-surge,wormhole-daemons-of-change-sergeant,alien-hives-predator-fighter,ratmen-clans-predator-shooter,blood-prime-brothers-melee-slayer,dao-union-ranged-slayer,blessed-sisters-point-blank-piercing`
Expected: nine `pass` lines. A point-blank game failing `no occasion in the log` met no attack within 12" on seed 1:
give it `seed:` the first value of 2..20 for which the command passes.

- [ ] **Step 7: Run the army tests and the full suite**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite`
Expected: PASS; the suite's last line starts `PASSED:`.

- [ ] **Step 8: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.HitAndAp.cs tests/Tabletop.GameServer.Tests/Batch1HitAndApTests.cs
git commit -m "Play the army-book hit and AP rules: Good Shot, Precise, Point-Blank, Sergeant, Predator, Slayers"
```
