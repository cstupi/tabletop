# Task 5: A grant is its effect row

**Files:**
- Modify: `server/game/Effects/LiveProperties.cs` (`LiveProperty.GrantRow`, `Effects`; `Gather` reads a grant as its row; pass one carries the grant id)
- Modify: `server/game/Effects/Lookup.cs` (`UnitProperties` resolves a grant through its row)
- Modify: `server/game/Effects/RollEffects.cs` (`Plan` reads `p.Effects`)
- Modify: `server/game/Rules/GrimdarkFuture/GfCasting.cs` (`EffectsStep` writes the spell's name, and does not stack)
- Create: `tests/Tabletop.GameServer.Tests/GrantRowTests.cs`
- Modify: `tests/Tabletop.GameServer.Tests/GfCastingTests.cs` (the `Ward` test's `EntryName`)

**Interfaces:**
- Consumes: `Grant(Guid Id, Guid EffectId, string EntryName, Guid CasterUnitId, int RemainingUses)` (Protocol, unchanged); `PropertySet.AllEffects()`; `Teams.Of(GameState, Guid)` (`server/game/Interpreter/StepContext.cs`).
- Produces: `LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId, Effect? GrantRow = null)` with `IReadOnlyList<Effect> Effects`; `Grant.EntryName` holds the granting spell's name from here on.

Spec → *A grant is its effect row*. Three corpus shapes break on today's reading: a conditional grant loses its condition, an *against* grant gives the enemy the rule, and a three-row spell makes all three rows live three times. A grant now exposes exactly its row. A subject-`opponent` row reaches only an opponent on the caster's team.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/GrantRowTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GrantRowTests
{
    static ConditionDocument Shooting => new(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"]);
    static EffectDocument Row(EffectKind kind, EffectSubject subject, string? entry = null, string? action = null, int? amount = null, params ConditionDocument[] when) =>
        new(subject, 10, kind, null, null, entry, action, null, null, null, amount, false, null, null, null, EffectDuration.Once, when, []);
    static EntryDocument Spell(string name, TargetSide side, params EffectDocument[] rows) =>
        new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(side, TargetGrain.Unit, 1, 18), rows);

    static readonly EntryDocument RendingShots = Spell("Rending Shots", TargetSide.Friendly, Row(EffectKind.GrantProperty, EffectSubject.Self, "Rending", when: Shooting));
    static readonly EntryDocument RelentlessAgainst = Spell("Marked", TargetSide.Enemy, Row(EffectKind.GrantProperty, EffectSubject.Opponent, "Relentless"));
    static readonly EntryDocument ShockSpeed = Spell("Shock Speed", TargetSide.Friendly,
        Row(EffectKind.ModifyStat, EffectSubject.Self, action: "Advance", amount: 2), Row(EffectKind.ModifyStat, EffectSubject.Self, action: "Rush", amount: 4), Row(EffectKind.ModifyStat, EffectSubject.Self, action: "Charge", amount: 4));
    static readonly PropertySet Props = TestProperties.WithSpells(RendingShots, RelentlessAgainst, ShockSpeed);

    /// <summary>Every once-row of the spell as a grant on the unit, the shape the effects step writes.</summary>
    static void Grant(GameState state, Guid unit, string spell, Guid caster)
    {
        foreach (var row in Props.Entry(spell)!.Effects.Where(e => e.Duration == EffectDuration.Once))
            state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new GrantAdded(unit, new Tabletop.Protocol.Grant(Guid.CreateVersion7(), row.Id, spell, caster, 1)), null));
    }

    static LiveSet Attack(GameState state, Guid attacker, Guid defender, AttackKind kind) =>
        LiveProperties.Settle(new Interaction(state, Props, Party.Whole(state, attacker), Party.Whole(state, defender), kind, null, 12, []));

    [Fact]
    public void A_conditional_grant_keeps_its_condition()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        Grant(state, a.Id, "Rending Shots", a.Id);
        Assert.True(Attack(state, a.Id, b.Id, AttackKind.Shooting).Has(Side.Attacker, "Rending"));
        Assert.False(Attack(state, a.Id, b.Id, AttackKind.Melee).Has(Side.Attacker, "Rending"));
    }

    [Fact]
    public void An_against_grant_gives_the_casters_side_the_rule_and_the_marked_unit_nothing()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        Grant(state, b.Id, "Marked", a.Id);
        Assert.True(Attack(state, a.Id, b.Id, AttackKind.Shooting).Has(Side.Attacker, "Relentless"));
        Assert.False(Attack(state, b.Id, a.Id, AttackKind.Shooting).Has(Side.Attacker, "Relentless"));
        Assert.False(Lookup.UnitHas(state, Props, b.Id, "Relentless"));
    }

    [Fact]
    public void An_against_grant_reaches_no_opponent_off_the_casters_team()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []); var c = Unit("C", "4+", "4+", [], 1, []);
        var state = State([a], [b, c]);
        Grant(state, b.Id, "Marked", c.Id);                    // written by a caster on the marked unit's own team
        Assert.False(Attack(state, a.Id, b.Id, AttackKind.Shooting).Has(Side.Attacker, "Relentless"));
    }

    [Fact]
    public void A_three_row_spell_moves_two_inches_more_on_advance_not_six()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var state = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        Grant(state, a.Id, "Shock Speed", a.Id);
        var live = LiveProperties.Settle(new Interaction(state, Props, Party.Whole(state, a.Id), null, null, "Advance", null, []));
        Assert.Equal(8, StatReader.ActionDistance(live, Side.Attacker, "Advance"));
        Assert.Equal(16, StatReader.ActionDistance(live, Side.Attacker, "Rush"));
    }

    [Fact]
    public void A_second_cast_of_the_same_spell_on_the_same_unit_writes_no_grant()
    {
        // Two casters, because one caster tries a spell once per activation (GfCasting.TriedPicks).
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var adept = Unit("Adept", "4+", "4+", [], 1, [P("Caster", 6)]);
        var sim = new Sim(State([wizard, adept], [Unit("B", "4+", "4+", [], 1, [])], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(6, 6));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6), new SpellTokensSet(adept.Models[0].Id, 6)]);
        foreach (var caster in new[] { wizard, adept })
        {
            sim.Start(GfNames.CastingSeq, new SequenceContext(caster.Id, [], [], null));
            sim.Answer(Samples.Team1, new Chosen($"Shock Speed|{caster.Models[0].Id}"));
            sim.Answer(Samples.Team1, new Passed());
            sim.Answer(Samples.Team1, new SelectedPieces([wizard.Id]));
        }
        Assert.Equal(3, sim.Unit(wizard.Id).Grants.Count);
        Assert.All(sim.Unit(wizard.Id).Grants, g => Assert.Equal("Shock Speed", g.EntryName));
    }
}

