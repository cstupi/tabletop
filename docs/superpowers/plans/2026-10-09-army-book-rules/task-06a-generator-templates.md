# Task 6a: The generator reads the books' rules and the templates

**Files:**
- Modify: `scripts/ArmyBooks/ArmyForgeBook.cs` (rule texts, units, upgrades)
- Create: `scripts/ArmyBooks/RuleTemplates.cs`, `scripts/ArmyBooks/RuleWriter.cs` (`NamesInScope`)
- Create: `tests/Tabletop.ArmyBooks.Tests/Fixtures/rules/books/rules-a.json`, `rules-b.json`,
  `tests/Tabletop.ArmyBooks.Tests/Fixtures/rules/conflict/rules-c.json`
- Test: `tests/Tabletop.ArmyBooks.Tests/RuleTemplatesTests.cs` (new)

**Interfaces:**
- Consumes: `EntryDocument.Played` (Task 1), `SpellTargeting.LineOfSight` (Task 3).
- Produces: `AfBook.SpecialRules/Units/UpgradePackages` and the records `AfRuleText`, `AfRuleRef`, `AfWeapon`, `AfGain`,
  `AfUnit`, `AfOption`, `AfSection`, `AfPackage`; `RulePattern`; `RuleTemplates.PatternOf/Base/Wrapper`;
  `RuleWriter.NamesInScope(AfBook)` (README → Fixed interfaces); the rule fixtures Task 6b's tests read.

Spec *Generator*: "**Names in scope.** Every rule name on a unit or an upgrade in the official books. … **A wrapper is
generated** when its name ends in Aura, Buff, Mark or Debuff, its description is that pattern's template with X equal
to the name less the suffix, and X is a catalog entry. The entry's rows point at X." *The four patterns* gives the
template texts and *Aura* and *Buff, Mark, Debuff: the pick* the entries.

*On a unit or an upgrade* is a unit's `rules`, its weapons' `specialRules`, its `items[].content` rules, and every
upgrade option's gains (an `ArmyBookRule`, an `ArmyBookWeapon`'s `specialRules`, an `ArmyBookItem`'s `content`): the
spec's batch 1 names `Ignores Cover` and `Ignores Regeneration`, which the books print only on weapons, and
`Predator Fighter`, which only an item's content carries. `NamesInScope` says, per name, whether any unit or upgrade
carries it as a rule (`true`) or only weapons do (`false`), which Task 6b turns into the text-only entry's kind.

- [ ] **Step 1: The fixtures**

Create `tests/Tabletop.ArmyBooks.Tests/Fixtures/rules/books/rules-a.json`:

```json
{ "uid": "rules-a", "name": "Rules A", "versionString": "1", "spells": [],
  "specialRules": [
    { "name": "Stealth Aura", "description": "This model and its unit get Stealth." },
    { "name": "Stealth Buff", "description": "Once per activation, before attacking, pick one friendly unit within 12\", which gets Stealth once (next time the effect would apply)." },
    { "name": "Rending Mark", "description": "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Rending against once (next time the effect would apply)." },
    { "name": "Fortified Aura", "description": "This model and its unit get Fortified." },
    { "name": "Precision Shooter Aura", "description": "This model and its unit get +1 to hit rolls when shooting." },
    { "name": "Defense Debuff", "description": "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which gets -1 to defense rolls once (next time the effect would apply)." },
    { "name": "Precise", "description": "Gets +1 to hit when attacking.\n" },
    { "name": "Ignores Cover", "description": "This weapon ignores cover." },
    { "name": "Bane when Shooting", "description": "This model gets Bane when shooting." }
  ],
  "units": [
    { "rules": [ { "name": "Bane when Shooting" } ],
      "weapons": [ { "specialRules": [ { "name": "Ignores Cover" }, { "name": "AP" } ] } ],
      "items": [ { "name": "Pole", "type": "ArmyBookItem", "content": [ { "name": "Stealth Aura", "type": "ArmyBookRule" } ] } ] }
  ],
  "upgradePackages": [ { "sections": [ { "options": [
    { "gains": [ { "name": "Stealth Buff", "type": "ArmyBookRule" } ] },
    { "gains": [ { "name": "Rending Mark", "type": "ArmyBookRule" }, { "name": "Banner", "type": "ArmyBookRule" } ] },
    { "gains": [ { "name": "Gun", "type": "ArmyBookWeapon", "specialRules": [ { "name": "Precise" } ] } ] },
    { "gains": [ { "name": "Kit", "type": "ArmyBookItem", "content": [
      { "name": "Fortified Aura", "type": "ArmyBookRule" }, { "name": "Precision Shooter Aura", "type": "ArmyBookRule" },
      { "name": "Defense Debuff", "type": "ArmyBookRule" }, { "name": "Precise", "type": "ArmyBookRule" } ] } ] }
  ] } ] } ]
}
```

