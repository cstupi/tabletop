# Task 11: Batch 1: defense rules

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (five entries)
- Create: `sim/Suite/SuiteCatalog.Army.Defense.cs` (`ArmyGames.Defense`)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (the `Army()` list)
- Test: `tests/Tabletop.GameServer.Tests/Batch1DefenseTests.cs` (new); the suite games

**Interfaces:**
- Consumes: `Game`, `Carried`, `Modifier`, `ApGrant`, `IgnoreWounds`, `On`, `Ruins`, `IgnoreOn6`, `Fix`, `R`
  (existing `SuiteCatalog.Army*.cs`), `Both` (Task 10).
- Produces: entries Fortified, Grounded Stealth, Screened, Guarded, Protected; `ArmyGames.Defense`.

| Entry | Army Forge text | Rows (the shape it copies) | Book |
| --- | --- | --- | --- |
| Fortified | "When units where all models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0)." | opponent `modify_parameter AP -1` (Guardian, unconditional) | Alien Hives |
| Grounded Stealth | "If a unit where all models have this rule has most of them within 1" of terrain, enemies get -1 to hit rolls when attacking them." | opponent hit -1, `position_terrain near`, melee or shooting (Grounded Reinforcement) | Machine Cults |
| Screened | "When units where all models have this rule are shot or charged from over 9" away, enemy units get -1 to hit rolls." | Machine-Fog's two rows | Wormhole Daemons of Plague (Screened Aura) |
| Guarded | "When units where all models have this rule are shot or charged from over 9" away, they get +1 to defense rolls." | Sturdy's two rows | Blessed Sisters (Guarded Buff) |
| Protected | "When a unit where all models have this rule takes wounds, roll one die for each. On a 6+ it is ignored." | a 6+ roll whose child overrides the wound (Self-Repair) | Human Inquisition |

*"To a min. of AP(0)"*: `modify_parameter` lowers the attacker's live AP instance; an AP(1) weapon reads AP(0), the
amount the Guardian game already checks (`ApGrant(..., 0, ...)`).

- [ ] **Step 1: Write the failing content test**

Create `tests/Tabletop.GameServer.Tests/Batch1DefenseTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1DefenseTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Shot(string rule, double range, AttackKind kind = AttackKind.Shooting, string? action = null, IReadOnlyList<string>? terrain = null)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Gun", "24\"", 1, P("AP", 1)));
        var b = Unit("B", "4+", "4+", [], 1, [P(rule)]);
        var s = State([a], [b]);
        var weapons = s.Projection.Models.Values.Where(m => m.UnitId == a.Id).SelectMany(m => m.Slot.Weapons).Select(w => w.Id).ToList();
        return LiveProperties.Settle(new Interaction(s, Core, Party.Of(s, a.Id, Lookup.AliveModels(s, a.Id), weapons), Party.Whole(s, b.Id), kind, action, range, terrain ?? []));
    }

    static int Mod(LiveSet l, Side side, string roll) => l.Effects(side, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Fortified_takes_one_from_the_attackers_ap()
    {
        Assert.Equal(0, Shot("Fortified", 20).Parameter(Side.Attacker, "AP"));
    }

    [Fact]
    public void Grounded_stealth_is_minus_one_to_hit_near_terrain_only()
    {
        Assert.Equal(-1, Mod(Shot("Grounded Stealth", 20, terrain: ["near"]), Side.Attacker, "hit"));
        Assert.Equal(0, Mod(Shot("Grounded Stealth", 20), Side.Attacker, "hit"));
    }

    [Fact]
    public void Screened_and_guarded_act_on_shots_and_charges_from_over_nine_inches()
    {
        Assert.Equal((-1, 0), (Mod(Shot("Screened", 9.5), Side.Attacker, "hit"), Mod(Shot("Screened", 9), Side.Attacker, "hit")));
        Assert.Equal(-1, Mod(Shot("Screened", 10, AttackKind.Melee, GfNames.Charge), Side.Attacker, "hit"));
        Assert.Equal((1, 0), (Mod(Shot("Guarded", 9.5), Side.Defender, "defense"), Mod(Shot("Guarded", 9), Side.Defender, "defense")));
    }

    [Fact]
    public void Protected_rolls_a_six_plus_to_ignore_each_wound()
    {
        var roll = Assert.Single(RollEffects.OwnRolls(Shot("Protected", 20), Side.Defender, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound }));
        Assert.Equal(("Protected", 6), (roll.Source.Entry.Name, roll.Roll.Threshold));
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1DefenseTests"`
Expected: FAIL in every test (text-only entries apply nothing; `Assert.Single` finds no roll).

