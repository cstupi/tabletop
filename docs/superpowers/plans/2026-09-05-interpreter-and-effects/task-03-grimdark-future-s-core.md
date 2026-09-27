# Task 3: Grimdark Future's core `Properties`, authored in-repo

**Files:**
- Create: `server/game/Content/grimdark-future-core.properties.json`
- Modify: `server/game/Tabletop.GameServer.csproj` (embed `Content/*.properties.json`)
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs`, `tests/Tabletop.GameServer.Tests/CoreContentTests.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces: the embedded resource `Tabletop.GameServer.Content.grimdark-future-core.properties.json`; test infrastructure `TestProperties.Core` (the set built from it with `Guid.CreateVersion7`), `TestProperties.CoreDocument`, `TestProperties.WithSpells(params EntryDocument[] spells)` (the core plus a test spell book, rebuilt so the hash differs). Stat names `Quality` and `Defense` at **both** `unit` and `model` level, `Range`, `Attacks` (weapon) — the names the management plan's import writes (management plan → Interfaces needed → Game-server plan). Actions `Hold` 0, `Advance` 6, `Rush` 12, `Charge` 12. Rolls `hit`, `defense`, `morale`, `casting`.
- **`Quality` and `Defense` are declared twice, once per level.** A hero Army Forge publishes as attached to another unit imports as a model of that unit (the management plan), and its slot carries its own quality and defence as `slot_stat` rows. `slot_stat` has no foreign key to `stat_definition`, so nothing rejects a stat written at a level the ruleset never declared; the second declaration is what keeps those stored values declared ones and gives a `replace_stat` row at `model` level somewhere to resolve against. A joined hero attacks at **its own** Quality (p.4) and blocks on the **unit's** Defense until every other model is dead (p.14), so the model-level `Quality` is read and the model-level `Defense` is read only in that one case. `StatReader.TargetNumber` takes an optional model and resolves either level (Task 7); `GfRolls.HitQuality` reads the slot's `Quality` for the attacking model, `GfAttack` splits the group's attacks by that value, and `GfRolls.DefensePlan` reads the slot's `Defense` only when the settled count of the target's living models is one. Ruling 13 states the mechanism.
- Every special rule on pages 13–15 is an entry. Effect rows follow `schema.md` → Worked against the book (`AP`, `Stealth`, `Impact`, `Bane`) and the effect grammar's table; the rest are read from the rulebook text quoted in `ruleText`. Entries with no effects are names `GrimdarkFutureRules` checks (effect grammar → What `GameRules` handles instead): `Ambush`, `Scout`, `Flying`, `Strider`, `Immobile`, `Limited`, `Counter`, `Caster`, `Hero`, `Tough`, `Fear`, `Transport`, `Takedown`'s targeting, `Aircraft`'s movement. `roll_result` conditions carry `values: [roll, "raw" | "modified"]` and `number` the face (`schema.md` → A condition is a row, and its values are rows).
- **Two entries are in both places** (effect grammar → "A property may be in both places"): `Indirect` carries its two effect rows *and* is checked by name for *"may target enemies that are not in line of sight"*, which is no kind (`GfAttack.EligibleModels`, Task 13); `Aircraft` carries its two and is checked by name during movement. `Hero`'s third clause, *"must use the unit's Defense until all other models have been killed"*, needs **no** effect row in either half: `GfRolls.DefensePlan` reads `Defense` at unit level ([content values](../specs/2026-08-29-content-values-design.md) → Stats are typed and declared at three levels) while the target holds more than one model, so a `replace_stat` row would restate it, and it reads the surviving model's own slot `Defense` once the settled count is one. No effect row could have expressed the sunset: `roll-to-block` throws one defence roll per hit before `deal` assigns any wound to a model (pages 8 and 9), so at the moment the threshold is chosen there is no model the grammar could name, and the count has to be the one settled before the first group fired rather than a live read (ruling 3). `Hero` stays a pure name check, read in two places: `EligibleForNextWound` for *"heroes must be assigned wounds last"* (Task 12), and `GfRolls.MoralePlan` for *"may take morale tests on behalf of the unit"* (Task 11).
- Kinds and aggregation follow the rulebook's wording: "models with this special rule" → `model`; a rule read on the unit's pooled roll aggregates `all` (`Stealth`, `Furious`, `Relentless`, `Fast`, `Slow`, `Flying`, `Strider`, `Immobile`), and so do `Fearless` and `Regeneration`, whose texts say *"a unit where all models have this rule"* (3.5.1), and `Artillery`, `all` after `Stealth`, which is the same to-hit penalty at the same range band; per-model rules are `none` (`Tough`, `Impact`, `Caster`, `Hero`, `Fear`, `Transport`); `Aircraft`, `Ambush`, `Scout` are `unit`; the rest are `weapon`. Ruling 4 records the readings. A `unit`-kind entry can still arrive on a model slot — import writes a rule onto slots when its count is smaller than the unit, and every rule a joined hero carries lands on its slot whatever the catalog kind — and it then counts for the whole unit in both readers, `LiveProperties.Gather` and `Lookup.UnitHas` (Task 6).

