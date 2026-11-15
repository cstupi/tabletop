using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A rule as the sheet shows it: its label with the parameter, its catalog text, and the *not automated* tag (a loaded catalog without it).</summary>
public sealed record SheetRule(string Label, string? Text, bool NotAutomated);
public enum ChipKind { Activated, Condition, SpellTokens, Grant, Reserve }
/// <summary>A status chip; Condition keeps the raw condition name, the key an icon is looked up by.</summary>
public sealed record SheetChip(ChipKind Kind, string Text, string? Condition = null);
/// <summary>Carriers: the standing models that carry the weapon, in slot order (the table lights them while its row is under the pointer).</summary>
public sealed record SheetWeapon(string Name, int Count, string Range, string Attacks, string Ap, SheetRule? ApRule, IReadOnlyList<SheetRule> Rules, IReadOnlyList<Guid>? Carriers = null);
/// <summary>A model: Label is the slot's name without Army Forge's trailing number; Kind is equal for models alike in name, weapons, rules,
/// stats and max wounds; Loadout lists its weapons (×n for one carried twice), its rule labels but Hero, and each slot stat that differs from
/// the card's badge; Hero marks a slot carrying the army list's Hero entry.</summary>
public sealed record SheetModel(Guid Id, string Label, int Left, int Max, bool Removed, string Kind, IReadOnlyList<string> Loadout, bool Hero);
public sealed record UnitSheetModel(Guid UnitId, string Team, string Name, int Alive, int Size, string? Quality, string? Defense,
    IReadOnlyList<SheetChip> Chips, IReadOnlyList<SheetWeapon> Weapons, IReadOnlyList<SheetRule> Rules, IReadOnlyList<SheetModel> Models, int? Points = null);

/// <summary>
/// The unit card (client UX spec → Unit card) from the army snapshot, the projection, the pinned catalog and the server's model
/// facts. Names no rule: a label is the snapshot's, a model's wounds are the ModelFacts value, and a rule with no catalog entry is
/// not automated.
/// </summary>
public static partial class UnitSheet
{
    /// <summary>Throws KeyNotFoundException for a unit the projection does not hold; the caller shows a sheet only for a unit it can see.</summary>
    public static UnitSheetModel Of(Projection p, Names n, Guid unitId, RuleTexts rules, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var unit = p.Units[unitId];
        var first = unit.Definition.Models.OrderBy(s => s.Ordinal).FirstOrDefault();
        var models = unit.Definition.Models.OrderBy(s => s.Ordinal).Select(slot => Model(p.Models[slot.Id], unit.Definition, first, maxWounds)).ToList();
        var standing = unit.Definition.Models.Where(slot => !models.Single(m => m.Id == slot.Id).Removed).OrderBy(s => s.Ordinal).ToList();
        var team = first is not null && n.TeamOfModel(first.Id) is { } t ? n.Team(t) : "";
        return new UnitSheetModel(unitId, team, n.Unit(unitId), models.Count(m => !m.Removed), unit.Definition.Models.Count,
            Stat(unit.Definition, first, "Quality"), Stat(unit.Definition, first, "Defense"),
            Chips(p, n, unit), Weapons(standing, rules), Rules(unit.Definition, standing, rules), models, unit.Definition.Points);
    }

