# Task 1: The grammar: `at_point`, usage scopes, `place`, attacks, migration 006

**Files:**
- Create: `server/management/Persistence/Migrations/006_army_rule_hooks.sql`
- Modify: `server/game/Properties/PropertySet.cs`, `PropertySetDocument.cs`, `PostgresPropertiesStore.cs`
- Modify: `server/game/Effects/Interaction.cs` (`Point`, `Faces`), `Conditions.cs` (`AtPoint`, usage scopes), `LiveProperties.cs` (`LiveSet.AtPoint`)
- Modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` (the five points)
- Create: `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`
- Modify: `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`, `PropertiesStoreTests.cs`, `tests/Tabletop.Management.Tests/MigrationTests.cs`

**Interfaces:**
- Consumes: the library's `Effect` and `EffectDocument` with their trailing `TargetTerrain`, `TargetCondition` (library Task 3).
- Produces: `EffectKind.Place`; `TargetQuantity.Attacks`; `ConditionKind.AtPoint`; trailing `string? AmountDice = null` on `Effect` and `EffectDocument`; `Interaction(..., IReadOnlyList<string> DefenderTerrain, string? Point = null, IReadOnlyDictionary<Side, IReadOnlyDictionary<string, int>>? Faces = null)`; `LiveSet.AtPoint(Side side, string point)`; `GfNames.ActivationStart`, `AfterShooting`, `AfterMelee`, `AttackStart` (and the existing `BeforeAttacking`).

Spec → *Hook points* and *Grammar, schema and wire*. `at_point` holds only when the engine asks for a point's effects; a `usage_count` whose first value is `round` or `activation` counts the sequences this rule started in that scope; `place` is a placement with an optional `amount_dice`; `add_hit` adds attacks; a `roll` row's children may test their own entry's roll.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class ArmyRuleHookTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static ConditionDocument At(string point) => new(ConditionKind.AtPoint, null, Comparison.Eq, null, [point]);
    static EffectDocument Move(int inches, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 10, EffectKind.Move, null, null, null, null, null, null, null, inches, false, null, null, null, EffectDuration.Interaction, when, []);

    [Fact]
    public void An_at_point_row_is_returned_only_at_its_point_and_never_by_an_interaction_query()
    {
        var hop = new EntryDocument("Hop", CatalogKind.Model, false, Aggregation.All, null, null, "Hop.", null, [Move(3, At(GfNames.AfterShooting))]);
        var props = TestProperties.WithSpells(hop);
        var a = Unit("A", "4+", "4+", [], 1, [P("Hop")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Live(string? point) => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], point));
        Assert.Single(Live(GfNames.AfterShooting).AtPoint(Side.Attacker, GfNames.AfterShooting));
        Assert.Empty(Live(GfNames.AfterMelee).AtPoint(Side.Attacker, GfNames.AfterMelee));
        Assert.Empty(Live(null).Effects(Side.Attacker, EffectKind.Move));
    }

    [Fact]
    public void A_round_scoped_usage_counts_this_rules_sequences_since_the_round_started()
    {
        var once = new EntryDocument("Once", CatalogKind.Model, false, Aggregation.All, null, null, "Once per round.", null,
            [Move(3, At(GfNames.AfterShooting), new ConditionDocument(ConditionKind.UsageCount, null, Comparison.Lt, 1, ["round"]))]);
        var props = TestProperties.WithSpells(once);
        var a = Unit("A", "4+", "4+", [], 1, [P("Once")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Now() => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], GfNames.AfterShooting));
        Assert.Single(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new SequenceStarted(Guid.CreateVersion7(), GfNames.RepositionSeq, new SequenceContext(a.Id, [], [], "Once")), null));
        Assert.Empty(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new RoundStarted(2, [], []), null));
        Assert.Single(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
    }
}
```

Add to `PropertySetDocumentTests`:

