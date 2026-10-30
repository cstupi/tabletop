using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class BookWriterTests
{
    static IReadOnlyList<AfBook> All() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books")).Order(StringComparer.Ordinal)
        .Select(f => ArmyForgeJson.ReadBook(File.ReadAllText(f))).ToList();

    [Fact]
    public void The_books_name_the_rules_id_they_were_built_for()
    {
        Assert.All(BookWriter.Build(All(), "grimdark-future-3.6"), b => Assert.Equal("grimdark-future-3.6", b.GameRules));
    }

    [Fact]
    public void Reprints_merge_and_colliding_names_are_qualified_by_the_first_book_that_holds_each_variant()
    {
        var books = BookWriter.Build(All(), "grimdark-future");
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
        var a = BookWriter.Build(All(), "grimdark-future").Select(BookWriter.Serialize).ToList();
        var b = BookWriter.Build(All().Reverse().ToList(), "grimdark-future").Select(BookWriter.Serialize).ToList();
        Assert.Equal(a.Order(StringComparer.Ordinal), b.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_rule_no_document_holds_is_refused_and_so_is_an_invalid_composition()
    {
        var core = new PropertySetDocument("core", "grimdark-future", [], [], [], []);
        var problems = BookWriter.Check(BookWriter.Build(All(), "grimdark-future"), core, new PropertySetDocument("part", "grimdark-future", [], [], [], []));
        Assert.Contains(problems, p => p.Contains("names no rule 'Bane'"));
    }

    [Fact]
    public void Serialize_uses_line_feeds_only()
    {
        foreach (var b in BookWriter.Build(All(), "grimdark-future").Where(b => b.Spells.Count > 0).Take(3))
        {
            var text = BookWriter.Serialize(b);
            Assert.DoesNotContain('\r', text);
            Assert.EndsWith("\n", text);
        }
    }

    [Fact]
    public void Sibling_effects_have_distinct_ascending_ordinals_and_multi_value_conditions_are_sorted()
    {
        static IEnumerable<IReadOnlyList<EffectDocument>> Groups(IReadOnlyList<EffectDocument> rows) =>
            new[] { rows }.Concat(rows.SelectMany(r => Groups(r.Children)));
        static IEnumerable<EffectDocument> Flat(IReadOnlyList<EffectDocument> rows) => rows.SelectMany(r => new[] { r }.Concat(Flat(r.Children)));
        var spells = BookWriter.Build(All(), "grimdark-future").SelectMany(b => b.Spells).ToList();
        foreach (var s in spells)
        {
            foreach (var g in Groups(s.Effects))
                Assert.Equal(g.Select(r => r.Ordinal).Distinct().Order().ToList(), g.Select(r => r.Ordinal).ToList());
            foreach (var c in Flat(s.Effects).SelectMany(r => r.Conditions))
                Assert.Equal(c.Values.Order(StringComparer.Ordinal).ToList(), c.Values.ToList());
        }
    }
}