    static SheetModel Model(ModelInPlay m, Unit unit, ModelSlot? first, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var max = maxWounds.GetValueOrDefault(m.Id, 1);
        var slot = m.Slot;
        var label = TrailingNumber().Replace(slot.Name, "");
        var loadout = slot.Weapons.GroupBy(w => w.Name, StringComparer.Ordinal).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key)
            .Concat(slot.Properties.Where(x => x.Name != HeroLabel).Select(Label))
            .Concat(slot.Stats.Where(kv => Stat(unit, first, kv.Key) != kv.Value).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value}"))
            .ToList();
        var kind = string.Join("\u001f", new[] { label, max.ToString() }
            .Concat(slot.Weapons.Select(w => $"{w.Name}|{Pairs(w.Stats)}|{string.Join(",", w.Properties.Select(Label))}").Order(StringComparer.Ordinal))
            .Append(string.Join(",", slot.Properties.Select(Label).Order(StringComparer.Ordinal)))
            .Append(Pairs(slot.Stats)));
        return new SheetModel(m.Id, label, Math.Max(0, max - m.Wounds), max, m.Location is OnShelf && m.Wounds >= max, kind, loadout,
            slot.Properties.Any(x => x.Name == HeroLabel));
    }

    static string Pairs(IReadOnlyDictionary<string, string> stats) =>
        string.Join(",", stats.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    static string Label(PropertyInstance x) => x.Parameter is { } v ? $"{x.Name}({v})" : x.Name;

    /// <summary>Army Forge numbers a unit's models ("Specialists 3", "Orc Leader 1"), the simulator "Red 1.2"; the card groups them under the name alone.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\s+\d+(\.\d+)*$")]
    private static partial System.Text.RegularExpressions.Regex TrailingNumber();
    [System.Text.RegularExpressions.GeneratedRegex(@"\s+\([^)]*\)$")]
    private static partial System.Text.RegularExpressions.Regex UpgradeSuffix();

    /// <summary>The army list's Hero entry: the card marks the model's group with it and computes nothing from it.</summary>
    public const string HeroLabel = "Hero";

    static string? Stat(Unit unit, ModelSlot? first, string name) =>
        unit.Stats.GetValueOrDefault(name) ?? first?.Stats.GetValueOrDefault(name);

    /// <summary>The printed army list's AP column label: the card moves this property's parameter into its own column and computes nothing from it.</summary>
    public const string ApLabel = "AP";

    static IReadOnlyList<SheetChip> Chips(Projection p, Names n, UnitInPlay unit)
    {
        var chips = new List<SheetChip>();
        if (RosterView.ReserveOf(p, unit) is { } reserve) chips.Add(new(ChipKind.Reserve, reserve));
        if (unit.Actions.Count > 0) chips.Add(new(ChipKind.Activated, $"Activated: {string.Join(", ", unit.Actions.Order(StringComparer.Ordinal))}"));
        chips.AddRange(Bookkeeping.OnCard(unit.Conditions).Order(StringComparer.Ordinal).Select(c => new SheetChip(ChipKind.Condition, RosterView.Words(c), c)));
        var tokens = p.Models.Values.Where(m => m.UnitId == unit.Id).Sum(m => m.SpellTokens);
        if (tokens > 0) chips.Add(new(ChipKind.SpellTokens, $"{tokens} spell token{(tokens == 1 ? "" : "s")}"));
        chips.AddRange(unit.Grants.Select(g => new SheetChip(ChipKind.Grant,
            $"{g.EntryName} from {n.Unit(g.CasterUnitId)} · {g.RemainingUses} use{(g.RemainingUses == 1 ? "" : "s")}")));
        return chips;
    }


    static IReadOnlyList<SheetWeapon> Weapons(IReadOnlyList<ModelSlot> standing, RuleTexts rules) =>
        standing.SelectMany(s => s.Weapons).GroupBy(w => w.Name, StringComparer.Ordinal).Select(g => WeaponRow(g.Key, g.Count(), g.First(), rules) with
        {
            Carriers = standing.Where(s => s.Weapons.Any(w => w.Name == g.Key)).Select(s => s.Id).ToList(),
        }).ToList();

    /// <summary>The imported entry a slot came from: its name without the upgrade it is named for ("Boss Mob (Junk Banner) 5") and the import's number.</summary>
    static string Entry(string slotName) => UpgradeSuffix().Replace(TrailingNumber().Replace(slotName, ""), "");

    /// <summary>What sets a model apart among the models it was imported with (a joined Hero is its own entry): its weapons, then its rules,
    /// that not every one of them carries (a banner, a special weapon). The web app's army page marks a model the same way (slotMarks).</summary>
    public static IReadOnlyList<string> Marks(Unit unit, ModelSlot slot)
    {
        var peers = unit.Models.Where(m => Entry(m.Name) == Entry(slot.Name)).ToList();
        if (peers.Count < 2) return [];
        var weapons = slot.Weapons.Select(w => w.Name).Distinct().Where(n => !peers.All(m => m.Weapons.Any(w => w.Name == n)));
        var rules = slot.Properties.Select(Label).Distinct().Where(l => !peers.All(m => m.Properties.Any(x => Label(x) == l)));
        return [.. weapons, .. rules];
    }

    /// <summary>The hover line of one model: its marks, else its own name when it is not one of the unit's most common entry (a joined Hero),
    /// else null (one of the many has nothing to say).</summary>
    public static string? ModelLine(Unit unit, ModelSlot slot)
    {
        if (Marks(unit, slot) is { Count: > 0 } marks) return string.Join(", ", marks);
        var common = unit.Models.GroupBy(m => Entry(m.Name)).OrderByDescending(g => g.Count()).ThenBy(g => g.Min(m => m.Ordinal)).First().Key;
        return Entry(slot.Name) == common ? null : Entry(slot.Name);
    }

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
        new(Label(x), rules.TextOf(x.Name), rules.NotAutomated(x.Name));
}