```csharp
    [Fact]
    public void Place_amount_dice_at_point_attacks_and_an_own_roll_validate()
    {
        EffectDocument F(EffectKind k, string? dice = null, TargetQuantity? q = null, IReadOnlyList<ConditionDocument>? when = null, IReadOnlyList<EffectDocument>? kids = null, int? threshold = null) =>
            new(EffectSubject.Self, 10, k, null, null, null, null, q, null, null, 1, false, threshold, null, null, EffectDuration.Interaction, when ?? [], kids ?? [], AmountDice: dice);
        EntryDocument E(params EffectDocument[] f) => new("X", CatalogKind.Model, false, Aggregation.All, null, null, "X.", null, f);
        IReadOnlyList<string> V(EntryDocument e) => PropertiesJson.Validate(TestProperties.RulebookDocument with { Entries = [.. TestProperties.RulebookDocument.Entries, e] });
        Assert.Empty(V(E(F(EffectKind.Place, "d3"))));
        Assert.Contains(V(E(F(EffectKind.Move, "d3"))), p => p.Contains("amount_dice is place only"));
        Assert.Contains(V(E(F(EffectKind.Place, "d6"))), p => p.Contains("amount_dice is 'd3'"));
        Assert.Contains(V(E(F(EffectKind.Place, when: [new ConditionDocument(ConditionKind.AtPoint, null, Comparison.Eq, null, ["whenever"])]))), p => p.Contains("no point 'whenever'"));
        Assert.Contains(V(E(F(EffectKind.AddHit, q: TargetQuantity.Attacks))), p => p.Contains("add_hit attacks is per die"));
        var own = new ConditionDocument(ConditionKind.RollResult, null, Comparison.Lte, 3, ["X", "raw"]);
        Assert.Empty(V(E(F(EffectKind.Roll, threshold: 1, kids: [F(EffectKind.GrantProperty, when: [own]) with { TargetEntry = "AP" }]))));
        Assert.Contains(V(E(F(EffectKind.GrantProperty, when: [own]) with { TargetEntry = "AP" })), p => p.Contains("roll 'X' is not declared"));
    }
```

Add to `PropertiesStoreTests`:

```csharp
    [Fact]
    public async Task A_place_row_its_dice_and_its_point_round_trip()
    {
        var blink = new EntryDocument("Blink", CatalogKind.Model, false, Aggregation.None, null, null, "Blink.", null,
            [new EffectDocument(EffectSubject.Self, 10, EffectKind.Place, null, null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction,
                [new ConditionDocument(ConditionKind.AtPoint, null, Comparison.Eq, null, ["activation-start"])], [], AmountDice: "d3")]);
        var doc = TestProperties.RulebookDocument with { Name = "Hooks " + Guid.CreateVersion7().ToString("N")[24..32], Entries = [.. TestProperties.RulebookDocument.Entries, blink] };
        var hash = PropertiesJson.Hash(doc);
        Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
        var row = (await _store.LoadAsync(hash, CancellationToken.None))!.Entry("Blink")!.Effects.Single();
        Assert.Equal(("d3", EffectKind.Place, ConditionKind.AtPoint), (row.AmountDice, row.Kind, row.Conditions.Single().Kind));
        await pg.DeletePropertiesAsync(hash);
    }
```

In `MigrationTests`, add: `Assert.Contains("amount_dice", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='effect'"));`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~ArmyRuleHookTests|FullyQualifiedName~PropertySetDocumentTests"`
Expected: compile errors (`ConditionKind.AtPoint`, `EffectKind.Place`, `AmountDice`, `LiveSet.AtPoint`, `GfNames.AfterShooting` do not exist).

- [ ] **Step 3: The migration**

Create `server/management/Persistence/Migrations/006_army_rule_hooks.sql`:

```sql
-- 006_army_rule_hooks.sql: the place kind and its dice, add_hit adding attacks, and the at_point predicate
-- (docs/superpowers/specs/2026-10-02-army-rule-hooks-design.md).
alter table management.effect drop constraint effect_kind_check;
alter table management.effect add constraint effect_kind_check check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
    'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers',
    'count_as_terrain', 'take_test', 'set_condition', 'move', 'place'));
alter table management.effect drop constraint effect_target_quantity_check;
alter table management.effect add constraint effect_target_quantity_check check (target_quantity in ('hits', 'wounds', 'attacks'));
alter table management.effect add column amount_dice text check (amount_dice in ('d3'));

alter table management.effect_condition drop constraint effect_condition_kind_check;
alter table management.effect_condition add constraint effect_condition_kind_check check (kind in ('action_taken', 'attack_kind', 'range', 'roll_result',
    'actor_state', 'position_terrain', 'round_number', 'usage_count', 'holds_property', 'most_models', 'at_point'));
```

Apply it: `docker compose up -d`, then `./scripts/management` until its log shows `006_army_rule_hooks.sql`.

- [ ] **Step 4: The enum values, the field, the validation**

In `PropertySet.cs`: append `Place` to `EffectKind`, `Attacks` to `TargetQuantity`, `AtPoint` to `ConditionKind`, and `string? AmountDice = null` after `TargetCondition` on `Effect`. In `PropertySetDocument.cs`: the same trailing parameter on `EffectDocument`, passed as `f.AmountDice` at the end of `ToSet`'s `Build`.

In `PropertiesJson`, give `ValidateEffect` an own-roll parameter and the new checks:

