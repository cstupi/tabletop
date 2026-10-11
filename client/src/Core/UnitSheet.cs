using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A rule as the sheet shows it: its label with the parameter, its catalog text, and the *not automated* tag (a loaded catalog without it).</summary>
public sealed record SheetRule(string Label, string? Text, bool NotAutomated);
public enum ChipKind { Activated, Condition, SpellTokens, Grant }
/// <summary>A status chip; Condition keeps the raw condition name, the key an icon is looked up by.</summary>
public sealed record SheetChip(ChipKind Kind, string Text, string? Condition = null);
public sealed record SheetWeapon(string Name, int Count, string Range, string Attacks, string Ap, SheetRule? ApRule, IReadOnlyList<SheetRule> Rules);
public sealed record SheetModel(Guid Id, string Label, int Left, int Max, bool Removed);
public sealed record UnitSheetModel(Guid UnitId, string Team, string Name, int Alive, int Size, string? Quality, string? Defense,
    IReadOnlyList<SheetChip> Chips, IReadOnlyList<SheetWeapon> Weapons, IReadOnlyList<SheetRule> Rules, IReadOnlyList<SheetModel> Models);

/// <summary>
/// The unit card (client UX spec → Unit card) from the army snapshot, the projection, the pinned catalog and the server's model
/// facts. Names no rule: a label is the snapshot's, a model's wounds are the ModelFacts value, and a rule with no catalog entry is
/// not automated.
/// </summary>
public static class UnitSheet
{
    /// <summary>Throws KeyNotFoundException for a unit the projection does not hold; the caller shows a sheet only for a unit it can see.</summary>
    public static UnitSheetModel Of(Projection p, Names n, Guid unitId, RuleTexts rules, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var unit = p.Units[unitId];
        var models = unit.Definition.Models.OrderBy(s => s.Ordinal).Select(slot => Model(p.Models[slot.Id], maxWounds)).ToList();
        var standing = unit.Definition.Models.Where(slot => !models.Single(m => m.Id == slot.Id).Removed).OrderBy(s => s.Ordinal).ToList();
        var first = unit.Definition.Models.OrderBy(s => s.Ordinal).FirstOrDefault();
        var team = first is not null && n.TeamOfModel(first.Id) is { } t ? n.Team(t) : "";
        return new UnitSheetModel(unitId, team, n.Unit(unitId), models.Count(m => !m.Removed), unit.Definition.Models.Count,
            Stat(unit.Definition, first, "Quality"), Stat(unit.Definition, first, "Defense"),
            Chips(p, n, unit), Weapons(standing, rules), Rules(unit.Definition, standing, rules), models);
    }

    static SheetModel Model(ModelInPlay m, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var max = maxWounds.GetValueOrDefault(m.Id, 1);
        return new SheetModel(m.Id, m.Slot.Name, Math.Max(0, max - m.Wounds), max, m.Location is OnShelf && m.Wounds >= max);
    }

    static string? Stat(Unit unit, ModelSlot? first, string name) =>
        unit.Stats.GetValueOrDefault(name) ?? first?.Stats.GetValueOrDefault(name);

    /// <summary>The printed army list's AP column label: the card moves this property's parameter into its own column and computes nothing from it.</summary>
    public const string ApLabel = "AP";

    static IReadOnlyList<SheetChip> Chips(Projection p, Names n, UnitInPlay unit)
    {
        var chips = new List<SheetChip>();
        if (unit.Actions.Count > 0) chips.Add(new(ChipKind.Activated, $"Activated: {string.Join(", ", unit.Actions.Order(StringComparer.Ordinal))}"));
        chips.AddRange(unit.Conditions.Order(StringComparer.Ordinal).Select(c => new SheetChip(ChipKind.Condition, RosterView.Words(c), c)));
        var tokens = p.Models.Values.Where(m => m.UnitId == unit.Id).Sum(m => m.SpellTokens);
        if (tokens > 0) chips.Add(new(ChipKind.SpellTokens, $"{tokens} spell token{(tokens == 1 ? "" : "s")}"));
        chips.AddRange(unit.Grants.Select(g => new SheetChip(ChipKind.Grant,
            $"{g.EntryName} from {n.Unit(g.CasterUnitId)} · {g.RemainingUses} use{(g.RemainingUses == 1 ? "" : "s")}")));
        return chips;
    }


    static IReadOnlyList<SheetWeapon> Weapons(IReadOnlyList<ModelSlot> standing, RuleTexts rules) =>
        standing.SelectMany(s => s.Weapons).GroupBy(w => w.Name, StringComparer.Ordinal).Select(g => WeaponRow(g.Key, g.Count(), g.First(), rules)).ToList();

    static SheetWeapon WeaponRow(string name, int count, Weapon w, RuleTexts rules)
    {
        var ap = w.Properties.Select((x, i) => (x, i)).FirstOrDefault(t => t.x.Name == ApLabel && t.x.Parameter is not null);
        var found = ap.x is not null;
        return new SheetWeapon(name, count, w.Stats.GetValueOrDefault("Range") ?? "-", w.Stats.GetValueOrDefault("Attacks") ?? "-",
            found ? ap.x!.Parameter!.Value.ToString() : "-", found ? Rule(ap.x!, rules) : null,
            w.Properties.Where((_, i) => !found || i != ap.i).Select(x => Rule(x, rules)).ToList());
    }

    static IReadOnlyList<SheetRule> Rules(Unit unit, IReadOnlyList<ModelSlot> standing, RuleTexts rules) =>
        unit.Properties.Concat(standing.SelectMany(s => s.Properties)).Select(x => Rule(x, rules)).DistinctBy(r => r.Label).ToList();

    static SheetRule Rule(PropertyInstance x, RuleTexts rules) =>
        new(x.Parameter is { } v ? $"{x.Name}({v})" : x.Name, rules.TextOf(x.Name), rules.NotAutomated(x.Name));
}