Create `tests/Tabletop.ArmyBooks.Tests/Fixtures/rules/books/rules-b.json`:

```json
{ "uid": "rules-b", "name": "Rules B", "versionString": "1", "spells": [],
  "specialRules": [
    { "name": "Stealth Aura", "description": "This model and its unit get Stealth." },
    { "name": "Swift Buff", "description": "Once per activation, before attacking, pick one friendly unit within 12\", which gets Swift once (next time the effect would apply)." }
  ],
  "units": [ { "rules": [ { "name": "Stealth Aura" }, { "name": "Swift Buff" } ] } ],
  "upgradePackages": []
}
```

Create `tests/Tabletop.ArmyBooks.Tests/Fixtures/rules/conflict/rules-c.json`:

```json
{ "uid": "rules-c", "name": "Rules C", "versionString": "1", "spells": [],
  "specialRules": [ { "name": "Precise", "description": "Gets +1 to hit." } ],
  "units": [ { "rules": [ { "name": "Precise" } ] } ],
  "upgradePackages": []
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Tabletop.ArmyBooks.Tests/RuleTemplatesTests.cs`:

```csharp
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class RuleTemplatesTests
{
    internal static AfBook Read(string path) => ArmyForgeJson.ReadBook(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", path)));

    [Fact]
    public void Names_in_scope_are_unit_rules_weapon_rules_item_contents_and_upgrade_gains()
    {
        var names = RuleWriter.NamesInScope(Read("books/rules-a.json"));
        Assert.Equal(["AP", "Bane when Shooting", "Banner", "Defense Debuff", "Fortified Aura", "Ignores Cover", "Precise", "Precision Shooter Aura", "Rending Mark", "Stealth Aura", "Stealth Buff"],
            names.Keys.Order(StringComparer.Ordinal));
        Assert.False(names["Ignores Cover"]);      // only on a weapon
        Assert.True(names["Precise"]);             // on a weapon and as a rule: a model's
    }

    [Fact]
    public void A_book_without_units_or_rule_texts_reads_empty()
    {
        var book = ArmyForgeJson.ReadBook("""{ "uid": "x", "name": "X", "spells": [] }""");
        Assert.Empty(RuleWriter.NamesInScope(book));
        Assert.Null(book.SpecialRules);
    }

    [Fact]
    public void Pattern_of_reads_the_last_word_exactly()
    {
        Assert.Equal(RulePattern.Aura, RuleTemplates.PatternOf("Stealth Aura"));
        Assert.Equal(RulePattern.Debuff, RuleTemplates.PatternOf("Defense Debuff"));
        Assert.Null(RuleTemplates.PatternOf("Aura"));
        Assert.Null(RuleTemplates.PatternOf("Rule 1"));
        Assert.Null(RuleTemplates.PatternOf("Stealth aura"));
    }

    [Fact]
    public void Base_is_x_only_when_the_text_is_the_template()
    {
        Assert.Equal("Stealth", RuleTemplates.Base(RulePattern.Aura, "Stealth Aura", "This model and its unit get Stealth."));
        Assert.Null(RuleTemplates.Base(RulePattern.Aura, "Precision Shooter Aura", "This model and its unit get +1 to hit rolls when shooting."));
        Assert.Equal("Stealth", RuleTemplates.Base(RulePattern.Buff, "Stealth Buff",
            "Once per activation, before attacking, pick one friendly unit within 12\", which gets Stealth once (next time the effect would apply)."));
        Assert.Equal("Rending", RuleTemplates.Base(RulePattern.Mark, "Rending Mark",
            "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Rending against once (next time the effect would apply)."));
        Assert.Null(RuleTemplates.Base(RulePattern.Debuff, "Defense Debuff", "anything"));
    }

    [Fact]
    public void The_wrappers_are_an_aura_entry_and_two_pick_entries()
    {
        var aura = RuleTemplates.Wrapper(RulePattern.Aura, "Stealth Aura", "Stealth", "This model and its unit get Stealth.");
        Assert.Equal((CatalogKind.Model, (Aggregation?)Aggregation.Any, (SpellTargeting?)null), (aura.Kind, aura.Aggregation, aura.Targeting));
        Assert.Equal((EffectSubject.Self, EffectKind.GrantProperty, "Stealth", EffectDuration.Interaction, 0),
            (aura.Effects.Single().Subject, aura.Effects.Single().Kind, aura.Effects.Single().TargetEntry, aura.Effects.Single().Duration, aura.Effects.Single().Conditions.Count));
        var buff = RuleTemplates.Wrapper(RulePattern.Buff, "Stealth Buff", "Stealth", "t");
        Assert.Equal(new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, false), buff.Targeting);
        Assert.Equal((EffectSubject.Self, "Stealth", EffectDuration.Once, "before-attacking"),
            (buff.Effects.Single().Subject, buff.Effects.Single().TargetEntry, buff.Effects.Single().Duration, buff.Effects.Single().Conditions.Single(c => c.Kind == ConditionKind.AtPoint).Values.Single()));
        var mark = RuleTemplates.Wrapper(RulePattern.Mark, "Rending Mark", "Rending", "t");
        Assert.Equal(new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), mark.Targeting);
        Assert.Equal((EffectSubject.Opponent, "Rending", EffectDuration.Once), (mark.Effects.Single().Subject, mark.Effects.Single().TargetEntry, mark.Effects.Single().Duration));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuleTemplates.Wrapper(RulePattern.Debuff, "Defense Debuff", "x", "t"));
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/Tabletop.ArmyBooks.Tests --filter "FullyQualifiedName~RuleTemplatesTests"`
Expected: build fails with `CS0103: The name 'RuleWriter' does not exist in the current context` and `'RuleTemplates'`.