```

In `GfCastingTests.Ward_grants_regeneration_once_to_two_units_and_the_grant_is_consumed_when_it_applies`, line 192, the expected `EntryName` becomes `"Ward"`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GrantRowTests|FullyQualifiedName~GfCastingTests"`
Expected: FAIL. The conditional grant is live in melee; the marked unit holds `Relentless`; Advance reads 12 rather than 8; the second cast writes three more grants; `Ward`'s grant reads `Regeneration`.

- [ ] **Step 3: A live instance carries its row**

In `server/game/Effects/LiveProperties.cs`, replace `LiveProperty`:

```csharp
/// <summary>One instance of a catalog entry in play: who holds it and where it came from. A granted instance (spell library → A grant is its
/// effect row) exposes only the row its grant holds, and that row's children; every other instance exposes its entry's effects.</summary>
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId, Effect? GrantRow = null)
{
    public IReadOnlyList<Effect> Effects => GrantRow is { } row ? [row] : Entry.Effects;
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}
```

In `LiveSet.Effects`, replace `p.Entry.Effects` with `p.Effects`. In `LiveProperties.Settle`, the candidates read `p.Effects`, and a grant pass one adds keeps the source's grant id:

```csharp
        var candidates = live.SelectMany(p => p.Effects.Where(e => PassOne.Contains(e.Kind) && e.Conditions.All(c => c.Kind != ConditionKind.RollResult)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).ToList();
```