```csharp
    static readonly string[] Points = ["activation-start", "before-attacking", "after-shooting", "after-melee", "attack-start"];

    static void ValidateEffect(PropertySetDocument d, HashSet<string> names, string entry, EffectDocument f, List<string> problems, string? ownRoll = null)
    {
        // ...existing checks, with the roll_result check admitting the own roll:
        //     if (!d.Rolls.Contains(c.Values[0]) && c.Values[0] != ownRoll) problems.Add(At($"roll_result roll '{c.Values[0]}' is not declared"));
        if (f.AmountDice is not null && f.Kind != EffectKind.Place) problems.Add(At("amount_dice is place only"));
        if (f.AmountDice is not null and not "d3") problems.Add(At($"amount_dice is 'd3', not '{f.AmountDice}'"));
        if (f.Kind == EffectKind.AddHit && f.TargetQuantity == TargetQuantity.Attacks && !f.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            problems.Add(At("add_hit attacks is per die: it needs a roll_result condition"));
        foreach (var c in f.Conditions.Where(c => c.Kind == ConditionKind.AtPoint))
        {
            if (c.Subject is not null) problems.Add(At("at_point takes no subject"));
            foreach (var v in c.Values.Where(v => !Points.Contains(v))) problems.Add(At($"no point '{v}'"));
        }
        foreach (var c in f.Conditions.Where(c => c.Kind == ConditionKind.UsageCount && c.Values.Count > 0))
            if (c.Values[0] is not "round" and not "activation") problems.Add(At($"usage_count scope is round or activation, not '{c.Values[0]}'"));
        // A roll's children may read the roll they are children of: a property's own roll is a RollRecord named for the entry.
        foreach (var child in f.Children) ValidateEffect(d, names, entry, child, problems, f.Kind == EffectKind.Roll ? entry : null);
    }
```

The existing `foreach (var child in f.Children) ValidateEffect(...)` line is replaced by the last line above. Add `ConditionKind.AtPoint` to the list of kinds that take no subject alongside `Range`, `RoundNumber`, `UsageCount`.

- [ ] **Step 5: The store**

In `PostgresPropertiesStore.LoadAsync`, the effect query gains `, amount_dice` (index 22) and the row lambda reads `var amountDice = NullableString(r, 22);`, passed last to `new Effect(...)`. In `SeedAsync`, the effect insert gains `, amount_dice` and `$24`, passing `f.AmountDice`.

- [ ] **Step 6: The point, and the usage scope**

In `server/game/Effects/Interaction.cs`, append to the record: `string? Point = null, IReadOnlyDictionary<Side, IReadOnlyDictionary<string, int>>? Faces = null`. `Faces` is read in Task 6.

In `GfNames`: `public const string ActivationStart = "activation-start", AfterShooting = "after-shooting", AfterMelee = "after-melee", AttackStart = "attack-start", RepositionSeq = "reposition";`.

In `server/game/Effects/Conditions.cs`:

```csharp
            case ConditionKind.AtPoint: return ix.Point is { } point && Membership(c, point);
            case ConditionKind.UsageCount: return c.Compare(Usage(ix, source, c));
```

and replace `Usage`:

```csharp
    /// <summary>
    /// How many times the rule has been used. A weapon rule with no scope counts the sequences that used the weapon (Limited, "once per game").
    /// A scope of `round` or `activation` counts the sequences this rule started for its holder's unit (its name is their Action) since the
    /// last round_started, or since the unit's activation began (its last action_recorded) (army-rule hooks spec).
    /// </summary>
    static int Usage(Interaction ix, LiveProperty source, EffectCondition c)
    {
        var log = EventApplier.EffectiveLog(ix.State.Applier.Log).ToList();
        if (c.Values.FirstOrDefault() is "round" or "activation")
        {
            var unit = ix.Of(source.Holder).UnitId;
            var from = c.Values[0] == "round" ? log.FindLastIndex(e => e.Payload is RoundStarted) : log.FindLastIndex(e => e.Payload is ActionRecorded a && a.UnitId == unit);
            return log.Skip(Math.Max(0, from)).Count(e => e.Payload is SequenceStarted s && s.Context.Action == source.Entry.Name && s.Context.ActorUnitId == unit);
        }
        return source.WeaponId is not { } w ? 0 : log.Count(e => e.Payload is SequenceStarted s && s.Context.WeaponIds.Contains(w));
    }
```

In `LiveSet`, add:

```csharp
    /// <summary>The rows acting on a side that are offered at a point: an at_point row naming it, whose other conditions hold now. The live set's
    /// interaction must carry that Point, or no at_point condition holds.</summary>
    public IEnumerable<(LiveProperty Source, Effect Effect)> AtPoint(Side side, string point) =>
        Live.SelectMany(p => p.Effects.Where(e => p.ActedOn(e) == side && e.Conditions.Any(c => c.Kind == ConditionKind.AtPoint && c.Values.Contains(point))
                && Conditions.Hold(Interaction, p.Holder, e, p, null)).Select(e => (p, e)));
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Management.Tests --filter FullyQualifiedName~MigrationTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add server/management/Persistence/Migrations/006_army_rule_hooks.sql server/game tests/Tabletop.GameServer.Tests tests/Tabletop.Management.Tests/MigrationTests.cs
git commit -m "Add at_point, usage scopes, the place kind and add_hit attacks to the grammar"
```