Spec: rulebook pages 13–15 (every rule); effect grammar → The twelve effect kinds, `Roll` nests, Multiplication carries a cap, Conditions belong to the effect; `schema.md` → An action is a row, A roll is a row, `amount` is signed.

- [ ] **Step 1: Write the failing test and `TestProperties`**

```csharp
using System.Reflection;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestProperties
{
    public const string ResourceName = "Tabletop.GameServer.Content.grimdark-future-core.properties.json";
    public static PropertySetDocument CoreDocument { get; } = PropertiesJson.Parse(ReadResource());
    public static PropertySet Core { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

    public static PropertySet WithSpells(params EntryDocument[] spells) =>
        PropertiesJson.ToSet(CoreDocument with { Name = "Core + test spells", Entries = [.. CoreDocument.Entries, .. spells] }, Guid.CreateVersion7);

    static string ReadResource()
    {
        using var s = typeof(PropertySet).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException($"{ResourceName} is not embedded");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
```

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class CoreContentTests
{
    static readonly PropertySet Core = TestProperties.Core;

    [Fact]
    public void The_document_is_valid_and_names_the_rulebooks_thirty_three_rules()
    {
        Assert.Empty(PropertiesJson.Validate(TestProperties.CoreDocument));
        string[] rules = ["Aircraft", "Ambush", "AP", "Artillery", "Bane", "Blast", "Caster", "Counter", "Deadly", "Fast", "Fear", "Fearless", "Flying", "Furious",
            "Hero", "Immobile", "Impact", "Indirect", "Limited", "Regeneration", "Relentless", "Reliable", "Rending", "Scout", "Slow", "Stealth", "Strider",
            "Surge", "Takedown", "Thrust", "Tough", "Transport", "Unstoppable"];
        Assert.Equal(rules.OrderBy(r => r), Core.Entries.Select(e => e.Name).OrderBy(r => r));
        // Quality and Defense are declared at model level as well as unit level: a joined hero is a model of the unit it joined and its slot carries its own two.
        Assert.Equal([(StatLevel.Unit, "Defense"), (StatLevel.Unit, "Quality"), (StatLevel.Model, "Defense"), (StatLevel.Model, "Quality"), (StatLevel.Weapon, "Attacks"), (StatLevel.Weapon, "Range")],
            Core.Stats.OrderBy(s => s.Level).ThenBy(s => s.Name, StringComparer.Ordinal).Select(s => (s.Level, s.Name)));
        Assert.Equal(Tabletop.Content.StatKind.TargetNumber, Core.Stat(StatLevel.Model, "Quality")!.Kind);
        Assert.Equal(["Advance", "Charge", "Hold", "Rush"], Core.Actions.Select(a => a.Name).OrderBy(n => n));
        Assert.Equal([("Advance", 6.0), ("Charge", 12.0), ("Hold", 0.0), ("Rush", 12.0)], Core.Actions.OrderBy(a => a.Name).Select(a => (a.Name, a.Distance)));
        Assert.Equal(["casting", "defense", "hit", "morale"], Core.Rolls.Select(r => r.Name).OrderBy(n => n));
        Assert.Equal("grimdark-future", Core.GameRules);
    }

    [Fact]
    public void The_four_worked_rows_match_schema_md()
    {
        var ap = Core.Entry("AP")!.Effects.Single();
        Assert.Equal((EffectSubject.Opponent, EffectKind.ModifyRoll, "defense", -1, true), (ap.Subject, ap.Kind, ap.TargetRoll, ap.Amount, ap.AmountFromParameter));
        var stealth = Core.Entry("Stealth")!;
        Assert.Equal(Aggregation.All, stealth.Aggregation);
        Assert.Equal(2, stealth.Effects.Single().Conditions.Count);
        Assert.Contains(stealth.Effects[0].Conditions, c => c.Kind == ConditionKind.Range && c.Comparison == Comparison.Gt && c.Number == 9 && c.Subject is null);
        var impact = Core.Entry("Impact")!.Effects.Single();
        Assert.Equal((EffectKind.Roll, true, 2, EffectKind.AddHit), (impact.Kind, impact.AmountFromParameter, impact.Threshold, impact.Children.Single().Kind));
        Assert.Contains(impact.Conditions, c => c.Kind == ConditionKind.ActorState && c.Comparison == Comparison.Neq && c.Values.Contains("fatigued"));
        var bane = Core.Entry("Bane")!.Effects;
        Assert.Equal(2, bane.Count);
        Assert.Contains(bane, e => e.Kind == EffectKind.ForceReroll && e.TargetRoll == "defense" && e.Conditions.Single().Values.SequenceEqual(["defense", "raw"]));
        Assert.Contains(bane, e => e.Kind == EffectKind.SuppressProperty && e.TargetEntry == "Regeneration" && e.Subject == EffectSubject.Opponent && e.Conditions.Count == 0);
    }

    [Fact]
    public void Name_check_rules_carry_no_effects_and_blast_resolves_last()
    {
        foreach (var name in new[] { "Ambush", "Scout", "Flying", "Strider", "Immobile", "Limited", "Counter", "Caster", "Hero", "Tough", "Fear", "Transport" })
            Assert.Empty(Core.Entry(name)!.Effects);
        var blast = Core.Entry("Blast")!.Effects.Single(e => e.Kind == EffectKind.Multiply);
        Assert.Equal((TargetQuantity.Hits, CapKind.ModelsInTargetUnit), (blast.TargetQuantity, blast.CapKind));
        Assert.True(blast.Ordinal > Core.AllEffects().Where(e => e.Kind != EffectKind.Multiply).Max(e => e.Ordinal));
        Assert.Equal(0, Core.Entry("Deadly")!.Effects.Single().Ordinal);
        Assert.Equal(3, Core.Entry("Fast")!.Effects.Count);
        Assert.Equal(["Advance", "Charge", "Rush"], Core.Entry("Slow")!.Effects.Select(e => e.TargetAction).OrderBy(a => a));
    }
}
```

- [ ] **Step 2: Add to `Tabletop.GameServer.csproj`**

```xml
  <ItemGroup>
    <EmbeddedResource Include="Content/*.properties.json" LogicalName="Tabletop.GameServer.Content.%(Filename)%(Extension)" />
  </ItemGroup>
