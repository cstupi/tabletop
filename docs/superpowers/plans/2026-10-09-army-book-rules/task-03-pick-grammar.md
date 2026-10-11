# Task 3: Pick entries in the grammar

**Files:**
- Modify: `server/game/Properties/PropertySet.cs` (`SpellTargeting`)
- Modify: `server/game/Properties/PropertySetDocument.cs` (`Parse`, `Validate`)
- Modify: `server/game/Properties/PostgresPropertiesStore.cs` (`target_line_of_sight`)
- Modify: `server/game/Effects/LiveProperties.cs` (`LiveProperty.Effects`)
- Modify: `server/game/Effects/Conditions.cs` (`AtPoint`)
- Create: `server/management/Persistence/Migrations/009_target_line_of_sight.sql`
- Modify: `docs/design/interpreter/effect-grammar.md` (*The grants store*), `docs/schema.md`
- Test: `tests/Tabletop.GameServer.Tests/PickGrammarTests.cs` (new), `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`

**Interfaces:**
- Consumes: `EntryDocument.Played` (Task 1).
- Produces: `SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range, bool? LineOfSight = null)` (null:
  the target must be in line of sight; `Parse` turns `true` into null); a *pick entry*: kind `model`, `targeting` with
  count 1, every row `duration: once` with an `at_point` condition naming exactly `before-attacking`. A carried pick
  entry exposes no rows; a granted row's `at_point` holds.

Spec *Buff, Mark, Debuff: the pick*: "A `model` entry with `targeting` (side, range, line of sight, an optional required
property), an `at_point` of `before-attacking`, a `usage_count` of one per `activation`, and its effect rows with
duration `once`. `PropertiesJson.Validate` … admits `targeting` and `once` rows on a `model` entry that has a
`before-attacking` `at_point`; today only spells may carry them."

Two readings this task settles, both forced by the existing grant reading:

