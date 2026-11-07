using System.Text.RegularExpressions;
using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;

namespace Tabletop.GameServer.Tests;

/// <summary>Owner ruling 2026-11-07: UI text carries no rulebook page citations. Comments keep theirs.</summary>
public partial class NoPageCitationsTests
{
    static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [GeneratedRegex(@"\bp\.\d")]
    private static partial Regex Citation();

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
    private static partial Regex Literal();

    [Fact]
    public void Notes_and_details_cite_no_page()
    {
        string?[] texts =
        [
            GrimdarkFutureRules.AircraftNote, GrimdarkFutureRules.ShakenNote, GrimdarkFutureRules.ImmobileNote, GrimdarkFutureRules.NoChargeNote,
            GfEdition.V351.ArtilleryNote, GfEdition.V360.CumbersomeNote, GrimdarkFutureRules.MissionLengthDetail(4),
        ];
        Assert.All(texts, t => Assert.DoesNotMatch(Citation(), t ?? ""));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void The_option_catalog_cites_no_page(string rulesId)
    {
        var catalog = GameRulesRegistry.Resolve(rulesId).OptionCatalog;
        Assert.All(catalog.Options, o => Assert.DoesNotMatch(Citation(), o.Label + string.Concat(o.Values.Select(v => v.Label))));
    }

    /// <summary>Every string literal in the servers and the libs, outside comments: refusal and breach details, Ask notes and prompts, labels.</summary>
    [Fact]
    public void No_string_literal_in_the_servers_or_libs_cites_a_page()
    {
        var found = new List<string>();
        foreach (var dir in new[] { "server/game", "server/management", "libs" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, dir), "*.cs", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimStart();
                    if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*")) continue;
                    var code = line.Split(" // ")[0];
                    if (Literal().Matches(code).Any(m => Citation().IsMatch(m.Value)))
                        found.Add($"{Path.GetRelativePath(RepoRoot, file)}:{i + 1}");
                }
            }
        Assert.Empty(found);
    }
}