- [ ] **Step 4: Read rule texts, units and upgrades**

In `scripts/ArmyBooks/ArmyForgeBook.cs`, replace the `AfBook` record and add the records after `AfParam`:

```csharp
public sealed record AfBook(string Uid, string Name, string? VersionString, List<AfSpell> Spells,
    List<AfRuleText>? SpecialRules = null, List<AfUnit>? Units = null, List<AfPackage>? UpgradePackages = null);
```

```csharp
/// <summary>A book's rule glossary entry: the name and Army Forge's description (army-book rules spec → Generator).</summary>
public sealed record AfRuleText(string Name, string? Description);
public sealed record AfRuleRef(string Name);
public sealed record AfWeapon(List<AfRuleRef>? SpecialRules);
/// <summary>An upgrade gain or an item's content: an ArmyBookRule (Name), an ArmyBookWeapon (SpecialRules) or an ArmyBookItem (Content).</summary>
public sealed record AfGain(string? Name, string? Type, List<AfRuleRef>? SpecialRules, List<AfGain>? Content);
public sealed record AfUnit(List<AfRuleRef>? Rules, List<AfWeapon>? Weapons, List<AfGain>? Items);
public sealed record AfOption(List<AfGain>? Gains);
public sealed record AfSection(List<AfOption>? Options);
public sealed record AfPackage(List<AfSection>? Sections);
```

- [ ] **Step 5: The templates**

Create `scripts/ArmyBooks/RuleTemplates.cs`:

