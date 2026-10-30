using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

public sealed record Generated(IReadOnlyList<BookDocument> Books, PropertySetDocument? Rules, RuleReport? Report, string Units, List<string> Problems, IReadOnlyList<string> Notes);

public static class Generation
{
    /// <summary>The generate pipeline over one edition's dump folder (index.json and books/): everything it would write, in memory.</summary>
    public static Generated Run(string dumpDir, PropertySetDocument core, PropertySetDocument hand, string rulesId)
    {
        var index = ArmyForgeJson.ReadIndex(File.ReadAllText(Path.Combine(dumpDir, "index.json")));
        var raw = index.Select(e => (Uid: e.Uid, Name: e.Name, Json: File.ReadAllText(Path.Combine(dumpDir, "books", e.Uid + ".json")))).ToList();
        var books = index.Zip(raw).Select(p => ArmyForgeJson.ReadBook(p.Second.Json) with { Name = p.First.Name, VersionString = p.First.VersionString }).ToList();
        var problems = books.SelectMany(b => b.Spells.SelectMany(s =>
        {
            try { return TextCheck.Problems(s, SpellTranslator.Translate(b, s)).Select(p => $"{b.Name}: {p}"); }
            catch (GenerationException ex) { return ex.Problems; }
        })).ToList();
        IReadOnlyList<BookDocument> built = [];
        var notes = new List<string>();
        foreach (var b in books) foreach (var sp in b.Spells)
            if (SpellTranslator.RangeCorrection(sp) is { } rc) notes.Add($"range from text: {b.Name} {sp.Name.Trim()} {rc.Generation} → {rc.Printed}");
        var (rules, report, ruleProblems) = RuleWriter.Build(books, core, hand, rulesId);
        problems.AddRange(ruleProblems);
        var (reserveUnits, reserveProblems) = ReserveUnits.Build(raw);
        problems.AddRange(reserveProblems);
        if (problems.Count == 0)
        {
            built = BookWriter.Build(books, rulesId);
            if (rulesId != RuleSets.RulesIdOf("3.5"))
            {
                var (live, demoted) = BookWriter.DemoteUndefinedGrants(built, core, hand);
                built = live;
                if (demoted.Count > 0) notes.Add($"text-only spells (undefined grant: <rule>): {demoted.Count}\n    {string.Join("\n    ", demoted)}");
            }
            problems.AddRange(BookWriter.Check(built, core, hand, rules));
        }
        var textOnly = books.SelectMany(b => b.Spells.Where(SpellTranslator.IsTextOnly).Select(sp => $"{b.Name}: {sp.Name.Trim()}")).ToList();
        if (textOnly.Count > 0) notes.Add($"text-only spells: {string.Join("; ", textOnly)}");
        return new Generated(built, rules, report, reserveUnits, problems, notes);
    }
}
