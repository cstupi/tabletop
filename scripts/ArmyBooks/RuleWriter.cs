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