```csharp
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

public enum RulePattern { Aura, Buff, Mark, Debuff }

/// <summary>Army Forge's three template texts and the entry each becomes (army-book rules spec → The four patterns). A Debuff has no template.</summary>
public static class RuleTemplates
{
    const string AuraText = "This model and its unit get {0}.";
    const string BuffText = "Once per activation, before attacking, pick one friendly unit within 12\", which gets {0} once (next time the effect would apply).";
    const string MarkText = "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets {0} against once (next time the effect would apply).";
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, ["before-attacking"]);

    /// <summary>The pattern a name's last word spells exactly; null for any other name.</summary>
    public static RulePattern? PatternOf(string name)
    {
        var cut = name.LastIndexOf(' ');
        return cut > 0 && Enum.TryParse<RulePattern>(name[(cut + 1)..], out var p) && p.ToString() == name[(cut + 1)..] ? p : null;
    }

    /// <summary>X when the description is the pattern's template with X the name less its suffix; null otherwise and for a Debuff.</summary>
    public static string? Base(RulePattern p, string name, string description)
    {
        var x = name[..name.LastIndexOf(' ')];
        var template = p switch { RulePattern.Aura => AuraText, RulePattern.Buff => BuffText, RulePattern.Mark => MarkText, _ => null };
        return template is not null && description == string.Format(template, x) ? x : null;
    }

    /// <summary>
    /// Aura: a model entry, aggregation any, one subject-self grant of X ("this model and its unit"). Buff: a pick entry, a friendly unit
    /// within 12" (range alone), a subject-self once grant. Mark: a pick entry, an enemy unit within 18" in sight, a subject-opponent once grant,
    /// which acts for the unit's attacker as a marked-unit spell does.
    /// </summary>
    public static EntryDocument Wrapper(RulePattern p, string name, string baseName, string description) => p switch
    {
        RulePattern.Aura => new(name, CatalogKind.Model, false, Aggregation.Any, null, null, description, null,
            [Grant(baseName, EffectSubject.Self, EffectDuration.Interaction, [])]),
        RulePattern.Buff => new(name, CatalogKind.Model, false, Aggregation.None, null, null, description,
            new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, LineOfSight: false), [Grant(baseName, EffectSubject.Self, EffectDuration.Once, [BeforeAttacking])]),
        RulePattern.Mark => new(name, CatalogKind.Model, false, Aggregation.None, null, null, description,
            new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), [Grant(baseName, EffectSubject.Opponent, EffectDuration.Once, [BeforeAttacking])]),
        _ => throw new ArgumentOutOfRangeException(nameof(p), p, "a Debuff has no template"),
    };

    static EffectDocument Grant(string entry, EffectSubject subject, EffectDuration duration, IReadOnlyList<ConditionDocument> conditions) =>
        new(subject, 5, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, duration, conditions, []);
}
```

- [ ] **Step 6: Names in scope**

Create `scripts/ArmyBooks/RuleWriter.cs` (Task 6b adds `Build` and `Serialize`):

```csharp
namespace Tabletop.ArmyBooks;

/// <summary>The army-book rules part (army-book rules spec → Generator).</summary>
public static class RuleWriter
{
    /// <summary>Every rule name a unit or an upgrade carries, true when any carries it as a rule (a model's), false when only weapons do.</summary>
    public static IReadOnlyDictionary<string, bool> NamesInScope(AfBook book)
    {
        var names = new Dictionary<string, bool>(StringComparer.Ordinal);
        void Rule(string n) => names[n] = true;
        void WeaponRule(string n) => names.TryAdd(n, false);
        void Gain(AfGain g)
        {
            switch (g.Type)
            {
                case "ArmyBookRule" when g.Name is { } n: Rule(n); break;
                case "ArmyBookWeapon": foreach (var r in g.SpecialRules ?? []) WeaponRule(r.Name); break;
                case "ArmyBookItem": foreach (var c in g.Content ?? []) Gain(c); break;
            }
        }
        foreach (var u in book.Units ?? [])
        {
            foreach (var r in u.Rules ?? []) Rule(r.Name);
            foreach (var r in (u.Weapons ?? []).SelectMany(w => w.SpecialRules ?? [])) WeaponRule(r.Name);
            foreach (var i in u.Items ?? []) Gain(i);
        }
        foreach (var g in (book.UpgradePackages ?? []).SelectMany(p => p.Sections ?? []).SelectMany(s => s.Options ?? []).SelectMany(o => o.Gains ?? [])) Gain(g);
        return names;
    }
}
```

- [ ] **Step 7: Run the generator tests**

Run: `dotnet test tests/Tabletop.ArmyBooks.Tests`
Expected: PASS, the existing `SpellTranslatorTests` and `BookWriterTests` included (their fixture books carry no
`specialRules`, `units` or `upgradePackages`, which read as null).

- [ ] **Step 8: Commit**

```bash
git add scripts/ArmyBooks/ArmyForgeBook.cs scripts/ArmyBooks/RuleTemplates.cs scripts/ArmyBooks/RuleWriter.cs \
  tests/Tabletop.ArmyBooks.Tests/RuleTemplatesTests.cs tests/Tabletop.ArmyBooks.Tests/Fixtures/rules
git commit -m "Read the army books' rule texts, units and upgrades, and the Aura, Buff and Mark templates"
```
