# Task 14b: Batch 1: the unbatched rules the grammar expresses, Banner and Musician

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (nine entries)
- Create: `sim/Suite/SuiteCatalog.Army.Unbatched.cs` (`ArmyGames.Unbatched`)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (the `Army()` list)
- Test: `tests/Tabletop.GameServer.Tests/Batch1UnbatchedTests.cs` (new); the suite games

**Interfaces:**
- Consumes: `Game`, `Carried`, `Morale`, `Modifier`, `ExtraHits`, `ExtraAttacks`, `IgnoreWounds`, `Unpredictable`,
  `RapidBlink`, `Allowance`, `By`, `When`, `IgnoreOn6`, `Change`, `Hives` (existing `SuiteCatalog.Army*.cs`); `Both`
  (Task 10), `Each` (Task 9), `All3` (Task 9), `ChangeDaemons` (Task 9), `Inquisition` (Task 11).
- Produces: entries Bad Shot, Bloodborn, Brutal Fighter, Knightborn, Unpredictable, Wolfborn, Unique, Banner, Musician;
  `ArmyGames.Unbatched`.

The spec's batches name none of these. Seven are standalone names whose Army Forge texts the current grammar
expresses; Banner and Musician are the books' command-group upgrades, whose texts the books' dump lacks and Army Forge's
common-rules endpoint holds: `GET https://army-forge.onepagerules.com/api/rules/common/2` (game system 2, Grimdark
Future) returns `{"rules": [...], "traits": [...]}`, and its `Banner` (`id` `ywXKQKPOLjTF`) and `Musician` (`id`
`qrG_DXJez5Dg`) carry the same ids as the books' upgrade gains. The other ten unbatched names go to later batches
(README → *Moved to a later batch*).

