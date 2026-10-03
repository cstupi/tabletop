# Task 11: The army-rules part

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (the 73 entries)
- Modify: `server/game/Rules/GrimdarkFuture/GfMelee.cs` (*Counter-Attack* strikes first when charged; *Unwieldy* strikes last when charging)
- Modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` (`CounterAttack`, `Unwieldy`)
- Create: `tests/Tabletop.GameServer.Tests/ArmyRulesContentTests.cs`

**Interfaces:**
- Consumes: every grammar addition of Tasks 3 to 10; `TestProperties.Core` (the composed set, Task 2).
- Produces: the 73 catalog entries, by name, in the shipped set; `GfNames.CounterAttack = "Counter-Attack"`, `GfNames.Unwieldy = "Unwieldy"`; the melee sequence `[melee-start, counter, impact, charger-attacks, strike-back, unwieldy, compare, consolidate]`.

Spec → *Army-book rules spells name*. The 50 rules spells name and the 23 base rules their Boosts name, each with its book's `description` as `ruleText`. Kind and aggregation follow the core document's reading: a weapon rule is `weapon`; *"units where all models have this rule"* and *"this model"* are a model rule with aggregation `all`; *Teleport*, *Rapid Blink* and its Boost are aggregation `none` (army-rule hooks spec). Fifteen entries are names with no rows here; the [army-rule hooks](../2026-10-02-army-rule-hooks/README.md) plan gives them rows.

The JSON below was produced from the local Army Forge dump (`.superpowers/armyforge/books/`) for the rule texts; it is the file's whole content. Two readings it encodes: *Warbound Boost* and *Infected Boost*'s *"failed defense rolls of 1-2"* is an extra wound on an unmodified 2 (no predicate tests a die's success, and a 2 fails every Defense but 2+); *Changebound*'s *"charged from over 9" away"* is `action_taken Charge` with `range gt 9`, which holds in the charger's strike, whose interaction carries the action `Charge` and the charge-start range (Task 9), and not in a strike back, whose action is null.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/ArmyRulesContentTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class ArmyRulesContentTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly string[] NamesOnly = ["Hit & Run Fighter", "Hit & Run Shooter", "Harassing", "Harassing Boost", "Quick Shot", "Grounded Reinforcement", "Teleport",
        "Rapid Blink", "Rapid Blink Boost", "Unpredictable Fighter", "Unpredictable Shooter", "Primal", "Primal Boost", "Clan Warrior", "Clan Warrior Boost"];

    [Fact]
    public void The_part_holds_seventy_three_rules_and_composes_valid()
    {
        var part = PropertiesJson.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "server/game/Content/grimdark-future-army-rules.part.json")));
        Assert.Equal(73, part.Entries.Count);
        Assert.Empty(PropertiesJson.Validate(TestProperties.CoreDocument));
        Assert.All(part.Entries, e => Assert.NotNull(Core.Entry(e.Name)));
        Assert.All(NamesOnly, n => Assert.Empty(Core.Entry(n)!.Effects));
        Assert.All(part.Entries, e => Assert.Null(e.Faction));
    }

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Tabletop.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("no Tabletop.sln above the test binaries");
    }

    static LiveSet Shot(GameState s, Guid a, Guid b, double range) => LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a), Party.Whole(s, b), AttackKind.Shooting, null, range, []));
    static int HitMods(LiveSet live) => live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Changebound_gives_minus_one_over_nine_inches_and_its_boost_gives_it_always_and_never_twice()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, []);
        var plain = Unit("P", "4+", "4+", [], 1, [P("Changebound")]); var boosted = Unit("B", "4+", "4+", [], 1, [P("Changebound"), P("Changebound Boost")]);
        var s = State([shooter], [plain, boosted]);
        Assert.Equal((0, -1), (HitMods(Shot(s, shooter.Id, plain.Id, 6)), HitMods(Shot(s, shooter.Id, plain.Id, 12))));
        Assert.Equal((-1, -1), (HitMods(Shot(s, shooter.Id, boosted.Id, 6)), HitMods(Shot(s, shooter.Id, boosted.Id, 12))));
    }

    [Fact]
    public void Shred_on_the_shipped_set_adds_a_wound_for_a_defense_one()
    {
        var a = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Shred"))); var b = Unit("B", "4+", "4+", [], 5, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5, 5, 1, 6));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
    }

    [Fact]
    public void Plaguebound_boost_ignores_wounds_on_a_five()
    {
        var a = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "6+", [], 1, [P("Plaguebound"), P("Plaguebound Boost"), P("Tough", 3)]);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5, 1, 5));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(0, sim.Model(b.Models[0].Id).Wounds);                                             // hit, failed block, a 5 ignores the wound
        Assert.Equal(5, sim.Steps("assign").Single().Rolls.Single().Threshold);
    }

    [Fact]
    public void Counter_attack_strikes_first_when_charged_and_unwieldy_strikes_last_when_charging()
    {
        var charger = Unit("C", "4+", "6+", [], 1, [], Melee("Maul", 1, P("Unwieldy")), Melee("Knife", 1));
        var defender = Unit("D", "4+", "6+", [], 1, [P("Counter-Attack")], Melee("Spear", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 20).ToArray()));
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        var order = sim.Emitted.OfType<SequenceStarted>().Where(x => x.Sequence == GfNames.AttackSequence).Select(x => x.Context.ActorUnitId).ToList();
        Assert.Equal([defender.Id, charger.Id, charger.Id], order);                                     // Spear first; then Knife; Maul last
        var maul = charger.Models[0].Weapons.Single(w => w.Name == "Maul").Id;
        Assert.Equal([maul], sim.Emitted.OfType<SequenceStarted>().Last(x => x.Sequence == GfNames.AttackSequence).Context.WeaponIds);
    }
}
```