```

- [ ] **Step 3: Write `Content/grimdark-future-core.properties.json`** (`ruleText` is the rulebook's sentence; abbreviated here with `…` only where the page text is long — the file carries the full sentence)

```json
{
  "name": "Grimdark Future v3.5.1 core", "gameRules": "grimdark-future",
  "stats": [
    { "level": "unit", "name": "Quality", "kind": "target_number" }, { "level": "unit", "name": "Defense", "kind": "target_number" },
    { "level": "model", "name": "Quality", "kind": "target_number" }, { "level": "model", "name": "Defense", "kind": "target_number" },
    { "level": "weapon", "name": "Range", "kind": "distance" }, { "level": "weapon", "name": "Attacks", "kind": "count" } ],
  "actions": [ { "name": "Hold", "distance": 0 }, { "name": "Advance", "distance": 6 }, { "name": "Rush", "distance": 12 }, { "name": "Charge", "distance": 12 } ],
  "rolls": [ "hit", "defense", "morale", "casting" ],
  "entries": [
    { "name": "Aircraft", "kind": "unit", "ruleText": "May only use Advance actions, moving in a straight line, and adding 30\" to its total move (even if Shaken). … units targeting them get -12\" to their range.",
      "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_stat", "targetStat": { "level": "weapon", "name": "Range" }, "amount": -12 } ] },
    { "name": "Ambush", "kind": "unit", "ruleText": "May be set aside before deployment. At the start of any round after the first, may be deployed anywhere over 9\" away from enemy units. …", "effects": [] },
    { "name": "AP", "kind": "weapon", "takesParameter": true, "ruleText": "Targets get -X to Defense rolls when blocking hits from weapons with this special rule.",
      "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": -1, "amountFromParameter": true } ] },
    { "name": "Artillery", "kind": "model", "aggregation": "all", "ruleText": "May only use Hold actions. When this model shoots at enemies over 9\" away, it gets +1 to hit rolls. When enemy units shoot at this model from over 9\" away, they get -2 to hit rolls.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [
          { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] }, { "kind": "range", "comparison": "gt", "number": 9 } ] },
        { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -2, "conditions": [
          { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] }, { "kind": "range", "comparison": "gt", "number": 9 } ] } ] },
    { "name": "Bane", "kind": "weapon", "ruleText": "Ignores Regeneration, and when attacking the target must re-roll unmodified Defense results of 6.",
      "effects": [
        { "subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [ { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "defense", "raw" ] } ] },
        { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" } ] },
    { "name": "Blast", "kind": "weapon", "takesParameter": true, "ruleText": "Ignores cover, and after resolving other special rules, each hit is multiplied by X, where X is up to as many hits as models in the target unit.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "suppress_modifiers", "targetModifier": "cover" },
        { "subject": "self", "ordinal": 90, "kind": "multiply", "targetQuantity": "hits", "amount": 1, "amountFromParameter": true, "capKind": "models_in_target_unit" } ] },
    { "name": "Caster", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "Gets X spell tokens at the start of each round, but can't hold more than 6 tokens at once. …", "effects": [] },
    { "name": "Counter", "kind": "weapon", "ruleText": "Strikes first with this weapon when charged, and the charging unit gets -1 total Impact rolls per model with Counter.", "effects": [] },
    { "name": "Deadly", "kind": "weapon", "takesParameter": true, "ruleText": "Assign each wound to one model, and multiply it by X. Hits from Deadly must be resolved first, and these wounds don't carry over to other models if the original target is killed.",
      "effects": [ { "subject": "self", "ordinal": 0, "kind": "multiply", "targetQuantity": "wounds", "amount": 1, "amountFromParameter": true } ] },
    { "name": "Fast", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule move +2\" when using Advance and +4\" when using Rush/Charge.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 2 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 4 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4 } ] },
    { "name": "Fear", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "This model counts as having dealt +X wounds when checking who won melee.", "effects": [] },
    { "name": "Fearless", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule fails a morale test, roll one die. On a 4+ it counts as passed instead.",
      "effects": [ { "subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 4, "children": [ { "subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "morale_test" } ] } ] },
    { "name": "Flying", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule may move through units and terrain, and ignore terrain effects whilst moving.", "effects": [] },
    { "name": "Furious", "kind": "model", "aggregation": "all", "ruleText": "When charging, unmodified results of 6 to hit in melee deal 1 extra hit (only the original hit counts as a 6 for special rules).",
      "effects": [ { "subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [
        { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "attack_kind", "comparison": "eq", "values": [ "melee" ] },
        { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "hit", "raw" ] } ] } ] },
    { "name": "Hero", "kind": "model", "aggregation": "none", "ruleText": "Heroes with up to Tough(6) may deploy as part of one multi-model unit without another Hero. …", "effects": [] },
    { "name": "Immobile", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule may only use Hold actions.", "effects": [] },
    { "name": "Impact", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "Roll X dice when attacking after charging, unless fatigued. For each 2+ the target takes one hit.",
      "effects": [ { "subject": "self", "ordinal": 20, "kind": "roll", "amount": 1, "amountFromParameter": true, "threshold": 2,
        "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "actor_state", "subject": "self", "comparison": "neq", "values": [ "fatigued" ] } ],
        "children": [ { "subject": "self", "ordinal": 0, "kind": "add_hit", "amount": 1 } ] } ] },
    { "name": "Indirect", "kind": "weapon", "ruleText": "Gets -1 to hit rolls when shooting after moving. May target enemies that are not in line of sight as if in line of sight, and ignores cover from sight obstructions.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Advance" ] } ] },
        { "subject": "self", "ordinal": 10, "kind": "suppress_modifiers", "targetModifier": "cover" } ] },
    { "name": "Limited", "kind": "weapon", "ruleText": "Weapons with this rule may only be used once per game.", "effects": [] },
    { "name": "Regeneration", "kind": "model", "aggregation": "all", "ruleText": "When a unit where all models have this rule takes wounds, roll one die for each. On a 5+ it is ignored.",
      "effects": [ { "subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 5, "children": [ { "subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound" } ] } ] },
    { "name": "Relentless", "kind": "model", "aggregation": "all", "ruleText": "When this model shoots at enemies over 9\" away, unmodified results of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).",
      "effects": [ { "subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [
        { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] }, { "kind": "range", "comparison": "gt", "number": 9 },
        { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "hit", "raw" ] } ] } ] },
    { "name": "Reliable", "kind": "weapon", "ruleText": "Models attacks at Quality 2+ with this weapon.",
      "effects": [ { "subject": "self", "ordinal": 1, "kind": "replace_stat", "targetStat": { "level": "unit", "name": "Quality" }, "replacement": "2+" } ] },
    { "name": "Rending", "kind": "weapon", "ruleText": "Ignores Regeneration, and on unmodified results of 6 to hit, those hits get AP(+4).",
      "effects": [
        { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" },
        { "subject": "self", "ordinal": 30, "kind": "grant_property", "targetEntry": "AP", "amount": 4, "conditions": [ { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "hit", "raw" ] } ] } ] },
    { "name": "Scout", "kind": "unit", "ruleText": "May be set aside before deployment. After all other units are deployed, may be deployed anywhere fully within 12\" of their deployment zone. …", "effects": [] },
    { "name": "Slow", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule move -2\" when using Advance, and -4\" when using Rush/Charge.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": -2 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": -4 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": -4 } ] },
    { "name": "Stealth", "kind": "model", "aggregation": "all", "ruleText": "When units where all models have this rule are shot from over 9\" away, enemy units get -1 to hit rolls.",
      "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [
        { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] }, { "kind": "range", "comparison": "gt", "number": 9 } ] } ] },
    { "name": "Strider", "kind": "model", "aggregation": "all", "ruleText": "May ignore the effects of difficult terrain when moving.", "effects": [] },
    { "name": "Surge", "kind": "weapon", "ruleText": "On unmodified results of 6 to hit, this weapon deals 1 extra hit (only the original hit counts as a 6 for special rules).",
      "effects": [ { "subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [
        { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "hit", "raw" ] } ] } ] },
    { "name": "Takedown", "kind": "weapon", "ruleText": "This model may pick any model in the target unit as its individual target, which is resolved as if it was a unit of [1]. Takedown attacks must be resolved before other weapons.",
      "effects": [] },
    { "name": "Thrust", "kind": "weapon", "ruleText": "When charging, gets +1 to hit rolls and AP(+1) in melee.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "attack_kind", "comparison": "eq", "values": [ "melee" ] } ] },
        { "subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "attack_kind", "comparison": "eq", "values": [ "melee" ] } ] } ] },
    { "name": "Tough", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "This model must take X wounds before being killed. … heroes must be assigned wounds last, even if already wounded.", "effects": [] },
    { "name": "Transport", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "May transport up to X models or Heroes with up to Tough(6) …", "effects": [] },
    { "name": "Unstoppable", "kind": "weapon", "ruleText": "Ignores Regeneration, and ignores all negative modifiers to this weapon.",
      "effects": [
        { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" },
        { "subject": "self", "ordinal": 5, "kind": "suppress_modifiers", "targetModifier": "to_hit_penalties" },
        { "subject": "self", "ordinal": 5, "kind": "suppress_modifiers", "targetModifier": "range_penalties" } ] }
  ]
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter CoreContentTests`; expect 3 passed. Commit: `Author Grimdark Future's core Properties as an in-repo document`.**
