using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;

namespace Tabletop.GameServer.Tests;

public class ContentFoldersTests
{
    const string Prefix360 = "Tabletop.GameServer.Content.grimdark-future-3.6.";

    [Fact]
    public void The_3_5_1_set_hash_is_pinned() =>
        Assert.Equal("sha256:c43d9fb4898fbe174847a458a77b167e6bd561b1f8cff1634654d899eefa457b",
            PropertiesJson.Hash(PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == RuleSets.Gf351)));

    [Fact]
    public void Two_editions_are_embedded_each_in_its_folder()
    {
        var docs = PropertiesSeeder.EmbeddedDocuments();
        Assert.Equal([RuleSets.Gf351, RuleSets.Gf360], docs.Select(d => d.GameRules).Order(StringComparer.Ordinal));
        Assert.Equal("Grimdark Future v3.5.1", docs.Single(d => d.GameRules == RuleSets.Gf351).Name);
        Assert.Equal("Grimdark Future v3.6.0", docs.Single(d => d.GameRules == RuleSets.Gf360).Name);
        Assert.Equal(["grimdark-future-3.5.1", "grimdark-future-3.6"], new[] { GfEdition.V351.ContentFolder, GfEdition.V360.ContentFolder });
        Assert.Equal(RuleSets.Gf351, PropertiesSeeder.EmbeddedPart("grimdark-future-3.5.1", PropertiesSeeder.GeneratedPartName)!.GameRules);
        Assert.Equal(RuleSets.Gf360, PropertiesSeeder.EmbeddedPart("grimdark-future-3.6", PropertiesSeeder.GeneratedPartName)!.GameRules);
        Assert.Null(PropertiesSeeder.EmbeddedPart("grimdark-future-3.6", "no-such.part.json"));
    }

    [Fact]
    public void Resources_are_named_by_folder_and_file()
    {
        var names = typeof(PropertiesSeeder).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("Tabletop.GameServer.Content.")).ToList();
        foreach (var folder in new[] { "grimdark-future-3.5.1", "grimdark-future-3.6" })
        {
            foreach (var file in new[] { "core.properties.json", "army-rules.part.json", "relic-wargear.part.json", "army-book-rules.part.json" })
                Assert.Contains($"Tabletop.GameServer.Content.{folder}.{file}", names);
            Assert.Equal(folder == "grimdark-future-3.6" ? 49 : 48, names.Count(n => n.StartsWith($"Tabletop.GameServer.Content.{folder}.army-books.") && n.EndsWith(".book.json")));
        }
    }

    [Fact]
    public void Every_3_6_file_names_the_3_6_id()
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        string Read(string n) { using var s = asm.GetManifestResourceStream(n)!; using var r = new StreamReader(s); return r.ReadToEnd(); }
        var names = asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix360)).ToList();
        Assert.Equal(53, names.Count);
        Assert.All(names, n => Assert.Equal(RuleSets.Gf360,
            n.EndsWith(".book.json") ? PropertiesJson.ParseBook(Read(n)).GameRules : PropertiesJson.Parse(Read(n)).GameRules));
    }

    [Fact]
    public void The_3_6_books_are_3_6_0()
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        var books = asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix360) && n.EndsWith(".book.json"))
            .Select(n => { using var st = asm.GetManifestResourceStream(n)!; using var r = new StreamReader(st); return PropertiesJson.ParseBook(r.ReadToEnd()); }).ToList();
        Assert.Equal(49, books.Count);
        Assert.All(books, b => Assert.StartsWith("3.6", b.Version));
    }
}
