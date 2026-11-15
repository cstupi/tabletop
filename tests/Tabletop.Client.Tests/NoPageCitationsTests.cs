using System.Text.RegularExpressions;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Owner ruling 2026-11-07: UI text carries no rulebook page citations.</summary>
public partial class NoPageCitationsTests
{
    [GeneratedRegex(@"\bp\.\d")]
    private static partial Regex Citation();

    [Theory]
    [InlineData("overlaps a base")]
    [InlineData("path crosses a model")]
    [InlineData("ends in impassable terrain")]
    [InlineData("within 1\" of Storm Troopers")]
    [InlineData("within 3\" of Storm Troopers")]
    public void A_contact_sentence_cites_no_page(string words) =>
        Assert.DoesNotMatch(Citation(), ContactCheck.Sentence(words, "APC 1"));

    [Fact]
    public void A_worked_roll_cites_no_page()
    {
        var block = new RollRecord(Guid.CreateVersion7(), "defense", 5, [new RollModifier("AP", -4)],
            [new Die(6, 2, true), new Die(1, 1, false)], null, new ThresholdBasis("Defense", 5, []));
        var lines = Assert.Single(DetailedLogTests.Block(new StepCompleted(Samples.Frame, "roll-to-block", null, [block], []))).Lines;
        Assert.Contains("a 6 always succeeds, a 1 always fails", lines);
        Assert.All(lines, line => Assert.DoesNotMatch(Citation(), line));
    }

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
    private static partial Regex Literal();

    /// <summary>Every string literal in the client's source, outside comments.</summary>
    [Fact]
    public void No_string_literal_in_the_client_cites_a_page()
    {
        var root = CallCases.RepoRoot();
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "client", "src"), "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*")) continue;
                if (Literal().Matches(line.Split(" // ")[0]).Any(m => Citation().IsMatch(m.Value)))
                    found.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
            }
        }
        Assert.Empty(found);
    }
}