- [ ] **Step 3: The entries**

In `server/game/Content/grimdark-future-army-rules.part.json`, add in name order:

```json
    {"name": "Fortified", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0).", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_parameter", "targetEntry": "AP", "amount": -1}]},
    {"name": "Grounded Stealth", "kind": "model", "aggregation": "all", "ruleText": "If a unit where all models have this rule has most of them within 1\" of terrain, enemies get -1 to hit rolls when attacking them.", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "position_terrain", "subject": "self", "comparison": "eq", "values": ["near"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee", "shooting"]}]}]},
    {"name": "Guarded", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule are shot or charged from over 9\" away, they get +1 to defense rolls.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": 1, "conditions": [{"kind": "action_taken", "subject": "opponent", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
    {"name": "Protected", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 6+ it is ignored.", "effects": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 6, "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}]}]},
    {"name": "Screened", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule are shot or charged from over 9\" away, enemy units get -1 to hit rolls.", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}, {"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "action_taken", "subject": "opponent", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
```

- [ ] **Step 4: Regenerate and run the content test**

Run: `./scripts/army-books generate && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1DefenseTests|FullyQualifiedName~ArmyRulesContentTests"`
Expected: PASS. Fortified Aura, Screened Aura, Protected Aura and Guarded Buff are now generated wrappers.

- [ ] **Step 5: The games**

Create `sim/Suite/SuiteCatalog.Army.Defense.cs`:

```csharp
namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: rules of the unit being attacked. Every game is Shot: the opponents shoot from at least 12" away.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) PlagueDaemons => ("fmuqw5lr5l6dq0mq", "Wormhole Daemons of Plague");
        static (string Uid, string Army) Inquisition => ("jpc0kyil0juwy602", "Human Inquisition");

        public static IReadOnlyList<TestGame> Defense =>
        [
            Game(Hives, "alien-hives-fortified", Play.Shot, ["Fortified"], [R(1, "Fortified")], [Fix("hit", 6, 6, 6)],
                [ApGrant("Fortified", On("Fortified", when: When.Shooting), 0, "\"those hits count as having AP(-1), to a min. of AP(0)\" (against AP(1))")],
                enemyWeapon: [R(0, "AP", 1)]),
            // Ruins cover the rule side's zone, so most of its models stand within 1" of terrain.
            Game(MachineCults, "machine-cults-grounded-stealth", Play.Shot, ["Grounded Stealth"], [R(1, "Grounded Stealth")], [],
                [Modifier("Grounded Stealth", "hit", -1, "\"enemies get -1 to hit rolls when attacking them\"")], board: Ruins),
            Game(PlagueDaemons, "wormhole-daemons-of-plague-screened", Play.Shot, ["Screened"], Both("Screened"), [],
                [Modifier("Screened", "hit", -1, "\"When units where all models have this rule are shot or charged from over 9\" away, enemy units get -1 to hit rolls\"")]),
            Game(Sisters, "blessed-sisters-guarded", Play.Shot, ["Guarded"], Both("Guarded"), [Fix("hit", 6, 6, 6)],
                [Modifier("Guarded", "defense", 1, "\"When units where all models have this rule are shot or charged from over 9\" away, they get +1 to defense rolls\"")]),
            Carried(Inquisition, "human-inquisition-protected", "Protected", [R(1, "Protected")], IgnoreWounds("Protected", 6, IgnoreOn6)),
        ];
    }
}
```

In `sim/Suite/SuiteCatalog.Army.cs`, extend the list with `.. ArmyGames.Defense`.

- [ ] **Step 6: Run the games, the army tests and the full suite**

Run: `dotnet build sim && dotnet run --project sim -- suite --only alien-hives-fortified,machine-cults-grounded-stealth,wormhole-daemons-of-plague-screened,blessed-sisters-guarded,human-inquisition-protected`
Expected: five `pass` lines.

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite`
Expected: PASS; the suite's last line starts `PASSED:`.

- [ ] **Step 7: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.Defense.cs tests/Tabletop.GameServer.Tests/Batch1DefenseTests.cs
git commit -m "Play the army-book defense rules: Fortified, Grounded Stealth, Screened, Guarded, Protected"
```
