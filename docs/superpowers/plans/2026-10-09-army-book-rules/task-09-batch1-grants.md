# Task 9: Batch 1: weapon rules in melee or when shooting

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (ten entries)
- Create: `sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs` (`NoCover`, `NoPenalty`, `NoRoll`, `EnemyRuins`)
- Create: `sim/Suite/SuiteCatalog.Army.Grants.cs` (`ArmyGames.Grants`)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (the `Army()` list)
- Test: `tests/Tabletop.GameServer.Tests/Batch1GrantsTests.cs` (new); the suite games

**Interfaces:**
- Consumes: `ArmyGames.Game(... carriers, gunRange, length, more)`, `Exact`, `By`, `With`, `Of`, `Rerolls`,
  `PerDieAp`, `ExtraWounds`, `ExtraHits`, `R`, `Fix` (Tasks 8 and existing `SuiteCatalog.Army*.cs`).
- Produces: entries Bane in Melee, Rending in Melee, Shred in Melee, Shred when Shooting, Surge when Shooting,
  Unstoppable in Melee, Ignores Cover, Ignores Cover when Shooting, Ignores Regeneration, Ignores Regeneration in Melee;
  the check helpers `NoCover(entry, occasion, quote)`, `NoPenalty(entry, occasion, quote)`, `NoRoll(entry, occasion,
  roll, quote)`, the board `EnemyRuins`; `ArmyGames.Grants`.

Army Forge texts (`specialRules[].description`), and the grammar each takes. A *"This model gets X in melee / when
shooting"* rule is `Bane when Shooting`'s shape: a subject-self `grant_property` of X under an `attack_kind` condition.