| Entry | Army Forge text | Rows (the shape it copies) | Book |
| --- | --- | --- | --- |
| Bad Shot | "This model gets -1 to hit rolls when shooting." | hit -1, shooting | Orc Marauders |
| Bloodborn | "For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks." | Primal's row | Blood Brothers |
| Brutal Fighter | "When in melee, unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules)." | add_hit 1 on raw 6, melee | Human Inquisition |
| Knightborn | "When a unit where all models have this rule takes wounds, roll one die for each. On a 6+ it is ignored. If the wounds were from a spell, then they are ignored on a 4+ instead." | Resistance's rows, 6+ and 4+ | Knight Brothers |
| Unpredictable | "When attacking, roll one die and apply one effect to all models with this rule: on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead." | Unpredictable Fighter's roll, melee or shooting | Change Disciples |
| Wolfborn | "When this unit is activated, you may place all models with this rule in it anywhere fully within D3" of their position." | Rapid Blink's row (the same text) | Wolf Brothers |
| Unique | "This unit may only be taken once per army." | none | Alien Hives |
| Banner | "This model and its unit get +1 to morale test rolls." | morale +1, aggregation any (Courage Aura's) | Wormhole Daemons of Change |
| Musician | "This model and its unit moves +1" when using move actions." | Advance, Rush, Charge +1, aggregation any | Wormhole Daemons of Change |

**Unique** limits how an army is built. The engine judges no list (army import and setup take whatever army a player
brings, as they take its points), and in play the rule asks nothing. It is played by doing nothing: a `model` entry (the
kind its text-only entry had) with no effects and `Played` true, so the stat sheet shows its text untagged. Its game's
`Evidence` says so: a game with a Unique unit that plays to its end is all the rule has to show.

*"Move actions"* are p.7's actions that move: Advance, Rush and Charge (Hold does not move).

- [ ] **Step 1: Write the failing content test**

Create `tests/Tabletop.GameServer.Tests/Batch1UnbatchedTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1UnbatchedTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Attack(string rule, AttackKind? kind, string? point = null)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule)], Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 1, [P(rule)]);
        var s = State([a], [b]);
        return LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), kind is null ? null : Party.Whole(s, b.Id), kind, null, 12, [], point));
    }

    static int Hit(LiveSet l) => l.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
    static int OnSix(LiveSet l, TargetQuantity q) => l.Effects(Side.Attacker, EffectKind.AddHit, new DieContext("hit", 6, 6, true))
        .Where(x => (x.Effect.TargetQuantity ?? TargetQuantity.Hits) == q).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Bad_shot_bloodborn_and_brutal_fighter_act_on_their_attacks()
    {
        Assert.Equal((-1, 0), (Hit(Attack("Bad Shot", AttackKind.Shooting)), Hit(Attack("Bad Shot", AttackKind.Melee))));
        Assert.Equal((1, 1), (OnSix(Attack("Bloodborn", AttackKind.Shooting), TargetQuantity.Attacks), OnSix(Attack("Bloodborn", AttackKind.Melee), TargetQuantity.Attacks)));
        Assert.Equal((1, 0), (OnSix(Attack("Brutal Fighter", AttackKind.Melee), TargetQuantity.Hits), OnSix(Attack("Brutal Fighter", AttackKind.Shooting), TargetQuantity.Hits)));
    }

    [Fact]
    public void Knightborn_ignores_wounds_on_six_and_on_four_from_a_spell()
    {
        IReadOnlyList<int?> Thresholds(AttackKind kind) => RollEffects.OwnRolls(Attack("Knightborn", kind), Side.Defender,
            c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound }).Select(x => x.Roll.Threshold).ToList();
        Assert.Equal(new int?[] { 6 }, Thresholds(AttackKind.Shooting));
        Assert.Equal(new int?[] { 4 }, Thresholds(AttackKind.Spell));
    }

    [Fact]
    public void Unpredictable_rolls_at_the_start_of_any_attack_and_wolfborn_places_within_d3_on_activation()
    {
        var roll = Core.Entry("Unpredictable")!.Effects.Single();
        Assert.Equal(EffectKind.Roll, roll.Kind);
        Assert.Contains(roll.Conditions, c => c.Kind == ConditionKind.AtPoint && c.Values.SequenceEqual([GfNames.AttackStart]));
        Assert.Contains(roll.Conditions, c => c.Kind == ConditionKind.AttackKind && c.Values.Order().SequenceEqual(["melee", "shooting"]));
        var place = Assert.Single(Attack("Wolfborn", null, GfNames.ActivationStart).AtPoint(Side.Attacker, GfNames.ActivationStart));
        Assert.Equal((EffectKind.Place, 1, "d3"), (place.Effect.Kind, place.Effect.Amount ?? 0, place.Effect.AmountDice));
    }

    [Fact]
    public void Unique_is_played_and_does_nothing()
    {
        var unique = Core.Entry("Unique")!;
        Assert.Equal((true, 0, CatalogKind.Model), (unique.Played, unique.Effects.Count, unique.Kind));
    }

    [Fact]
    public void Banner_is_plus_one_to_morale_and_musician_plus_one_inch_on_every_move()
    {
        var banner = Attack("Banner", AttackKind.Shooting);
        Assert.Equal(1, banner.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "morale").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter)));
        var musician = Attack("Musician", null);
        Assert.Equal((7.0, 13.0, 13.0), (StatReader.ActionDistance(musician, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(musician, Side.Attacker, GfNames.Rush),
            StatReader.ActionDistance(musician, Side.Attacker, GfNames.Charge)));
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1UnbatchedTests"`
Expected: FAIL in every test (text-only entries; `Unique` reads `Played` false).

- [ ] **Step 3: The entries**

In `server/game/Content/grimdark-future-army-rules.part.json`, add in name order:

```json
    {"name": "Bad Shot", "kind": "model", "aggregation": "all", "ruleText": "This model gets -1 to hit rolls when shooting.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Banner", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get +1 to morale test rolls.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "morale", "amount": 1}]},
    {"name": "Bloodborn", "kind": "model", "aggregation": "all", "ruleText": "For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks.", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "attacks", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee", "shooting"]}, {"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Brutal Fighter", "kind": "model", "aggregation": "all", "ruleText": "When in melee, unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Knightborn", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 6+ it is ignored. If the wounds were from a spell, then they are ignored on a 4+ instead.", "effects": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 6, "conditions": [{"kind": "attack_kind", "comparison": "neq", "values": ["spell"]}], "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}]}, {"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 4, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["spell"]}], "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}]}]},
    {"name": "Musician", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit moves +1\" when using move actions.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 1}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 1}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 1}]},
    {"name": "Unique", "kind": "model", "aggregation": "all", "ruleText": "This unit may only be taken once per army."},
    {"name": "Unpredictable", "kind": "model", "aggregation": "all", "ruleText": "When attacking, roll one die and apply one effect to all models with this rule: on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead.", "effects": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 1, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["attack-start"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee", "shooting"]}], "children": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "lte", "number": 3, "values": ["Unpredictable", "raw"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "gte", "number": 4, "values": ["Unpredictable", "raw"]}]}]}]},
    {"name": "Wolfborn", "kind": "model", "aggregation": "none", "ruleText": "When this unit is activated, you may place all models with this rule in it anywhere fully within D3\" of their position.", "effects": [{"subject": "self", "ordinal": 10, "kind": "place", "amount": 1, "amountDice": "d3", "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["activation-start"]}]}]},
```

- [ ] **Step 4: Regenerate and run the content test**

Run: `./scripts/army-books generate && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1UnbatchedTests|FullyQualifiedName~ArmyRulesContentTests"`
Expected: PASS. The report's "Standalone, own text" falls by nine and the "no description" line is gone (Banner and
Musician are hand-authored and skipped).