The Plaguebound test's dice are the hit (5, passes 2+), the block (1, fails 6+) and the ignore roll (5). The melee test's 2s miss every attack, which keeps the order the only thing under test; if the strike-back Ask opens for the defender, the `counter` step has already taken all its weapons, so strike-back offers nothing and completes.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~ArmyRulesContentTests`
Expected: FAIL: the part holds no entries.

- [ ] **Step 3: Write the part**

Replace `server/game/Content/grimdark-future-army-rules.part.json` with:

```json
{ "name": "Grimdark Future army-book rules", "gameRules": "grimdark-future",
  "entries": [
    {"name": "Bane when Shooting", "kind": "model", "aggregation": "all", "ruleText": "This model gets Bane when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Bane", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Changebound", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule are shot or charged from over 9\" away, enemy units get -1 to hit rolls.", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}, {"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "action_taken", "subject": "opponent", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
    {"name": "Changebound Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Changebound, enemies attacking them always get -1 to hit rolls from Changebound (instead of only when being shot/charged from over 9\" away).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Changebound", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Changebound"]}]}, {"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Changebound"]}]}]},
    {"name": "Clan Warrior", "kind": "model", "aggregation": "all", "ruleText": "For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks.", "effects": []},
    {"name": "Clan Warrior Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Clan Warrior, it gets extra attacks on successful unmodified hit results of 5-6 from Clan Warrior (instead of only on 6).", "effects": []},
    {"name": "Counter-Attack", "kind": "model", "aggregation": "all", "ruleText": "Strikes first when charged.", "effects": []},
    {"name": "Crack", "kind": "weapon", "ruleText": "On unmodified results of 6 to hit, those hits get AP(+2).", "effects": [{"subject": "self", "ordinal": 30, "kind": "grant_property", "targetEntry": "AP", "amount": 2, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Destructive", "kind": "weapon", "ruleText": "On unmodified results of 6 to hit, those hits get AP(+4).", "effects": [{"subject": "self", "ordinal": 30, "kind": "grant_property", "targetEntry": "AP", "amount": 4, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Devout", "kind": "model", "aggregation": "all", "ruleText": "When attacking, unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Devout Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Devout, when it shoots or charges enemies over 9\" away, it deals extra hits on successful unmodified hit results of 5-6 from Devout (instead of only on 6).", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Devout"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["hit", "raw"]}]}, {"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Devout"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["hit", "raw"]}]}]},
    {"name": "Disintegrate", "kind": "weapon", "ruleText": "Ignores Regeneration, and against units where most models have Defense 2+ to Defense 3+, this weapon gets AP(+2).", "effects": [{"subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration"}, {"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 2, "conditions": [{"kind": "most_models", "subject": "opponent", "comparison": "lte", "number": 3, "values": ["Defense"]}]}]},
    {"name": "Evasive", "kind": "model", "aggregation": "all", "ruleText": "Enemies get -1 to hit rolls when attacking units where all models have this rule.", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1}]},
    {"name": "Ferocious", "kind": "model", "aggregation": "all", "ruleText": "When attacking, unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["hit", "raw"]}]}]},
    {"name": "Ferocious Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Ferocious, when it shoots or charges enemies over 9\" away, it deals extra hits on successful unmodified hit results of 5-6 from Ferocious (instead of only on 6).", "effects": [{"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Ferocious"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["hit", "raw"]}]}, {"subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Ferocious"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["hit", "raw"]}]}]},
    {"name": "Grounded Reinforcement", "kind": "model", "aggregation": "all", "ruleText": "If a unit where all models have this rule has most of them within 1\" of terrain, they get +1 to defense rolls.", "effects": []},
    {"name": "Guardian", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule are shot or charged from over 9\" away and take hits, those hits count as having AP(-1), to a min. of AP(0).", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_parameter", "targetEntry": "AP", "amount": -1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}, {"subject": "opponent", "ordinal": 10, "kind": "modify_parameter", "targetEntry": "AP", "amount": -1, "conditions": [{"kind": "action_taken", "subject": "opponent", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
    {"name": "Guardian Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Guardian, enemy hits always count as having AP(-1) from Guardian (instead of only when being shot/charged from over 9\" away).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Guardian", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Guardian"]}]}, {"subject": "opponent", "ordinal": 10, "kind": "modify_parameter", "targetEntry": "AP", "amount": -1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Guardian"]}]}]},
    {"name": "Harassing", "kind": "model", "aggregation": "all", "ruleText": "Once per round, units where all models have this rule may move by up to 3\" after shooting or being in melee.", "effects": []},
    {"name": "Harassing Boost", "kind": "model", "aggregation": "all", "ruleText": "If most models in this unit have Harassing, they may move by up to 6\" from Harassing (instead of only 3\").", "effects": []},
    {"name": "Havocbound", "kind": "model", "aggregation": "all", "ruleText": "When this model shoots at enemies over 9\" away, or when it charges, its weapons get AP(+1).", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}, {"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Havocbound Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Havocbound, it always gets AP(+1) from Havocbound (instead of only when shooting over 9\" away, or when charging).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Havocbound", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Havocbound"]}]}, {"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Havocbound"]}]}]},
    {"name": "Hazardous", "kind": "weapon", "ruleText": "Gets AP(4), but this model's unit takes one wound on unmodified rolls of 1 to hit.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 4}, {"subject": "self", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 1, "values": ["hit", "raw"]}]}]},
    {"name": "Highborn", "kind": "model", "aggregation": "all", "ruleText": "Moves +2\" when using Advance, and +2” when using Rush/Charge.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 2}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 2}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 2}]},
    {"name": "Highborn Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Highborn, it moves +4\" on Advance and +4\" on Rush/Charge from Highborn (instead of only +2\" and +2\").", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Highborn", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Highborn"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 4, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Highborn"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 4, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Highborn"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Highborn"]}]}]},
    {"name": "Hit & Run Fighter", "kind": "model", "aggregation": "all", "ruleText": "Once per round, units where all models have this rule may move by up to 3\" after being in melee.", "effects": []},
    {"name": "Hit & Run Shooter", "kind": "model", "aggregation": "all", "ruleText": "Once per round, units where all models have this rule may move by up to 3\" after shooting.", "effects": []},
    {"name": "Hive Bond", "kind": "model", "aggregation": "all", "ruleText": "Units where all models have this rule get +1 to morale test rolls triggered by shooting/melee.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "morale", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting", "melee"]}]}]},
    {"name": "Hive Bond Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Hive Bond, they get +2 to morale test rolls from Hive Bond (instead of only +1).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Hive Bond", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Hive Bond"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "morale", "amount": 2, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Hive Bond"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting", "melee"]}]}]},
    {"name": "Hold the Line", "kind": "model", "aggregation": "all", "ruleText": "Units where all models have this rule get +1 to morale test rolls triggered by shooting/melee.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "morale", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting", "melee"]}]}]},
    {"name": "Hold the Line Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Hold the Line, they get +2 to morale test rolls from Hold the Line (instead of only +1).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Hold the Line", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Hold the Line"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "morale", "amount": 2, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Hold the Line"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting", "melee"]}]}]},
    {"name": "Infected", "kind": "model", "aggregation": "all", "ruleText": "Enemies that roll to block hits from this model's weapons take 1 extra wound for each unmodified result of 1 that they roll.", "effects": [{"subject": "opponent", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 1, "values": ["defense", "raw"]}]}]},
    {"name": "Infected Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Infected, when it shoots or charges enemies over 9\" away, enemies taking wounds from it take extra wounds on failed defense rolls of 1-2 from Infected (instead of only on 1).", "effects": [{"subject": "opponent", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Infected"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 2, "values": ["defense", "raw"]}]}, {"subject": "opponent", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Infected"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 2, "values": ["defense", "raw"]}]}]},
    {"name": "Lacerate", "kind": "weapon", "ruleText": "When attacking the target must re-roll unmodified Defense results of 6.", "effects": [{"subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["defense", "raw"]}]}]},
    {"name": "Lustbound", "kind": "model", "aggregation": "all", "ruleText": "This model gets +4\" range when shooting and moves +2\" when using Charge actions.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetStat": {"level": "weapon", "name": "Range"}, "amount": 4, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 2}]},
    {"name": "Lustbound Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Lustbound, it gets +8\" range when shooting and moves +4\" when using Charge actions from Lustbound (instead of only +4\" and +2\").", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Lustbound", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Lustbound"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetStat": {"level": "weapon", "name": "Range"}, "amount": 8, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Lustbound"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Lustbound"]}]}]},
    {"name": "Machine-Fog", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule are shot or charged from over 9\" away, enemy units get -1 to hit rolls.", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}, {"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "action_taken", "subject": "opponent", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
    {"name": "Machine-Fog Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Machine-Fog, enemies attacking them always get -1 to hit rolls from Machine-Fog (instead of only when being shot/charged from over 9\" away).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Machine-Fog", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Machine-Fog"]}]}, {"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Machine-Fog"]}]}]},
    {"name": "Melee Evasion", "kind": "model", "aggregation": "all", "ruleText": "Enemies get -1 to hit rolls in melee when attacking units where all models have this rule.", "effects": [{"subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Mischievous", "kind": "model", "aggregation": "all", "ruleText": "When attacking, targets must re-roll unmodified Defense results of 6 when blocking hits from this model's weapons.", "effects": [{"subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["defense", "raw"]}]}]},
    {"name": "Mischievous Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Mischievous, when it shoots or charges enemies over 9\" away, enemies taking hits from it must re-roll successful unmodified defense results of 5-6 from Mischievous (instead of only on 6).", "effects": [{"subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Mischievous"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["defense", "raw"]}]}, {"subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Mischievous"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["defense", "raw"]}]}]},
    {"name": "Piercing Assault", "kind": "model", "aggregation": "all", "ruleText": "This model gets AP(+1) when charging.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Piercing Hunter", "kind": "model", "aggregation": "all", "ruleText": "When this model shoots at enemies over 9\" away, its weapons get AP(+1).", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
    {"name": "Plaguebound", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 6+ it is ignored.", "effects": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 6, "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}]}]},
    {"name": "Plaguebound Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Plaguebound, they ignore wounds on rolls of 5-6 from Plaguebound (instead of only on 6+).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Plaguebound", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Plaguebound"]}]}, {"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 5, "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}], "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Plaguebound"]}]}]},
    {"name": "Primal", "kind": "model", "aggregation": "all", "ruleText": "For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks.", "effects": []},
    {"name": "Primal Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Primal, it gets extra attacks on successful unmodified hit results of 5-6 from Primal (instead of only on 6).", "effects": []},
    {"name": "Quick Shot", "kind": "model", "aggregation": "all", "ruleText": "This model may shoot after using Rush actions.", "effects": []},
    {"name": "Rapid Advance", "kind": "model", "aggregation": "all", "ruleText": "This model moves +4\" when using Advance actions.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 4}]},
    {"name": "Rapid Blink", "kind": "model", "aggregation": "none", "ruleText": "When this unit is activated, you may place all models with this rule in it anywhere fully within D3\" of their position.", "effects": []},
    {"name": "Rapid Blink Boost", "kind": "model", "aggregation": "none", "ruleText": "If this model has Rapid Blink, it may be placed within 2D3\" from Rapid Blink (instead of only within D3\").", "effects": []},
    {"name": "Rapid Rush", "kind": "model", "aggregation": "all", "ruleText": "This model moves +6\" when using Rush actions.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 6}]},
    {"name": "Resistance", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 6+ it is ignored. If the wounds were from a spell, then they are ignored on a 2+ instead.", "effects": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 6, "conditions": [{"kind": "attack_kind", "comparison": "neq", "values": ["spell"]}], "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}]}, {"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 2, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["spell"]}], "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}]}]},
    {"name": "Scrapper", "kind": "model", "aggregation": "all", "ruleText": "When attacking, targets must re-roll unmodified Defense results of 6 when blocking hits from this model's weapons.", "effects": [{"subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 6, "values": ["defense", "raw"]}]}]},
    {"name": "Scrapper Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Scrapper, when it shoots or charges enemies over 9\" away, enemies taking hits from it must re-roll successful unmodified defense results of 5-6 from Scrapper (instead of only on 6).", "effects": [{"subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Scrapper"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["defense", "raw"]}]}, {"subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Scrapper"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 5, "values": ["defense", "raw"]}]}]},
    {"name": "Scurry", "kind": "model", "aggregation": "all", "ruleText": "Moves +2\" when using Advance, and +2” when using Rush/Charge.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 2}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 2}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 2}]},
    {"name": "Scurry Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Scurry, it moves +4\" on Advance and +4\" on Rush/Charge from Scurry (instead of only +2\" and +2\").", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Scurry", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Scurry"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 4, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Scurry"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 4, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Scurry"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Scurry"]}]}]},
    {"name": "Self-Repair", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 6+ it is ignored.", "effects": [{"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 6, "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}]}]},
    {"name": "Self-Repair Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Self-Repair, they ignore wounds on rolls of 5-6 from Self-Repair (instead of only on 6+).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Self-Repair", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Self-Repair"]}]}, {"subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 5, "children": [{"subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound"}], "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Self-Repair"]}]}]},
    {"name": "Shatter", "kind": "weapon", "ruleText": "Against units where most models have Tough(3) or higher, this weapon gets AP(+2).", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 2, "conditions": [{"kind": "most_models", "subject": "opponent", "comparison": "gte", "number": 3, "values": ["Tough"]}]}]},
    {"name": "Shielded", "kind": "model", "aggregation": "all", "ruleText": "Units where all models have this rule get +1 to defense rolls against hits that are not from spells.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "neq", "values": ["spell"]}]}]},
    {"name": "Shred", "kind": "weapon", "ruleText": "On unmodified results of 1 to block hits, this weapon deals 1 extra wound.", "effects": [{"subject": "opponent", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 1, "values": ["defense", "raw"]}]}]},
    {"name": "Slayer", "kind": "model", "aggregation": "all", "ruleText": "When this model shoots at enemies over 9\" away, or when it charges, its weapons get AP(+2) if most models in the target have Tough(3) or higher.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 2, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "most_models", "subject": "opponent", "comparison": "gte", "number": 3, "values": ["Tough"]}]}, {"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 2, "conditions": [{"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "most_models", "subject": "opponent", "comparison": "gte", "number": 3, "values": ["Tough"]}]}]},
    {"name": "Sturdy", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule are shot or charged from over 9\" away, they get +1 to defense rolls.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": 1, "conditions": [{"kind": "action_taken", "subject": "opponent", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
    {"name": "Sturdy Boost", "kind": "model", "aggregation": "all", "ruleText": "If all models in this unit have Sturdy, they always get +1 to defense rolls from Sturdy (instead of only when being shot/charged from over 9\" away).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Sturdy", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Sturdy"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Sturdy"]}]}]},
    {"name": "Targeting Visor", "kind": "model", "aggregation": "all", "ruleText": "When this model shoots at enemies over 9\" away, it gets +1 to hit rolls.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}]}]},
    {"name": "Targeting Visor Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Targeting Visor, it always gets +1 to hit rolls when shooting from Targeting Visor (instead of only when shooting over 9\" away).", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Targeting Visor", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Targeting Visor"]}]}, {"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Targeting Visor"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Tear", "kind": "weapon", "ruleText": "Against units where most models have Tough(9) or higher, this weapon gets AP(+4).", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 4, "conditions": [{"kind": "most_models", "subject": "opponent", "comparison": "gte", "number": 9, "values": ["Tough"]}]}]},
    {"name": "Teleport", "kind": "model", "aggregation": "none", "ruleText": "Once per activation, before attacking, you may place this model anywhere fully within 3\" of its position when using Advance/Charge actions, or fully within 6\" of its position when using Rush actions.", "effects": []},
    {"name": "Unpredictable Fighter", "kind": "model", "aggregation": "all", "ruleText": "When in melee, roll one die and apply one effect to all models with this rule: on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead.", "effects": []},
    {"name": "Unpredictable Shooter", "kind": "model", "aggregation": "all", "ruleText": "When shooting, roll one die and apply one effect to all models with this rule: on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead.", "effects": []},
    {"name": "Unwieldy", "kind": "weapon", "ruleText": "Strikes last when charging.", "effects": []},
    {"name": "Warbound", "kind": "model", "aggregation": "all", "ruleText": "Enemies that roll to block hits from this model's weapons take 1 extra wound for each unmodified result of 1 that they roll.", "effects": [{"subject": "opponent", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "roll_result", "comparison": "eq", "number": 1, "values": ["defense", "raw"]}]}]},
    {"name": "Warbound Boost", "kind": "model", "aggregation": "all", "ruleText": "If this model has Warbound, when it shoots or charges enemies over 9\" away, enemies taking wounds from it take extra wounds on failed defense rolls of 1-2 from Warbound (instead of only on 1).", "effects": [{"subject": "opponent", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Warbound"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 2, "values": ["defense", "raw"]}]}, {"subject": "opponent", "ordinal": 30, "kind": "add_hit", "targetQuantity": "wounds", "amount": 1, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Warbound"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Charge"]}, {"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}, {"kind": "range", "comparison": "gt", "number": 9}, {"kind": "roll_result", "comparison": "eq", "number": 2, "values": ["defense", "raw"]}]}]}
  ] }
