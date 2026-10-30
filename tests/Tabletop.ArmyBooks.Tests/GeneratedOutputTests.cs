using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

/// <summary><c>generate --edition 3.5</c> over the 3.5 dump reproduces the committed 3.5.1 content byte for byte.</summary>
public class GeneratedOutputTests
{
    static string? Up(string relative)
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative)) || Directory.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return null;
    }

    [SkippableFact]
    public void The_3_5_pipeline_reproduces_the_committed_3_5_1_content()
    {
        var dump = Up(Path.Combine(".superpowers", "armyforge", "3.5", "index.json"));
        Skip.If(dump is null, ".superpowers/armyforge/3.5/index.json not found above the working directory (the dump is git-ignored)");
        var sln = Up("Tabletop.sln");
        Assert.NotNull(sln);
        var repo = Path.GetDirectoryName(sln)!;
        var content = Path.Combine(repo, "server", "game", "Content", "grimdark-future-3.5.1");
        var core = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "core.properties.json")));
        var hand = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "army-rules.part.json")));

        var g = Generation.Run(Path.GetDirectoryName(dump)!, core, hand, RuleSets.RulesIdOf("3.5"));

        Assert.Empty(g.Problems);
        Assert.Equal(48, g.Books.Count);
        foreach (var book in g.Books)
            Assert.True(File.ReadAllBytes(Path.Combine(content, "army-books", book.Uid + ".book.json")).AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(BookWriter.Serialize(book))), $"{book.Uid}.book.json differs");
        Assert.Equal(48, Directory.GetFiles(Path.Combine(content, "army-books"), "*.book.json").Length);
        Same(Path.Combine(content, "army-book-rules.part.json"), RuleWriter.Serialize(g.Rules!));
        Same(Path.Combine(repo, "server", "management", "Content", "Armies", ReserveUnits.FileName("3.5")), g.Units);
    }

    static void Same(string committed, string generated) =>
        Assert.True(File.ReadAllBytes(committed).AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(generated)), $"{Path.GetFileName(committed)} differs");
}
