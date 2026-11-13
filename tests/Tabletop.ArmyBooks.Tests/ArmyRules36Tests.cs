using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

/// <summary>The 3.6 hand part (server/game/Content/grimdark-future-3.6/army-rules.part.json) against the 3.6 dump's books: every name it defines is one a
/// 3.6 book describes or carries, and its text is that book's. Skips without the dump (git-ignored).</summary>
public class ArmyRules36Tests
{
    /// <summary>Entries still holding their 3.5.1 text and effects: 3.6.0 changed the text, and each is authored against it here.</summary>
    static readonly string[] AwaitingTheir36Text = [];

    /// <summary>The names whose printed text is the common-rules file's: no book and no rulebook page describes them (the other common rules are not 3.6.0 text for a hand entry).</summary>
    static readonly string[] DescribedByCommonRules = ["Banner", "Brace", "Parry"];

    static string? Up(string relative)
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return null;
    }

    static (List<AfBook> Books, PropertySetDocument Hand, ILookup<string, string> Common) Load()
    {
        var index = Up(Path.Combine(".superpowers", "armyforge", "3.6", "index.json"));
        Skip.If(index is null, ".superpowers/armyforge/3.6/index.json not found above the working directory (the dump is git-ignored)");
        var books = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(index)!, "books"), "*.json").Select(f => ArmyForgeJson.ReadBook(File.ReadAllText(f))).ToList();
        var repo = Path.GetDirectoryName(Up("Tabletop.sln"))!;
        var commonFile = Path.Combine(Path.GetDirectoryName(index)!, "common-rules.json");
        var common = File.Exists(commonFile) ? ArmyForgeJson.ReadCommonRules(File.ReadAllText(commonFile)) : [];
        return (books, PropertiesJson.Parse(File.ReadAllText(Path.Combine(repo, "server", "game", "Content", "grimdark-future-3.6", "army-rules.part.json"))), common.ToLookup(c => c.Key, c => c.Value, StringComparer.Ordinal));
    }

    [SkippableFact]
    public void Every_3_6_hand_entry_text_matches_a_3_6_book()
    {
        var (books, hand, common) = Load();
        var texts = books.SelectMany(b => b.SpecialRules ?? []).Select(r => (r.Name, Text: (r.Description ?? "").Trim())).Concat(common.Where(c => DescribedByCommonRules.Contains(c.Key)).SelectMany(c => c.Select(t => (Name: c.Key, Text: t))))
            .ToLookup(r => r.Name, r => r.Text, StringComparer.Ordinal);       // a book's text, or the common rules' for a name no book describes
        var differing = hand.Entries.Where(e => texts.Contains(e.Name) && !texts[e.Name].Contains(e.RuleText)).Select(e => e.Name);
        Assert.Equal(AwaitingTheir36Text.Order(StringComparer.Ordinal), differing.Order(StringComparer.Ordinal));
    }

    [SkippableFact]
    public void No_hand_entry_names_a_rule_no_3_6_book_uses()
    {
        var (books, hand, _) = Load();
        var used = books.SelectMany(b => b.SpecialRules ?? []).Select(r => r.Name).Concat(books.SelectMany(b => RuleWriter.NamesInScope(b).Keys)).ToHashSet(StringComparer.Ordinal);
        Assert.Equal([], hand.Entries.Select(e => e.Name).Where(n => !used.Contains(n)));
    }
}