- [ ] **Step 5: The games**

Create `sim/Suite/SuiteCatalog.Army.Unbatched.cs`:

```csharp
namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: standalone rules no spec batch named that the grammar expresses, and the command-group upgrades Banner and Musician
    /// (texts from Army Forge's common rules, GET /api/rules/common/2).</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) BloodBrothers => ("xnnqhh1775kvmz2r", "Blood Brothers");
        static (string Uid, string Army) KnightBrothers => ("w70ha3o85pa7nigq", "Knight Brothers");
        static (string Uid, string Army) WolfBrothers => ("yxjboa8oma9bbdck", "Wolf Brothers");

        public static IReadOnlyList<TestGame> Unbatched =>
        [
            Game(Orcs, "orc-marauders-bad-shot", Play.Shoots, ["Bad Shot"], Both("Bad Shot"), [],
                [Modifier("Bad Shot", "hit", -1, "\"This model gets -1 to hit rolls when shooting.\"")]),
            Game(BloodBrothers, "blood-brothers-bloodborn", Play.Shoots, ["Bloodborn"], Both("Bloodborn"), [],
                [ExtraAttacks("Bloodborn", By("Bloodborn"), [6], "\"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon\"")]),
            Game(Inquisition, "human-inquisition-brutal-fighter", Play.Charges, ["Brutal Fighter"], All3("Brutal Fighter"), [],
                [ExtraHits("Brutal Fighter", By("Brutal Fighter", when: When.Charging), [6], "\"When in melee, unmodified results of 6 to hit deal 1 extra hit\"")]),
            Carried(KnightBrothers, "knight-brothers-knightborn", "Knightborn", [R(1, "Knightborn")], IgnoreWounds("Knightborn", 6, IgnoreOn6)),
            Game(Change, "change-disciples-unpredictable", Play.Shoots, ["Unpredictable"], Both("Unpredictable"), [], [Unpredictable("Unpredictable")]),
            Game(WolfBrothers, "wolf-brothers-wolfborn", Play.Shoots, ["Wolfborn"], Both("Wolfborn"), [], [RapidBlink("Wolfborn", 1)]),
            // "May only be taken once per army" is a list-building rule: the engine judges no list, and in play the rule asks nothing. A game
            // with a Unique unit that plays to its end (Checks.Ended) is all it has to show.
            Game(Hives, "alien-hives-unique", Play.Shoots, ["Unique"], [R(1, "Unique")], [], []) with
            {
                Evidence = new Dictionary<string, Func<IReadOnlyList<Tabletop.Protocol.GameEvent>, bool>> { ["Unique"] = _ => true },
            },
            // p.10: the fixed hits leave the unit at half or less, so it tests morale; every model carries the Banner, so a casualty cannot take it away.
            Morale(ChangeDaemons, "wormhole-daemons-of-change-banner", "Banner", [R(1, "Banner")], 1, "\"This model and its unit get +1 to morale test rolls.\""),
            Game(ChangeDaemons, "wormhole-daemons-of-change-musician", Play.Moves, ["Musician"], Each(4, "Musician"), [],
                [Allowance("Musician", null, "\"This model and its unit moves +1\" when using move actions.\"", ("advance", 7), ("rush", 13), ("charge", 13))]),
        ];
    }
}
```

In `sim/Suite/SuiteCatalog.Army.cs`, extend the list with `.. ArmyGames.Unbatched`.

- [ ] **Step 6: Run the games, the army tests and the full suite**

Run: `dotnet build sim && dotnet run --project sim -- suite --only orc-marauders-bad-shot,blood-brothers-bloodborn,human-inquisition-brutal-fighter,knight-brothers-knightborn,change-disciples-unpredictable,wolf-brothers-wolfborn,alien-hives-unique,wormhole-daemons-of-change-banner,wormhole-daemons-of-change-musician`
Expected: nine `pass` lines.

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite`
Expected: PASS; the suite's last line starts `PASSED:`.

- [ ] **Step 7: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.Unbatched.cs tests/Tabletop.GameServer.Tests/Batch1UnbatchedTests.cs
git commit -m "Play Bad Shot, Bloodborn, Brutal Fighter, Knightborn, Unpredictable, Wolfborn, Unique, Banner and Musician"
```
