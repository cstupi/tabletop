# Task 5: A carried Aura's rule by name

**Files:**
- Modify: `server/game/Effects/Lookup.cs` (`UnitHas`)
- Test: `tests/Tabletop.GameServer.Tests/AuraByNameTests.cs` (new)

**Interfaces:**
- Consumes: `CatalogEntry.Played` (Task 1), `CatalogEntry.Targeting` (Task 3).
- Produces: `Lookup.UnitHas(state, props, unit, X)` is true when one of the unit's slots carries an Aura: a played
  `model` entry with aggregation `any`, no targeting, and an unconditional subject-self `grant_property` row naming X.

Spec *Aura*: "A `model` entry, aggregation `any`, one `grant_property` row of X, subject `self`. `Gather` emits an `any`
entry as one unit-level instance when any model carries it, so the unit holds X: *"this model and its unit"*." The live
set holds X that way. A rule the ruleset reads by name instead (`GfRules.Deployment`'s `Scout` and `Ambush`,
`GfRules.Scheduling`'s `Aircraft` and `Artillery`) reads `Lookup.UnitHas`, which reads the unit's properties, its slots
under aggregation and its grants store, never a carried grant row: `Scout Aura` and `Ambush Aura`, both generated, would
do nothing. `UnitHas` reads a carried Aura's grant off the definition, as it reads a unit-kind rule on a slot
(`UnitKindOnSlots`), because a deployment rule is read while the unit is shelved.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/AuraByNameTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class AuraByNameTests
{
    static EffectDocument Grant(string entry, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 5, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, when, []);
    static EntryDocument Aura(string name, string grants, Aggregation aggregation = Aggregation.Any, params ConditionDocument[] when) =>
        new(name, CatalogKind.Model, false, aggregation, null, null, "This model and its unit get it.", null, [Grant(grants, when)]);

    static readonly PropertySet Props = TestProperties.WithSpells(
        Aura("Test Scout Aura", "Scout"),
        Aura("Test Shooting Scout Aura", "Scout", Aggregation.Any, new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"])),
        Aura("Test All Scout", "Scout", Aggregation.All),
        new EntryDocument("Test Text Aura", CatalogKind.Model, false, Aggregation.Any, null, null, "This model and its unit get Scout.", null, [], Played: false));

    /// <summary>A three-model unit whose first model alone carries the rule.</summary>
    static Tabletop.Content.Unit OneCarrier(string rule)
    {
        var u = Unit("A", "4+", "4+", [], 3, []);
        return u with { Models = [u.Models[0] with { Properties = [P(rule)] }, .. u.Models.Skip(1)] };
    }

    [Fact]
    public void A_carried_aura_gives_its_unit_a_rule_read_by_name()
    {
        var a = OneCarrier("Test Scout Aura");
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        Assert.True(Lookup.UnitHas(s, Props, a.Id, "Scout"));
    }

    [Fact]
    public void A_conditioned_grant_an_all_entry_or_a_text_only_aura_gives_no_rule_by_name()
    {
        foreach (var rule in new[] { "Test Shooting Scout Aura", "Test All Scout", "Test Text Aura" })
        {
            var a = OneCarrier(rule);
            var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
            Assert.False(Lookup.UnitHas(s, Props, a.Id, "Scout"), rule);
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~AuraByNameTests"`
Expected: `A_carried_aura_gives_its_unit_a_rule_read_by_name` fails (`Assert.True() Failure`); the other passes.

- [ ] **Step 3: Read a carried Aura's grant**

In `server/game/Effects/Lookup.cs`, in `UnitHas`, replace the first line:

```csharp
        if (UnitProperties(state, props, unitId).Any(p => p.Name == entry) || AuraGrants(state, props, unitId).Contains(entry)) return true;
```

and add after `UnitKindOnSlots`:

```csharp
    /// <summary>
    /// The rules carried Auras give the unit (army-book rules spec → Aura: "This model and its unit get X"): a played model entry with aggregation
    /// any and no targeting, on any of the unit's slots, whose unconditional subject-self grant_property row names X. Read off the definition, as
    /// UnitKindOnSlots is, because a deployment rule (Scout, Ambush) is read before the unit has a position.
    /// </summary>
    static IEnumerable<string> AuraGrants(GameState state, PropertySet props, Guid unitId) =>
        state.Projection.Units[unitId].Definition.Models.SelectMany(m => m.Properties)
            .Select(p => props.Entry(p.Name)).OfType<CatalogEntry>()
            .Where(e => e is { Kind: CatalogKind.Model, Aggregation: Aggregation.Any, Played: true, Targeting: null })
            .SelectMany(e => e.Effects.Where(f => f is { Kind: EffectKind.GrantProperty, Subject: EffectSubject.Self, TargetEntry: not null } && f.Conditions.Count == 0))
            .Select(f => f.TargetEntry!);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~AuraByNameTests|FullyQualifiedName~DeploymentTests|FullyQualifiedName~ConditionsTests"`
Expected: PASS.

- [ ] **Step 5: Document the name check**

In `docs/design/interpreter/effect-grammar.md`, *Name checks handled by `GameRules`*, add at the end of the section:

```markdown
`Lookup.UnitHas` also reads a carried Aura (an `any` model entry whose unconditional subject-self `grant_property` row
names the rule) off the unit's definition, so *"This model and its unit get Scout"* is a Scout unit at deployment.
```

- [ ] **Step 6: Run the game server suite and commit**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS.

```bash
git add server/game/Effects/Lookup.cs tests/Tabletop.GameServer.Tests/AuraByNameTests.cs docs/design/interpreter/effect-grammar.md
git commit -m "Read a carried Aura's granted rule in name checks"
```
