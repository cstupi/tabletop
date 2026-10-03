using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class BookWriterTests
{
    static IReadOnlyList<AfBook> All() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books")).Order(StringComparer.Ordinal)
        .Select(f => ArmyForgeJson.ReadBook(File.ReadAllText(f))).ToList();

    [Fact]
    public void Reprints_merge_and_colliding_names_are_qualified_by_the_first_book_that_holds_each_variant()
    {
        var books = BookWriter.Build(All());
        var names = books.SelectMany(b => b.Spells).Select(s => s.Name).Distinct().ToList();
        Assert.Contains("Shield Breaker (Blood Brothers)", names);
        Assert.Contains("Shield Breaker (Blood Prime Brothers)", names);
        Assert.Contains("Psy-Injected Courage (Human Defense Force)", names);
        Assert.Contains("Psy-Injected Courage (Human Inquisition)", names);
        Assert.Contains("Rapid Putrefaction (Plague Disciples)", names);
        Assert.Contains("Rapid Putrefaction (Titan Lords Plague Disciples)", names);
        Assert.DoesNotContain("Shield Breaker", names);
        Assert.Contains("Battle Rune", names);                                             // trimmed
        var plague = books.Single(b => b.Uid == "fmuqw5lr5l6dq0mq").Spells.Single(s => s.Name.StartsWith("Rapid Putrefaction"));
        Assert.Equal("Rapid Putrefaction (Plague Disciples)", plague.Name);                 // the Wormhole printing equals the Plague Disciples one
        Assert.Empty(books.Single(b => b.Uid == "i4ZEAmqeFSfAcw3f").Spells);
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var a = BookWriter.Build(All()).Select(BookWriter.Serialize).ToList();
        var b = BookWriter.Build(All().Reverse().ToList()).Select(BookWriter.Serialize).ToList();
        Assert.Equal(a.Order(StringComparer.Ordinal), b.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_rule_no_document_holds_is_refused_and_so_is_an_invalid_composition()
    {
        var core = new PropertySetDocument("core", "grimdark-future", [], [], [], []);
        var problems = BookWriter.Check(BookWriter.Build(All()), core, new PropertySetDocument("part", "grimdark-future", [], [], [], []));
        Assert.Contains(problems, p => p.Contains("names no rule 'Bane'"));
    }
}