| Entry | Text | Rows | Book |
| --- | --- | --- | --- |
| Bane in Melee | "This model gets Bane in melee." | grant Bane, melee | Alien Hives |
| Rending in Melee | "This model gets Rending in melee." | grant Rending, melee | Robot Legions |
| Shred in Melee | "This model gets Shred in melee." | grant Shred, melee | Blessed Sisters |
| Shred when Shooting | "This model gets Shred when shooting." | grant Shred, shooting | Custodian Brothers |
| Surge when Shooting | "This model gets Surge when shooting." | grant Surge, shooting | Dwarf Guilds |
| Unstoppable in Melee | "This model gets Unstoppable in melee." | grant Unstoppable, melee | Custodian Brothers |
| Ignores Cover (weapon) | "This weapon ignores cover." | `suppress_modifiers cover` (Blast's row) | Lust Disciples |
| Ignores Cover when Shooting | "This model gets Ignores Cover when shooting." | grant Ignores Cover, shooting | DAO Union |
| Ignores Regeneration (weapon) | "This weapon ignores Regeneration." | opponent `suppress_property Regeneration` (Bane's row) | Wormhole Daemons of Change |
| Ignores Regeneration in Melee | "This model gets Ignores Regeneration in melee." | grant Ignores Regeneration, melee | Jackals |

- [ ] **Step 1: Write the failing content test**

Create `tests/Tabletop.GameServer.Tests/Batch1GrantsTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1GrantsTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Attack(string rule, AttackKind kind, PropertyInstance[]? weaponRules = null, PropertyInstance[]? defender = null)
    {
        var a = Unit("A", "4+", "4+", [], 1, weaponRules is null ? [P(rule)] : [], Gun("Gun", "24\"", 1, weaponRules ?? []));
        var b = Unit("B", "4+", "4+", [], 1, defender ?? []);
        var s = State([a], [b]);
        var weapons = s.Projection.Models.Values.Where(m => m.UnitId == a.Id).SelectMany(m => m.Slot.Weapons).Select(w => w.Id).ToList();
        return LiveProperties.Settle(new Interaction(s, Core, Party.Of(s, a.Id, Lookup.AliveModels(s, a.Id), weapons), Party.Whole(s, b.Id), kind, null, 12, []));
    }

    [Theory]
    [InlineData("Bane in Melee", "Bane", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Rending in Melee", "Rending", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Shred in Melee", "Shred", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Shred when Shooting", "Shred", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Surge when Shooting", "Surge", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Unstoppable in Melee", "Unstoppable", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Ignores Cover when Shooting", "Ignores Cover", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Ignores Regeneration in Melee", "Ignores Regeneration", AttackKind.Melee, AttackKind.Shooting)]
    public void A_model_rule_grants_its_weapon_rule_in_its_attack_kind_only(string rule, string granted, AttackKind yes, AttackKind no)
    {
        Assert.True(Attack(rule, yes).Has(Side.Attacker, granted));
        Assert.False(Attack(rule, no).Has(Side.Attacker, granted));
    }

    [Fact]
    public void Ignores_cover_suppresses_cover_and_ignores_regeneration_removes_the_targets_regeneration()
    {
        Assert.Contains(TargetModifier.Cover, Attack("", AttackKind.Shooting, [P("Ignores Cover")]).SuppressedModifiers);
        Assert.False(Attack("", AttackKind.Shooting, [P("Ignores Regeneration")], [P("Regeneration")]).Has(Side.Defender, "Regeneration"));
        Assert.Equal(CatalogKind.Weapon, Core.Entry("Ignores Cover")!.Kind);
        Assert.Equal(CatalogKind.Weapon, Core.Entry("Ignores Regeneration")!.Kind);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1GrantsTests"`
Expected: FAIL: every `Assert.True` fails (the generated part holds these names as `played: false`, so `Gather` skips them).

- [ ] **Step 3: The entries**

In `server/game/Content/grimdark-future-army-rules.part.json`, add these lines among the entries in name order:

```json
    {"name": "Bane in Melee", "kind": "model", "aggregation": "all", "ruleText": "This model gets Bane in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Bane", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Ignores Cover", "kind": "weapon", "ruleText": "This weapon ignores cover.", "effects": [{"subject": "self", "ordinal": 10, "kind": "suppress_modifiers", "targetModifier": "cover"}]},
    {"name": "Ignores Cover when Shooting", "kind": "model", "aggregation": "all", "ruleText": "This model gets Ignores Cover when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Ignores Cover", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Ignores Regeneration", "kind": "weapon", "ruleText": "This weapon ignores Regeneration.", "effects": [{"subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration"}]},
    {"name": "Ignores Regeneration in Melee", "kind": "model", "aggregation": "all", "ruleText": "This model gets Ignores Regeneration in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Ignores Regeneration", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Rending in Melee", "kind": "model", "aggregation": "all", "ruleText": "This model gets Rending in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Rending", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Shred in Melee", "kind": "model", "aggregation": "all", "ruleText": "This model gets Shred in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Shred", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Shred when Shooting", "kind": "model", "aggregation": "all", "ruleText": "This model gets Shred when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Shred", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Surge when Shooting", "kind": "model", "aggregation": "all", "ruleText": "This model gets Surge when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Surge", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Unstoppable in Melee", "kind": "model", "aggregation": "all", "ruleText": "This model gets Unstoppable in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Unstoppable", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
```

- [ ] **Step 4: Regenerate and run the content test**

Run: `./scripts/army-books generate && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1GrantsTests|FullyQualifiedName~ArmyRulesContentTests"`
Expected: PASS. The report's "Standalone, own text" count falls by ten: the ten names are now hand-authored and skipped.

- [ ] **Step 5: The check helpers**

Create `sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs`:

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1's checks beyond the army checks: what a rule takes away (cover, a penalty, a roll), read from the logged rolls.</summary>
    private static partial class ArmyGames
    {
        /// <summary>p.11 cover is "+1 to Defense rolls when blocking hits from shooting attacks": no defense roll of the occasion's attacks carries it.</summary>
        static GameCheck NoCover(string entry, Occasion occasion, string quote) =>
            Exact(entry, $"{quote}: no cover on defense", (run, log) =>
                from a in Of(run, log, occasion)
                from s in a.Blocks
                from r in s.Rolls("defense")
                select r.Modifiers.Any(m => m.Source == "cover") ? "a defense roll with cover +1" : null);

        /// <summary>"Ignores all negative modifiers to this weapon": no hit roll of the occasion's attacks carries a negative modifier (a fatigued
        /// unit's roll, p.9 "only hit on unmodified rolls of 6", carries none and is left out).</summary>
        static GameCheck NoPenalty(string entry, Occasion occasion, string quote) =>
            Exact(entry, $"{quote}: no negative modifier to hit", (run, log) =>
                from a in Of(run, log, occasion)
                where a.Hit is not null
                from r in a.Hit!.Rolls("hit")
                where !(r.Threshold == 6 && r.Modifiers.Count == 0)
                select r.Modifiers.Where(m => m.Amount < 0).Select(m => $"{m.Source} {m.Amount}").ToList() is { Count: > 0 } bad ? $"a hit roll under {string.Join(", ", bad)}" : null);

        /// <summary>A rule that ignores a roll-to-ignore rule: under every occasion attack whose block failed a die, no step rolls that rule's dice.</summary>
        static GameCheck NoRoll(string entry, Occasion occasion, string roll, string quote) =>
            Exact(entry, $"{quote}: no {roll} roll", (run, log) =>
                from a in Of(run, log, occasion)
                where a.Blocks.Any(b => b.Rolls("defense").Any(r => b.Now(r).Any(d => !d.Success)))
                let rolled = log.Steps.Count(s => s.Ancestors.Any(f => f.FrameId == a.Frame.FrameId) && s.Rolls(roll).Any())
                select rolled == 0 ? null : $"{rolled} steps rolled {roll}");

        /// <summary>Low ruins (a cover area, p.11) over the opponents' whole zone, so their units are in cover when shot.</summary>
        static ScenarioBuilder EnemyRuins(ScenarioBuilder b) => b
            .Terrain("ruin", new Point3(18, 6, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(34, 12), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Cover, null)]);
    }
}
```

- [ ] **Step 6: The games**

Create `sim/Suite/SuiteCatalog.Army.Grants.cs`:

```csharp
namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: "This model gets X in melee / when shooting" and the two weapon rules Ignores Cover and Ignores Regeneration.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) ChangeDaemons => ("04z57ua0bwth37zh", "Wormhole Daemons of Change");
        static IReadOnlyList<ScenarioRule> All3(string rule) => [R(1, rule), R(2, rule), R(3, rule)];
        static IReadOnlyList<ScenarioRule> Each(int units, params string[] rules) => [.. Enumerable.Range(1, units).SelectMany(u => rules.Select(r => R(u, r)))];

        public static IReadOnlyList<TestGame> Grants =>
        [
            Game(Hives, "alien-hives-bane-in-melee", Play.Charges, ["Bane in Melee"], All3("Bane in Melee"), [],
                [Rerolls("Bane in Melee", By("Bane in Melee", when: When.Charging), [6], successOnly: false, "\"This model gets Bane in melee\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")]),
            Game(Robots, "robot-legions-rending-in-melee", Play.Charges, ["Rending in Melee"], All3("Rending in Melee"), [],
                [PerDieAp("Rending in Melee", By("Rending in Melee", when: When.Charging), 4, "\"This model gets Rending in melee\": p.14 Rending, \"on unmodified results of 6 to hit, those hits get AP(+4)\"")]),
            Game(Sisters, "blessed-sisters-shred-in-melee", Play.Charges, ["Shred in Melee"], All3("Shred in Melee"), [],
                [ExtraWounds("Shred in Melee", By("Shred in Melee", when: When.Charging), [1], "\"This model gets Shred in melee\": Shred, \"On unmodified results of 1 to block hits, this weapon deals 1 extra wound\"")]),
            Game(Custodians, "custodian-brothers-shred-when-shooting", Play.Shoots, ["Shred when Shooting"], [R(1, "Shred when Shooting"), R(2, "Shred when Shooting")], [],
                [ExtraWounds("Shred when Shooting", By("Shred when Shooting", when: When.Shooting), [1], "\"This model gets Shred when shooting\": Shred, \"On unmodified results of 1 to block hits, this weapon deals 1 extra wound\"")]),
            Game(Dwarves, "dwarf-guilds-surge-when-shooting", Play.Shoots, ["Surge when Shooting"], [R(1, "Surge when Shooting"), R(2, "Surge when Shooting")], [],
                [ExtraHits("Surge when Shooting", By("Surge when Shooting", when: When.Shooting), [6], "\"This model gets Surge when shooting\": p.14 Surge, \"On unmodified results of 6 to hit, this weapon deals 1 extra hit\"")]),
            // Melee Evasion (-1 to hit in melee) on the opponents is the negative modifier Unstoppable ignores.
            Game(Custodians, "custodian-brothers-unstoppable-in-melee", Play.Charges, ["Unstoppable in Melee"], All3("Unstoppable in Melee"), [],
                [NoPenalty("Unstoppable in Melee", By("Unstoppable in Melee", when: When.Charging), "\"This model gets Unstoppable in melee\": p.15 Unstoppable, \"ignores all negative modifiers to this weapon\"")],
                enemy: Each(2, "Melee Evasion")),
            Game(Lust, "lust-disciples-ignores-cover", Play.Shoots, ["Ignores Cover"], [], [],
                [NoCover("Ignores Cover", With("Ignores Cover"), "\"This weapon ignores cover.\"")], weapon: [R(0, "Ignores Cover")], units: 2, board: EnemyRuins),
            Game(Dao, "dao-union-ignores-cover-when-shooting", Play.Shoots, ["Ignores Cover when Shooting"], [R(1, "Ignores Cover when Shooting"), R(2, "Ignores Cover when Shooting")], [],
                [NoCover("Ignores Cover when Shooting", By("Ignores Cover when Shooting", when: When.Shooting), "\"This model gets Ignores Cover when shooting.\"")], board: EnemyRuins),
            // The opponents carry Regeneration ("roll one die for each. On a 5+ it is ignored"), which Ignores Regeneration leaves unrolled.
            Game(ChangeDaemons, "wormhole-daemons-of-change-ignores-regeneration", Play.Shoots, ["Ignores Regeneration"], [], [Fix("defense", 1, 1, 1)],
                [NoRoll("Ignores Regeneration", With("Ignores Regeneration"), "Regeneration", "\"This weapon ignores Regeneration.\"")],
                weapon: [R(0, "Ignores Regeneration")], units: 2, enemy: [R(1, "Tough", 3), R(2, "Tough", 3), R(1, "Regeneration"), R(2, "Regeneration")]),
            Game(Jackals, "jackals-ignores-regeneration-in-melee", Play.Charges, ["Ignores Regeneration in Melee"], All3("Ignores Regeneration in Melee"), [Fix("defense", 1, 1, 1)],
                [NoRoll("Ignores Regeneration in Melee", By("Ignores Regeneration in Melee", when: When.Charging), "Regeneration", "\"This model gets Ignores Regeneration in melee.\"")],
                enemy: Each(2, "Regeneration")),
        ];
    }
}
```

In `sim/Suite/SuiteCatalog.Army.cs`, extend the list: `private static partial IEnumerable<TestGame> Army() => [.. ArmyGames.All, .. ArmyGames.Patterns, .. ArmyGames.Grants];`

Add `using Tabletop.Sim.Scenarios;` at the top of `SuiteCatalog.Army.Grants.cs` (for `ScenarioRule`).

- [ ] **Step 7: Run the games**

Run: `dotnet build sim && dotnet run --project sim -- suite --only alien-hives-bane-in-melee,robot-legions-rending-in-melee,blessed-sisters-shred-in-melee,custodian-brothers-shred-when-shooting,dwarf-guilds-surge-when-shooting,custodian-brothers-unstoppable-in-melee,lust-disciples-ignores-cover,dao-union-ignores-cover-when-shooting,wormhole-daemons-of-change-ignores-regeneration,jackals-ignores-regeneration-in-melee`
Expected: ten `pass` lines.

- [ ] **Step 8: Run the sim tests and the full suite**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite`
Expected: PASS; the suite's last line starts `PASSED:` and its not-automated count is ten lower than Task 8's.

- [ ] **Step 9: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs sim/Suite/SuiteCatalog.Army.Grants.cs \
  tests/Tabletop.GameServer.Tests/Batch1GrantsTests.cs
git commit -m "Play the army-book weapon rules granted in melee or when shooting, and Ignores Cover and Ignores Regeneration"
```