- The `at_point` sits on each `once` row (a condition is a row's), and the grant names that row (`Grant.EffectId`). On
  the picked unit the row is read in attacks, where no point is set, so a granted row's `at_point` holds: it said when
  the rule is used, and the grant is that use.
- The per-model, per-activation use is counted by the mechanism (Task 4, `GfPick.Uses`), not by a `usage_count` row:
  `usage_count` counts per unit (`Conditions.Usage`), and the spec gives each carrying model its own use.

The optional required property (Casting Buff's *"with Caster"*) is batch 2's; nothing in this plan picks by it.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/PickGrammarTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class PickGrammarTests
{
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.BeforeAttacking]);
    static EffectDocument Grant(string entry, EffectSubject subject, EffectDuration duration = EffectDuration.Once, params ConditionDocument[] when) =>
        new(subject, 5, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, duration, when, []);
    static EntryDocument Buff(string name = "Test Buff", params EffectDocument[] rows) =>
        new(name, CatalogKind.Model, false, Aggregation.None, null, null, "Test.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, LineOfSight: false),
            rows.Length == 0 ? [Grant("Stealth", EffectSubject.Self, EffectDuration.Once, BeforeAttacking)] : rows);
    static IReadOnlyList<string> Problems(params EntryDocument[] entries) =>
        PropertiesJson.Validate(TestProperties.CoreDocument with { Entries = [.. TestProperties.CoreDocument.Entries, .. entries] });

    [Fact]
    public void Line_of_sight_false_is_read_and_true_reads_as_omitted()
    {
        const string json = """
        { "name": "D", "gameRules": "grimdark-future", "entries": [
          { "name": "A", "kind": "model", "aggregation": "none", "ruleText": "A.", "targeting": { "side": "friendly", "grain": "unit", "count": 1, "range": 12, "lineOfSight": false } },
          { "name": "B", "kind": "model", "aggregation": "none", "ruleText": "B.", "targeting": { "side": "enemy", "grain": "unit", "count": 1, "range": 18, "lineOfSight": true } } ] }
        """;
        var doc = PropertiesJson.Parse(json);
        Assert.False(doc.Entries[0].Targeting!.LineOfSight);
        Assert.Null(doc.Entries[1].Targeting!.LineOfSight);
        Assert.DoesNotContain("lineOfSight", PropertiesJson.EntryCanonical(doc.Entries[1]));
    }

    [Fact]
    public void A_pick_entry_is_admitted()
    {
        Assert.Empty(Problems(Buff()));
    }

    [Fact]
    public void A_pick_entry_is_a_model_entry_of_once_rows_at_before_attacking_that_picks_one()
    {
        Assert.Contains("Unit Pick: targeting is set on a spell or a pick entry (kind model)", Problems(Buff("Unit Pick") with { Kind = CatalogKind.Unit, Aggregation = null }));
        Assert.Contains("No Point: a pick entry's row has an at_point naming before-attacking", Problems(Buff("No Point", Grant("Stealth", EffectSubject.Self))));
        Assert.Contains("Lasting: a pick entry's rows have duration once", Problems(Buff("Lasting", Grant("Stealth", EffectSubject.Self, EffectDuration.Interaction, BeforeAttacking))));
        Assert.Contains("Two: a pick entry picks one", Problems(Buff("Two") with { Targeting = new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 12) }));
    }

    [Fact]
    public void A_once_row_off_a_spell_or_pick_entry_is_refused_and_a_spell_is_always_in_sight()
    {
        var loose = new EntryDocument("Loose", CatalogKind.Model, false, Aggregation.All, null, null, "Loose.", null, [Grant("Stealth", EffectSubject.Self)]);
        Assert.Contains("Loose: a once row belongs to a spell or a pick entry", Problems(loose));
        var blind = new EntryDocument("Blind", CatalogKind.Spell, false, null, 1, null, "Blind.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12, LineOfSight: false),
            [Grant("Stealth", EffectSubject.Self)]);
        Assert.Contains("Blind: a spell's target is in line of sight (p.13)", PropertiesJson.Validate(TestProperties.CoreDocument with
        {
            Entries = [.. TestProperties.CoreDocument.Entries, blind], Books = [.. TestProperties.CoreDocument.Books ?? [], new BookRecord("blind-book", "Blind", null, ["Blind"])],
        }));
    }

    [Fact]
    public void A_carried_pick_entry_exposes_no_rows()
    {
        var props = TestProperties.WithSpells(Buff());
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], GfNames.BeforeAttacking));
        Assert.False(live.Has(Side.Attacker, "Stealth"));
        Assert.Empty(live.AtPoint(Side.Attacker, GfNames.BeforeAttacking));
    }

    [Fact]
    public void A_granted_rows_at_point_holds_wherever_the_grant_is_read()
    {
        var props = TestProperties.WithSpells(Buff());
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a, b], [Unit("C", "4+", "4+", [], 1, [])]);
        var row = props.Entry("Test Buff")!.Effects.Single();
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new GrantAdded(b.Id, new Grant(Guid.CreateVersion7(), row.Id, "Test Buff", a.Id, 1)), null));
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, b.Id), null, null, null, null, []));
        Assert.True(live.Has(Side.Attacker, "Stealth"));
    }
}
```

In `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`, add:

```csharp
    [Fact]
    public async Task Line_of_sight_round_trips_through_the_catalog_entry_table()
    {
        var name = "Sight " + Guid.CreateVersion7().ToString("N")[24..32];
        var at = new ConditionDocument(ConditionKind.AtPoint, null, Comparison.Eq, null, ["before-attacking"]);
        var row = new EffectDocument(EffectSubject.Self, 5, EffectKind.ModifyRoll, "hit", null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Once, [at], []);
        var doc = new PropertySetDocument(name, "sight-" + name, [], [], ["hit"],
            [new EntryDocument("Picker", CatalogKind.Model, false, Aggregation.None, null, null, "Pick.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, LineOfSight: false), [row])]);
        var hash = PropertiesJson.Hash(doc);
        try
        {
            Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
            Assert.Equal(new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, false), (await _store.LoadAsync(hash, CancellationToken.None))!.Entry("Picker")!.Targeting);
        }
        finally { await pg.DeletePropertiesAsync(hash); }
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~PickGrammarTests"`
Expected: build fails with `CS1739: … does not have a parameter named 'LineOfSight'`.

- [ ] **Step 3: `LineOfSight` on the targeting**

In `server/game/Properties/PropertySet.cs`, replace the `SpellTargeting` record:

```csharp
/// <summary>Who a spell or a pick entry may target. LineOfSight null: the target must be in sight (p.13, and a Mark's or Debuff's "in line of
/// sight"); false: range alone (a Buff's "pick one friendly unit within 12"").</summary>
public sealed record SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range, bool? LineOfSight = null);
```

In `PropertiesJson.Parse`, extend the entry projection written in Task 1:

```csharp
            Entries = (doc.Entries ?? []).Select(e => e with
            {
                Effects = Fix(e.Effects), Played = e.Played == false ? false : null,
                Targeting = e.Targeting is { LineOfSight: true } t ? t with { LineOfSight = null } : e.Targeting,
            }).ToList(),
```

- [ ] **Step 4: Validate pick entries**

In `PropertiesJson.Validate`, inside `foreach (var e in d.Entries)`, after the spell-effect check:

```csharp
            if (spell && e.Targeting?.LineOfSight == false) problems.Add($"{e.Name}: a spell's target is in line of sight (p.13)");
            // A pick entry (army-book rules spec → the pick): a model entry whose once rows are granted to the unit it picks before attacking.
            if (!spell && e.Targeting is { } pick)
            {
                if (e.Kind != CatalogKind.Model) problems.Add($"{e.Name}: targeting is set on a spell or a pick entry (kind model)");
                if (pick.Count != 1) problems.Add($"{e.Name}: a pick entry picks one");
                if (e.Effects.Count == 0 || e.Effects.Any(f => f.Duration != EffectDuration.Once)) problems.Add($"{e.Name}: a pick entry's rows have duration once");
                if (e.Effects.Any(f => !f.Conditions.Any(c => c.Kind == ConditionKind.AtPoint && c.Values is [PickPoint])))
                    problems.Add($"{e.Name}: a pick entry's row has an at_point naming {PickPoint}");
            }
            if (!spell && e.Targeting is null && e.Effects.Any(f => f.Duration == EffectDuration.Once)) problems.Add($"{e.Name}: a once row belongs to a spell or a pick entry");
```

and next to `Points`:

```csharp
    /// <summary>The point a pick entry is used at (army-book rules spec: "Once per activation, before attacking, pick …").</summary>
    const string PickPoint = "before-attacking";
```

- [ ] **Step 5: A carried pick entry exposes no rows; a granted row's point holds**

In `server/game/Effects/LiveProperties.cs`, replace `LiveProperty`'s summary and `Effects`:

```csharp
/// <summary>One instance of a catalog entry in play: who holds it and where it came from. A granted instance (spell library → A grant is its
/// effect row) exposes only the row its grant holds, and that row's children. A carried pick entry (one with targeting) exposes none: its once
/// rows act only through the grants its pick writes (army-book rules spec → the pick). Every other instance exposes its entry's effects.</summary>
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId, Effect? GrantRow = null, IReadOnlyList<Effect>? Chosen = null)
{
    public IReadOnlyList<Effect> Effects => Chosen ?? (GrantRow is { } row ? [row] : Entry.Targeting is not null ? [] : Entry.Effects);
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}
```

A spell is never a carried instance (spells reach a live set only as grants or as their weapon rules), so the
`Targeting` test touches pick entries alone.

In `server/game/Effects/Conditions.cs`, replace the `AtPoint` case:

```csharp
            // A grant row's at_point said when its rule was used; the grant is that use, so wherever the grant is read the point has passed
            // (army-book rules spec → the pick). Any other row holds only at its point.
            case ConditionKind.AtPoint: return source.GrantRow is not null || (ix.Point is { } point && Membership(c, point));
```

- [ ] **Step 6: Run the grammar tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~PickGrammarTests|FullyQualifiedName~ArmyRuleHookTests|FullyQualifiedName~GrantRowTests|FullyQualifiedName~SpellLibraryTests"`
Expected: PASS.

- [ ] **Step 7: The migration and the store**

Create `server/management/Persistence/Migrations/009_target_line_of_sight.sql`:

```sql
-- 009_target_line_of_sight.sql: a pick entry may target out of sight; null is in sight (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → the pick).
alter table management.catalog_entry add column target_line_of_sight boolean check (target_line_of_sight is null or target_side is not null);
```

In `PostgresPropertiesStore.LoadAsync`, the entries query selects `target_line_of_sight` as column 12 and the targeting is:

```csharp
        var entries = await Rows(conn, "select name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range, played, target_line_of_sight from management.catalog_entry where properties_hash = $1 order by name", hash, ct,
            r => new CatalogEntry(r.GetString(0), EnumFrom<CatalogKind>(r.GetString(1)), r.GetBoolean(2), r.IsDBNull(3) ? null : EnumFrom<Aggregation>(r.GetString(3)), r.IsDBNull(4) ? null : r.GetInt32(4), NullableString(r, 5), r.GetString(6),
                r.IsDBNull(7) ? null : new SpellTargeting(EnumFrom<TargetSide>(r.GetString(7)), EnumFrom<TargetGrain>(r.GetString(8)), r.GetInt32(9), r.GetDouble(10), r.IsDBNull(12) ? null : r.GetBoolean(12)),
                effectRows.Where(x => x.Entry == r.GetString(0) && x.Parent is null).Select(x => x.Build(Children(x.Row))).ToList(), r.GetBoolean(11)));
```

In `SeedAsync`, the insert gains `target_line_of_sight` as `$14`:

```csharp
        foreach (var e in set.Entries)
            await Run("insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range, played, target_line_of_sight) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)",
                set.ContentHash, e.Name, EnumText(e.Kind), e.TakesParameter, e.Aggregation is { } ag ? EnumText(ag) : null, e.Cost, e.Faction, e.RuleText,
                e.Targeting is { } t ? EnumText(t.Side) : null, e.Targeting is { } t2 ? EnumText(t2.Grain) : null, e.Targeting?.Count, e.Targeting is { } t3 ? (double?)t3.Range : null, e.Played,
                e.Targeting?.LineOfSight);
```

- [ ] **Step 8: Run the store tests**

Run: `docker compose restart management && sleep 15 && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~PropertiesStoreTests"`
Expected: PASS.

- [ ] **Step 9: Documents**

In `docs/design/interpreter/effect-grammar.md`, at the end of *The grants store* (before *Name checks handled by
`GameRules`*), add:

```markdown
**A pick entry writes grants too.** A `model` entry with `targeting` (count one, an optional `lineOfSight: false`) whose
rows are all `once` rows with `at_point before-attacking` is an army-book Buff, Mark or Debuff. Carried, it exposes no
rows; the `pick-rule` sequence writes one `GrantAdded` per row on the unit it picks, and the grant is read and consumed
as a spell's is. A granted row's `at_point` holds wherever the grant is read: it said when the rule was used. Each
carrying model has one use per activation, counted by `GfPick.Uses`.
```

In `docs/schema.md`, in the entity diagram's `CATALOG_ENTRY` block add `bool target_line_of_sight` after `bool played`,
and replace the paragraph under *The catalog's per-kind check*:

```markdown
`cost` is non-null only when `kind = spell`; `target_side`, `target_grain`, `target_count` and `target_range` are non-null
on a spell and on a pick entry (a `model` entry an army-book Buff, Mark or Debuff writes); `target_line_of_sight` is null
(in sight) or false (range alone, a Buff), and set only with a target; `aggregation` only when `kind = model`;
`takes_parameter` is false for a spell. `PropertiesJson.Validate` holds a pick entry to count one and `once` rows at
`before-attacking`.
```

- [ ] **Step 10: Run the game server suite**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add server/game/Properties/PropertySet.cs server/game/Properties/PropertySetDocument.cs server/game/Properties/PostgresPropertiesStore.cs \
  server/game/Effects/LiveProperties.cs server/game/Effects/Conditions.cs server/management/Persistence/Migrations/009_target_line_of_sight.sql \
  docs/design/interpreter/effect-grammar.md docs/schema.md tests/Tabletop.GameServer.Tests/PickGrammarTests.cs tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs
git commit -m "Admit pick entries: targeting and once rows on a model entry used before attacking"
```