```

- [ ] **Step 4: Strike order**

In `GfNames`: `public const string CounterAttack = "Counter-Attack", Unwieldy = "Unwieldy";`.

In `GfMelee`, add the two readers; a granted rule counts through `Lookup.UnitHas`, which reads a subject-self `grant_property` grant by name (Task 5), so *Creator of Illusions*' *"gets Unwieldy in melee"* reaches every weapon of the unit:

```csharp
    /// <summary>p.13 Counter's strike-first weapons, and every melee weapon of a defender with Counter-Attack ("Strikes first when charged").</summary>
    static bool StrikesFirst(StepContext ctx, Weapon w) => IsCounter(w) || Lookup.UnitHas(ctx.State, ctx.Properties, Defender(ctx), GfNames.CounterAttack);
    /// <summary>Unwieldy: "Strikes last when charging": the charger's weapon, or every weapon of a charger the rule was granted to.</summary>
    static bool StrikesLast(StepContext ctx, Weapon w) => w.Properties.Any(p => p.Name == GfNames.Unwieldy) || Lookup.UnitHas(ctx.State, ctx.Properties, Charger(ctx), GfNames.Unwieldy);
```

`CounterStep.Run` reads `MeleeWeapons(ctx.State, Defender(ctx), w => StrikesFirst(ctx, w))`. `StrikeBackStep.Run` reads `MeleeWeapons(ctx.State, defender, w => !StrikesFirst(ctx, w))`; with Counter-Attack that list is empty and the step completes with the defender's fatigue, as it already does after a Counter strike with nothing left. `ChargerAttacksStep` strikes with the charger's weapons that do not strike last, and fatigues the charger only when none do:

```csharp
    sealed class ChargerAttacksStep : IStep
    {
        public string Name => "charger-attacks";
        public StepOutcome Run(StepContext ctx)
        {
            var weapons = ctx.Frame.Context.WeaponIds.Where(id => !StrikesLast(ctx, WeaponOf(ctx.State, id))).ToList();
            var lastOnes = ctx.Frame.Context.WeaponIds.Count > weapons.Count;
            return Strike(ctx, Name, GroupsOf(ctx.State, weapons), Charger(ctx), Defender(ctx), GfNames.Charge, lastOnes ? [] : Fatigue(Charger(ctx)));
        }
    }

    /// <summary>Unwieldy's strike, after the strike back: the charger's strike-last weapons, which fatigue it when they are done.</summary>
    sealed class UnwieldyStep : IStep
    {
        public string Name => "unwieldy";
        public StepOutcome Run(StepContext ctx)
        {
            var weapons = ctx.Frame.Context.WeaponIds.Where(id => StrikesLast(ctx, WeaponOf(ctx.State, id))).ToList();
            if (weapons.Count == 0 && !Recorded(ctx, Name)) return new Complete([]);
            if (Lookup.LivingModels(ctx.State, Charger(ctx)).Count == 0) return new Complete([]);
            return Strike(ctx, Name, GroupsOf(ctx.State, weapons), Charger(ctx), Defender(ctx), GfNames.Charge, Fatigue(Charger(ctx)));
        }
    }

    static Weapon WeaponOf(GameState state, Guid id) => state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == id);
```

and the sequence:

```csharp
    public static Sequence Melee() => new(GfNames.Melee, [new MeleeStartStep(), new CounterStep(), new ImpactStep(), new ChargerAttacksStep(), new StrikeBackStep(), new UnwieldyStep(), new CompareStep(), new ConsolidateStep()]);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. `GfMeleeTests` that read the charger's fatigue after `charger-attacks` still pass: no core weapon is Unwieldy, so the charger is fatigued there as before.

- [ ] **Step 6: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Rules/GrimdarkFuture tests/Tabletop.GameServer.Tests/ArmyRulesContentTests.cs
git commit -m "Author the 73 army-book rules spells name; Counter-Attack strikes first, Unwieldy last"
```
