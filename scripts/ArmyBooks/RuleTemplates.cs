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
