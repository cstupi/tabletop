using Tabletop.Content;
using System.Text.Json;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>Generated: the count of wrappers written. Groups: the spec's table (The gap), in its order. NoDescription: names neither a book nor the common rules describe.
/// DescribedByCommonRules: names no book describes that the common-rules file does.</summary>
public sealed record RuleReport(int Generated, IReadOnlyList<(string Group, int Count)> Groups, IReadOnlyList<string> NoDescription, IReadOnlyList<string> DescribedByCommonRules);

/// <summary>The army-book rules part (army-book rules spec → Generator): a wrapper per template name whose base the catalog holds, a text-only
/// entry per other name in scope that core and the hand-authored part leave undefined.</summary>
public static class RuleWriter
{
    static readonly JsonSerializerOptions Indented = new(PropertiesJson.Options) { WriteIndented = true, NewLine = "\n" };
    static readonly string[] GroupOrder =
    [
        "Aura, template text, base in catalog", "Aura, template text, base missing", "Aura, template text, base text-only", "Aura, effect written out",
        "Buff, template text, base in catalog", "Buff, template text, base missing", "Buff, template text, base text-only", "Buff, effect written out",
        "Mark, template text, base in catalog", "Mark, template text, base missing", "Mark, template text, base text-only", "Mark, effect written out",
        "Debuff, effect written out", "Standalone, own text",
    ];

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

    public static (PropertySetDocument? Part, RuleReport? Report, IReadOnlyList<string> Problems) Build(IReadOnlyList<AfBook> books, PropertySetDocument core, PropertySetDocument hand, string rulesId, IReadOnlyDictionary<string, string>? common = null)
    {
        var ordered = books.OrderBy(b => b.Name, StringComparer.Ordinal).ToList();
        var descriptions = new Dictionary<string, (string Text, string Book)>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var b in ordered)
            foreach (var r in b.SpecialRules ?? [])
            {
                var text = (r.Description ?? "").Trim();
                if (!descriptions.TryGetValue(r.Name, out var had)) descriptions[r.Name] = (text, b.Name);
                else if (had.Text != text) problems.Add($"'{r.Name}' has two descriptions: {had.Book} and {b.Name}");
            }
        if (problems.Count > 0) return (null, null, problems.Distinct().ToList());

        var defined = core.Entries.Concat(hand.Entries).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        // Under 3.6 a wrapper whose base is text-only (played false) is text-only too: its grant could never act (as BookWriter.DemoteUndefinedGrants for spells).
        var unplayed = rulesId == RuleSets.RulesIdOf("3.5") ? [] : core.Entries.Concat(hand.Entries).Where(e => e.Played == false).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var scope = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (name, isRule) in ordered.SelectMany(NamesInScope)) scope[name] = scope.GetValueOrDefault(name) || isRule;
        var entries = new List<EntryDocument>();
        var counts = GroupOrder.ToDictionary(g => g, _ => 0);
        var noDescription = new List<string>();
        var describedByCommon = new List<string>();
        foreach (var name in scope.Keys.Where(n => !defined.Contains(n)).Order(StringComparer.Ordinal))
        {
            var text = "";
            if (descriptions.TryGetValue(name, out var d)) text = d.Text;
            else if (common is not null && common.TryGetValue(name, out var c)) { text = c; describedByCommon.Add(name); }
            else noDescription.Add(name);
            var pattern = RuleTemplates.PatternOf(name);
            var x = pattern is { } p && p != RulePattern.Debuff ? RuleTemplates.Base(p, name, text) : null;
            if (x is not null && defined.Contains(x) && !unplayed.Contains(x))
            {
                entries.Add(RuleTemplates.Wrapper(pattern!.Value, name, x, text));
                counts[$"{pattern}, template text, base in catalog"]++;
                continue;
            }
            counts[pattern switch
            {
                null => "Standalone, own text",
                RulePattern.Debuff => "Debuff, effect written out",
                { } q when x is not null && unplayed.Contains(x) => $"{q}, template text, base text-only",
                { } q when x is not null => $"{q}, template text, base missing",
                { } q => $"{q}, effect written out",
            }]++;
            var weaponOnly = !scope[name];
            entries.Add(new EntryDocument(name, weaponOnly ? CatalogKind.Weapon : CatalogKind.Model, false, weaponOnly ? null : Aggregation.Any, null, null, text, null, [], Played: false));
        }
        var part = new PropertySetDocument("Grimdark Future army-book rules (generated)", rulesId, [], [], [], entries);
        var generated = GroupOrder.Where(g => g.EndsWith("base in catalog")).Sum(g => counts[g]);
        return (part, new RuleReport(generated, GroupOrder.Select(g => (g, counts[g])).ToList(), noDescription, describedByCommon), []);
    }

    public static string Serialize(PropertySetDocument part) =>
        JsonSerializer.Serialize(part with { Entries = part.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList() }, Indented) + "\n";
}