```csharp
                case EffectKind.GrantProperty when ix.Properties.Entry(effect.TargetEntry!) is { } granted:
                    live.Add(new LiveProperty(side, granted, effect.Amount, source.ModelId, source.WeaponId, source.GrantId)); break;
```

In `Gather`, replace the grants loop:

```csharp
        // A grant is its row (spell library spec): the spell entry it names, exposing only that row. A subject-opponent row ("friendly units get
        // X against") reaches only an opponent on the caster's team, which is p.13's "friendly" read against the grant.
        foreach (var g in unit.Grants)
        {
            if (ix.Properties.Entry(g.EntryName) is not { } spell || ix.Properties.AllEffects().FirstOrDefault(x => x.Id == g.EffectId) is not { } row) continue;
            if (row.Subject == EffectSubject.Opponent && ix.Defender is not null
                && Interpreter.Teams.Of(state, ix.Of(Interaction.Other(side)).UnitId) != Interpreter.Teams.Of(state, g.CasterUnitId)) continue;
            result.Add(new LiveProperty(side, spell, null, null, null, g.Id, row));
        }
```

In `server/game/Effects/RollEffects.cs`, `Plan`'s force-reroll scan reads `p.Effects` in place of `p.Entry.Effects`.

- [ ] **Step 4: A name check reads a grant through its row**

In `server/game/Effects/Lookup.cs`, replace `UnitProperties`:

```csharp
    /// <summary>The unit's own properties plus the rules its grants give it: a grant whose row is a subject-self grant_property names a rule the
    /// unit holds, with the row's amount as its parameter ("gets AP(1)" → AP 1). Any other grant gives the unit no rule by name.</summary>
    public static IEnumerable<PropertyInstance> UnitProperties(GameState state, PropertySet props, Guid unitId)
    {
        var unit = state.Projection.Units[unitId];
        var granted = unit.Grants.Select(g => props.AllEffects().FirstOrDefault(e => e.Id == g.EffectId))
            .Where(r => r is { Kind: EffectKind.GrantProperty, Subject: EffectSubject.Self, TargetEntry: not null })
            .Select(r => new PropertyInstance(r!.TargetEntry!, r.Amount));
        return unit.Definition.Properties.Concat(granted);
    }
```

- [ ] **Step 5: The effects step writes the spell's name and does not stack**

In `GfCasting.EffectsStep.Run`, replace the grant-writing statement:

```csharp
            // p.13: "Effects from multiple instances of the same special rule or spell don't stack": a target already holding this spell's grants gets none.
            var already = ctx.Projection.Units[targetUnit].Grants.Any(g => g.EntryName == spell.Name);
            // A once-row's conditions say when the grant applies ("Shred when shooting"), not whether it is written: every once-row is granted.
            var changes = already ? [] : spell.Effects.Where(e => e.Duration == EffectDuration.Once)
                .Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, spell.Name, casterUnit, 1))).ToList();
```

Today the step writes only the once-rows in `holding`, the rows whose conditions hold between the caster and the target at the cast; a row conditioned on `attack_kind shooting` never holds there, so *Blessed Ammo* would grant nothing.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GrantRowTests|FullyQualifiedName~GfCastingTests|FullyQualifiedName~LivePropertiesTests|FullyQualifiedName~RollEffectsTests|FullyQualifiedName~GfDealWoundsTests"`
Expected: PASS. `Ward`'s Regeneration is still consumed when it rolls: pass one adds `Regeneration` carrying the grant's id, which is what the assign step's `regen.Source?.GrantId` reads.

- [ ] **Step 7: Commit**

```bash
git add server/game/Effects server/game/Rules/GrimdarkFuture/GfCasting.cs tests/Tabletop.GameServer.Tests
git commit -m "Read a grant as the one effect row it holds"
```
